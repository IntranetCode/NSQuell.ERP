using ERP.NSQuell.Models;
using ERP.NSQuell.Servicios.Almacen;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Linq;

namespace ERP.NSQuell.Controllers
{
    public class GP12Controller : Controller
    {
        private readonly IConfiguration _configuration;

        public GP12Controller(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private string ConnectionString =>
            _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "No se encontró la cadena de conexión DefaultConnection.");

        [HttpGet]
        public async Task<IActionResult> Index(
            string? busqueda,
            int? estatusID,
            string? origen)
        {
            busqueda = Limpiar(busqueda);

            origen = string.IsNullOrWhiteSpace(origen)
                ? null
                : origen.Trim().ToUpperInvariant();

            if (!string.IsNullOrWhiteSpace(origen) &&
                !GP12Origen.EsValido(origen))
            {
                origen = null;
            }

            var model = new GP12IndexViewModel
            {
                Busqueda = busqueda,
                EstatusID = estatusID,
                Origen = origen
            };

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            const string sqlTotales = @"
SELECT
    SUM(CASE WHEN EstatusID = 1  THEN 1 ELSE 0 END) AS Recibidos,
    SUM(CASE WHEN EstatusID = 2  THEN 1 ELSE 0 END) AS PendientesProgramar,
    SUM(CASE WHEN EstatusID = 3  THEN 1 ELSE 0 END) AS Programados,
    SUM(CASE WHEN EstatusID = 4  THEN 1 ELSE 0 END) AS Asignados,
    SUM(CASE WHEN EstatusID = 5  THEN 1 ELSE 0 END) AS EnInspeccion,
    SUM(CASE WHEN EstatusID = 6  THEN 1 ELSE 0 END) AS InspeccionPausada,
    SUM(CASE WHEN EstatusID = 7  THEN 1 ELSE 0 END) AS InspeccionTerminada,
    SUM(CASE WHEN EstatusID = 8  THEN 1 ELSE 0 END) AS EnTarima,
    SUM(CASE WHEN EstatusID = 9  THEN 1 ELSE 0 END) AS SalidaRegistrada,
    SUM(CASE WHEN EstatusID = 10 THEN 1 ELSE 0 END) AS Cerrados
FROM dbo.GP12_Solicitudes
WHERE Activo = 1;";

            await using (var cmd = new SqlCommand(sqlTotales, cn))
            await using (var rd = await cmd.ExecuteReaderAsync())
            {
                if (await rd.ReadAsync())
                {
                    model.TotalRecibidos = LeerInt(rd["Recibidos"]);
                    model.TotalPendientesProgramar = LeerInt(rd["PendientesProgramar"]);
                    model.TotalProgramados = LeerInt(rd["Programados"]);
                    model.TotalAsignados = LeerInt(rd["Asignados"]);
                    model.TotalEnInspeccion = LeerInt(rd["EnInspeccion"]);
                    model.TotalInspeccionPausada = LeerInt(rd["InspeccionPausada"]);
                    model.TotalInspeccionTerminada = LeerInt(rd["InspeccionTerminada"]);
                    model.TotalEnTarima = LeerInt(rd["EnTarima"]);
                    model.TotalSalidaRegistrada = LeerInt(rd["SalidaRegistrada"]);
                    model.TotalCerrados = LeerInt(rd["Cerrados"]);
                }
            }

            const string sqlListado = @"
SELECT
    s.SolicitudGP12ID,
    s.Origen,
    s.OrdenFabricacion,
    s.ClienteNombre,
    s.NumeroParte,
    s.DescripcionParte,
    ISNULL(s.CantidadSolicitada, 0) AS CantidadSolicitada,
    ISNULL(s.CantidadRecibida, 0) AS CantidadRecibida,
    ISNULL(s.CantidadProcesada, 0) AS CantidadProcesada,
    ISNULL(s.CantidadPendiente, 0) AS CantidadPendiente,
    s.EstatusID,
    ISNULL(e.Codigo, N'') AS EstatusCodigo,
    ISNULL(e.Nombre, N'') AS EstatusNombre,
    s.FechaSolicitud,
    s.FechaRecepcion,
    s.Motivo
FROM dbo.GP12_Solicitudes s
INNER JOIN dbo.GP12_Estatus e
    ON e.EstatusID = s.EstatusID
WHERE s.Activo = 1
  AND (@EstatusID IS NULL OR s.EstatusID = @EstatusID)
  AND (@Origen IS NULL OR s.Origen = @Origen)
  AND
  (
      @Busqueda IS NULL
      OR s.OrdenFabricacion LIKE N'%' + @Busqueda + N'%'
      OR s.ClienteNombre LIKE N'%' + @Busqueda + N'%'
      OR s.NumeroParte LIKE N'%' + @Busqueda + N'%'
      OR s.DescripcionParte LIKE N'%' + @Busqueda + N'%'
      OR s.MaterialCodigo LIKE N'%' + @Busqueda + N'%'
      OR s.MaterialDescripcion LIKE N'%' + @Busqueda + N'%'
      OR s.Motivo LIKE N'%' + @Busqueda + N'%'
  )
ORDER BY
    CASE WHEN e.EsFinal = 0 THEN 0 ELSE 1 END,
    s.FechaSolicitud DESC,
    s.SolicitudGP12ID DESC;";

            await using (var cmd = new SqlCommand(sqlListado, cn))
            {
                cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value =
                    (object?)estatusID ?? DBNull.Value;

                cmd.Parameters.Add("@Origen", SqlDbType.NVarChar, 40).Value =
                    (object?)origen ?? DBNull.Value;

                cmd.Parameters.Add("@Busqueda", SqlDbType.NVarChar, 500).Value =
                    string.IsNullOrWhiteSpace(busqueda)
                        ? DBNull.Value
                        : busqueda;

                await using var rd = await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    model.Solicitudes.Add(new GP12ListadoItemViewModel
                    {
                        SolicitudGP12ID = Convert.ToInt32(rd["SolicitudGP12ID"]),
                        Origen = rd["Origen"] as string ?? string.Empty,
                        OrdenFabricacion = rd["OrdenFabricacion"] as string,
                        ClienteNombre = rd["ClienteNombre"] as string,
                        NumeroParte = rd["NumeroParte"] as string,
                        DescripcionParte = rd["DescripcionParte"] as string,
                        CantidadSolicitada = Convert.ToDecimal(rd["CantidadSolicitada"]),
                        CantidadRecibida = Convert.ToDecimal(rd["CantidadRecibida"]),
                        CantidadProcesada = Convert.ToDecimal(rd["CantidadProcesada"]),
                        CantidadPendiente = Convert.ToDecimal(rd["CantidadPendiente"]),
                        EstatusID = Convert.ToInt32(rd["EstatusID"]),
                        EstatusCodigo = rd["EstatusCodigo"] as string ?? string.Empty,
                        EstatusNombre = rd["EstatusNombre"] as string ?? string.Empty,
                        FechaSolicitud = Convert.ToDateTime(rd["FechaSolicitud"]),
                        FechaRecepcion = LeerFechaNullable(rd["FechaRecepcion"]),
                        Motivo = rd["Motivo"] as string
                    });
                }
            }

            model.TotalMostrados = model.Solicitudes.Count;
            return View(model);
        }

        // =========================================================
        // ALMACÉN GP12
        // Muestra todo el material activo de GP12, sin importar su origen.
        // No consulta ni modifica tablas del módulo Almacén.
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Almacen(
            string? busqueda,
            string? filtro)
        {
            busqueda = Limpiar(busqueda);

            filtro = string.IsNullOrWhiteSpace(filtro)
                ? GP12FiltroAlmacen.Todos
                : filtro.Trim().ToUpperInvariant();

            if (!GP12FiltroAlmacen.EsValido(filtro))
                filtro = GP12FiltroAlmacen.Todos;

            var model = new GP12AlmacenViewModel
            {
                Busqueda = busqueda,
                Filtro = filtro
            };

            var materiales = new List<GP12AlmacenItemViewModel>();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            const string sql = @"
WITH Movimientos AS
(
    SELECT
        m.SolicitudGP12ID,
        SUM
        (
            CASE
                WHEN m.TipoMovimiento IN (N'ENTRADA', N'AJUSTE_ENTRADA')
                    THEN ISNULL(m.Cantidad, 0)
                WHEN m.TipoMovimiento IN (N'SALIDA', N'AJUSTE_SALIDA')
                    THEN -ISNULL(m.Cantidad, 0)
                ELSE 0
            END
        ) AS SaldoInventario,
        SUM
        (
            CASE
                WHEN m.TipoMovimiento IN (N'SALIDA', N'AJUSTE_SALIDA')
                    THEN ISNULL(m.Cantidad, 0)
                ELSE 0
            END
        ) AS TotalSalidas,
        MAX
        (
            CASE
                WHEN m.TipoMovimiento IN (N'ENTRADA', N'AJUSTE_ENTRADA')
                    THEN m.FechaMovimiento
            END
        ) AS FechaUltimaEntrada,
        MAX
        (
            CASE
                WHEN m.TipoMovimiento IN (N'SALIDA', N'AJUSTE_SALIDA')
                    THEN m.FechaMovimiento
            END
        ) AS FechaUltimaSalida
    FROM dbo.GP12_InventarioMovimientos m
    WHERE m.Activo = 1
    GROUP BY m.SolicitudGP12ID
),
UltimaInspeccion AS
(
    SELECT
        q.SolicitudGP12ID,
        q.CantidadNOK,
        q.CantidadScrap
    FROM
    (
        SELECT
            i.SolicitudGP12ID,
            ISNULL(i.CantidadNOK, 0) AS CantidadNOK,
            ISNULL(i.CantidadScrap, 0) AS CantidadScrap,
            ROW_NUMBER() OVER
            (
                PARTITION BY i.SolicitudGP12ID
                ORDER BY i.FechaFin DESC, i.InspeccionGP12ID DESC
            ) AS rn
        FROM dbo.GP12_Inspecciones i
        WHERE i.Activo = 1
          AND i.FechaFin IS NOT NULL
    ) q
    WHERE q.rn = 1
)
SELECT
    s.SolicitudGP12ID,
    s.CajaProduccionID,
    s.CajaLiberadaID,
    s.CalidadInspeccionID,
    s.Origen,
    s.OrdenFabricacion,
    s.ClienteNombre,
    s.NumeroParte,
    s.DescripcionParte,
    s.MaterialCodigo,
    s.MaterialDescripcion,
    ISNULL(s.CantidadSolicitada, 0) AS CantidadSolicitada,
    ISNULL(s.CantidadRecibida, 0) AS CantidadRecibida,
    ISNULL(s.CantidadProcesada, 0) AS CantidadProcesada,
    ISNULL(m.SaldoInventario, 0) AS SaldoInventario,
    s.EstatusID,
    ISNULL(e.Nombre, N'') AS EstatusNombre,
    s.FechaSolicitud,
    s.FechaRecepcion,
    m.FechaUltimaEntrada,
    m.FechaUltimaSalida,
    CASE
        WHEN ISNULL(s.CantidadRecibida, 0) < ISNULL(s.CantidadSolicitada, 0)
            THEN N'PENDIENTE_RECEPCION'
        WHEN ISNULL(m.SaldoInventario, 0) <= 0
             AND ISNULL(m.TotalSalidas, 0) > 0
            THEN N'SALIDA_REGISTRADA'
        WHEN s.EstatusID IN (@InspeccionTerminada, @EnTarima, @SalidaRegistrada)
             AND ISNULL(s.CantidadRecibida, 0) > 0
             AND ISNULL(s.CantidadProcesada, 0) >= ISNULL(s.CantidadRecibida, 0)
             AND ISNULL(u.CantidadNOK, 0) = 0
             AND ISNULL(u.CantidadScrap, 0) = 0
             AND ISNULL(m.SaldoInventario, 0) > 0
            THEN N'LISTO_ALMACEN'
        WHEN ISNULL(m.SaldoInventario, 0) > 0
            THEN N'EN_GP12'
        ELSE N'PENDIENTE_RECEPCION'
    END AS EstadoAlmacen
FROM dbo.GP12_Solicitudes s
INNER JOIN dbo.GP12_Estatus e
    ON e.EstatusID = s.EstatusID
LEFT JOIN Movimientos m
    ON m.SolicitudGP12ID = s.SolicitudGP12ID
LEFT JOIN UltimaInspeccion u
    ON u.SolicitudGP12ID = s.SolicitudGP12ID
WHERE s.Activo = 1
  AND
  (
        @Busqueda IS NULL
     OR s.OrdenFabricacion LIKE N'%' + @Busqueda + N'%'
     OR s.ClienteNombre LIKE N'%' + @Busqueda + N'%'
     OR s.NumeroParte LIKE N'%' + @Busqueda + N'%'
     OR s.DescripcionParte LIKE N'%' + @Busqueda + N'%'
     OR s.MaterialCodigo LIKE N'%' + @Busqueda + N'%'
     OR s.MaterialDescripcion LIKE N'%' + @Busqueda + N'%'
  )
ORDER BY
    CASE
        WHEN ISNULL(s.CantidadRecibida, 0) < ISNULL(s.CantidadSolicitada, 0) THEN 0
        WHEN s.EstatusID IN (@InspeccionTerminada, @EnTarima, @SalidaRegistrada)
             AND ISNULL(s.CantidadProcesada, 0) >= ISNULL(s.CantidadRecibida, 0)
             AND ISNULL(u.CantidadNOK, 0) = 0
             AND ISNULL(u.CantidadScrap, 0) = 0
             AND ISNULL(m.SaldoInventario, 0) > 0 THEN 1
        WHEN ISNULL(m.SaldoInventario, 0) > 0 THEN 2
        ELSE 3
    END,
    s.FechaSolicitud DESC,
    s.SolicitudGP12ID DESC;";

            await using var cmd = new SqlCommand(sql, cn);


            cmd.Parameters.Add("@Busqueda", SqlDbType.NVarChar, 500).Value =
                string.IsNullOrWhiteSpace(busqueda)
                    ? DBNull.Value
                    : busqueda;

            cmd.Parameters.Add("@InspeccionTerminada", SqlDbType.Int).Value =
                GP12Estatus.InspeccionTerminada;

            cmd.Parameters.Add("@EnTarima", SqlDbType.Int).Value =
                GP12Estatus.EnTarima;

            cmd.Parameters.Add("@SalidaRegistrada", SqlDbType.Int).Value =
                GP12Estatus.SalidaRegistrada;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                materiales.Add(new GP12AlmacenItemViewModel
                {
                    SolicitudGP12ID = Convert.ToInt32(rd["SolicitudGP12ID"]),
                    CajaProduccionID = rd["CajaProduccionID"] == DBNull.Value
                        ? null
                        : Convert.ToInt64(rd["CajaProduccionID"]),
                    CajaLiberadaID = LeerIntNullable(rd["CajaLiberadaID"]),
                    CalidadInspeccionID = LeerIntNullable(rd["CalidadInspeccionID"]),
                    Origen = rd["Origen"] as string ?? string.Empty,
                    OrdenFabricacion = rd["OrdenFabricacion"] as string,
                    ClienteNombre = rd["ClienteNombre"] as string,
                    NumeroParte = rd["NumeroParte"] as string,
                    DescripcionParte = rd["DescripcionParte"] as string,
                    MaterialCodigo = rd["MaterialCodigo"] as string,
                    MaterialDescripcion = rd["MaterialDescripcion"] as string,
                    CantidadSolicitada = Convert.ToDecimal(rd["CantidadSolicitada"]),
                    CantidadRecibida = Convert.ToDecimal(rd["CantidadRecibida"]),
                    CantidadProcesada = Convert.ToDecimal(rd["CantidadProcesada"]),
                    SaldoInventario = Convert.ToDecimal(rd["SaldoInventario"]),
                    EstatusID = Convert.ToInt32(rd["EstatusID"]),
                    EstatusNombre = rd["EstatusNombre"] as string ?? string.Empty,
                    EstadoAlmacen = rd["EstadoAlmacen"] as string
                        ?? GP12FiltroAlmacen.PendienteRecepcion,
                    FechaSolicitud = Convert.ToDateTime(rd["FechaSolicitud"]),
                    FechaRecepcion = LeerFechaNullable(rd["FechaRecepcion"]),
                    FechaUltimaEntrada = LeerFechaNullable(rd["FechaUltimaEntrada"]),
                    FechaUltimaSalida = LeerFechaNullable(rd["FechaUltimaSalida"])
                });
            }

            model.TotalSolicitudes = materiales.Count;
            model.TotalPendienteRecibir = materiales.Count(x => x.EsPendienteRecepcion);
            model.TotalEnInventario = materiales.Count(x => x.EstaEnGP12);
            model.TotalListoAlmacen = materiales.Count(x => x.EstaListoAlmacen);
            model.TotalSalidaRegistrada = materiales.Count(x => x.TieneSalidaRegistrada);

            model.PiezasPendientesRecibir = materiales.Sum(x => x.PendienteRecibir);
            model.PiezasEnInventario = materiales.Sum(x => Math.Max(0, x.SaldoInventario));
            model.PiezasListasAlmacen = materiales
                .Where(x => x.EstaListoAlmacen)
                .Sum(x => Math.Max(0, x.SaldoInventario));

            model.Materiales = filtro == GP12FiltroAlmacen.Todos
                ? materiales
                : materiales.FindAll(x => x.EstadoAlmacen == filtro);

            return View(model);
        }

        // =========================================================
        // CREAR SOLICITUD GP12 DESDE UNA OF EXISTENTE
        // /GP12/Crear
        // GP12 NO crea ni modifica la OF; únicamente la consulta.
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Crear()
        {
            var model = new GP12CrearViewModel();
            await CargarOrdenesFabricacionAsync(model);
            return View(model);
        }

        // =========================================================
        // AJAX: DATOS Y RENGLONES DE UNA OF
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> ObtenerOF(
            int solicitudProduccionId)
        {
            if (solicitudProduccionId <= 0)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "La OF seleccionada no es válida."
                });
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            const string sql = @"
SELECT
    s.SolicitudProduccionID,
    s.FolioSolicitud,
    s.NumeroOFRecibida,
    s.FechaSolicitud,
    s.FechaRequerida,
    s.ClienteID,
    ISNULL(c.Nombre, s.ClienteNombre) AS ClienteNombre,
    s.EstatusID,

    d.SolicitudProduccionDetalleID,
    d.Renglon,
    d.ParteID,

    COALESCE(
        NULLIF(p.NumeroParte, N''),
        NULLIF(d.ReferenciaSAP, N''),
        N'SIN PARTE'
    ) AS NumeroParte,

    COALESCE(
        NULLIF(d.ReferenciaSAP, N''),
        NULLIF(p.ReferenciaSAP, N''),
        NULLIF(p.NumeroParte, N'')
    ) AS ReferenciaSAP,

    COALESCE(
        NULLIF(d.DesignacionDescripcionSAP, N''),
        NULLIF(p.Designacion, N''),
        NULLIF(p.Descripcion, N''),
        NULLIF(p.NumeroParte, N''),
        N'Sin descripción'
    ) AS DescripcionParte,

    ISNULL(d.CantidadPiezas, 0) AS CantidadPiezas,
    d.Color,
    d.PiezasPorCaja,

    d.MaterialID,
    d.MaterialCodigo,
    d.MaterialDescripcion

FROM dbo.SolicitudesProduccion s

LEFT JOIN dbo.ERP_Clientes c
    ON c.ClienteID = s.ClienteID

INNER JOIN dbo.SolicitudesProduccionDetalle d
    ON d.SolicitudProduccionID = s.SolicitudProduccionID
   AND d.Activo = 1

LEFT JOIN dbo.ERP_Partes p
    ON p.ParteID = d.ParteID

WHERE s.SolicitudProduccionID = @SolicitudProduccionID
  AND s.Activo = 1
  AND ISNULL(s.EstatusID, 0) <> @EstatusCancelado

