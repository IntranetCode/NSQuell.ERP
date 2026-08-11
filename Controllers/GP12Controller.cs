using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Security.Claims;
using System.Threading.Tasks;

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

        // =========================================================
        // RECEPCIÓN DE MATERIAL POR CLASIFICACIÓN
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarRecepcion(
            GP12RecepcionViewModel model)
        {
            model.Referencia = Limpiar(model.Referencia);
            model.Observaciones = Limpiar(model.Observaciones);

            if (!ModelState.IsValid)
            {
                TempData["Error"] =
                    "Revisa la información de la recepción.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.SolicitudGP12ID });
            }

            if (model.CantidadTotal <= 0)
            {
                TempData["Error"] =
                    "Captura al menos una cantidad de material para recibir.";

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
SELECT
    EstatusID,
    CantidadSolicitada,
    CantidadRecibida,
    CantidadProcesada
FROM dbo.GP12_Solicitudes
WITH (UPDLOCK, HOLDLOCK)
WHERE SolicitudGP12ID = @SolicitudGP12ID
  AND Activo = 1;";

                int estatusAnterior;
                decimal cantidadSolicitada;
                decimal cantidadRecibida;
                decimal cantidadProcesada;

                await using (var cmd =
                    new SqlCommand(sqlSolicitud, cn, tx))
                {
                    cmd.Parameters.Add(
                        "@SolicitudGP12ID",
                        SqlDbType.Int).Value =
                        model.SolicitudGP12ID;

                    await using var rd =
                        await cmd.ExecuteReaderAsync();

                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();
                        return NotFound();
                    }

                    estatusAnterior =
                        Convert.ToInt32(rd["EstatusID"]);

                    cantidadSolicitada =
                        Convert.ToDecimal(rd["CantidadSolicitada"]);

                    cantidadRecibida =
                        Convert.ToDecimal(rd["CantidadRecibida"]);

                    cantidadProcesada =
                        Convert.ToDecimal(rd["CantidadProcesada"]);
                }

                if (GP12Estatus.EsFinal(estatusAnterior))
                {
                    throw new InvalidOperationException(
                        "La solicitud está cerrada o cancelada y ya no puede recibir material.");
                }

                const string sqlEtiquetas = @"
SELECT
    SolicitudEtiquetaID,
    TipoEtiqueta,
    CantidadSolicitada,
    CantidadRecibida,
    CantidadProcesada
FROM dbo.GP12_SolicitudEtiquetas
WITH (UPDLOCK, HOLDLOCK)
WHERE SolicitudGP12ID = @SolicitudGP12ID
  AND Activo = 1;";

                var etiquetas =
                    new List<GP12SolicitudEtiquetaData>();

                await using (var cmd =
                    new SqlCommand(sqlEtiquetas, cn, tx))
                {
                    cmd.Parameters.Add(
                        "@SolicitudGP12ID",
                        SqlDbType.Int).Value =
                        model.SolicitudGP12ID;

                    await using var rd =
                        await cmd.ExecuteReaderAsync();

                    while (await rd.ReadAsync())
                    {
                        etiquetas.Add(
                            new GP12SolicitudEtiquetaData
                            {
                                SolicitudEtiquetaID =
                                    Convert.ToInt32(
                                        rd["SolicitudEtiquetaID"]),

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
                                        rd["CantidadProcesada"])
                            });
                    }
                }

                if (etiquetas.Count == 0)
                {
                    throw new InvalidOperationException(
                        "La solicitud no tiene clasificación de material configurada.");
                }

                var capturas = new[]
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
                    },
                    new
                    {
                        Tipo = GP12TipoEtiqueta.SinClasificar,
                        Cantidad = model.CantidadSinClasificar
                    }
                };

                var totalRecepcion = 0m;
                var detalleHistorial = new List<string>();

                const string sqlUpdateEtiqueta = @"
UPDATE dbo.GP12_SolicitudEtiquetas
SET
    CantidadRecibida = CantidadRecibida + @Cantidad,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = SYSDATETIME()
