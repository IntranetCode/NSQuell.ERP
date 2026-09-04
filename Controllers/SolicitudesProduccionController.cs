using ERP.NSQuell.Models.ERP;
using ERP.NSQuell.Servicios;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers
{
    public class SolicitudesProduccionController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly NotificacionEventoService _notificacionEventoService;

        public SolicitudesProduccionController(
            IConfiguration configuration,
            NotificacionEventoService notificacionEventoService)
        {
            _configuration = configuration;
            _notificacionEventoService = notificacionEventoService;
        }

        private string ConnectionString =>
            _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("No se encontró la cadena de conexión DefaultConnection.");

        // index
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var lista = new List<SolicitudProduccionIndexVm>();

            const string sql = @"
SELECT
    s.SolicitudProduccionID,
    s.FolioSolicitud,
    s.NumeroOFRecibida,
    s.FechaSolicitud,
    s.FechaRequerida,
    ISNULL(c.Nombre, s.ClienteNombre) AS Cliente,
    s.Prioridad,
    s.EstatusID,
    COUNT(DISTINCT d.SolicitudProduccionDetalleID) AS TotalRenglones,
    ISNULL(SUM(d.CantidadPiezas), 0) AS TotalPiezas
FROM dbo.SolicitudesProduccion s
LEFT JOIN dbo.ERP_Clientes c
    ON c.ClienteID = s.ClienteID
LEFT JOIN dbo.SolicitudesProduccionDetalle d
    ON d.SolicitudProduccionID = s.SolicitudProduccionID
   AND d.Activo = 1
WHERE s.Activo = 1
GROUP BY
    s.SolicitudProduccionID,
    s.FolioSolicitud,
    s.NumeroOFRecibida,
    s.FechaSolicitud,
    s.FechaRequerida,
    ISNULL(c.Nombre, s.ClienteNombre),
    s.Prioridad,
    s.EstatusID,
    s.FechaCreacion
ORDER BY s.FechaCreacion DESC;";

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var estatusID = rd.GetInt32(rd.GetOrdinal("EstatusID"));

                lista.Add(new SolicitudProduccionIndexVm
                {
                    SolicitudProduccionID = rd.GetInt32(rd.GetOrdinal("SolicitudProduccionID")),
                    FolioSolicitud = rd["FolioSolicitud"] as string,
                    NumeroOFRecibida = rd["NumeroOFRecibida"] as string,
                    FechaSolicitud = rd.GetDateTime(rd.GetOrdinal("FechaSolicitud")),
                    FechaRequerida = rd["FechaRequerida"] == DBNull.Value ? null : rd.GetDateTime(rd.GetOrdinal("FechaRequerida")),
                    Cliente = rd["Cliente"] as string,
                    Prioridad = rd["Prioridad"] as string ?? "Normal",
                    EstatusID = estatusID,
                    EstatusNombre = SolicitudProduccionEstatus.Nombre(estatusID),
                    TotalRenglones = Convert.ToInt32(rd["TotalRenglones"]),
                    TotalPiezas = Convert.ToInt32(rd["TotalPiezas"])
                });
            }

            return View(lista);
        }

        // get crear
        [HttpGet]
        public async Task<IActionResult> Crear()
        {
            var vm = new SolicitudProduccionCrearVm
            {
                FolioSolicitud = null,
                FechaSolicitud = DateTime.Today,
                Prioridad = "Normal",
                OrigenSolicitud = "Manual"
            };

            vm.Detalles.Add(new SolicitudProduccionDetalleCrearVm
            {
                Renglon = 1
            });

            await CargarCatalogosAsync(vm);

            return View(vm);
        }

        // post crear
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Crear(SolicitudProduccionCrearVm vm)
        {
            // El folio interno siempre lo controla el servidor.
            // Se ignora cualquier texto enviado por la vista, por ejemplo:
            // "Se asignará al guardar".
            vm.FolioSolicitud = null;
            ModelState.Remove(nameof(vm.FolioSolicitud));

            var usuarioId = ObtenerUsuarioID();

            if (usuarioId <= 0)
            {
                ModelState.AddModelError("", "No se pudo identificar el usuario de sesión.");
            }

            vm.Detalles = vm.Detalles
                .Where(d =>
                    d.CantidadPiezas > 0 &&
                    (
                        d.ParteID.HasValue ||
                        !string.IsNullOrWhiteSpace(d.ReferenciaSAP) ||
                        !string.IsNullOrWhiteSpace(d.DesignacionDescripcionSAP)
                    ))
                .ToList();

            if (!vm.Detalles.Any())
            {
                ModelState.AddModelError("", "Debes capturar al menos un renglón de producción.");
            }

            if (!vm.ClienteID.HasValue && string.IsNullOrWhiteSpace(vm.ClienteNombre))
            {
                ModelState.AddModelError("", "Selecciona o captura el cliente.");
            }

            foreach (var detalle in vm.Detalles)
            {
                var asignacionesValidas = detalle.AsignacionesMaquina
                    .Where(a => a.MaquinaID > 0 && a.CantidadAsignada > 0)
                    .ToList();

                var totalAsignado = asignacionesValidas.Sum(a => a.CantidadAsignada);

                if (totalAsignado > 0 && totalAsignado != detalle.CantidadPiezas)
                {
                    ModelState.AddModelError(
                        "",
                        $"En el renglón {detalle.Renglon}, la cantidad asignada a máquinas ({totalAsignado}) debe coincidir con la cantidad de piezas ({detalle.CantidadPiezas})."
                    );
                }
            }

            if (!ModelState.IsValid)
            {
                await CargarCatalogosAsync(vm);
                return View(vm);
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var tx = await cn.BeginTransactionAsync();

            try
            {
                // Siempre se genera un folio nuevo dentro de la transacción.
                // Nunca se usa el valor recibido desde el navegador.
                vm.FolioSolicitud = await GenerarFolioAsync(
                    cn,
                    (SqlTransaction)tx
                );

                var clienteNombre = vm.ClienteNombre;

                if (vm.ClienteID.HasValue)
                {
                    clienteNombre = await ObtenerClienteNombreAsync(vm.ClienteID.Value, cn, (SqlTransaction)tx);
                }

                var solicitudId = await InsertarSolicitudAsync(vm, clienteNombre, usuarioId, cn, (SqlTransaction)tx);

                // Toda OF manual entra al mismo flujo de Planeación mediante
                // un Release interno generado dentro de la misma transacción.
                var releaseId = await InsertarReleaseAutomaticoAsync(
                    vm,
                    clienteNombre,
                    usuarioId,
                    cn,
                    (SqlTransaction)tx
                );

                var renglon = 1;

                foreach (var d in vm.Detalles)
                {
                    await CompletarDetalleDesdeParteAsync(d, cn, (SqlTransaction)tx);

                    var detalleId = await InsertarDetalleAsync(
                        solicitudId,
                        renglon,
                        d,
                        cn,
                        (SqlTransaction)tx
                    );

                    var releaseRenglonId = await InsertarReleaseRenglonAutomaticoAsync(
                        releaseId,
                        renglon,
                        d,
                        usuarioId,
                        cn,
                        (SqlTransaction)tx
                    );

                    var asignacionesValidas = d.AsignacionesMaquina
                        .Where(a => a.MaquinaID > 0 && a.CantidadAsignada > 0)
                        .ToList();

                    var secuenciaEntrega = 1;

                    foreach (var a in asignacionesValidas)
                    {
                        await InsertarAsignacionMaquinaAsync(
                            detalleId,
                            a,
                            d.MoldeID,
                            usuarioId,
                            cn,
                            (SqlTransaction)tx
                        );

                        await InsertarReleaseDetalleAutomaticoAsync(
                            releaseId,
                            releaseRenglonId,
                            solicitudId,
                            renglon,
                            secuenciaEntrega,
                            vm,
                            d,
                            a,
                            usuarioId,
                            cn,
                            (SqlTransaction)tx
                        );

                        secuenciaEntrega++;
                    }

                    // Una OF puede capturarse todavía sin asignación de máquina.
                    // En ese caso se genera una necesidad completa para que
                    // Planeación seleccione posteriormente máquina y horario.
                    if (!asignacionesValidas.Any())
                    {
                        await InsertarReleaseDetalleAutomaticoAsync(
                            releaseId,
                            releaseRenglonId,
                            solicitudId,
                            renglon,
                            1,
                            vm,
                            d,
                            null,
                            usuarioId,
                            cn,
                            (SqlTransaction)tx
                        );
                    }

                    renglon++;
                }

                await VincularSolicitudConReleaseAsync(
                    solicitudId,
                    releaseId,
                    usuarioId,
                    cn,
                    (SqlTransaction)tx
                );

                await InsertarHistorialAsync(
                    solicitudId,
                    null,
                    SolicitudProduccionEstatus.Capturada,
                    "Creación de solicitud de producción",
                    "Solicitud capturada por Gestión Comercial.",
                    usuarioId,
                    cn,
                    (SqlTransaction)tx
                );

                await tx.CommitAsync();
                // NSQ_NOTIFICACIONES_V3_INTERNO_OF_CREADA
                // Evento explicito DESPUES del commit. El servicio registra cualquier
                // fallo y nunca revierte la OF ya confirmada.
                await _notificacionEventoService.PublicarOfCreadaAsync(
                    solicitudId,
                    usuarioId);

                TempData["Success"] = "Solicitud de producción creada correctamente.";
                return RedirectToAction(nameof(Detalle), new { id = solicitudId });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                ModelState.AddModelError("", "Ocurrió un error al guardar la solicitud: " + ex.Message);
                await CargarCatalogosAsync(vm);
                return View(vm);
            }
        }

        // detalle
        [HttpGet]
        public async Task<IActionResult> Detalle(int id)
        {
            SolicitudProduccionDetalleVistaVm? vm = null;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            const string sqlSolicitud = @"
SELECT
    s.SolicitudProduccionID,
    s.FolioSolicitud,
    s.NumeroOFRecibida,
    s.FechaSolicitud,
    s.FechaRequerida,
    ISNULL(c.Nombre, s.ClienteNombre) AS Cliente,
    s.Prioridad,
    s.EstatusID,
    s.NotasGenerales
FROM dbo.SolicitudesProduccion s
LEFT JOIN dbo.ERP_Clientes c
    ON c.ClienteID = s.ClienteID
WHERE s.SolicitudProduccionID = @SolicitudProduccionID
  AND s.Activo = 1;";

            await using (var cmd = new SqlCommand(sqlSolicitud, cn))
            {
                cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = id;

                await using var rd = await cmd.ExecuteReaderAsync();

                if (await rd.ReadAsync())
                {
                    var estatusID = rd.GetInt32(rd.GetOrdinal("EstatusID"));

                    vm = new SolicitudProduccionDetalleVistaVm
                    {
                        SolicitudProduccionID = rd.GetInt32(rd.GetOrdinal("SolicitudProduccionID")),
                        FolioSolicitud = rd["FolioSolicitud"] as string,
                        NumeroOFRecibida = rd["NumeroOFRecibida"] as string,
                        FechaSolicitud = rd.GetDateTime(rd.GetOrdinal("FechaSolicitud")),
                        FechaRequerida = rd["FechaRequerida"] == DBNull.Value ? null : rd.GetDateTime(rd.GetOrdinal("FechaRequerida")),
                        Cliente = rd["Cliente"] as string,
                        Prioridad = rd["Prioridad"] as string ?? "Normal",
                        EstatusID = estatusID,
                        EstatusNombre = SolicitudProduccionEstatus.Nombre(estatusID),
                        NotasGenerales = rd["NotasGenerales"] as string
                    };
                }
            }

            if (vm == null)
            {
                return NotFound();
            }

            vm.Detalles = await ObtenerDetallesAsync(id, cn);

            foreach (var detalle in vm.Detalles)
            {
                detalle.AsignacionesMaquina = await ObtenerAsignacionesAsync(detalle.SolicitudProduccionDetalleID, cn);
            }

            vm.Historial = await ObtenerHistorialAsync(id, cn);
            // NSQ_OF_TRAZABILIDAD_V3E
            ViewBag.TrazabilidadOF = await ObtenerTrazabilidadCompletaAsync(id, cn);
            ViewBag.AlertasOF = await ObtenerAlertasActivasAsync(id, cn);
            ViewBag.EstadoActualOF = await ObtenerEstadoActualAsync(id, cn);
            ViewBag.SoloLectura = string.Equals(Request.Query["soloLectura"], "1", StringComparison.OrdinalIgnoreCase);

            return View(vm);
        }

        // cancelar
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancelar(int id, string? comentario)
        {
            var usuarioId = ObtenerUsuarioID();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var tx = await cn.BeginTransactionAsync();

            try
            {
                int? estatusActual = null;

                const string sqlGet = @"
SELECT EstatusID
FROM dbo.SolicitudesProduccion
WHERE SolicitudProduccionID = @SolicitudProduccionID
  AND Activo = 1;";

                await using (var cmd = new SqlCommand(sqlGet, cn, (SqlTransaction)tx))
                {
                    cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = id;
                    var result = await cmd.ExecuteScalarAsync();

                    if (result == null || result == DBNull.Value)
                    {
                        await tx.RollbackAsync();
                        return NotFound();
                    }

                    estatusActual = Convert.ToInt32(result);
                }

                if (estatusActual >= SolicitudProduccionEstatus.EnProduccion)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "No se puede cancelar una solicitud que ya está en producción o cerrada.";
                    return RedirectToAction(nameof(Detalle), new { id });
                }

                const string sqlUpdate = @"
UPDATE dbo.SolicitudesProduccion
SET
    EstatusID = @EstatusID,
    UsuarioModificacionID = @UsuarioModificacionID,
    FechaModificacion = GETDATE()
WHERE SolicitudProduccionID = @SolicitudProduccionID;";

                await using (var cmd = new SqlCommand(sqlUpdate, cn, (SqlTransaction)tx))
                {
                    cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value = SolicitudProduccionEstatus.Cancelada;
                    cmd.Parameters.Add("@UsuarioModificacionID", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = id;

                    await cmd.ExecuteNonQueryAsync();
                }

                await InsertarHistorialAsync(
                    id,
                    estatusActual,
                    SolicitudProduccionEstatus.Cancelada,
                    "Cancelación de solicitud",
                    comentario,
                    usuarioId,
                    cn,
                    (SqlTransaction)tx
                );

                await tx.CommitAsync();

                TempData["Success"] = "Solicitud cancelada correctamente.";
                return RedirectToAction(nameof(Detalle), new { id });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "Error al cancelar: " + ex.Message;
                return RedirectToAction(nameof(Detalle), new { id });
            }
        }

        // AJAX: obtener la información técnica de una parte
        [HttpGet]
        public async Task<IActionResult> ObtenerParteInfo(int parteId)
        {
            if (parteId <= 0)
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "No se recibió una parte válida."
                });
            }

            try
            {
                const string sql = @"
SELECT TOP (1)
    p.ParteID,
    p.ClienteID,
    p.NumeroParte,
    p.ReferenciaSAP,
    p.Descripcion,
    p.Designacion,

    t.Color,
    t.Cavidades,
    t.ObjetivoHora,
    t.PiezasPorCaja,
    t.MaquinaPrincipalID,
    t.MaquinaSustitutaID,
    t.MoldePrincipalID,

    m.CodigoMolde AS MoldeCodigo

FROM dbo.ERP_Partes p

LEFT JOIN dbo.ERP_ParteDatosTecnicos t
    ON t.ParteID = p.ParteID
   AND t.Activo = 1

LEFT JOIN dbo.ERP_Moldes m
    ON m.MoldeID = t.MoldePrincipalID
   AND m.Activo = 1

WHERE p.ParteID = @ParteID
  AND p.Activo = 1;";

                await using var cn = new SqlConnection(ConnectionString);
                await cn.OpenAsync();

                await using var cmd = new SqlCommand(sql, cn);
                cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId;

                await using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                {
                    return Json(new
                    {
                        ok = false,
                        mensaje = "No se encontró la parte seleccionada."
                    });
                }

                var numeroParte = rd["NumeroParte"] == DBNull.Value
                    ? string.Empty
                    : rd["NumeroParte"].ToString() ?? string.Empty;

                var referenciaSap = rd["ReferenciaSAP"] == DBNull.Value
                    ? null
                    : rd["ReferenciaSAP"].ToString();

                var descripcion = rd["Descripcion"] == DBNull.Value
                    ? string.Empty
                    : rd["Descripcion"].ToString() ?? string.Empty;

                var designacion = rd["Designacion"] == DBNull.Value
                    ? null
                    : rd["Designacion"].ToString();

                return Json(new
                {
                    ok = true,
                    parteID = Convert.ToInt32(rd["ParteID"]),
                    clienteID = rd["ClienteID"] == DBNull.Value
                        ? (int?)null
                        : Convert.ToInt32(rd["ClienteID"]),
                    numeroParte,
                    referenciaSAP = string.IsNullOrWhiteSpace(referenciaSap)
                        ? numeroParte
                        : referenciaSap,
                    designacionDescripcionSAP = !string.IsNullOrWhiteSpace(designacion)
                        ? designacion
                        : descripcion,
                    color = rd["Color"] == DBNull.Value
                        ? null
                        : rd["Color"].ToString(),
                    cavidades = rd["Cavidades"] == DBNull.Value
                        ? (int?)null
                        : Convert.ToInt32(rd["Cavidades"]),
                    objetivoHora = rd["ObjetivoHora"] == DBNull.Value
                        ? (int?)null
                        : Convert.ToInt32(rd["ObjetivoHora"]),
                    piezasPorCaja = rd["PiezasPorCaja"] == DBNull.Value
                        ? (int?)null
                        : Convert.ToInt32(rd["PiezasPorCaja"]),
                    moldeID = rd["MoldePrincipalID"] == DBNull.Value
                        ? (int?)null
                        : Convert.ToInt32(rd["MoldePrincipalID"]),
                    moldeCodigo = rd["MoldeCodigo"] == DBNull.Value
                        ? null
                        : rd["MoldeCodigo"].ToString(),
                    maquinaPrincipalID = rd["MaquinaPrincipalID"] == DBNull.Value
                        ? (int?)null
                        : Convert.ToInt32(rd["MaquinaPrincipalID"]),
                    maquinaSustitutaID = rd["MaquinaSustitutaID"] == DBNull.Value
                        ? (int?)null
                        : Convert.ToInt32(rd["MaquinaSustitutaID"])
                });
            }
            catch (Exception ex)
            {
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        ok = false,
                        mensaje = "No fue posible consultar la información de la parte: " + ex.Message
                    }
                );
            }
        }

        // helpers
        private async Task<int> InsertarReleaseAutomaticoAsync(
            SolicitudProduccionCrearVm vm,
            string? clienteNombre,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            var folioRelease = await GenerarFolioReleaseAutomaticoAsync(cn, tx);
            var nivelCriticidad = NormalizarCriticidad(vm.Prioridad);

            const string sql = @"
INSERT INTO dbo.Planeacion_Releases
(
    FolioRelease,
    FolioCliente,
    ClienteID,
    ClienteNombre,
    FechaRecepcion,
    VersionRelease,
    ArchivoOrigenNombre,
    PlantillaImportacion,
    ImportadoDesdeArchivo,
    NivelCriticidad,
    ComentarioCriticidad,
    Observaciones,
    EstatusID,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
OUTPUT INSERTED.ReleaseID
VALUES
(
    @FolioRelease,
    @FolioCliente,
    @ClienteID,
    @ClienteNombre,
    @FechaRecepcion,
    @VersionRelease,
    NULL,
    N'OF_MANUAL',
    0,
    @NivelCriticidad,
    @ComentarioCriticidad,
    @Observaciones,
    2,
    @UsuarioCreacionID,
    GETDATE(),
    1
);";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@FolioRelease", SqlDbType.NVarChar, 40).Value = folioRelease;
            cmd.Parameters.Add("@FolioCliente", SqlDbType.NVarChar, 100).Value =
                (object?)vm.NumeroOFRecibida ?? (object?)vm.FolioSolicitud ?? DBNull.Value;
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value =
                (object?)vm.ClienteID ?? DBNull.Value;
            cmd.Parameters.Add("@ClienteNombre", SqlDbType.NVarChar, 200).Value =
                (object?)clienteNombre ?? DBNull.Value;
            cmd.Parameters.Add("@FechaRecepcion", SqlDbType.Date).Value = vm.FechaSolicitud.Date;
            cmd.Parameters.Add("@VersionRelease", SqlDbType.NVarChar, 50).Value =
                (object?)vm.NumeroOFRecibida ?? DBNull.Value;
            cmd.Parameters.Add("@NivelCriticidad", SqlDbType.NVarChar, 20).Value = nivelCriticidad;
            cmd.Parameters.Add("@ComentarioCriticidad", SqlDbType.NVarChar, 300).Value =
                nivelCriticidad == "NORMAL" ? DBNull.Value : (object?)vm.NotasGenerales ?? DBNull.Value;
            cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value =
                $"Release interno generado automáticamente desde la OF manual {vm.FolioSolicitud}.";
            cmd.Parameters.Add("@UsuarioCreacionID", SqlDbType.Int).Value = usuarioId;

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        private async Task<int> InsertarReleaseRenglonAutomaticoAsync(
            int releaseId,
            int renglon,
            SolicitudProduccionDetalleCrearVm d,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
INSERT INTO dbo.Planeacion_ReleaseRenglones
(
    ReleaseID,
    Renglon,
    ParteID,
    NumeroParte,
    ReferenciaSAP,
    DesignacionDescripcionSAP,
    UnidadMedidaCliente,
    ContratoCliente,
    Observaciones,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
OUTPUT INSERTED.ReleaseRenglonID
SELECT
    @ReleaseID,
    @Renglon,
    @ParteID,
    COALESCE(NULLIF(p.NumeroParte, N''), NULLIF(@ReferenciaSAP, N'')),
    COALESCE(NULLIF(@ReferenciaSAP, N''), NULLIF(p.ReferenciaSAP, N''), NULLIF(p.NumeroParte, N'')),
    COALESCE(NULLIF(@Descripcion, N''), NULLIF(p.Designacion, N''), NULLIF(p.Descripcion, N'')),
    NULL,
    NULL,
    @Observaciones,
    @UsuarioCreacionID,
    GETDATE(),
    1
FROM (VALUES (1)) AS base(N)
LEFT JOIN dbo.ERP_Partes p
    ON p.ParteID = @ParteID;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = releaseId;
            cmd.Parameters.Add("@Renglon", SqlDbType.Int).Value = renglon;
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = (object?)d.ParteID ?? DBNull.Value;
            cmd.Parameters.Add("@ReferenciaSAP", SqlDbType.NVarChar, 150).Value =
                (object?)d.ReferenciaSAP ?? DBNull.Value;
            cmd.Parameters.Add("@Descripcion", SqlDbType.NVarChar, 300).Value =
                (object?)d.DesignacionDescripcionSAP ?? DBNull.Value;
            cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value =
                (object?)d.Notas ?? "Renglón originado desde una OF manual.";
            cmd.Parameters.Add("@UsuarioCreacionID", SqlDbType.Int).Value = usuarioId;

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        private async Task InsertarReleaseDetalleAutomaticoAsync(
            int releaseId,
            int releaseRenglonId,
            int solicitudProduccionId,
            int renglon,
            int secuenciaEntrega,
            SolicitudProduccionCrearVm vm,
            SolicitudProduccionDetalleCrearVm d,
            SolicitudProduccionAsignacionMaquinaCrearVm? asignacion,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            var cantidad = asignacion?.CantidadAsignada > 0
                ? asignacion.CantidadAsignada
                : d.CantidadPiezas;

            var fechaInicio = ConstruirFechaHora(
                asignacion?.FechaProgramadaTentativa,
                asignacion?.HoraInicioTentativa);

            var fechaFin = ConstruirFechaHora(
                asignacion?.FechaProgramadaTentativa,
                asignacion?.HoraFinTentativa);

            if (fechaInicio.HasValue && fechaFin.HasValue && fechaFin.Value <= fechaInicio.Value)
                fechaFin = fechaFin.Value.AddDays(1);

            var horas = asignacion?.HorasEstimadas ?? d.HorasPlaneadas;
            if ((!horas.HasValue || horas.Value <= 0) && d.ObjetivoHora.GetValueOrDefault() > 0)
                horas = Math.Round(cantidad / (decimal)d.ObjetivoHora!.Value, 2);

            if (!fechaFin.HasValue && fechaInicio.HasValue && horas.GetValueOrDefault() > 0)
                fechaFin = fechaInicio.Value.AddHours((double)horas!.Value);

            bool? daTiempo = null;
            if (vm.FechaRequerida.HasValue && fechaFin.HasValue)
                daTiempo = fechaFin.Value.Date <= vm.FechaRequerida.Value.Date;

            const string sql = @"
INSERT INTO dbo.Planeacion_ReleaseDetalle
(
    ReleaseID,
    ReleaseRenglonID,
    SecuenciaEntrega,
    Renglon,
    ParteID,
    NumeroParte,
    ReferenciaSAP,
    DesignacionDescripcionSAP,
    FechaCarga,
    FechaRequerida,
    CantidadRequerida,
    PTDisponibleAlCalcular,
    ProduccionProgramadaPendiente,
    PiezasDesdePT,
    PiezasAProducir,
    MaterialID,
    MaterialCodigo,
    MaterialDescripcion,
    PesoBrutoPieza,
    MPRequeridaKg,
    MPDisponibleKg,
    EmbalajeCodigo,
    EmbalajeDescripcion,
    PiezasPorEmbalaje,
    EmbalajeRequerido,
    EmbalajeDisponible,
    MoldeID,
    MoldeCodigo,
    MaquinaSugeridaID,
    MaquinaSugeridaCodigo,
    MaquinaSugeridaNombre,
    ObjetivoHora,
    HorasNecesarias,
    FechaInicioSugerida,
    FechaFinEstimada,
    DaTiempo,
    MensajeCapacidad,
    SolicitudProduccionID,
    EstatusID,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
SELECT
    @ReleaseID,
    @ReleaseRenglonID,
    @SecuenciaEntrega,
    @Renglon,
    @ParteID,
    COALESCE(NULLIF(p.NumeroParte, N''), NULLIF(@ReferenciaSAP, N'')),
    COALESCE(NULLIF(@ReferenciaSAP, N''), NULLIF(p.ReferenciaSAP, N''), NULLIF(p.NumeroParte, N'')),
    COALESCE(NULLIF(@Descripcion, N''), NULLIF(p.Designacion, N''), NULLIF(p.Descripcion, N'')),
    @FechaCarga,
    @FechaRequerida,
    @CantidadRequerida,
    0,
    0,
    0,
    @CantidadRequerida,
    t.MaterialID,
    t.MaterialCodigo,
    t.MaterialDescripcion,
    t.PesoBrutoPieza,
    CASE
        WHEN t.PesoBrutoPieza IS NULL OR t.PesoBrutoPieza <= 0 THEN NULL
        ELSE ROUND((@CantidadRequerida * t.PesoBrutoPieza) / 1000.0, 4)
    END,
    NULL,
    t.EmbalajeCodigo,
    t.EmbalajeDescripcion,
    t.PiezasPorEmbalaje,
    CASE
        WHEN t.PiezasPorEmbalaje IS NULL OR t.PiezasPorEmbalaje <= 0 THEN NULL
        ELSE CEILING(@CantidadRequerida / t.PiezasPorEmbalaje)
    END,
    NULL,
    COALESCE(@MoldeID, t.MoldePrincipalID),
    mol.CodigoMolde,
    COALESCE(@MaquinaID, t.MaquinaPrincipalID),
    maq.Codigo,
    maq.Nombre,
    COALESCE(@ObjetivoHora, t.ObjetivoHora),
    @HorasNecesarias,
    @FechaInicio,
    @FechaFin,
    @DaTiempo,
    @MensajeCapacidad,
    @SolicitudProduccionID,
    2,
    @UsuarioCreacionID,
    GETDATE(),
    1
FROM (VALUES (1)) AS base(N)
LEFT JOIN dbo.ERP_Partes p
    ON p.ParteID = @ParteID
LEFT JOIN dbo.ERP_ParteDatosTecnicos t
    ON t.ParteID = @ParteID
   AND t.Activo = 1
LEFT JOIN dbo.ERP_Moldes mol
    ON mol.MoldeID = COALESCE(@MoldeID, t.MoldePrincipalID)
LEFT JOIN dbo.ERP_Maquinas maq
    ON maq.MaquinaID = COALESCE(@MaquinaID, t.MaquinaPrincipalID);";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = releaseId;
            cmd.Parameters.Add("@ReleaseRenglonID", SqlDbType.Int).Value = releaseRenglonId;
            cmd.Parameters.Add("@SecuenciaEntrega", SqlDbType.Int).Value = secuenciaEntrega;
            cmd.Parameters.Add("@Renglon", SqlDbType.Int).Value = renglon;
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = (object?)d.ParteID ?? DBNull.Value;
            cmd.Parameters.Add("@ReferenciaSAP", SqlDbType.NVarChar, 150).Value =
                (object?)d.ReferenciaSAP ?? DBNull.Value;
            cmd.Parameters.Add("@Descripcion", SqlDbType.NVarChar, 300).Value =
                (object?)d.DesignacionDescripcionSAP ?? DBNull.Value;
            cmd.Parameters.Add("@FechaCarga", SqlDbType.Date).Value =
                (object?)fechaInicio?.Date ?? vm.FechaSolicitud.Date;
            cmd.Parameters.Add("@FechaRequerida", SqlDbType.Date).Value =
                (object?)vm.FechaRequerida?.Date ?? (object?)fechaInicio?.Date ?? vm.FechaSolicitud.Date;
            cmd.Parameters.Add("@CantidadRequerida", SqlDbType.Int).Value = cantidad;
            cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value =
                (object?)asignacion?.MoldeID ?? (object?)d.MoldeID ?? DBNull.Value;
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value =
                (object?)asignacion?.MaquinaID ?? DBNull.Value;
            cmd.Parameters.Add("@ObjetivoHora", SqlDbType.Int).Value =
                (object?)d.ObjetivoHora ?? DBNull.Value;

            var horasParam = cmd.Parameters.Add("@HorasNecesarias", SqlDbType.Decimal);
            horasParam.Precision = 18;
            horasParam.Scale = 2;
            horasParam.Value = (object?)horas ?? DBNull.Value;

            cmd.Parameters.Add("@FechaInicio", SqlDbType.DateTime).Value =
                (object?)fechaInicio ?? DBNull.Value;
            cmd.Parameters.Add("@FechaFin", SqlDbType.DateTime).Value =
                (object?)fechaFin ?? DBNull.Value;
            cmd.Parameters.Add("@DaTiempo", SqlDbType.Bit).Value =
                (object?)daTiempo ?? DBNull.Value;
            cmd.Parameters.Add("@MensajeCapacidad", SqlDbType.NVarChar, 500).Value =
                asignacion == null
                    ? "OF manual incorporada a Planeación. Pendiente de confirmar máquina y horario."
                    : "OF manual incorporada a Planeación con la máquina y horario capturados.";
            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudProduccionId;
            cmd.Parameters.Add("@UsuarioCreacionID", SqlDbType.Int).Value = usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task VincularSolicitudConReleaseAsync(
            int solicitudProduccionId,
            int releaseId,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.SolicitudesProduccion
SET
    ReleaseID = @ReleaseID,
    ReleaseDetalleID = COALESCE
    (
        ReleaseDetalleID,
        (
            SELECT TOP (1) d.ReleaseDetalleID
            FROM dbo.Planeacion_ReleaseDetalle d
            WHERE d.ReleaseID = @ReleaseID
              AND d.Activo = 1
            ORDER BY d.Renglon, d.SecuenciaEntrega, d.ReleaseDetalleID
        )
    ),
    TipoOF = COALESCE(NULLIF(TipoOF, N''), N'RELEASE'),
    OrigenOF = N'MANUAL',
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE SolicitudProduccionID = @SolicitudProduccionID
  AND Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = releaseId;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudProduccionId;
            await cmd.ExecuteNonQueryAsync();
        }

        private async Task<string> GenerarFolioReleaseAutomaticoAsync(
            SqlConnection cn,
            SqlTransaction tx)
        {
            var anio = DateTime.Today.Year;

            const string sql = @"
SELECT ISNULL(MAX(ReleaseID), 0) + 1
FROM dbo.Planeacion_Releases WITH (UPDLOCK, HOLDLOCK);";

            await using var cmd = new SqlCommand(sql, cn, tx);
            var consecutivo = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            return $"REL-{consecutivo:000000}/{anio}";
        }

        private static DateTime? ConstruirFechaHora(DateTime? fecha, TimeSpan? hora)
        {
            if (!fecha.HasValue)
                return null;

            return fecha.Value.Date.Add(hora ?? TimeSpan.Zero);
        }

        private static string NormalizarCriticidad(string? prioridad)
        {
            var value = (prioridad ?? string.Empty).Trim().ToUpperInvariant();

            if (value.Contains("URG"))
                return "URGENTE";

            if (value.Contains("ALTA") || value.Contains("CRIT"))
                return "CRITICO";

            return "NORMAL";
        }

        private async Task<int> InsertarSolicitudAsync(
            SolicitudProduccionCrearVm vm,
            string? clienteNombre,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
INSERT INTO dbo.SolicitudesProduccion
(
    FolioSolicitud,
    NumeroOFRecibida,
    FechaSolicitud,
    FechaRequerida,
    ClienteID,
    ClienteNombre,
    OrigenSolicitud,
    Prioridad,
    EstatusID,
    NotasGenerales,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
OUTPUT INSERTED.SolicitudProduccionID
VALUES
(
    @FolioSolicitud,
    @NumeroOFRecibida,
    @FechaSolicitud,
    @FechaRequerida,
    @ClienteID,
    @ClienteNombre,
    @OrigenSolicitud,
    @Prioridad,
    @EstatusID,
    @NotasGenerales,
    @UsuarioCreacionID,
    GETDATE(),
    1
);";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@FolioSolicitud", SqlDbType.NVarChar, 30).Value = (object?)vm.FolioSolicitud ?? DBNull.Value;
            cmd.Parameters.Add("@NumeroOFRecibida", SqlDbType.NVarChar, 80).Value = (object?)vm.NumeroOFRecibida ?? DBNull.Value;
            cmd.Parameters.Add("@FechaSolicitud", SqlDbType.Date).Value = vm.FechaSolicitud.Date;
            cmd.Parameters.Add("@FechaRequerida", SqlDbType.Date).Value = (object?)vm.FechaRequerida?.Date ?? DBNull.Value;
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = (object?)vm.ClienteID ?? DBNull.Value;
            cmd.Parameters.Add("@ClienteNombre", SqlDbType.NVarChar, 200).Value = (object?)clienteNombre ?? DBNull.Value;
            cmd.Parameters.Add("@OrigenSolicitud", SqlDbType.NVarChar, 50).Value = (object?)vm.OrigenSolicitud ?? DBNull.Value;
            cmd.Parameters.Add("@Prioridad", SqlDbType.NVarChar, 30).Value = string.IsNullOrWhiteSpace(vm.Prioridad) ? "Normal" : vm.Prioridad;
            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value = SolicitudProduccionEstatus.Capturada;
            cmd.Parameters.Add("@NotasGenerales", SqlDbType.NVarChar).Value = (object?)vm.NotasGenerales ?? DBNull.Value;
            cmd.Parameters.Add("@UsuarioCreacionID", SqlDbType.Int).Value = usuarioId;

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        private async Task<int> InsertarDetalleAsync(
            int solicitudId,
            int renglon,
            SolicitudProduccionDetalleCrearVm d,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
INSERT INTO dbo.SolicitudesProduccionDetalle
(
    SolicitudProduccionID,
    Renglon,
    ParteID,
    MoldeID,
    DesignacionDescripcionSAP,
    ReferenciaSAP,
    CantidadPiezas,
    HorasPlaneadas,
    NumeroMoldeTexto,
    Color,
    Cavidades,
    ObjetivoHora,
    PiezasPorCaja,
    Notas,
    EstatusID,
    Activo,
    FechaCreacion
)
OUTPUT INSERTED.SolicitudProduccionDetalleID
VALUES
(
    @SolicitudProduccionID,
    @Renglon,
    @ParteID,
    @MoldeID,
    @DesignacionDescripcionSAP,
    @ReferenciaSAP,
    @CantidadPiezas,
    @HorasPlaneadas,
    @NumeroMoldeTexto,
    @Color,
    @Cavidades,
    @ObjetivoHora,
    @PiezasPorCaja,
    @Notas,
    1,
    1,
    GETDATE()
);";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudId;
            cmd.Parameters.Add("@Renglon", SqlDbType.Int).Value = renglon;
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = (object?)d.ParteID ?? DBNull.Value;
            cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value = (object?)d.MoldeID ?? DBNull.Value;
            cmd.Parameters.Add("@DesignacionDescripcionSAP", SqlDbType.NVarChar, 300).Value = d.DesignacionDescripcionSAP;
            cmd.Parameters.Add("@ReferenciaSAP", SqlDbType.NVarChar, 150).Value = d.ReferenciaSAP;
            cmd.Parameters.Add("@CantidadPiezas", SqlDbType.Int).Value = d.CantidadPiezas;
            cmd.Parameters.Add("@HorasPlaneadas", SqlDbType.Decimal).Value = (object?)d.HorasPlaneadas ?? DBNull.Value;
            cmd.Parameters["@HorasPlaneadas"].Precision = 10;
            cmd.Parameters["@HorasPlaneadas"].Scale = 2;
            cmd.Parameters.Add("@NumeroMoldeTexto", SqlDbType.NVarChar, 100).Value = (object?)d.NumeroMoldeTexto ?? DBNull.Value;
            cmd.Parameters.Add("@Color", SqlDbType.NVarChar, 80).Value = (object?)d.Color ?? DBNull.Value;
            cmd.Parameters.Add("@Cavidades", SqlDbType.Int).Value = (object?)d.Cavidades ?? DBNull.Value;
            cmd.Parameters.Add("@ObjetivoHora", SqlDbType.Int).Value = (object?)d.ObjetivoHora ?? DBNull.Value;
            cmd.Parameters.Add("@PiezasPorCaja", SqlDbType.Int).Value = (object?)d.PiezasPorCaja ?? DBNull.Value;
            cmd.Parameters.Add("@Notas", SqlDbType.NVarChar, 500).Value = (object?)d.Notas ?? DBNull.Value;

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        private async Task InsertarAsignacionMaquinaAsync(
            int detalleId,
            SolicitudProduccionAsignacionMaquinaCrearVm a,
            int? moldeDetalleId,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
INSERT INTO dbo.SolicitudesProduccionAsignacionMaquina
(
    SolicitudProduccionDetalleID,
    MaquinaID,
    MoldeID,
    CantidadAsignada,
    HorasEstimadas,
    Secuencia,
    CondicionProduccion,
    FechaProgramadaTentativa,
    HoraInicioTentativa,
    HoraFinTentativa,
    EstatusID,
    Observaciones,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
VALUES
(
    @SolicitudProduccionDetalleID,
    @MaquinaID,
    @MoldeID,
    @CantidadAsignada,
    @HorasEstimadas,
    @Secuencia,
    @CondicionProduccion,
    @FechaProgramadaTentativa,
    @HoraInicioTentativa,
    @HoraFinTentativa,
    1,
    @Observaciones,
    @UsuarioCreacionID,
    GETDATE(),
    1
);";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = detalleId;
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = a.MaquinaID;
            cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value = (object?)a.MoldeID ?? (object?)moldeDetalleId ?? DBNull.Value;
            cmd.Parameters.Add("@CantidadAsignada", SqlDbType.Int).Value = a.CantidadAsignada;
            cmd.Parameters.Add("@HorasEstimadas", SqlDbType.Decimal).Value = (object?)a.HorasEstimadas ?? DBNull.Value;
            cmd.Parameters["@HorasEstimadas"].Precision = 10;
            cmd.Parameters["@HorasEstimadas"].Scale = 2;
            cmd.Parameters.Add("@Secuencia", SqlDbType.Int).Value = a.Secuencia <= 0 ? 1 : a.Secuencia;
            cmd.Parameters.Add("@CondicionProduccion", SqlDbType.NVarChar, 10).Value = (object?)a.CondicionProduccion ?? DBNull.Value;
            cmd.Parameters.Add("@FechaProgramadaTentativa", SqlDbType.Date).Value = (object?)a.FechaProgramadaTentativa?.Date ?? DBNull.Value;
            cmd.Parameters.Add("@HoraInicioTentativa", SqlDbType.Time).Value = (object?)a.HoraInicioTentativa ?? DBNull.Value;
            cmd.Parameters.Add("@HoraFinTentativa", SqlDbType.Time).Value = (object?)a.HoraFinTentativa ?? DBNull.Value;
            cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value = (object?)a.Observaciones ?? DBNull.Value;
            cmd.Parameters.Add("@UsuarioCreacionID", SqlDbType.Int).Value = usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task InsertarHistorialAsync(
            int solicitudId,
            int? estatusAnterior,
            int estatusNuevo,
            string movimiento,
            string? comentario,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
INSERT INTO dbo.SolicitudProduccionHistorial
(
    SolicitudProduccionID,
    EstatusAnteriorID,
    EstatusNuevoID,
    Movimiento,
    Comentario,
    UsuarioID,
    FechaMovimiento
)
VALUES
(
    @SolicitudProduccionID,
    @EstatusAnteriorID,
    @EstatusNuevoID,
    @Movimiento,
    @Comentario,
    @UsuarioID,
    GETDATE()
);";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudId;
            cmd.Parameters.Add("@EstatusAnteriorID", SqlDbType.Int).Value = (object?)estatusAnterior ?? DBNull.Value;
            cmd.Parameters.Add("@EstatusNuevoID", SqlDbType.Int).Value = estatusNuevo;
            cmd.Parameters.Add("@Movimiento", SqlDbType.NVarChar, 100).Value = movimiento;
            cmd.Parameters.Add("@Comentario", SqlDbType.NVarChar, 500).Value = (object?)comentario ?? DBNull.Value;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task CargarCatalogosAsync(SolicitudProduccionCrearVm vm)
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            vm.Clientes = await CargarSelectAsync(
                cn,
                "SELECT ClienteID AS Id, Nombre AS Texto FROM dbo.ERP_Clientes WHERE Activo = 1 ORDER BY Nombre;"
            );

            vm.Partes = await CargarSelectAsync(
                cn,
                "SELECT ParteID AS Id, NumeroParte + ' - ' + Descripcion AS Texto FROM dbo.ERP_Partes WHERE Activo = 1 ORDER BY NumeroParte;"
            );

            vm.Moldes = await CargarSelectAsync(
                cn,
                "SELECT MoldeID AS Id, CodigoMolde + ' - ' + ISNULL(NombreMolde, '') AS Texto FROM dbo.ERP_Moldes WHERE Activo = 1 ORDER BY CodigoMolde;"
            );

            vm.Maquinas = await CargarSelectAsync(
                cn,
                "SELECT MaquinaID AS Id, Codigo + ' - ' + Nombre AS Texto FROM dbo.ERP_Maquinas WHERE Activo = 1 ORDER BY Codigo;"
            );
        }

        private static async Task<List<SelectListItem>> CargarSelectAsync(SqlConnection cn, string sql)
        {
            var lista = new List<SelectListItem>();

            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new SelectListItem
                {
                    Value = rd["Id"].ToString(),
                    Text = rd["Texto"].ToString()
                });
            }

            return lista;
        }

        private async Task<string?> ObtenerClienteNombreAsync(int clienteId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
SELECT Nombre
FROM dbo.ERP_Clientes
WHERE ClienteID = @ClienteID;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = clienteId;

            return await cmd.ExecuteScalarAsync() as string;
        }

        private async Task CompletarDetalleDesdeParteAsync(
            SolicitudProduccionDetalleCrearVm d,
            SqlConnection cn,
            SqlTransaction tx)
        {
            if (!d.ParteID.HasValue || d.ParteID.Value <= 0)
            {
                return;
            }

            const string sql = @"
SELECT TOP (1)
    p.NumeroParte,
    p.ReferenciaSAP,
    p.Descripcion,
    p.Designacion,

    t.Color,
    t.Cavidades,
    t.ObjetivoHora,
    t.PiezasPorCaja,
    t.MoldePrincipalID

FROM dbo.ERP_Partes p

LEFT JOIN dbo.ERP_ParteDatosTecnicos t
    ON t.ParteID = p.ParteID
   AND t.Activo = 1

WHERE p.ParteID = @ParteID
  AND p.Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = d.ParteID.Value;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
            {
                return;
            }

            var numeroParte = rd["NumeroParte"] == DBNull.Value
                ? string.Empty
                : rd["NumeroParte"].ToString() ?? string.Empty;

            var referencia = rd["ReferenciaSAP"] == DBNull.Value
                ? null
                : rd["ReferenciaSAP"].ToString();

            var descripcion = rd["Descripcion"] == DBNull.Value
                ? string.Empty
                : rd["Descripcion"].ToString() ?? string.Empty;

            var designacion = rd["Designacion"] == DBNull.Value
                ? null
                : rd["Designacion"].ToString();

            if (string.IsNullOrWhiteSpace(d.ReferenciaSAP))
            {
                d.ReferenciaSAP = string.IsNullOrWhiteSpace(referencia)
                    ? numeroParte
                    : referencia;
            }

            if (string.IsNullOrWhiteSpace(d.DesignacionDescripcionSAP))
            {
                d.DesignacionDescripcionSAP = !string.IsNullOrWhiteSpace(designacion)
                    ? designacion
                    : descripcion;
            }

            if (string.IsNullOrWhiteSpace(d.Color) && rd["Color"] != DBNull.Value)
            {
                d.Color = rd["Color"].ToString();
            }

            if (!d.Cavidades.HasValue && rd["Cavidades"] != DBNull.Value)
            {
                d.Cavidades = Convert.ToInt32(rd["Cavidades"]);
            }

            if (!d.ObjetivoHora.HasValue && rd["ObjetivoHora"] != DBNull.Value)
            {
                d.ObjetivoHora = Convert.ToInt32(rd["ObjetivoHora"]);
            }

            if (!d.PiezasPorCaja.HasValue && rd["PiezasPorCaja"] != DBNull.Value)
            {
                d.PiezasPorCaja = Convert.ToInt32(rd["PiezasPorCaja"]);
            }

            if (!d.MoldeID.HasValue && rd["MoldePrincipalID"] != DBNull.Value)
            {
                d.MoldeID = Convert.ToInt32(rd["MoldePrincipalID"]);
            }
        }

        private async Task<List<SolicitudProduccionDetalleVistaRenglonVm>> ObtenerDetallesAsync(int solicitudId, SqlConnection cn)
        {
            var lista = new List<SolicitudProduccionDetalleVistaRenglonVm>();

            const string sql = @"
SELECT
    d.SolicitudProduccionDetalleID,
    d.Renglon,
    d.ReferenciaSAP,
    d.DesignacionDescripcionSAP,
    d.CantidadPiezas,
    ISNULL(m.CodigoMolde, d.NumeroMoldeTexto) AS Molde,
    d.Color,
    d.Cavidades,
    d.ObjetivoHora,
    d.PiezasPorCaja,
    d.Notas
FROM dbo.SolicitudesProduccionDetalle d
LEFT JOIN dbo.ERP_Moldes m
    ON m.MoldeID = d.MoldeID
WHERE d.SolicitudProduccionID = @SolicitudProduccionID
  AND d.Activo = 1
ORDER BY d.Renglon;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudId;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new SolicitudProduccionDetalleVistaRenglonVm
                {
                    SolicitudProduccionDetalleID = Convert.ToInt32(rd["SolicitudProduccionDetalleID"]),
                    Renglon = Convert.ToInt32(rd["Renglon"]),
                    ReferenciaSAP = rd["ReferenciaSAP"] as string ?? "",
                    DesignacionDescripcionSAP = rd["DesignacionDescripcionSAP"] as string ?? "",
                    CantidadPiezas = Convert.ToInt32(rd["CantidadPiezas"]),
                    Molde = rd["Molde"] as string,
                    Color = rd["Color"] as string,
                    Cavidades = rd["Cavidades"] == DBNull.Value ? null : Convert.ToInt32(rd["Cavidades"]),
                    ObjetivoHora = rd["ObjetivoHora"] == DBNull.Value ? null : Convert.ToInt32(rd["ObjetivoHora"]),
                    PiezasPorCaja = rd["PiezasPorCaja"] == DBNull.Value ? null : Convert.ToInt32(rd["PiezasPorCaja"]),
                    Notas = rd["Notas"] as string
                });
            }

            return lista;
        }

        private async Task<List<SolicitudProduccionAsignacionMaquinaVistaVm>> ObtenerAsignacionesAsync(int detalleId, SqlConnection cn)
        {
            var lista = new List<SolicitudProduccionAsignacionMaquinaVistaVm>();

            const string sql = @"
SELECT
    a.AsignacionMaquinaID,
    maq.Codigo + ' - ' + maq.Nombre AS Maquina,
    mol.CodigoMolde AS Molde,
    a.CantidadAsignada,
    a.HorasEstimadas,
    a.Secuencia,
    a.CondicionProduccion,
    a.FechaProgramadaTentativa,
    a.HoraInicioTentativa,
    a.HoraFinTentativa,
    a.Observaciones
FROM dbo.SolicitudesProduccionAsignacionMaquina a
INNER JOIN dbo.ERP_Maquinas maq
    ON maq.MaquinaID = a.MaquinaID
LEFT JOIN dbo.ERP_Moldes mol
    ON mol.MoldeID = a.MoldeID
WHERE a.SolicitudProduccionDetalleID = @SolicitudProduccionDetalleID
  AND a.Activo = 1
ORDER BY a.Secuencia;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = detalleId;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new SolicitudProduccionAsignacionMaquinaVistaVm
                {
                    AsignacionMaquinaID = Convert.ToInt32(rd["AsignacionMaquinaID"]),
                    Maquina = rd["Maquina"] as string ?? "",
                    Molde = rd["Molde"] as string,
                    CantidadAsignada = Convert.ToInt32(rd["CantidadAsignada"]),
                    HorasEstimadas = rd["HorasEstimadas"] == DBNull.Value ? null : Convert.ToDecimal(rd["HorasEstimadas"]),
                    Secuencia = Convert.ToInt32(rd["Secuencia"]),
                    CondicionProduccion = rd["CondicionProduccion"] as string,
                    FechaProgramadaTentativa = rd["FechaProgramadaTentativa"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaProgramadaTentativa"]),
                    HoraInicioTentativa = rd["HoraInicioTentativa"] == DBNull.Value ? null : (TimeSpan)rd["HoraInicioTentativa"],
                    HoraFinTentativa = rd["HoraFinTentativa"] == DBNull.Value ? null : (TimeSpan)rd["HoraFinTentativa"],
                    Observaciones = rd["Observaciones"] as string
                });
            }

            return lista;
        }

        private async Task<List<SolicitudProduccionHistorialVistaVm>> ObtenerHistorialAsync(int solicitudId, SqlConnection cn)
        {
            var lista = new List<SolicitudProduccionHistorialVistaVm>();

            const string sql = @"
SELECT
    h.FechaMovimiento,
    h.Movimiento,
    h.Comentario,
    h.EstatusAnteriorID,
    h.EstatusNuevoID,
    CAST(h.UsuarioID AS NVARCHAR(50)) AS Usuario
FROM dbo.SolicitudProduccionHistorial h
WHERE h.SolicitudProduccionID = @SolicitudProduccionID
ORDER BY h.FechaMovimiento DESC;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudId;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new SolicitudProduccionHistorialVistaVm
                {
                    FechaMovimiento = Convert.ToDateTime(rd["FechaMovimiento"]),
                    Movimiento = rd["Movimiento"] as string ?? "",
                    Comentario = rd["Comentario"] as string,
                    EstatusAnteriorID = rd["EstatusAnteriorID"] == DBNull.Value ? null : Convert.ToInt32(rd["EstatusAnteriorID"]),
                    EstatusNuevoID = Convert.ToInt32(rd["EstatusNuevoID"]),
                    Usuario = rd["Usuario"] as string ?? ""
                });
            }

            return lista;
        }

        // NSQ_OF_TRAZABILIDAD_V3E
        private async Task<List<SolicitudProduccionTrazabilidadItemVm>> ObtenerTrazabilidadCompletaAsync(int solicitudId, SqlConnection cn)
        {
            var lista = new List<SolicitudProduccionTrazabilidadItemVm>();
            const string sql = @"
IF OBJECT_ID(N'dbo.vw_OF_Trazabilidad',N'V') IS NULL RETURN;
SELECT SolicitudProduccionID,FechaEvento,OrdenEtapa,Etapa,Evento,EstadoAnterior,EstadoNuevo,
       Descripcion,Usuario,TipoOrigen,OrigenID,EsAlerta,Severidad,EvidenciaUrl
FROM dbo.vw_OF_Trazabilidad
WHERE SolicitudProduccionID=@SolicitudProduccionID
ORDER BY FechaEvento DESC,OrdenEtapa DESC,OrigenID DESC;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudId;
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                lista.Add(new SolicitudProduccionTrazabilidadItemVm
                {
                    SolicitudProduccionID = Convert.ToInt32(rd["SolicitudProduccionID"]),
                    FechaEvento = Convert.ToDateTime(rd["FechaEvento"]),
                    OrdenEtapa = Convert.ToInt32(rd["OrdenEtapa"]),
                    Etapa = rd["Etapa"]?.ToString() ?? string.Empty,
                    Evento = rd["Evento"]?.ToString() ?? string.Empty,
                    EstadoAnterior = rd["EstadoAnterior"] == DBNull.Value ? null : rd["EstadoAnterior"].ToString(),
                    EstadoNuevo = rd["EstadoNuevo"] == DBNull.Value ? null : rd["EstadoNuevo"].ToString(),
                    Descripcion = rd["Descripcion"] == DBNull.Value ? null : rd["Descripcion"].ToString(),
                    Usuario = rd["Usuario"]?.ToString() ?? "Sistema",
                    TipoOrigen = rd["TipoOrigen"]?.ToString() ?? string.Empty,
                    OrigenID = Convert.ToInt64(rd["OrigenID"]),
                    EsAlerta = Convert.ToBoolean(rd["EsAlerta"]),
                    Severidad = rd["Severidad"] == DBNull.Value ? null : rd["Severidad"].ToString(),
                    EvidenciaUrl = rd["EvidenciaUrl"] == DBNull.Value ? null : rd["EvidenciaUrl"].ToString()
                });
            }
            return lista;
        }

        private async Task<List<SolicitudProduccionAlertaVm>> ObtenerAlertasActivasAsync(int solicitudId, SqlConnection cn)
        {
            var lista = new List<SolicitudProduccionAlertaVm>();
            const string sql = @"
IF OBJECT_ID(N'dbo.vw_OF_AlertasActivas',N'V') IS NULL RETURN;
SELECT SolicitudProduccionID,FechaAlerta,Departamento,TipoAlerta,Severidad,Mensaje,OrigenTabla,OrigenID,EvidenciaUrl
FROM dbo.vw_OF_AlertasActivas
WHERE SolicitudProduccionID=@SolicitudProduccionID
ORDER BY CASE Severidad WHEN N'Crítica' THEN 0 WHEN N'Alta' THEN 1 WHEN N'Media' THEN 2 ELSE 3 END,
         FechaAlerta DESC,OrigenID DESC;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudId;
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                lista.Add(new SolicitudProduccionAlertaVm
                {
                    SolicitudProduccionID = Convert.ToInt32(rd["SolicitudProduccionID"]),
                    FechaAlerta = Convert.ToDateTime(rd["FechaAlerta"]),
                    Departamento = rd["Departamento"]?.ToString() ?? string.Empty,
                    TipoAlerta = rd["TipoAlerta"]?.ToString() ?? string.Empty,
                    Severidad = rd["Severidad"]?.ToString() ?? "Media",
                    Mensaje = rd["Mensaje"]?.ToString() ?? string.Empty,
                    OrigenTabla = rd["OrigenTabla"]?.ToString() ?? string.Empty,
                    OrigenID = Convert.ToInt64(rd["OrigenID"]),
                    EvidenciaUrl = rd["EvidenciaUrl"] == DBNull.Value ? null : rd["EvidenciaUrl"].ToString()
                });
            }
            return lista;
        }

        private async Task<SolicitudProduccionEstadoActualVm> ObtenerEstadoActualAsync(int solicitudId, SqlConnection cn)
        {
            const string sql = @"
IF OBJECT_ID(N'dbo.vw_OF_EstadoActual',N'V') IS NULL
BEGIN
    SELECT @SolicitudProduccionID AS SolicitudProduccionID,1 AS EtapaActualOrden,N'OF creada' AS EtapaActual,
           N'La vista V3E de estado actual aun no esta instalada.' AS ResumenActual,CAST(NULL AS datetime2) AS FechaUltimoAvance;
    RETURN;
END;
SELECT SolicitudProduccionID,EtapaActualOrden,EtapaActual,ResumenActual,FechaUltimoAvance
FROM dbo.vw_OF_EstadoActual
WHERE SolicitudProduccionID=@SolicitudProduccionID;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudId;
            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync())
                return new SolicitudProduccionEstadoActualVm { SolicitudProduccionID = solicitudId, EtapaActualOrden = 1, EtapaActual = "OF creada" };

            return new SolicitudProduccionEstadoActualVm
            {
                SolicitudProduccionID = Convert.ToInt32(rd["SolicitudProduccionID"]),
                EtapaActualOrden = Convert.ToInt32(rd["EtapaActualOrden"]),
                EtapaActual = rd["EtapaActual"]?.ToString() ?? "OF creada",
                ResumenActual = rd["ResumenActual"]?.ToString() ?? string.Empty,
                FechaUltimoAvance = rd["FechaUltimoAvance"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaUltimoAvance"])
            };
        }

        private async Task<string> GenerarFolioAsync()
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            return await GenerarFolioAsync(cn, null);
        }

        private async Task<string> GenerarFolioAsync(SqlConnection cn, SqlTransaction? tx)
        {
            const string sql = "SELECT NEXT VALUE FOR dbo.SEQ_SolicitudesProduccion;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            var consecutivo = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            return $"SP-{consecutivo:000000}";
        }

        private int ObtenerUsuarioID()
        {
            return HttpContext.Session.GetInt32("UsuarioID") ?? 0;
        }
    }
}
