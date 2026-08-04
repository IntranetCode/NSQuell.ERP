using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers
{
    public sealed class ProduccionOperadorController : Controller
    {
        private readonly IConfiguration _configuration;

        public ProduccionOperadorController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private string ConnectionString =>
            _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("No se encontró la cadena de conexión DefaultConnection.");



[HttpGet]
        public async Task<IActionResult> Index()
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var usuarioId = ObtenerUsuarioID();

            var esOperador = await UsuarioEsOperadorAsync(usuarioId, cn);

            if (!esOperador)
                return AccesoDenegadoOperador();

            var programas = await ObtenerProgramasEnProduccionAsync(cn);

            ViewBag.AlertasProximosProgramas =
                await ObtenerAlertasProximosProgramasAsync(cn, 15);

            return View(programas);
        }

        [HttpGet]
        public async Task<IActionResult> Captura(int id)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var usuarioId = ObtenerUsuarioID();

            var esOperador = await UsuarioEsOperadorAsync(usuarioId, cn);

            if (!esOperador)
                return AccesoDenegadoOperador();

            var vm = await ObtenerTabletVmAsync(id, cn);

            if (vm == null)
                return NotFound();

            vm.MotivosParo = await CargarMotivosParoAsync(cn);

            return View(vm);
        }

        // ============================================================
        // GUARDAR PRODUCCION POR HORA
        // ============================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarHora(ProduccionRegistroHoraPostVm vm)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var usuarioId = ObtenerUsuarioID();

            var esOperador = await UsuarioEsOperadorAsync(usuarioId, cn);

            if (!esOperador)
                return AccesoDenegadoOperador();

            if (vm.EjecucionProduccionID <= 0)
            {
                TempData["Error"] = "No se recibió la ejecución de producción.";
                return RedirectToAction(nameof(Index));
            }

            if (!TimeSpan.TryParse(vm.HoraInicio, out var horaInicio) ||
                !TimeSpan.TryParse(vm.HoraFin, out var horaFin))
            {
                TempData["Error"] = "El rango de hora no es válido.";
                return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
            }

            if (horaFin <= horaInicio)
            {
                TempData["Error"] = "La hora fin debe ser mayor que la hora inicio.";
                return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
            }

            if (vm.CantidadOK < 0 ||
                vm.CantidadSospechosa < 0 ||
                vm.CantidadScrap < 0)
            {
                TempData["Error"] = "Las cantidades no pueden ser negativas.";
                return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
            }

            if (vm.CantidadOK == 0 &&
                vm.CantidadSospechosa == 0 &&
                vm.CantidadScrap == 0 &&
                string.IsNullOrWhiteSpace(vm.Observaciones))
            {
                TempData["Error"] = "Captura al menos una cantidad u observación.";
                return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
            }

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

            try
            {
                var ejecucion = await ObtenerEjecucionOperadorAsync(
                    vm.EjecucionProduccionID,
                    cn,
                    tx);

                if (ejecucion == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                if (ejecucion.EstatusID != ProduccionEstatus.EnProduccion)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "Solo puedes capturar piezas cuando la producción está en serie.";

                    return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
                }

                var tieneParoAbierto = await TieneParoAbiertoAsync(
                    vm.EjecucionProduccionID,
                    cn,
                    tx);

                if (tieneParoAbierto)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "No puedes capturar piezas mientras exista un paro abierto.";

                    return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
                }

                var personaOperador = await ObtenerPersonaOperadorAsync(usuarioId, cn, tx);

                await InsertarRegistroHoraAsync(
                    ejecucion,
                    vm,
                    horaInicio,
                    horaFin,
                    personaOperador.PersonaID,
                    usuarioId,
                    cn,
                    tx);

                await RecalcularTotalesEjecucionAsync(
                    vm.EjecucionProduccionID,
                    usuarioId,
                    cn,
                    tx);

                await tx.CommitAsync();

                TempData["Success"] = "Producción guardada correctamente.";

                return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "No fue posible guardar la producción: " + ex.Message;

                return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
            }
        }

        // ============================================================
        // INICIAR PARO DESDE TABLET
        // ============================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> IniciarParo(ProduccionParoPostVm vm)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var usuarioId = ObtenerUsuarioID();

            var esOperador = await UsuarioEsOperadorAsync(usuarioId, cn);

            if (!esOperador)
                return AccesoDenegadoOperador();

            if (vm.EjecucionProduccionID <= 0)
            {
                TempData["Error"] = "No se recibió la ejecución de producción.";
                return RedirectToAction(nameof(Index));
            }

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

            try
            {
                var ejecucion = await ObtenerEjecucionOperadorAsync(
                    vm.EjecucionProduccionID,
                    cn,
                    tx);

                if (ejecucion == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                if (ejecucion.EstatusID != ProduccionEstatus.EnProduccion)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "Solo puedes reportar paro cuando la producción está en serie.";

                    return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
                }

                var tieneParoAbierto = await TieneParoAbiertoAsync(
                    vm.EjecucionProduccionID,
                    cn,
                    tx);

                if (tieneParoAbierto)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "Ya existe un paro abierto para esta producción.";

                    return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
                }

                var motivoTexto = vm.MotivoParoTexto;

                if (vm.MotivoParoID.HasValue)
                {
                    motivoTexto = await ObtenerMotivoParoNombreAsync(
                        vm.MotivoParoID.Value,
                        cn,
                        tx);
                }

                var personaOperador = await ObtenerPersonaOperadorAsync(usuarioId, cn, tx);

                const string sqlInsert = @"