WHERE SolicitudEtiquetaID = @SolicitudEtiquetaID
  AND SolicitudGP12ID = @SolicitudGP12ID
  AND Activo = 1;";

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
    NULL,
    NULL,
    @Referencia,
    @Observaciones,
    SYSDATETIME(),
    @UsuarioID,
    1
);";

                foreach (var captura in capturas)
                {
                    if (captura.Cantidad <= 0)
                        continue;

                    var etiqueta = etiquetas.Find(
                        x => string.Equals(
                            x.TipoEtiqueta,
                            captura.Tipo,
                            StringComparison.OrdinalIgnoreCase));

                    if (etiqueta == null)
                    {
                        throw new InvalidOperationException(
                            $"La solicitud no tiene una clasificación {GP12TipoEtiqueta.Nombre(captura.Tipo)} disponible para recibir.");
                    }

                    var faltante =
                        etiqueta.CantidadSolicitada -
                        etiqueta.CantidadRecibida;

                    if (captura.Cantidad > faltante)
                    {
                        throw new InvalidOperationException(
                            $"La recepción de material {GP12TipoEtiqueta.Nombre(captura.Tipo)} excede lo pendiente. " +
                            $"Pendiente: {Math.Max(0, faltante):N4}.");
                    }

                    await using (var cmd =
                        new SqlCommand(sqlUpdateEtiqueta, cn, tx))
                    {
                        AgregarDecimal(
                            cmd,
                            "@Cantidad",
                            captura.Cantidad);

                        cmd.Parameters.Add(
                            "@UsuarioID",
                            SqlDbType.Int).Value =
                            usuarioID.Value;

                        cmd.Parameters.Add(
                            "@SolicitudEtiquetaID",
                            SqlDbType.Int).Value =
                            etiqueta.SolicitudEtiquetaID;

                        cmd.Parameters.Add(
                            "@SolicitudGP12ID",
                            SqlDbType.Int).Value =
                            model.SolicitudGP12ID;

                        await cmd.ExecuteNonQueryAsync();
                    }

                    await using (var cmd =
                        new SqlCommand(sqlMovimiento, cn, tx))
                    {
                        cmd.Parameters.Add(
                            "@SolicitudGP12ID",
                            SqlDbType.Int).Value =
                            model.SolicitudGP12ID;

                        cmd.Parameters.Add(
                            "@SolicitudEtiquetaID",
                            SqlDbType.Int).Value =
                            etiqueta.SolicitudEtiquetaID;

                        AgregarDecimal(
                            cmd,
                            "@Cantidad",
                            captura.Cantidad);

                        AgregarNullable(
                            cmd,
                            "@Referencia",
                            SqlDbType.NVarChar,
                            500,
                            model.Referencia);

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

                        await cmd.ExecuteNonQueryAsync();
                    }

                    totalRecepcion += captura.Cantidad;

                    detalleHistorial.Add(
                        $"{GP12TipoEtiqueta.Nombre(captura.Tipo)}: {captura.Cantidad:N4}");
                }

                var nuevaCantidadRecibida =
                    cantidadRecibida + totalRecepcion;

                if (nuevaCantidadRecibida > cantidadSolicitada)
                {
                    throw new InvalidOperationException(
                        "La recepción total excede la cantidad solicitada de la solicitud GP12.");
                }

                var nuevoEstatus =
                    estatusAnterior < GP12Estatus.PendienteProgramar
                        ? GP12Estatus.PendienteProgramar
                        : estatusAnterior;

                var nuevaCantidadPendiente =
                    Math.Max(
                        0,
                        nuevaCantidadRecibida -
                        cantidadProcesada);

                const string sqlUpdate = @"
UPDATE dbo.GP12_Solicitudes
SET
    CantidadRecibida = @CantidadRecibida,
    CantidadPendiente = @CantidadPendiente,
    FechaRecepcion = COALESCE(FechaRecepcion, SYSDATETIME()),
    UsuarioRecepcionID = COALESCE(UsuarioRecepcionID, @UsuarioID),
    EstatusID = @EstatusID,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = SYSDATETIME()