ORDER BY
    d.Renglon,
    d.SolicitudProduccionDetalleID;";

            await using var cmd = new SqlCommand(sql, cn);

            cmd.Parameters.Add(
                "@SolicitudProduccionID",
                SqlDbType.Int).Value =
                solicitudProduccionId;

            cmd.Parameters.Add(
                "@EstatusCancelado",
                SqlDbType.Int).Value =
                PlaneacionOFEstatus.Cancelada;

            await using var rd = await cmd.ExecuteReaderAsync();

            object? encabezado = null;
            var detalles = new List<object>();

            while (await rd.ReadAsync())
            {
                if (encabezado == null)
                {
                    var numeroOF =
                        rd["NumeroOFRecibida"] as string;

                    var folio =
                        rd["FolioSolicitud"] as string;

                    encabezado = new
                    {
                        solicitudProduccionID =
                            Convert.ToInt32(
                                rd["SolicitudProduccionID"]),

                        numeroOF =
                            !string.IsNullOrWhiteSpace(numeroOF)
                                ? numeroOF
                                : !string.IsNullOrWhiteSpace(folio)
                                    ? folio
                                    : $"OF #{solicitudProduccionId}",

                        folioSolicitud = folio,

                        clienteID =
                            LeerIntNullable(rd["ClienteID"]),

                        clienteNombre =
                            rd["ClienteNombre"] as string,

                        fechaSolicitud =
                            Convert.ToDateTime(
                                rd["FechaSolicitud"])
                            .ToString("dd/MM/yyyy"),

                        fechaRequerida =
                            LeerFechaNullable(
                                rd["FechaRequerida"])
                            ?.ToString("dd/MM/yyyy")
                    };
                }

                detalles.Add(new
                {
                    solicitudProduccionDetalleID =
                        Convert.ToInt32(
                            rd["SolicitudProduccionDetalleID"]),

                    renglon =
                        Convert.ToInt32(rd["Renglon"]),

                    parteID =
                        LeerIntNullable(rd["ParteID"]),

                    numeroParte =
                        rd["NumeroParte"] as string
                        ?? string.Empty,

                    referenciaSAP =
                        rd["ReferenciaSAP"] as string,

                    descripcionParte =
                        rd["DescripcionParte"] as string
                        ?? string.Empty,

                    cantidadPiezas =
                        Convert.ToDecimal(
                            rd["CantidadPiezas"]),

                    color =
                        rd["Color"] as string,

                    piezasPorCaja =
                        rd["PiezasPorCaja"] == DBNull.Value
                            ? (decimal?)null
                            : Convert.ToDecimal(
                                rd["PiezasPorCaja"]),

                    materialID =
                        LeerIntNullable(rd["MaterialID"]),

                    materialCodigo =
                        rd["MaterialCodigo"] as string,

                    materialDescripcion =
                        rd["MaterialDescripcion"] as string
                });
            }

            if (encabezado == null)
            {
                return NotFound(new
                {
                    ok = false,
                    mensaje =
                        "No se encontró la OF, está cancelada o no tiene renglones activos."
                });
            }

            return Json(new
            {
                ok = true,
                encabezado,
                detalles
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Crear(
            GP12CrearViewModel model)
        {
            NormalizarCrear(model);

            if (!ModelState.IsValid)
            {
                await CargarOrdenesFabricacionAsync(model);
                return View(model);
            }

            var cantidadTotal =
                model.CantidadAmarilla +
                model.CantidadRoja;

            if (cantidadTotal <= 0)
            {
                ModelState.AddModelError(
                    "",
                    "Captura al menos una pieza amarilla o roja para GP12.");

                await CargarOrdenesFabricacionAsync(model);
                return View(model);
            }

            var usuarioID = ObtenerUsuarioIdActual();

            if (!usuarioID.HasValue || usuarioID.Value <= 0)
            {
                ModelState.AddModelError(
                    "",
                    "No se pudo identificar al usuario de la sesión.");

                await CargarOrdenesFabricacionAsync(model);
                return View(model);
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var tx =
                (SqlTransaction)await cn.BeginTransactionAsync();

            try
            {
                var origen = await ObtenerDatosOrigenOFAsync(
                    model.SolicitudProduccionID!.Value,
                    model.SolicitudProduccionDetalleID!.Value,
                    cn,
                    tx);

                if (origen == null)
                {
                    throw new InvalidOperationException(
                        "La OF o el renglón seleccionado ya no existe, fue cancelado o no pertenece a la OF elegida.");
                }

                if (cantidadTotal > origen.CantidadPiezasOF)
                {
                    throw new InvalidOperationException(
                        $"La suma de piezas amarillas y rojas no puede superar la cantidad del renglón de la OF ({origen.CantidadPiezasOF:N4}).");
                }

                const string sql = @"
INSERT INTO dbo.GP12_Solicitudes
(
    Origen,
    ProgramaProduccionID,
    EjecucionProduccionID,
    CalidadInspeccionID,
    SolicitudProduccionID,
    SolicitudProduccionDetalleID,

    OrdenFabricacion,

    ClienteID,
    ClienteNombre,

    ParteID,
    NumeroParte,
    DescripcionParte,

    MaterialID,
    MaterialCodigo,
    MaterialDescripcion,

    CantidadSolicitada,
    CantidadRecibida,
    CantidadProcesada,
    CantidadPendiente,

    Motivo,
    InstruccionTrabajo,
    CodigoHIP,
    CodigoHOE,
    Observaciones,

    EstatusID,
    FechaSolicitud,

    UsuarioSolicitudID,
    UsuarioCreacionID,
    FechaCreacion,

    Activo
)
VALUES
(
    N'PLANEACION',
    NULL,
    NULL,
    NULL,
    @SolicitudProduccionID,
    @SolicitudProduccionDetalleID,

    @OrdenFabricacion,

    @ClienteID,
    @ClienteNombre,

    @ParteID,
    @NumeroParte,
    @DescripcionParte,

    @MaterialID,
    @MaterialCodigo,
    @MaterialDescripcion,

    @CantidadSolicitada,
    0,
    0,
    0,

    @Motivo,
    @InstruccionTrabajo,
    @CodigoHIP,
    @CodigoHOE,
    @Observaciones,

    @EstatusID,
    SYSDATETIME(),

    @UsuarioID,
    @UsuarioID,
    SYSDATETIME(),

    1
);

SELECT CAST(SCOPE_IDENTITY() AS INT);";

                await using var cmd = new SqlCommand(sql, cn, tx);

                cmd.Parameters.Add(
                    "@SolicitudProduccionID",
                    SqlDbType.Int).Value =
                    origen.SolicitudProduccionID;

                cmd.Parameters.Add(
                    "@SolicitudProduccionDetalleID",
                    SqlDbType.Int).Value =
                    origen.SolicitudProduccionDetalleID;

                AgregarNullable(
                    cmd,
                    "@OrdenFabricacion",
                    SqlDbType.NVarChar,
                    200,
                    origen.OrdenFabricacion);

                cmd.Parameters.Add(
                    "@ClienteID",
                    SqlDbType.Int).Value =
                    (object?)origen.ClienteID
                    ?? DBNull.Value;

                AgregarNullable(
                    cmd,
                    "@ClienteNombre",
                    SqlDbType.NVarChar,
                    500,
                    origen.ClienteNombre);

                cmd.Parameters.Add(
                    "@ParteID",
                    SqlDbType.Int).Value =
                    (object?)origen.ParteID
                    ?? DBNull.Value;

                AgregarNullable(
                    cmd,
                    "@NumeroParte",
                    SqlDbType.NVarChar,
                    300,
                    origen.NumeroParte);

                AgregarNullable(
                    cmd,
                    "@DescripcionParte",
                    SqlDbType.NVarChar,
                    1000,
                    origen.DescripcionParte);

                cmd.Parameters.Add(
                    "@MaterialID",
                    SqlDbType.Int).Value =
                    (object?)origen.MaterialID
                    ?? DBNull.Value;

                AgregarNullable(
                    cmd,
                    "@MaterialCodigo",
                    SqlDbType.NVarChar,
                    300,
                    origen.MaterialCodigo);

                AgregarNullable(
                    cmd,
                    "@MaterialDescripcion",
                    SqlDbType.NVarChar,
                    1000,
                    origen.MaterialDescripcion);

                AgregarDecimal(
                    cmd,
                    "@CantidadSolicitada",
                    cantidadTotal);

                cmd.Parameters.Add(
                    "@Motivo",
                    SqlDbType.NVarChar,
                    2000).Value =
                    model.Motivo;

                AgregarNullable(
                    cmd,
                    "@InstruccionTrabajo",
                    SqlDbType.NVarChar,
                    500,
                    model.InstruccionTrabajo);

                AgregarNullable(
                    cmd,
                    "@CodigoHIP",
                    SqlDbType.NVarChar,
                    200,
                    model.CodigoHIP);

                AgregarNullable(
                    cmd,
                    "@CodigoHOE",
                    SqlDbType.NVarChar,
                    200,
                    model.CodigoHOE);

                AgregarNullable(
                    cmd,
                    "@Observaciones",
                    SqlDbType.NVarChar,
                    4000,
                    model.Observaciones);

                cmd.Parameters.Add(
                    "@EstatusID",
                    SqlDbType.Int).Value =
                    GP12Estatus.Recibido;

                cmd.Parameters.Add(
                    "@UsuarioID",
                    SqlDbType.Int).Value =
                    usuarioID.Value;

                var solicitudID =
                    Convert.ToInt32(
                        await cmd.ExecuteScalarAsync());

                const string sqlEtiqueta = @"
INSERT INTO dbo.GP12_SolicitudEtiquetas
(
    SolicitudGP12ID,
    TipoEtiqueta,
    CantidadSolicitada,
    CantidadRecibida,
    CantidadProcesada,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
VALUES
(
    @SolicitudGP12ID,
    @TipoEtiqueta,
    @CantidadSolicitada,
    0,
    0,
    @UsuarioID,
    SYSDATETIME(),
    1
);";

                var clasificaciones = new[]
                {
                    new
                    {
                        Tipo = GP12TipoEtiqueta.Amarilla,
                        Cantidad = model.CantidadAmarilla
                    },
                    new
                    {
                        Tipo = GP12TipoEtiqueta.Roja,
                        Cantidad = model.CantidadRoja
                    }
                };

                foreach (var clasificacion in clasificaciones)
                {
                    if (clasificacion.Cantidad <= 0)
                        continue;

                    await using var cmdEtiqueta =
                        new SqlCommand(sqlEtiqueta, cn, tx);

                    cmdEtiqueta.Parameters.Add(
                        "@SolicitudGP12ID",
                        SqlDbType.Int).Value = solicitudID;

                    cmdEtiqueta.Parameters.Add(
                        "@TipoEtiqueta",
                        SqlDbType.VarChar,
                        20).Value = clasificacion.Tipo;

                    AgregarDecimal(
                        cmdEtiqueta,
                        "@CantidadSolicitada",
                        clasificacion.Cantidad);

                    cmdEtiqueta.Parameters.Add(
                        "@UsuarioID",
                        SqlDbType.Int).Value = usuarioID.Value;

                    await cmdEtiqueta.ExecuteNonQueryAsync();
                }

                await AgregarHistorialAsync(
                    cn,
                    tx,
                    solicitudID,
                    GP12Movimientos.SolicitudCreada,
                    null,
                    GP12Estatus.Recibido,
                    GP12EntidadHistorial.Solicitud,
                    solicitudID,
                    $"Solicitud GP12 creada desde la OF {origen.OrdenFabricacion}, renglón {origen.Renglon}. " +
                    $"Amarillas: {model.CantidadAmarilla:N4}. " +
                    $"Rojas: {model.CantidadRoja:N4}. " +
                    $"Total GP12: {cantidadTotal:N4}.",
                    usuarioID.Value);

                await tx.CommitAsync();

                TempData["Mensaje"] =
                    "Solicitud GP12 creada correctamente desde la OF seleccionada.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = solicitudID });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                ModelState.AddModelError(
                    "",
                    "No fue posible crear la solicitud GP12: " +
                    ex.Message);

                await CargarOrdenesFabricacionAsync(model);
                return View(model);
            }
        }

        // =========================================================
        // DETALLE
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Detalle(int id)
        {
            if (id <= 0)
                return NotFound();

            var model = await ConstruirDetalleAsync(id);

            if (model == null)
                return NotFound();

            return View(model);
        }

        private static async Task<long> CrearEntregaScrapDesdeGP12Async(int solicitudGP12ID, int inspeccionGP12ID, decimal cantidadScrap, string? observaciones, int usuarioID, SqlConnection cn, SqlTransaction tx)
        {
            if (solicitudGP12ID <= 0) throw new InvalidOperationException("La solicitud GP12 no es válida.");
            if (inspeccionGP12ID <= 0) throw new InvalidOperationException("La inspección GP12 no es válida.");
            if (cantidadScrap <= 0) throw new InvalidOperationException("La cantidad de scrap debe ser mayor que cero.");
            const string sqlExistente = @"
SELECT TOP(1)
    ScrapEntregaID,
    CantidadScrap,
    Estado
FROM dbo.Calidad_ScrapEntregas WITH(UPDLOCK,HOLDLOCK)
WHERE Origen=N'GP12'
  AND GP12InspeccionID=@GP12InspeccionID
  AND Activo=1
  AND Estado<>N'CANCELADO'
ORDER BY ScrapEntregaID DESC;";
            await using (var cmd = new SqlCommand(sqlExistente, cn, tx))
            {
                cmd.Parameters.Add("@GP12InspeccionID", SqlDbType.Int).Value = inspeccionGP12ID;
                await using var rd = await cmd.ExecuteReaderAsync();
                if (await rd.ReadAsync())
                {
                    var scrapEntregaID = Convert.ToInt64(rd["ScrapEntregaID"]);
                    var cantidadExistente = Convert.ToDecimal(rd["CantidadScrap"]);
                    if (cantidadExistente != cantidadScrap) throw new InvalidOperationException($"La inspección GP12 ya tiene una entrega de scrap por {cantidadExistente:N4} pieza(s), diferente a las {cantidadScrap:N4} capturadas.");
                    return scrapEntregaID;
                }
            }
            const string sqlInsert = @"
INSERT INTO dbo.Calidad_ScrapEntregas
(
    InspeccionID,
    DisposicionID,
    EjecucionProduccionID,
    ProgramaProduccionID,
    SolicitudProduccionID,
    SolicitudProduccionDetalleID,
    ReleaseID,
    ReleaseDetalleID,
    ParteID,
    NumeroParte,
    OrdenFabricacion,
    CantidadScrap,
    Estado,
    Observaciones,
    UsuarioCreacionID,
    FechaCreacion,
    Activo,
    Origen,
    GP12SolicitudID,
    GP12InspeccionID,
    CajaProduccionID
)
SELECT
    s.CalidadInspeccionID,
    NULL,
    s.EjecucionProduccionID,
    s.ProgramaProduccionID,
    s.SolicitudProduccionID,
    s.SolicitudProduccionDetalleID,
    NULL,
    NULL,
    s.ParteID,
    s.NumeroParte,
    s.OrdenFabricacion,
    @CantidadScrap,
    N'PENDIENTE_ENTREGA_GP12',
    @Observaciones,
    @UsuarioID,
    SYSDATETIME(),
    1,
    N'GP12',
    s.SolicitudGP12ID,
    @GP12InspeccionID,
    s.CajaProduccionID
FROM dbo.GP12_Solicitudes s WITH(UPDLOCK,HOLDLOCK)
WHERE s.SolicitudGP12ID=@SolicitudGP12ID
  AND s.Activo=1;
IF @@ROWCOUNT<>1
    THROW 51310,'No fue posible relacionar el scrap con la solicitud GP12.',1;
SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";
            var comentario = $"Scrap determinado por GP12. Solicitud GP12: {solicitudGP12ID}. Inspección GP12: {inspeccionGP12ID}. Cantidad: {cantidadScrap:N4}.";
            if (!string.IsNullOrWhiteSpace(observaciones)) comentario += " " + observaciones.Trim();
            if (comentario.Length > 1000) comentario = comentario[..1000];
            await using var cmdInsert = new SqlCommand(sqlInsert, cn, tx);
            cmdInsert.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value = solicitudGP12ID;
            cmdInsert.Parameters.Add("@GP12InspeccionID", SqlDbType.Int).Value = inspeccionGP12ID;
            var parametroCantidad = cmdInsert.Parameters.Add("@CantidadScrap", SqlDbType.Decimal);
            parametroCantidad.Precision = 18;
            parametroCantidad.Scale = 4;
            parametroCantidad.Value = cantidadScrap;
            cmdInsert.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = comentario;
            cmdInsert.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioID;
            var resultado = await cmdInsert.ExecuteScalarAsync();
            if (resultado == null || resultado == DBNull.Value) throw new InvalidOperationException("No fue posible generar la entrega de scrap desde GP12.");
            return Convert.ToInt64(resultado);
        }

        // NSQ_GP12_CAJAS_REPORTADAS_CALIDAD_V1_RECEIVE
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecibirCajaReportada(int solicitudGP12ID)
        {
            if (solicitudGP12ID <= 0)
                return NotFound();

            string? codigo = null;

            await using (var cn = new SqlConnection(ConnectionString))
            {
                await cn.OpenAsync();

                const string sql = @"
SELECT TOP(1)
    pc.CodigoBarrasOrigen
FROM dbo.GP12_Solicitudes s
INNER JOIN dbo.Produccion_Cajas pc
    ON pc.CajaProduccionID=s.CajaProduccionID
WHERE s.SolicitudGP12ID=@SolicitudGP12ID
  AND s.Activo=1
  AND pc.Activo=1
  AND s.CajaProduccionID IS NOT NULL
  AND UPPER(LTRIM(RTRIM(ISNULL(s.Origen,N''))))=N'CALIDAD';";

                await using var cmd = new SqlCommand(sql, cn);
                cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value =
                    solicitudGP12ID;

                var value = await cmd.ExecuteScalarAsync();
                codigo = value == null || value == DBNull.Value
                    ? null
                    : value.ToString()?.Trim();
            }

            if (string.IsNullOrWhiteSpace(codigo))
            {
                TempData["Error"] =
                    "La caja reportada por Calidad no tiene un código de origen asociado.";
                return RedirectToAction(nameof(Detalle), new { id = solicitudGP12ID });
            }

            return await RecibirCajaEscaneada(
                new GP12RecepcionEscaneoViewModel
                {
                    SolicitudGP12ID = solicitudGP12ID,
                    CodigoBarras = codigo
                });
        }

        private static async Task RegistrarDescuentoBonusScrapGP12Async(int solicitudGP12ID, int inspeccionGP12ID, decimal cantidadScrap, int usuarioID, SqlConnection cn, SqlTransaction tx)
        {
            if (solicitudGP12ID <= 0) throw new InvalidOperationException("La solicitud GP12 no es válida para afectar el bonus.");
            if (inspeccionGP12ID <= 0) throw new InvalidOperationException("La inspección GP12 no es válida para afectar el bonus.");
            if (cantidadScrap <= 0) return;
            const string sqlSolicitud = @"
SELECT
    s.CajaProduccionID,
    s.Origen,
    ISNULL(pc.CantidadPiezas,ISNULL(pc.Cantidad,0)) AS CantidadCaja
FROM dbo.GP12_Solicitudes s WITH(UPDLOCK,HOLDLOCK)
LEFT JOIN dbo.Produccion_Cajas pc WITH(UPDLOCK,HOLDLOCK)
    ON pc.CajaProduccionID=s.CajaProduccionID
   AND pc.Activo=1
WHERE s.SolicitudGP12ID=@SolicitudGP12ID
  AND s.Activo=1;";
            long? cajaProduccionID;
            string origen;
            int cantidadCaja;
            await using (var cmd = new SqlCommand(sqlSolicitud, cn, tx))
            {
                cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value = solicitudGP12ID;
                await using var rd = await cmd.ExecuteReaderAsync();
                if (!await rd.ReadAsync()) throw new InvalidOperationException("No se encontró la solicitud GP12 para relacionar el scrap con el bonus.");
                cajaProduccionID = rd["CajaProduccionID"] == DBNull.Value ? null : Convert.ToInt64(rd["CajaProduccionID"]);
                origen = rd["Origen"] == DBNull.Value ? string.Empty : rd["Origen"]?.ToString()?.Trim().ToUpperInvariant() ?? string.Empty;
                cantidadCaja = rd["CantidadCaja"] == DBNull.Value ? 0 : Convert.ToInt32(rd["CantidadCaja"]);
            }
            if (!cajaProduccionID.HasValue || cajaProduccionID.Value <= 0) return;
            if (cantidadScrap != decimal.Truncate(cantidadScrap)) throw new InvalidOperationException("El scrap de una caja física de Producción debe expresarse en piezas completas para afectar el bonus.");
            var piezasScrap = decimal.ToInt32(cantidadScrap);
            if (piezasScrap <= 0) return;
            if (cantidadCaja <= 0) throw new InvalidOperationException("La caja relacionada con GP12 no tiene una cantidad física válida.");
            if (piezasScrap > cantidadCaja) throw new InvalidOperationException($"GP12 intenta descontar {piezasScrap:N0} pieza(s), pero la caja solamente contiene {cantidadCaja:N0}.");
            var prefijoReferencia = $"GP12_INSPECCION:{inspeccionGP12ID}:SCRAP:";
            const string sqlExistente = @"
SELECT ISNULL(SUM(-CONVERT(BIGINT,PiezasMovimiento)),0)
FROM dbo.Produccion_BonusOperadorMovimientos WITH(UPDLOCK,HOLDLOCK)
WHERE TipoMovimiento=@TipoMovimiento
  AND ReferenciaEvento LIKE @Prefijo+N'%'
  AND PiezasMovimiento<0
  AND Activo=1;";
            long descuentoExistente;
            await using (var cmd = new SqlCommand(sqlExistente, cn, tx))
            {
                cmd.Parameters.Add("@TipoMovimiento", SqlDbType.NVarChar, 120).Value = ProduccionTipoMovimientoBonus.ScrapConfirmadoGP12;
                cmd.Parameters.Add("@Prefijo", SqlDbType.NVarChar, 400).Value = prefijoReferencia;
                var value = await cmd.ExecuteScalarAsync();
                descuentoExistente = value == null || value == DBNull.Value ? 0L : Convert.ToInt64(value);
            }
            if (descuentoExistente == piezasScrap) return;
            if (descuentoExistente > 0) throw new InvalidOperationException($"La inspección GP12 {inspeccionGP12ID} ya tiene {descuentoExistente:N0} pieza(s) descontadas del bonus, pero ahora intenta registrar {piezasScrap:N0}. Se evitó duplicar o alterar un movimiento previamente aplicado.");
            const string sqlTrazabilidad = @"
SELECT
    d.EjecucionProduccionID,
    d.RegistroHoraID,
    d.OperadorID,
    d.CantidadPiezas,
    rh.FechaProduccion,
    rh.HoraInicio,
    rh.HoraFin,
    ISNULL
    (
        (
            SELECT SUM(CONVERT(BIGINT,m.PiezasMovimiento))
            FROM dbo.Produccion_BonusOperadorMovimientos m WITH(UPDLOCK,HOLDLOCK)
            WHERE m.RegistroHoraID=d.RegistroHoraID
              AND m.OperadorID=d.OperadorID
              AND m.Activo=1
        ),
        0
    ) AS SaldoBonus
FROM dbo.Produccion_CajaRegistroHoraDetalle d WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Produccion_RegistroHora rh WITH(UPDLOCK,HOLDLOCK)
    ON rh.RegistroHoraID=d.RegistroHoraID
   AND rh.EjecucionProduccionID=d.EjecucionProduccionID
   AND rh.Activo=1
WHERE d.CajaProduccionID=@CajaProduccionID
  AND d.Activo=1
ORDER BY d.CajaRegistroHoraDetalleID;";
            var origenes = new List<GP12BonusOrigenHoraData>();
            await using (var cmd = new SqlCommand(sqlTrazabilidad, cn, tx))
            {
                cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = cajaProduccionID.Value;
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    var cantidadOrigen = Convert.ToInt32(rd["CantidadPiezas"]);
                    var saldo = Convert.ToInt64(rd["SaldoBonus"]);
                    origenes.Add(new GP12BonusOrigenHoraData
                    {
                        EjecucionProduccionID = Convert.ToInt32(rd["EjecucionProduccionID"]),
                        RegistroHoraID = Convert.ToInt32(rd["RegistroHoraID"]),
                        OperadorID = Convert.ToInt32(rd["OperadorID"]),
                        CantidadCaja = cantidadOrigen,
                        SaldoBonus = saldo,
                        FechaProduccion = Convert.ToDateTime(rd["FechaProduccion"]),
                        HoraInicio = rd["HoraInicio"] == DBNull.Value ? TimeSpan.Zero : (TimeSpan)rd["HoraInicio"],
                        HoraFin = rd["HoraFin"] == DBNull.Value ? TimeSpan.Zero : (TimeSpan)rd["HoraFin"],
                        CapacidadDescuento = (int)Math.Min(cantidadOrigen, Math.Max(0L, saldo))
                    });
                }
            }
            if (origenes.Count == 0) throw new InvalidOperationException($"La caja {cajaProduccionID.Value} no conserva trazabilidad hacia Produccion_RegistroHora. GP12 no puede determinar qué operador debe recibir el descuento.");
            var totalTrazado = origenes.Sum(x => x.CantidadCaja);
            if (totalTrazado != cantidadCaja) throw new InvalidOperationException($"La trazabilidad de la caja {cajaProduccionID.Value} no coincide con su cantidad física. Caja: {cantidadCaja:N0}; trazado: {totalTrazado:N0}.");
            var capacidadTotal = origenes.Sum(x => (long)x.CapacidadDescuento);
            if (capacidadTotal < piezasScrap) throw new InvalidOperationException($"GP12 confirmó {piezasScrap:N0} pieza(s) scrap, pero los registros horarios de la caja solamente conservan {capacidadTotal:N0} pieza(s) disponibles en el bonus. Se evitó un descuento doble.");
            foreach (var item in origenes)
            {
                var cuotaExacta = (decimal)piezasScrap * item.CantidadCaja / totalTrazado;
                var baseAsignacion = (int)Math.Floor(cuotaExacta);
                item.ScrapAsignado = Math.Min(baseAsignacion, item.CapacidadDescuento);
                item.Fraccion = cuotaExacta - Math.Floor(cuotaExacta);
            }
            var pendiente = piezasScrap - origenes.Sum(x => x.ScrapAsignado);
            while (pendiente > 0)
            {
                var asigno = false;
                foreach (var item in origenes.OrderByDescending(x => x.Fraccion).ThenBy(x => x.RegistroHoraID))
                {
                    if (pendiente <= 0) break;
                    if (item.ScrapAsignado >= item.CapacidadDescuento) continue;
                    item.ScrapAsignado++;
                    pendiente--;
                    asigno = true;
                }
                if (!asigno) throw new InvalidOperationException("No existe saldo suficiente en los registros horarios relacionados con la caja para distribuir todo el scrap confirmado por GP12.");
            }
            foreach (var item in origenes.Where(x => x.ScrapAsignado > 0))
            {
                var referenciaEvento = $"{prefijoReferencia}REGISTRO:{item.RegistroHoraID}";
                var motivo = $"GP12 confirmó {item.ScrapAsignado:N0} pieza(s) scrap correspondientes a la caja {cajaProduccionID.Value}, solicitud GP12 {solicitudGP12ID}, inspección GP12 {inspeccionGP12ID}. El RegistroHoraID {item.RegistroHoraID} aportó {item.CantidadCaja:N0} pieza(s) a la caja.";
                if (!string.IsNullOrWhiteSpace(origen)) motivo += $" Origen GP12: {origen}.";
                if (motivo.Length > 2000) motivo = motivo[..2000];
                var inicioBloque = item.FechaProduccion.Date.Add(item.HoraInicio);
                var fechaMovimiento = item.FechaProduccion.Date.Add(item.HoraFin);
                if (fechaMovimiento <= inicioBloque) fechaMovimiento = fechaMovimiento.AddDays(1);
                const string sqlInsert = @"
INSERT INTO dbo.Produccion_BonusOperadorMovimientos
(
    OperadorID,EjecucionProduccionID,RegistroHoraID,MonitoreoID,DisposicionID,
    TipoMovimiento,PiezasMovimiento,PiezasReferencia,Motivo,ReferenciaEvento,
    UsuarioCreacionID,FechaMovimiento,Activo
)
SELECT
    @OperadorID,@EjecucionProduccionID,@RegistroHoraID,NULL,NULL,
    @TipoMovimiento,@PiezasMovimiento,@PiezasReferencia,@Motivo,@ReferenciaEvento,
    @UsuarioID,@FechaMovimiento,1
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.Produccion_BonusOperadorMovimientos WITH(UPDLOCK,HOLDLOCK)
    WHERE ReferenciaEvento=@ReferenciaEvento
      AND Activo=1
);";
                await using var cmd = new SqlCommand(sqlInsert, cn, tx);
                cmd.Parameters.Add("@OperadorID", SqlDbType.Int).Value = item.OperadorID;
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = item.EjecucionProduccionID;
                cmd.Parameters.Add("@RegistroHoraID", SqlDbType.Int).Value = item.RegistroHoraID;
                cmd.Parameters.Add("@TipoMovimiento", SqlDbType.NVarChar, 120).Value = ProduccionTipoMovimientoBonus.ScrapConfirmadoGP12;
                cmd.Parameters.Add("@PiezasMovimiento", SqlDbType.Int).Value = -item.ScrapAsignado;
                cmd.Parameters.Add("@PiezasReferencia", SqlDbType.Int).Value = item.CantidadCaja;
                cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 2000).Value = motivo;
                cmd.Parameters.Add("@ReferenciaEvento", SqlDbType.NVarChar, 400).Value = referenciaEvento;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioID;
                cmd.Parameters.Add("@FechaMovimiento", SqlDbType.DateTime2).Value = fechaMovimiento;
                await cmd.ExecuteNonQueryAsync();
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecibirCajaEscaneada(GP12RecepcionEscaneoViewModel model)
        {
            var codigo = model.CodigoBarras?.Trim() ?? string.Empty;

            if (model.SolicitudGP12ID <= 0)
            {
                TempData["Error"] = "La solicitud GP12 no es válida.";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(codigo))
            {
                TempData["Error"] = "Escanea la etiqueta física de la caja.";
                return RedirectToAction(nameof(Detalle), new { id = model.SolicitudGP12ID });
            }

            if (codigo.Length > 500)
            {
                TempData["Error"] = "El código escaneado excede la longitud permitida.";
                return RedirectToAction(nameof(Detalle), new { id = model.SolicitudGP12ID });
            }

            var usuarioID = ObtenerUsuarioIdActual();
            if (!usuarioID.HasValue || usuarioID.Value <= 0)
                return Unauthorized();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                const string sqlSolicitud = @"
SELECT TOP(1)
    s.SolicitudGP12ID,
    s.CajaProduccionID,
    s.CalidadInspeccionID,
    s.Origen,
    s.OrdenFabricacion,
    s.NumeroParte,
    s.EstatusID,
    ISNULL(s.CantidadSolicitada,0) AS CantidadSolicitada,
    ISNULL(s.CantidadRecibida,0) AS CantidadRecibida,
    ISNULL(s.CantidadProcesada,0) AS CantidadProcesada,
    s.FechaRecepcion,
    pc.CodigoBarrasOrigen,
    pc.NumeroOFEtiqueta,
    pc.NumeroParteEtiqueta,
    pc.CantidadEtiqueta,
    ISNULL(pc.CantidadPiezas,ISNULL(pc.Cantidad,0)) AS CantidadCaja,
    pc.FechaEscaneoCalidad,
    ISNULL(pc.EstadoCajaID,1) AS EstadoCajaID,
    COALESCE(
        NULLIF(pc.FolioCaja,N''),
        NULLIF(pc.Etiqueta,N''),
        CONVERT(NVARCHAR(100),pc.CajaProduccionID)
    ) AS FolioCaja
FROM dbo.GP12_Solicitudes s WITH(UPDLOCK,HOLDLOCK)
LEFT JOIN dbo.Produccion_Cajas pc WITH(UPDLOCK,HOLDLOCK)
    ON pc.CajaProduccionID=s.CajaProduccionID
   AND pc.Activo=1
WHERE s.SolicitudGP12ID=@SolicitudGP12ID
  AND s.Activo=1;";

                long? cajaProduccionID;
                int estatusAnterior;
                decimal cantidadSolicitada;
                decimal cantidadRecibida;
                decimal cantidadProcesada;
                DateTime? fechaRecepcion;
                string? codigoEsperado;
                string? ordenFabricacion;
                string? numeroParte;
                string? numeroOFEtiqueta;
                string? numeroParteEtiqueta;
                decimal cantidadCaja;
                int? cantidadEtiqueta;
                DateTime? fechaEscaneoCalidad;
                int estadoCajaID;
                string folioCaja;

                await using (var cmd = new SqlCommand(sqlSolicitud, cn, tx))
                {
                    cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value =
                        model.SolicitudGP12ID;

                    await using var rd = await cmd.ExecuteReaderAsync();

                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();
                        return NotFound();
                    }

                    cajaProduccionID =
                        rd["CajaProduccionID"] == DBNull.Value
                            ? null
                            : Convert.ToInt64(rd["CajaProduccionID"]);

                    estatusAnterior =
                        Convert.ToInt32(rd["EstatusID"]);

                    cantidadSolicitada =
                        Convert.ToDecimal(rd["CantidadSolicitada"]);

                    cantidadRecibida =
                        Convert.ToDecimal(rd["CantidadRecibida"]);

                    cantidadProcesada =
                        Convert.ToDecimal(rd["CantidadProcesada"]);

                    fechaRecepcion =
                        rd["FechaRecepcion"] == DBNull.Value
                            ? null
                            : Convert.ToDateTime(rd["FechaRecepcion"]);

                    codigoEsperado =
                        rd["CodigoBarrasOrigen"] == DBNull.Value
                            ? null
                            : rd["CodigoBarrasOrigen"]?.ToString()?.Trim();

                    ordenFabricacion =
                        rd["OrdenFabricacion"] == DBNull.Value
                            ? null
                            : rd["OrdenFabricacion"]?.ToString()?.Trim();

                    numeroParte =
                        rd["NumeroParte"] == DBNull.Value
                            ? null
                            : rd["NumeroParte"]?.ToString()?.Trim();

                    numeroOFEtiqueta =
                        rd["NumeroOFEtiqueta"] == DBNull.Value
                            ? null
                            : rd["NumeroOFEtiqueta"]?.ToString()?.Trim();

                    numeroParteEtiqueta =
                        rd["NumeroParteEtiqueta"] == DBNull.Value
                            ? null
                            : rd["NumeroParteEtiqueta"]?.ToString()?.Trim();

                    cantidadCaja =
                        Convert.ToDecimal(rd["CantidadCaja"]);

                    cantidadEtiqueta =
                        rd["CantidadEtiqueta"] == DBNull.Value
                            ? null
                            : Convert.ToInt32(rd["CantidadEtiqueta"]);

                    fechaEscaneoCalidad =
                        rd["FechaEscaneoCalidad"] == DBNull.Value
                            ? null
                            : Convert.ToDateTime(rd["FechaEscaneoCalidad"]);

                    estadoCajaID =
                        Convert.ToInt32(rd["EstadoCajaID"]);

                    folioCaja =
                        rd["FolioCaja"]?.ToString()?.Trim() ?? string.Empty;
                }

                if (!cajaProduccionID.HasValue || cajaProduccionID.Value <= 0)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] =
                        "Esta solicitud GP12 no proviene de una caja física. Utiliza la recepción manual.";
                    return RedirectToAction(nameof(Detalle), new { id = model.SolicitudGP12ID });
                }

                if (GP12Estatus.EsFinal(estatusAnterior))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] =
                        "La solicitud GP12 está cerrada o cancelada y ya no puede recibir material.";
                    return RedirectToAction(nameof(Detalle), new { id = model.SolicitudGP12ID });
                }

                if (string.IsNullOrWhiteSpace(codigoEsperado))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] =
                        "La caja relacionada no tiene un código de barras físico registrado.";
                    return RedirectToAction(nameof(Detalle), new { id = model.SolicitudGP12ID });
                }

                if (!string.Equals(codigoEsperado, codigo, StringComparison.Ordinal))
                {
                    const string sqlOtraCaja = @"
SELECT TOP(1)
    s.SolicitudGP12ID,
    s.OrdenFabricacion,
    s.NumeroParte,
    COALESCE(
        NULLIF(pc.FolioCaja,N''),
        NULLIF(pc.Etiqueta,N''),
        CONVERT(NVARCHAR(100),pc.CajaProduccionID)
    ) AS FolioCaja
FROM dbo.Produccion_Cajas pc
INNER JOIN dbo.GP12_Solicitudes s
    ON s.CajaProduccionID=pc.CajaProduccionID
   AND s.Activo=1
WHERE pc.Activo=1
  AND pc.CodigoBarrasOrigen=@CodigoBarras
ORDER BY s.SolicitudGP12ID DESC;";

                    int? solicitudCorrectaID = null;
                    string? ofCorrecta = null;
                    string? parteCorrecta = null;
                    string? folioCorrecto = null;

                    await using (var cmd = new SqlCommand(sqlOtraCaja, cn, tx))
                    {
                        cmd.Parameters.Add("@CodigoBarras", SqlDbType.NVarChar, 500).Value =
                            codigo;

                        await using var rd = await cmd.ExecuteReaderAsync();

                        if (await rd.ReadAsync())
                        {
                            solicitudCorrectaID =
                                Convert.ToInt32(rd["SolicitudGP12ID"]);

                            ofCorrecta =
                                rd["OrdenFabricacion"] == DBNull.Value
                                    ? null
                                    : rd["OrdenFabricacion"]?.ToString()?.Trim();

                            parteCorrecta =
                                rd["NumeroParte"] == DBNull.Value
                                    ? null
                                    : rd["NumeroParte"]?.ToString()?.Trim();

                            folioCorrecto =
                                rd["FolioCaja"] == DBNull.Value
                                    ? null
                                    : rd["FolioCaja"]?.ToString()?.Trim();
                        }
                    }

                    await tx.RollbackAsync();

                    if (solicitudCorrectaID.HasValue)
                    {
                        TempData["Error"] =
                            $"La etiqueta pertenece a otra solicitud GP12. " +
                            $"Caja: {folioCorrecto ?? "Sin folio"} · " +
                            $"OF: {ofCorrecta ?? "Sin OF"} · " +
                            $"Parte: {parteCorrecta ?? "Sin parte"}. " +
                            "No se registró la recepción.";

                        TempData["SolicitudGP12EscaneadaID"] =
                            solicitudCorrectaID.Value;
                    }
                    else
                    {
                        TempData["Error"] =
                            "La etiqueta escaneada no corresponde a la caja enviada a esta solicitud GP12.";
                    }

                    return RedirectToAction(nameof(Detalle), new { id = model.SolicitudGP12ID });
                }

                /*
                 * IMPORTANTE:
                 * Calidad es el primer departamento que realiza el escaneo físico.
                 * GP12 NO debe exigir un escaneo previo de Producción.
                 */
                if (!fechaEscaneoCalidad.HasValue)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] =
                        "La caja todavía no fue escaneada físicamente por Calidad. GP12 no puede recibirla.";
                    return RedirectToAction(nameof(Detalle), new { id = model.SolicitudGP12ID });
                }

                if (estadoCajaID != ProduccionCajaEstatus.RetenidaGp12Scrap)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] =
                        $"La caja {folioCaja} ya no se encuentra en estado de envío a GP12.";
                    return RedirectToAction(nameof(Detalle), new { id = model.SolicitudGP12ID });
                }

                if (fechaRecepcion.HasValue || cantidadRecibida > 0)
                {
                    await tx.RollbackAsync();

                    TempData["Mensaje"] =
                        fechaRecepcion.HasValue
                            ? $"La caja {folioCaja} ya había sido recibida físicamente por GP12 el {fechaRecepcion.Value:dd/MM/yyyy HH:mm}."
                            : $"La caja {folioCaja} ya tiene material recibido en GP12.";

                    return RedirectToAction(nameof(Detalle), new { id = model.SolicitudGP12ID });
                }

                if (cantidadSolicitada <= 0)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] =
                        "La solicitud GP12 no tiene una cantidad solicitada válida.";
                    return RedirectToAction(nameof(Detalle), new { id = model.SolicitudGP12ID });
                }

                if (cantidadCaja <= 0)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] =
                        "La caja no tiene una cantidad válida.";
                    return RedirectToAction(nameof(Detalle), new { id = model.SolicitudGP12ID });
                }

                if (cantidadEtiqueta.HasValue &&
                    cantidadEtiqueta.Value > 0 &&
                    Convert.ToDecimal(cantidadEtiqueta.Value) != cantidadCaja)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        $"La cantidad física de la etiqueta ({cantidadEtiqueta.Value:N0}) " +
                        $"no coincide con la cantidad registrada de la caja ({cantidadCaja:N0}).";

                    return RedirectToAction(nameof(Detalle), new { id = model.SolicitudGP12ID });
                }

                if (cantidadCaja != cantidadSolicitada)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        $"La cantidad de la caja ({cantidadCaja:N0}) no coincide con " +
                        $"la cantidad solicitada a GP12 ({cantidadSolicitada:N0}).";

                    return RedirectToAction(nameof(Detalle), new { id = model.SolicitudGP12ID });
                }

                // NSQ_GP12_OF_CANONICA_V1
                if (!string.IsNullOrWhiteSpace(numeroOFEtiqueta) &&
                    !string.IsNullOrWhiteSpace(ordenFabricacion) &&
                    !AlmacenPTCodigoBarrasService.NumerosOFEquivalentes(
                        numeroOFEtiqueta,
                        ordenFabricacion))
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        $"La OF de la etiqueta ({numeroOFEtiqueta}) no coincide con " +
                        $"la solicitud GP12 ({ordenFabricacion}).";

                    return RedirectToAction(nameof(Detalle), new { id = model.SolicitudGP12ID });
                }

                if (!string.IsNullOrWhiteSpace(numeroParteEtiqueta) &&
                    !string.IsNullOrWhiteSpace(numeroParte) &&
                    NormalizarCodigoComparacion(numeroParteEtiqueta) !=
                    NormalizarCodigoComparacion(numeroParte))
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        $"El número de parte de la etiqueta ({numeroParteEtiqueta}) " +
                        $"no coincide con la solicitud GP12 ({numeroParte}).";

                    return RedirectToAction(nameof(Detalle), new { id = model.SolicitudGP12ID });
                }

                const string sqlEtiqueta = @"