INSERT INTO dbo.Produccion_Paros
(
    EjecucionProduccionID,
    ProgramaProduccionID,
    SolicitudProduccionID,
    MaquinaID,
    OperadorID,
    FechaInicioParo,
    MotivoParoID,
    MotivoParoTexto,
    Descripcion,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
VALUES
(
    @EjecucionProduccionID,
    @ProgramaProduccionID,
    @SolicitudProduccionID,
    @MaquinaID,
    @OperadorID,
    GETDATE(),
    @MotivoParoID,
    @MotivoParoTexto,
    @Descripcion,
    @UsuarioID,
    GETDATE(),
    1
);";

                await using (var cmd = new SqlCommand(sqlInsert, cn, tx))
                {
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                        ejecucion.EjecucionProduccionID;

                    cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value =
                        ejecucion.ProgramaProduccionID;

                    cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value =
                        (object?)ejecucion.SolicitudProduccionID ?? DBNull.Value;

                    cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value =
                        (object?)ejecucion.MaquinaID ?? DBNull.Value;

                    cmd.Parameters.Add("@OperadorID", SqlDbType.Int).Value =
                        personaOperador.PersonaID;

                    cmd.Parameters.Add("@MotivoParoID", SqlDbType.Int).Value =
                        (object?)vm.MotivoParoID ?? DBNull.Value;

                    cmd.Parameters.Add("@MotivoParoTexto", SqlDbType.NVarChar, 200).Value =
                        (object?)motivoTexto ?? DBNull.Value;

                    cmd.Parameters.Add("@Descripcion", SqlDbType.NVarChar, 500).Value =
                        string.IsNullOrWhiteSpace(vm.Descripcion)
                            ? DBNull.Value
                            : vm.Descripcion.Trim();

                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                        usuarioId;

                    await cmd.ExecuteNonQueryAsync();
                }

                await CambiarEstatusEjecucionAsync(
                    ejecucion.EjecucionProduccionID,
                    ProduccionEstatus.Pausado,
                    usuarioId,
                    cn,
                    tx);

                await CambiarEstatusProgramaAsync(
                    ejecucion.ProgramaProduccionID,
                    ProgramaProduccionEstatus.Pausado,
                    usuarioId,
                    cn,
                    tx);

                await tx.CommitAsync();

                TempData["Success"] = "Paro reportado correctamente.";

                return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "No fue posible reportar el paro: " + ex.Message;

                return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CerrarParo(ProduccionCerrarParoPostVm vm)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var usuarioId = ObtenerUsuarioID();

            var esOperador = await UsuarioEsOperadorAsync(usuarioId, cn);

            if (!esOperador)
                return AccesoDenegadoOperador();

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

            int ejecucionProduccionId;

            try
            {
                const string sqlLeer = @"
SELECT TOP (1)
    ParoID,
    EjecucionProduccionID,
    FechaInicioParo
FROM dbo.Produccion_Paros
WHERE ParoID = @ParoID
  AND Activo = 1
  AND FechaFinParo IS NULL;";

                DateTime fechaInicioParo;

                await using (var cmd = new SqlCommand(sqlLeer, cn, tx))
                {
                    cmd.Parameters.Add("@ParoID", SqlDbType.Int).Value =
                        vm.ParoID;

                    await using var rd = await cmd.ExecuteReaderAsync();

                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();

                        TempData["Error"] =
                            "No se encontró un paro abierto para cerrar.";

                        return RedirectToAction(nameof(Index));
                    }

                    ejecucionProduccionId =
                        Convert.ToInt32(rd["EjecucionProduccionID"]);

                    fechaInicioParo =
                        Convert.ToDateTime(rd["FechaInicioParo"]);
                }

                var duracionMinutos =
                    (int)Math.Max(0, (DateTime.Now - fechaInicioParo).TotalMinutes);

                var esMayorA15Minutos = duracionMinutos > 15;

                const string sqlCerrar = @"
UPDATE dbo.Produccion_Paros
SET
    FechaFinParo = GETDATE(),
    DuracionMinutos = @DuracionMinutos,
    EsMayorA15Minutos = CASE WHEN @DuracionMinutos > 15 THEN 1 ELSE 0 END,
    Descripcion =
        CASE
            WHEN @ObservacionesCierre IS NULL OR LTRIM(RTRIM(@ObservacionesCierre)) = ''
                THEN Descripcion
            WHEN Descripcion IS NULL OR LTRIM(RTRIM(Descripcion)) = ''
                THEN @ObservacionesCierre
            ELSE Descripcion + CHAR(13) + CHAR(10) + 'Cierre: ' + @ObservacionesCierre
        END,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE ParoID = @ParoID
  AND Activo = 1;";

                await using (var cmd = new SqlCommand(sqlCerrar, cn, tx))
                {
                    cmd.Parameters.Add("@ParoID", SqlDbType.Int).Value =
                        vm.ParoID;

                    cmd.Parameters.Add("@DuracionMinutos", SqlDbType.Int).Value =
                        duracionMinutos;

                    cmd.Parameters.Add("@ObservacionesCierre", SqlDbType.NVarChar, 500).Value =
                        string.IsNullOrWhiteSpace(vm.ObservacionesCierre)
                            ? DBNull.Value
                            : vm.ObservacionesCierre.Trim();

                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                        usuarioId;

                    await cmd.ExecuteNonQueryAsync();
                }

                var ejecucion = await ObtenerEjecucionOperadorAsync(
                    ejecucionProduccionId,
                    cn,
                    tx);

                if (ejecucion == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                if (esMayorA15Minutos)
                {
                    await CambiarEstatusEjecucionAsync(
                        ejecucionProduccionId,
                        ProduccionEstatus.EnPreparacion,
                        usuarioId,
                        cn,
                        tx);

                    await CambiarEstatusProgramaAsync(
                        ejecucion.ProgramaProduccionID,
                        ProgramaProduccionEstatus.EnPreparacion,
                        usuarioId,
                        cn,
                        tx);

                    await CrearOActualizarSolicitudReliberacionCalidadPorParoAsync(
                        ejecucionProduccionId,
                        vm.ParoID,
                        duracionMinutos,
                        usuarioId,
                        cn,
                        tx);

                    await tx.CommitAsync();

                    TempData["Success"] =
                        "Paro cerrado. Duró más de 15 minutos, por lo que la producción regresó a preparación. " +
                        "Debe ejecutar nuevamente los 5 disparos de prueba y solicitar reliberación de Calidad.";

                    return RedirectToAction(
                        "Detalle",
                        "Produccion",
                        new { id = ejecucionProduccionId });
                }

                await CambiarEstatusEjecucionAsync(
                    ejecucionProduccionId,
                    ProduccionEstatus.EnProduccion,
                    usuarioId,
                    cn,
                    tx);

                await CambiarEstatusProgramaAsync(
                    ejecucion.ProgramaProduccionID,
                    ProgramaProduccionEstatus.EnProduccion,
                    usuarioId,
                    cn,
                    tx);

                await tx.CommitAsync();

                TempData["Success"] =
                    "Paro cerrado correctamente. La producción continúa en serie.";

                return RedirectToAction(
                    nameof(Captura),
                    new { id = ejecucionProduccionId });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "No fue posible cerrar el paro: " + ex.Message;

                return RedirectToAction(nameof(Index));
            }
        }

        // ============================================================
        // CAJAS PRODUCCION - PUNTO 12 EN ADELANTE
        // ============================================================

        [HttpGet]
        public async Task<IActionResult> Cajas(int id)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var usuarioId = ObtenerUsuarioID();

            var esOperador = await UsuarioEsOperadorAsync(usuarioId, cn);

            if (!esOperador)
                return AccesoDenegadoOperador();

            var vm = await ObtenerCajasOperadorVmAsync(id, cn);

            if (vm == null)
                return NotFound();

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FormarCaja(
     int ejecucionProduccionId,
     int cantidadPiezas,
     string tipoCaja,
     string? loteMaterial,
     string? etiquetaFolio,
     string? observaciones)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var usuarioId = ObtenerUsuarioID();

            var esOperador = await UsuarioEsOperadorAsync(usuarioId, cn);

            if (!esOperador)
                return AccesoDenegadoOperador();

            if (ejecucionProduccionId <= 0)
            {
                TempData["Error"] = "No se recibió la ejecución de producción.";
                return RedirectToAction(nameof(Index));
            }

            if (cantidadPiezas <= 0)
            {
                TempData["Error"] = "La cantidad de piezas de la caja debe ser mayor a cero.";
                return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
            }

            var tipoNormalizado = NormalizarTipoCajaOperador(tipoCaja);

            if (string.IsNullOrWhiteSpace(tipoNormalizado))
            {
                TempData["Error"] = "El tipo de caja no es válido.";
                return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
            }

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

            try
            {
                var ejecucion = await ObtenerEjecucionOperadorAsync(
                    ejecucionProduccionId,
                    cn,
                    tx);

                if (ejecucion == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                if (ejecucion.EstatusID != ProduccionEstatus.EnProduccion)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "Solo puedes formar cajas cuando la producción está en serie.";

                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                var tieneParoAbierto = await TieneParoAbiertoAsync(
                    ejecucionProduccionId,
                    cn,
                    tx);

                if (tieneParoAbierto)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "No puedes formar cajas mientras exista un paro abierto.";

                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                var capturadoDisponible = await ObtenerCantidadDisponibleParaCajaAsync(
                    ejecucionProduccionId,
                    tipoNormalizado,
                    cn,
                    tx);

                if (cantidadPiezas > capturadoDisponible)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "No puedes formar la caja porque la cantidad excede lo capturado disponible para el tipo " +
                        tipoNormalizado + ". Disponible: " + capturadoDisponible.ToString("N0") + " pieza(s).";

                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                var siguienteNumero = await ObtenerSiguienteNumeroCajaAsync(
                    ejecucionProduccionId,
                    cn,
                    tx);

                var folioCaja = CrearFolioCajaOperador(
                    ejecucion,
                    siguienteNumero,
                    etiquetaFolio);

                const string sqlInsert = @"
INSERT INTO dbo.Produccion_Cajas
(
    EjecucionProduccionID,
    ProgramaProduccionID,
    SolicitudProduccionID,
    SolicitudProduccionDetalleID,
    ReleaseID,
    ReleaseDetalleID,

    NumeroCaja,
    FolioCaja,

    CantidadPiezas,
    TipoCaja,
    LoteMaterial,
    EtiquetaFolio,

    EstadoCajaID,
    EstadoCajaNombre,
    EtiquetaVerde,

    FechaFormacion,
    UsuarioFormacionID,

    Observaciones,

    Activo,
    UsuarioCreacionID,
    FechaCreacion,

    Etiqueta,
    Cantidad,
    EstatusCalidad,
    OperadorUsuarioID
)
VALUES
(
    @EjecucionProduccionID,
    @ProgramaProduccionID,
    @SolicitudProduccionID,
    @SolicitudProduccionDetalleID,
    @ReleaseID,
    @ReleaseDetalleID,

    @NumeroCaja,
    @FolioCaja,

    @CantidadPiezas,
    @TipoCaja,
    @LoteMaterial,
    @EtiquetaFolio,

    @EstadoCajaID,
    @EstadoCajaNombre,
    0,

    GETDATE(),
    @UsuarioID,

    @Observaciones,

    1,
    @UsuarioID,
    GETDATE(),

    @EtiquetaCompatibilidad,
    @CantidadCompatibilidad,
    N'FORMADA',
    @UsuarioID
);";

                await using (var cmd = new SqlCommand(sqlInsert, cn, tx))
                {
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                        ejecucion.EjecucionProduccionID;

                    cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value =
                        ejecucion.ProgramaProduccionID;

                    cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value =
                        (object?)ejecucion.SolicitudProduccionID ?? DBNull.Value;

                    cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value =
                        (object?)ejecucion.SolicitudProduccionDetalleID ?? DBNull.Value;

                    cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value =
                        (object?)ejecucion.ReleaseID ?? DBNull.Value;

                    cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value =
                        (object?)ejecucion.ReleaseDetalleID ?? DBNull.Value;

                    cmd.Parameters.Add("@NumeroCaja", SqlDbType.Int).Value =
                        siguienteNumero;

                    cmd.Parameters.Add("@FolioCaja", SqlDbType.NVarChar, 100).Value =
                        (object?)folioCaja ?? DBNull.Value;

                    cmd.Parameters.Add("@CantidadPiezas", SqlDbType.Int).Value =
                        cantidadPiezas;

                    cmd.Parameters.Add("@TipoCaja", SqlDbType.NVarChar, 30).Value =
                        tipoNormalizado;

                    cmd.Parameters.Add("@LoteMaterial", SqlDbType.NVarChar, 100).Value =
                        string.IsNullOrWhiteSpace(loteMaterial)
                            ? DBNull.Value
                            : loteMaterial.Trim();

                    cmd.Parameters.Add("@EtiquetaFolio", SqlDbType.NVarChar, 100).Value =
                        string.IsNullOrWhiteSpace(etiquetaFolio)
                            ? DBNull.Value
                            : etiquetaFolio.Trim();

                    cmd.Parameters.Add("@EstadoCajaID", SqlDbType.Int).Value =
                        ProduccionCajaEstatus.FormadaProduccion;

                    cmd.Parameters.Add("@EstadoCajaNombre", SqlDbType.NVarChar, 100).Value =
                        ProduccionCajaEstatus.Nombre(ProduccionCajaEstatus.FormadaProduccion);

                    cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value =
                        string.IsNullOrWhiteSpace(observaciones)
                            ? DBNull.Value
                            : observaciones.Trim();

                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                        usuarioId;

                    cmd.Parameters.Add("@EtiquetaCompatibilidad", SqlDbType.NVarChar, 100).Value =
                        string.IsNullOrWhiteSpace(etiquetaFolio)
                            ? (object?)folioCaja ?? DBNull.Value
                            : etiquetaFolio.Trim();

                    cmd.Parameters.Add("@CantidadCompatibilidad", SqlDbType.Int).Value =
                        cantidadPiezas;

                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();

                TempData["Success"] =
                    "Caja " + siguienteNumero.ToString() + " formada correctamente.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "No fue posible formar la caja: " + ex.Message;
            }

            return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SolicitarLiberacionCaja(
            int cajaProduccionId)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var usuarioId = ObtenerUsuarioID();

            var esOperador = await UsuarioEsOperadorAsync(usuarioId, cn);

            if (!esOperador)
                return AccesoDenegadoOperador();

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

            int ejecucionProduccionId = 0;

            try
            {
                var caja = await ObtenerCajaOperadorAsync(cajaProduccionId, cn, tx);

                if (caja == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                ejecucionProduccionId = caja.EjecucionProduccionID;

                if (caja.EstadoCajaID != ProduccionCajaEstatus.FormadaProduccion)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "Solo puedes solicitar liberación de una caja formada en Producción.";

                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                const string sql = @"
UPDATE dbo.Produccion_Cajas
SET
    EstadoCajaID = @EstadoCajaID,
    EstadoCajaNombre = @EstadoCajaNombre,
    FechaSolicitudCalidad = GETDATE(),
    UsuarioSolicitudCalidadID = @UsuarioID,
    EstatusCalidad = N'PENDIENTE',
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE CajaProduccionID = @CajaProduccionID
  AND Activo = 1;";

                await using var cmd = new SqlCommand(sql, cn, tx);

                cmd.Parameters.Add("@CajaProduccionID", SqlDbType.Int).Value =
                    cajaProduccionId;

                cmd.Parameters.Add("@EstadoCajaID", SqlDbType.Int).Value =
                    ProduccionCajaEstatus.PendienteCalidad;

                cmd.Parameters.Add("@EstadoCajaNombre", SqlDbType.NVarChar, 100).Value =
                    ProduccionCajaEstatus.Nombre(ProduccionCajaEstatus.PendienteCalidad);

                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                    usuarioId;

                await cmd.ExecuteNonQueryAsync();

                await tx.CommitAsync();

                TempData["Success"] =
                    "Caja enviada a Calidad para liberación.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "No fue posible solicitar liberación de la caja: " + ex.Message;
            }

            return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MoverCajaZonaVerde(
     int cajaProduccionId)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var usuarioId = ObtenerUsuarioID();

            var esOperador = await UsuarioEsOperadorAsync(usuarioId, cn);

            if (!esOperador)
                return AccesoDenegadoOperador();

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

            int ejecucionProduccionId = 0;

            try
            {
                var caja = await ObtenerCajaOperadorAsync(cajaProduccionId, cn, tx);

                if (caja == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                ejecucionProduccionId = caja.EjecucionProduccionID;

                if (caja.EstadoCajaID != ProduccionCajaEstatus.LiberadaCalidad || !caja.EtiquetaVerde)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "No puedes mover esta caja a zona verde. Primero debe estar liberada por Calidad con etiqueta verde.";

                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                const string sql = @"
UPDATE dbo.Produccion_Cajas
SET
    EstadoCajaID = @EstadoCajaID,
    EstadoCajaNombre = @EstadoCajaNombre,
    FechaZonaVerde = GETDATE(),
    UsuarioZonaVerdeID = @UsuarioID,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE CajaProduccionID = @CajaProduccionID
  AND Activo = 1;";

                await using var cmd = new SqlCommand(sql, cn, tx);

                cmd.Parameters.Add("@CajaProduccionID", SqlDbType.Int).Value =
                    cajaProduccionId;

                cmd.Parameters.Add("@EstadoCajaID", SqlDbType.Int).Value =
                    ProduccionCajaEstatus.ZonaVerde;

                cmd.Parameters.Add("@EstadoCajaNombre", SqlDbType.NVarChar, 100).Value =
                    ProduccionCajaEstatus.Nombre(ProduccionCajaEstatus.ZonaVerde);

                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                    usuarioId;

                await cmd.ExecuteNonQueryAsync();

                await tx.CommitAsync();

                TempData["Success"] =
                    "Caja movida a zona verde.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "No fue posible mover la caja a zona verde: " + ex.Message;
            }

            return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EscanearSalidaCaja(
     int cajaProduccionId,
     string? etiquetaEscaneada)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var usuarioId = ObtenerUsuarioID();

            var esOperador = await UsuarioEsOperadorAsync(usuarioId, cn);

            if (!esOperador)
                return AccesoDenegadoOperador();

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

            int ejecucionProduccionId = 0;

            try
            {
                var caja = await ObtenerCajaOperadorAsync(cajaProduccionId, cn, tx);

                if (caja == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                ejecucionProduccionId = caja.EjecucionProduccionID;

                if (caja.EstadoCajaID != ProduccionCajaEstatus.ZonaVerde)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "Solo puedes escanear salida de Producción cuando la caja ya está en zona verde.";

                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                if (!string.IsNullOrWhiteSpace(caja.EtiquetaFolio) &&
                    !string.IsNullOrWhiteSpace(etiquetaEscaneada) &&
                    !string.Equals(caja.EtiquetaFolio.Trim(), etiquetaEscaneada.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "La etiqueta escaneada no coincide con la etiqueta registrada en la caja.";

                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                const string sql = @"
UPDATE dbo.Produccion_Cajas
SET
    EstadoCajaID = @EstadoCajaID,
    EstadoCajaNombre = @EstadoCajaNombre,
    FechaSalidaProduccion = GETDATE(),
    UsuarioSalidaProduccionID = @UsuarioID,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE CajaProduccionID = @CajaProduccionID
  AND Activo = 1;";

                await using var cmd = new SqlCommand(sql, cn, tx);

                cmd.Parameters.Add("@CajaProduccionID", SqlDbType.Int).Value =
                    cajaProduccionId;

                cmd.Parameters.Add("@EstadoCajaID", SqlDbType.Int).Value =
                    ProduccionCajaEstatus.SalidaProduccion;

                cmd.Parameters.Add("@EstadoCajaNombre", SqlDbType.NVarChar, 100).Value =
                    ProduccionCajaEstatus.Nombre(ProduccionCajaEstatus.SalidaProduccion);

                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                    usuarioId;

                await cmd.ExecuteNonQueryAsync();

                await tx.CommitAsync();

                TempData["Success"] =
                    "Salida de Producción escaneada correctamente. Pendiente recepción de Almacén PT.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "No fue posible escanear la salida de Producción: " + ex.Message;
            }

            return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
        }


        private async Task<ProduccionOperadorCajasVm?> ObtenerCajasOperadorVmAsync(
    int ejecucionProduccionId,
    SqlConnection cn)
        {
            const string sql = @"
SELECT TOP (1)
    e.EjecucionProduccionID,
    e.ProgramaProduccionID,
    e.SolicitudProduccionID,

    s.FolioSolicitud,
    s.NumeroOFRecibida,

    pp.ClienteNombre,

    e.MaquinaCodigo,
    e.MaquinaNombre,

    e.NumeroParte,
    e.ReferenciaSAP,
    e.DescripcionParte,

    e.MoldeCodigo,

    pp.MaterialCodigo,
    pp.MaterialDescripcion,
    pp.EmbalajeCodigo,
    pp.EmbalajeDescripcion,

    ISNULL(e.CantidadPlaneada, 0) AS CantidadPlaneada,
    ISNULL(e.CantidadOKTotal, 0) AS CantidadOKTotal,
    ISNULL(e.CantidadSospechosaTotal, 0) AS CantidadSospechosaTotal,
    ISNULL(e.CantidadScrapTotal, 0) AS CantidadScrapTotal,

    e.EstatusID,

    CASE
        WHEN EXISTS
        (
            SELECT 1
            FROM dbo.Produccion_Paros p
            WHERE p.EjecucionProduccionID = e.EjecucionProduccionID
              AND p.Activo = 1
              AND p.FechaFinParo IS NULL
        )
        THEN 1 ELSE 0
    END AS TieneParoAbierto
FROM dbo.Produccion_Ejecucion e
LEFT JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID = e.SolicitudProduccionID
LEFT JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.ProgramaProduccionID = e.ProgramaProduccionID
   AND pp.Activo = 1
WHERE e.EjecucionProduccionID = @EjecucionProduccionID
  AND e.Activo = 1;";

            ProduccionOperadorCajasVm? vm = null;

            await using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                    ejecucionProduccionId;

                await using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    return null;

                vm = new ProduccionOperadorCajasVm
                {
                    EjecucionProduccionID = Entero(rd, "EjecucionProduccionID"),
                    ProgramaProduccionID = Entero(rd, "ProgramaProduccionID"),
                    SolicitudProduccionID = NullableEntero(rd, "SolicitudProduccionID"),

                    FolioSolicitud = TextoNullable(rd, "FolioSolicitud"),
                    NumeroOFRecibida = TextoNullable(rd, "NumeroOFRecibida"),

                    ClienteNombre = TextoNullable(rd, "ClienteNombre"),

                    MaquinaCodigo = TextoNullable(rd, "MaquinaCodigo"),
                    MaquinaNombre = TextoNullable(rd, "MaquinaNombre"),

                    NumeroParte = TextoNullable(rd, "NumeroParte"),
                    ReferenciaSAP = TextoNullable(rd, "ReferenciaSAP"),
                    DescripcionParte = TextoNullable(rd, "DescripcionParte"),

                    MoldeCodigo = TextoNullable(rd, "MoldeCodigo"),

                    MaterialCodigo = TextoNullable(rd, "MaterialCodigo"),
                    MaterialDescripcion = TextoNullable(rd, "MaterialDescripcion"),

                    EmbalajeCodigo = TextoNullable(rd, "EmbalajeCodigo"),
                    EmbalajeDescripcion = TextoNullable(rd, "EmbalajeDescripcion"),

                    CantidadPlaneada = Entero(rd, "CantidadPlaneada"),
                    CantidadOKTotal = Entero(rd, "CantidadOKTotal"),
                    CantidadSospechosaTotal = Entero(rd, "CantidadSospechosaTotal"),
                    CantidadScrapTotal = Entero(rd, "CantidadScrapTotal"),

                    EstatusID = Entero(rd, "EstatusID"),
                    TieneParoAbierto = Booleano(rd, "TieneParoAbierto")
                };
            }

            vm.Cajas = await ObtenerCajasPorEjecucionAsync(ejecucionProduccionId, cn);

            vm.CantidadOKEnCajas = vm.Cajas
                .Where(x => x.TipoCaja == "OK")
                .Sum(x => x.CantidadPiezas);

            vm.CantidadSospechosaEnCajas = vm.Cajas
                .Where(x => x.TipoCaja == "SOSPECHOSO")
                .Sum(x => x.CantidadPiezas);

            vm.CantidadScrapEnCajas = vm.Cajas
                .Where(x => x.TipoCaja == "SCRAP")
                .Sum(x => x.CantidadPiezas);

            vm.CantidadRetencionEnCajas = vm.Cajas
                .Where(x => x.TipoCaja == "RETENCION")
                .Sum(x => x.CantidadPiezas);

            vm.SiguienteNumeroCaja =
                vm.Cajas.Any()
                    ? vm.Cajas.Max(x => x.NumeroCaja) + 1
                    : 1;

            vm.PuedeFormarCaja =
                vm.EstatusID == ProduccionEstatus.EnProduccion &&
                !vm.TieneParoAbierto;

            return vm;
        }

        private async Task<List<ProduccionOperadorCajaVm>> ObtenerCajasPorEjecucionAsync(
            int ejecucionProduccionId,
            SqlConnection cn)
        {
            var lista = new List<ProduccionOperadorCajaVm>();

            const string sql = @"
SELECT
    CajaProduccionID,
    EjecucionProduccionID,
    ISNULL(ProgramaProduccionID, 0) AS ProgramaProduccionID,

    ISNULL(NumeroCaja, 0) AS NumeroCaja,
    FolioCaja,

    ISNULL(CantidadPiezas, ISNULL(Cantidad, 0)) AS CantidadPiezas,
    ISNULL(TipoCaja, N'OK') AS TipoCaja,

    LoteMaterial,
    ISNULL(EtiquetaFolio, Etiqueta) AS EtiquetaFolio,

    ISNULL(EtiquetaVerde, 0) AS EtiquetaVerde,

    ISNULL(EstadoCajaID, 1) AS EstadoCajaID,
    ISNULL(EstadoCajaNombre, N'Formada en Producción') AS EstadoCajaNombre,

    ISNULL(FechaFormacion, FechaCreacion) AS FechaFormacion,
    UsuarioFormacionID,

    FechaSolicitudCalidad,
    UsuarioSolicitudCalidadID,

    FechaLiberacionCalidad,
    UsuarioCalidadID,

    ResultadoCalidad,
    MotivoCalidad,

    FechaZonaVerde,
    UsuarioZonaVerdeID,

    FechaSalidaProduccion,
    UsuarioSalidaProduccionID,

    FechaRecepcionAlmacen,
    UsuarioAlmacenID,

    Observaciones
FROM dbo.Produccion_Cajas
WHERE EjecucionProduccionID = @EjecucionProduccionID
  AND Activo = 1
ORDER BY
    NumeroCaja,
    CajaProduccionID;";

            await using var cmd = new SqlCommand(sql, cn);

            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                ejecucionProduccionId;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(MapearCajaOperador(rd));
            }

            return lista;
        }

        private async Task<ProduccionOperadorCajaVm?> ObtenerCajaOperadorAsync(
            int cajaProduccionId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP (1)
    CajaProduccionID,
    EjecucionProduccionID,
    ISNULL(ProgramaProduccionID, 0) AS ProgramaProduccionID,

    ISNULL(NumeroCaja, 0) AS NumeroCaja,
    FolioCaja,

    ISNULL(CantidadPiezas, ISNULL(Cantidad, 0)) AS CantidadPiezas,
    ISNULL(TipoCaja, N'OK') AS TipoCaja,

    LoteMaterial,
    ISNULL(EtiquetaFolio, Etiqueta) AS EtiquetaFolio,

    ISNULL(EtiquetaVerde, 0) AS EtiquetaVerde,

    ISNULL(EstadoCajaID, 1) AS EstadoCajaID,
    ISNULL(EstadoCajaNombre, N'Formada en Producción') AS EstadoCajaNombre,

    ISNULL(FechaFormacion, FechaCreacion) AS FechaFormacion,
    UsuarioFormacionID,

    FechaSolicitudCalidad,
    UsuarioSolicitudCalidadID,

    FechaLiberacionCalidad,
    UsuarioCalidadID,

    ResultadoCalidad,
    MotivoCalidad,

    FechaZonaVerde,
    UsuarioZonaVerdeID,

    FechaSalidaProduccion,
    UsuarioSalidaProduccionID,

    FechaRecepcionAlmacen,
    UsuarioAlmacenID,

    Observaciones
FROM dbo.Produccion_Cajas
WHERE CajaProduccionID = @CajaProduccionID
  AND Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@CajaProduccionID", SqlDbType.Int).Value =
                cajaProduccionId;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return null;

            return MapearCajaOperador(rd);
        }

        private static ProduccionOperadorCajaVm MapearCajaOperador(
            SqlDataReader rd)
        {
            return new ProduccionOperadorCajaVm
            {
                CajaProduccionID = Entero(rd, "CajaProduccionID"),
                EjecucionProduccionID = Entero(rd, "EjecucionProduccionID"),
                ProgramaProduccionID = Entero(rd, "ProgramaProduccionID"),

                NumeroCaja = Entero(rd, "NumeroCaja"),
                FolioCaja = TextoNullable(rd, "FolioCaja"),

                CantidadPiezas = Entero(rd, "CantidadPiezas"),
                TipoCaja = TextoNullable(rd, "TipoCaja") ?? "OK",

                LoteMaterial = TextoNullable(rd, "LoteMaterial"),
                EtiquetaFolio = TextoNullable(rd, "EtiquetaFolio"),

                EtiquetaVerde = Booleano(rd, "EtiquetaVerde"),

                EstadoCajaID = Entero(rd, "EstadoCajaID"),
                EstadoCajaNombre =
                    TextoNullable(rd, "EstadoCajaNombre") ?? "Formada en Producción",

                FechaFormacion = Fecha(rd, "FechaFormacion"),
                UsuarioFormacionID = NullableEntero(rd, "UsuarioFormacionID"),

                FechaSolicitudCalidad = NullableFecha(rd, "FechaSolicitudCalidad"),
                UsuarioSolicitudCalidadID = NullableEntero(rd, "UsuarioSolicitudCalidadID"),

                FechaLiberacionCalidad = NullableFecha(rd, "FechaLiberacionCalidad"),
                UsuarioCalidadID = NullableEntero(rd, "UsuarioCalidadID"),

                ResultadoCalidad = TextoNullable(rd, "ResultadoCalidad"),
                MotivoCalidad = TextoNullable(rd, "MotivoCalidad"),

                FechaZonaVerde = NullableFecha(rd, "FechaZonaVerde"),
                UsuarioZonaVerdeID = NullableEntero(rd, "UsuarioZonaVerdeID"),

                FechaSalidaProduccion = NullableFecha(rd, "FechaSalidaProduccion"),
                UsuarioSalidaProduccionID = NullableEntero(rd, "UsuarioSalidaProduccionID"),

                FechaRecepcionAlmacen = NullableFecha(rd, "FechaRecepcionAlmacen"),
                UsuarioAlmacenID = NullableEntero(rd, "UsuarioAlmacenID"),

                Observaciones = TextoNullable(rd, "Observaciones")
            };
        }

        private async Task<int> ObtenerSiguienteNumeroCajaAsync(
    int ejecucionProduccionId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            const string sql = @"
SELECT ISNULL(MAX(NumeroCaja), 0) + 1
FROM dbo.Produccion_Cajas
WHERE EjecucionProduccionID = @EjecucionProduccionID
  AND Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                ejecucionProduccionId;

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        private async Task<int> ObtenerCantidadDisponibleParaCajaAsync(
            int ejecucionProduccionId,
            string tipoCaja,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT
    ISNULL(e.CantidadOKTotal, 0) AS OKTotal,
    ISNULL(e.CantidadSospechosaTotal, 0) AS SospechosaTotal,
    ISNULL(e.CantidadScrapTotal, 0) AS ScrapTotal,

    ISNULL((
        SELECT SUM(ISNULL(c.CantidadPiezas, ISNULL(c.Cantidad, 0)))
        FROM dbo.Produccion_Cajas c
        WHERE c.EjecucionProduccionID = e.EjecucionProduccionID
          AND c.Activo = 1
          AND ISNULL(c.TipoCaja, N'OK') = @TipoCaja
    ), 0) AS YaEnCajas
FROM dbo.Produccion_Ejecucion e
WHERE e.EjecucionProduccionID = @EjecucionProduccionID
  AND e.Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                ejecucionProduccionId;

            cmd.Parameters.Add("@TipoCaja", SqlDbType.NVarChar, 30).Value =
                tipoCaja;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return 0;

            var totalBase = 0;

            if (tipoCaja == "OK")
                totalBase = Entero(rd, "OKTotal");
            else if (tipoCaja == "SOSPECHOSO")
                totalBase = Entero(rd, "SospechosaTotal");
            else if (tipoCaja == "SCRAP")
                totalBase = Entero(rd, "ScrapTotal");
            else if (tipoCaja == "RETENCION")
                totalBase = Entero(rd, "SospechosaTotal");

            var yaEnCajas = Entero(rd, "YaEnCajas");

            var disponible = totalBase - yaEnCajas;

            return disponible < 0 ? 0 : disponible;
        }

        private static string NormalizarTipoCajaOperador(string? tipoCaja)
        {
            var valor = string.IsNullOrWhiteSpace(tipoCaja)
                ? ""
                : tipoCaja.Trim().ToUpperInvariant();

            if (valor == "OK")
                return "OK";

            if (valor == "SOSPECHOSA" || valor == "SOSPECHOSO")
                return "SOSPECHOSO";

            if (valor == "SCRAP")
                return "SCRAP";

            if (valor == "RETENCION" || valor == "RETENCIÓN")
                return "RETENCION";

            return "";
        }

        private static string CrearFolioCajaOperador(
            ProduccionEjecucionVm ejecucion,
            int numeroCaja,
            string? etiquetaFolio)
        {
            if (!string.IsNullOrWhiteSpace(etiquetaFolio))
                return etiquetaFolio.Trim();

            var baseFolio =
                !string.IsNullOrWhiteSpace(ejecucion.ReferenciaSAP)
                    ? ejecucion.ReferenciaSAP.Trim()
                    : !string.IsNullOrWhiteSpace(ejecucion.NumeroParte)
                        ? ejecucion.NumeroParte.Trim()
                        : "PROG-" + ejecucion.ProgramaProduccionID.ToString();

            return baseFolio + "-C" + numeroCaja.ToString("000");
        }



        private async Task CrearOActualizarSolicitudReliberacionCalidadPorParoAsync(
    int ejecucionProduccionId,
    int paroId,
    int duracionMinutos,
    int usuarioId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            const string sqlDatos = @"
SELECT TOP (1)
    e.EjecucionProduccionID,
    e.ProgramaProduccionID,
    e.SolicitudProduccionID,
    e.SolicitudProduccionDetalleID,
    e.ReleaseID,
    e.ReleaseDetalleID,

    e.MaquinaID,
    e.MaquinaCodigo,
    e.MaquinaNombre,

    e.MoldeID,
    e.MoldeCodigo,

    e.ParteID,
    e.NumeroParte,
    e.ReferenciaSAP,
    e.DescripcionParte,

    e.OperadorID,
    e.OperadorNombre,
    e.CantidadPlaneada,

    pp.ClienteID,
    pp.ClienteNombre,
    pp.MaterialID,
    pp.MaterialCodigo,
    pp.MaterialDescripcion,
    pp.FechaInicioProgramada,
    pp.FechaFinProgramada,

    sp.NumeroOFRecibida,
    sp.FolioSolicitud,

    ca.ChecklistArranqueID
FROM dbo.Produccion_Ejecucion e
INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.ProgramaProduccionID = e.ProgramaProduccionID
   AND pp.Activo = 1
LEFT JOIN dbo.SolicitudesProduccion sp
    ON sp.SolicitudProduccionID = e.SolicitudProduccionID
OUTER APPLY
(
    SELECT TOP (1)
        c.ChecklistArranqueID
    FROM dbo.Produccion_ChecklistArranque c
    WHERE c.EjecucionProduccionID = e.EjecucionProduccionID
      AND c.Activo = 1
    ORDER BY c.ChecklistArranqueID DESC
) ca
WHERE e.EjecucionProduccionID = @EjecucionProduccionID
  AND e.Activo = 1;";

            int programaProduccionId;
            int? solicitudProduccionId;
            int? solicitudProduccionDetalleId;
            int? releaseId;
            int? releaseDetalleId;
            int? clienteId;
            string? clienteNombre;
            int? parteId;
            int? maquinaId;
            int? moldeId;
            int? materialId;
            int? checklistArranqueId;
            string? ordenTrabajo;
            string? numeroParte;
            string? material;
            string? maquina;
            string? molde;
            DateTime? fechaInicioProgramada;
            DateTime? fechaFinProgramada;
            int? operadorPrincipalId;
            string? operadorPrincipalNombre;
            int cantidadTotal;

            await using (var cmd = new SqlCommand(sqlDatos, cn, tx))
            {
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                    ejecucionProduccionId;

                await using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                {
                    throw new InvalidOperationException(
                        "No se encontró la información de la ejecución para solicitar reliberación de Calidad.");
                }

                programaProduccionId = Entero(rd, "ProgramaProduccionID");
                solicitudProduccionId = NullableEntero(rd, "SolicitudProduccionID");
                solicitudProduccionDetalleId = NullableEntero(rd, "SolicitudProduccionDetalleID");
                releaseId = NullableEntero(rd, "ReleaseID");
                releaseDetalleId = NullableEntero(rd, "ReleaseDetalleID");

                clienteId = NullableEntero(rd, "ClienteID");
                clienteNombre = TextoNullable(rd, "ClienteNombre");

                parteId = NullableEntero(rd, "ParteID");
                maquinaId = NullableEntero(rd, "MaquinaID");
                moldeId = NullableEntero(rd, "MoldeID");
                materialId = NullableEntero(rd, "MaterialID");
                checklistArranqueId = NullableEntero(rd, "ChecklistArranqueID");

                ordenTrabajo =
                    TextoNullable(rd, "NumeroOFRecibida") ??
                    TextoNullable(rd, "FolioSolicitud") ??
                    ("PROG-" + programaProduccionId.ToString());

                numeroParte =
                    TextoNullable(rd, "ReferenciaSAP") ??
                    TextoNullable(rd, "NumeroParte");

                material = UnirTextoProduccionOperador(
                    TextoNullable(rd, "MaterialCodigo"),
                    TextoNullable(rd, "MaterialDescripcion"));

                maquina = UnirTextoProduccionOperador(
                    TextoNullable(rd, "MaquinaCodigo"),
                    TextoNullable(rd, "MaquinaNombre"));

                molde = TextoNullable(rd, "MoldeCodigo");

                fechaInicioProgramada = NullableFecha(rd, "FechaInicioProgramada");
                fechaFinProgramada = NullableFecha(rd, "FechaFinProgramada");

                operadorPrincipalId = NullableEntero(rd, "OperadorID");
                operadorPrincipalNombre = TextoNullable(rd, "OperadorNombre");

                cantidadTotal = Entero(rd, "CantidadPlaneada");
            }

            if (!checklistArranqueId.HasValue || checklistArranqueId.Value <= 0)
            {
                throw new InvalidOperationException(
                    "No existe checklist de arranque para esta ejecución. No se puede solicitar reliberación de Calidad.");
            }

            const string sqlInvalidarAnterior = @"
UPDATE dbo.Calidad_Inspecciones
SET
    ConfiguracionInvalidada = 1,
    Estado = 'INVALIDADA_POR_PARO',
    Observaciones =
        CASE
            WHEN Observaciones IS NULL OR LTRIM(RTRIM(Observaciones)) = ''
                THEN @ObservacionInvalidacion
            ELSE Observaciones + CHAR(13) + CHAR(10) + @ObservacionInvalidacion
        END,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE EjecucionProduccionID = @EjecucionProduccionID
  AND ISNULL(ConfiguracionInvalidada, 0) = 0
  AND Estado <> 'CERRADA';";

            await using (var cmd = new SqlCommand(sqlInvalidarAnterior, cn, tx))
            {
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                    ejecucionProduccionId;

                cmd.Parameters.Add("@ObservacionInvalidacion", SqlDbType.NVarChar, 500).Value =
                    "Inspección invalidada automáticamente por paro mayor a 15 minutos. " +
                    "Se requiere reliberación antes de volver a iniciar serie.";

                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                    usuarioId;

                await cmd.ExecuteNonQueryAsync();
            }

            const string sqlInsert = @"
INSERT INTO dbo.Calidad_Inspecciones
(
    ProgramaProduccionID,
    EjecucionProduccionID,
    ChecklistArranqueID,

    SolicitudProduccionID,
    SolicitudProduccionDetalleID,
    ReleaseID,
    ReleaseDetalleID,

    ClienteID,
    ClienteNombre,

    ParteID,
    MaquinaID,
    MoldeID,
    MaterialID,

    OrdenTrabajo,
    NumeroParte,
    Material,
    Proceso,
    Maquina,
    Molde,

    FechaInicioProgramada,
    FechaFinProgramada,

    OperadorPrincipalPersonaID,
    OperadorPrincipalNombre,

    CantidadTotal,
    CantidadRevisada,
    CantidadPendiente,

    ChecklistValidado,
    HojaInspeccionProducto,
    HojaValidacionCalidad,

    FechaNotificacionCalidad,
    UsuarioNotificoID,

    CincoDisparosSegregados,
    CantidadDisparosConformes,

    Liberado,
    RequiereGP12,
    EnContencion,
    EsScrap,
    ConfiguracionInvalidada,

    Observaciones,
    Estado,

    UsuarioCreacionID,
    FechaCreacion
)
VALUES
(
    @ProgramaProduccionID,
    @EjecucionProduccionID,
    @ChecklistArranqueID,

    @SolicitudProduccionID,
    @SolicitudProduccionDetalleID,
    @ReleaseID,
    @ReleaseDetalleID,

    @ClienteID,
    @ClienteNombre,

    @ParteID,
    @MaquinaID,
    @MoldeID,
    @MaterialID,

    @OrdenTrabajo,
    @NumeroParte,
    @Material,
    @Proceso,
    @Maquina,
    @Molde,

    @FechaInicioProgramada,
    @FechaFinProgramada,

    @OperadorPrincipalPersonaID,
    @OperadorPrincipalNombre,

    @CantidadTotal,
    0,
    @CantidadTotal,

    1,
    0,
    0,

    GETDATE(),
    @UsuarioID,

    0,
    0,

    0,
    0,
    0,
    0,
    0,

    @Observaciones,
    'PENDIENTE_PREARRANQUE',

    @UsuarioID,
    GETDATE()
);";

            await using (var cmd = new SqlCommand(sqlInsert, cn, tx))
            {
                cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value =
                    programaProduccionId;

                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                    ejecucionProduccionId;

                cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value =
                    checklistArranqueId.Value;

                cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value =
                    (object?)solicitudProduccionId ?? DBNull.Value;

                cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value =
                    (object?)solicitudProduccionDetalleId ?? DBNull.Value;

                cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value =
                    (object?)releaseId ?? DBNull.Value;

                cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value =
                    (object?)releaseDetalleId ?? DBNull.Value;

                cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value =
                    (object?)clienteId ?? DBNull.Value;

                cmd.Parameters.Add("@ClienteNombre", SqlDbType.NVarChar, 200).Value =
                    (object?)clienteNombre ?? DBNull.Value;

                cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value =
                    (object?)parteId ?? DBNull.Value;

                cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value =
                    (object?)maquinaId ?? DBNull.Value;

                cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value =
                    (object?)moldeId ?? DBNull.Value;

                cmd.Parameters.Add("@MaterialID", SqlDbType.Int).Value =
                    (object?)materialId ?? DBNull.Value;

                cmd.Parameters.Add("@OrdenTrabajo", SqlDbType.NVarChar, 100).Value =
                    (object?)ordenTrabajo ?? DBNull.Value;

                cmd.Parameters.Add("@NumeroParte", SqlDbType.NVarChar, 150).Value =
                    (object?)numeroParte ?? DBNull.Value;

                cmd.Parameters.Add("@Material", SqlDbType.NVarChar, 300).Value =
                    (object?)material ?? DBNull.Value;

                cmd.Parameters.Add("@Proceso", SqlDbType.NVarChar, 150).Value =
                    "RELIBERACIÓN POR PARO MAYOR A 15 MIN";

                cmd.Parameters.Add("@Maquina", SqlDbType.NVarChar, 300).Value =
                    (object?)maquina ?? DBNull.Value;

                cmd.Parameters.Add("@Molde", SqlDbType.NVarChar, 150).Value =
                    (object?)molde ?? DBNull.Value;

                cmd.Parameters.Add("@FechaInicioProgramada", SqlDbType.DateTime).Value =
                    (object?)fechaInicioProgramada ?? DBNull.Value;

                cmd.Parameters.Add("@FechaFinProgramada", SqlDbType.DateTime).Value =
                    (object?)fechaFinProgramada ?? DBNull.Value;

                cmd.Parameters.Add("@OperadorPrincipalPersonaID", SqlDbType.Int).Value =
                    (object?)operadorPrincipalId ?? DBNull.Value;

                cmd.Parameters.Add("@OperadorPrincipalNombre", SqlDbType.NVarChar, 200).Value =
                    (object?)operadorPrincipalNombre ?? DBNull.Value;

                var cantidadParam = cmd.Parameters.Add("@CantidadTotal", SqlDbType.Decimal);
                cantidadParam.Precision = 18;
                cantidadParam.Scale = 2;
                cantidadParam.Value = cantidadTotal;

                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value =
                    "Solicitud automática de reliberación por paro mayor a 15 minutos. " +
                    "ParoID: " + paroId.ToString() + ". " +
                    "Duración: " + duracionMinutos.ToString() + " minutos. " +
                    "De acuerdo al flujo, Producción debe regresar a 5 disparos de prueba y solicitar validación de Calidad.";

                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                    usuarioId;

                await cmd.ExecuteNonQueryAsync();
            }
        }

        private async Task<List<ProduccionOperadorTabletVm>> ObtenerProgramasEnProduccionAsync(
            SqlConnection cn)
        {
            var lista = new List<ProduccionOperadorTabletVm>();

            const string sql = @"
SELECT
    e.EjecucionProduccionID,
    e.ProgramaProduccionID,
    e.SolicitudProduccionID,
    s.FolioSolicitud,
    s.NumeroOFRecibida,

    e.MaquinaID,
    e.MaquinaCodigo,
    e.MaquinaNombre,

    e.ParteID,
    e.NumeroParte,
    e.ReferenciaSAP,
    e.DescripcionParte,

    e.OperadorID,
    e.OperadorNombre,

    e.CantidadPlaneada,
    e.CantidadOKTotal,
    e.CantidadSospechosaTotal,
    e.CantidadScrapTotal,

    e.EstatusID,

    CASE
        WHEN EXISTS
        (
            SELECT 1
            FROM dbo.Produccion_Paros p
            WHERE p.EjecucionProduccionID = e.EjecucionProduccionID
              AND p.Activo = 1
              AND p.FechaFinParo IS NULL
        )
        THEN 1 ELSE 0
    END AS TieneParoAbierto,

    (
        SELECT TOP (1)
            p.ParoID
        FROM dbo.Produccion_Paros p
        WHERE p.EjecucionProduccionID = e.EjecucionProduccionID
          AND p.Activo = 1
          AND p.FechaFinParo IS NULL
        ORDER BY p.ParoID DESC
    ) AS ParoAbiertoID
FROM dbo.Produccion_Ejecucion e
LEFT JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID = e.SolicitudProduccionID
LEFT JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.ProgramaProduccionID = e.ProgramaProduccionID
   AND pp.Activo = 1
WHERE e.Activo = 1
  AND e.EstatusID IN (@EnProduccion, @Pausado)
ORDER BY
    e.MaquinaCodigo,
    ISNULL(pp.FechaInicioProgramada, e.FechaInicioReal),
    e.EjecucionProduccionID;";

            await using var cmd = new SqlCommand(sql, cn);

            cmd.Parameters.Add("@EnProduccion", SqlDbType.Int).Value =
                ProduccionEstatus.EnProduccion;

            cmd.Parameters.Add("@Pausado", SqlDbType.Int).Value =
                ProduccionEstatus.Pausado;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var vm = MapearTabletVm(rd);
                AsignarHoraSugerida(vm);
                lista.Add(vm);
            }

            return lista;
        }

        private async Task<ProduccionOperadorTabletVm?> ObtenerTabletVmAsync(
            int ejecucionProduccionId,
            SqlConnection cn)
        {
            const string sql = @"
SELECT
    e.EjecucionProduccionID,
    e.ProgramaProduccionID,
    e.SolicitudProduccionID,
    s.FolioSolicitud,
    s.NumeroOFRecibida,

    e.MaquinaID,
    e.MaquinaCodigo,
    e.MaquinaNombre,

    e.ParteID,
    e.NumeroParte,
    e.ReferenciaSAP,
    e.DescripcionParte,

    e.OperadorID,
    e.OperadorNombre,

    e.CantidadPlaneada,
    e.CantidadOKTotal,
    e.CantidadSospechosaTotal,
    e.CantidadScrapTotal,

    e.EstatusID,

    CASE
        WHEN EXISTS
        (
            SELECT 1
            FROM dbo.Produccion_Paros p
            WHERE p.EjecucionProduccionID = e.EjecucionProduccionID
              AND p.Activo = 1
              AND p.FechaFinParo IS NULL
        )
        THEN 1 ELSE 0
    END AS TieneParoAbierto,

    (
        SELECT TOP (1)
            p.ParoID
        FROM dbo.Produccion_Paros p
        WHERE p.EjecucionProduccionID = e.EjecucionProduccionID
          AND p.Activo = 1
          AND p.FechaFinParo IS NULL
        ORDER BY p.ParoID DESC
    ) AS ParoAbiertoID
FROM dbo.Produccion_Ejecucion e
LEFT JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID = e.SolicitudProduccionID
WHERE e.EjecucionProduccionID = @EjecucionProduccionID
  AND e.Activo = 1
  AND e.EstatusID IN (@EnProduccion, @Pausado);";

            await using var cmd = new SqlCommand(sql, cn);

            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                ejecucionProduccionId;

            cmd.Parameters.Add("@EnProduccion", SqlDbType.Int).Value =
                ProduccionEstatus.EnProduccion;

            cmd.Parameters.Add("@Pausado", SqlDbType.Int).Value =
                ProduccionEstatus.Pausado;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return null;

            var vm = MapearTabletVm(rd);
            AsignarHoraSugerida(vm);

            return vm;
        }

        private static string? UnirTextoProduccionOperador(string? codigo, string? descripcion)
        {
            var partes = new List<string>();

            if (!string.IsNullOrWhiteSpace(codigo))
                partes.Add(codigo.Trim());

            if (!string.IsNullOrWhiteSpace(descripcion))
                partes.Add(descripcion.Trim());

            return partes.Count == 0
                ? null
                : string.Join(" - ", partes);
        }

        private async Task<ProduccionEjecucionVm?> ObtenerEjecucionOperadorAsync(
            int ejecucionProduccionId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT
    EjecucionProduccionID,
    ProgramaProduccionID,
    SolicitudProduccionID,
    SolicitudProduccionDetalleID,
    ReleaseID,
    ReleaseDetalleID,

    MaquinaID,
    MaquinaCodigo,
    MaquinaNombre,

    ParteID,
    NumeroParte,
    ReferenciaSAP,
    DescripcionParte,

    MoldeID,
    MoldeCodigo,

    OperadorID,
    OperadorNombre,

    FechaInicioReal,
    FechaFinReal,

    CantidadPlaneada,
    CantidadOKTotal,
    CantidadSospechosaTotal,
    CantidadScrapTotal,

    EstatusID,
    Observaciones,

    UsuarioCreacionID,
    FechaCreacion,
    UsuarioModificacionID,
    FechaModificacion,
    Activo
FROM dbo.Produccion_Ejecucion
WHERE EjecucionProduccionID = @EjecucionProduccionID
  AND Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                ejecucionProduccionId;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return null;

            return new ProduccionEjecucionVm
            {
                EjecucionProduccionID = Entero(rd, "EjecucionProduccionID"),
                ProgramaProduccionID = Entero(rd, "ProgramaProduccionID"),
                SolicitudProduccionID = NullableEntero(rd, "SolicitudProduccionID"),
                SolicitudProduccionDetalleID = NullableEntero(rd, "SolicitudProduccionDetalleID"),
                ReleaseID = NullableEntero(rd, "ReleaseID"),
                ReleaseDetalleID = NullableEntero(rd, "ReleaseDetalleID"),

                MaquinaID = NullableEntero(rd, "MaquinaID"),
                MaquinaCodigo = TextoNullable(rd, "MaquinaCodigo"),
                MaquinaNombre = TextoNullable(rd, "MaquinaNombre"),

                ParteID = NullableEntero(rd, "ParteID"),
                NumeroParte = TextoNullable(rd, "NumeroParte"),
                ReferenciaSAP = TextoNullable(rd, "ReferenciaSAP"),
                DescripcionParte = TextoNullable(rd, "DescripcionParte"),

                MoldeID = NullableEntero(rd, "MoldeID"),
                MoldeCodigo = TextoNullable(rd, "MoldeCodigo"),

                OperadorID = NullableEntero(rd, "OperadorID"),
                OperadorNombre = TextoNullable(rd, "OperadorNombre"),

                FechaInicioReal = NullableFecha(rd, "FechaInicioReal"),
                FechaFinReal = NullableFecha(rd, "FechaFinReal"),

                CantidadPlaneada = NullableEntero(rd, "CantidadPlaneada"),
                CantidadOKTotal = Entero(rd, "CantidadOKTotal"),
                CantidadSospechosaTotal = Entero(rd, "CantidadSospechosaTotal"),
                CantidadScrapTotal = Entero(rd, "CantidadScrapTotal"),

                EstatusID = Entero(rd, "EstatusID"),
                Observaciones = TextoNullable(rd, "Observaciones"),

                UsuarioCreacionID = NullableEntero(rd, "UsuarioCreacionID"),
                FechaCreacion = Fecha(rd, "FechaCreacion"),
                UsuarioModificacionID = NullableEntero(rd, "UsuarioModificacionID"),
                FechaModificacion = NullableFecha(rd, "FechaModificacion"),
                Activo = Booleano(rd, "Activo")
            };
        }

        // ============================================================
        // ESCRITURAS
        // ============================================================

        private async Task InsertarRegistroHoraAsync(
            ProduccionEjecucionVm ejecucion,
            ProduccionRegistroHoraPostVm vm,
            TimeSpan horaInicio,
            TimeSpan horaFin,
            int operadorPersonaId,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
INSERT INTO dbo.Produccion_RegistroHora
(
    EjecucionProduccionID,
    ProgramaProduccionID,
    SolicitudProduccionID,
    MaquinaID,
    OperadorID,
    FechaProduccion,
    HoraInicio,
    HoraFin,
    CantidadOK,
    CantidadSospechosa,
    CantidadScrap,
    Observaciones,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
VALUES
(
    @EjecucionProduccionID,
    @ProgramaProduccionID,
    @SolicitudProduccionID,
    @MaquinaID,
    @OperadorID,
    @FechaProduccion,
    @HoraInicio,
    @HoraFin,
    @CantidadOK,
    @CantidadSospechosa,
    @CantidadScrap,
    @Observaciones,
    @UsuarioID,
    GETDATE(),
    1
);";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                ejecucion.EjecucionProduccionID;

            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value =
                ejecucion.ProgramaProduccionID;

            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value =
                (object?)ejecucion.SolicitudProduccionID ?? DBNull.Value;

            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value =
                (object?)ejecucion.MaquinaID ?? DBNull.Value;

            cmd.Parameters.Add("@OperadorID", SqlDbType.Int).Value =
                operadorPersonaId;

            cmd.Parameters.Add("@FechaProduccion", SqlDbType.Date).Value =
                vm.FechaProduccion.Date;

            cmd.Parameters.Add("@HoraInicio", SqlDbType.Time).Value =
                horaInicio;

            cmd.Parameters.Add("@HoraFin", SqlDbType.Time).Value =
                horaFin;

            cmd.Parameters.Add("@CantidadOK", SqlDbType.Int).Value =
                vm.CantidadOK;

            cmd.Parameters.Add("@CantidadSospechosa", SqlDbType.Int).Value =
                vm.CantidadSospechosa;

            cmd.Parameters.Add("@CantidadScrap", SqlDbType.Int).Value =
                vm.CantidadScrap;

            cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value =
                string.IsNullOrWhiteSpace(vm.Observaciones)
                    ? DBNull.Value
                    : vm.Observaciones.Trim();

            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task RecalcularTotalesEjecucionAsync(
            int ejecucionProduccionId,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
;WITH Totales AS
(
    SELECT
        EjecucionProduccionID,
        SUM(ISNULL(CantidadOK, 0)) AS OKTotal,
        SUM(ISNULL(CantidadSospechosa, 0)) AS SospechosaTotal,
        SUM(ISNULL(CantidadScrap, 0)) AS ScrapTotal
    FROM dbo.Produccion_RegistroHora
    WHERE EjecucionProduccionID = @EjecucionProduccionID
      AND Activo = 1
    GROUP BY EjecucionProduccionID
)
UPDATE e
SET
    e.CantidadOKTotal = ISNULL(t.OKTotal, 0),
    e.CantidadSospechosaTotal = ISNULL(t.SospechosaTotal, 0),
    e.CantidadScrapTotal = ISNULL(t.ScrapTotal, 0),
    e.UsuarioModificacionID = @UsuarioID,
    e.FechaModificacion = GETDATE()
FROM dbo.Produccion_Ejecucion e
LEFT JOIN Totales t
    ON t.EjecucionProduccionID = e.EjecucionProduccionID
WHERE e.EjecucionProduccionID = @EjecucionProduccionID;

UPDATE pp
SET
    pp.CantidadProducida = ISNULL(e.CantidadOKTotal, 0),
    pp.HorasReales =
        CASE
            WHEN e.FechaInicioReal IS NOT NULL
                THEN CONVERT(DECIMAL(18,2), DATEDIFF(MINUTE, e.FechaInicioReal, GETDATE()) / 60.0)
            ELSE pp.HorasReales
        END,
    pp.UsuarioModificacionID = @UsuarioID,
    pp.FechaModificacion = GETDATE()
FROM dbo.Planeacion_ProgramaProduccion pp
INNER JOIN dbo.Produccion_Ejecucion e
    ON e.ProgramaProduccionID = pp.ProgramaProduccionID
WHERE e.EjecucionProduccionID = @EjecucionProduccionID
  AND pp.Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                ejecucionProduccionId;

            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task CambiarEstatusEjecucionAsync(
            int ejecucionProduccionId,
            int estatusId,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Produccion_Ejecucion
SET
    EstatusID = @EstatusID,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE EjecucionProduccionID = @EjecucionProduccionID
  AND Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                ejecucionProduccionId;

            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value =
                estatusId;

            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task CambiarEstatusProgramaAsync(
            int programaProduccionId,
            int estatusId,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Planeacion_ProgramaProduccion
SET
    EstatusID = @EstatusID,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE ProgramaProduccionID = @ProgramaProduccionID
  AND Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value =
                programaProduccionId;

            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value =
                estatusId;

            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }

        // ============================================================
        // CATALOGOS
        // ============================================================

        private async Task<List<SelectListItem>> CargarMotivosParoAsync(
            SqlConnection cn)
        {
            var lista = new List<SelectListItem>
            {
                new() { Value = "", Text = "Selecciona motivo" }
            };

            const string sql = @"
SELECT
    MotivoParoID,
    Nombre
FROM dbo.ERP_MotivosParoProduccion
WHERE Activo = 1
ORDER BY Nombre;";

            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new SelectListItem
                {
                    Value = rd["MotivoParoID"].ToString(),
                    Text = rd["Nombre"].ToString()
                });
            }

            return lista;
        }

        private async Task<string?> ObtenerMotivoParoNombreAsync(
            int motivoParoId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP (1)
    Nombre
FROM dbo.ERP_MotivosParoProduccion
WHERE MotivoParoID = @MotivoParoID
  AND Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@MotivoParoID", SqlDbType.Int).Value =
                motivoParoId;

            var result = await cmd.ExecuteScalarAsync();

            return result == null || result == DBNull.Value
                ? null
                : result.ToString();
        }

        // ============================================================
        // VALIDACION OPERADOR
        // ============================================================

        private sealed class PersonaOperadorInfo
        {
            public int PersonaID { get; set; }
            public string NombreCompleto { get; set; } = string.Empty;
            public string? Puesto { get; set; }
        }

        private async Task<bool> UsuarioEsOperadorAsync(
            int usuarioId,
            SqlConnection cn)
        {
            const string sql = @"
SELECT TOP (1)
    p.Puesto
FROM dbo.Usuarios u
INNER JOIN dbo.Persona p
    ON p.PersonaID = u.PersonaID
WHERE u.UsuarioID = @UsuarioID
  AND u.Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn);

            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                usuarioId;

            var result = await cmd.ExecuteScalarAsync();

            if (result == null || result == DBNull.Value)
                return false;

            var puesto = result.ToString()?.Trim() ?? string.Empty;

            return puesto.Equals("OPERADOR", StringComparison.OrdinalIgnoreCase) ||
                   puesto.Contains("OPERADOR", StringComparison.OrdinalIgnoreCase);
        }


        private async Task<List<ProduccionAlertaProximoProgramaVm>> ObtenerAlertasProximosProgramasAsync(
      SqlConnection cn,
      int minutosAntes)
        {
            var lista = new List<ProduccionAlertaProximoProgramaVm>();

            const string sql = @"
DECLARE @Ahora DATETIME = GETDATE();
DECLARE @Hasta DATETIME = DATEADD(MINUTE, @MinutosAntes, @Ahora);

;WITH ProgramasBase AS
(
    SELECT
        pp.ProgramaProduccionID,
        pe.EjecucionProduccionID,

        pp.MaquinaID,
        COALESCE(NULLIF(pp.MaquinaCodigo, ''), maq.Codigo) AS MaquinaCodigo,
        COALESCE(NULLIF(pp.MaquinaNombre, ''), maq.Nombre) AS MaquinaNombre,

        pp.ParteID,
        pp.NumeroParte,
        pp.ReferenciaSAP,
        pp.DesignacionDescripcionSAP AS DescripcionParte,

        pp.MoldeID,
        pp.MoldeCodigo,

        CONVERT(INT, ISNULL(pp.CantidadProgramada, 0)) AS CantidadProgramada,

        pp.FechaInicioProgramada,
        pp.FechaFinProgramada,

        CASE
            WHEN pp.Cambio IS NULL THEN NULL
            ELSE DATEADD
            (
                SECOND,
                DATEDIFF(SECOND, CAST('00:00:00' AS TIME), CAST(pp.Cambio AS TIME)),
                CAST(CAST(ISNULL(pp.FechaInicioProgramada, GETDATE()) AS DATE) AS DATETIME)
            )
        END AS FechaCambioMolde,

        CASE
            WHEN pp.Arranque IS NULL THEN ISNULL(pp.FechaInicioProgramada, GETDATE())
            ELSE DATEADD
            (
                SECOND,
                DATEDIFF(SECOND, CAST('00:00:00' AS TIME), CAST(pp.Arranque AS TIME)),
                CAST(CAST(ISNULL(pp.FechaInicioProgramada, GETDATE()) AS DATE) AS DATETIME)
            )
        END AS FechaArranque,

        opPrincipal.PersonaID AS OperadorPrincipalID,
        opPrincipal.NombreCompleto AS OperadorPrincipalNombre,

        opAuxiliar.PersonaID AS OperadorAuxiliarID,
        opAuxiliar.NombreCompleto AS OperadorAuxiliarNombre

    FROM dbo.Planeacion_ProgramaProduccion pp

    LEFT JOIN dbo.ERP_Maquinas maq
        ON maq.MaquinaID = pp.MaquinaID

    OUTER APPLY
    (
        SELECT TOP (1)
            e.EjecucionProduccionID
        FROM dbo.Produccion_Ejecucion e
        WHERE e.ProgramaProduccionID = pp.ProgramaProduccionID
          AND e.Activo = 1
        ORDER BY e.EjecucionProduccionID DESC
    ) pe

    OUTER APPLY
    (
        SELECT TOP (1)
            po.PersonaID,
            LTRIM(RTRIM(
                ISNULL(p.Nombre, '') + ' ' +
                ISNULL(p.ApellidoPaterno, '') + ' ' +
                ISNULL(p.ApellidoMaterno, '')
            )) AS NombreCompleto
        FROM dbo.Planeacion_ProgramaOperadores po
        LEFT JOIN dbo.Persona p
            ON p.PersonaID = po.PersonaID
        WHERE po.ProgramaProduccionID = pp.ProgramaProduccionID
          AND po.Activo = 1
          AND UPPER(ISNULL(po.RolOperador, '')) = 'PRINCIPAL'
        ORDER BY po.ProgramaOperadorID
    ) opPrincipal

    OUTER APPLY
    (
        SELECT TOP (1)
            po.PersonaID,
            LTRIM(RTRIM(
                ISNULL(p.Nombre, '') + ' ' +
                ISNULL(p.ApellidoPaterno, '') + ' ' +
                ISNULL(p.ApellidoMaterno, '')
            )) AS NombreCompleto
        FROM dbo.Planeacion_ProgramaOperadores po
        LEFT JOIN dbo.Persona p
            ON p.PersonaID = po.PersonaID
        WHERE po.ProgramaProduccionID = pp.ProgramaProduccionID
          AND po.Activo = 1
          AND UPPER(ISNULL(po.RolOperador, '')) = 'AUXILIAR'
        ORDER BY po.ProgramaOperadorID
    ) opAuxiliar

    WHERE pp.Activo = 1
      AND pp.MaquinaID IS NOT NULL
      AND ISNULL(pp.EstatusID, 1) IN
      (
          @EstatusPendiente,
          @EstatusEnPreparacion
      )
),
Alertas AS
(
    SELECT
        ProgramaProduccionID,
        EjecucionProduccionID,

        MaquinaID,
        MaquinaCodigo,
        MaquinaNombre,

        ParteID,
        NumeroParte,
        ReferenciaSAP,
        DescripcionParte,

        MoldeID,
        MoldeCodigo,

        CantidadProgramada,

        'CAMBIO_MOLDE' AS TipoAlerta,
        FechaCambioMolde AS FechaObjetivo,

        OperadorPrincipalID,
        OperadorPrincipalNombre,
        OperadorAuxiliarID,
        OperadorAuxiliarNombre

    FROM ProgramasBase
    WHERE FechaCambioMolde IS NOT NULL
      AND FechaCambioMolde <= @Hasta
      AND FechaCambioMolde >= DATEADD(MINUTE, -5, @Ahora)
      AND
      (
          FechaArranque IS NULL
          OR FechaCambioMolde < FechaArranque
      )

    UNION ALL

    SELECT
        ProgramaProduccionID,
        EjecucionProduccionID,

        MaquinaID,
        MaquinaCodigo,
        MaquinaNombre,

        ParteID,
        NumeroParte,
        ReferenciaSAP,
        DescripcionParte,

        MoldeID,
        MoldeCodigo,

        CantidadProgramada,

        'ARRANQUE' AS TipoAlerta,
        FechaArranque AS FechaObjetivo,

        OperadorPrincipalID,
        OperadorPrincipalNombre,
        OperadorAuxiliarID,
        OperadorAuxiliarNombre

    FROM ProgramasBase
    WHERE FechaArranque IS NOT NULL
      AND FechaArranque <= @Hasta
      AND FechaArranque >= DATEADD(MINUTE, -5, @Ahora)
)
SELECT
    ProgramaProduccionID,
    EjecucionProduccionID,

    MaquinaID,
    MaquinaCodigo,
    MaquinaNombre,

    ParteID,
    NumeroParte,
    ReferenciaSAP,
    DescripcionParte,

    MoldeID,
    MoldeCodigo,

    CantidadProgramada,

    TipoAlerta,
    FechaObjetivo,
    DATEDIFF(MINUTE, @Ahora, FechaObjetivo) AS MinutosRestantes,

    OperadorPrincipalID,
    OperadorPrincipalNombre,
    OperadorAuxiliarID,
    OperadorAuxiliarNombre

FROM Alertas
ORDER BY
    FechaObjetivo,
    CASE
        WHEN TipoAlerta = 'CAMBIO_MOLDE' THEN 1
        ELSE 2
    END,
    MaquinaCodigo,
    ProgramaProduccionID;";

            await using var cmd = new SqlCommand(sql, cn);

            cmd.Parameters.Add("@MinutosAntes", SqlDbType.Int).Value =
                minutosAntes;

            cmd.Parameters.Add("@EstatusPendiente", SqlDbType.Int).Value =
                ProgramaProduccionEstatus.Pendiente;

            cmd.Parameters.Add("@EstatusEnPreparacion", SqlDbType.Int).Value =
                ProgramaProduccionEstatus.EnPreparacion;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new ProduccionAlertaProximoProgramaVm
                {
                    ProgramaProduccionID = Entero(rd, "ProgramaProduccionID"),
                    EjecucionProduccionID = NullableEntero(rd, "EjecucionProduccionID"),

                    MaquinaID = NullableEntero(rd, "MaquinaID"),
                    MaquinaCodigo = TextoNullable(rd, "MaquinaCodigo"),
                    MaquinaNombre = TextoNullable(rd, "MaquinaNombre"),

                    ParteID = NullableEntero(rd, "ParteID"),
                    NumeroParte = TextoNullable(rd, "NumeroParte"),
                    ReferenciaSAP = TextoNullable(rd, "ReferenciaSAP"),
                    DescripcionParte = TextoNullable(rd, "DescripcionParte"),

                    MoldeID = NullableEntero(rd, "MoldeID"),
                    MoldeCodigo = TextoNullable(rd, "MoldeCodigo"),

                    CantidadProgramada = Entero(rd, "CantidadProgramada"),

                    TipoAlerta = TextoNullable(rd, "TipoAlerta") ?? "",
                    FechaObjetivo = Convert.ToDateTime(rd["FechaObjetivo"]),
                    MinutosRestantes = Entero(rd, "MinutosRestantes"),

                    OperadorPrincipalID = NullableEntero(rd, "OperadorPrincipalID"),
                    OperadorPrincipalNombre = TextoNullable(rd, "OperadorPrincipalNombre"),

                    OperadorAuxiliarID = NullableEntero(rd, "OperadorAuxiliarID"),
                    OperadorAuxiliarNombre = TextoNullable(rd, "OperadorAuxiliarNombre")
                });
            }

            return lista;
        }


        private async Task<PersonaOperadorInfo> ObtenerPersonaOperadorAsync(
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP (1)
    p.PersonaID,
    LTRIM(RTRIM(
        ISNULL(p.Nombre, '') + ' ' +
        ISNULL(p.ApellidoPaterno, '') + ' ' +
        ISNULL(p.ApellidoMaterno, '')
    )) AS NombreCompleto,
    p.Puesto
FROM dbo.Usuarios u
INNER JOIN dbo.Persona p
    ON p.PersonaID = u.PersonaID
WHERE u.UsuarioID = @UsuarioID
  AND u.Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                usuarioId;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                throw new InvalidOperationException("No se encontró la persona vinculada al usuario.");

            return new PersonaOperadorInfo
            {
                PersonaID = Entero(rd, "PersonaID"),
                NombreCompleto = TextoNullable(rd, "NombreCompleto") ?? string.Empty,
                Puesto = TextoNullable(rd, "Puesto")
            };
        }

        private IActionResult AccesoDenegadoOperador()
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;

            return Content(
                "Acceso denegado. Esta pantalla es exclusiva para usuarios con puesto OPERADOR.",
                "text/plain");
        }

        // ============================================================
        // MAPEO
        // ============================================================

        private static ProduccionOperadorTabletVm MapearTabletVm(
            SqlDataReader rd)
        {
            return new ProduccionOperadorTabletVm
            {
                EjecucionProduccionID = Entero(rd, "EjecucionProduccionID"),
                ProgramaProduccionID = Entero(rd, "ProgramaProduccionID"),

                SolicitudProduccionID = NullableEntero(rd, "SolicitudProduccionID"),
                FolioSolicitud = TextoNullable(rd, "FolioSolicitud"),
                NumeroOFRecibida = TextoNullable(rd, "NumeroOFRecibida"),

                MaquinaID = NullableEntero(rd, "MaquinaID"),
                MaquinaCodigo = TextoNullable(rd, "MaquinaCodigo"),
                MaquinaNombre = TextoNullable(rd, "MaquinaNombre"),

                ParteID = NullableEntero(rd, "ParteID"),
                NumeroParte = TextoNullable(rd, "NumeroParte"),
                ReferenciaSAP = TextoNullable(rd, "ReferenciaSAP"),
                DescripcionParte = TextoNullable(rd, "DescripcionParte"),

                OperadorID = NullableEntero(rd, "OperadorID"),
                OperadorNombre = TextoNullable(rd, "OperadorNombre"),

                CantidadPlaneada = NullableEntero(rd, "CantidadPlaneada"),
                CantidadOKTotal = Entero(rd, "CantidadOKTotal"),
                CantidadSospechosaTotal = Entero(rd, "CantidadSospechosaTotal"),
                CantidadScrapTotal = Entero(rd, "CantidadScrapTotal"),

                EstatusID = Entero(rd, "EstatusID"),

                TieneParoAbierto = Booleano(rd, "TieneParoAbierto"),
                ParoAbiertoID = NullableEntero(rd, "ParoAbiertoID")
            };
        }

        private static void AsignarHoraSugerida(
            ProduccionOperadorTabletVm vm)
        {
            var ahora = DateTime.Now;
            var inicio = new TimeSpan(ahora.Hour, 0, 0);
            var fin = inicio.Add(TimeSpan.FromHours(1));

            if (fin.TotalHours >= 24)
                fin = new TimeSpan(23, 59, 0);

            vm.FechaProduccion = DateTime.Today;
            vm.HoraInicioSugerida = inicio;
            vm.HoraFinSugerida = fin;
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private bool UsuarioEnSesion()
        {
            return HttpContext.Session.GetInt32("UsuarioID").HasValue;
        }

        private int ObtenerUsuarioID()
        {
            return HttpContext.Session.GetInt32("UsuarioID") ?? 0;
        }

        private async Task<bool> TieneParoAbiertoAsync(
            int ejecucionProduccionId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT COUNT(1)
FROM dbo.Produccion_Paros
WHERE EjecucionProduccionID = @EjecucionProduccionID
  AND Activo = 1
  AND FechaFinParo IS NULL;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                ejecucionProduccionId;

            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }

        private static int Entero(SqlDataReader rd, string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? 0
                : Convert.ToInt32(rd.GetValue(ordinal));
        }

        private static int? NullableEntero(SqlDataReader rd, string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? null
                : Convert.ToInt32(rd.GetValue(ordinal));
        }

        private static DateTime Fecha(SqlDataReader rd, string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? DateTime.MinValue
                : Convert.ToDateTime(rd.GetValue(ordinal));
        }

        private static DateTime? NullableFecha(SqlDataReader rd, string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? null
                : Convert.ToDateTime(rd.GetValue(ordinal));
        }

        private static bool Booleano(SqlDataReader rd, string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return !rd.IsDBNull(ordinal) &&
                   Convert.ToBoolean(rd.GetValue(ordinal));
        }

        private static string? TextoNullable(SqlDataReader rd, string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? null
                : rd.GetValue(ordinal)?.ToString()?.Trim();
        }
    }
}