WHERE SolicitudGP12ID = @SolicitudGP12ID
  AND Activo = 1;";

                await using (var cmd =
                    new SqlCommand(sqlUpdate, cn, tx))
                {
                    AgregarDecimal(
                        cmd,
                        "@CantidadRecibida",
                        nuevaCantidadRecibida);

                    AgregarDecimal(
                        cmd,
                        "@CantidadPendiente",
                        nuevaCantidadPendiente);

                    cmd.Parameters.Add(
                        "@EstatusID",
                        SqlDbType.Int).Value =
                        nuevoEstatus;

                    cmd.Parameters.Add(
                        "@UsuarioID",
                        SqlDbType.Int).Value =
                        usuarioID.Value;

                    cmd.Parameters.Add(
                        "@SolicitudGP12ID",
                        SqlDbType.Int).Value =
                        model.SolicitudGP12ID;

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
                    $"Recepción de {totalRecepcion:N4} pieza(s). " +
                    string.Join(" · ", detalleHistorial),
                    usuarioID.Value);

                await tx.CommitAsync();

                TempData["Mensaje"] =
                    "Material recibido correctamente y registrado por clasificación en el inventario GP12.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "No fue posible registrar la recepción: " +
                    ex.Message;
            }

            return RedirectToAction(
                nameof(Detalle),
                new { id = model.SolicitudGP12ID });
        }

        // =========================================================
        // PROGRAMACIÓN POR CLASIFICACIÓN
        // =========================================================
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
        private static async Task LiberarCajaOrigenCalidadAsync(int solicitudGP12Id, decimal cantidadOK, string? observaciones, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            const string sqlOrigen = @"
SELECT TOP (1)
    s.CajaProduccionID,
    s.CajaLiberadaID,
    s.CalidadInspeccionID,
    s.CantidadSolicitada,
    s.Origen,
    c.FolioCaja
FROM dbo.GP12_Solicitudes s WITH (UPDLOCK,HOLDLOCK)
LEFT JOIN dbo.Calidad_CajasLiberadas c
    ON c.CajaLiberadaID=s.CajaLiberadaID
   AND c.Activo=1
WHERE s.SolicitudGP12ID=@SolicitudGP12ID
  AND s.Activo=1;";
            long? cajaProduccionId;
            int? cajaLiberadaId;
            int? inspeccionId;
            decimal cantidadSolicitada;
            string origen;
            string folioCaja;
            await using (var cmd = new SqlCommand(sqlOrigen, cn, tx))
            {
                cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value = solicitudGP12Id;
                await using var rd = await cmd.ExecuteReaderAsync();
                if (!await rd.ReadAsync()) throw new InvalidOperationException("No se encontró la solicitud GP12.");
                cajaProduccionId = rd["CajaProduccionID"] == DBNull.Value ? null : Convert.ToInt64(rd["CajaProduccionID"]);
                cajaLiberadaId = rd["CajaLiberadaID"] == DBNull.Value ? null : Convert.ToInt32(rd["CajaLiberadaID"]);
                inspeccionId = rd["CalidadInspeccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["CalidadInspeccionID"]);
                cantidadSolicitada = Convert.ToDecimal(rd["CantidadSolicitada"]);
                origen = rd["Origen"]?.ToString()?.Trim().ToUpperInvariant() ?? string.Empty;
                folioCaja = rd["FolioCaja"] == DBNull.Value ? string.Empty : rd["FolioCaja"].ToString()?.Trim() ?? string.Empty;
            }
            if (origen != GP12Origen.Calidad) return;
            if (!cajaProduccionId.HasValue || cajaProduccionId.Value <= 0) throw new InvalidOperationException("La solicitud GP12 proveniente de Calidad no tiene CajaProduccionID.");
            if (!cajaLiberadaId.HasValue || cajaLiberadaId.Value <= 0) throw new InvalidOperationException("La solicitud GP12 proveniente de Calidad no tiene CajaLiberadaID.");
            if (!inspeccionId.HasValue || inspeccionId.Value <= 0) throw new InvalidOperationException("La solicitud GP12 proveniente de Calidad no tiene CalidadInspeccionID.");
            if (cantidadOK <= 0) throw new InvalidOperationException("La cantidad liberada por GP12 debe ser mayor que cero.");
            if (cantidadOK != cantidadSolicitada) throw new InvalidOperationException($"Para liberar la caja completa desde GP12 deben quedar conformes las {cantidadSolicitada:N0} pieza(s).");
            const string sql = @"
DECLARE @EstadoCajaActual INT;
SELECT @EstadoCajaActual=EstadoCajaID
FROM dbo.Produccion_Cajas WITH (UPDLOCK,HOLDLOCK)
WHERE CajaProduccionID=@CajaProduccionID
  AND Activo=1;
IF @EstadoCajaActual IS NULL
    THROW 51200,'No se encontró la caja activa de Producción.',1;
IF @EstadoCajaActual IN (@SalidaProduccion,@RecibidaAlmacen)
    THROW 51201,'La caja ya salió de Producción o fue recibida en Almacén.',1;
UPDATE dbo.Calidad_CajasLiberadas
SET EtiquetaLiberacion=N'VERDE',
    Destino=N'ALMACEN',
    Estado=N'LIBERADA',
    FechaValidacionCalidad=@Ahora,
    UsuarioValidacionCalidadID=@UsuarioID,
    Observaciones=CASE
        WHEN @Observaciones IS NULL THEN
            CASE WHEN Observaciones IS NULL OR LTRIM(RTRIM(Observaciones))=N''
                THEN N'Caja liberada por GP12. Pendiente escaneo de salida en Producción hacia Almacén PT.'
                ELSE Observaciones+CHAR(13)+CHAR(10)+N'Caja liberada por GP12. Pendiente escaneo de salida en Producción hacia Almacén PT.'
            END
        WHEN Observaciones IS NULL OR LTRIM(RTRIM(Observaciones))=N'' THEN @Observaciones
        ELSE Observaciones+CHAR(13)+CHAR(10)+@Observaciones
    END,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=@Ahora
WHERE CajaLiberadaID=@CajaLiberadaID
  AND Activo=1;
IF @@ROWCOUNT<>1
    THROW 51202,'No fue posible actualizar la caja registrada en Calidad.',1;
UPDATE dbo.Produccion_Cajas
SET EstadoCajaID=@ZonaVerde,
    EstadoCajaNombre=@NombreZonaVerde,
    EstatusCalidad=N'LIBERADA',
    ResultadoCalidad=N'LIBERADA_GP12',
    MotivoCalidad=NULL,
    EtiquetaVerde=1,
    FechaLiberacionCalidad=@Ahora,
    UsuarioLiberacionCalidadID=@UsuarioID,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=@Ahora
WHERE CajaProduccionID=@CajaProduccionID
  AND Activo=1;
IF @@ROWCOUNT<>1
    THROW 51203,'No fue posible regresar la caja liberada a Producción.',1;
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
VALUES
(
    @InspeccionID,
    N'GP12_LIBERADO',
    NULL,
    NULL,
    N'LIBERADA_GP12',
    N'VERDE',
    @Comentario,
    @UsuarioID,
    @Ahora
);";
            var ahora = DateTime.Now;
            var comentario = $"GP12 liberó la caja {folioCaja}. {cantidadOK:N0} pieza(s) conformes. La caja regresó a Producción para escaneo de salida hacia Almacén PT." + (string.IsNullOrWhiteSpace(observaciones) ? string.Empty : $" Observaciones: {observaciones.Trim()}");
            await using var cmdUpdate = new SqlCommand(sql, cn, tx);
            cmdUpdate.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = cajaProduccionId.Value;
            cmdUpdate.Parameters.Add("@CajaLiberadaID", SqlDbType.Int).Value = cajaLiberadaId.Value;
            cmdUpdate.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId.Value;
            cmdUpdate.Parameters.Add("@ZonaVerde", SqlDbType.Int).Value = ProduccionCajaEstatus.ZonaVerde;
            cmdUpdate.Parameters.Add("@NombreZonaVerde", SqlDbType.NVarChar, 100).Value = ProduccionCajaEstatus.Nombre(ProduccionCajaEstatus.ZonaVerde);
            cmdUpdate.Parameters.Add("@SalidaProduccion", SqlDbType.Int).Value = ProduccionCajaEstatus.SalidaProduccion;
            cmdUpdate.Parameters.Add("@RecibidaAlmacen", SqlDbType.Int).Value = ProduccionCajaEstatus.RecibidaAlmacenPt;
            cmdUpdate.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            cmdUpdate.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
            cmdUpdate.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = string.IsNullOrWhiteSpace(observaciones) ? DBNull.Value : observaciones.Trim();
            cmdUpdate.Parameters.Add("@Comentario", SqlDbType.NVarChar, 1000).Value = comentario;
            await cmdUpdate.ExecuteNonQueryAsync();
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


        private async Task<GP12DetalleViewModel?> ConstruirDetalleAsync(int solicitudGP12ID)
        {
            var model = await CargarEncabezadoAsync(solicitudGP12ID);
            if (model == null) return null;
            await CargarEtiquetasAsync(model);
            await CargarInventarioAsync(model);
            await CargarProgramacionesAsync(model);
            await CargarAsignacionesAsync(model);
            await CargarInspeccionesAsync(model);
            await CargarHistorialAsync(model);
            await CargarPersonalDisponibleAsync(model);
            await CargarCatalogoDefectosAsync(model);
            return model;
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
            if (cantidadRetrabajada > cantidadRevisada || cantidadScrap > cantidadRevisada)
            {
                TempData["Error"] = "Retrabajo o scrap no pueden superar la cantidad revisada.";
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
SELECT TOP (1)
    i.AsignacionGP12ID,
    i.FechaFin,
    a.CantidadAsignada,
    p.SolicitudEtiquetaID,
    s.EstatusID,
    s.Origen,
    ISNULL(s.CantidadSolicitada,0) AS CantidadSolicitada,
    ISNULL(s.CantidadRecibida,0) AS CantidadRecibida
FROM dbo.GP12_Inspecciones i WITH (UPDLOCK,HOLDLOCK)
INNER JOIN dbo.GP12_Asignaciones a WITH (UPDLOCK,HOLDLOCK)
    ON a.AsignacionGP12ID=i.AsignacionGP12ID
INNER JOIN dbo.GP12_Programacion p
    ON p.ProgramacionGP12ID=a.ProgramacionGP12ID
INNER JOIN dbo.GP12_Solicitudes s WITH (UPDLOCK,HOLDLOCK)
    ON s.SolicitudGP12ID=i.SolicitudGP12ID
WHERE i.InspeccionGP12ID=@InspeccionGP12ID
  AND i.SolicitudGP12ID=@SolicitudGP12ID
  AND i.Activo=1
  AND a.Activo=1
  AND s.Activo=1;";
                int asignacionGP12ID;
                int? solicitudEtiquetaID;
                int estatusAnterior;
                string origen;
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
                    origen = rd["Origen"] as string ?? string.Empty;
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
FROM dbo.GP12_Asignaciones WITH (UPDLOCK,HOLDLOCK)
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
                if (procesoCompleto && string.Equals(origen, GP12Origen.Calidad, StringComparison.OrdinalIgnoreCase))
                {
                    if (totalNOK == 0 && totalScrap == 0 && totalOK >= cantidadSolicitada)
                    {
                        await LiberarCajaOrigenCalidadAsync(solicitudGP12ID, totalOK, observaciones, usuarioID.Value, cn, tx);
                    }
                    else
                    {
                        await MantenerCajaOrigenCalidadEnGP12Async(solicitudGP12ID, totalNOK, totalScrap, observaciones, usuarioID.Value, cn, tx);
                    }
                }
                await AgregarHistorialAsync(cn, tx, solicitudGP12ID, GP12Movimientos.InspeccionTerminada, estatusAnterior, nuevoEstatus, GP12EntidadHistorial.Inspeccion, inspeccionGP12ID, $"Inspección GP12 terminada. Revisadas: {cantidadRevisada:N4}; OK: {cantidadOK:N4}; NOK: {cantidadNOK:N4}; retrabajadas: {cantidadRetrabajada:N4}; scrap: {cantidadScrap:N4}.", usuarioID.Value);
                await tx.CommitAsync();
                TempData["Mensaje"] = procesoCompleto
                    ? totalNOK == 0 && totalScrap == 0
                        ? "Inspección GP12 terminada. El material conforme fue liberado."
                        : "Inspección GP12 terminada con material NOK. La caja permanece en GP12."
                    : "Inspección GP12 registrada. Aún existe material pendiente por procesar.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible finalizar la inspección GP12: " + ex.Message;
            }
            return RedirectToAction(nameof(Detalle), new { id = solicitudGP12ID });
        }

        private static async Task MantenerCajaOrigenCalidadEnGP12Async(int solicitudGP12Id, decimal cantidadNOK, decimal cantidadScrap, string? observaciones, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
DECLARE @CajaProduccionID BIGINT;
DECLARE @CajaLiberadaID INT;
DECLARE @InspeccionID INT;
DECLARE @Origen NVARCHAR(20);
SELECT
    @CajaProduccionID=CajaProduccionID,
    @CajaLiberadaID=CajaLiberadaID,
    @InspeccionID=CalidadInspeccionID,
    @Origen=Origen
FROM dbo.GP12_Solicitudes WITH (UPDLOCK,HOLDLOCK)
WHERE SolicitudGP12ID=@SolicitudGP12ID
  AND Activo=1;
IF UPPER(LTRIM(RTRIM(ISNULL(@Origen,N''))))<>N'CALIDAD'
    RETURN;
IF @CajaProduccionID IS NULL OR @CajaLiberadaID IS NULL OR @InspeccionID IS NULL
    THROW 51310,'La solicitud GP12 no conserva la trazabilidad completa con Calidad y Producción.',1;
UPDATE dbo.Calidad_CajasLiberadas
SET EtiquetaLiberacion=N'AMARILLA',
    Destino=N'GP12',
    Estado=N'EN_GP12',
    Observaciones=
        CASE
            WHEN @Observaciones IS NULL THEN Observaciones
            WHEN Observaciones IS NULL OR LTRIM(RTRIM(Observaciones))=N'' THEN @Observaciones
            ELSE Observaciones+CHAR(13)+CHAR(10)+@Observaciones
        END,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=@Ahora
WHERE CajaLiberadaID=@CajaLiberadaID
  AND Activo=1;
IF @@ROWCOUNT<>1
    THROW 51311,'No fue posible mantener la caja en GP12 desde Calidad.',1;
UPDATE dbo.Produccion_Cajas
SET EstadoCajaID=4,
    EstadoCajaNombre=N'GP12 - pendiente de disposición',
    EstatusCalidad=N'GP12',
    ResultadoCalidad=N'GP12_NOK',
    EtiquetaVerde=0,
    MotivoCalidad=@Observaciones,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=@Ahora
WHERE CajaProduccionID=@CajaProduccionID
  AND Activo=1;
IF @@ROWCOUNT<>1
    THROW 51312,'No fue posible actualizar la caja de Producción en GP12.',1;
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
VALUES
(
    @InspeccionID,
    N'GP12_REVISION_NOK',
    NULL,
    NULL,
    N'GP12_NOK',
    N'AMARILLA',
    @Comentario,
    @UsuarioID,
    @Ahora
);";
            var ahora = DateTime.Now;
            var comentario = $"GP12 terminó revisión con material pendiente de disposición. NOK: {cantidadNOK:N0}; scrap detectado: {cantidadScrap:N0}." + (string.IsNullOrWhiteSpace(observaciones) ? string.Empty : $" Observaciones: {observaciones.Trim()}");
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value = solicitudGP12Id;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
            cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = string.IsNullOrWhiteSpace(observaciones) ? DBNull.Value : observaciones.Trim();
            cmd.Parameters.Add("@Comentario", SqlDbType.NVarChar, 1000).Value = comentario;
            await cmd.ExecuteNonQueryAsync();
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
    }
}