SELECT TOP(1)
    SolicitudEtiquetaID,
    TipoEtiqueta,
    ISNULL(CantidadSolicitada,0) AS CantidadSolicitada,
    ISNULL(CantidadRecibida,0) AS CantidadRecibida,
    ISNULL(CantidadProcesada,0) AS CantidadProcesada
FROM dbo.GP12_SolicitudEtiquetas WITH(UPDLOCK,HOLDLOCK)
WHERE SolicitudGP12ID=@SolicitudGP12ID
  AND Activo=1
ORDER BY
    CASE WHEN TipoEtiqueta=@Amarilla THEN 0 ELSE 1 END,
    SolicitudEtiquetaID;";

                int solicitudEtiquetaID;
                string tipoEtiqueta;
                decimal cantidadSolicitadaEtiqueta;
                decimal cantidadRecibidaEtiqueta;

                await using (var cmd = new SqlCommand(sqlEtiqueta, cn, tx))
                {
                    cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value =
                        model.SolicitudGP12ID;

                    cmd.Parameters.Add("@Amarilla", SqlDbType.VarChar, 20).Value =
                        GP12TipoEtiqueta.Amarilla;

                    await using var rd = await cmd.ExecuteReaderAsync();

                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] =
                            "La solicitud GP12 no tiene una clasificación de recepción configurada.";
                        return RedirectToAction(nameof(Detalle), new { id = model.SolicitudGP12ID });
                    }

                    solicitudEtiquetaID =
                        Convert.ToInt32(rd["SolicitudEtiquetaID"]);

                    tipoEtiqueta =
                        rd["TipoEtiqueta"]?.ToString()?.Trim()
                        ?? GP12TipoEtiqueta.SinClasificar;

                    cantidadSolicitadaEtiqueta =
                        Convert.ToDecimal(rd["CantidadSolicitada"]);

                    cantidadRecibidaEtiqueta =
                        Convert.ToDecimal(rd["CantidadRecibida"]);
                }

                if (cantidadRecibidaEtiqueta > 0)
                {
                    await tx.RollbackAsync();

                    TempData["Mensaje"] =
                        $"La caja {folioCaja} ya fue recibida anteriormente en la clasificación " +
                        $"{GP12TipoEtiqueta.Nombre(tipoEtiqueta)}.";

                    return RedirectToAction(nameof(Detalle), new { id = model.SolicitudGP12ID });
                }

                if (cantidadSolicitadaEtiqueta != cantidadCaja)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        $"La clasificación GP12 espera {cantidadSolicitadaEtiqueta:N0} pieza(s), " +
                        $"pero la caja contiene {cantidadCaja:N0}.";

                    return RedirectToAction(nameof(Detalle), new { id = model.SolicitudGP12ID });
                }

                var ahora = DateTime.Now;

                const string sqlUpdateEtiqueta = @"
UPDATE dbo.GP12_SolicitudEtiquetas
SET CantidadRecibida=@Cantidad,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=@Ahora
WHERE SolicitudEtiquetaID=@SolicitudEtiquetaID
  AND SolicitudGP12ID=@SolicitudGP12ID
  AND Activo=1
  AND ISNULL(CantidadRecibida,0)=0;

IF @@ROWCOUNT<>1
    THROW 51501,'La clasificación GP12 cambió o ya fue recibida.',1;";

                await using (var cmd = new SqlCommand(sqlUpdateEtiqueta, cn, tx))
                {
                    AgregarDecimal(cmd, "@Cantidad", cantidadCaja);

                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                        usuarioID.Value;

                    cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value =
                        ahora;

                    cmd.Parameters.Add("@SolicitudEtiquetaID", SqlDbType.Int).Value =
                        solicitudEtiquetaID;

                    cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value =
                        model.SolicitudGP12ID;

                    await cmd.ExecuteNonQueryAsync();
                }

                const string sqlMovimiento = @"
INSERT INTO dbo.GP12_InventarioMovimientos
(
    SolicitudGP12ID,
    SolicitudEtiquetaID,
    TipoMovimiento,
    Cantidad,
    CajaID,
    TarimaID,
    Referencia,
    Observaciones,
    FechaMovimiento,
    UsuarioID,
    Activo
)
VALUES
(
    @SolicitudGP12ID,
    @SolicitudEtiquetaID,
    N'ENTRADA',
    @Cantidad,
    NULL /* NSQ_GP12_CAJAID_FK_NULLABLE_V3: caja fisica de Produccion; no es GP12_Cajas.CajaID */,
    NULL,
    @Referencia,
    @Observaciones,
    @Ahora,
    @UsuarioID,
    1
);";

                await using (var cmd = new SqlCommand(sqlMovimiento, cn, tx))
                {
                    cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value =
                        model.SolicitudGP12ID;

                    cmd.Parameters.Add("@SolicitudEtiquetaID", SqlDbType.Int).Value =
                        solicitudEtiquetaID;

                    AgregarDecimal(cmd, "@Cantidad", cantidadCaja);

                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value =
                        cajaProduccionID.Value;

                    cmd.Parameters.Add("@Referencia", SqlDbType.NVarChar, 500).Value =
                        codigo;

                    cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 2000).Value =
                        $"Caja {folioCaja} recibida físicamente en GP12 mediante escaneo.";

                    cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value =
                        ahora;

                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                        usuarioID.Value;

                    await cmd.ExecuteNonQueryAsync();
                }

                var nuevaCantidadPendiente =
                    Math.Max(0, cantidadCaja - cantidadProcesada);

                var nuevoEstatus =
                    estatusAnterior < GP12Estatus.PendienteProgramar
                        ? GP12Estatus.PendienteProgramar
                        : estatusAnterior;

                const string sqlUpdateSolicitud = @"
UPDATE dbo.GP12_Solicitudes
SET CantidadRecibida=@CantidadRecibida,
    CantidadPendiente=@CantidadPendiente,
    FechaRecepcion=@Ahora,
    UsuarioRecepcionID=@UsuarioID,
    EstatusID=@EstatusID,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=@Ahora
WHERE SolicitudGP12ID=@SolicitudGP12ID
  AND Activo=1
  AND CajaProduccionID=@CajaProduccionID
  AND FechaRecepcion IS NULL
  AND ISNULL(CantidadRecibida,0)=0;

IF @@ROWCOUNT<>1
    THROW 51502,'La solicitud GP12 cambió o la caja ya fue recibida.',1;";

                await using (var cmd = new SqlCommand(sqlUpdateSolicitud, cn, tx))
                {
                    AgregarDecimal(cmd, "@CantidadRecibida", cantidadCaja);
                    AgregarDecimal(cmd, "@CantidadPendiente", nuevaCantidadPendiente);

                    cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value =
                        ahora;

                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                        usuarioID.Value;

                    cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value =
                        nuevoEstatus;

                    cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value =
                        model.SolicitudGP12ID;

                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value =
                        cajaProduccionID.Value;

                    await cmd.ExecuteNonQueryAsync();
                }

                await AgregarHistorialAsync(
                    cn,
                    tx,
                    model.SolicitudGP12ID,
                    GP12Movimientos.MaterialRecibido,
                    estatusAnterior,
                    nuevoEstatus,
                    GP12EntidadHistorial.Solicitud,
                    model.SolicitudGP12ID,
                    $"Caja {folioCaja} recibida físicamente por GP12 mediante escaneo. " +
                    $"Cantidad: {cantidadCaja:N0}. " +
                    $"Clasificación: {GP12TipoEtiqueta.Nombre(tipoEtiqueta)}. " +
                    $"OF: {ordenFabricacion ?? "Sin OF"}. " +
                    $"Parte: {numeroParte ?? "Sin parte"}.",
                    usuarioID.Value);

                await tx.CommitAsync();

                TempData["Mensaje"] =
                    $"Caja {folioCaja} recibida físicamente en GP12 con {cantidadCaja:N0} pieza(s). " +
                    "Ya está disponible para programación.";
            }
            catch (Exception ex)
            {
                try
                {
                    await tx.RollbackAsync();
                }
                catch
                {
                }

                TempData["Error"] =
                    "No fue posible recibir la caja en GP12: " + ex.Message;
            }

            return RedirectToAction(
                nameof(Detalle),
                new { id = model.SolicitudGP12ID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarRecepcion(GP12RecepcionViewModel model)
        {
            model.Referencia = Limpiar(model.Referencia);
            model.Observaciones = Limpiar(model.Observaciones);
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Revisa la información de la recepción.";
                return RedirectToAction(nameof(Detalle), new { id = model.SolicitudGP12ID });
            }
            if (model.CantidadTotal <= 0)
            {
                TempData["Error"] = "Captura al menos una cantidad de material para recibir.";
                return RedirectToAction(nameof(Detalle), new { id = model.SolicitudGP12ID });
            }
            var usuarioID = ObtenerUsuarioIdActual();
            if (!usuarioID.HasValue || usuarioID.Value <= 0) return Unauthorized();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                const string sqlSolicitud = @"
SELECT
    EstatusID,
    CajaProduccionID,
    CantidadSolicitada,
    CantidadRecibida,
    CantidadProcesada
FROM dbo.GP12_Solicitudes WITH(UPDLOCK,HOLDLOCK)
WHERE SolicitudGP12ID=@SolicitudGP12ID
  AND Activo=1;";
                int estatusAnterior;
                long? cajaProduccionID;
                decimal cantidadSolicitada;
                decimal cantidadRecibida;
                decimal cantidadProcesada;
                await using (var cmd = new SqlCommand(sqlSolicitud, cn, tx))
                {
                    cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value = model.SolicitudGP12ID;
                    await using var rd = await cmd.ExecuteReaderAsync();
                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();
                        return NotFound();
                    }
                    estatusAnterior = Convert.ToInt32(rd["EstatusID"]);
                    cajaProduccionID = rd["CajaProduccionID"] == DBNull.Value ? null : Convert.ToInt64(rd["CajaProduccionID"]);
                    cantidadSolicitada = Convert.ToDecimal(rd["CantidadSolicitada"]);
                    cantidadRecibida = Convert.ToDecimal(rd["CantidadRecibida"]);
                    cantidadProcesada = Convert.ToDecimal(rd["CantidadProcesada"]);
                }
                if (cajaProduccionID.HasValue && cajaProduccionID.Value > 0)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Esta solicitud proviene de una caja física de Producción. Debes recibirla escaneando la etiqueta de la caja; la recepción manual está deshabilitada.";
                    return RedirectToAction(nameof(Detalle), new { id = model.SolicitudGP12ID });
                }
                if (GP12Estatus.EsFinal(estatusAnterior)) throw new InvalidOperationException("La solicitud está cerrada o cancelada y ya no puede recibir material.");
                const string sqlEtiquetas = @"
SELECT
    SolicitudEtiquetaID,
    TipoEtiqueta,
    CantidadSolicitada,
    CantidadRecibida,
    CantidadProcesada
FROM dbo.GP12_SolicitudEtiquetas WITH(UPDLOCK,HOLDLOCK)
WHERE SolicitudGP12ID=@SolicitudGP12ID
  AND Activo=1;";
                var etiquetas = new List<GP12SolicitudEtiquetaData>();
                await using (var cmd = new SqlCommand(sqlEtiquetas, cn, tx))
                {
                    cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value = model.SolicitudGP12ID;
                    await using var rd = await cmd.ExecuteReaderAsync();
                    while (await rd.ReadAsync())
                    {
                        etiquetas.Add(new GP12SolicitudEtiquetaData
                        {
                            SolicitudEtiquetaID = Convert.ToInt32(rd["SolicitudEtiquetaID"]),
                            TipoEtiqueta = rd["TipoEtiqueta"] as string ?? GP12TipoEtiqueta.SinClasificar,
                            CantidadSolicitada = Convert.ToDecimal(rd["CantidadSolicitada"]),
                            CantidadRecibida = Convert.ToDecimal(rd["CantidadRecibida"]),
                            CantidadProcesada = Convert.ToDecimal(rd["CantidadProcesada"])
                        });
                    }
                }
                if (etiquetas.Count == 0) throw new InvalidOperationException("La solicitud no tiene clasificación de material configurada.");
                var capturas = new[]
                {
            new { Tipo = GP12TipoEtiqueta.Amarilla, Cantidad = model.CantidadAmarilla },
            new { Tipo = GP12TipoEtiqueta.Roja, Cantidad = model.CantidadRoja },
            new { Tipo = GP12TipoEtiqueta.SinClasificar, Cantidad = model.CantidadSinClasificar }
        };
                var totalRecepcion = 0m;
                var detalleHistorial = new List<string>();
                const string sqlUpdateEtiqueta = @"
UPDATE dbo.GP12_SolicitudEtiquetas
SET CantidadRecibida=CantidadRecibida+@Cantidad,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=SYSDATETIME()
WHERE SolicitudEtiquetaID=@SolicitudEtiquetaID
  AND SolicitudGP12ID=@SolicitudGP12ID
  AND Activo=1;";
                const string sqlMovimiento = @"
INSERT INTO dbo.GP12_InventarioMovimientos
(
    SolicitudGP12ID,SolicitudEtiquetaID,TipoMovimiento,Cantidad,CajaID,TarimaID,Referencia,Observaciones,FechaMovimiento,UsuarioID,Activo
)
VALUES
(
    @SolicitudGP12ID,@SolicitudEtiquetaID,N'ENTRADA',@Cantidad,NULL,NULL,@Referencia,@Observaciones,SYSDATETIME(),@UsuarioID,1
);";
                foreach (var captura in capturas)
                {
                    if (captura.Cantidad <= 0) continue;
                    var etiqueta = etiquetas.Find(x => string.Equals(x.TipoEtiqueta, captura.Tipo, StringComparison.OrdinalIgnoreCase));
                    if (etiqueta == null) throw new InvalidOperationException($"La solicitud no tiene una clasificación {GP12TipoEtiqueta.Nombre(captura.Tipo)} disponible para recibir.");
                    var faltante = etiqueta.CantidadSolicitada - etiqueta.CantidadRecibida;
                    if (captura.Cantidad > faltante) throw new InvalidOperationException($"La recepción de material {GP12TipoEtiqueta.Nombre(captura.Tipo)} excede lo pendiente. Pendiente: {Math.Max(0, faltante):N4}.");
                    await using (var cmd = new SqlCommand(sqlUpdateEtiqueta, cn, tx))
                    {
                        AgregarDecimal(cmd, "@Cantidad", captura.Cantidad);
                        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioID.Value;
                        cmd.Parameters.Add("@SolicitudEtiquetaID", SqlDbType.Int).Value = etiqueta.SolicitudEtiquetaID;
                        cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value = model.SolicitudGP12ID;
                        await cmd.ExecuteNonQueryAsync();
                    }
                    await using (var cmd = new SqlCommand(sqlMovimiento, cn, tx))
                    {
                        cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value = model.SolicitudGP12ID;
                        cmd.Parameters.Add("@SolicitudEtiquetaID", SqlDbType.Int).Value = etiqueta.SolicitudEtiquetaID;
                        AgregarDecimal(cmd, "@Cantidad", captura.Cantidad);
                        AgregarNullable(cmd, "@Referencia", SqlDbType.NVarChar, 500, model.Referencia);
                        AgregarNullable(cmd, "@Observaciones", SqlDbType.NVarChar, 2000, model.Observaciones);
                        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioID.Value;
                        await cmd.ExecuteNonQueryAsync();
                    }
                    totalRecepcion += captura.Cantidad;
                    detalleHistorial.Add($"{GP12TipoEtiqueta.Nombre(captura.Tipo)}: {captura.Cantidad:N4}");
                }
                var nuevaCantidadRecibida = cantidadRecibida + totalRecepcion;
                if (nuevaCantidadRecibida > cantidadSolicitada) throw new InvalidOperationException("La recepción total excede la cantidad solicitada de la solicitud GP12.");
                var nuevoEstatus = estatusAnterior < GP12Estatus.PendienteProgramar ? GP12Estatus.PendienteProgramar : estatusAnterior;
                var nuevaCantidadPendiente = Math.Max(0, nuevaCantidadRecibida - cantidadProcesada);
                const string sqlUpdate = @"
UPDATE dbo.GP12_Solicitudes
SET CantidadRecibida=@CantidadRecibida,
    CantidadPendiente=@CantidadPendiente,
    FechaRecepcion=COALESCE(FechaRecepcion,SYSDATETIME()),
    UsuarioRecepcionID=COALESCE(UsuarioRecepcionID,@UsuarioID),
    EstatusID=@EstatusID,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=SYSDATETIME()
