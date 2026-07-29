// RELEASE_HISTORICO_CONTROLLER_V1_0_1
using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

public partial class PlaneacionReleaseController
{
    [HttpGet]
    public async Task<IActionResult> Historico(
        int? clienteId = null)
    {
        var lista =
            new List<PlaneacionReleaseIndexVm>();

        const string sql = @"
SELECT
    r.ReleaseID,
    r.FolioRelease,
    r.FolioCliente,
    r.ClienteID,
    ISNULL(c.Nombre, r.ClienteNombre) AS ClienteNombre,
    r.FechaRecepcion,
    r.VersionRelease,
    r.ArchivoOrigenNombre,
    r.PlantillaImportacion,
    ISNULL(r.ImportadoDesdeArchivo, 0) AS ImportadoDesdeArchivo,
    r.EstatusID,
    r.FechaCreacion,
    ISNULL(renglones.TotalRenglones, 0) AS TotalRenglones,
    ISNULL(entregas.TotalEntregas, 0) AS TotalEntregas,
    ISNULL(entregas.TotalPiezasRequeridas, 0) AS TotalPiezasRequeridas,
    ISNULL(entregas.TotalPiezasAProducir, 0) AS TotalPiezasAProducir,
    entregas.UltimaFechaRequerida
FROM dbo.Planeacion_Releases r
LEFT JOIN dbo.ERP_Clientes c
    ON c.ClienteID = r.ClienteID
OUTER APPLY
(
    SELECT COUNT(1) AS TotalRenglones
    FROM dbo.Planeacion_ReleaseRenglones rr
    WHERE rr.ReleaseID = r.ReleaseID
      AND rr.Activo = 1
) renglones
OUTER APPLY
(
    SELECT
        COUNT(1) AS TotalEntregas,
        ISNULL(SUM(d.CantidadRequerida), 0) AS TotalPiezasRequeridas,
        ISNULL(SUM(ISNULL(d.PiezasAProducir, 0)), 0) AS TotalPiezasAProducir,
        MAX(d.FechaRequerida) AS UltimaFechaRequerida
    FROM dbo.Planeacion_ReleaseDetalle d
    WHERE d.ReleaseID = r.ReleaseID
      AND d.Activo = 1
) entregas
WHERE r.Activo = 1
  AND (@ClienteID IS NULL OR r.ClienteID = @ClienteID)
  AND entregas.UltimaFechaRequerida IS NOT NULL
  AND entregas.UltimaFechaRequerida < CONVERT(date, GETDATE())
ORDER BY
    ISNULL(c.Nombre, r.ClienteNombre),
    entregas.UltimaFechaRequerida DESC,
    r.FechaCreacion DESC;";

        await using var cn =
            new SqlConnection(ConnectionString);

        await cn.OpenAsync();

        await using var cmd =
            new SqlCommand(sql, cn);

        cmd.Parameters.Add(
            "@ClienteID",
            SqlDbType.Int).Value =
            (object?)clienteId ??
            DBNull.Value;

        await using var rd =
            await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
        {
            var estatusId =
                Convert.ToInt32(rd["EstatusID"]);

            lista.Add(new PlaneacionReleaseIndexVm
            {
                ReleaseID =
                    Convert.ToInt32(rd["ReleaseID"]),

                FolioRelease =
                    rd["FolioRelease"] as string,

                FolioCliente =
                    rd["FolioCliente"] as string,

                ClienteID =
                    rd["ClienteID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(
                            rd["ClienteID"]),

                ClienteNombre =
                    rd["ClienteNombre"] as string,

                FechaRecepcion =
                    Convert.ToDateTime(
                        rd["FechaRecepcion"]),

                VersionRelease =
                    rd["VersionRelease"] as string,

                ArchivoOrigenNombre =
                    rd["ArchivoOrigenNombre"] as string,

                PlantillaImportacion =
                    rd["PlantillaImportacion"] as string,

                ImportadoDesdeArchivo =
                    rd["ImportadoDesdeArchivo"] !=
                        DBNull.Value &&
                    Convert.ToBoolean(
                        rd["ImportadoDesdeArchivo"]),

                EstatusID = estatusId,

                EstatusNombre =
                    PlaneacionReleaseEstatus.Nombre(
                        estatusId),

                FechaCreacion =
                    Convert.ToDateTime(
                        rd["FechaCreacion"]),

                TotalRenglones =
                    Convert.ToInt32(
                        rd["TotalRenglones"]),

                TotalEntregas =
                    Convert.ToInt32(
                        rd["TotalEntregas"]),

                TotalPiezasRequeridas =
                    Convert.ToInt32(
                        rd["TotalPiezasRequeridas"]),

                TotalPiezasAProducir =
                    Convert.ToInt32(
                        rd["TotalPiezasAProducir"]),

                UltimaFechaRequerida =
                    rd["UltimaFechaRequerida"] ==
                        DBNull.Value
                        ? null
                        : Convert.ToDateTime(
                            rd["UltimaFechaRequerida"])
            });
        }

        ViewBag.ClienteID = clienteId;

        return View(lista);
    }

    [HttpGet]
    public async Task<IActionResult> ProgramacionEntrega(
        int releaseDetalleId)
    {
        if (releaseDetalleId <= 0)
            return BadRequest();

        const string sql = @"
SELECT TOP (1)
    pp.ProgramaProduccionID,
    pp.FechaInicioProgramada
FROM dbo.Planeacion_ProgramaProduccion pp
WHERE pp.ReleaseDetalleID = @ReleaseDetalleID
  AND pp.Activo = 1
  AND ISNULL(pp.EstatusID, 1) NOT IN (9, 99)
ORDER BY
    pp.ProgramaProduccionID DESC;";

        await using var cn =
            new SqlConnection(ConnectionString);

        await cn.OpenAsync();

        await using var cmd =
            new SqlCommand(sql, cn);

        cmd.Parameters.Add(
            "@ReleaseDetalleID",
            SqlDbType.Int).Value =
            releaseDetalleId;

        await using var rd =
            await cmd.ExecuteReaderAsync();

        if (await rd.ReadAsync())
        {
            var semana =
                rd["FechaInicioProgramada"] ==
                    DBNull.Value
                    ? DateTime.Today
                    : Convert.ToDateTime(
                        rd["FechaInicioProgramada"]);

            return RedirectToAction(
                "CalendarioMaquinas",
                "PlaneacionPrograma",
                new
                {
                    semana = semana.Date
                });
        }

        return RedirectToAction(
            "CrearDesdeNecesidad",
            "PlaneacionPrograma",
            new
            {
                releaseDetalleId
            });
    }
}