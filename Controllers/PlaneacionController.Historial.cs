using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

public partial class PlaneacionController
{
    // NSQ_HISTORIAL_OF_V1
    [HttpGet]
    public async Task<IActionResult> Historial(
        string? busqueda = null,
        DateTime? fechaDesde = null,
        DateTime? fechaHasta = null)
    {
        var vm = new PlaneacionOFHistorialIndexVm
        {
            Busqueda = string.IsNullOrWhiteSpace(busqueda)
                ? null
                : busqueda.Trim(),
            FechaDesde = fechaDesde?.Date,
            FechaHasta = fechaHasta?.Date
        };

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        const string sql = @"
SELECT
    s.SolicitudProduccionID,
    ISNULL(NULLIF(s.FolioSolicitud,N''), N'OF-' + CONVERT(NVARCHAR(20),s.SolicitudProduccionID)) AS FolioSolicitud,
    NULLIF(s.NumeroOFRecibida,N'') AS NumeroOFRecibida,
    ISNULL(NULLIF(c.Nombre,N''), ISNULL(NULLIF(s.ClienteNombre,N''),N'SIN CLIENTE')) AS Cliente,
    ISNULL(NULLIF(s.TipoOF,N''),N'RELEASE') AS TipoOF,
    s.EstatusID,
    s.FechaSolicitud,
    s.FechaRequerida,
    s.FechaInicioPlaneada,
    s.FechaFinPlaneada,

    prod.FechaInicioReal,
    prod.FechaFinReal,

    COALESCE(
        prod.FechaFinReal,
        prod.FechaInicioReal,
        s.FechaFinPlaneada,
        s.FechaInicioPlaneada,
        s.FechaSolicitud
    ) AS FechaReferencia,

    ISNULL(det.TotalRenglones,0) AS TotalRenglones,
    ISNULL(det.CantidadPlaneada,0) AS CantidadPlaneada,

    ISNULL(prod.CantidadOK,0) AS CantidadOK,
    ISNULL(prod.CantidadSospechosa,0) AS CantidadSospechosa,
    ISNULL(prod.CantidadScrap,0) AS CantidadScrap,
    ISNULL(prod.TotalEjecuciones,0) AS TotalEjecuciones,

    ISNULL(det.PartesTexto,N'Sin detalle de parte') AS Partes,
    ISNULL(maq.MaquinasTexto,N'Sin máquina registrada') AS Maquinas,
    ISNULL(prod.PersonalTexto,N'Sin personal registrado') AS Personal

FROM dbo.SolicitudesProduccion s

LEFT JOIN dbo.ERP_Clientes c
    ON c.ClienteID = s.ClienteID

OUTER APPLY
(
    SELECT
        COUNT(1) AS TotalRenglones,
        ISNULL(SUM(ISNULL(d.CantidadPiezas,0)),0) AS CantidadPlaneada,

        STUFF
        (
            (
                SELECT N' · ' + x.Texto
                FROM
                (
                    SELECT DISTINCT
                        LTRIM(RTRIM(
                            CONCAT(
                                NULLIF(d2.ReferenciaSAP,N''),
                                CASE
                                    WHEN NULLIF(d2.ReferenciaSAP,N'') IS NOT NULL
                                     AND NULLIF(d2.DesignacionDescripcionSAP,N'') IS NOT NULL
                                        THEN N' - '
                                    ELSE N''
                                END,
                                NULLIF(d2.DesignacionDescripcionSAP,N'')
                            )
                        )) AS Texto
                    FROM dbo.SolicitudesProduccionDetalle d2
                    WHERE d2.SolicitudProduccionID = s.SolicitudProduccionID
                      AND d2.Activo = 1
                ) x
                WHERE NULLIF(x.Texto,N'') IS NOT NULL
                ORDER BY x.Texto
                FOR XML PATH(''), TYPE
            ).value('.','NVARCHAR(MAX)'),
            1,
            3,
            N''
        ) AS PartesTexto

    FROM dbo.SolicitudesProduccionDetalle d
    WHERE d.SolicitudProduccionID = s.SolicitudProduccionID
      AND d.Activo = 1
) det

OUTER APPLY
(
    SELECT
        COUNT(1) AS TotalEjecuciones,
        MIN(e.FechaInicioReal) AS FechaInicioReal,
        MAX(e.FechaFinReal) AS FechaFinReal,
        ISNULL(SUM(ISNULL(e.CantidadOKTotal,0)),0) AS CantidadOK,
        ISNULL(SUM(ISNULL(e.CantidadSospechosaTotal,0)),0) AS CantidadSospechosa,
        ISNULL(SUM(ISNULL(e.CantidadScrapTotal,0)),0) AS CantidadScrap,

        STUFF
        (
            (
                SELECT N' · ' + p.Persona
                FROM
                (
                    SELECT DISTINCT
                        NULLIF(LTRIM(RTRIM(v.Persona)),N'') AS Persona
                    FROM
                    (
                        SELECT e2.OperadorNombre AS Persona
                        FROM dbo.Produccion_Ejecucion e2
                        WHERE e2.SolicitudProduccionID = s.SolicitudProduccionID
                          AND e2.Activo = 1

                        UNION ALL

                        SELECT e2.OperadorAuxiliarNombre
                        FROM dbo.Produccion_Ejecucion e2
                        WHERE e2.SolicitudProduccionID = s.SolicitudProduccionID
                          AND e2.Activo = 1

                        UNION ALL

                        SELECT e2.TecnicoProduccionNombre
                        FROM dbo.Produccion_Ejecucion e2
                        WHERE e2.SolicitudProduccionID = s.SolicitudProduccionID
                          AND e2.Activo = 1
                    ) v
                ) p
                WHERE p.Persona IS NOT NULL
                ORDER BY p.Persona
                FOR XML PATH(''), TYPE
            ).value('.','NVARCHAR(MAX)'),
            1,
            3,
            N''
        ) AS PersonalTexto

    FROM dbo.Produccion_Ejecucion e
    WHERE e.SolicitudProduccionID = s.SolicitudProduccionID
      AND e.Activo = 1
) prod

OUTER APPLY
(
    SELECT
        STUFF
        (
            (
                SELECT N' · ' + x.Maquina
                FROM
                (
                    SELECT DISTINCT
                        LTRIM(RTRIM(
                            CONCAT(
                                ISNULL(NULLIF(m.Codigo,N''),N'MAQ'),
                                CASE
                                    WHEN NULLIF(m.Nombre,N'') IS NOT NULL
                                     AND NULLIF(m.Nombre,N'') <> NULLIF(m.Codigo,N'')
                                        THEN N' - ' + m.Nombre
                                    ELSE N''
                                END
                            )
                        )) AS Maquina
                    FROM dbo.SolicitudesProduccionDetalle d3
                    INNER JOIN dbo.SolicitudesProduccionAsignacionMaquina a
                        ON a.SolicitudProduccionDetalleID = d3.SolicitudProduccionDetalleID
                       AND a.Activo = 1
                    INNER JOIN dbo.ERP_Maquinas m
                        ON m.MaquinaID = a.MaquinaID
                    WHERE d3.SolicitudProduccionID = s.SolicitudProduccionID
                      AND d3.Activo = 1
                ) x
                WHERE NULLIF(x.Maquina,N'') IS NOT NULL
                ORDER BY x.Maquina
                FOR XML PATH(''), TYPE
            ).value('.','NVARCHAR(MAX)'),
            1,
            3,
            N''
        ) AS MaquinasTexto
) maq

WHERE s.Activo = 1

  AND
  (
      ISNULL(prod.TotalEjecuciones,0) > 0
      OR s.EstatusID IN (10,99)
  )

  AND
  (
      @FechaDesde IS NULL
      OR CONVERT(
            date,
            COALESCE(
                prod.FechaFinReal,
                prod.FechaInicioReal,
                s.FechaFinPlaneada,
                s.FechaInicioPlaneada,
                s.FechaSolicitud
            )
         ) >= @FechaDesde
  )

  AND
  (
      @FechaHasta IS NULL
      OR CONVERT(
            date,
            COALESCE(
                prod.FechaFinReal,
                prod.FechaInicioReal,
                s.FechaFinPlaneada,
                s.FechaInicioPlaneada,
                s.FechaSolicitud
            )
         ) <= @FechaHasta
  )

  AND
  (
      @Busqueda IS NULL
      OR s.FolioSolicitud LIKE @Busqueda
      OR s.NumeroOFRecibida LIKE @Busqueda
      OR ISNULL(c.Nombre,s.ClienteNombre) LIKE @Busqueda
      OR det.PartesTexto LIKE @Busqueda
      OR maq.MaquinasTexto LIKE @Busqueda
      OR prod.PersonalTexto LIKE @Busqueda
  )

ORDER BY
    FechaReferencia DESC,
    s.SolicitudProduccionID DESC;";

        await using var cmd = new SqlCommand(sql, cn);

        cmd.Parameters.Add("@FechaDesde", SqlDbType.Date).Value =
            (object?)vm.FechaDesde ?? DBNull.Value;

        cmd.Parameters.Add("@FechaHasta", SqlDbType.Date).Value =
            (object?)vm.FechaHasta ?? DBNull.Value;

        cmd.Parameters.Add("@Busqueda", SqlDbType.NVarChar, 250).Value =
            string.IsNullOrWhiteSpace(vm.Busqueda)
                ? DBNull.Value
                : $"%{vm.Busqueda}%";

        await using var rd = await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
        {
            var estatusId = Convert.ToInt32(rd["EstatusID"]);

            vm.Items.Add(new PlaneacionOFHistorialItemVm
            {
                SolicitudProduccionID =
                    Convert.ToInt32(rd["SolicitudProduccionID"]),

                FolioSolicitud =
                    rd["FolioSolicitud"]?.ToString() ?? string.Empty,

                NumeroOFRecibida =
                    rd["NumeroOFRecibida"] == DBNull.Value
                        ? null
                        : rd["NumeroOFRecibida"].ToString(),

                Cliente =
                    rd["Cliente"]?.ToString() ?? "SIN CLIENTE",

                TipoOF =
                    rd["TipoOF"]?.ToString() ?? "RELEASE",

                EstatusID = estatusId,
                EstatusNombre = PlaneacionOFEstatus.Nombre(estatusId),

                FechaSolicitud =
                    Convert.ToDateTime(rd["FechaSolicitud"]),

                FechaRequerida =
                    rd["FechaRequerida"] == DBNull.Value
                        ? null
                        : Convert.ToDateTime(rd["FechaRequerida"]),

                FechaInicioPlaneada =
                    rd["FechaInicioPlaneada"] == DBNull.Value
                        ? null
                        : Convert.ToDateTime(rd["FechaInicioPlaneada"]),

                FechaFinPlaneada =
                    rd["FechaFinPlaneada"] == DBNull.Value
                        ? null
                        : Convert.ToDateTime(rd["FechaFinPlaneada"]),

                FechaInicioReal =
                    rd["FechaInicioReal"] == DBNull.Value
                        ? null
                        : Convert.ToDateTime(rd["FechaInicioReal"]),

                FechaFinReal =
                    rd["FechaFinReal"] == DBNull.Value
                        ? null
                        : Convert.ToDateTime(rd["FechaFinReal"]),

                FechaReferencia =
                    Convert.ToDateTime(rd["FechaReferencia"]),

                TotalRenglones =
                    Convert.ToInt32(rd["TotalRenglones"]),

                CantidadPlaneada =
                    Convert.ToInt32(rd["CantidadPlaneada"]),

                CantidadOK =
                    Convert.ToInt32(rd["CantidadOK"]),

                CantidadSospechosa =
                    Convert.ToInt32(rd["CantidadSospechosa"]),

                CantidadScrap =
                    Convert.ToInt32(rd["CantidadScrap"]),

                TotalEjecuciones =
                    Convert.ToInt32(rd["TotalEjecuciones"]),

                Partes =
                    rd["Partes"]?.ToString() ?? "Sin detalle de parte",

                Maquinas =
                    rd["Maquinas"]?.ToString() ?? "Sin máquina registrada",

                Personal =
                    rd["Personal"]?.ToString() ?? "Sin personal registrado"
            });
        }

        return View(vm);
    }
}