WHERE SolicitudGP12ID=@SolicitudGP12ID
  AND Activo=1;";
                await using (var cmd = new SqlCommand(sqlUpdate, cn, tx))
                {
                    AgregarDecimal(cmd, "@CantidadRecibida", nuevaCantidadRecibida);
                    AgregarDecimal(cmd, "@CantidadPendiente", nuevaCantidadPendiente);
                    cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value = nuevoEstatus;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioID.Value;
                    cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value = model.SolicitudGP12ID;
                    await cmd.ExecuteNonQueryAsync();
                }
                await AgregarHistorialAsync(cn, tx, model.SolicitudGP12ID, GP12Movimientos.MaterialRecibido, estatusAnterior, nuevoEstatus, GP12EntidadHistorial.Solicitud, model.SolicitudGP12ID, $"Recepción de {totalRecepcion:N4} pieza(s). {string.Join(" · ", detalleHistorial)}", usuarioID.Value);
                await tx.CommitAsync();
                TempData["Mensaje"] = "Material recibido correctamente y registrado por clasificación en el inventario GP12.";
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                TempData["Error"] = "No fue posible registrar la recepción: " + ex.Message;
            }
            return RedirectToAction(nameof(Detalle), new { id = model.SolicitudGP12ID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarSalidaParaAlmacen(
        GP12SalidaAlmacenViewModel model)
        {
            model.ReferenciaAlmacen =
                Limpiar(model.ReferenciaAlmacen) ?? string.Empty;

            model.Observaciones =
                Limpiar(model.Observaciones);

            if (!ModelState.IsValid)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "La solicitud de salida hacia Almacén no es válida."
                });
            }

            if (model.Cantidad <= 0)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "La cantidad de salida debe ser mayor que cero."
                });
            }

            var usuarioID = ObtenerUsuarioIdActual();

            if (!usuarioID.HasValue || usuarioID.Value <= 0)
                return Unauthorized();

            await using var cn =
                new SqlConnection(ConnectionString);

            await cn.OpenAsync();

            await using var tx =
                (SqlTransaction)await cn.BeginTransactionAsync(
                    IsolationLevel.Serializable);

            try
            {
                const string sqlSolicitud = @"
SELECT
    s.EstatusID,
    s.Origen,
    s.CajaProduccionID,
    s.CajaLiberadaID,
    s.CalidadInspeccionID,
    ISNULL(s.CantidadRecibida,0) AS CantidadRecibida,
    ISNULL(s.CantidadProcesada,0) AS CantidadProcesada,
    ISNULL
    (
        (
            SELECT SUM
            (
                CASE
                    WHEN m.TipoMovimiento IN(N'ENTRADA',N'AJUSTE_ENTRADA')
                        THEN ISNULL(m.Cantidad,0)
                    WHEN m.TipoMovimiento IN(N'SALIDA',N'AJUSTE_SALIDA')
                        THEN -ISNULL(m.Cantidad,0)
                    ELSE 0
                END
            )
            FROM dbo.GP12_InventarioMovimientos m WITH(UPDLOCK,HOLDLOCK)
            WHERE m.SolicitudGP12ID=s.SolicitudGP12ID
              AND m.Activo=1
        ),
        0
    ) AS SaldoInventario,
    (
        SELECT COUNT(1)
        FROM dbo.GP12_Inspecciones ia WITH(UPDLOCK,HOLDLOCK)
        WHERE ia.SolicitudGP12ID=s.SolicitudGP12ID
          AND ia.Activo=1
          AND ia.FechaFin IS NULL
    ) AS InspeccionesAbiertas,
    ultima.CantidadNOK AS UltimaCantidadNOK,
    ultima.CantidadScrap AS UltimaCantidadScrap,
    ultima.InspeccionGP12ID AS UltimaInspeccionGP12ID,
    etiqueta.SolicitudEtiquetaID
FROM dbo.GP12_Solicitudes s WITH(UPDLOCK,HOLDLOCK)
OUTER APPLY
(
    SELECT TOP(1)
        i.InspeccionGP12ID,
        ISNULL(i.CantidadNOK,0) AS CantidadNOK,
        ISNULL(i.CantidadScrap,0) AS CantidadScrap
    FROM dbo.GP12_Inspecciones i WITH(UPDLOCK,HOLDLOCK)
    WHERE i.SolicitudGP12ID=s.SolicitudGP12ID
      AND i.Activo=1
      AND i.FechaFin IS NOT NULL
    ORDER BY
        i.FechaFin DESC,
        i.InspeccionGP12ID DESC
) ultima
OUTER APPLY
(
    SELECT
        CASE
            WHEN COUNT(1)=1
                THEN MAX(se.SolicitudEtiquetaID)
            ELSE NULL
        END AS SolicitudEtiquetaID
    FROM dbo.GP12_SolicitudEtiquetas se WITH(UPDLOCK,HOLDLOCK)
    WHERE se.SolicitudGP12ID=s.SolicitudGP12ID
      AND se.Activo=1
) etiqueta
WHERE s.SolicitudGP12ID=@SolicitudGP12ID
  AND s.Activo=1;";

                int estatusAnterior;
                string origen;
                long? cajaProduccionID;
                int? cajaLiberadaID;
                int? calidadInspeccionID;
                decimal cantidadRecibida;
                decimal cantidadProcesada;
                decimal saldoInventario;
                int inspeccionesAbiertas;
                decimal? ultimaCantidadNOK;
                decimal? ultimaCantidadScrap;
                int? ultimaInspeccionGP12ID;
                int? solicitudEtiquetaID;

                await using (var cmd = new SqlCommand(sqlSolicitud, cn, tx))
                {
                    cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value =
                        model.SolicitudGP12ID;

                    await using var rd = await cmd.ExecuteReaderAsync();

                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();

                        return NotFound(new
                        {
                            ok = false,
                            mensaje = "No se encontró la solicitud GP12."
                        });
                    }

                    estatusAnterior =
                        Convert.ToInt32(rd["EstatusID"]);

                    origen =
                        rd["Origen"] == DBNull.Value
                            ? string.Empty
                            : rd["Origen"]?.ToString()?.Trim().ToUpperInvariant()
                              ?? string.Empty;

                    cajaProduccionID =
                        rd["CajaProduccionID"] == DBNull.Value
                            ? null
                            : Convert.ToInt64(rd["CajaProduccionID"]);

                    cajaLiberadaID =
                        LeerIntNullable(rd["CajaLiberadaID"]);

                    calidadInspeccionID =
                        LeerIntNullable(rd["CalidadInspeccionID"]);

                    cantidadRecibida =
                        Convert.ToDecimal(rd["CantidadRecibida"]);

                    cantidadProcesada =
                        Convert.ToDecimal(rd["CantidadProcesada"]);

                    saldoInventario =
                        Convert.ToDecimal(rd["SaldoInventario"]);

                    inspeccionesAbiertas =
                        Convert.ToInt32(rd["InspeccionesAbiertas"]);

                    ultimaCantidadNOK =
                        rd["UltimaCantidadNOK"] == DBNull.Value
                            ? null
                            : Convert.ToDecimal(rd["UltimaCantidadNOK"]);

                    ultimaCantidadScrap =
                        rd["UltimaCantidadScrap"] == DBNull.Value
                            ? null
                            : Convert.ToDecimal(rd["UltimaCantidadScrap"]);

                    ultimaInspeccionGP12ID =
                        LeerIntNullable(rd["UltimaInspeccionGP12ID"]);

                    solicitudEtiquetaID =
                        LeerIntNullable(rd["SolicitudEtiquetaID"]);
                }

                if (estatusAnterior == GP12Estatus.Cancelado ||
                    estatusAnterior == GP12Estatus.Cerrado)
                {
                    throw new InvalidOperationException(
                        "La solicitud GP12 ya está cerrada o cancelada.");
                }

                if (estatusAnterior != GP12Estatus.InspeccionTerminada &&
                    estatusAnterior != GP12Estatus.EnTarima &&
                    estatusAnterior != GP12Estatus.SalidaRegistrada)
                {
                    throw new InvalidOperationException(
                        "El material todavía no se encuentra listo para una salida hacia Almacén.");
                }

                if (cantidadRecibida <= 0 ||
                    cantidadProcesada < cantidadRecibida)
                {
                    throw new InvalidOperationException(
                        "Todavía existe material recibido pendiente de procesar en GP12.");
                }

                if (inspeccionesAbiertas > 0)
                {
                    throw new InvalidOperationException(
                        "Existe una inspección GP12 abierta. Debe finalizarse antes de registrar la salida.");
                }

                if (!ultimaInspeccionGP12ID.HasValue)
                {
                    throw new InvalidOperationException(
                        "No existe una inspección GP12 terminada que respalde la salida.");
                }

                if ((ultimaCantidadNOK ?? 0) > 0 ||
                    (ultimaCantidadScrap ?? 0) > 0)
                {
                    throw new InvalidOperationException(
                        "La última inspección GP12 conserva material NOK o scrap y no está disponible para Almacén.");
                }

                if (saldoInventario <= 0)
                {
                    throw new InvalidOperationException(
                        "La solicitud ya no tiene existencia disponible en GP12.");
                }

                if (model.Cantidad > saldoInventario)
                {
                    throw new InvalidOperationException(
                        $"La salida solicitada excede el inventario GP12 disponible. " +
                        $"Disponible: {saldoInventario:N4}.");
                }

                var ahora = DateTime.Now;
                var saldoRestante = saldoInventario - model.Cantidad;
                var salidaCompleta = saldoRestante <= 0;

                /*
                 * Mientras todavía exista saldo, GP12 permanece en SalidaRegistrada.
                 * Cuando sale todo el material, la solicitud GP12 queda Cerrada.
                 */
                var nuevoEstatus =
                    salidaCompleta
                        ? GP12Estatus.Cerrado
                        : GP12Estatus.SalidaRegistrada;

                const string sqlMovimiento = @"
INSERT INTO dbo.GP12_InventarioMovimientos
(
    SolicitudGP12ID,
    SolicitudEtiquetaID,
    TipoMovimiento,
    Cantidad,
    CajaID,
    TarimaID,
    Referencia,
    Observaciones,
    FechaMovimiento,
    UsuarioID,
    Activo
)
VALUES
(
    @SolicitudGP12ID,
    @SolicitudEtiquetaID,
    @TipoMovimiento,
    @Cantidad,
    NULL /* NSQ_GP12_CAJAID_FK_NULLABLE_V3: salida ligada a solicitud/caja Produccion; no es GP12_Cajas.CajaID */,
    NULL,
    @Referencia,
    @Observaciones,
    @Ahora,
    @UsuarioID,
    1
);

SELECT CAST(SCOPE_IDENTITY() AS INT);";

                int movimientoID;

                await using (var cmd = new SqlCommand(sqlMovimiento, cn, tx))
                {
                    cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value =
                        model.SolicitudGP12ID;

                    cmd.Parameters.Add("@SolicitudEtiquetaID", SqlDbType.Int).Value =
                        (object?)solicitudEtiquetaID ?? DBNull.Value;

                    cmd.Parameters.Add("@TipoMovimiento", SqlDbType.NVarChar, 20).Value =
                        GP12TipoMovimiento.Salida;

                    AgregarDecimal(
                        cmd,
                        "@Cantidad",
                        model.Cantidad);

                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value =
                        (object?)cajaProduccionID ?? DBNull.Value;

                    cmd.Parameters.Add("@Referencia", SqlDbType.NVarChar, 250).Value =
                        model.ReferenciaAlmacen;

                    cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value =
                        string.IsNullOrWhiteSpace(model.Observaciones)
                            ? "Salida de inventario GP12 con destino final Almacén PT."
                            : model.Observaciones;

                    cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value =
                        ahora;

                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                        usuarioID.Value;

                    movimientoID =
                        Convert.ToInt32(
                            await cmd.ExecuteScalarAsync());
                }

                const string sqlUpdateSolicitud = @"
UPDATE dbo.GP12_Solicitudes
SET
    EstatusID=@EstatusID,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=@Ahora
WHERE SolicitudGP12ID=@SolicitudGP12ID
  AND Activo=1;

