using ERP.NSQuell.Models.ViewModels.Almacen;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

// ALMACEN_OF_HISTORICO_V4_2
// NSQ_ALMACEN_OF_HISTORICO_FISICO_V1_7
// NSQ_ALMACEN_OF_HISTORICO_LISTA_V1_8_2
public sealed partial class AlmacenOFController
{
    [HttpGet]
    public async Task<IActionResult> Historico(
        string? q,
        DateTime? desde,
        DateTime? hasta,
        CancellationToken cancellationToken = default)
    {
        var resultado = await Index(
            q: q,
            estatus: null,
            area: null,
            desde: desde,
            hasta: hasta,
            pagina: 1,
            cancellationToken: cancellationToken);

        if (resultado is ViewResult vista
            && vista.Model is AlmacenOFIndexVm modelo)
        {
            /*
             * Historico representa surtimiento FISICO de Almacen.
             * Index conserva la cantidad aceptada por Produccion.
             */
            foreach (var orden in modelo.Ordenes)
            {
                foreach (var item in orden.MaterialesEntrega)
                {
                    item.Entregado =
                        Math.Max(0m, item.EntregadoFisico);
                }

                foreach (var item in orden.EmbalajesEntrega)
                {
                    item.Entregado =
                        Math.Max(0m, item.EntregadoFisico);
                }

                orden.MpEntregada =
                    orden.MaterialesEntrega.Sum(x => x.Entregado);

                orden.EmbalajeEntregado =
                    orden.EmbalajesEntrega.Sum(x => x.Entregado);
            }

            await CargarUltimaActualizacionHistoricoAsync(
                modelo.Ordenes,
                cancellationToken);

            modelo.Ordenes =
                modelo.Ordenes
                    .OrderByDescending(
                        x => x.UltimaActualizacionAlmacen
                             ?? x.FechaSolicitud)
                    .ThenByDescending(
                        x => x.SolicitudProduccionID)
                    .ToList();

            return View("Historico", modelo);
        }

        return resultado;
    }

    private async Task CargarUltimaActualizacionHistoricoAsync(
        List<AlmacenOFItemVm> ordenes,
        CancellationToken cancellationToken)
    {
        if (ordenes.Count == 0)
            return;

        await using var connection =
            await AbrirConexionAsync(cancellationToken);

        var parametros =
            ordenes
                .Select(
                    (x, index) =>
                        new
                        {
                            x.SolicitudProduccionID,
                            Nombre = $"@HistUpd{index}"
                        })
                .ToList();

        var inSql =
            string.Join(
                ",",
                parametros.Select(x => x.Nombre));

        var sql = $@"
SELECT
    s.SolicitudProduccionID,
    UltimaActualizacion =
    (
        SELECT MAX(actividad.FechaMovimiento)
        FROM
        (
            SELECT MAX(mp.FechaMovimiento) AS FechaMovimiento
            FROM dbo.AlmacenMP_Movimientos mp
            WHERE mp.Activo=1
              AND
              (
                  mp.SolicitudProduccionID=s.SolicitudProduccionID
                  OR
                  (
                      mp.SolicitudProduccionID IS NULL
                      AND
                      (
                          (
                              NULLIF(LTRIM(RTRIM(s.FolioSolicitud)),N'') IS NOT NULL
                              AND LTRIM(RTRIM(ISNULL(mp.NumeroOF,N'')))=
                                  LTRIM(RTRIM(s.FolioSolicitud))
                          )
                          OR
                          (
                              NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)),N'') IS NOT NULL
                              AND LTRIM(RTRIM(ISNULL(mp.NumeroOF,N'')))=
                                  LTRIM(RTRIM(s.NumeroOFRecibida))
                          )
                      )
                  )
              )

            UNION ALL

            SELECT MAX(em.FechaMovimiento)
            FROM dbo.AlmacenEmbalajes_Movimientos em
            WHERE em.Activo=1
              AND
              (
                  em.SolicitudProduccionID=s.SolicitudProduccionID
                  OR
                  (
                      em.SolicitudProduccionID IS NULL
                      AND
                      (
                          (
                              NULLIF(LTRIM(RTRIM(s.FolioSolicitud)),N'') IS NOT NULL
                              AND LTRIM(RTRIM(ISNULL(em.NumeroOF,N'')))=
                                  LTRIM(RTRIM(s.FolioSolicitud))
                          )
                          OR
                          (
                              NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)),N'') IS NOT NULL
                              AND LTRIM(RTRIM(ISNULL(em.NumeroOF,N'')))=
                                  LTRIM(RTRIM(s.NumeroOFRecibida))
                          )
                      )
                  )
              )

            UNION ALL

            SELECT MAX(pt.FechaMovimiento)
            FROM dbo.AlmacenPT_Movimientos pt
            WHERE pt.Activo=1
              AND
              (
                  (
                      NULLIF(LTRIM(RTRIM(s.FolioSolicitud)),N'') IS NOT NULL
                      AND LTRIM(RTRIM(ISNULL(pt.NumeroOF,N'')))=
                          LTRIM(RTRIM(s.FolioSolicitud))
                  )
                  OR
                  (
                      NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)),N'') IS NOT NULL
                      AND LTRIM(RTRIM(ISNULL(pt.NumeroOF,N'')))=
                          LTRIM(RTRIM(s.NumeroOFRecibida))
                  )
              )
        ) actividad
    )
FROM dbo.SolicitudesProduccion s
WHERE s.SolicitudProduccionID IN ({inSql});";

        await using var command =
            new SqlCommand(sql, connection);

        foreach (var parametro in parametros)
        {
            command.Parameters.Add(
                parametro.Nombre,
                SqlDbType.Int).Value =
                parametro.SolicitudProduccionID;
        }

        var fechas =
            new Dictionary<int, DateTime?>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var solicitudId =
                Convert.ToInt32(
                    reader["SolicitudProduccionID"]);

            DateTime? fecha =
                reader["UltimaActualizacion"] == DBNull.Value
                    ? null
                    : Convert.ToDateTime(
                        reader["UltimaActualizacion"]);

            fechas[solicitudId] = fecha;
        }

        foreach (var orden in ordenes)
        {
            if (fechas.TryGetValue(
                    orden.SolicitudProduccionID,
                    out var fecha))
            {
                orden.UltimaActualizacionAlmacen =
                    fecha ?? orden.FechaSolicitud;
            }
            else
            {
                orden.UltimaActualizacionAlmacen =
                    orden.FechaSolicitud;
            }
        }
    }
}