IF @@ROWCOUNT<>1
    THROW 51520,'La solicitud GP12 cambió mientras se registraba la salida.',1;";

                await using (var cmd = new SqlCommand(sqlUpdateSolicitud, cn, tx))
                {
                    cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value =
                        nuevoEstatus;

                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                        usuarioID.Value;

                    cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value =
                        ahora;

                    cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value =
                        model.SolicitudGP12ID;

                    await cmd.ExecuteNonQueryAsync();
                }

                /*
                 * Si esta solicitud nació desde Calidad y está ligada a una caja
                 * física, únicamente cuando GP12 ya no conserva saldo se devuelve
                 * la caja al flujo normal de Producción.
                 *
                 * NO marcamos SalidaProduccion aquí.
                 * Ese estado lo generará Producción mediante su escaneo físico.
                 */
                var cajaDevueltaAProduccion = false;

                if (salidaCompleta &&
                    cajaProduccionID.HasValue &&
                    cajaProduccionID.Value > 0 &&
                    string.Equals(
                        origen,
                        GP12Origen.Calidad,
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (!cajaLiberadaID.HasValue ||
                        cajaLiberadaID.Value <= 0)
                    {
                        throw new InvalidOperationException(
                            "La solicitud GP12 proviene de Calidad, pero no conserva la relación con Calidad_CajasLiberadas.");
                    }

                    if (!calidadInspeccionID.HasValue ||
                        calidadInspeccionID.Value <= 0)
                    {
                        throw new InvalidOperationException(
                            "La solicitud GP12 proviene de Calidad, pero no conserva la inspección de Calidad relacionada.");
                    }

                    const string sqlLiberarCajaProduccion = @"
UPDATE dbo.Produccion_Cajas
SET
    EstadoCajaID=@LiberadaCalidad,
    EstadoCajaNombre=N'Liberada por GP12 - pendiente entrega a Almacén PT',
    EstatusCalidad=N'LIBERADA',
    EtiquetaVerde=1,
    FechaLiberacionCalidad=@Ahora,
    ResultadoCalidad=N'LIBERADA',
    MotivoCalidad=
        LEFT
        (
            CONCAT
            (
                CASE
                    WHEN NULLIF(LTRIM(RTRIM(ISNULL(MotivoCalidad,N''))),N'') IS NULL
                        THEN N''
                    ELSE MotivoCalidad+N' | '
                END,
                N'GP12 liberó la caja. Solicitud GP12 ',
                CONVERT(NVARCHAR(20),@SolicitudGP12ID),
                N'.'
            ),
            500
        ),
    FechaZonaVerde=NULL,
    UsuarioZonaVerdeID=NULL,
    FechaSalidaProduccion=NULL,
    UsuarioSalidaProduccionID=NULL,
    FechaRecepcionAlmacen=NULL,
    UsuarioAlmacenID=NULL,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=@Ahora
WHERE CajaProduccionID=@CajaProduccionID
  AND Activo=1
  AND EstadoCajaID=@RetenidaGP12
  AND UPPER(LTRIM(RTRIM(ISNULL(ResultadoCalidad,N''))))=N'GP12';

IF @@ROWCOUNT<>1
    THROW 51521,'La caja cambió de estado o ya no se encuentra retenida en GP12.',1;";

                    await using (var cmd =
                        new SqlCommand(
                            sqlLiberarCajaProduccion,
                            cn,
                            tx))
                    {
                        cmd.Parameters.Add(
                            "@CajaProduccionID",
                            SqlDbType.BigInt).Value =
                            cajaProduccionID.Value;

                        cmd.Parameters.Add(
                            "@LiberadaCalidad",
                            SqlDbType.Int).Value =
                            ProduccionCajaEstatus.LiberadaCalidad;

                        cmd.Parameters.Add(
                            "@RetenidaGP12",
                            SqlDbType.Int).Value =
                            ProduccionCajaEstatus.RetenidaGp12Scrap;

                        cmd.Parameters.Add(
                            "@SolicitudGP12ID",
                            SqlDbType.Int).Value =
                            model.SolicitudGP12ID;

                        cmd.Parameters.Add(
                            "@UsuarioID",
                            SqlDbType.Int).Value =
                            usuarioID.Value;

                        cmd.Parameters.Add(
                            "@Ahora",
                            SqlDbType.DateTime2).Value =
                            ahora;

                        await cmd.ExecuteNonQueryAsync();
                    }

                    /*
                     * La misma caja deja de aparecer como EN_GP12 dentro de Calidad
                     * y vuelve a ser una caja liberada con destino a Almacén.
                     *
                     * No modificamos FechaValidacionCalidad ni
                     * UsuarioValidacionCalidadID: preservamos quién tomó la
                     * decisión original.
                     */
                    const string sqlLiberarCajaCalidad = @"
UPDATE dbo.Calidad_CajasLiberadas
SET
    EtiquetaLiberacion=N'VERDE',
    Destino=N'ALMACEN',
    Estado=N'LIBERADA',
    Observaciones=
        LEFT
        (
            CONCAT
            (
                CASE
                    WHEN NULLIF(LTRIM(RTRIM(ISNULL(Observaciones,N''))),N'') IS NULL
                        THEN N''
                    ELSE Observaciones+N' | '
                END,
                N'GP12 concluyó satisfactoriamente la solicitud ',
                CONVERT(NVARCHAR(20),@SolicitudGP12ID),
                N'. Caja liberada y pendiente de escaneo físico de salida por Producción.'
            ),
            2000
        ),
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=@Ahora
WHERE CajaLiberadaID=@CajaLiberadaID
  AND CajaProduccionID=@CajaProduccionID
  AND InspeccionID=@InspeccionID
  AND Activo=1;

IF @@ROWCOUNT<>1
    THROW 51522,'No fue posible sincronizar la liberación de GP12 con la caja de Calidad.',1;";

                    await using (var cmd =
                        new SqlCommand(
                            sqlLiberarCajaCalidad,
                            cn,
                            tx))
                    {
                        cmd.Parameters.Add(
                            "@CajaLiberadaID",
                            SqlDbType.Int).Value =
                            cajaLiberadaID.Value;

                        cmd.Parameters.Add(
                            "@CajaProduccionID",
                            SqlDbType.BigInt).Value =
                            cajaProduccionID.Value;

                        cmd.Parameters.Add(
                            "@InspeccionID",
                            SqlDbType.Int).Value =
                            calidadInspeccionID.Value;

                        cmd.Parameters.Add(
                            "@SolicitudGP12ID",
                            SqlDbType.Int).Value =
                            model.SolicitudGP12ID;

                        cmd.Parameters.Add(
                            "@UsuarioID",
                            SqlDbType.Int).Value =
                            usuarioID.Value;

                        cmd.Parameters.Add(
                            "@Ahora",
                            SqlDbType.DateTime2).Value =
                            ahora;

                        await cmd.ExecuteNonQueryAsync();
                    }

                    const string sqlHistorialCalidad = @"
INSERT INTO dbo.Calidad_InspeccionHistorial
(
    InspeccionID,
    Movimiento,
    EstadoAnterior,
    EstadoNuevo,
    ResultadoCalidad,
    Etiqueta,
    Comentario,
    UsuarioID,
    FechaMovimiento
)
SELECT
    @InspeccionID,
    N'GP12_LIBERADA_PARA_SALIDA',
    ci.Estado,
    ci.Estado,
    N'LIBERADA',
    N'VERDE',
    @Comentario,
    @UsuarioID,
    @Ahora
FROM dbo.Calidad_Inspecciones ci
WHERE ci.InspeccionID=@InspeccionID
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Calidad_InspeccionHistorial h
      WHERE h.InspeccionID=@InspeccionID
        AND h.Movimiento=N'GP12_LIBERADA_PARA_SALIDA'
        AND h.Comentario LIKE
            N'%Solicitud GP12 '+CONVERT(NVARCHAR(20),@SolicitudGP12ID)+N'%'
  );";

                    await using (var cmd =
                        new SqlCommand(
                            sqlHistorialCalidad,
                            cn,
                            tx))
                    {
                        var comentario =
                            $"Solicitud GP12 {model.SolicitudGP12ID} concluida. " +
                            $"La caja {cajaProduccionID.Value} fue liberada con etiqueta verde " +
                            $"y quedó pendiente del escaneo físico de entrega por Producción hacia Almacén PT.";

                        if (comentario.Length > 1000)
                            comentario = comentario[..1000];

                        cmd.Parameters.Add(
                            "@InspeccionID",
                            SqlDbType.Int).Value =
                            calidadInspeccionID.Value;

                        cmd.Parameters.Add(
                            "@SolicitudGP12ID",
                            SqlDbType.Int).Value =
                            model.SolicitudGP12ID;

                        cmd.Parameters.Add(
                            "@Comentario",
                            SqlDbType.NVarChar,
                            1000).Value =
                            comentario;

                        cmd.Parameters.Add(
                            "@UsuarioID",
                            SqlDbType.Int).Value =
                            usuarioID.Value;

                        cmd.Parameters.Add(
                            "@Ahora",
                            SqlDbType.DateTime2).Value =
                            ahora;

                        await cmd.ExecuteNonQueryAsync();
                    }

                    cajaDevueltaAProduccion = true;
                }

                var comentarioHistorial =
                    salidaCompleta
                        ? $"Salida completa de GP12 registrada. Cantidad: {model.Cantidad:N4}. " +
                          $"Referencia Almacén: {model.ReferenciaAlmacen}. " +
                          "Saldo GP12: 0. La solicitud quedó cerrada."
                        : $"Salida parcial de GP12 registrada. Cantidad: {model.Cantidad:N4}. " +
                          $"Referencia Almacén: {model.ReferenciaAlmacen}. " +
                          $"Saldo GP12 restante: {saldoRestante:N4}.";

                if (cajaDevueltaAProduccion)
                {
                    comentarioHistorial +=
                        " La caja fue liberada por GP12 y regresó al flujo de Producción " +
                        "para registrar el escaneo físico de entrega a Almacén PT.";
                }

                await AgregarHistorialAsync(
                    cn,
                    tx,
                    model.SolicitudGP12ID,
                    GP12Movimientos.SalidaRegistrada,
                    estatusAnterior,
                    nuevoEstatus,
                    GP12EntidadHistorial.Solicitud,
                    model.SolicitudGP12ID,
                    comentarioHistorial,
                    usuarioID.Value);

                await tx.CommitAsync();

                if (cajaDevueltaAProduccion)
                {
                    return Json(new
                    {
                        ok = true,
                        solicitudGP12ID = model.SolicitudGP12ID,
                        movimientoID,
                        cantidad = model.Cantidad,
                        saldoAnterior = saldoInventario,
                        saldoRestante,
                        referenciaAlmacen = model.ReferenciaAlmacen,
                        gp12Cerrado = true,
                        cajaProduccionID,
                        mensaje =
                            "GP12 concluido. La caja fue liberada con etiqueta verde y regresó al flujo de Producción. " +
                            "Ahora Producción debe realizar el escaneo físico de entrega a Almacén PT."
                    });
                }

                if (salidaCompleta)
                {
                    return Json(new
                    {
                        ok = true,
                        solicitudGP12ID = model.SolicitudGP12ID,
                        movimientoID,
                        cantidad = model.Cantidad,
                        saldoAnterior = saldoInventario,
                        saldoRestante,
                        referenciaAlmacen = model.ReferenciaAlmacen,
                        gp12Cerrado = true,
                        mensaje =
                            "Salida completa registrada. La solicitud GP12 quedó cerrada."
                    });
                }

                return Json(new
                {
                    ok = true,
                    solicitudGP12ID = model.SolicitudGP12ID,
                    movimientoID,
                    cantidad = model.Cantidad,
                    saldoAnterior = saldoInventario,
                    saldoRestante,
                    referenciaAlmacen = model.ReferenciaAlmacen,
                    gp12Cerrado = false,
                    mensaje =
                        $"Salida parcial registrada. GP12 conserva {saldoRestante:N4} pieza(s) pendientes."
                });
            }
            catch (Exception ex)
            {
                try
                {
                    await tx.RollbackAsync();
                }
                catch
                {
                }

                return BadRequest(new
                {
                    ok = false,
                    mensaje = ex.Message
                });
            }
        }
        // NSQ_GP12_DESTINO_CAJA_V1
        // Regresa una caja física de origen Calidad a la bandeja de Calidad
        // para una nueva validación. No reutiliza la decisión anterior y exige
        // un nuevo escaneo físico de Calidad.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegresarCajaACalidad(
            int solicitudGP12ID,
            string? observaciones)
        {
            observaciones = Limpiar(observaciones);

            if (solicitudGP12ID <= 0)
            {
                TempData["Error"] = "La solicitud GP12 no es válida.";
                return RedirectToAction(nameof(Index));
            }

            var usuarioID = ObtenerUsuarioIdActual();
            if (!usuarioID.HasValue || usuarioID.Value <= 0)
                return Unauthorized();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx =
                (SqlTransaction)await cn.BeginTransactionAsync(
                    IsolationLevel.Serializable);

            try
            {
                const string sqlContexto = @"
SELECT
    s.EstatusID,
    s.Origen,
    s.CajaProduccionID,
    s.CajaLiberadaID,
    s.CalidadInspeccionID,
    ISNULL
    (
        (
            SELECT SUM
            (
                CASE
                    WHEN m.TipoMovimiento IN(N'ENTRADA',N'AJUSTE_ENTRADA')
                        THEN ISNULL(m.Cantidad,0)
                    WHEN m.TipoMovimiento IN(N'SALIDA',N'AJUSTE_SALIDA')
                        THEN -ISNULL(m.Cantidad,0)
                    ELSE 0
                END
            )
            FROM dbo.GP12_InventarioMovimientos m WITH(UPDLOCK,HOLDLOCK)
            WHERE m.SolicitudGP12ID=s.SolicitudGP12ID
              AND m.Activo=1
        ),0
    ) AS SaldoInventario,
    (
        SELECT COUNT(1)
        FROM dbo.GP12_Inspecciones ia WITH(UPDLOCK,HOLDLOCK)
        WHERE ia.SolicitudGP12ID=s.SolicitudGP12ID
          AND ia.Activo=1
          AND ia.FechaFin IS NULL
    ) AS InspeccionesAbiertas,
    ISNULL(ultima.CantidadScrap,0) AS UltimaCantidadScrap,
    ci.Estado AS EstadoInspeccionCalidad
FROM dbo.GP12_Solicitudes s WITH(UPDLOCK,HOLDLOCK)
LEFT JOIN dbo.Calidad_Inspecciones ci WITH(UPDLOCK,HOLDLOCK)
    ON ci.InspeccionID=s.CalidadInspeccionID
OUTER APPLY
(
    SELECT TOP(1)
        i.CantidadScrap
    FROM dbo.GP12_Inspecciones i WITH(UPDLOCK,HOLDLOCK)
    WHERE i.SolicitudGP12ID=s.SolicitudGP12ID
      AND i.Activo=1
      AND i.FechaFin IS NOT NULL
    ORDER BY i.FechaFin DESC,i.InspeccionGP12ID DESC
) ultima
WHERE s.SolicitudGP12ID=@SolicitudGP12ID
  AND s.Activo=1;";

                int estatusAnterior;
                string origen;
                long cajaProduccionID;
                int cajaLiberadaID;
                int calidadInspeccionID;
                decimal saldoInventario;
                int inspeccionesAbiertas;
                decimal cantidadScrap;
                string estadoInspeccionCalidad;

                await using (var cmd = new SqlCommand(sqlContexto, cn, tx))
                {
                    cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value =
                        solicitudGP12ID;

                    await using var rd = await cmd.ExecuteReaderAsync();
                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();
                        return NotFound();
                    }

                    estatusAnterior = Convert.ToInt32(rd["EstatusID"]);
                    origen = rd["Origen"]?.ToString()?.Trim().ToUpperInvariant()
                        ?? string.Empty;

                    if (rd["CajaProduccionID"] == DBNull.Value ||
                        rd["CajaLiberadaID"] == DBNull.Value ||
                        rd["CalidadInspeccionID"] == DBNull.Value)
                    {
                        throw new InvalidOperationException(
                            "La solicitud no conserva una caja física y una inspección de Calidad válidas.");
                    }

                    cajaProduccionID = Convert.ToInt64(rd["CajaProduccionID"]);
                    cajaLiberadaID = Convert.ToInt32(rd["CajaLiberadaID"]);
                    calidadInspeccionID = Convert.ToInt32(rd["CalidadInspeccionID"]);
                    saldoInventario = Convert.ToDecimal(rd["SaldoInventario"]);
                    inspeccionesAbiertas = Convert.ToInt32(rd["InspeccionesAbiertas"]);
                    cantidadScrap = Convert.ToDecimal(rd["UltimaCantidadScrap"]);
                    estadoInspeccionCalidad = rd["EstadoInspeccionCalidad"]?.ToString()?.Trim()
                        ?? string.Empty;
                }

                if (!string.Equals(
                        origen,
                        GP12Origen.Calidad,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Sólo las cajas que llegaron desde Calidad pueden regresar a Calidad.");
                }

                if (estatusAnterior != GP12Estatus.InspeccionTerminada)
                {
                    throw new InvalidOperationException(
                        "Primero debe terminar completamente la inspección GP12 antes de elegir el destino de la caja.");
                }

                if (inspeccionesAbiertas > 0)
                {
                    throw new InvalidOperationException(
                        "Todavía existe una inspección GP12 abierta para esta caja.");
                }

                if (saldoInventario <= 0)
                {
                    throw new InvalidOperationException(
                        "GP12 ya no conserva existencia disponible para regresar a Calidad.");
                }

                if (cantidadScrap > 0)
                {
                    throw new InvalidOperationException(
                        "La última inspección contiene scrap. El scrap debe completar su flujo rojo hacia Almacén antes de mover el resto de la caja.");
                }

                if (string.Equals(
                        estadoInspeccionCalidad,
                        "CERRADA",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "La inspección original de Calidad ya está cerrada. No es seguro reabrirla automáticamente desde GP12.");
                }

                var ahora = DateTime.Now;

                const string sqlMovimiento = @"
INSERT INTO dbo.GP12_InventarioMovimientos
(
    SolicitudGP12ID,SolicitudEtiquetaID,TipoMovimiento,Cantidad,
    CajaID,TarimaID,Referencia,Observaciones,FechaMovimiento,UsuarioID,Activo
)
VALUES
(
    @SolicitudGP12ID,NULL,N'SALIDA',@Cantidad,
    NULL,NULL,N'RETORNO_CALIDAD',@Observaciones,@Ahora,@UsuarioID,1
);";

                await using (var cmd = new SqlCommand(sqlMovimiento, cn, tx))
                {
                    cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value =
                        solicitudGP12ID;
                    AgregarDecimal(cmd, "@Cantidad", saldoInventario);
                    cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value =
                        string.IsNullOrWhiteSpace(observaciones)
                            ? "Caja devuelta por GP12 para revalidación de Calidad."
                            : observaciones;
                    cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioID.Value;
                    await cmd.ExecuteNonQueryAsync();
                }

                const string sqlSolicitud = @"
UPDATE dbo.GP12_Solicitudes
SET
    EstatusID=@Cerrado,
    CantidadPendiente=0,
    FechaFin=COALESCE(FechaFin,@Ahora),
    FechaCierre=COALESCE(FechaCierre,@Ahora),
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=@Ahora
WHERE SolicitudGP12ID=@SolicitudGP12ID
  AND Activo=1;
IF @@ROWCOUNT<>1
    THROW 51630,'La solicitud GP12 cambió mientras se regresaba a Calidad.',1;";

                await using (var cmd = new SqlCommand(sqlSolicitud, cn, tx))
                {
                    cmd.Parameters.Add("@Cerrado", SqlDbType.Int).Value = GP12Estatus.Cerrado;
                    cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioID.Value;
                    cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value = solicitudGP12ID;
                    await cmd.ExecuteNonQueryAsync();
                }

                const string sqlCaja = @"
UPDATE dbo.Produccion_Cajas
SET
    EstadoCajaID=@PendienteCalidad,
    EstadoCajaNombre=N'Devuelta por GP12 - pendiente revalidación de Calidad',
    EstatusCalidad=N'PENDIENTE',
    EtiquetaVerde=0,
    FechaSolicitudCalidad=@Ahora,
    FechaLiberacionCalidad=NULL,
    ResultadoCalidad=N'PENDIENTE',
    MotivoCalidad=
        LEFT
        (
            CONCAT
            (
                CASE
                    WHEN NULLIF(LTRIM(RTRIM(ISNULL(MotivoCalidad,N''))),N'') IS NULL
                        THEN N''
                    ELSE MotivoCalidad+N' | '
                END,
                N'GP12 regresó la caja a Calidad para revalidación. Solicitud ',
                CONVERT(NVARCHAR(20),@SolicitudGP12ID),N'.'
            ),500
        ),
    FechaEscaneoCalidad=NULL,
    UsuarioEscaneoCalidadID=NULL,
    FechaZonaVerde=NULL,
    UsuarioZonaVerdeID=NULL,
    FechaSalidaProduccion=NULL,
    UsuarioSalidaProduccionID=NULL,
    FechaRecepcionAlmacen=NULL,
    UsuarioAlmacenID=NULL,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=@Ahora
WHERE CajaProduccionID=@CajaProduccionID
  AND Activo=1
  AND EstadoCajaID=@RetenidaGP12;
IF @@ROWCOUNT<>1
    THROW 51631,'La caja ya no se encuentra retenida en GP12.',1;";

                await using (var cmd = new SqlCommand(sqlCaja, cn, tx))
                {
                    cmd.Parameters.Add("@PendienteCalidad", SqlDbType.Int).Value =
                        ProduccionCajaEstatus.PendienteCalidad;
                    cmd.Parameters.Add("@RetenidaGP12", SqlDbType.Int).Value =
                        ProduccionCajaEstatus.RetenidaGp12Scrap;
                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value =
                        cajaProduccionID;
                    cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value =
                        solicitudGP12ID;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioID.Value;
                    cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
                    await cmd.ExecuteNonQueryAsync();
                }

                const string sqlCajaCalidad = @"
UPDATE dbo.Calidad_CajasLiberadas
SET
    EtiquetaLiberacion=NULL,
    Destino=NULL,
    Estado=N'PENDIENTE',
    Observaciones=
        LEFT
        (
            CONCAT
            (
                CASE
                    WHEN NULLIF(LTRIM(RTRIM(ISNULL(Observaciones,N''))),N'') IS NULL
                        THEN N''
                    ELSE Observaciones+N' | '
                END,
                N'GP12 regresó la caja para revalidación. Solicitud ',
                CONVERT(NVARCHAR(20),@SolicitudGP12ID),N'.'
            ),2000
        ),
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=@Ahora
WHERE CajaLiberadaID=@CajaLiberadaID
  AND CajaProduccionID=@CajaProduccionID
  AND InspeccionID=@InspeccionID
  AND Activo=1;
IF @@ROWCOUNT<>1
    THROW 51632,'No fue posible sincronizar el retorno con Calidad.',1;";

                await using (var cmd = new SqlCommand(sqlCajaCalidad, cn, tx))
                {
                    cmd.Parameters.Add("@CajaLiberadaID", SqlDbType.Int).Value = cajaLiberadaID;
                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = cajaProduccionID;
                    cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = calidadInspeccionID;
                    cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value = solicitudGP12ID;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioID.Value;
                    cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
                    await cmd.ExecuteNonQueryAsync();
                }

                const string sqlHistorialCalidad = @"
INSERT INTO dbo.Calidad_InspeccionHistorial
(
    InspeccionID,Movimiento,EstadoAnterior,EstadoNuevo,ResultadoCalidad,
    Etiqueta,Comentario,UsuarioID,FechaMovimiento
)
SELECT
    @InspeccionID,N'GP12_DEVUELTA_CALIDAD',ci.Estado,ci.Estado,N'PENDIENTE',
    NULL,@Comentario,@UsuarioID,@Ahora
FROM dbo.Calidad_Inspecciones ci
WHERE ci.InspeccionID=@InspeccionID;";

                await using (var cmd = new SqlCommand(sqlHistorialCalidad, cn, tx))
                {
                    cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = calidadInspeccionID;
                    cmd.Parameters.Add("@Comentario", SqlDbType.NVarChar, 1000).Value =
                        $"Solicitud GP12 {solicitudGP12ID} regresó la caja {cajaProduccionID} a Calidad para una nueva validación física.";
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioID.Value;
                    cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
                    await cmd.ExecuteNonQueryAsync();
                }

                await AgregarHistorialAsync(
                    cn,
                    tx,
                    solicitudGP12ID,
                    "DEVUELTA_CALIDAD",
                    estatusAnterior,
                    GP12Estatus.Cerrado,
                    GP12EntidadHistorial.Solicitud,
                    solicitudGP12ID,
                    $"GP12 devolvió {saldoInventario:N4} pieza(s) a Calidad para revalidación. Caja Producción {cajaProduccionID}.",
                    usuarioID.Value);

                await tx.CommitAsync();

                TempData["Mensaje"] =
                    "La caja regresó a Calidad. Calidad deberá escanearla nuevamente y tomar una nueva decisión.";
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                TempData["Error"] =
                    "No fue posible regresar la caja a Calidad: " + ex.Message;
            }

            return RedirectToAction(nameof(Detalle), new { id = solicitudGP12ID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarProgramacion(
            GP12ProgramacionGuardarViewModel model)
        {
            model.Observaciones = Limpiar(model.Observaciones);

            if (!ModelState.IsValid)
            {
                TempData["Error"] =
                    "Revisa la información de la programación.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.SolicitudGP12ID });
            }

            if (model.HoraInicioProgramada.HasValue &&
                model.HoraFinProgramada.HasValue &&
                model.HoraFinProgramada.Value <=
                model.HoraInicioProgramada.Value)
            {
                TempData["Error"] =
                    "La hora fin debe ser posterior a la hora inicio.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.SolicitudGP12ID });
            }

            var usuarioID = ObtenerUsuarioIdActual();

            if (!usuarioID.HasValue || usuarioID.Value <= 0)
                return Unauthorized();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var tx =
                (SqlTransaction)await cn.BeginTransactionAsync();

            try
            {
                const string sqlSolicitud = @"
SELECT EstatusID
FROM dbo.GP12_Solicitudes
WITH (UPDLOCK, HOLDLOCK)
WHERE SolicitudGP12ID = @SolicitudGP12ID
  AND Activo = 1;";

                int estatusAnterior;

                await using (var cmd =
                    new SqlCommand(sqlSolicitud, cn, tx))
                {
                    cmd.Parameters.Add(
                        "@SolicitudGP12ID",
                        SqlDbType.Int).Value =
                        model.SolicitudGP12ID;

                    var resultado =
                        await cmd.ExecuteScalarAsync();

                    if (resultado == null ||
                        resultado == DBNull.Value)
                    {
                        await tx.RollbackAsync();
                        return NotFound();
                    }

                    estatusAnterior =
                        Convert.ToInt32(resultado);
                }

                if (GP12Estatus.EsFinal(estatusAnterior))
                {
                    throw new InvalidOperationException(
                        "La solicitud ya está cerrada o cancelada.");
                }

                const string sqlEtiqueta = @"
SELECT
    TipoEtiqueta,
    CantidadRecibida
FROM dbo.GP12_SolicitudEtiquetas
WITH (UPDLOCK, HOLDLOCK)
WHERE SolicitudEtiquetaID = @SolicitudEtiquetaID
  AND SolicitudGP12ID = @SolicitudGP12ID
  AND Activo = 1;";

                string tipoEtiqueta;
                decimal cantidadRecibidaEtiqueta;

                await using (var cmd =
                    new SqlCommand(sqlEtiqueta, cn, tx))
                {
                    cmd.Parameters.Add(
                        "@SolicitudEtiquetaID",
                        SqlDbType.Int).Value =
                        model.SolicitudEtiquetaID;

                    cmd.Parameters.Add(
                        "@SolicitudGP12ID",
                        SqlDbType.Int).Value =
                        model.SolicitudGP12ID;

                    await using var rd =
                        await cmd.ExecuteReaderAsync();

                    if (!await rd.ReadAsync())
                    {
                        throw new InvalidOperationException(
                            "La clasificación seleccionada no existe o no pertenece a esta solicitud.");
                    }

                    tipoEtiqueta =
                        rd["TipoEtiqueta"] as string
                        ?? GP12TipoEtiqueta.SinClasificar;

                    cantidadRecibidaEtiqueta =
                        Convert.ToDecimal(
                            rd["CantidadRecibida"]);
                }

                if (cantidadRecibidaEtiqueta <= 0)
                {
                    throw new InvalidOperationException(
                        $"Todavía no se ha recibido material {GP12TipoEtiqueta.Nombre(tipoEtiqueta)} para programar.");
                }

                const string sqlProgramado = @"
SELECT ISNULL(SUM(CantidadProgramada), 0)
FROM dbo.GP12_Programacion
WHERE SolicitudGP12ID = @SolicitudGP12ID
  AND SolicitudEtiquetaID = @SolicitudEtiquetaID
  AND Activo = 1;";

                decimal cantidadYaProgramada;

                await using (var cmd =
                    new SqlCommand(sqlProgramado, cn, tx))
                {
                    cmd.Parameters.Add(
                        "@SolicitudGP12ID",
                        SqlDbType.Int).Value =
                        model.SolicitudGP12ID;

                    cmd.Parameters.Add(
                        "@SolicitudEtiquetaID",
                        SqlDbType.Int).Value =
                        model.SolicitudEtiquetaID;

                    cantidadYaProgramada =
                        Convert.ToDecimal(
                            await cmd.ExecuteScalarAsync());
                }

                var disponibleProgramar =
                    cantidadRecibidaEtiqueta -
                    cantidadYaProgramada;

                if (model.CantidadProgramada >
                    disponibleProgramar)
                {
                    throw new InvalidOperationException(
                        $"La cantidad programada excede el material {GP12TipoEtiqueta.Nombre(tipoEtiqueta)} disponible. " +
                        $"Disponible: {Math.Max(0, disponibleProgramar):N4}.");
                }

                const string sqlInsert = @"
INSERT INTO dbo.GP12_Programacion
(
    SolicitudGP12ID,
    SolicitudEtiquetaID,
    FechaProgramada,
    HoraInicioProgramada,
    HoraFinProgramada,
    Prioridad,
    CantidadProgramada,
    Observaciones,
    UsuarioProgramacionID,
    FechaCreacion,
    Activo
)
VALUES
(
    @SolicitudGP12ID,
    @SolicitudEtiquetaID,
    @FechaProgramada,
    @HoraInicio,
    @HoraFin,
    @Prioridad,
    @CantidadProgramada,
    @Observaciones,
    @UsuarioID,
    SYSDATETIME(),
    1
);

SELECT CAST(SCOPE_IDENTITY() AS INT);";

                int programacionID;

                await using (var cmd =
                    new SqlCommand(sqlInsert, cn, tx))
                {
                    cmd.Parameters.Add(
                        "@SolicitudGP12ID",
                        SqlDbType.Int).Value =
                        model.SolicitudGP12ID;

                    cmd.Parameters.Add(
                        "@SolicitudEtiquetaID",
                        SqlDbType.Int).Value =
                        model.SolicitudEtiquetaID;

                    cmd.Parameters.Add(
                        "@FechaProgramada",
                        SqlDbType.Date).Value =
                        model.FechaProgramada.Date;

                    cmd.Parameters.Add(
                        "@HoraInicio",
                        SqlDbType.Time).Value =
                        (object?)model.HoraInicioProgramada
                        ?? DBNull.Value;

                    cmd.Parameters.Add(
                        "@HoraFin",
                        SqlDbType.Time).Value =
                        (object?)model.HoraFinProgramada
                        ?? DBNull.Value;

                    cmd.Parameters.Add(
                        "@Prioridad",
                        SqlDbType.Int).Value =
                        model.Prioridad;

                    AgregarDecimal(
                        cmd,
                        "@CantidadProgramada",
                        model.CantidadProgramada);

                    AgregarNullable(
                        cmd,
                        "@Observaciones",
                        SqlDbType.NVarChar,
                        2000,
                        model.Observaciones);

                    cmd.Parameters.Add(
                        "@UsuarioID",
                        SqlDbType.Int).Value =
                        usuarioID.Value;

                    programacionID =
                        Convert.ToInt32(
                            await cmd.ExecuteScalarAsync());
                }

                var nuevoEstatus =
                    estatusAnterior < GP12Estatus.Programado
                        ? GP12Estatus.Programado
                        : estatusAnterior;

                await ActualizarEstatusSolicitudAsync(
                    cn,
                    tx,
                    model.SolicitudGP12ID,
                    nuevoEstatus,
                    usuarioID.Value);

                await AgregarHistorialAsync(
                    cn,
                    tx,
                    model.SolicitudGP12ID,
                    GP12Movimientos.ProgramacionCreada,
                    estatusAnterior,
                    nuevoEstatus,
                    GP12EntidadHistorial.Programacion,
                    programacionID,
                    $"Se programaron {model.CantidadProgramada:N4} pieza(s) {GP12TipoEtiqueta.Nombre(tipoEtiqueta)} para {model.FechaProgramada:dd/MM/yyyy}.",
                    usuarioID.Value);

                await tx.CommitAsync();

                TempData["Mensaje"] =
                    "Programación GP12 registrada correctamente.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "No fue posible guardar la programación: " +
                    ex.Message;
            }

            return RedirectToAction(
                nameof(Detalle),
                new { id = model.SolicitudGP12ID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarAsignacion(
            GP12AsignacionGuardarViewModel model)
        {
            model.Observaciones = Limpiar(model.Observaciones);

            if (!ModelState.IsValid)
            {
                TempData["Error"] =
                    "Revisa la información de la asignación.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.SolicitudGP12ID });
            }

            var usuarioID = ObtenerUsuarioIdActual();

            if (!usuarioID.HasValue || usuarioID.Value <= 0)
                return Unauthorized();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var tx =
                (SqlTransaction)await cn.BeginTransactionAsync();

            try
            {
                const string sqlSolicitud = @"
SELECT EstatusID
FROM dbo.GP12_Solicitudes
WITH (UPDLOCK, HOLDLOCK)
WHERE SolicitudGP12ID = @SolicitudGP12ID
  AND Activo = 1;";

                int estatusAnterior;

                await using (var cmd =
                    new SqlCommand(sqlSolicitud, cn, tx))
                {
                    cmd.Parameters.Add(
                        "@SolicitudGP12ID",
                        SqlDbType.Int).Value =
                        model.SolicitudGP12ID;

                    var resultado =
                        await cmd.ExecuteScalarAsync();

                    if (resultado == null ||
                        resultado == DBNull.Value)
                    {
                        await tx.RollbackAsync();
                        return NotFound();
                    }

                    estatusAnterior =
                        Convert.ToInt32(resultado);
                }

                if (GP12Estatus.EsFinal(estatusAnterior))
                {
                    throw new InvalidOperationException(
                        "La solicitud ya está cerrada o cancelada.");
                }

                const string sqlProgramacion = @"
SELECT CantidadProgramada
FROM dbo.GP12_Programacion
WITH (UPDLOCK, HOLDLOCK)
WHERE ProgramacionGP12ID = @ProgramacionGP12ID
  AND SolicitudGP12ID = @SolicitudGP12ID
  AND Activo = 1;";

                decimal cantidadProgramada;

                await using (var cmd =
                    new SqlCommand(sqlProgramacion, cn, tx))
                {
                    cmd.Parameters.Add(
                        "@ProgramacionGP12ID",
                        SqlDbType.Int).Value =
                        model.ProgramacionGP12ID;

                    cmd.Parameters.Add(
                        "@SolicitudGP12ID",
                        SqlDbType.Int).Value =
                        model.SolicitudGP12ID;

                    var resultado =
                        await cmd.ExecuteScalarAsync();

                    if (resultado == null ||
                        resultado == DBNull.Value)
                    {
                        throw new InvalidOperationException(
                            "La programación seleccionada no existe o no pertenece a esta solicitud.");
                    }

                    cantidadProgramada =
                        Convert.ToDecimal(resultado);
                }

                const string sqlPersona = @"
SELECT COUNT(1)
FROM dbo.Persona
WHERE PersonaID = @PersonaID
  AND EsColaboradorActivo = 1;";

                await using (var cmd =
                    new SqlCommand(sqlPersona, cn, tx))
                {
                    cmd.Parameters.Add(
                        "@PersonaID",
                        SqlDbType.Int).Value =
                        model.PersonaID;

                    var existe =
                        Convert.ToInt32(
                            await cmd.ExecuteScalarAsync());

                    if (existe <= 0)
                    {
                        throw new InvalidOperationException(
                            "La persona seleccionada no existe o ya no está activa.");
                    }
                }

                const string sqlAsignado = @"
SELECT ISNULL(SUM(CantidadAsignada), 0)
FROM dbo.GP12_Asignaciones
WHERE ProgramacionGP12ID = @ProgramacionGP12ID
  AND SolicitudGP12ID = @SolicitudGP12ID
  AND Activo = 1;";

                decimal cantidadAsignadaActual;

                await using (var cmd =
                    new SqlCommand(sqlAsignado, cn, tx))
                {
                    cmd.Parameters.Add(
                        "@ProgramacionGP12ID",
                        SqlDbType.Int).Value =
                        model.ProgramacionGP12ID;

                    cmd.Parameters.Add(
                        "@SolicitudGP12ID",
                        SqlDbType.Int).Value =
                        model.SolicitudGP12ID;

                    cantidadAsignadaActual =
                        Convert.ToDecimal(
                            await cmd.ExecuteScalarAsync());
                }

                var disponibleAsignar =
                    cantidadProgramada -
                    cantidadAsignadaActual;

                if (model.CantidadAsignada >
                    disponibleAsignar)
                {
                    throw new InvalidOperationException(
                        $"La cantidad asignada excede lo disponible en la programación. " +
                        $"Disponible: {disponibleAsignar:N4}.");
                }

                const string sqlInsert = @"
INSERT INTO dbo.GP12_Asignaciones
(
    ProgramacionGP12ID,
    SolicitudGP12ID,
    PersonaID,
    CantidadAsignada,
    FechaAsignacion,
    Cumplida,
    Observaciones,
    UsuarioAsignacionID,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
VALUES
(
    @ProgramacionGP12ID,
    @SolicitudGP12ID,
    @PersonaID,
    @CantidadAsignada,
    SYSDATETIME(),
    0,
    @Observaciones,
    @UsuarioID,
    @UsuarioID,
    SYSDATETIME(),
    1
);

SELECT CAST(SCOPE_IDENTITY() AS INT);";

                int asignacionID;

                await using (var cmd =
                    new SqlCommand(sqlInsert, cn, tx))
                {
                    cmd.Parameters.Add(
                        "@ProgramacionGP12ID",
                        SqlDbType.Int).Value =
                        model.ProgramacionGP12ID;

                    cmd.Parameters.Add(
                        "@SolicitudGP12ID",
                        SqlDbType.Int).Value =
                        model.SolicitudGP12ID;

                    cmd.Parameters.Add(
                        "@PersonaID",
                        SqlDbType.Int).Value =
                        model.PersonaID;

                    AgregarDecimal(
                        cmd,
                        "@CantidadAsignada",
                        model.CantidadAsignada);

                    AgregarNullable(
                        cmd,
                        "@Observaciones",
                        SqlDbType.NVarChar,
                        2000,
                        model.Observaciones);

                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                        usuarioID.Value;

                    asignacionID =
                        Convert.ToInt32(
                            await cmd.ExecuteScalarAsync());
                }

                var nuevoEstatus =
                    estatusAnterior < GP12Estatus.Asignado
                        ? GP12Estatus.Asignado
                        : estatusAnterior;

                await ActualizarEstatusSolicitudAsync(
                    cn,
                    tx,
                    model.SolicitudGP12ID,
                    nuevoEstatus,
                    usuarioID.Value);

                await AgregarHistorialAsync(
                    cn,
                    tx,
                    model.SolicitudGP12ID,
                    GP12Movimientos.TrabajoAsignado,
                    estatusAnterior,
                    nuevoEstatus,
                    GP12EntidadHistorial.Asignacion,
                    asignacionID,
                    $"Se asignaron {model.CantidadAsignada:N4} pieza(s) a la persona {model.PersonaID}.",
                    usuarioID.Value);

                await tx.CommitAsync();

                TempData["Mensaje"] =
                    "Carga de trabajo asignada correctamente.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "No fue posible guardar la asignación: " +
                    ex.Message;
            }

            return RedirectToAction(
                nameof(Detalle),
                new { id = model.SolicitudGP12ID });
        }

        // =========================================================
        // HELPERS DE ORDEN DE FABRICACIÓN
        // =========================================================
        private async Task CargarOrdenesFabricacionAsync(
            GP12CrearViewModel model)
        {
            model.OrdenesFabricacion.Clear();

            const string sql = @"
SELECT
    s.SolicitudProduccionID,
    s.FolioSolicitud,
    s.NumeroOFRecibida,
    s.FechaSolicitud,
    s.FechaRequerida,
    ISNULL(c.Nombre, s.ClienteNombre) AS ClienteNombre,
    COUNT(DISTINCT d.SolicitudProduccionDetalleID) AS TotalRenglones,
    ISNULL(SUM(d.CantidadPiezas), 0) AS TotalPiezas
FROM dbo.SolicitudesProduccion s
LEFT JOIN dbo.ERP_Clientes c
    ON c.ClienteID = s.ClienteID
INNER JOIN dbo.SolicitudesProduccionDetalle d
    ON d.SolicitudProduccionID = s.SolicitudProduccionID
   AND d.Activo = 1
WHERE s.Activo = 1
  AND ISNULL(s.EstatusID, 0) <> @EstatusCancelado
GROUP BY
    s.SolicitudProduccionID,
    s.FolioSolicitud,
    s.NumeroOFRecibida,
    s.FechaSolicitud,
    s.FechaRequerida,
    ISNULL(c.Nombre, s.ClienteNombre),
    s.FechaCreacion
ORDER BY
    s.FechaCreacion DESC,
    s.SolicitudProduccionID DESC;";

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);

            cmd.Parameters.Add(
                "@EstatusCancelado",
                SqlDbType.Int).Value =
                PlaneacionOFEstatus.Cancelada;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                model.OrdenesFabricacion.Add(
                    new GP12OFSelectorItemViewModel
                    {
                        SolicitudProduccionID =
                            Convert.ToInt32(
                                rd["SolicitudProduccionID"]),

                        FolioSolicitud =
                            rd["FolioSolicitud"] as string,

                        NumeroOFRecibida =
                            rd["NumeroOFRecibida"] as string,

                        ClienteNombre =
                            rd["ClienteNombre"] as string,

                        FechaSolicitud =
                            Convert.ToDateTime(
                                rd["FechaSolicitud"]),

                        FechaRequerida =
                            LeerFechaNullable(
                                rd["FechaRequerida"]),

                        TotalRenglones =
                            Convert.ToInt32(
                                rd["TotalRenglones"]),

                        TotalPiezas =
                            Convert.ToDecimal(
                                rd["TotalPiezas"])
                    });
            }
        }

        private static async Task<GP12OrigenOFData?>
            ObtenerDatosOrigenOFAsync(
                int solicitudProduccionID,
                int solicitudProduccionDetalleID,
                SqlConnection cn,
                SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP (1)
    s.SolicitudProduccionID,
    s.FolioSolicitud,
    s.NumeroOFRecibida,
    s.ClienteID,
    ISNULL(c.Nombre, s.ClienteNombre) AS ClienteNombre,

    d.SolicitudProduccionDetalleID,
    d.Renglon,
    d.ParteID,

    COALESCE(
        NULLIF(p.NumeroParte, N''),
        NULLIF(d.ReferenciaSAP, N''),
        N'SIN PARTE'
    ) AS NumeroParte,

    COALESCE(
        NULLIF(d.DesignacionDescripcionSAP, N''),
        NULLIF(p.Designacion, N''),
        NULLIF(p.Descripcion, N''),
        NULLIF(p.NumeroParte, N''),
        N'Sin descripción'
    ) AS DescripcionParte,

    ISNULL(d.CantidadPiezas, 0) AS CantidadPiezasOF,

    d.MaterialID,
    d.MaterialCodigo,
    d.MaterialDescripcion

FROM dbo.SolicitudesProduccion s

INNER JOIN dbo.SolicitudesProduccionDetalle d
    ON d.SolicitudProduccionID = s.SolicitudProduccionID
   AND d.SolicitudProduccionDetalleID = @SolicitudProduccionDetalleID
   AND d.Activo = 1

LEFT JOIN dbo.ERP_Clientes c
    ON c.ClienteID = s.ClienteID

LEFT JOIN dbo.ERP_Partes p
    ON p.ParteID = d.ParteID

WHERE s.SolicitudProduccionID = @SolicitudProduccionID
  AND s.Activo = 1
  AND ISNULL(s.EstatusID, 0) <> @EstatusCancelado;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add(
                "@SolicitudProduccionID",
                SqlDbType.Int).Value =
                solicitudProduccionID;

            cmd.Parameters.Add(
                "@SolicitudProduccionDetalleID",
                SqlDbType.Int).Value =
                solicitudProduccionDetalleID;

            cmd.Parameters.Add(
                "@EstatusCancelado",
                SqlDbType.Int).Value =
                PlaneacionOFEstatus.Cancelada;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return null;

            var numeroOF = rd["NumeroOFRecibida"] as string;
            var folio = rd["FolioSolicitud"] as string;

            return new GP12OrigenOFData
            {
                SolicitudProduccionID =
                    Convert.ToInt32(
                        rd["SolicitudProduccionID"]),

                SolicitudProduccionDetalleID =
                    Convert.ToInt32(
                        rd["SolicitudProduccionDetalleID"]),

                Renglon =
                    Convert.ToInt32(rd["Renglon"]),

                OrdenFabricacion =
                    !string.IsNullOrWhiteSpace(numeroOF)
                        ? numeroOF
                        : !string.IsNullOrWhiteSpace(folio)
                            ? folio
                            : $"OF #{solicitudProduccionID}",

                ClienteID =
                    LeerIntNullable(rd["ClienteID"]),

                ClienteNombre =
                    rd["ClienteNombre"] as string,

                ParteID =
                    LeerIntNullable(rd["ParteID"]),

                NumeroParte =
                    rd["NumeroParte"] as string,

                DescripcionParte =
                    rd["DescripcionParte"] as string,

                CantidadPiezasOF =
                    Convert.ToDecimal(
                        rd["CantidadPiezasOF"]),

                MaterialID =
                    LeerIntNullable(rd["MaterialID"]),

                MaterialCodigo =
                    rd["MaterialCodigo"] as string,

                MaterialDescripcion =
                    rd["MaterialDescripcion"] as string
            };
        }

        private static void NormalizarCrear(
            GP12CrearViewModel model)
        {
            model.Motivo =
                model.Motivo?.Trim()
                ?? string.Empty;

            model.InstruccionTrabajo =
                Limpiar(model.InstruccionTrabajo);

            model.CodigoHIP =
                Limpiar(model.CodigoHIP);

            model.CodigoHOE =
                Limpiar(model.CodigoHOE);

            model.Observaciones =
                Limpiar(model.Observaciones);
        }

        private sealed class GP12OrigenOFData
        {
            public int SolicitudProduccionID { get; set; }
            public int SolicitudProduccionDetalleID { get; set; }
            public int Renglon { get; set; }

            public string? OrdenFabricacion { get; set; }

            public int? ClienteID { get; set; }
            public string? ClienteNombre { get; set; }

            public int? ParteID { get; set; }
            public string? NumeroParte { get; set; }
            public string? DescripcionParte { get; set; }

            public decimal CantidadPiezasOF { get; set; }

            public int? MaterialID { get; set; }
            public string? MaterialCodigo { get; set; }
            public string? MaterialDescripcion { get; set; }
        }

        private sealed class GP12SolicitudEtiquetaData
        {
            public int SolicitudEtiquetaID { get; set; }
            public string TipoEtiqueta { get; set; } =
                GP12TipoEtiqueta.SinClasificar;
            public decimal CantidadSolicitada { get; set; }
            public decimal CantidadRecibida { get; set; }
            public decimal CantidadProcesada { get; set; }
        }
        // NSQ_GP12_CAJAS_REPORTADAS_CALIDAD_V1_LOADER
        // NSQ_GP12_CAJAS_DESDE_CALIDAD_V1_3_LOADER
        private async Task CargarCajasReportadasCalidadAsync(
            GP12DetalleViewModel model)
        {
            model.CajasReportadasCalidad.Clear();

            if (!string.Equals(
                    model.Origen,
                    GP12Origen.Calidad,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await using var cn =
                new SqlConnection(ConnectionString);

            await cn.OpenAsync();

            const string sql = @"
SELECT
    gp.SolicitudGP12ID,
    cl.CajaProduccionID,
    cl.CajaLiberadaID,

    COALESCE(
        NULLIF(LTRIM(RTRIM(gp.OrdenFabricacion)),N''),
        NULLIF(LTRIM(RTRIM(ci.OrdenTrabajo)),N''),
        NULLIF(LTRIM(RTRIM(pc.NumeroOFEtiqueta)),N''),
        NULLIF(LTRIM(RTRIM(sp.NumeroOFRecibida)),N''),
        NULLIF(LTRIM(RTRIM(sp.FolioSolicitud)),N'')
    ) AS OrdenFabricacion,

    COALESCE(
        NULLIF(LTRIM(RTRIM(gp.NumeroParte)),N''),
        NULLIF(LTRIM(RTRIM(ci.NumeroParte)),N''),
        NULLIF(LTRIM(RTRIM(pc.NumeroParteEtiqueta)),N'')
    ) AS NumeroParte,

    COALESCE(
        NULLIF(LTRIM(RTRIM(gp.Motivo)),N''),
        NULLIF(LTRIM(RTRIM(cl.Observaciones)),N''),
        NULLIF(LTRIM(RTRIM(pc.MotivoCalidad)),N'')
    ) AS Motivo,

    gp.EstatusID,

    COALESCE(
        gp.FechaSolicitud,
        cl.FechaValidacionCalidad,
        cl.FechaCreacion
    ) AS FechaSolicitud,

    gp.FechaRecepcion,
    ISNULL(gp.CantidadRecibida,0) AS CantidadRecibida,

    ISNULL(pc.NumeroCaja,0) AS NumeroCaja,

    COALESCE(
        NULLIF(pc.FolioCaja,N''),
        NULLIF(cl.FolioCaja,N''),
        NULLIF(pc.Etiqueta,N''),
        CONVERT(NVARCHAR(100),pc.CajaProduccionID)
    ) AS FolioCaja,

    ISNULL(
        NULLIF(pc.CantidadPiezas,0),
        ISNULL(NULLIF(cl.CantidadPiezas,0),pc.Cantidad)
    ) AS CantidadPiezas,

    pc.CodigoBarrasOrigen,
    cl.Estado AS EstadoCalidad,
    cl.Destino AS DestinoCalidad,
    cl.FechaValidacionCalidad

FROM dbo.Calidad_CajasLiberadas cl

INNER JOIN dbo.Produccion_Cajas pc
    ON pc.CajaProduccionID=cl.CajaProduccionID

LEFT JOIN dbo.Calidad_Inspecciones ci
    ON ci.InspeccionID=cl.InspeccionID

LEFT JOIN dbo.SolicitudesProduccion sp
    ON sp.SolicitudProduccionID=
       COALESCE(pc.SolicitudProduccionID,ci.SolicitudProduccionID)

OUTER APPLY
(
    SELECT TOP(1)
        s.SolicitudGP12ID,
        s.OrdenFabricacion,
        s.NumeroParte,
        s.Motivo,
        s.EstatusID,
        s.FechaSolicitud,
        s.FechaRecepcion,
        s.CantidadRecibida
    FROM dbo.GP12_Solicitudes s
    WHERE s.Activo=1
      AND UPPER(LTRIM(RTRIM(ISNULL(s.Origen,N''))))=N'CALIDAD'
      AND
      (
            s.CajaLiberadaID=cl.CajaLiberadaID
         OR s.CajaProduccionID=cl.CajaProduccionID
      )
    ORDER BY s.SolicitudGP12ID DESC
) gp

WHERE cl.Activo=1
  AND pc.Activo=1

  AND
  (
       UPPER(LTRIM(RTRIM(ISNULL(cl.Destino,N''))))=N'GP12'
    OR UPPER(LTRIM(RTRIM(ISNULL(cl.Estado,N''))))=N'EN_GP12'
    OR UPPER(LTRIM(RTRIM(ISNULL(pc.ResultadoCalidad,N''))))=N'GP12'
    OR UPPER(LTRIM(RTRIM(ISNULL(pc.EstatusCalidad,N''))))=N'GP12'
  )

  AND
  (
       (
           @CalidadInspeccionID IS NOT NULL
           AND cl.InspeccionID=@CalidadInspeccionID
       )
    OR (
           @EjecucionProduccionID IS NOT NULL
           AND cl.EjecucionProduccionID=@EjecucionProduccionID
       )
    OR (
           @SolicitudProduccionID IS NOT NULL
           AND COALESCE(
                   pc.SolicitudProduccionID,
                   ci.SolicitudProduccionID
               )=@SolicitudProduccionID
       )
    OR (
           @OrdenFabricacion IS NOT NULL
           AND UPPER(
               REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                   LTRIM(RTRIM(COALESCE(
                       ci.OrdenTrabajo,
                       pc.NumeroOFEtiqueta,
                       sp.NumeroOFRecibida,
                       sp.FolioSolicitud,
                       N''
                   ))),
                   N'OF',N''),
                   N'-',N''),
                   N'/',N''),
                   N'''',N''),
                   N' ',N'')
           ) =
           UPPER(
               REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                   LTRIM(RTRIM(@OrdenFabricacion)),
                   N'OF',N''),
                   N'-',N''),
                   N'/',N''),
                   N'''',N''),
                   N' ',N'')
           )
       )
  )

ORDER BY
    ISNULL(pc.NumeroCaja,0),
    cl.CajaLiberadaID;";

            await using var cmd =
                new SqlCommand(sql, cn);

            cmd.Parameters.Add(
                "@CalidadInspeccionID",
                SqlDbType.Int).Value =
                (object?)model.CalidadInspeccionID ??
                DBNull.Value;

            cmd.Parameters.Add(
                "@EjecucionProduccionID",
                SqlDbType.Int).Value =
                (object?)model.EjecucionProduccionID ??
                DBNull.Value;

            cmd.Parameters.Add(
                "@SolicitudProduccionID",
                SqlDbType.Int).Value =
                (object?)model.SolicitudProduccionID ??
                DBNull.Value;

            cmd.Parameters.Add(
                "@OrdenFabricacion",
                SqlDbType.NVarChar,
                100).Value =
                string.IsNullOrWhiteSpace(
                    model.OrdenFabricacion)
                    ? DBNull.Value
                    : model.OrdenFabricacion.Trim();

            await using var rd =
                await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                if (rd["CajaProduccionID"] == DBNull.Value)
                    continue;

                model.CajasReportadasCalidad.Add(
                    new GP12CajaReportadaCalidadViewModel
                    {
                        SolicitudGP12ID =
                            rd["SolicitudGP12ID"] == DBNull.Value
                                ? null
                                : Convert.ToInt32(
                                    rd["SolicitudGP12ID"]),

                        CajaProduccionID =
                            Convert.ToInt64(
                                rd["CajaProduccionID"]),

                        CajaLiberadaID =
                            Convert.ToInt32(
                                rd["CajaLiberadaID"]),

                        OrdenFabricacion =
                            rd["OrdenFabricacion"] == DBNull.Value
                                ? null
                                : rd["OrdenFabricacion"]
                                    ?.ToString()?.Trim(),

                        NumeroParte =
                            rd["NumeroParte"] == DBNull.Value
                                ? null
                                : rd["NumeroParte"]
                                    ?.ToString()?.Trim(),

                        Motivo =
                            rd["Motivo"] == DBNull.Value
                                ? null
                                : rd["Motivo"]
                                    ?.ToString()?.Trim(),

                        EstatusID =
                            rd["EstatusID"] == DBNull.Value
                                ? null
                                : Convert.ToInt32(
                                    rd["EstatusID"]),

                        FechaSolicitud =
                            Convert.ToDateTime(
                                rd["FechaSolicitud"]),

                        FechaRecepcion =
                            rd["FechaRecepcion"] == DBNull.Value
                                ? null
                                : Convert.ToDateTime(
                                    rd["FechaRecepcion"]),

                        CantidadRecibida =
                            Convert.ToDecimal(
                                rd["CantidadRecibida"]),

                        NumeroCaja =
                            Convert.ToInt32(
                                rd["NumeroCaja"]),

                        FolioCaja =
                            rd["FolioCaja"]
                                ?.ToString()?.Trim()
                            ?? rd["CajaProduccionID"]
                                .ToString()!,

                        CantidadPiezas =
                            Convert.ToInt32(
                                rd["CantidadPiezas"]),

                        CodigoBarrasOrigen =
                            rd["CodigoBarrasOrigen"] == DBNull.Value
                                ? null
                                : rd["CodigoBarrasOrigen"]
                                    ?.ToString()?.Trim(),

                        EstadoCalidad =
                            rd["EstadoCalidad"]
                                ?.ToString()?.Trim()
                            ?? string.Empty,

                        DestinoCalidad =
                            rd["DestinoCalidad"] == DBNull.Value
                                ? null
                                : rd["DestinoCalidad"]
                                    ?.ToString()?.Trim(),

                        FechaValidacionCalidad =
                            rd["FechaValidacionCalidad"] == DBNull.Value
                                ? null
                                : Convert.ToDateTime(
                                    rd["FechaValidacionCalidad"])
                    });
            }
        }
private async Task<GP12DetalleViewModel?> ConstruirDetalleAsync(int solicitudGP12ID)
        {
            var model = await CargarEncabezadoAsync(solicitudGP12ID);
            if (model == null) return null;

            // NSQ_GP12_CAJAS_REPORTADAS_CALIDAD_V1_CONTROLLER
            await CargarCajasReportadasCalidadAsync(model);

            await CargarEtiquetasAsync(model);
            await CargarInventarioAsync(model);
            await CargarProgramacionesAsync(model);
            await CargarAsignacionesAsync(model);
            await CargarInspeccionesAsync(model);
            await CargarScrapEntregasAsync(model);
            await CargarHistorialAsync(model);
            await CargarPersonalDisponibleAsync(model);
            await CargarCatalogoDefectosAsync(model);
            return model;
        }

        private async Task CargarScrapEntregasAsync(GP12DetalleViewModel model)
        {
            if (model.SolicitudGP12ID <= 0) return;
            const string sql = @"
SELECT
    e.ScrapEntregaID,
    e.GP12SolicitudID,
    e.GP12InspeccionID,
    e.CajaProduccionID,
    ISNULL(e.CantidadScrap,0) AS CantidadScrap,
    ISNULL(e.Estado,N'') AS Estado,
    e.UsuarioEntregaID,
    e.FechaEntrega,
    e.UsuarioRecepcionID,
    e.FechaRecepcion,
    e.UbicacionScrap,
    e.UsuarioMoliendaID,
    e.FechaMolienda,
    e.CantidadMolida,
    e.Observaciones,
    e.FechaCreacion
FROM dbo.Calidad_ScrapEntregas e
WHERE e.GP12SolicitudID=@SolicitudGP12ID
  AND e.Origen=N'GP12'
  AND e.Activo=1
ORDER BY e.FechaCreacion DESC,e.ScrapEntregaID DESC;";
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value = model.SolicitudGP12ID;
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                model.ScrapEntregas.Add(new GP12ScrapEntregaItemViewModel
                {
                    ScrapEntregaID = Convert.ToInt64(rd["ScrapEntregaID"]),
                    GP12SolicitudID = LeerIntNullable(rd["GP12SolicitudID"]),
                    GP12InspeccionID = LeerIntNullable(rd["GP12InspeccionID"]),
                    CajaProduccionID = rd["CajaProduccionID"] == DBNull.Value ? null : Convert.ToInt64(rd["CajaProduccionID"]),
                    CantidadScrap = Convert.ToDecimal(rd["CantidadScrap"]),
                    Estado = rd["Estado"]?.ToString()?.Trim() ?? string.Empty,
                    UsuarioEntregaID = LeerIntNullable(rd["UsuarioEntregaID"]),
                    FechaEntrega = LeerFechaNullable(rd["FechaEntrega"]),
                    UsuarioRecepcionID = LeerIntNullable(rd["UsuarioRecepcionID"]),
                    FechaRecepcion = LeerFechaNullable(rd["FechaRecepcion"]),
                    UbicacionScrap = rd["UbicacionScrap"] as string,
                    UsuarioMoliendaID = LeerIntNullable(rd["UsuarioMoliendaID"]),
                    FechaMolienda = LeerFechaNullable(rd["FechaMolienda"]),
                    CantidadMolida = rd["CantidadMolida"] == DBNull.Value ? null : Convert.ToDecimal(rd["CantidadMolida"]),
                    Observaciones = rd["Observaciones"] as string,
                    FechaCreacion = Convert.ToDateTime(rd["FechaCreacion"])
                });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EntregarScrapAlmacen(long scrapEntregaID)
        {
            if (scrapEntregaID <= 0)
            {
                TempData["Error"] = "La entrega de scrap no es válida.";
                return RedirectToAction(nameof(Index));
            }
            var usuarioID = ObtenerUsuarioIdActual();
            if (!usuarioID.HasValue || usuarioID.Value <= 0) return Unauthorized();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            int solicitudGP12ID = 0;
            try
            {
                const string sqlEntrega = @"
SELECT
    e.ScrapEntregaID,
    e.GP12SolicitudID,
    e.GP12InspeccionID,
    ISNULL(e.CantidadScrap,0) AS CantidadScrap,
    ISNULL(e.Estado,N'') AS Estado,
    s.EstatusID,
    ISNULL
    (
        (
            SELECT SUM
            (
                CASE
                    WHEN m.TipoMovimiento IN(N'ENTRADA',N'AJUSTE_ENTRADA') THEN ISNULL(m.Cantidad,0)
                    WHEN m.TipoMovimiento IN(N'SALIDA',N'AJUSTE_SALIDA') THEN -ISNULL(m.Cantidad,0)
                    ELSE 0
                END
            )
            FROM dbo.GP12_InventarioMovimientos m WITH(UPDLOCK,HOLDLOCK)
            WHERE m.SolicitudGP12ID=e.GP12SolicitudID
              AND m.Activo=1
        ),0
    ) AS SaldoInventario
FROM dbo.Calidad_ScrapEntregas e WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.GP12_Solicitudes s WITH(UPDLOCK,HOLDLOCK)
    ON s.SolicitudGP12ID=e.GP12SolicitudID
   AND s.Activo=1
WHERE e.ScrapEntregaID=@ScrapEntregaID
  AND e.Origen=N'GP12'
  AND e.Activo=1;";
                int? inspeccionGP12ID;
                decimal cantidadScrap;
                decimal saldoInventario;
                string estado;
                await using (var cmd = new SqlCommand(sqlEntrega, cn, tx))
                {
                    cmd.Parameters.Add("@ScrapEntregaID", SqlDbType.BigInt).Value = scrapEntregaID;
                    await using var rd = await cmd.ExecuteReaderAsync();
                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "No se encontró la entrega de scrap GP12.";
                        return RedirectToAction(nameof(Index));
                    }
                    solicitudGP12ID = LeerIntNullable(rd["GP12SolicitudID"]) ?? 0;
                    inspeccionGP12ID = LeerIntNullable(rd["GP12InspeccionID"]);
                    cantidadScrap = Convert.ToDecimal(rd["CantidadScrap"]);
                    estado = rd["Estado"]?.ToString()?.Trim().ToUpperInvariant() ?? string.Empty;
                    saldoInventario = Convert.ToDecimal(rd["SaldoInventario"]);
                }
                if (solicitudGP12ID <= 0) throw new InvalidOperationException("La entrega no tiene una solicitud GP12 relacionada.");
                if (!inspeccionGP12ID.HasValue || inspeccionGP12ID.Value <= 0) throw new InvalidOperationException("La entrega no tiene una inspección GP12 relacionada.");
                if (estado == "PENDIENTE_RECEPCION")
                {
                    await tx.CommitAsync();
                    TempData["Mensaje"] = "El scrap ya fue entregado y está pendiente de recepción por Almacén.";
                    return RedirectToAction(nameof(Detalle), new { id = solicitudGP12ID });
                }
                if (estado == "RECIBIDO_ALMACEN" || estado == "PENDIENTE_MOLIENDA" || estado == "MOLIDO") throw new InvalidOperationException("El scrap ya fue recibido por Almacén y no puede volver a entregarse.");
                if (estado == "CANCELADO") throw new InvalidOperationException("La entrega de scrap está cancelada.");
                if (estado != "PENDIENTE_ENTREGA_GP12") throw new InvalidOperationException($"La entrega de scrap no está disponible para entregar. Estado actual: {estado}.");
                if (cantidadScrap <= 0) throw new InvalidOperationException("La cantidad de scrap no es válida.");
                if (saldoInventario < cantidadScrap) throw new InvalidOperationException($"No existe inventario suficiente en GP12 para respaldar esta entrega. Disponible: {saldoInventario:N4}; Scrap: {cantidadScrap:N4}.");
                const string sqlUpdate = @"
UPDATE dbo.Calidad_ScrapEntregas
SET Estado=N'PENDIENTE_RECEPCION',
    UsuarioEntregaID=@UsuarioID,
    FechaEntrega=SYSDATETIME(),
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=SYSDATETIME()
WHERE ScrapEntregaID=@ScrapEntregaID
  AND Origen=N'GP12'
  AND Estado=N'PENDIENTE_ENTREGA_GP12'
  AND Activo=1;
IF @@ROWCOUNT<>1
    THROW 51311,'La entrega de scrap cambió de estado mientras se procesaba.',1;";
                await using (var cmd = new SqlCommand(sqlUpdate, cn, tx))
                {
                    cmd.Parameters.Add("@ScrapEntregaID", SqlDbType.BigInt).Value = scrapEntregaID;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioID.Value;
                    await cmd.ExecuteNonQueryAsync();
                }
                await AgregarHistorialAsync(cn, tx, solicitudGP12ID, "SCRAP_ENTREGADO_ALMACEN", null, null, GP12EntidadHistorial.Inspeccion, inspeccionGP12ID.Value, $"GP12 entregó {cantidadScrap:N4} pieza(s) scrap a Almacén. ScrapEntregaID: {scrapEntregaID}. Estado: PENDIENTE_RECEPCION. El inventario permanece en GP12 hasta la confirmación física de Almacén.", usuarioID.Value);
                await tx.CommitAsync();
                TempData["Mensaje"] = $"Se entregaron {cantidadScrap:N4} pieza(s) scrap a Almacén. Quedaron pendientes de recepción.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible entregar el scrap a Almacén: " + ex.Message;
            }
            return solicitudGP12ID > 0
                ? RedirectToAction(nameof(Detalle), new { id = solicitudGP12ID })
                : RedirectToAction(nameof(Index));
        }
        private async Task<GP12DetalleViewModel?> CargarEncabezadoAsync(int solicitudGP12ID)
        {
            const string sql = @"
SELECT
    s.SolicitudGP12ID,
    s.Origen,
    s.ProgramaProduccionID,
    s.EjecucionProduccionID,
    s.CalidadInspeccionID,
    s.CajaProduccionID,
    s.CajaLiberadaID,
    s.SolicitudProduccionID,
    s.SolicitudProduccionDetalleID,
    s.OrdenFabricacion,
    s.ClienteID,
    s.ClienteNombre,
    s.ParteID,
    s.NumeroParte,
    s.DescripcionParte,
    s.MaterialID,
    s.MaterialCodigo,
    s.MaterialDescripcion,
    ISNULL(s.CantidadSolicitada,0) AS CantidadSolicitada,
    ISNULL(s.CantidadRecibida,0) AS CantidadRecibida,
    ISNULL(s.CantidadProcesada,0) AS CantidadProcesada,
    ISNULL(s.CantidadPendiente,0) AS CantidadPendiente,
    s.Motivo,
    s.InstruccionTrabajo,
    s.CodigoHIP,
    s.CodigoHOE,
    s.Observaciones,
    s.EstatusID,
    ISNULL(e.Codigo,N'') AS EstatusCodigo,
    ISNULL(e.Nombre,N'') AS EstatusNombre,
    s.FechaSolicitud,
    s.FechaRecepcion,
    s.FechaInicio,
    s.FechaFin,
    s.FechaCierre
FROM dbo.GP12_Solicitudes s
INNER JOIN dbo.GP12_Estatus e ON e.EstatusID=s.EstatusID
WHERE s.SolicitudGP12ID=@SolicitudGP12ID
  AND s.Activo=1;";
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value = solicitudGP12ID;
            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) return null;
            return new GP12DetalleViewModel
            {
                SolicitudGP12ID = Convert.ToInt32(rd["SolicitudGP12ID"]),
                Origen = rd["Origen"] as string ?? string.Empty,
                ProgramaProduccionID = LeerIntNullable(rd["ProgramaProduccionID"]),
                EjecucionProduccionID = LeerIntNullable(rd["EjecucionProduccionID"]),
                CalidadInspeccionID = LeerIntNullable(rd["CalidadInspeccionID"]),
                CajaProduccionID = rd["CajaProduccionID"] == DBNull.Value ? null : Convert.ToInt64(rd["CajaProduccionID"]),
                CajaLiberadaID = LeerIntNullable(rd["CajaLiberadaID"]),
                SolicitudProduccionID = LeerIntNullable(rd["SolicitudProduccionID"]),
                SolicitudProduccionDetalleID = LeerIntNullable(rd["SolicitudProduccionDetalleID"]),
                OrdenFabricacion = rd["OrdenFabricacion"] as string,
                ClienteID = LeerIntNullable(rd["ClienteID"]),
                ClienteNombre = rd["ClienteNombre"] as string,
                ParteID = LeerIntNullable(rd["ParteID"]),
                NumeroParte = rd["NumeroParte"] as string,
                DescripcionParte = rd["DescripcionParte"] as string,
                MaterialID = LeerIntNullable(rd["MaterialID"]),
                MaterialCodigo = rd["MaterialCodigo"] as string,
                MaterialDescripcion = rd["MaterialDescripcion"] as string,
                CantidadSolicitada = Convert.ToDecimal(rd["CantidadSolicitada"]),
                CantidadRecibida = Convert.ToDecimal(rd["CantidadRecibida"]),
                CantidadProcesada = Convert.ToDecimal(rd["CantidadProcesada"]),
                CantidadPendiente = Convert.ToDecimal(rd["CantidadPendiente"]),
                Motivo = rd["Motivo"] as string,
                InstruccionTrabajo = rd["InstruccionTrabajo"] as string,
                CodigoHIP = rd["CodigoHIP"] as string,
                CodigoHOE = rd["CodigoHOE"] as string,
                Observaciones = rd["Observaciones"] as string,
                EstatusID = Convert.ToInt32(rd["EstatusID"]),
                EstatusCodigo = rd["EstatusCodigo"] as string ?? string.Empty,
                EstatusNombre = rd["EstatusNombre"] as string ?? string.Empty,
                FechaSolicitud = Convert.ToDateTime(rd["FechaSolicitud"]),
                FechaRecepcion = LeerFechaNullable(rd["FechaRecepcion"]),
                FechaInicio = LeerFechaNullable(rd["FechaInicio"]),
                FechaFin = LeerFechaNullable(rd["FechaFin"]),
                FechaCierre = LeerFechaNullable(rd["FechaCierre"])
            };
        }

        private async Task CargarInspeccionesAsync(GP12DetalleViewModel model)
        {
            const string sql = @"
SELECT
    i.InspeccionGP12ID,
    i.SolicitudGP12ID,
    i.AsignacionGP12ID,
    i.PersonaInspectorID,
    LTRIM(RTRIM(
        ISNULL(p.Nombre,N'')+
        CASE WHEN NULLIF(p.ApellidoPaterno,N'') IS NULL THEN N'' ELSE N' '+p.ApellidoPaterno END+
        CASE WHEN NULLIF(p.ApellidoMaterno,N'') IS NULL THEN N'' ELSE N' '+p.ApellidoMaterno END
    )) AS InspectorNombre,
    i.FechaInicio,
    i.FechaFin,
    ISNULL(i.CantidadRevisada,0) AS CantidadRevisada,
    ISNULL(i.CantidadOK,0) AS CantidadOK,
    ISNULL(i.CantidadNOK,0) AS CantidadNOK,
    ISNULL(i.CantidadRetrabajada,0) AS CantidadRetrabajada,
    ISNULL(i.CantidadScrap,0) AS CantidadScrap,
    ISNULL(i.ValidacionEtiqueta,0) AS ValidacionEtiqueta,
    ISNULL(i.DocumentacionColocada,0) AS DocumentacionColocada,
    ISNULL(i.RutaInspeccionValidada,0) AS RutaInspeccionValidada,
    ISNULL(i.CantidadBasculaValidada,0) AS CantidadBasculaValidada,
    ISNULL(i.EtiquetaInspeccionColocada,0) AS EtiquetaInspeccionColocada,
    ISNULL(i.Activo,0) AS Activo
FROM dbo.GP12_Inspecciones i
LEFT JOIN dbo.Persona p ON p.PersonaID=i.PersonaInspectorID
WHERE i.SolicitudGP12ID=@SolicitudGP12ID
ORDER BY
    CASE WHEN i.FechaFin IS NULL THEN 0 ELSE 1 END,
    i.FechaInicio DESC,
    i.InspeccionGP12ID DESC;";
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value = model.SolicitudGP12ID;
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                model.Inspecciones.Add(new GP12InspeccionItemViewModel
                {
                    InspeccionGP12ID = Convert.ToInt32(rd["InspeccionGP12ID"]),
                    SolicitudGP12ID = Convert.ToInt32(rd["SolicitudGP12ID"]),
                    AsignacionGP12ID = Convert.ToInt32(rd["AsignacionGP12ID"]),
                    PersonaInspectorID = Convert.ToInt32(rd["PersonaInspectorID"]),
                    InspectorNombre = rd["InspectorNombre"] as string ?? string.Empty,
                    FechaInicio = LeerFechaNullable(rd["FechaInicio"]),
                    FechaFin = LeerFechaNullable(rd["FechaFin"]),
                    CantidadRevisada = Convert.ToDecimal(rd["CantidadRevisada"]),
                    CantidadOK = Convert.ToDecimal(rd["CantidadOK"]),
                    CantidadNOK = Convert.ToDecimal(rd["CantidadNOK"]),
                    CantidadRetrabajada = Convert.ToDecimal(rd["CantidadRetrabajada"]),
                    CantidadScrap = Convert.ToDecimal(rd["CantidadScrap"]),
                    ValidacionEtiqueta = Convert.ToBoolean(rd["ValidacionEtiqueta"]),
                    DocumentacionColocada = Convert.ToBoolean(rd["DocumentacionColocada"]),
                    RutaInspeccionValidada = Convert.ToBoolean(rd["RutaInspeccionValidada"]),
                    CantidadBasculaValidada = Convert.ToBoolean(rd["CantidadBasculaValidada"]),
                    EtiquetaInspeccionColocada = Convert.ToBoolean(rd["EtiquetaInspeccionColocada"]),
                    Activo = Convert.ToBoolean(rd["Activo"])
                });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> IniciarInspeccion(int solicitudGP12ID, int asignacionGP12ID)
        {
            if (solicitudGP12ID <= 0 || asignacionGP12ID <= 0)
            {
                TempData["Error"] = "La solicitud o asignación GP12 no es válida.";
                return RedirectToAction(nameof(Index));
            }
            var usuarioID = ObtenerUsuarioIdActual();
            if (!usuarioID.HasValue || usuarioID.Value <= 0) return Unauthorized();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                const string sqlOrigen = @"
SELECT TOP (1)
    a.PersonaID,
    a.CantidadAsignada,
    a.FechaInicio,
    a.FechaFin,
    a.Cumplida,
    s.EstatusID
FROM dbo.GP12_Asignaciones a WITH (UPDLOCK,HOLDLOCK)
INNER JOIN dbo.GP12_Solicitudes s WITH (UPDLOCK,HOLDLOCK)
    ON s.SolicitudGP12ID=a.SolicitudGP12ID
WHERE a.AsignacionGP12ID=@AsignacionGP12ID
  AND a.SolicitudGP12ID=@SolicitudGP12ID
  AND a.Activo=1
  AND s.Activo=1;";
                int personaID;
                bool cumplida;
                int estatusAnterior;
                await using (var cmd = new SqlCommand(sqlOrigen, cn, tx))
                {
                    cmd.Parameters.Add("@AsignacionGP12ID", SqlDbType.Int).Value = asignacionGP12ID;
                    cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value = solicitudGP12ID;
                    await using var rd = await cmd.ExecuteReaderAsync();
                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "No se encontró la asignación GP12.";
                        return RedirectToAction(nameof(Detalle), new { id = solicitudGP12ID });
                    }
                    personaID = Convert.ToInt32(rd["PersonaID"]);
                    cumplida = Convert.ToBoolean(rd["Cumplida"]);
                    estatusAnterior = Convert.ToInt32(rd["EstatusID"]);
                }
                if (GP12Estatus.EsFinal(estatusAnterior)) throw new InvalidOperationException("La solicitud GP12 ya está cerrada o cancelada.");
                if (cumplida) throw new InvalidOperationException("La asignación ya fue concluida.");
                const string sqlExistente = @"
SELECT TOP (1) InspeccionGP12ID
FROM dbo.GP12_Inspecciones WITH (UPDLOCK,HOLDLOCK)
WHERE SolicitudGP12ID=@SolicitudGP12ID
  AND AsignacionGP12ID=@AsignacionGP12ID
  AND FechaFin IS NULL
  AND Activo=1
ORDER BY InspeccionGP12ID DESC;";
                int? inspeccionExistente = null;
                await using (var cmd = new SqlCommand(sqlExistente, cn, tx))
                {
                    cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value = solicitudGP12ID;
                    cmd.Parameters.Add("@AsignacionGP12ID", SqlDbType.Int).Value = asignacionGP12ID;
                    var value = await cmd.ExecuteScalarAsync();
                    if (value != null && value != DBNull.Value) inspeccionExistente = Convert.ToInt32(value);
                }
                if (inspeccionExistente.HasValue)
                {
                    await tx.CommitAsync();
                    TempData["Mensaje"] = "La asignación ya tiene una inspección GP12 en proceso.";
                    return RedirectToAction(nameof(Detalle), new { id = solicitudGP12ID });
                }
                const string sqlInsert = @"
INSERT INTO dbo.GP12_Inspecciones
(
    SolicitudGP12ID,
    AsignacionGP12ID,
    PersonaInspectorID,
    FechaInicio,
    CantidadRevisada,
    CantidadOK,
    CantidadNOK,
    CantidadRetrabajada,
    CantidadScrap,
    ValidacionEtiqueta,
    DocumentacionColocada,
    RutaInspeccionValidada,
    CantidadBasculaValidada,
    EtiquetaInspeccionColocada,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
VALUES
(
    @SolicitudGP12ID,
    @AsignacionGP12ID,
    @PersonaID,
    SYSDATETIME(),
    0,0,0,0,0,
    0,0,0,0,0,
    @UsuarioID,
    SYSDATETIME(),
    1
);
SELECT CAST(SCOPE_IDENTITY() AS INT);";
                int inspeccionGP12ID;
                await using (var cmd = new SqlCommand(sqlInsert, cn, tx))
                {
                    cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value = solicitudGP12ID;
                    cmd.Parameters.Add("@AsignacionGP12ID", SqlDbType.Int).Value = asignacionGP12ID;
                    cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = personaID;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioID.Value;
                    inspeccionGP12ID = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                }
                const string sqlUpdate = @"
UPDATE dbo.GP12_Asignaciones
SET FechaInicio=COALESCE(FechaInicio,SYSDATETIME()),
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=SYSDATETIME()
WHERE AsignacionGP12ID=@AsignacionGP12ID
  AND SolicitudGP12ID=@SolicitudGP12ID
  AND Activo=1;
UPDATE dbo.GP12_Solicitudes
SET EstatusID=@EstatusID,
    FechaInicio=COALESCE(FechaInicio,SYSDATETIME()),
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=SYSDATETIME()
WHERE SolicitudGP12ID=@SolicitudGP12ID
  AND Activo=1;";
                await using (var cmd = new SqlCommand(sqlUpdate, cn, tx))
                {
                    cmd.Parameters.Add("@AsignacionGP12ID", SqlDbType.Int).Value = asignacionGP12ID;
                    cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value = solicitudGP12ID;
                    cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value = GP12Estatus.EnInspeccion;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioID.Value;
                    await cmd.ExecuteNonQueryAsync();
                }
                await AgregarHistorialAsync(cn, tx, solicitudGP12ID, GP12Movimientos.InspeccionIniciada, estatusAnterior, GP12Estatus.EnInspeccion, GP12EntidadHistorial.Inspeccion, inspeccionGP12ID, $"Se inició la inspección GP12 de la asignación {asignacionGP12ID}.", usuarioID.Value);
                await tx.CommitAsync();
                TempData["Mensaje"] = "Inspección GP12 iniciada correctamente.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible iniciar la inspección GP12: " + ex.Message;
            }
            return RedirectToAction(nameof(Detalle), new { id = solicitudGP12ID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FinalizarInspeccion(int solicitudGP12ID, int inspeccionGP12ID, decimal cantidadRevisada, decimal cantidadOK, decimal cantidadNOK, decimal cantidadRetrabajada, decimal cantidadScrap, bool validacionEtiqueta, bool documentacionColocada, bool rutaInspeccionValidada, bool cantidadBasculaValidada, bool etiquetaInspeccionColocada, string? observaciones)
        {
            observaciones = Limpiar(observaciones);
            if (solicitudGP12ID <= 0 || inspeccionGP12ID <= 0)
            {
                TempData["Error"] = "La solicitud o inspección GP12 no es válida.";
                return RedirectToAction(nameof(Index));
            }
            if (cantidadRevisada <= 0)
            {
                TempData["Error"] = "La cantidad revisada debe ser mayor que cero.";
                return RedirectToAction(nameof(Detalle), new { id = solicitudGP12ID });
            }
            if (cantidadOK < 0 || cantidadNOK < 0 || cantidadRetrabajada < 0 || cantidadScrap < 0)
            {
                TempData["Error"] = "Las cantidades GP12 no pueden ser negativas.";
                return RedirectToAction(nameof(Detalle), new { id = solicitudGP12ID });
            }
            if (cantidadOK + cantidadNOK != cantidadRevisada)
            {
                TempData["Error"] = "La suma de cantidad OK y NOK debe ser igual a la cantidad revisada.";
                return RedirectToAction(nameof(Detalle), new { id = solicitudGP12ID });
            }
            if (cantidadRetrabajada > cantidadNOK || cantidadScrap > cantidadNOK)
            {
                TempData["Error"] = "Retrabajo y scrap no pueden superar la cantidad NOK.";
                return RedirectToAction(nameof(Detalle), new { id = solicitudGP12ID });
            }
            if (cantidadRetrabajada + cantidadScrap > cantidadNOK)
            {
                TempData["Error"] = "La suma de retrabajo y scrap no puede superar la cantidad NOK.";
                return RedirectToAction(nameof(Detalle), new { id = solicitudGP12ID });
            }
            var usuarioID = ObtenerUsuarioIdActual();
            if (!usuarioID.HasValue || usuarioID.Value <= 0) return Unauthorized();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                const string sqlInspeccion = @"
SELECT TOP(1)
    i.AsignacionGP12ID,
    i.FechaFin,
    a.CantidadAsignada,
    p.SolicitudEtiquetaID,
    s.EstatusID,
    ISNULL(s.CantidadSolicitada,0) AS CantidadSolicitada,
    ISNULL(s.CantidadRecibida,0) AS CantidadRecibida
FROM dbo.GP12_Inspecciones i WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.GP12_Asignaciones a WITH(UPDLOCK,HOLDLOCK)
    ON a.AsignacionGP12ID=i.AsignacionGP12ID
INNER JOIN dbo.GP12_Programacion p
    ON p.ProgramacionGP12ID=a.ProgramacionGP12ID
INNER JOIN dbo.GP12_Solicitudes s WITH(UPDLOCK,HOLDLOCK)
    ON s.SolicitudGP12ID=i.SolicitudGP12ID
WHERE i.InspeccionGP12ID=@InspeccionGP12ID
  AND i.SolicitudGP12ID=@SolicitudGP12ID
  AND i.Activo=1
  AND a.Activo=1
  AND s.Activo=1;";
                int asignacionGP12ID;
                int? solicitudEtiquetaID;
                int estatusAnterior;
                decimal cantidadSolicitada;
                decimal cantidadRecibida;
                DateTime? fechaFin;
                await using (var cmd = new SqlCommand(sqlInspeccion, cn, tx))
                {
                    cmd.Parameters.Add("@InspeccionGP12ID", SqlDbType.Int).Value = inspeccionGP12ID;
                    cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value = solicitudGP12ID;
                    await using var rd = await cmd.ExecuteReaderAsync();
                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "No se encontró la inspección GP12.";
                        return RedirectToAction(nameof(Detalle), new { id = solicitudGP12ID });
                    }
                    asignacionGP12ID = Convert.ToInt32(rd["AsignacionGP12ID"]);
                    solicitudEtiquetaID = LeerIntNullable(rd["SolicitudEtiquetaID"]);
                    estatusAnterior = Convert.ToInt32(rd["EstatusID"]);
                    cantidadSolicitada = Convert.ToDecimal(rd["CantidadSolicitada"]);
                    cantidadRecibida = Convert.ToDecimal(rd["CantidadRecibida"]);
                    fechaFin = LeerFechaNullable(rd["FechaFin"]);
                }
                if (fechaFin.HasValue) throw new InvalidOperationException("La inspección GP12 ya fue finalizada.");
                if (GP12Estatus.EsFinal(estatusAnterior)) throw new InvalidOperationException("La solicitud GP12 ya está cerrada o cancelada.");
                const string sqlUpdateInspeccion = @"
UPDATE dbo.GP12_Inspecciones
SET FechaFin=SYSDATETIME(),
    CantidadRevisada=@CantidadRevisada,
    CantidadOK=@CantidadOK,
    CantidadNOK=@CantidadNOK,
    CantidadRetrabajada=@CantidadRetrabajada,
    CantidadScrap=@CantidadScrap,
    ValidacionEtiqueta=@ValidacionEtiqueta,
    DocumentacionColocada=@DocumentacionColocada,
    RutaInspeccionValidada=@RutaInspeccionValidada,
    CantidadBasculaValidada=@CantidadBasculaValidada,
    EtiquetaInspeccionColocada=@EtiquetaInspeccionColocada,
    Observaciones=@Observaciones,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=SYSDATETIME()
WHERE InspeccionGP12ID=@InspeccionGP12ID
  AND SolicitudGP12ID=@SolicitudGP12ID
  AND FechaFin IS NULL
  AND Activo=1;
IF @@ROWCOUNT<>1
    THROW 51300,'No fue posible finalizar la inspección GP12.',1;";
                await using (var cmd = new SqlCommand(sqlUpdateInspeccion, cn, tx))
                {
                    cmd.Parameters.Add("@InspeccionGP12ID", SqlDbType.Int).Value = inspeccionGP12ID;
                    cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value = solicitudGP12ID;
                    AgregarDecimal(cmd, "@CantidadRevisada", cantidadRevisada);
                    AgregarDecimal(cmd, "@CantidadOK", cantidadOK);
                    AgregarDecimal(cmd, "@CantidadNOK", cantidadNOK);
                    AgregarDecimal(cmd, "@CantidadRetrabajada", cantidadRetrabajada);
                    AgregarDecimal(cmd, "@CantidadScrap", cantidadScrap);
                    cmd.Parameters.Add("@ValidacionEtiqueta", SqlDbType.Bit).Value = validacionEtiqueta;
                    cmd.Parameters.Add("@DocumentacionColocada", SqlDbType.Bit).Value = documentacionColocada;
                    cmd.Parameters.Add("@RutaInspeccionValidada", SqlDbType.Bit).Value = rutaInspeccionValidada;
                    cmd.Parameters.Add("@CantidadBasculaValidada", SqlDbType.Bit).Value = cantidadBasculaValidada;
                    cmd.Parameters.Add("@EtiquetaInspeccionColocada", SqlDbType.Bit).Value = etiquetaInspeccionColocada;
                    cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 2000).Value = string.IsNullOrWhiteSpace(observaciones) ? DBNull.Value : observaciones;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioID.Value;
                    await cmd.ExecuteNonQueryAsync();
                }
                if (cantidadScrap > 0)
                {
                    await CrearEntregaScrapDesdeGP12Async(solicitudGP12ID, inspeccionGP12ID, cantidadScrap, observaciones, usuarioID.Value, cn, tx);
                    await RegistrarDescuentoBonusScrapGP12Async(solicitudGP12ID, inspeccionGP12ID, cantidadScrap, usuarioID.Value, cn, tx);
                }
                if (solicitudEtiquetaID.HasValue)
                {
                    const string sqlEtiqueta = @"
UPDATE dbo.GP12_SolicitudEtiquetas
SET CantidadProcesada=
(
    SELECT ISNULL(SUM(i.CantidadRevisada),0)
    FROM dbo.GP12_Inspecciones i
    INNER JOIN dbo.GP12_Asignaciones a ON a.AsignacionGP12ID=i.AsignacionGP12ID
    INNER JOIN dbo.GP12_Programacion p ON p.ProgramacionGP12ID=a.ProgramacionGP12ID
    WHERE i.SolicitudGP12ID=@SolicitudGP12ID
      AND p.SolicitudEtiquetaID=@SolicitudEtiquetaID
      AND i.FechaFin IS NOT NULL
      AND i.Activo=1
      AND a.Activo=1
      AND p.Activo=1
),
UsuarioModificacionID=@UsuarioID,
FechaModificacion=SYSDATETIME()
WHERE SolicitudEtiquetaID=@SolicitudEtiquetaID
  AND SolicitudGP12ID=@SolicitudGP12ID
  AND Activo=1;";
                    await using var cmd = new SqlCommand(sqlEtiqueta, cn, tx);
                    cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value = solicitudGP12ID;
                    cmd.Parameters.Add("@SolicitudEtiquetaID", SqlDbType.Int).Value = solicitudEtiquetaID.Value;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioID.Value;
                    await cmd.ExecuteNonQueryAsync();
                }
                const string sqlAsignacion = @"
DECLARE @ProcesadoAsignacion DECIMAL(18,4);
DECLARE @Asignado DECIMAL(18,4);
SELECT @Asignado=ISNULL(CantidadAsignada,0)
FROM dbo.GP12_Asignaciones WITH(UPDLOCK,HOLDLOCK)
WHERE AsignacionGP12ID=@AsignacionGP12ID
  AND Activo=1;
SELECT @ProcesadoAsignacion=ISNULL(SUM(CantidadRevisada),0)
FROM dbo.GP12_Inspecciones
WHERE AsignacionGP12ID=@AsignacionGP12ID
  AND FechaFin IS NOT NULL
  AND Activo=1;
UPDATE dbo.GP12_Asignaciones
SET Cumplida=CASE WHEN @ProcesadoAsignacion>=@Asignado AND @Asignado>0 THEN 1 ELSE 0 END,
    FechaFin=CASE WHEN @ProcesadoAsignacion>=@Asignado AND @Asignado>0 THEN COALESCE(FechaFin,SYSDATETIME()) ELSE FechaFin END,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=SYSDATETIME()
WHERE AsignacionGP12ID=@AsignacionGP12ID
  AND Activo=1;";
                await using (var cmd = new SqlCommand(sqlAsignacion, cn, tx))
                {
                    cmd.Parameters.Add("@AsignacionGP12ID", SqlDbType.Int).Value = asignacionGP12ID;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioID.Value;
                    await cmd.ExecuteNonQueryAsync();
                }
                const string sqlTotales = @"
SELECT
    ISNULL(SUM(CASE WHEN FechaFin IS NOT NULL THEN CantidadRevisada ELSE 0 END),0) AS CantidadProcesada,
    ISNULL(SUM(CASE WHEN FechaFin IS NOT NULL THEN CantidadOK ELSE 0 END),0) AS CantidadOK,
    ISNULL(SUM(CASE WHEN FechaFin IS NOT NULL THEN CantidadNOK ELSE 0 END),0) AS CantidadNOK,
    ISNULL(SUM(CASE WHEN FechaFin IS NOT NULL THEN CantidadRetrabajada ELSE 0 END),0) AS CantidadRetrabajada,
    ISNULL(SUM(CASE WHEN FechaFin IS NOT NULL THEN CantidadScrap ELSE 0 END),0) AS CantidadScrap
FROM dbo.GP12_Inspecciones
WHERE SolicitudGP12ID=@SolicitudGP12ID
  AND Activo=1;";
                decimal totalProcesado;
                decimal totalOK;
                decimal totalNOK;
                decimal totalRetrabajado;
                decimal totalScrap;
                await using (var cmd = new SqlCommand(sqlTotales, cn, tx))
                {
                    cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value = solicitudGP12ID;
                    await using var rd = await cmd.ExecuteReaderAsync();
                    await rd.ReadAsync();
                    totalProcesado = Convert.ToDecimal(rd["CantidadProcesada"]);
                    totalOK = Convert.ToDecimal(rd["CantidadOK"]);
                    totalNOK = Convert.ToDecimal(rd["CantidadNOK"]);
                    totalRetrabajado = Convert.ToDecimal(rd["CantidadRetrabajada"]);
                    totalScrap = Convert.ToDecimal(rd["CantidadScrap"]);
                }
                var pendiente = Math.Max(0, cantidadRecibida - totalProcesado);
                var procesoCompleto = cantidadSolicitada > 0 && cantidadRecibida >= cantidadSolicitada && totalProcesado >= cantidadSolicitada;
                var nuevoEstatus = procesoCompleto ? GP12Estatus.InspeccionTerminada : GP12Estatus.EnInspeccion;
                const string sqlSolicitud = @"
UPDATE dbo.GP12_Solicitudes
SET CantidadProcesada=@CantidadProcesada,
    CantidadPendiente=@CantidadPendiente,
    EstatusID=@EstatusID,
    FechaFin=CASE WHEN @ProcesoCompleto=1 THEN COALESCE(FechaFin,SYSDATETIME()) ELSE FechaFin END,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=SYSDATETIME()
WHERE SolicitudGP12ID=@SolicitudGP12ID
  AND Activo=1;";
                await using (var cmd = new SqlCommand(sqlSolicitud, cn, tx))
                {
                    AgregarDecimal(cmd, "@CantidadProcesada", totalProcesado);
                    AgregarDecimal(cmd, "@CantidadPendiente", pendiente);
                    cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value = nuevoEstatus;
                    cmd.Parameters.Add("@ProcesoCompleto", SqlDbType.Bit).Value = procesoCompleto;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioID.Value;
                    cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value = solicitudGP12ID;
                    await cmd.ExecuteNonQueryAsync();
                }
                var mensajeHistorial = $"Inspección GP12 terminada. Revisadas: {cantidadRevisada:N4}; OK: {cantidadOK:N4}; NOK: {cantidadNOK:N4}; retrabajadas: {cantidadRetrabajada:N4}; scrap: {cantidadScrap:N4}.";
                if (cantidadScrap > 0) mensajeHistorial += $" Se generó una entrega de scrap por {cantidadScrap:N4} pieza(s) en estado PENDIENTE_ENTREGA_GP12 y, cuando la solicitud proviene de una caja trazable de Producción, el scrap fue descontado del bonus de sus operadores origen.";
                await AgregarHistorialAsync(cn, tx, solicitudGP12ID, GP12Movimientos.InspeccionTerminada, estatusAnterior, nuevoEstatus, GP12EntidadHistorial.Inspeccion, inspeccionGP12ID, mensajeHistorial, usuarioID.Value);
                await tx.CommitAsync();
                if (cantidadScrap > 0)
                {
                    TempData["Mensaje"] = procesoCompleto
                        ? $"Inspección GP12 terminada. Se identificaron {cantidadScrap:N4} pieza(s) scrap. El material quedó pendiente de entrega a Almacén y el bonus fue conciliado cuando existió trazabilidad con Producción."
                        : $"Inspección registrada. Se identificaron {cantidadScrap:N4} pieza(s) scrap pendientes de entrega a Almacén. El bonus fue conciliado cuando existió trazabilidad con Producción y aún existe material por procesar.";
                }
                else
                {
                    TempData["Mensaje"] = procesoCompleto
                        ? totalNOK == 0
                            ? "Inspección GP12 terminada. El material liberado queda disponible para el flujo de Almacén PT."
                            : "Inspección GP12 terminada con material NOK pendiente de resolución dentro de GP12."
                        : "Inspección GP12 registrada. Aún existe material pendiente por procesar.";
                }
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible finalizar la inspección GP12: " + ex.Message;
            }
            return RedirectToAction(nameof(Detalle), new { id = solicitudGP12ID });
        }
        private async Task CargarEtiquetasAsync(
            GP12DetalleViewModel model)
        {
            const string sql = @"
SELECT
    SolicitudEtiquetaID,
    SolicitudGP12ID,
    TipoEtiqueta,
    CantidadSolicitada,
    CantidadRecibida,
    CantidadProcesada,
    Activo
FROM dbo.GP12_SolicitudEtiquetas
WHERE SolicitudGP12ID = @SolicitudGP12ID
ORDER BY
    CASE TipoEtiqueta
        WHEN 'AMARILLA' THEN 1
        WHEN 'ROJA' THEN 2
        ELSE 3
    END,
    SolicitudEtiquetaID;";

            await using var cn =
                new SqlConnection(ConnectionString);

            await cn.OpenAsync();

            await using var cmd =
                new SqlCommand(sql, cn);

            cmd.Parameters.Add(
                "@SolicitudGP12ID",
                SqlDbType.Int).Value =
                model.SolicitudGP12ID;

            await using var rd =
                await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                model.Etiquetas.Add(
                    new GP12SolicitudEtiquetaItemViewModel
                    {
                        SolicitudEtiquetaID =
                            Convert.ToInt32(
                                rd["SolicitudEtiquetaID"]),

                        SolicitudGP12ID =
                            Convert.ToInt32(
                                rd["SolicitudGP12ID"]),

                        TipoEtiqueta =
                            rd["TipoEtiqueta"] as string
                            ?? GP12TipoEtiqueta.SinClasificar,

                        CantidadSolicitada =
                            Convert.ToDecimal(
                                rd["CantidadSolicitada"]),

                        CantidadRecibida =
                            Convert.ToDecimal(
                                rd["CantidadRecibida"]),

                        CantidadProcesada =
                            Convert.ToDecimal(
                                rd["CantidadProcesada"]),

                        Activo =
                            Convert.ToBoolean(rd["Activo"])
                    });
            }
        }

        private async Task CargarInventarioAsync(
            GP12DetalleViewModel model)
        {
            const string sql = @"
SELECT
    m.MovimientoID,
    m.SolicitudGP12ID,
    m.SolicitudEtiquetaID,
    ISNULL(e.TipoEtiqueta, 'SIN_CLASIFICAR') AS TipoEtiqueta,
    m.TipoMovimiento,
    m.Cantidad,
    m.CajaID,
    m.TarimaID,
    m.Referencia,
    m.Observaciones,
    m.FechaMovimiento,
    m.UsuarioID,
    m.Activo
FROM dbo.GP12_InventarioMovimientos m
LEFT JOIN dbo.GP12_SolicitudEtiquetas e
    ON e.SolicitudEtiquetaID = m.SolicitudEtiquetaID
WHERE m.SolicitudGP12ID = @SolicitudGP12ID
ORDER BY
    m.FechaMovimiento DESC,
    m.MovimientoID DESC;";

            await using var cn =
                new SqlConnection(ConnectionString);

            await cn.OpenAsync();

            await using var cmd =
                new SqlCommand(sql, cn);

            cmd.Parameters.Add(
                "@SolicitudGP12ID",
                SqlDbType.Int).Value =
                model.SolicitudGP12ID;

            await using var rd =
                await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                model.InventarioMovimientos.Add(
                    new GP12InventarioMovimientoItemViewModel
                    {
                        MovimientoID =
                            Convert.ToInt32(rd["MovimientoID"]),

                        SolicitudGP12ID =
                            Convert.ToInt32(rd["SolicitudGP12ID"]),

                        SolicitudEtiquetaID =
                            LeerIntNullable(
                                rd["SolicitudEtiquetaID"]),

                        TipoEtiqueta =
                            rd["TipoEtiqueta"] as string
                            ?? GP12TipoEtiqueta.SinClasificar,

                        TipoMovimiento =
                            rd["TipoMovimiento"] as string
                            ?? string.Empty,

                        Cantidad =
                            Convert.ToDecimal(rd["Cantidad"]),

                        CajaID =
                            LeerIntNullable(rd["CajaID"]),

                        TarimaID =
                            LeerIntNullable(rd["TarimaID"]),

                        Referencia =
                            rd["Referencia"] as string,

                        Observaciones =
                            rd["Observaciones"] as string,

                        FechaMovimiento =
                            Convert.ToDateTime(
                                rd["FechaMovimiento"]),

                        UsuarioID =
                            LeerIntNullable(rd["UsuarioID"]),

                        Activo =
                            Convert.ToBoolean(rd["Activo"])
                    });
            }
        }

        private async Task CargarProgramacionesAsync(
            GP12DetalleViewModel model)
        {
            const string sql = @"
SELECT
    p.ProgramacionGP12ID,
    p.SolicitudGP12ID,
    p.SolicitudEtiquetaID,
    ISNULL(e.TipoEtiqueta, 'SIN_CLASIFICAR') AS TipoEtiqueta,
    p.FechaProgramada,
    p.HoraInicioProgramada,
    p.HoraFinProgramada,
    p.Prioridad,
    p.CantidadProgramada,
    p.Observaciones,
    p.UsuarioProgramacionID,
    p.FechaCreacion,
    p.Activo
FROM dbo.GP12_Programacion p
LEFT JOIN dbo.GP12_SolicitudEtiquetas e
    ON e.SolicitudEtiquetaID = p.SolicitudEtiquetaID
WHERE p.SolicitudGP12ID = @SolicitudGP12ID
ORDER BY
    p.Activo DESC,
    p.FechaProgramada DESC,
    p.ProgramacionGP12ID DESC;";

            await using var cn =
                new SqlConnection(ConnectionString);

            await cn.OpenAsync();

            await using var cmd =
                new SqlCommand(sql, cn);

            cmd.Parameters.Add(
                "@SolicitudGP12ID",
                SqlDbType.Int).Value =
                model.SolicitudGP12ID;

            await using var rd =
                await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                model.Programaciones.Add(
                    new GP12ProgramacionItemViewModel
                    {
                        ProgramacionGP12ID =
                            Convert.ToInt32(
                                rd["ProgramacionGP12ID"]),

                        SolicitudGP12ID =
                            Convert.ToInt32(
                                rd["SolicitudGP12ID"]),

                        SolicitudEtiquetaID =
                            LeerIntNullable(
                                rd["SolicitudEtiquetaID"]),

                        TipoEtiqueta =
                            rd["TipoEtiqueta"] as string
                            ?? GP12TipoEtiqueta.SinClasificar,

                        FechaProgramada =
                            Convert.ToDateTime(
                                rd["FechaProgramada"]),

                        HoraInicioProgramada =
                            LeerHoraNullable(
                                rd["HoraInicioProgramada"]),

                        HoraFinProgramada =
                            LeerHoraNullable(
                                rd["HoraFinProgramada"]),

                        Prioridad =
                            Convert.ToInt32(rd["Prioridad"]),

                        CantidadProgramada =
                            Convert.ToDecimal(
                                rd["CantidadProgramada"]),

                        Observaciones =
                            rd["Observaciones"] as string,

                        UsuarioProgramacionID =
                            LeerIntNullable(
                                rd["UsuarioProgramacionID"]),

                        FechaCreacion =
                            Convert.ToDateTime(
                                rd["FechaCreacion"]),

                        Activo =
                            Convert.ToBoolean(rd["Activo"])
                    });
            }
        }

        private async Task CargarAsignacionesAsync(
            GP12DetalleViewModel model)
        {
            const string sql = @"
SELECT
    a.AsignacionGP12ID,
    a.ProgramacionGP12ID,
    a.SolicitudGP12ID,
    ISNULL(e.TipoEtiqueta, 'SIN_CLASIFICAR') AS TipoEtiqueta,
    a.PersonaID,
    a.CantidadAsignada,
    a.FechaAsignacion,
    a.FechaInicio,
    a.FechaFin,
    a.Cumplida,
    a.Observaciones,
    a.Activo,
    LTRIM(RTRIM(
        ISNULL(per.Nombre, N'') +
        CASE
            WHEN NULLIF(per.ApellidoPaterno, N'') IS NULL
                THEN N''
            ELSE N' ' + per.ApellidoPaterno
        END +
        CASE
            WHEN NULLIF(per.ApellidoMaterno, N'') IS NULL
                THEN N''
            ELSE N' ' + per.ApellidoMaterno
        END
    )) AS PersonaNombre
FROM dbo.GP12_Asignaciones a
INNER JOIN dbo.Persona per
    ON per.PersonaID = a.PersonaID
LEFT JOIN dbo.GP12_Programacion prog
    ON prog.ProgramacionGP12ID = a.ProgramacionGP12ID
LEFT JOIN dbo.GP12_SolicitudEtiquetas e
    ON e.SolicitudEtiquetaID = prog.SolicitudEtiquetaID
WHERE a.SolicitudGP12ID = @SolicitudGP12ID
ORDER BY
    a.Activo DESC,
    a.FechaAsignacion DESC,
    a.AsignacionGP12ID DESC;";

            await using var cn =
                new SqlConnection(ConnectionString);

            await cn.OpenAsync();

            await using var cmd =
                new SqlCommand(sql, cn);

            cmd.Parameters.Add(
                "@SolicitudGP12ID",
                SqlDbType.Int).Value =
                model.SolicitudGP12ID;

            await using var rd =
                await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                model.Asignaciones.Add(
                    new GP12AsignacionItemViewModel
                    {
                        AsignacionGP12ID =
                            Convert.ToInt32(
                                rd["AsignacionGP12ID"]),

                        ProgramacionGP12ID =
                            Convert.ToInt32(
                                rd["ProgramacionGP12ID"]),

                        SolicitudGP12ID =
                            Convert.ToInt32(
                                rd["SolicitudGP12ID"]),

                        TipoEtiqueta =
                            rd["TipoEtiqueta"] as string
                            ?? GP12TipoEtiqueta.SinClasificar,

                        PersonaID =
                            Convert.ToInt32(rd["PersonaID"]),

                        PersonaNombre =
                            rd["PersonaNombre"] as string
                            ?? string.Empty,

                        CantidadAsignada =
                            Convert.ToDecimal(
                                rd["CantidadAsignada"]),

                        FechaAsignacion =
                            Convert.ToDateTime(
                                rd["FechaAsignacion"]),

                        FechaInicio =
                            LeerFechaNullable(rd["FechaInicio"]),

                        FechaFin =
                            LeerFechaNullable(rd["FechaFin"]),

                        Cumplida =
                            Convert.ToBoolean(rd["Cumplida"]),

                        Observaciones =
                            rd["Observaciones"] as string,

                        Activo =
                            Convert.ToBoolean(rd["Activo"])
                    });
            }
        }

        private async Task CargarHistorialAsync(
            GP12DetalleViewModel model)
        {
            const string sql = @"
SELECT
    h.HistorialGP12ID,
    h.SolicitudGP12ID,
    h.Movimiento,
    h.EstatusAnteriorID,
    ea.Nombre AS EstatusAnteriorNombre,
    h.EstatusNuevoID,
    en.Nombre AS EstatusNuevoNombre,
    h.Entidad,
    h.EntidadID,
    h.Comentario,
    h.UsuarioID,
    h.FechaMovimiento
FROM dbo.GP12_Historial h
LEFT JOIN dbo.GP12_Estatus ea
    ON ea.EstatusID = h.EstatusAnteriorID
LEFT JOIN dbo.GP12_Estatus en
    ON en.EstatusID = h.EstatusNuevoID
WHERE h.SolicitudGP12ID = @SolicitudGP12ID
ORDER BY
    h.FechaMovimiento DESC,
    h.HistorialGP12ID DESC;";

            await using var cn =
                new SqlConnection(ConnectionString);

            await cn.OpenAsync();

            await using var cmd =
                new SqlCommand(sql, cn);

            cmd.Parameters.Add(
                "@SolicitudGP12ID",
                SqlDbType.Int).Value =
                model.SolicitudGP12ID;

            await using var rd =
                await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                model.Historial.Add(
                    new GP12HistorialItemViewModel
                    {
                        HistorialGP12ID =
                            Convert.ToInt32(rd["HistorialGP12ID"]),

                        SolicitudGP12ID =
                            Convert.ToInt32(rd["SolicitudGP12ID"]),

                        Movimiento =
                            rd["Movimiento"] as string ?? string.Empty,

                        EstatusAnteriorID =
                            LeerIntNullable(rd["EstatusAnteriorID"]),

                        EstatusAnteriorNombre =
                            rd["EstatusAnteriorNombre"] as string,

                        EstatusNuevoID =
                            LeerIntNullable(rd["EstatusNuevoID"]),

                        EstatusNuevoNombre =
                            rd["EstatusNuevoNombre"] as string,

                        Entidad =
                            rd["Entidad"] as string,

                        EntidadID =
                            LeerIntNullable(rd["EntidadID"]),

                        Comentario =
                            rd["Comentario"] as string,

                        UsuarioID =
                            LeerIntNullable(rd["UsuarioID"]),

                        FechaMovimiento =
                            Convert.ToDateTime(rd["FechaMovimiento"])
                    });
            }
        }

        private async Task CargarPersonalDisponibleAsync(
            GP12DetalleViewModel model)
        {
            const string sql = @"
SELECT
    PersonaID,
    Nombre,
    ApellidoPaterno,
    ApellidoMaterno,
    Puesto
FROM dbo.Persona
WHERE EsColaboradorActivo = 1
ORDER BY
    Nombre,
    ApellidoPaterno,
    ApellidoMaterno,
    PersonaID;";

            await using var cn =
                new SqlConnection(ConnectionString);

            await cn.OpenAsync();

            await using var cmd =
                new SqlCommand(sql, cn);

            await using var rd =
                await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                model.PersonalDisponible.Add(
                    new GP12PersonaItemViewModel
                    {
                        PersonaID =
                            Convert.ToInt32(rd["PersonaID"]),

                        Nombre =
                            ConstruirNombrePersona(
                                rd["Nombre"] as string,
                                rd["ApellidoPaterno"] as string,
                                rd["ApellidoMaterno"] as string),

                        Puesto =
                            rd["Puesto"] as string
                    });
            }
        }

        private async Task CargarCatalogoDefectosAsync(
            GP12DetalleViewModel model)
        {
            const string sql = @"
SELECT
    DefectoID,
    Codigo,
    Nombre,
    Orden
FROM dbo.GP12_CatalogoDefectos
WHERE Activo = 1
ORDER BY
    Orden,
    DefectoID;";

            await using var cn =
                new SqlConnection(ConnectionString);

            await cn.OpenAsync();

            await using var cmd =
                new SqlCommand(sql, cn);

            await using var rd =
                await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                model.CatalogoDefectos.Add(
                    new GP12CatalogoDefectoItemViewModel
                    {
                        DefectoID =
                            Convert.ToInt32(rd["DefectoID"]),

                        Codigo =
                            rd["Codigo"] as string ?? string.Empty,

                        Nombre =
                            rd["Nombre"] as string ?? string.Empty,

                        Orden =
                            Convert.ToInt32(rd["Orden"])
                    });
            }
        }

        // =========================================================
        // HELPERS DE ESTADO E HISTORIAL
        // =========================================================
        private static async Task ActualizarEstatusSolicitudAsync(
            SqlConnection cn,
            SqlTransaction tx,
            int solicitudGP12ID,
            int estatusID,
            int usuarioID)
        {
            const string sql = @"
UPDATE dbo.GP12_Solicitudes
SET
    EstatusID = @EstatusID,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = SYSDATETIME()
WHERE SolicitudGP12ID = @SolicitudGP12ID
  AND Activo = 1;";

            await using var cmd =
                new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value =
                estatusID;

            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                usuarioID;

            cmd.Parameters.Add(
                "@SolicitudGP12ID",
                SqlDbType.Int).Value =
                solicitudGP12ID;

            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task AgregarHistorialAsync(
            SqlConnection cn,
            SqlTransaction tx,
            int solicitudGP12ID,
            string movimiento,
            int? estatusAnteriorID,
            int? estatusNuevoID,
            string? entidad,
            int? entidadID,
            string? comentario,
            int? usuarioID)
        {
            const string sql = @"
INSERT INTO dbo.GP12_Historial
(
    SolicitudGP12ID,
    Movimiento,
    EstatusAnteriorID,
    EstatusNuevoID,
    Entidad,
    EntidadID,
    Comentario,
    UsuarioID,
    FechaMovimiento
)
VALUES
(
    @SolicitudGP12ID,
    @Movimiento,
    @EstatusAnteriorID,
    @EstatusNuevoID,
    @Entidad,
    @EntidadID,
    @Comentario,
    @UsuarioID,
    SYSDATETIME()
);";

            await using var cmd =
                new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add(
                "@SolicitudGP12ID",
                SqlDbType.Int).Value =
                solicitudGP12ID;

            cmd.Parameters.Add(
                "@Movimiento",
                SqlDbType.NVarChar,
                100).Value =
                movimiento;

            cmd.Parameters.Add(
                "@EstatusAnteriorID",
                SqlDbType.Int).Value =
                (object?)estatusAnteriorID
                ?? DBNull.Value;

            cmd.Parameters.Add(
                "@EstatusNuevoID",
                SqlDbType.Int).Value =
                (object?)estatusNuevoID
                ?? DBNull.Value;

            cmd.Parameters.Add(
                "@Entidad",
                SqlDbType.NVarChar,
                30).Value =
                (object?)entidad
                ?? DBNull.Value;

            cmd.Parameters.Add(
                "@EntidadID",
                SqlDbType.Int).Value =
                (object?)entidadID
                ?? DBNull.Value;

            cmd.Parameters.Add(
                "@Comentario",
                SqlDbType.NVarChar,
                2000).Value =
                string.IsNullOrWhiteSpace(comentario)
                    ? DBNull.Value
                    : comentario.Trim();

            cmd.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value =
                (object?)usuarioID
                ?? DBNull.Value;

            await cmd.ExecuteNonQueryAsync();
        }

        // =========================================================
        // HELPERS GENERALES
        // =========================================================
        private int? ObtenerUsuarioIdActual()
        {
            var claimValue =
                User.FindFirst("UsuarioID")?.Value
                ?? User.FindFirst("UserId")?.Value
                ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (int.TryParse(claimValue, out var usuarioID) &&
                usuarioID > 0)
            {
                return usuarioID;
            }

            try
            {
                var sessionID =
                    HttpContext.Session.GetInt32("UsuarioID");

                if (sessionID.HasValue &&
                    sessionID.Value > 0)
                {
                    return sessionID.Value;
                }
            }
            catch
            {
                // La sesión puede no estar disponible.
            }

            return null;
        }

        private static void NormalizarSolicitudManual(
            GP12SolicitudManualViewModel model)
        {
            model.OrdenFabricacion =
                Limpiar(model.OrdenFabricacion);

            model.ClienteNombre =
                Limpiar(model.ClienteNombre);

            model.NumeroParte =
                model.NumeroParte?.Trim()
                ?? string.Empty;

            model.DescripcionParte =
                Limpiar(model.DescripcionParte);

            model.MaterialCodigo =
                Limpiar(model.MaterialCodigo);

            model.MaterialDescripcion =
                Limpiar(model.MaterialDescripcion);

            model.Motivo =
                model.Motivo?.Trim()
                ?? string.Empty;

            model.InstruccionTrabajo =
                Limpiar(model.InstruccionTrabajo);

            model.CodigoHIP =
                Limpiar(model.CodigoHIP);

            model.CodigoHOE =
                Limpiar(model.CodigoHOE);

            model.Observaciones =
                Limpiar(model.Observaciones);
        }

        private static string? Limpiar(string? valor)
        {
            return string.IsNullOrWhiteSpace(valor)
                ? null
                : valor.Trim();
        }

        private static string ConstruirNombrePersona(
            string? nombre,
            string? apellidoPaterno,
            string? apellidoMaterno)
        {
            var partes = new List<string>();

            if (!string.IsNullOrWhiteSpace(nombre))
                partes.Add(nombre.Trim());

            if (!string.IsNullOrWhiteSpace(apellidoPaterno))
                partes.Add(apellidoPaterno.Trim());

            if (!string.IsNullOrWhiteSpace(apellidoMaterno))
                partes.Add(apellidoMaterno.Trim());

            return string.Join(" ", partes);
        }

        private static string NormalizarCodigoComparacion(string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor)) return string.Empty;
            return new string(valor.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        }
        private static void AgregarDecimal(
            SqlCommand cmd,
            string nombre,
            decimal valor)
        {
            var parametro =
                cmd.Parameters.Add(
                    nombre,
                    SqlDbType.Decimal);

            parametro.Precision = 18;
            parametro.Scale = 4;
            parametro.Value = valor;
        }

        private static void AgregarNullable(
            SqlCommand cmd,
            string nombre,
            SqlDbType tipo,
            int longitud,
            string? valor)
        {
            cmd.Parameters.Add(
                nombre,
                tipo,
                longitud).Value =
                string.IsNullOrWhiteSpace(valor)
                    ? DBNull.Value
                    : valor.Trim();
        }

        private static int LeerInt(object valor)
        {
            return valor == DBNull.Value
                ? 0
                : Convert.ToInt32(valor);
        }

        private static int? LeerIntNullable(object valor)
        {
            return valor == DBNull.Value
                ? null
                : Convert.ToInt32(valor);
        }

        private static DateTime? LeerFechaNullable(object valor)
        {
            return valor == DBNull.Value
                ? null
                : Convert.ToDateTime(valor);
        }

        private static TimeSpan? LeerHoraNullable(object valor)
        {
            return valor == DBNull.Value
                ? null
                : (TimeSpan)valor;
        }

        private sealed class GP12BonusOrigenHoraData
        {
            public int EjecucionProduccionID { get; set; }
            public int RegistroHoraID { get; set; }
            public int OperadorID { get; set; }
            public int CantidadCaja { get; set; }
            public long SaldoBonus { get; set; }
            public DateTime FechaProduccion { get; set; }
            public TimeSpan HoraInicio { get; set; }
            public TimeSpan HoraFin { get; set; }
            public int CapacidadDescuento { get; set; }
            public int ScrapAsignado { get; set; }
            public decimal Fraccion { get; set; }
        }
    }
}
