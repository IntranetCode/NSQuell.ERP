using ERP.NSQuell.Models;
using ERP.NSQuell.Servicios.Almacen;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.Data;
using static ERP.NSQuell.Models.ProduccionChecklistArranqueVm;
using static ERP.NSQuell.Models.ProduccionOperadorCajasVm;

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

            await using var cn =
                new SqlConnection(ConnectionString);

            await cn.OpenAsync();

            var usuarioId =
                ObtenerUsuarioID();

            var esOperador =
                await UsuarioEsOperadorAsync(
                    usuarioId,
                    cn);

            if (!esOperador)
                return AccesoDenegadoOperador();

            var personaId =
                await ObtenerPersonaIDUsuarioAsync(
                    usuarioId,
                    cn);

            if (!personaId.HasValue ||
                personaId.Value <= 0)
            {
                return AccesoDenegadoOperador();
            }

            var programas =
                await ObtenerProgramasEnProduccionAsync(
                    personaId.Value,
                    cn);

            ViewBag.AlertasProximosProgramas =
                await ObtenerAlertasProximosProgramasAsync(
                    personaId.Value,
                    cn,
                    15);

            return View(programas);
        }

        [HttpGet]
        public async Task<IActionResult> Captura(int id)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            if (id <= 0) return NotFound();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var usuarioId = ObtenerUsuarioID();
            if (!await UsuarioEsOperadorAsync(usuarioId, cn)) return AccesoDenegadoOperador();

            var personaId = await ObtenerPersonaIDUsuarioAsync(usuarioId, cn);
            if (!personaId.HasValue || personaId.Value <= 0) return AccesoDenegadoOperador();

            if (!await PersonaAsignadaAEjecucionAsync(id, personaId.Value, cn))
            {
                TempData["Error"] = "Esta ejecución no se encuentra asignada al operador conectado.";
                return RedirectToAction(nameof(Index));
            }

            var vm = await ObtenerTabletVmAsync(id, cn);
            if (vm == null) return NotFound();

            vm.ConfiguracionActual = await ObtenerConfiguracionActualOperadorAsync(id, cn);
            vm.UltimoContadorMaquina = await ObtenerUltimaLecturaContadorMaquinaAsync(id, cn);
            vm.BonusOperadorActual = await ObtenerBonusOperadorActualAsync(personaId.Value, cn);
            vm.MotivosParo = await CargarMotivosParoAsync(cn);
            vm.HorasCaptura = await ObtenerFilasCapturaHoraAsync(vm.EjecucionProduccionID, vm.ProgramaProduccionID, cn);
            vm.HistorialCambiosTurno = await ObtenerHistorialCambiosTurnoAsync(vm.EjecucionProduccionID, cn);
            vm.HistorialTurnos = ConstruirHistorialTurnos(vm.HorasCaptura, vm.HistorialCambiosTurno);

            vm.FechaHoraServidor = DateTime.Now;
            vm.TiempoExtraActivo = await ObtenerTiempoExtraActivoAsync(vm.EjecucionProduccionID, cn);
            vm.HistorialTiempoExtra = await ObtenerHistorialTiempoExtraAsync(vm.EjecucionProduccionID, cn);
            vm.PuedeIniciarTiempoExtra = vm.TiempoExtraActivo == null && await PuedeIniciarTiempoExtraAsync(vm.EjecucionProduccionID, cn);

            var primeraPendiente = vm.HorasCaptura.Where(x => !x.Capturada).OrderBy(x => x.NumeroHora).FirstOrDefault();
            if (primeraPendiente != null)
            {
                vm.FechaProduccion = primeraPendiente.FechaProduccion;
                vm.HoraInicioSugerida = primeraPendiente.HoraInicio;
                vm.HoraFinSugerida = primeraPendiente.HoraFin;
            }

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> IniciarTiempoExtra(ProduccionTiempoExtraIniciarPostVm vm)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            if (vm.EjecucionProduccionID <= 0)
            {
                TempData["Error"] = "No se recibió una ejecución de Producción válida.";
                return RedirectToAction(nameof(Index));
            }

            var motivo = NormalizarMotivoTiempoExtra(vm.Motivo);
            if (motivo == null)
            {
                TempData["Error"] = "Selecciona un motivo válido para iniciar el tiempo extra.";
                return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
            }

            vm.Observaciones = vm.Observaciones?.Trim();
            if (motivo == ProduccionTiempoExtraMotivo.Otro && string.IsNullOrWhiteSpace(vm.Observaciones))
            {
                TempData["Error"] = "Cuando seleccionas Otro debes indicar el motivo en observaciones.";
                return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
            }

            if (!string.IsNullOrWhiteSpace(vm.Observaciones) && vm.Observaciones.Length > 500)
            {
                TempData["Error"] = "Las observaciones no pueden superar 500 caracteres.";
                return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var usuarioId = ObtenerUsuarioID();
            if (!await UsuarioEsOperadorAsync(usuarioId, cn)) return AccesoDenegadoOperador();

            var personaId = await ObtenerPersonaIDUsuarioAsync(usuarioId, cn);
            if (!personaId.HasValue || personaId.Value <= 0) return AccesoDenegadoOperador();

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                var ejecucion = await ObtenerEjecucionOperadorAsync(vm.EjecucionProduccionID, cn, tx);
                if (ejecucion == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                if (!await PersonaAsignadaAEjecucionAsync(vm.EjecucionProduccionID, personaId.Value, cn, tx))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La ejecución ya no se encuentra asignada al operador conectado.";
                    return RedirectToAction(nameof(Index));
                }

                if (ejecucion.EstatusID != ProduccionEstatus.EnProduccion)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Solo puedes iniciar tiempo extra mientras la corrida se encuentre en Producción.";
                    return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
                }

                if (ejecucion.FechaLiberacionMaquina.HasValue)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La máquina ya fue liberada. No se puede iniciar tiempo extra.";
                    return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
                }

                if (await TieneParoAbiertoAsync(vm.EjecucionProduccionID, cn, tx))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Existe un paro abierto. Finalízalo antes de iniciar tiempo extra.";
                    return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
                }

                var configuracionActual = await ObtenerConfiguracionActualOperadorAsync(vm.EjecucionProduccionID, cn, tx);
                if (configuracionActual == null || !configuracionActual.EstaVigente)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La corrida no tiene una configuración técnica vigente.";
                    return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
                }

                var sesionActual = await ObtenerTiempoExtraActivoAsync(vm.EjecucionProduccionID, cn, tx, true);
                if (sesionActual != null)
                {
                    await tx.RollbackAsync();
                    TempData["Info"] = $"Ya existe una sesión de tiempo extra en curso desde las {sesionActual.FechaHoraInicio:HH:mm}.";
                    return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
                }

                if (!await PuedeIniciarTiempoExtraAsync(vm.EjecucionProduccionID, cn, tx))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Todavía no se ha completado el tiempo normal planeado de esta corrida. Captura primero los bloques normales pendientes.";
                    return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
                }

                var ultimoContador = await ObtenerUltimaLecturaContadorMaquinaAsync(vm.EjecucionProduccionID, cn, tx);
                if (!ultimoContador.HasValue)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "No existe una lectura previa del contador de máquina. No se puede establecer la base del tiempo extra.";
                    return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
                }

                var ahora = DateTime.Now;

                const string sql = @"
INSERT INTO dbo.Produccion_TiempoExtra
(
    EjecucionProduccionID,
    OperadorInicioID,
    OperadorFinID,
    ConfiguracionCorridaInicioID,
    FechaHoraInicio,
    FechaHoraUltimoCorte,
    FechaHoraFin,
    ContadorInicio,
    ContadorUltimoCorte,
    ContadorFin,
    Estado,
    Motivo,
    Observaciones,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
OUTPUT INSERTED.TiempoExtraID
VALUES
(
    @EjecucionProduccionID,
    @OperadorInicioID,
    NULL,
    @ConfiguracionCorridaInicioID,
    @Ahora,
    @Ahora,
    NULL,
    @ContadorInicio,
    @ContadorInicio,
    NULL,
    @Estado,
    @Motivo,
    @Observaciones,
    @UsuarioID,
    @Ahora,
    1
);";

                await using var cmd = new SqlCommand(sql, cn, tx);
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = vm.EjecucionProduccionID;
                cmd.Parameters.Add("@OperadorInicioID", SqlDbType.Int).Value = personaId.Value;
                cmd.Parameters.Add("@ConfiguracionCorridaInicioID", SqlDbType.Int).Value = configuracionActual.ConfiguracionCorridaID;
                cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
                cmd.Parameters.Add("@ContadorInicio", SqlDbType.BigInt).Value = ultimoContador.Value;
                cmd.Parameters.Add("@Estado", SqlDbType.NVarChar, 20).Value = ProduccionTiempoExtraEstado.EnCurso;
                cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 100).Value = motivo;
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(vm.Observaciones) ? DBNull.Value : vm.Observaciones;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

                var resultado = await cmd.ExecuteScalarAsync();
                if (resultado == null || resultado == DBNull.Value) throw new InvalidOperationException("No fue posible crear la sesión de tiempo extra.");

                var tiempoExtraId = Convert.ToInt32(resultado);
                await tx.CommitAsync();

                TempData["Success"] = $"Tiempo extra iniciado. Sesión #{tiempoExtraId}. Contador base: {ultimoContador.Value:N0}.";
                return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                try { await tx.RollbackAsync(); } catch { }
                TempData["Info"] = "Ya existe una sesión de tiempo extra abierta para esta corrida.";
                return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                TempData["Error"] = "No fue posible iniciar el tiempo extra: " + ex.Message;
                return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
            }
        }

        private async Task<ProduccionTiempoExtraVm?> ObtenerTiempoExtraActivoAsync(int ejecucionProduccionId, SqlConnection cn, SqlTransaction? tx = null, bool bloquear = false)
        {
            if (ejecucionProduccionId <= 0) return null;

            var hint = bloquear && tx != null ? " WITH(UPDLOCK,HOLDLOCK)" : string.Empty;
            var sql = $@"
SELECT TOP(1)
    te.TiempoExtraID,
    te.EjecucionProduccionID,
    te.OperadorInicioID,
    LTRIM(RTRIM(CONCAT(ISNULL(pi.Nombre,N''),N' ',ISNULL(pi.ApellidoPaterno,N''),N' ',ISNULL(pi.ApellidoMaterno,N'')))) AS OperadorInicioNombre,
    te.OperadorFinID,
    LTRIM(RTRIM(CONCAT(ISNULL(pf.Nombre,N''),N' ',ISNULL(pf.ApellidoPaterno,N''),N' ',ISNULL(pf.ApellidoMaterno,N'')))) AS OperadorFinNombre,
    te.ConfiguracionCorridaInicioID,
    te.FechaHoraInicio,
    te.FechaHoraUltimoCorte,
    te.FechaHoraFin,
    te.ContadorInicio,
    te.ContadorUltimoCorte,
    te.ContadorFin,
    te.Estado,
    te.Motivo,
    te.Observaciones,
    te.UsuarioCreacionID,
    te.FechaCreacion,
    te.UsuarioModificacionID,
    te.FechaModificacion,
    te.UsuarioCancelacionID,
    te.FechaCancelacion,
    te.MotivoCancelacion,
    te.Activo
FROM dbo.Produccion_TiempoExtra te{hint}
LEFT JOIN dbo.Persona pi ON pi.PersonaID=te.OperadorInicioID
LEFT JOIN dbo.Persona pf ON pf.PersonaID=te.OperadorFinID
WHERE te.EjecucionProduccionID=@EjecucionProduccionID
  AND te.Activo=1
  AND te.FechaHoraFin IS NULL
  AND UPPER(LTRIM(RTRIM(te.Estado))) IN(N'EN_CURSO',N'PAUSADO')
ORDER BY te.TiempoExtraID DESC;";

            ProduccionTiempoExtraVm? vm = null;

            await using (var cmd = tx == null ? new SqlCommand(sql, cn) : new SqlCommand(sql, cn, tx))
            {
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                await using var rd = await cmd.ExecuteReaderAsync();
                if (await rd.ReadAsync()) vm = MapearTiempoExtra(rd);
            }

            if (vm == null) return null;

            vm.Cortes = await ObtenerCortesTiempoExtraAsync(vm.TiempoExtraID, cn, tx);
            return vm;
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PrevisualizarProduccionHora(int ejecucionProduccionId, DateTime fechaProduccion, string horaInicio, string horaFin, long? contadorMaquinaActual, int cantidadSospechosa = 0, int cantidadScrap = 0)
        {
            if (!UsuarioEnSesion())
                return Unauthorized(new { ok = false, mensaje = "La sesión ha expirado." });
            if (ejecucionProduccionId <= 0)
                return Json(new { ok = false, mensaje = "La ejecución de Producción no es válida." });
            if (!contadorMaquinaActual.HasValue)
                return Json(new { ok = false, mensaje = "Captura el contador actual de la máquina." });
            if (contadorMaquinaActual.Value < 0)
                return Json(new { ok = false, mensaje = "El contador de la máquina no puede ser negativo." });
            if (cantidadSospechosa < 0 || cantidadScrap < 0)
                return Json(new { ok = false, mensaje = "Sospechosos y scrap no pueden ser negativos." });
            if (!TimeSpan.TryParse(horaInicio, out var horaInicioEnviada) || !TimeSpan.TryParse(horaFin, out var horaFinEnviada))
                return Json(new { ok = false, mensaje = "El rango de hora no es válido." });

            var horaInicioNormalizada = new TimeSpan(horaInicioEnviada.Hours, horaInicioEnviada.Minutes, 0);
            var horaFinNormalizada = new TimeSpan(horaFinEnviada.Hours, horaFinEnviada.Minutes, 0);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var usuarioId = ObtenerUsuarioID();

            if (!await UsuarioEsOperadorAsync(usuarioId, cn))
                return Unauthorized(new { ok = false, mensaje = "El usuario no tiene permisos de operador." });

            var personaId = await ObtenerPersonaIDUsuarioAsync(usuarioId, cn);
            if (!personaId.HasValue || personaId.Value <= 0)
                return Unauthorized(new { ok = false, mensaje = "No fue posible identificar al operador." });

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                var ejecucion = await ObtenerEjecucionOperadorAsync(ejecucionProduccionId, cn, tx);
                if (ejecucion == null)
                {
                    await tx.RollbackAsync();
                    return Json(new { ok = false, mensaje = "No se encontró la ejecución de Producción." });
                }

                if (!await PersonaAsignadaAEjecucionAsync(ejecucionProduccionId, personaId.Value, cn, tx))
                {
                    await tx.RollbackAsync();
                    return Json(new { ok = false, mensaje = "La ejecución ya no está asignada al operador conectado." });
                }

                if (ejecucion.EstatusID != ProduccionEstatus.EnProduccion)
                {
                    await tx.RollbackAsync();
                    return Json(new { ok = false, mensaje = "La corrida no se encuentra en Producción." });
                }

                if (ejecucion.FechaLiberacionMaquina.HasValue)
                {
                    await tx.RollbackAsync();
                    return Json(new { ok = false, mensaje = "La máquina ya fue liberada. No pueden registrarse nuevas horas." });
                }

                if (await TieneParoAbiertoAsync(ejecucionProduccionId, cn, tx))
                {
                    await tx.RollbackAsync();
                    return Json(new { ok = false, mensaje = "Existe un paro abierto. Finalízalo antes de capturar producción." });
                }

                var configuracionActual = await ObtenerConfiguracionActualOperadorAsync(ejecucionProduccionId, cn, tx);
                if (configuracionActual == null)
                {
                    await tx.RollbackAsync();
                    return Json(new { ok = false, mensaje = "La corrida no tiene configuración técnica vigente." });
                }

                var filas = await ObtenerFilasCapturaHoraAsync(ejecucionProduccionId, ejecucion.ProgramaProduccionID, cn, tx);
                var fila = filas.FirstOrDefault(x =>
                    x.FechaProduccion.Date == fechaProduccion.Date &&
                    x.HoraInicio.Hours == horaInicioNormalizada.Hours &&
                    x.HoraInicio.Minutes == horaInicioNormalizada.Minutes &&
                    x.HoraFin.Hours == horaFinNormalizada.Hours &&
                    x.HoraFin.Minutes == horaFinNormalizada.Minutes);

                if (fila == null)
                {
                    await tx.RollbackAsync();
                    return Json(new { ok = false, mensaje = "El bloque solicitado ya no corresponde a la captura disponible." });
                }

                if (fila.Capturada)
                {
                    await tx.RollbackAsync();
                    return Json(new { ok = false, mensaje = "Este bloque ya fue capturado." });
                }

                if (!fila.Disponible)
                {
                    await tx.RollbackAsync();
                    return Json(new { ok = false, mensaje = "Este bloque todavía no está disponible para captura." });
                }

                var fechaInicioFila = fila.FechaProduccion.Date.Add(fila.HoraInicio);
                var fechaFinFila = fila.FechaProduccion.Date.Add(fila.HoraFin);
                if (fechaFinFila <= fechaInicioFila)
                    fechaFinFila = fechaFinFila.AddDays(1);

                if (DateTime.Now < fechaFinFila)
                {
                    await tx.RollbackAsync();
                    return Json(new { ok = false, mensaje = $"El bloque todavía no termina. Podrás capturarlo a partir de las {fechaFinFila:HH:mm}." });
                }

                var calculo = await CalcularProduccionContadorHoraAsync(ejecucionProduccionId, fechaInicioFila, fechaFinFila, contadorMaquinaActual.Value, cn, tx);
                var piezasNoOk = (long)cantidadSospechosa + cantidadScrap;

                if (piezasNoOk > calculo.PiezasCalculadas)
                {
                    await tx.RollbackAsync();
                    return Json(new
                    {
                        ok = false,
                        mensaje = $"Sospechosos + Scrap suman {piezasNoOk:N0}, pero el contador solamente indica {calculo.PiezasCalculadas:N0} pieza(s) físicas.",
                        piezasFisicas = calculo.PiezasCalculadas,
                        cantidadOK = 0
                    });
                }

                var cantidadOK = calculo.PiezasCalculadas - Convert.ToInt32(piezasNoOk);
                decimal? porcentajeCumplimiento = null;
                int? diferenciaObjetivo = null;
                bool? cumplioObjetivo = null;

                if (calculo.ObjetivoBloque > 0)
                {
                    diferenciaObjetivo = cantidadOK - calculo.ObjetivoBloque;
                    cumplioObjetivo = cantidadOK >= calculo.ObjetivoBloque;
                    porcentajeCumplimiento = Math.Round((decimal)cantidadOK * 100m / calculo.ObjetivoBloque, 2);
                }

                await tx.RollbackAsync();

                return Json(new
                {
                    ok = true,
                    contadorInicial = calculo.ContadorInicialReferencia,
                    contadorFinal = contadorMaquinaActual.Value,
                    piezasFisicas = calculo.PiezasCalculadas,
                    cantidadOK,
                    cantidadSospechosa,
                    cantidadScrap,
                    objetivoHora = calculo.ObjetivoHora,
                    objetivoBloque = calculo.ObjetivoBloque,
                    porcentajeCumplimiento,
                    diferenciaObjetivo,
                    cumplioObjetivo,
                    minutosProductivos = calculo.MinutosProductivos,
                    tieneCambioConfiguracion = calculo.TieneCambioConfiguracion,
                    tieneReinicioContador = calculo.TieneReinicioContador,
                    numeroSegmentos = calculo.Segmentos.Count
                });
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                return Json(new { ok = false, mensaje = "No fue posible calcular la producción del bloque: " + ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarHora(ProduccionRegistroHoraPostVm vm)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            if (vm.EjecucionProduccionID <= 0)
            {
                TempData["Error"] = "No se recibió la ejecución de producción.";
                return RedirectToAction(nameof(Index));
            }

            if (!TimeSpan.TryParse(vm.HoraInicio, out var horaInicioEnviada) || !TimeSpan.TryParse(vm.HoraFin, out var horaFinEnviada))
            {
                TempData["Error"] = "El rango de hora no es válido.";
                return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
            }

            var horaInicio = new TimeSpan(horaInicioEnviada.Hours, horaInicioEnviada.Minutes, 0);
            var horaFin = new TimeSpan(horaFinEnviada.Hours, horaFinEnviada.Minutes, 0);
            var fechaInicioSolicitada = vm.FechaProduccion.Date.Add(horaInicio);
            var fechaFinSolicitada = vm.FechaProduccion.Date.Add(horaFin);

            if (fechaFinSolicitada <= fechaInicioSolicitada)
                fechaFinSolicitada = fechaFinSolicitada.AddDays(1);

            if (DateTime.Now < fechaFinSolicitada)
            {
                TempData["Error"] = $"La hora todavía no ha terminado. Podrás capturar este bloque a partir de {fechaFinSolicitada:HH:mm}.";
                return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
            }

            if (!vm.ContadorMaquinaActual.HasValue)
            {
                TempData["Error"] = "Captura el contador de la máquina correspondiente al cierre de este bloque.";
                return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
            }

            if (vm.ContadorMaquinaActual.Value < 0)
            {
                TempData["Error"] = "El contador de la máquina no puede ser negativo.";
                return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
            }

            if (vm.CantidadSospechosa < 0 || vm.CantidadScrap < 0)
            {
                TempData["Error"] = "Las cantidades de sospechosos y scrap no pueden ser negativas.";
                return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var usuarioId = ObtenerUsuarioID();

            if (!await UsuarioEsOperadorAsync(usuarioId, cn))
                return AccesoDenegadoOperador();

            var personaId = await ObtenerPersonaIDUsuarioAsync(usuarioId, cn);
            if (!personaId.HasValue || personaId.Value <= 0)
                return AccesoDenegadoOperador();

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                var ejecucion = await ObtenerEjecucionOperadorAsync(vm.EjecucionProduccionID, cn, tx);
                if (ejecucion == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                if (!await PersonaAsignadaAEjecucionAsync(vm.EjecucionProduccionID, personaId.Value, cn, tx))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La ejecución ya no se encuentra asignada al operador conectado.";
                    return RedirectToAction(nameof(Index));
                }

                if (ejecucion.EstatusID != ProduccionEstatus.EnProduccion)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Solo puedes capturar producción cuando la corrida se encuentra en serie.";
                    return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
                }

                if (ejecucion.FechaLiberacionMaquina.HasValue)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"La máquina fue liberada por el técnico el {ejecucion.FechaLiberacionMaquina.Value:dd/MM/yyyy HH:mm}. Ya no se pueden registrar nuevas horas.";
                    return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
                }

                if (await TieneParoAbiertoAsync(vm.EjecucionProduccionID, cn, tx))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "No puedes capturar producción mientras exista un paro abierto.";
                    return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
                }

                var configuracionActual = await ObtenerConfiguracionActualOperadorAsync(vm.EjecucionProduccionID, cn, tx);
                if (configuracionActual == null)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La corrida no tiene configuración técnica vigente. El Técnico de Producción debe confirmar cavidades y ciclo.";
                    return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
                }

                var filasPermitidas = await ObtenerFilasCapturaHoraAsync(ejecucion.EjecucionProduccionID, ejecucion.ProgramaProduccionID, cn, tx);
                var filaSolicitada = filasPermitidas.FirstOrDefault(x =>
                    x.FechaProduccion.Date == vm.FechaProduccion.Date &&
                    x.HoraInicio.Hours == horaInicio.Hours &&
                    x.HoraInicio.Minutes == horaInicio.Minutes);

                if (filaSolicitada == null)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La hora enviada no pertenece a los bloques generados para esta producción.";
                    return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
                }

                if (filaSolicitada.Capturada)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Esta hora ya fue capturada.";
                    return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
                }

                if (!filaSolicitada.Disponible)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Debes capturar primero la hora pendiente anterior.";
                    return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
                }

                var fechaInicioFila = filaSolicitada.FechaProduccion.Date.Add(filaSolicitada.HoraInicio);
                var fechaFinFila = filaSolicitada.FechaProduccion.Date.Add(filaSolicitada.HoraFin);
                if (fechaFinFila <= fechaInicioFila)
                    fechaFinFila = fechaFinFila.AddDays(1);

                var diferenciaInicioSegundos = Math.Abs((fechaInicioFila - fechaInicioSolicitada).TotalSeconds);
                if (diferenciaInicioSegundos >= 60)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La hora inicial enviada no coincide con el bloque de producción.";
                    return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
                }

                var diferenciaFinSegundos = Math.Abs((fechaFinFila - fechaFinSolicitada).TotalSeconds);
                if (diferenciaFinSegundos >= 60)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "El rango enviado no coincide con el bloque de producción.";
                    return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
                }

                if (DateTime.Now < fechaFinFila)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"La hora todavía no ha terminado. Podrás capturar a partir de {fechaFinFila:HH:mm}.";
                    return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
                }

                var fechaProduccionReal = filaSolicitada.FechaProduccion.Date;
                var horaInicioReal = new TimeSpan(filaSolicitada.HoraInicio.Hours, filaSolicitada.HoraInicio.Minutes, 0);
                var horaFinReal = new TimeSpan(filaSolicitada.HoraFin.Hours, filaSolicitada.HoraFin.Minutes, 0);

                if (await ExisteRegistroHoraAsync(vm.EjecucionProduccionID, fechaProduccionReal, horaInicioReal, cn, tx))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La hora seleccionada ya fue capturada. Actualiza la pantalla.";
                    return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
                }

                var calculo = await CalcularProduccionContadorHoraAsync(vm.EjecucionProduccionID, fechaInicioFila, fechaFinFila, vm.ContadorMaquinaActual.Value, cn, tx);
                var piezasNoOk = (long)vm.CantidadSospechosa + vm.CantidadScrap;

                if (piezasNoOk > calculo.PiezasCalculadas)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"El contador indica {calculo.PiezasCalculadas:N0} pieza(s) físicas, pero capturaste {piezasNoOk:N0} entre sospechosos y scrap. Esas cantidades no pueden superar la producción física.";
                    return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
                }

                // El navegador NO decide las piezas OK.
                // El servidor las calcula siempre con la producción física.
                vm.CantidadOK = calculo.PiezasCalculadas - Convert.ToInt32(piezasNoOk);

                if (calculo.PiezasCalculadas == 0 && string.IsNullOrWhiteSpace(vm.Observaciones))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "El contador no registró producción durante este bloque. Indica en observaciones qué ocurrió.";
                    return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
                }

                vm.FechaProduccion = fechaProduccionReal;
                vm.HoraInicio = horaInicioReal.ToString(@"hh\:mm");
                vm.HoraFin = horaFinReal.ToString(@"hh\:mm");

                var registroHoraId = await InsertarRegistroHoraAsync(ejecucion, vm, horaInicioReal, horaFinReal, personaId.Value, usuarioId, calculo, cn, tx);

                await InsertarSegmentosRegistroHoraAsync(registroHoraId, ejecucion.EjecucionProduccionID, calculo, usuarioId, cn, tx);
                await RegistrarLecturaContadorHoraAsync(ejecucion, registroHoraId, personaId.Value, usuarioId, fechaFinFila, vm.ContadorMaquinaActual.Value, calculo, cn, tx);
                await RegistrarBonusProduccionHoraAsync(personaId.Value, ejecucion.EjecucionProduccionID, registroHoraId, vm.CantidadOK, calculo.PiezasCalculadas, usuarioId, cn, tx);
                await VincularRegistroHoraConCalidadAsync(ejecucion, vm, horaInicioReal, horaFinReal, registroHoraId, usuarioId, cn, tx);
                await RecalcularTotalesEjecucionAsync(vm.EjecucionProduccionID, usuarioId, cn, tx);

                await tx.CommitAsync();

                TempData["Success"] = ConstruirMensajeCumplimientoHora(
                    filaSolicitada.NumeroHora,
                    vm.CantidadOK,
                    vm.CantidadSospechosa,
                    vm.CantidadScrap,
                    vm.ContadorMaquinaActual.Value,
                    calculo);

                return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                TempData["Error"] = "No fue posible guardar la producción: " + ex.Message;
                return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
            }
        }

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
                if (ejecucion.FechaLiberacionMaquina.HasValue)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La máquina ya fue liberada por el técnico. No se pueden registrar nuevos paros sobre esta corrida.";
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
        public async Task<IActionResult> CerrarParo(
     ProduccionCerrarParoPostVm vm)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            await using var cn =
                new SqlConnection(ConnectionString);

            await cn.OpenAsync();

            var usuarioId = ObtenerUsuarioID();

            var esOperador =
                await UsuarioEsOperadorAsync(usuarioId, cn);

            if (!esOperador)
                return AccesoDenegadoOperador();

            await using var tx =
                (SqlTransaction)await cn.BeginTransactionAsync();

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
                    cmd.Parameters.Add(
                        "@ParoID",
                        SqlDbType.Int).Value =
                        vm.ParoID;

                    await using var rd =
                        await cmd.ExecuteReaderAsync();

                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();

                        TempData["Error"] =
                            "No se encontró un paro abierto para cerrar.";

                        return RedirectToAction(nameof(Index));
                    }

                    ejecucionProduccionId =
                        Convert.ToInt32(
                            rd["EjecucionProduccionID"]);

                    fechaInicioParo =
                        Convert.ToDateTime(
                            rd["FechaInicioParo"]);
                }

                var ahora = DateTime.Now;

                var duracionMinutos =
                    (int)Math.Max(
                        0,
                        Math.Round(
                            (ahora - fechaInicioParo)
                                .TotalMinutes));

                var esMayorA15Minutos =
                    duracionMinutos > 15;

                const string sqlCerrar = @"
UPDATE dbo.Produccion_Paros
SET
    FechaFinParo = @FechaFinParo,
    DuracionMinutos = @DuracionMinutos,
    EsMayorA15Minutos =
        CASE
            WHEN @DuracionMinutos > 15 THEN 1
            ELSE 0
        END,
    Descripcion =
        CASE
            WHEN @ObservacionesCierre IS NULL
              OR LTRIM(RTRIM(@ObservacionesCierre)) = N''
                THEN Descripcion
            WHEN Descripcion IS NULL
              OR LTRIM(RTRIM(Descripcion)) = N''
                THEN @ObservacionesCierre
            ELSE
                Descripcion
                + CHAR(13)
                + CHAR(10)
                + N'Cierre: '
                + @ObservacionesCierre
        END,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE ParoID = @ParoID
  AND Activo = 1;";

                await using (var cmd = new SqlCommand(sqlCerrar, cn, tx))
                {
                    cmd.Parameters.Add(
                        "@ParoID",
                        SqlDbType.Int).Value =
                        vm.ParoID;

                    cmd.Parameters.Add(
                        "@FechaFinParo",
                        SqlDbType.DateTime).Value =
                        ahora;

                    cmd.Parameters.Add(
                        "@DuracionMinutos",
                        SqlDbType.Int).Value =
                        duracionMinutos;

                    cmd.Parameters.Add(
                        "@ObservacionesCierre",
                        SqlDbType.NVarChar,
                        500).Value =
                        string.IsNullOrWhiteSpace(
                            vm.ObservacionesCierre)
                            ? DBNull.Value
                            : vm.ObservacionesCierre.Trim();

                    cmd.Parameters.Add(
                        "@UsuarioID",
                        SqlDbType.Int).Value =
                        usuarioId;

                    await cmd.ExecuteNonQueryAsync();
                }

                var ejecucion =
                    await ObtenerEjecucionOperadorAsync(
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
                        "Paro cerrado. Duró más de 15 minutos, por lo que " +
                        "la producción regresó a preparación. Debe ejecutar " +
                        "nuevamente los 5 disparos de prueba y solicitar " +
                        "reliberación de Calidad.";

                    return RedirectToAction(
                        "Detalle",
                        "Produccion",
                        new { id = ejecucionProduccionId });
                }

                /*
                 * Paro corto: al cerrarlo ya conocemos toda la interrupción.
                 * Se recorre el fin programado por esos minutos.
                 */
                await DesplazarFinProgramadoParoCortoAsync(
                    ejecucion.ProgramaProduccionID,
                    ejecucionProduccionId,
                    duracionMinutos,
                    usuarioId,
                    cn,
                    tx);

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
                    "No fue posible cerrar el paro: " +
                    ex.Message;

                return RedirectToAction(nameof(Index));
            }
        }
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
        public async Task<IActionResult> FormarCaja(int ejecucionProduccionId, int cantidadPiezas, string tipoCaja, string? observaciones)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            var usuarioId = ObtenerUsuarioID();
            if (!await UsuarioEsOperadorAsync(usuarioId, cn)) return AccesoDenegadoOperador();
            if (ejecucionProduccionId <= 0) { TempData["Error"] = "No se recibió la ejecución de producción."; return RedirectToAction(nameof(Index)); }
            if (cantidadPiezas <= 0) { TempData["Error"] = "La cantidad de piezas debe ser mayor a cero."; return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId }); }
            var tipoNormalizado = NormalizarTipoCajaOperador(tipoCaja);
            if (string.IsNullOrWhiteSpace(tipoNormalizado)) { TempData["Error"] = "El tipo de caja no es válido."; return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId }); }
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var ejecucion = await ObtenerEjecucionOperadorAsync(ejecucionProduccionId, cn, tx);
                if (ejecucion == null) { await tx.RollbackAsync(); return NotFound(); }
                if (ejecucion.EstatusID != ProduccionEstatus.EnProduccion) { await tx.RollbackAsync(); TempData["Error"] = "Solo puedes formar cajas cuando la producción está en serie."; return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId }); }
                if (await TieneParoAbiertoAsync(ejecucionProduccionId, cn, tx)) { await tx.RollbackAsync(); TempData["Error"] = "No puedes formar cajas mientras exista un paro abierto."; return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId }); }
                decimal? piezasPorEmbalaje = null;
                decimal? cantidadEmbalajes = null;
                if (ejecucion.SolicitudProduccionDetalleID.HasValue && ejecucion.SolicitudProduccionDetalleID.Value > 0)
                {
                    const string sqlEmbalaje = @"
SELECT TOP(1) PiezasPorEmbalaje,CantidadEmbalajes
FROM dbo.SolicitudesProduccionDetalle WITH(UPDLOCK,HOLDLOCK)
WHERE SolicitudProduccionDetalleID=@SolicitudProduccionDetalleID AND Activo=1;";
                    await using var cmdEmbalaje = new SqlCommand(sqlEmbalaje, cn, tx);
                    cmdEmbalaje.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = ejecucion.SolicitudProduccionDetalleID.Value;
                    await using var rd = await cmdEmbalaje.ExecuteReaderAsync();
                    if (await rd.ReadAsync())
                    {
                        piezasPorEmbalaje = rd["PiezasPorEmbalaje"] == DBNull.Value ? null : Convert.ToDecimal(rd["PiezasPorEmbalaje"]);
                        cantidadEmbalajes = rd["CantidadEmbalajes"] == DBNull.Value ? null : Convert.ToDecimal(rd["CantidadEmbalajes"]);
                    }
                }
                var esIncompleta = tipoNormalizado == ProduccionCajaTipo.Incompleta;
                if ((tipoNormalizado == ProduccionCajaTipo.Ok || esIncompleta) && (!piezasPorEmbalaje.HasValue || piezasPorEmbalaje.Value <= 0)) { await tx.RollbackAsync(); TempData["Error"] = "La pieza no tiene configurada la capacidad de piezas por embalaje."; return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId }); }
                var capacidadCaja = piezasPorEmbalaje.HasValue ? Convert.ToInt32(Math.Floor(piezasPorEmbalaje.Value)) : 0;
                if (tipoNormalizado == ProduccionCajaTipo.Ok && capacidadCaja > 0 && cantidadPiezas > capacidadCaja) { await tx.RollbackAsync(); TempData["Error"] = $"La caja excede la capacidad del embalaje. Máximo permitido: {capacidadCaja:N0} pieza(s)."; return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId }); }
                if (esIncompleta)
                {
                    if (cantidadPiezas >= capacidadCaja) { await tx.RollbackAsync(); TempData["Error"] = $"Una caja incompleta debe contener menos de {capacidadCaja:N0} pieza(s). Si alcanza la capacidad debe formarse como caja OK."; return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId }); }
                    if (!ejecucion.CantidadPlaneada.HasValue || ejecucion.CantidadPlaneada.Value <= 0) { await tx.RollbackAsync(); TempData["Error"] = "La ejecución no tiene una cantidad planeada válida."; return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId }); }
                    var consumo = await ObtenerConsumoCajasEjecucionAsync(ejecucionProduccionId, cn, tx);
                    if (consumo.Ok < ejecucion.CantidadPlaneada.Value) { await tx.RollbackAsync(); TempData["Error"] = $"Todavía faltan piezas planeadas por empacar. Planeado: {ejecucion.CantidadPlaneada.Value:N0}; aplicado a cajas: {consumo.Ok:N0}. La etiqueta blanca solo se usa para sobreproducción."; return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId }); }
                    var excedenteProducido = Math.Max(0, ejecucion.CantidadOKTotal - ejecucion.CantidadPlaneada.Value);
                    if (excedenteProducido <= 0) { await tx.RollbackAsync(); TempData["Error"] = "No existe sobreproducción OK disponible para formar una caja incompleta."; return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId }); }
                }
                var tipoDisponibilidad = esIncompleta ? ProduccionCajaTipo.Ok : tipoNormalizado;
                var capturadoDisponible = await ObtenerCantidadDisponibleParaCajaAsync(ejecucionProduccionId, tipoDisponibilidad, cn, tx);
                if (cantidadPiezas > capturadoDisponible) { await tx.RollbackAsync(); TempData["Error"] = "No puedes formar la caja porque la cantidad excede lo capturado disponible. Disponible: " + capturadoDisponible.ToString("N0") + " pieza(s)."; return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId }); }
                if (tipoNormalizado == ProduccionCajaTipo.Ok)
                {
                    const string sqlTotales = @"
SELECT COUNT(1) AS CajasFormadas,ISNULL(SUM(ISNULL(CantidadPiezas,ISNULL(Cantidad,0))),0) AS PiezasEnCajas
FROM dbo.Produccion_Cajas c WITH(UPDLOCK,HOLDLOCK)
WHERE c.EjecucionProduccionID=@EjecucionProduccionID AND c.Activo=1
AND UPPER(LTRIM(RTRIM(ISNULL(c.TipoCaja,N'OK'))))=N'OK'
AND NOT EXISTS(SELECT 1 FROM dbo.Produccion_CajaOrigenDetalle od WHERE od.CajaProduccionID=c.CajaProduccionID AND od.Activo=1);";
                    int cajasFormadas;
                    int piezasEnCajas;
                    await using (var cmdTotales = new SqlCommand(sqlTotales, cn, tx))
                    {
                        cmdTotales.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                        await using var rd = await cmdTotales.ExecuteReaderAsync();
                        await rd.ReadAsync();
                        cajasFormadas = Convert.ToInt32(rd["CajasFormadas"]);
                        piezasEnCajas = Convert.ToInt32(rd["PiezasEnCajas"]);
                    }
                    var detalleAplicado = await ObtenerCantidadDetalleCajaPorEjecucionAsync(ejecucionProduccionId, cn, tx);
                    var totalAplicado = piezasEnCajas + detalleAplicado;
                    if (ejecucion.CantidadPlaneada.HasValue && ejecucion.CantidadPlaneada.Value > 0 && totalAplicado + cantidadPiezas > ejecucion.CantidadPlaneada.Value) { await tx.RollbackAsync(); TempData["Error"] = $"La caja excedería la cantidad planeada. Planeado: {ejecucion.CantidadPlaneada.Value:N0}; actualmente aplicado a cajas: {totalAplicado:N0}."; return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId }); }
                    if (cantidadEmbalajes.HasValue && cantidadEmbalajes.Value > 0)
                    {
                        var cajasEsperadas = Convert.ToInt32(Math.Ceiling(cantidadEmbalajes.Value));
                        if (cajasFormadas >= cajasEsperadas) { await tx.RollbackAsync(); TempData["Error"] = $"Ya se formaron las {cajasEsperadas:N0} caja(s)/embalaje(s) normales esperadas para esta orden."; return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId }); }
                    }
                }
                var siguienteNumero = await ObtenerSiguienteNumeroCajaAsync(ejecucionProduccionId, cn, tx);
                var folioCaja = CrearFolioCajaOperador(ejecucion, siguienteNumero);
                const string sqlInsert = @"
INSERT INTO dbo.Produccion_Cajas
(
    EjecucionProduccionID,ProgramaProduccionID,SolicitudProduccionID,SolicitudProduccionDetalleID,ReleaseID,ReleaseDetalleID,
    NumeroCaja,FolioCaja,CantidadPiezas,TipoCaja,LoteMaterial,EtiquetaFolio,EstadoCajaID,EstadoCajaNombre,EtiquetaVerde,
    FechaFormacion,UsuarioFormacionID,Observaciones,Activo,UsuarioCreacionID,FechaCreacion,Etiqueta,Cantidad,EstatusCalidad,
    OperadorUsuarioID,EsProductoIncompleto,EstadoProductoIncompleto,CapacidadObjetivoCaja,CantidadPendienteCompletar,EtiquetaBlanca
)
OUTPUT INSERTED.CajaProduccionID
VALUES
(
    @EjecucionProduccionID,@ProgramaProduccionID,@SolicitudProduccionID,@SolicitudProduccionDetalleID,@ReleaseID,@ReleaseDetalleID,
    @NumeroCaja,@FolioCaja,@CantidadPiezas,@TipoCaja,NULL,NULL,@EstadoCajaID,@EstadoCajaNombre,0,
    GETDATE(),@UsuarioID,@Observaciones,1,@UsuarioID,GETDATE(),@EtiquetaCompatibilidad,@CantidadPiezas,@EstatusCalidad,
    @UsuarioID,@EsProductoIncompleto,@EstadoProductoIncompleto,@CapacidadObjetivoCaja,@CantidadPendienteCompletar,NULL
);";
                long cajaProduccionId;
                await using (var cmd = new SqlCommand(sqlInsert, cn, tx))
                {
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucion.EjecucionProduccionID;
                    cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = ejecucion.ProgramaProduccionID;
                    cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = (object?)ejecucion.SolicitudProduccionID ?? DBNull.Value;
                    cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = (object?)ejecucion.SolicitudProduccionDetalleID ?? DBNull.Value;
                    cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = (object?)ejecucion.ReleaseID ?? DBNull.Value;
                    cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = (object?)ejecucion.ReleaseDetalleID ?? DBNull.Value;
                    cmd.Parameters.Add("@NumeroCaja", SqlDbType.Int).Value = siguienteNumero;
                    cmd.Parameters.Add("@FolioCaja", SqlDbType.NVarChar, 100).Value = folioCaja;
                    cmd.Parameters.Add("@CantidadPiezas", SqlDbType.Int).Value = cantidadPiezas;
                    cmd.Parameters.Add("@TipoCaja", SqlDbType.NVarChar, 30).Value = tipoNormalizado;
                    cmd.Parameters.Add("@EstadoCajaID", SqlDbType.Int).Value = ProduccionCajaEstatus.FormadaProduccion;
                    cmd.Parameters.Add("@EstadoCajaNombre", SqlDbType.NVarChar, 100).Value = ProduccionCajaEstatus.Nombre(ProduccionCajaEstatus.FormadaProduccion);
                    cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(observaciones) ? DBNull.Value : observaciones.Trim();
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@EtiquetaCompatibilidad", SqlDbType.NVarChar, 100).Value = folioCaja;
                    cmd.Parameters.Add(
     "@EstatusCalidad",
     SqlDbType.NVarChar,
     50).Value =
     "FORMADA";
                    cmd.Parameters.Add("@EsProductoIncompleto", SqlDbType.Bit).Value = esIncompleta;
                    cmd.Parameters.Add("@EstadoProductoIncompleto", SqlDbType.NVarChar, 30).Value = esIncompleta ? ProduccionProductoIncompletoEstado.Disponible : DBNull.Value;
                    cmd.Parameters.Add("@CapacidadObjetivoCaja", SqlDbType.Int).Value = esIncompleta ? capacidadCaja : DBNull.Value;
                    cmd.Parameters.Add("@CantidadPendienteCompletar", SqlDbType.Int).Value = esIncompleta ? capacidadCaja - cantidadPiezas : DBNull.Value;
                    cajaProduccionId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                }
                if (esIncompleta)
                {
                    var etiquetaBlanca = $"BLA-{cajaProduccionId:000000}";
                    const string sqlBlanca = @"
UPDATE dbo.Produccion_Cajas
SET EtiquetaBlanca=@EtiquetaBlanca,EtiquetaFolio=@EtiquetaBlanca,Etiqueta=@EtiquetaBlanca,UsuarioModificacionID=@UsuarioID,FechaModificacion=GETDATE()
WHERE CajaProduccionID=@CajaProduccionID AND Activo=1;
INSERT INTO dbo.Produccion_CajaOrigenDetalle
(CajaProduccionID,EjecucionProduccionID,ProgramaProduccionID,SolicitudProduccionID,SolicitudProduccionDetalleID,ReleaseID,ReleaseDetalleID,CantidadPiezas,TipoMovimiento,Observaciones,UsuarioCreacionID,FechaCreacion,Activo)
VALUES
(@CajaProduccionID,@EjecucionProduccionID,@ProgramaProduccionID,@SolicitudProduccionID,@SolicitudProduccionDetalleID,@ReleaseID,@ReleaseDetalleID,@CantidadPiezas,N'ORIGEN',N'Sobreproducción resguardada como producto incompleto con etiqueta blanca.',@UsuarioID,SYSDATETIME(),1);";
                    await using var cmdBlanca = new SqlCommand(sqlBlanca, cn, tx);
                    cmdBlanca.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = cajaProduccionId;
                    cmdBlanca.Parameters.Add("@EtiquetaBlanca", SqlDbType.NVarChar, 100).Value = etiquetaBlanca;
                    cmdBlanca.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucion.EjecucionProduccionID;
                    cmdBlanca.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = ejecucion.ProgramaProduccionID;
                    cmdBlanca.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = (object?)ejecucion.SolicitudProduccionID ?? DBNull.Value;
                    cmdBlanca.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = (object?)ejecucion.SolicitudProduccionDetalleID ?? DBNull.Value;
                    cmdBlanca.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = (object?)ejecucion.ReleaseID ?? DBNull.Value;
                    cmdBlanca.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = (object?)ejecucion.ReleaseDetalleID ?? DBNull.Value;
                    cmdBlanca.Parameters.Add("@CantidadPiezas", SqlDbType.Int).Value = cantidadPiezas;
                    cmdBlanca.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    await cmdBlanca.ExecuteNonQueryAsync();
                    await tx.CommitAsync();
                    TempData["Success"] = $"Producto incompleto {etiquetaBlanca} registrado con {cantidadPiezas:N0} pieza(s). Faltan {capacidadCaja - cantidadPiezas:N0} para completar la caja.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }
                await tx.CommitAsync();
                TempData["Success"] = $"Caja {siguienteNumero:N0} formada correctamente con {cantidadPiezas:N0} pieza(s).";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible formar la caja: " + ex.Message;
            }
            return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EscanearCaja(ProduccionEscanearCajaPostVm vm)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            if (vm.EjecucionProduccionID <= 0)
            {
                TempData["Error"] = "No se recibió una ejecución de Producción válida.";
                return RedirectToAction(nameof(Index));
            }
            vm.CodigoBarras = vm.CodigoBarras?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(vm.CodigoBarras))
            {
                TempData["Error"] = "Escanea una etiqueta física.";
                return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
            }
            if (!AlmacenPTCodigoBarrasService.TryParse(vm.CodigoBarras, out var parseado, out var error) || parseado == null)
            {
                TempData["Error"] = string.IsNullOrWhiteSpace(error) ? "No fue posible interpretar la etiqueta escaneada." : error;
                return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
            }
            if (parseado.Cantidad <= 0)
            {
                TempData["Error"] = "La etiqueta no contiene una cantidad válida.";
                return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
            }
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            var usuarioId = ObtenerUsuarioID();
            if (!await UsuarioEsOperadorAsync(usuarioId, cn)) return AccesoDenegadoOperador();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var contexto = await ObtenerContextoEscaneoCajaAsync(vm.EjecucionProduccionID, cn, tx);
                if (contexto == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }
                if (contexto.EstatusID != ProduccionEstatus.EnProduccion)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Solo puedes registrar cajas cuando la corrida se encuentra en Producción.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                if (await TieneParoAbiertoAsync(vm.EjecucionProduccionID, cn, tx))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "No puedes registrar cajas mientras exista un paro abierto.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                var codigoFisico = parseado.CodigoOriginal?.Trim();
                if (string.IsNullOrWhiteSpace(codigoFisico)) codigoFisico = vm.CodigoBarras;
                if (codigoFisico.Length > 500)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "El código de barras excede la longitud permitida.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                if (await ExisteCodigoBarrasCajaAsync(codigoFisico, cn, tx))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Esta etiqueta ya fue escaneada anteriormente. No se generó una caja duplicada.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                var ofEsperada = NormalizarValorEscaneo(contexto.NumeroOF);
                var ofEscaneada = NormalizarValorEscaneo(parseado.NumeroOF);
                if (string.IsNullOrWhiteSpace(ofEsperada))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La corrida no tiene una Orden de Fabricación válida para comparar contra la etiqueta.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                if (string.IsNullOrWhiteSpace(ofEscaneada) || !string.Equals(ofEsperada, ofEscaneada, StringComparison.OrdinalIgnoreCase))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"La etiqueta no corresponde a la OF actual. Esperada: {contexto.NumeroOF}. Escaneada: {parseado.NumeroOF}.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                var parteEscaneada = NormalizarValorEscaneo(parseado.NumeroParte);
                var numeroParteEsperado = NormalizarValorEscaneo(contexto.NumeroParte);
                var referenciaEsperada = NormalizarValorEscaneo(contexto.ReferenciaSAP);
                var parteCoincide = !string.IsNullOrWhiteSpace(parteEscaneada) && ((!string.IsNullOrWhiteSpace(numeroParteEsperado) && parteEscaneada == numeroParteEsperado) || (!string.IsNullOrWhiteSpace(referenciaEsperada) && parteEscaneada == referenciaEsperada));
                if (!parteCoincide)
                {
                    await tx.RollbackAsync();
                    var parteMostrar = !string.IsNullOrWhiteSpace(contexto.ReferenciaSAP) ? contexto.ReferenciaSAP : contexto.NumeroParte;
                    TempData["Error"] = $"La etiqueta pertenece a otro número de parte. Esperado: {parteMostrar}. Escaneado: {parseado.NumeroParte}.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                if (!contexto.PiezasPorEmbalaje.HasValue || contexto.PiezasPorEmbalaje.Value <= 0)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La pieza no tiene configurada la capacidad de piezas por embalaje.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                var capacidadCaja = Convert.ToInt32(Math.Floor(contexto.PiezasPorEmbalaje.Value));
                if (capacidadCaja <= 0)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La capacidad configurada del embalaje no es válida.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                if (parseado.Cantidad > capacidadCaja)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"La etiqueta indica {parseado.Cantidad:N0} pieza(s), pero el embalaje permite como máximo {capacidadCaja:N0}.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                if (contexto.CantidadPlaneada <= 0)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La ejecución no tiene una cantidad planeada válida.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                var consumo = await ObtenerConsumoCajasEjecucionAsync(vm.EjecucionProduccionID, cn, tx);
                var planeadoPendiente = Math.Max(0, contexto.CantidadPlaneada - consumo.Ok);
                if (planeadoPendiente <= 0)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La cantidad planeada ya se encuentra completamente aplicada a cajas. La sobreproducción debe manejarse mediante el flujo de producto incompleto/etiqueta blanca.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                var cantidadEsperadaCaja = Math.Min(capacidadCaja, planeadoPendiente);
                if (parseado.Cantidad != cantidadEsperadaCaja)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"La cantidad de la etiqueta no corresponde a la caja que debe formarse. Esperada: {cantidadEsperadaCaja:N0} pieza(s). Etiqueta: {parseado.Cantidad:N0} pieza(s).";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                var capturadoDisponible = await ObtenerCantidadDisponibleParaCajaAsync(vm.EjecucionProduccionID, ProduccionCajaTipo.Ok, cn, tx);
                if (parseado.Cantidad > capturadoDisponible)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"Todavía no existen suficientes piezas OK capturadas para esta caja. Etiqueta: {parseado.Cantidad:N0}; disponible: {capturadoDisponible:N0}.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                if (contexto.CantidadEmbalajes.HasValue && contexto.CantidadEmbalajes.Value > 0)
                {
                    var cajasEsperadas = Convert.ToInt32(Math.Ceiling(contexto.CantidadEmbalajes.Value));
                    var cajasActuales = await ObtenerCantidadCajasNormalesAsync(vm.EjecucionProduccionID, cn, tx);
                    if (cajasActuales >= cajasEsperadas)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = $"Ya se registraron las {cajasEsperadas:N0} caja(s) normales esperadas para esta orden.";
                        return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                    }
                }
                var validacionCalidad = await ValidarEnvioCajaCalidadAsync(vm.EjecucionProduccionID, cn, tx);
                if (!validacionCalidad.Permitido || !validacionCalidad.InspeccionID.HasValue)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = validacionCalidad.Mensaje;
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                var siguienteNumero = await ObtenerSiguienteNumeroCajaAsync(vm.EjecucionProduccionID, cn, tx);
                var ejecucion = await ObtenerEjecucionOperadorAsync(vm.EjecucionProduccionID, cn, tx);
                if (ejecucion == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }
                var folioCaja = CrearFolioCajaOperador(ejecucion, siguienteNumero);
                const string sqlInsert = @"
INSERT INTO dbo.Produccion_Cajas
(
    EjecucionProduccionID,ProgramaProduccionID,SolicitudProduccionID,SolicitudProduccionDetalleID,ReleaseID,ReleaseDetalleID,
    NumeroCaja,FolioCaja,CantidadPiezas,TipoCaja,LoteMaterial,EtiquetaFolio,EstadoCajaID,EstadoCajaNombre,EtiquetaVerde,
    FechaFormacion,UsuarioFormacionID,FechaSolicitudCalidad,UsuarioSolicitudCalidadID,Observaciones,Activo,UsuarioCreacionID,FechaCreacion,
    Etiqueta,Cantidad,EstatusCalidad,OperadorUsuarioID,EsProductoIncompleto,
    CodigoBarrasOrigen,NumeroOFEtiqueta,NumeroParteEtiqueta,DesignacionEtiqueta,CantidadEtiqueta,LoteEtiqueta,
    FechaEscaneoProduccion,UsuarioEscaneoProduccionID,FechaEscaneoCalidad,UsuarioEscaneoCalidadID
)
OUTPUT INSERTED.CajaProduccionID
VALUES
(
    @EjecucionProduccionID,@ProgramaProduccionID,@SolicitudProduccionID,@SolicitudProduccionDetalleID,@ReleaseID,@ReleaseDetalleID,
    @NumeroCaja,@FolioCaja,@CantidadPiezas,N'OK',@LoteMaterial,NULL,@EstadoCajaID,@EstadoCajaNombre,0,
    @Ahora,@UsuarioID,@Ahora,@UsuarioID,@Observaciones,1,@UsuarioID,@Ahora,
    @FolioCaja,@CantidadPiezas,N'PENDIENTE',@UsuarioID,0,
    @CodigoBarrasOrigen,@NumeroOFEtiqueta,@NumeroParteEtiqueta,@DesignacionEtiqueta,@CantidadEtiqueta,@LoteEtiqueta,
    @Ahora,@UsuarioID,NULL,NULL
);";
                long cajaProduccionId;
                var ahora = DateTime.Now;
                await using (var cmd = new SqlCommand(sqlInsert, cn, tx))
                {
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = contexto.EjecucionProduccionID;
                    cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = contexto.ProgramaProduccionID;
                    cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = (object?)contexto.SolicitudProduccionID ?? DBNull.Value;
                    cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = (object?)contexto.SolicitudProduccionDetalleID ?? DBNull.Value;
                    cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = (object?)contexto.ReleaseID ?? DBNull.Value;
                    cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = (object?)contexto.ReleaseDetalleID ?? DBNull.Value;
                    cmd.Parameters.Add("@NumeroCaja", SqlDbType.Int).Value = siguienteNumero;
                    cmd.Parameters.Add("@FolioCaja", SqlDbType.NVarChar, 100).Value = folioCaja;
                    cmd.Parameters.Add("@CantidadPiezas", SqlDbType.Int).Value = parseado.Cantidad;
                    cmd.Parameters.Add("@LoteMaterial", SqlDbType.NVarChar, 150).Value = string.IsNullOrWhiteSpace(parseado.Lote) ? DBNull.Value : parseado.Lote.Trim();
                    cmd.Parameters.Add("@EstadoCajaID", SqlDbType.Int).Value = ProduccionCajaEstatus.PendienteCalidad;
                    cmd.Parameters.Add("@EstadoCajaNombre", SqlDbType.NVarChar, 100).Value = ProduccionCajaEstatus.Nombre(ProduccionCajaEstatus.PendienteCalidad);
                    cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value = "Caja formada mediante escaneo de etiqueta física y enviada a Calidad pendiente de recepción física.";
                    cmd.Parameters.Add("@CodigoBarrasOrigen", SqlDbType.NVarChar, 500).Value = codigoFisico;
                    cmd.Parameters.Add("@NumeroOFEtiqueta", SqlDbType.NVarChar, 120).Value = string.IsNullOrWhiteSpace(parseado.NumeroOF) ? DBNull.Value : parseado.NumeroOF.Trim();
                    cmd.Parameters.Add("@NumeroParteEtiqueta", SqlDbType.NVarChar, 150).Value = string.IsNullOrWhiteSpace(parseado.NumeroParte) ? DBNull.Value : parseado.NumeroParte.Trim();
                    cmd.Parameters.Add("@DesignacionEtiqueta", SqlDbType.NVarChar, 300).Value = string.IsNullOrWhiteSpace(parseado.Designacion) ? DBNull.Value : parseado.Designacion.Trim();
                    cmd.Parameters.Add("@CantidadEtiqueta", SqlDbType.Int).Value = parseado.Cantidad;
                    cmd.Parameters.Add("@LoteEtiqueta", SqlDbType.NVarChar, 150).Value = string.IsNullOrWhiteSpace(parseado.Lote) ? DBNull.Value : parseado.Lote.Trim();
                    cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    cajaProduccionId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                }
                var comentario = $"Producción escaneó y envió la caja {folioCaja} a Calidad. Etiqueta física validada contra OF {contexto.NumeroOF}, parte {(string.IsNullOrWhiteSpace(contexto.ReferenciaSAP) ? contexto.NumeroParte : contexto.ReferenciaSAP)} y cantidad {parseado.Cantidad:N0}. Pendiente de recepción física por Calidad.";
                if (comentario.Length > 1000) comentario = comentario[..1000];
                const string sqlHistorial = @"
INSERT INTO dbo.Calidad_InspeccionHistorial
(
    InspeccionID,Movimiento,EstadoAnterior,EstadoNuevo,ResultadoCalidad,Etiqueta,Comentario,UsuarioID,FechaMovimiento
)
VALUES
(
    @InspeccionID,N'CAJA_ENVIADA_DESDE_PRODUCCION',N'MONITOREO_ACTIVO',N'MONITOREO_ACTIVO',NULL,NULL,@Comentario,@UsuarioID,@Ahora
);";
                await using (var cmd = new SqlCommand(sqlHistorial, cn, tx))
                {
                    cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = validacionCalidad.InspeccionID.Value;
                    cmd.Parameters.Add("@Comentario", SqlDbType.NVarChar, 1000).Value = comentario;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
                    await cmd.ExecuteNonQueryAsync();
                }
                await tx.CommitAsync();
                TempData["Success"] = $"Caja {siguienteNumero:N0} registrada por escáner con {parseado.Cantidad:N0} pieza(s). Calidad ya fue notificada; la caja aún está pendiente de recepción física.";
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "La etiqueta ya fue registrada. No se generó una caja duplicada.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible registrar la caja escaneada: " + ex.Message;
            }
            return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CorregirCajaDevuelta(int cajaProduccionId, string? correccionRealizada)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            if (cajaProduccionId <= 0)
            {
                TempData["Error"] = "No se recibió una caja válida.";
                return RedirectToAction(nameof(Index));
            }

            correccionRealizada = correccionRealizada?.Trim();
            if (string.IsNullOrWhiteSpace(correccionRealizada))
            {
                TempData["Error"] = "Captura la corrección realizada antes de reenviar la caja a Calidad.";
                return RedirectToAction(nameof(Index));
            }

            if (correccionRealizada.Length > 1000)
            {
                TempData["Error"] = "La descripción de la corrección no puede superar 1000 caracteres.";
                return RedirectToAction(nameof(Index));
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var usuarioId = ObtenerUsuarioID();
            if (!await UsuarioEsOperadorAsync(usuarioId, cn)) return AccesoDenegadoOperador();

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            var ejecucionProduccionId = 0;

            try
            {
                const string sqlObtenerCaja = @"
SELECT TOP (1)
    c.CajaProduccionID,
    c.EjecucionProduccionID,
    ISNULL(c.NumeroCaja,0) AS NumeroCaja,
    COALESCE(NULLIF(c.FolioCaja,N''),NULLIF(c.EtiquetaFolio,N''),NULLIF(c.Etiqueta,N''),CONVERT(NVARCHAR(100),c.CajaProduccionID)) AS FolioCaja,
    ISNULL(c.EstadoCajaID,1) AS EstadoCajaID,
    UPPER(LTRIM(RTRIM(ISNULL(c.EstatusCalidad,N'')))) AS EstatusCalidad,
    ISNULL(c.MotivoCalidad,N'') AS MotivoCalidad,
    ci.InspeccionID,
    UPPER(LTRIM(RTRIM(ISNULL(ci.Estado,N'')))) AS EstadoInspeccion,
    ISNULL(ci.ConfiguracionInvalidada,0) AS ConfiguracionInvalidada
FROM dbo.Produccion_Cajas c WITH (UPDLOCK,HOLDLOCK)
OUTER APPLY
(
    SELECT TOP (1)
        i.InspeccionID,
        i.Estado,
        i.ConfiguracionInvalidada
    FROM dbo.Calidad_Inspecciones i WITH (UPDLOCK,HOLDLOCK)
    WHERE i.EjecucionProduccionID=c.EjecucionProduccionID
      AND ISNULL(i.Estado,N'')<>N'CERRADA'
    ORDER BY i.InspeccionID DESC
) ci
WHERE c.CajaProduccionID=@CajaProduccionID
  AND c.Activo=1;";

                int inspeccionId;
                int estadoCajaId;
                string estatusCalidad;
                string folioCaja;
                string motivoDevolucion;
                string estadoInspeccion;
                bool configuracionInvalidada;

                await using (var cmd = new SqlCommand(sqlObtenerCaja, cn, tx))
                {
                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = cajaProduccionId;
                    await using var rd = await cmd.ExecuteReaderAsync();

                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();
                        return NotFound();
                    }

                    ejecucionProduccionId = Convert.ToInt32(rd["EjecucionProduccionID"]);
                    estadoCajaId = Convert.ToInt32(rd["EstadoCajaID"]);
                    estatusCalidad = rd["EstatusCalidad"]?.ToString()?.Trim() ?? string.Empty;
                    folioCaja = rd["FolioCaja"]?.ToString()?.Trim() ?? cajaProduccionId.ToString();
                    motivoDevolucion = rd["MotivoCalidad"]?.ToString()?.Trim() ?? string.Empty;
                    estadoInspeccion = rd["EstadoInspeccion"]?.ToString()?.Trim() ?? string.Empty;
                    configuracionInvalidada = Convert.ToBoolean(rd["ConfiguracionInvalidada"]);

                    if (rd["InspeccionID"] == DBNull.Value)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "No existe una inspección activa de Calidad relacionada con esta caja.";
                        return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                    }

                    inspeccionId = Convert.ToInt32(rd["InspeccionID"]);
                }

                if (estadoCajaId != ProduccionCajaEstatus.FormadaProduccion || estatusCalidad != "DEVUELTA")
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Solamente pueden corregirse cajas devueltas por Calidad.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                if (configuracionInvalidada)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La configuración de Calidad está invalidada. Primero debe corregirse la configuración de la ejecución.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                if (estadoInspeccion == "PENDIENTE_RELIBERACION")
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La ejecución tiene una reliberación pendiente. La caja no puede corregirse para reenvío hasta que Calidad autorice el reinicio.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                var comentarioCorreccion = $"Producción corrigió la caja {folioCaja}. Motivo de devolución: {(string.IsNullOrWhiteSpace(motivoDevolucion) ? "No especificado" : motivoDevolucion)}. Corrección realizada: {correccionRealizada}";
                if (comentarioCorreccion.Length > 1000) comentarioCorreccion = comentarioCorreccion[..1000];

                const string sqlActualizar = @"
UPDATE dbo.Produccion_Cajas
SET EstatusCalidad=N'CORREGIDA',
    EstadoCajaID=@EstadoCajaID,
    EstadoCajaNombre=@EstadoCajaNombre,
    EtiquetaVerde=0,
    FechaLiberacionCalidad=NULL,
    AuditorCalidadUsuarioID=NULL,
    UsuarioCalidadID=NULL,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE CajaProduccionID=@CajaProduccionID
  AND EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1
  AND EstadoCajaID=@EstadoCajaID
  AND UPPER(LTRIM(RTRIM(ISNULL(EstatusCalidad,N''))))=N'DEVUELTA';

IF @@ROWCOUNT<>1
    THROW 51070,'La caja cambió de estado mientras se registraba la corrección.',1;

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
    N'CAJA_CORREGIDA_PRODUCCION',
    @EstadoInspeccion,
    @EstadoInspeccion,
    N'CORREGIDA',
    NULL,
    @Comentario,
    @UsuarioID,
    GETDATE()
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.Calidad_InspeccionHistorial h
    WHERE h.InspeccionID=@InspeccionID
      AND h.Movimiento=N'CAJA_CORREGIDA_PRODUCCION'
      AND h.Comentario LIKE N'%caja '+@FolioCaja+N'%'
      AND h.Comentario LIKE N'%'+@CorreccionRealizada+N'%'
);";

                await using (var cmd = new SqlCommand(sqlActualizar, cn, tx))
                {
                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = cajaProduccionId;
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                    cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId;
                    cmd.Parameters.Add("@EstadoCajaID", SqlDbType.Int).Value = ProduccionCajaEstatus.FormadaProduccion;
                    cmd.Parameters.Add("@EstadoCajaNombre", SqlDbType.NVarChar, 100).Value = ProduccionCajaEstatus.Nombre(ProduccionCajaEstatus.FormadaProduccion);
                    cmd.Parameters.Add("@EstadoInspeccion", SqlDbType.NVarChar, 50).Value = estadoInspeccion;
                    cmd.Parameters.Add("@Comentario", SqlDbType.NVarChar, 1000).Value = comentarioCorreccion;
                    cmd.Parameters.Add("@FolioCaja", SqlDbType.NVarChar, 100).Value = folioCaja;
                    cmd.Parameters.Add("@CorreccionRealizada", SqlDbType.NVarChar, 1000).Value = correccionRealizada;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
                TempData["Success"] = $"Corrección de la caja {folioCaja} registrada. Ya puede reenviarse a Calidad.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible registrar la corrección de la caja: " + ex.Message;
            }

            return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SolicitarLiberacionCaja(int cajaProduccionId)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            if (cajaProduccionId <= 0)
            {
                TempData["Error"] = "No se recibió una caja válida.";
                return RedirectToAction(nameof(Index));
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var usuarioId = ObtenerUsuarioID();
            if (!await UsuarioEsOperadorAsync(usuarioId, cn)) return AccesoDenegadoOperador();

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            var ejecucionProduccionId = 0;

            try
            {
                var caja = await ObtenerCajaOperadorAsync(cajaProduccionId, cn, tx);
                if (caja == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                ejecucionProduccionId = caja.EjecucionProduccionID;

                if (caja.EstadoCajaID == ProduccionCajaEstatus.PendienteCalidad)
                {
                    await tx.CommitAsync();
                    TempData["Info"] = "La caja ya se encuentra pendiente de revisión de Calidad.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                if (caja.EstadoCajaID != ProduccionCajaEstatus.FormadaProduccion)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Solo puedes solicitar liberación de una caja formada en Producción.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                const string sqlEstadoCaja = @"
SELECT TOP (1)
    UPPER(LTRIM(RTRIM(ISNULL(EstatusCalidad,N'')))) AS EstatusCalidad,
    ISNULL(MotivoCalidad,N'') AS MotivoCalidad
FROM dbo.Produccion_Cajas WITH (UPDLOCK,HOLDLOCK)
WHERE CajaProduccionID=@CajaProduccionID
  AND EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1;";

                string estatusCalidad;
                string motivoCalidad;

                await using (var cmd = new SqlCommand(sqlEstadoCaja, cn, tx))
                {
                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = cajaProduccionId;
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                    await using var rd = await cmd.ExecuteReaderAsync();

                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "No fue posible consultar el estado de Calidad de la caja.";
                        return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                    }

                    estatusCalidad = rd["EstatusCalidad"]?.ToString()?.Trim() ?? string.Empty;
                    motivoCalidad = rd["MotivoCalidad"]?.ToString()?.Trim() ?? string.Empty;
                }

                if (estatusCalidad == "DEVUELTA")
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = string.IsNullOrWhiteSpace(motivoCalidad)
                        ? "La caja fue devuelta por Calidad. Registra la corrección realizada antes de reenviarla."
                        : $"La caja fue devuelta por Calidad: {motivoCalidad}. Registra la corrección realizada antes de reenviarla.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                if (!string.IsNullOrWhiteSpace(estatusCalidad) && estatusCalidad != "CORREGIDA" && estatusCalidad != "FORMADA")
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"La caja no puede enviarse a Calidad porque actualmente tiene el estatus {estatusCalidad}.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                var validacion = await ValidarEnvioCajaCalidadAsync(ejecucionProduccionId, cn, tx);
                if (!validacion.Permitido || !validacion.InspeccionID.HasValue)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = validacion.Mensaje;
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                var esReenvio = estatusCalidad == "CORREGIDA";
                var movimiento = esReenvio ? "CAJA_REENVIADA_DESDE_PRODUCCION" : "CAJA_RECIBIDA_DESDE_PRODUCCION";
                var comentario = esReenvio
                    ? $"Producción reenvió la caja {caja.FolioCaja} después de registrar su corrección. Cantidad: {caja.CantidadPiezas} pieza(s). Tipo: {caja.TipoCaja}."
                    : $"Producción envió la caja {caja.FolioCaja} a Calidad. Cantidad: {caja.CantidadPiezas} pieza(s). Tipo: {caja.TipoCaja}.";

                const string sql = @"
UPDATE dbo.Produccion_Cajas
SET EstadoCajaID=@EstadoCajaID,
    EstadoCajaNombre=@EstadoCajaNombre,
    FechaSolicitudCalidad=GETDATE(),
    UsuarioSolicitudCalidadID=@UsuarioID,
    EstatusCalidad=N'PENDIENTE',
    EtiquetaVerde=0,
    FechaLiberacionCalidad=NULL,
    AuditorCalidadUsuarioID=NULL,
    UsuarioCalidadID=NULL,
    ResultadoCalidad=NULL,
    MotivoCalidad=NULL,
    FechaZonaVerde=NULL,
    UsuarioZonaVerdeID=NULL,
    FechaSalidaProduccion=NULL,
    UsuarioSalidaProduccionID=NULL,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE CajaProduccionID=@CajaProduccionID
  AND EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1
  AND EstadoCajaID=@EstadoActual
  AND
  (
      UPPER(LTRIM(RTRIM(ISNULL(EstatusCalidad,N'')))) IN (N'',N'FORMADA',N'CORREGIDA')
  );

IF @@ROWCOUNT<>1
    THROW 51060,'La caja cambió de estado mientras se enviaba a Calidad.',1;

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
    @Movimiento,
    N'MONITOREO_ACTIVO',
    N'MONITOREO_ACTIVO',
    NULL,
    NULL,
    @Comentario,
    @UsuarioID,
    GETDATE()
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.Calidad_InspeccionHistorial h
    WHERE h.InspeccionID=@InspeccionID
      AND h.Movimiento=@Movimiento
      AND h.Comentario LIKE N'%'+@FolioCaja+N'%'
      AND h.FechaMovimiento>=DATEADD(SECOND,-5,GETDATE())
);";

                await using (var cmd = new SqlCommand(sql, cn, tx))
                {
                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = cajaProduccionId;
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                    cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = validacion.InspeccionID.Value;
                    cmd.Parameters.Add("@EstadoCajaID", SqlDbType.Int).Value = ProduccionCajaEstatus.PendienteCalidad;
                    cmd.Parameters.Add("@EstadoCajaNombre", SqlDbType.NVarChar, 100).Value = ProduccionCajaEstatus.Nombre(ProduccionCajaEstatus.PendienteCalidad);
                    cmd.Parameters.Add("@EstadoActual", SqlDbType.Int).Value = ProduccionCajaEstatus.FormadaProduccion;
                    cmd.Parameters.Add("@Movimiento", SqlDbType.NVarChar, 100).Value = movimiento;
                    cmd.Parameters.Add("@Comentario", SqlDbType.NVarChar, 1000).Value = comentario.Length > 1000 ? comentario[..1000] : comentario;
                    cmd.Parameters.Add("@FolioCaja", SqlDbType.NVarChar, 100).Value = string.IsNullOrWhiteSpace(caja.FolioCaja) ? cajaProduccionId.ToString() : caja.FolioCaja;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
                TempData["Success"] = esReenvio
                    ? "Caja corregida y reenviada a Calidad para una nueva revisión."
                    : "Caja enviada a Calidad para revisión.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible solicitar liberación de la caja: " + ex.Message;
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

                cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value =
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

                cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value =
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


        private async Task<ProduccionOperadorCajasVm?> ObtenerCajasOperadorVmAsync(int ejecucionProduccionId, SqlConnection cn)
        {
            const string sql = @"
SELECT TOP(1)
    e.EjecucionProduccionID,e.ProgramaProduccionID,e.SolicitudProduccionID,e.SolicitudProduccionDetalleID,e.ParteID,
    s.FolioSolicitud,s.NumeroOFRecibida,pp.ClienteNombre,e.MaquinaCodigo,e.MaquinaNombre,e.NumeroParte,e.ReferenciaSAP,
    e.DescripcionParte,e.MoldeCodigo,
    COALESCE(NULLIF(d.MaterialCodigo,N''),NULLIF(pp.MaterialCodigo,N'')) AS MaterialCodigo,
    COALESCE(NULLIF(d.MaterialDescripcion,N''),NULLIF(pp.MaterialDescripcion,N'')) AS MaterialDescripcion,
    COALESCE(NULLIF(d.EmbalajeCodigo,N''),NULLIF(pp.EmbalajeCodigo,N'')) AS EmbalajeCodigo,
    COALESCE(NULLIF(d.EmbalajeDescripcion,N''),NULLIF(pp.EmbalajeDescripcion,N'')) AS EmbalajeDescripcion,
    d.PiezasPorEmbalaje,d.CantidadEmbalajes,
    ISNULL(e.CantidadPlaneada,0) AS CantidadPlaneada,
    ISNULL(e.CantidadOKTotal,0) AS CantidadOKTotal,
    ISNULL(e.CantidadSospechosaTotal,0) AS CantidadSospechosaTotal,
    ISNULL(e.CantidadScrapTotal,0) AS CantidadScrapTotal,
    e.EstatusID,
    CASE WHEN EXISTS(SELECT 1 FROM dbo.Produccion_Paros p WHERE p.EjecucionProduccionID=e.EjecucionProduccionID AND p.Activo=1 AND p.FechaFinParo IS NULL) THEN 1 ELSE 0 END AS TieneParoAbierto
FROM dbo.Produccion_Ejecucion e
LEFT JOIN dbo.SolicitudesProduccion s ON s.SolicitudProduccionID=e.SolicitudProduccionID AND s.Activo=1
LEFT JOIN dbo.SolicitudesProduccionDetalle d ON d.SolicitudProduccionDetalleID=e.SolicitudProduccionDetalleID AND d.Activo=1
LEFT JOIN dbo.Planeacion_ProgramaProduccion pp ON pp.ProgramaProduccionID=e.ProgramaProduccionID AND pp.Activo=1
WHERE e.EjecucionProduccionID=@EjecucionProduccionID AND e.Activo=1;";
            ProduccionOperadorCajasVm? vm = null;
            await using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                await using var rd = await cmd.ExecuteReaderAsync();
                if (!await rd.ReadAsync()) return null;
                vm = new ProduccionOperadorCajasVm
                {
                    EjecucionProduccionID = Entero(rd, "EjecucionProduccionID"),
                    ProgramaProduccionID = Entero(rd, "ProgramaProduccionID"),
                    SolicitudProduccionID = NullableEntero(rd, "SolicitudProduccionID"),
                    SolicitudProduccionDetalleID = NullableEntero(rd, "SolicitudProduccionDetalleID"),
                    ParteID = NullableEntero(rd, "ParteID"),
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
                    PiezasPorEmbalaje = rd["PiezasPorEmbalaje"] == DBNull.Value ? null : Convert.ToDecimal(rd["PiezasPorEmbalaje"]),
                    CantidadEmbalajes = rd["CantidadEmbalajes"] == DBNull.Value ? null : Convert.ToDecimal(rd["CantidadEmbalajes"]),
                    CantidadPlaneada = Entero(rd, "CantidadPlaneada"),
                    CantidadOKTotal = Entero(rd, "CantidadOKTotal"),
                    CantidadSospechosaTotal = Entero(rd, "CantidadSospechosaTotal"),
                    CantidadScrapTotal = Entero(rd, "CantidadScrapTotal"),
                    EstatusID = Entero(rd, "EstatusID"),
                    TieneParoAbierto = Booleano(rd, "TieneParoAbierto")
                };
            }
            vm.Cajas = await ObtenerCajasPorEjecucionAsync(ejecucionProduccionId, cn);
            var consumo = await ObtenerConsumoCajasEjecucionAsync(ejecucionProduccionId, cn, null);
            vm.CantidadOKEnCajas = consumo.Ok;
            vm.CantidadSospechosaEnCajas = consumo.Sospechoso;
            vm.CantidadScrapEnCajas = consumo.Scrap;
            vm.CantidadRetencionEnCajas = consumo.Retencion;
            vm.SiguienteNumeroCaja = vm.Cajas.Any() ? vm.Cajas.Max(x => x.NumeroCaja) + 1 : 1;
            vm.PuedeFormarCaja = vm.EstatusID == ProduccionEstatus.EnProduccion && !vm.TieneParoAbierto;
            vm.CajasIncompletasDisponibles = vm.ParteID.HasValue ? await ObtenerCajasIncompletasCompatiblesAsync(ejecucionProduccionId, vm.ParteID.Value, vm.PiezasPorCajaSugeridas, cn) : new List<ProduccionCajaIncompletaDisponibleVm>();
            return vm;
        }


        private async Task<List<ProduccionOperadorCajaVm>> ObtenerCajasPorEjecucionAsync(int ejecucionProduccionId, SqlConnection cn)
        {
            var lista = new List<ProduccionOperadorCajaVm>();
            const string sql = @"
SELECT
    CajaProduccionID,EjecucionProduccionID,ISNULL(ProgramaProduccionID,0) AS ProgramaProduccionID,
    SolicitudProduccionID,SolicitudProduccionDetalleID,ISNULL(NumeroCaja,0) AS NumeroCaja,FolioCaja,
    ISNULL(CantidadPiezas,ISNULL(Cantidad,0)) AS CantidadPiezas,ISNULL(TipoCaja,N'OK') AS TipoCaja,
    LoteMaterial,ISNULL(EtiquetaFolio,Etiqueta) AS EtiquetaFolio,ISNULL(EtiquetaVerde,0) AS EtiquetaVerde,
    ISNULL(EstadoCajaID,1) AS EstadoCajaID,ISNULL(EstadoCajaNombre,N'Formada en Producción') AS EstadoCajaNombre,
    ISNULL(FechaFormacion,FechaCreacion) AS FechaFormacion,UsuarioFormacionID,FechaSolicitudCalidad,UsuarioSolicitudCalidadID,
    FechaLiberacionCalidad,UsuarioCalidadID,ResultadoCalidad,MotivoCalidad,FechaZonaVerde,UsuarioZonaVerdeID,
    FechaSalidaProduccion,UsuarioSalidaProduccionID,FechaRecepcionAlmacen,UsuarioAlmacenID,Observaciones,
    ISNULL(EsProductoIncompleto,0) AS EsProductoIncompleto,EstadoProductoIncompleto,EjecucionReservaID,ProgramaReservaID,
    SolicitudReservaID,SolicitudDetalleReservaID,FechaReservaIncompleto,UsuarioReservaIncompletoID,FechaCompletadoIncompleto,
    CapacidadObjetivoCaja,CantidadPendienteCompletar,EtiquetaBlanca,
    CodigoBarrasOrigen,NumeroOFEtiqueta,NumeroParteEtiqueta,DesignacionEtiqueta,CantidadEtiqueta,LoteEtiqueta,
    FechaEscaneoProduccion,UsuarioEscaneoProduccionID,FechaEscaneoCalidad,UsuarioEscaneoCalidadID
FROM dbo.Produccion_Cajas
WHERE EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1
ORDER BY NumeroCaja,CajaProduccionID;";
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync()) lista.Add(MapearCajaOperador(rd));
            return lista;
        }
        private async Task<ProduccionOperadorCajaVm?> ObtenerCajaOperadorAsync(long cajaProduccionId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP(1)
    CajaProduccionID,EjecucionProduccionID,ISNULL(ProgramaProduccionID,0) AS ProgramaProduccionID,
    SolicitudProduccionID,SolicitudProduccionDetalleID,ISNULL(NumeroCaja,0) AS NumeroCaja,FolioCaja,
    ISNULL(CantidadPiezas,ISNULL(Cantidad,0)) AS CantidadPiezas,ISNULL(TipoCaja,N'OK') AS TipoCaja,
    LoteMaterial,ISNULL(EtiquetaFolio,Etiqueta) AS EtiquetaFolio,ISNULL(EtiquetaVerde,0) AS EtiquetaVerde,
    ISNULL(EstadoCajaID,1) AS EstadoCajaID,ISNULL(EstadoCajaNombre,N'Formada en Producción') AS EstadoCajaNombre,
    ISNULL(FechaFormacion,FechaCreacion) AS FechaFormacion,UsuarioFormacionID,FechaSolicitudCalidad,UsuarioSolicitudCalidadID,
    FechaLiberacionCalidad,UsuarioCalidadID,ResultadoCalidad,MotivoCalidad,FechaZonaVerde,UsuarioZonaVerdeID,
    FechaSalidaProduccion,UsuarioSalidaProduccionID,FechaRecepcionAlmacen,UsuarioAlmacenID,Observaciones,
    ISNULL(EsProductoIncompleto,0) AS EsProductoIncompleto,EstadoProductoIncompleto,EjecucionReservaID,ProgramaReservaID,
    SolicitudReservaID,SolicitudDetalleReservaID,FechaReservaIncompleto,UsuarioReservaIncompletoID,FechaCompletadoIncompleto,
    CapacidadObjetivoCaja,CantidadPendienteCompletar,EtiquetaBlanca,
    CodigoBarrasOrigen,NumeroOFEtiqueta,NumeroParteEtiqueta,DesignacionEtiqueta,CantidadEtiqueta,LoteEtiqueta,
    FechaEscaneoProduccion,UsuarioEscaneoProduccionID,FechaEscaneoCalidad,UsuarioEscaneoCalidadID
FROM dbo.Produccion_Cajas WITH(UPDLOCK,HOLDLOCK)
WHERE CajaProduccionID=@CajaProduccionID
  AND Activo=1;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = cajaProduccionId;
            await using var rd = await cmd.ExecuteReaderAsync();
            return await rd.ReadAsync() ? MapearCajaOperador(rd) : null;
        }

        private static ProduccionOperadorCajaVm MapearCajaOperador(SqlDataReader rd)
        {
            return new ProduccionOperadorCajaVm
            {
                CajaProduccionID = EnteroLargo(rd, "CajaProduccionID"),
                EjecucionProduccionID = Entero(rd, "EjecucionProduccionID"),
                ProgramaProduccionID = Entero(rd, "ProgramaProduccionID"),
                SolicitudProduccionID = NullableEntero(rd, "SolicitudProduccionID"),
                SolicitudProduccionDetalleID = NullableEntero(rd, "SolicitudProduccionDetalleID"),
                NumeroCaja = Entero(rd, "NumeroCaja"),
                FolioCaja = TextoNullable(rd, "FolioCaja"),
                CantidadPiezas = Entero(rd, "CantidadPiezas"),
                TipoCaja = TextoNullable(rd, "TipoCaja") ?? ProduccionCajaTipo.Ok,
                LoteMaterial = TextoNullable(rd, "LoteMaterial"),
                EtiquetaFolio = TextoNullable(rd, "EtiquetaFolio"),
                EtiquetaVerde = Booleano(rd, "EtiquetaVerde"),
                EstadoCajaID = Entero(rd, "EstadoCajaID"),
                EstadoCajaNombre = TextoNullable(rd, "EstadoCajaNombre") ?? "Formada en Producción",
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
                Observaciones = TextoNullable(rd, "Observaciones"),
                EsProductoIncompleto = Booleano(rd, "EsProductoIncompleto"),
                EstadoProductoIncompleto = TextoNullable(rd, "EstadoProductoIncompleto"),
                EjecucionReservaID = NullableEntero(rd, "EjecucionReservaID"),
                ProgramaReservaID = NullableEntero(rd, "ProgramaReservaID"),
                SolicitudReservaID = NullableEntero(rd, "SolicitudReservaID"),
                SolicitudDetalleReservaID = NullableEntero(rd, "SolicitudDetalleReservaID"),
                FechaReservaIncompleto = NullableFecha(rd, "FechaReservaIncompleto"),
                UsuarioReservaIncompletoID = NullableEntero(rd, "UsuarioReservaIncompletoID"),
                FechaCompletadoIncompleto = NullableFecha(rd, "FechaCompletadoIncompleto"),
                CapacidadObjetivoCaja = NullableEntero(rd, "CapacidadObjetivoCaja"),
                CantidadPendienteCompletar = NullableEntero(rd, "CantidadPendienteCompletar"),
                EtiquetaBlanca = TextoNullable(rd, "EtiquetaBlanca"),
                CodigoBarrasOrigen = TextoNullable(rd, "CodigoBarrasOrigen"),
                NumeroOFEtiqueta = TextoNullable(rd, "NumeroOFEtiqueta"),
                NumeroParteEtiqueta = TextoNullable(rd, "NumeroParteEtiqueta"),
                DesignacionEtiqueta = TextoNullable(rd, "DesignacionEtiqueta"),
                CantidadEtiqueta = NullableEntero(rd, "CantidadEtiqueta"),
                LoteEtiqueta = TextoNullable(rd, "LoteEtiqueta"),
                FechaEscaneoProduccion = NullableFecha(rd, "FechaEscaneoProduccion"),
                UsuarioEscaneoProduccionID = NullableEntero(rd, "UsuarioEscaneoProduccionID"),
                FechaEscaneoCalidad = NullableFecha(rd, "FechaEscaneoCalidad"),
                UsuarioEscaneoCalidadID = NullableEntero(rd, "UsuarioEscaneoCalidadID"),
                ActivoParaCalculo = true
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

        private async Task<int> ObtenerCantidadDisponibleParaCajaAsync(int ejecucionProduccionId, string tipoCaja, SqlConnection cn, SqlTransaction tx)
        {
            tipoCaja = NormalizarTipoCajaOperador(tipoCaja);
            const string sql = @"
SELECT ISNULL(CantidadOKTotal,0) AS OKTotal,ISNULL(CantidadSospechosaTotal,0) AS SospechosaTotal,ISNULL(CantidadScrapTotal,0) AS ScrapTotal
FROM dbo.Produccion_Ejecucion WITH(UPDLOCK,HOLDLOCK)
WHERE EjecucionProduccionID=@EjecucionProduccionID AND Activo=1;";
            int okTotal;
            int sospechosaTotal;
            int scrapTotal;
            await using (var cmd = new SqlCommand(sql, cn, tx))
            {
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                await using var rd = await cmd.ExecuteReaderAsync();
                if (!await rd.ReadAsync()) return 0;
                okTotal = Entero(rd, "OKTotal");
                sospechosaTotal = Entero(rd, "SospechosaTotal");
                scrapTotal = Entero(rd, "ScrapTotal");
            }
            var consumo = await ObtenerConsumoCajasEjecucionAsync(ejecucionProduccionId, cn, tx);
            return tipoCaja switch
            {
                ProduccionCajaTipo.Ok => Math.Max(0, okTotal - consumo.Ok),
                ProduccionCajaTipo.Incompleta => Math.Max(0, okTotal - consumo.Ok),
                ProduccionCajaTipo.Sospechoso => Math.Max(0, sospechosaTotal - consumo.Sospechoso - consumo.Retencion),
                ProduccionCajaTipo.Retencion => Math.Max(0, sospechosaTotal - consumo.Sospechoso - consumo.Retencion),
                ProduccionCajaTipo.Scrap => Math.Max(0, scrapTotal - consumo.Scrap),
                _ => 0
            };
        }

        private async Task<(int Ok, int Sospechoso, int Scrap, int Retencion)> ObtenerConsumoCajasEjecucionAsync(int ejecucionProduccionId, SqlConnection cn, SqlTransaction? tx)
        {
            const string sql = @"
SELECT
ISNULL(SUM(CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(c.TipoCaja,N'OK'))))=N'OK' THEN ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0)) ELSE 0 END),0) AS OkNormal,
ISNULL(SUM(CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(c.TipoCaja,N''))))=N'SOSPECHOSO' THEN ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0)) ELSE 0 END),0) AS Sospechoso,
ISNULL(SUM(CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(c.TipoCaja,N''))))=N'SCRAP' THEN ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0)) ELSE 0 END),0) AS Scrap,
ISNULL(SUM(CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(c.TipoCaja,N''))))=N'RETENCION' THEN ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0)) ELSE 0 END),0) AS Retencion
FROM dbo.Produccion_Cajas c
WHERE c.EjecucionProduccionID=@EjecucionProduccionID AND c.Activo=1
AND NOT EXISTS(SELECT 1 FROM dbo.Produccion_CajaOrigenDetalle od WHERE od.CajaProduccionID=c.CajaProduccionID AND od.Activo=1);
SELECT ISNULL(SUM(od.CantidadPiezas),0)
FROM dbo.Produccion_CajaOrigenDetalle od
INNER JOIN dbo.Produccion_Cajas c ON c.CajaProduccionID=od.CajaProduccionID AND c.Activo=1
WHERE od.EjecucionProduccionID=@EjecucionProduccionID AND od.Activo=1;";
            await using var cmd = tx == null ? new SqlCommand(sql, cn) : new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
            int okNormal;
            int sospechoso;
            int scrap;
            int retencion;
            int detalleOk;
            await using (var rd = await cmd.ExecuteReaderAsync())
            {
                if (!await rd.ReadAsync()) return (0, 0, 0, 0);
                okNormal = Entero(rd, "OkNormal");
                sospechoso = Entero(rd, "Sospechoso");
                scrap = Entero(rd, "Scrap");
                retencion = Entero(rd, "Retencion");
                if (!await rd.NextResultAsync() || !await rd.ReadAsync()) detalleOk = 0;
                else detalleOk = rd[0] == DBNull.Value ? 0 : Convert.ToInt32(rd[0]);
            }
            return (okNormal + detalleOk, sospechoso, scrap, retencion);
        }

        private async Task<int> ObtenerCantidadDetalleCajaPorEjecucionAsync(int ejecucionProduccionId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
SELECT ISNULL(SUM(CantidadPiezas),0)
FROM dbo.Produccion_CajaOrigenDetalle WITH(UPDLOCK,HOLDLOCK)
WHERE EjecucionProduccionID=@EjecucionProduccionID AND Activo=1;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        private async Task<List<ProduccionCajaIncompletaDisponibleVm>> ObtenerCajasIncompletasCompatiblesAsync(int ejecucionActualId, int parteId, int capacidadCajaActual, SqlConnection cn)
        {
            var lista = new List<ProduccionCajaIncompletaDisponibleVm>();
            const string sql = @"
SELECT c.CajaProduccionID,c.EjecucionProduccionID,ISNULL(c.ProgramaProduccionID,0) AS ProgramaProduccionID,
c.SolicitudProduccionID,c.SolicitudProduccionDetalleID,eOrigen.ParteID,eOrigen.NumeroParte,eOrigen.ReferenciaSAP,
c.FolioCaja,c.EtiquetaBlanca,ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0)) AS CantidadPiezas,
ISNULL(c.CapacidadObjetivoCaja,0) AS CapacidadObjetivoCaja,
ISNULL(c.CantidadPendienteCompletar,CASE WHEN ISNULL(c.CapacidadObjetivoCaja,0)>ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0)) THEN c.CapacidadObjetivoCaja-ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0)) ELSE 0 END) AS CantidadPendienteCompletar,
ISNULL(c.EstadoProductoIncompleto,N'RESERVADA') AS EstadoProductoIncompleto,
c.EjecucionReservaID,c.ProgramaReservaID,c.SolicitudReservaID,c.SolicitudDetalleReservaID,
ISNULL(c.FechaFormacion,c.FechaCreacion) AS FechaFormacion,c.FechaReservaIncompleto
FROM dbo.Planeacion_ProductoIncompletoApartado a
INNER JOIN dbo.Produccion_Cajas c ON c.CajaProduccionID=a.CajaProduccionID
INNER JOIN dbo.Produccion_Ejecucion eOrigen ON eOrigen.EjecucionProduccionID=c.EjecucionProduccionID
WHERE a.EjecucionProduccionID=@EjecucionActualID
  AND a.Activo=1
  AND a.EstatusID=4
  AND c.Activo=1
  AND ISNULL(c.EsProductoIncompleto,0)=1
  AND eOrigen.ParteID=@ParteID
  AND c.EjecucionProduccionID<>@EjecucionActualID
  AND c.EjecucionReservaID=@EjecucionActualID
  AND UPPER(LTRIM(RTRIM(ISNULL(c.EstadoProductoIncompleto,N'')))) IN(N'RESERVADA',N'EN_COMPLETADO')
  AND(@CapacidadCaja<=0 OR ISNULL(c.CapacidadObjetivoCaja,0)=@CapacidadCaja)
ORDER BY ISNULL(c.FechaReservaIncompleto,c.FechaFormacion),c.CajaProduccionID;";
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@EjecucionActualID", SqlDbType.Int).Value = ejecucionActualId;
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId;
            cmd.Parameters.Add("@CapacidadCaja", SqlDbType.Int).Value = capacidadCajaActual;
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                lista.Add(new ProduccionCajaIncompletaDisponibleVm
                {
                    CajaProduccionID = EnteroLargo(rd, "CajaProduccionID"),
                    EjecucionProduccionID = Entero(rd, "EjecucionProduccionID"),
                    ProgramaProduccionID = Entero(rd, "ProgramaProduccionID"),
                    SolicitudProduccionID = NullableEntero(rd, "SolicitudProduccionID"),
                    SolicitudProduccionDetalleID = NullableEntero(rd, "SolicitudProduccionDetalleID"),
                    ParteID = NullableEntero(rd, "ParteID"),
                    NumeroParte = TextoNullable(rd, "NumeroParte"),
                    ReferenciaSAP = TextoNullable(rd, "ReferenciaSAP"),
                    FolioCaja = TextoNullable(rd, "FolioCaja"),
                    EtiquetaBlanca = TextoNullable(rd, "EtiquetaBlanca"),
                    CantidadPiezas = Entero(rd, "CantidadPiezas"),
                    CapacidadObjetivoCaja = Entero(rd, "CapacidadObjetivoCaja"),
                    CantidadPendienteCompletar = Entero(rd, "CantidadPendienteCompletar"),
                    EstadoProductoIncompleto = TextoNullable(rd, "EstadoProductoIncompleto") ?? ProduccionProductoIncompletoEstado.Reservada,
                    EjecucionReservaID = NullableEntero(rd, "EjecucionReservaID"),
                    ProgramaReservaID = NullableEntero(rd, "ProgramaReservaID"),
                    SolicitudReservaID = NullableEntero(rd, "SolicitudReservaID"),
                    SolicitudDetalleReservaID = NullableEntero(rd, "SolicitudDetalleReservaID"),
                    FechaFormacion = Fecha(rd, "FechaFormacion"),
                    FechaReservaIncompleto = NullableFecha(rd, "FechaReservaIncompleto")
                });
            }
            return lista;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReservarCajaIncompleta(ProduccionReservarCajaIncompletaPostVm vm)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            if (vm.CajaProduccionID <= 0 || vm.EjecucionProduccionID <= 0) { TempData["Error"] = "No se recibió correctamente la etiqueta blanca."; return RedirectToAction(nameof(Index)); }
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            var usuarioId = ObtenerUsuarioID();
            if (!await UsuarioEsOperadorAsync(usuarioId, cn)) return AccesoDenegadoOperador();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var ejecucion = await ObtenerEjecucionOperadorAsync(vm.EjecucionProduccionID, cn, tx);
                if (ejecucion == null) { await tx.RollbackAsync(); return NotFound(); }
                if (!ejecucion.ParteID.HasValue || ejecucion.ParteID.Value <= 0) { await tx.RollbackAsync(); TempData["Error"] = "La ejecución no tiene una pieza válida relacionada."; return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID }); }
                const string sql = @"
SELECT TOP(1)c.CajaProduccionID,c.EtiquetaBlanca,ISNULL(c.CapacidadObjetivoCaja,0) AS CapacidadObjetivoCaja,
ISNULL(c.CantidadPendienteCompletar,0) AS CantidadPendienteCompletar,
ISNULL(c.EstadoProductoIncompleto,N'') AS EstadoProductoIncompleto,c.EjecucionReservaID,eOrigen.ParteID,
a.ProductoIncompletoApartadoID,a.EstatusID AS EstatusApartado
FROM dbo.Planeacion_ProductoIncompletoApartado a WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Produccion_Cajas c WITH(UPDLOCK,HOLDLOCK) ON c.CajaProduccionID=a.CajaProduccionID
INNER JOIN dbo.Produccion_Ejecucion eOrigen ON eOrigen.EjecucionProduccionID=c.EjecucionProduccionID
WHERE a.CajaProduccionID=@CajaProduccionID
  AND a.EjecucionProduccionID=@EjecucionProduccionID
  AND a.ProgramaProduccionID=@ProgramaProduccionID
  AND a.Activo=1
  AND a.EstatusID=4
  AND c.Activo=1
  AND ISNULL(c.EsProductoIncompleto,0)=1;";
                long apartadoId;
                int parteOrigen;
                int capacidadCaja;
                int pendiente;
                int? reservaActual;
                string etiquetaBlanca;
                string estado;
                await using (var cmd = new SqlCommand(sql, cn, tx))
                {
                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = vm.CajaProduccionID;
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = vm.EjecucionProduccionID;
                    cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = ejecucion.ProgramaProduccionID;
                    await using var rd = await cmd.ExecuteReaderAsync();
                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "Esta etiqueta blanca no fue asignada por Planeación a esta OF. Producción no puede reservar producto incompleto libre.";
                        return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                    }
                    apartadoId = Convert.ToInt64(rd["ProductoIncompletoApartadoID"]);
                    parteOrigen = rd["ParteID"] == DBNull.Value ? 0 : Convert.ToInt32(rd["ParteID"]);
                    capacidadCaja = Convert.ToInt32(rd["CapacidadObjetivoCaja"]);
                    pendiente = Convert.ToInt32(rd["CantidadPendienteCompletar"]);
                    reservaActual = rd["EjecucionReservaID"] == DBNull.Value ? null : Convert.ToInt32(rd["EjecucionReservaID"]);
                    etiquetaBlanca = rd["EtiquetaBlanca"]?.ToString() ?? vm.CajaProduccionID.ToString();
                    estado = rd["EstadoProductoIncompleto"]?.ToString()?.Trim().ToUpperInvariant() ?? string.Empty;
                }
                if (parteOrigen != ejecucion.ParteID.Value) { await tx.RollbackAsync(); TempData["Error"] = $"La etiqueta blanca {etiquetaBlanca} corresponde a una pieza diferente."; return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID }); }
                if (pendiente <= 0) { await tx.RollbackAsync(); TempData["Error"] = $"La etiqueta blanca {etiquetaBlanca} ya no tiene piezas pendientes por completar."; return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID }); }
                if (reservaActual.HasValue && reservaActual.Value != vm.EjecucionProduccionID) { await tx.RollbackAsync(); TempData["Error"] = $"La etiqueta blanca {etiquetaBlanca} quedó relacionada con otra ejecución. Solicita revisión de Planeación."; return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID }); }
                if (estado != ProduccionProductoIncompletoEstado.Reservada && estado != ProduccionProductoIncompletoEstado.EnCompletado) { await tx.RollbackAsync(); TempData["Error"] = $"La etiqueta blanca {etiquetaBlanca} no se encuentra en estado válido para esta OF."; return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID }); }
                if (ejecucion.SolicitudProduccionDetalleID.HasValue)
                {
                    const string sqlCapacidad = @"SELECT TOP(1)PiezasPorEmbalaje FROM dbo.SolicitudesProduccionDetalle WITH(UPDLOCK,HOLDLOCK) WHERE SolicitudProduccionDetalleID=@SolicitudProduccionDetalleID AND Activo=1;";
                    await using var cmdCapacidad = new SqlCommand(sqlCapacidad, cn, tx);
                    cmdCapacidad.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = ejecucion.SolicitudProduccionDetalleID.Value;
                    var valor = await cmdCapacidad.ExecuteScalarAsync();
                    var capacidadActual = valor == null || valor == DBNull.Value ? 0 : Convert.ToInt32(Math.Floor(Convert.ToDecimal(valor)));
                    if (capacidadActual <= 0 || capacidadActual != capacidadCaja) { await tx.RollbackAsync(); TempData["Error"] = $"La capacidad del embalaje de esta OF no coincide con la etiqueta blanca {etiquetaBlanca}."; return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID }); }
                }
                const string sqlSincronizar = @"
UPDATE dbo.Produccion_Cajas
SET EstadoProductoIncompleto=CASE WHEN EstadoProductoIncompleto=N'EN_COMPLETADO' THEN N'EN_COMPLETADO' ELSE N'RESERVADA' END,
EjecucionReservaID=@EjecucionProduccionID,ProgramaReservaID=@ProgramaProduccionID,SolicitudReservaID=@SolicitudProduccionID,SolicitudDetalleReservaID=@SolicitudDetalleReservaID,
FechaReservaIncompleto=COALESCE(FechaReservaIncompleto,SYSDATETIME()),UsuarioReservaIncompletoID=COALESCE(UsuarioReservaIncompletoID,@UsuarioID),
UsuarioModificacionID=@UsuarioID,FechaModificacion=SYSDATETIME()
WHERE CajaProduccionID=@CajaProduccionID AND Activo=1 AND ISNULL(EsProductoIncompleto,0)=1
AND(EjecucionReservaID IS NULL OR EjecucionReservaID=@EjecucionProduccionID);
IF @@ROWCOUNT<>1 THROW 51110,'La etiqueta blanca cambió de asignación mientras se validaba.',1;";
                await using (var cmd = new SqlCommand(sqlSincronizar, cn, tx))
                {
                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = vm.CajaProduccionID;
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucion.EjecucionProduccionID;
                    cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = ejecucion.ProgramaProduccionID;
                    cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = (object?)ejecucion.SolicitudProduccionID ?? DBNull.Value;
                    cmd.Parameters.Add("@SolicitudDetalleReservaID", SqlDbType.Int).Value = (object?)ejecucion.SolicitudProduccionDetalleID ?? DBNull.Value;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    await cmd.ExecuteNonQueryAsync();
                }
                await tx.CommitAsync();
                TempData["Info"] = $"Etiqueta blanca {etiquetaBlanca} confirmada para esta OF. Contiene producto previo y faltan {pendiente:N0} pieza(s) para completar la caja.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible validar la etiqueta blanca asignada: " + ex.Message;
            }
            return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CompletarCajaIncompleta(ProduccionCompletarCajaIncompletaPostVm vm)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            if (vm.CajaProduccionID <= 0 || vm.EjecucionProduccionID <= 0 || vm.CantidadPiezas <= 0) { TempData["Error"] = "Los datos para completar la caja no son válidos."; return RedirectToAction(nameof(Index)); }
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            var usuarioId = ObtenerUsuarioID();
            if (!await UsuarioEsOperadorAsync(usuarioId, cn)) return AccesoDenegadoOperador();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var ejecucion = await ObtenerEjecucionOperadorAsync(vm.EjecucionProduccionID, cn, tx);
                if (ejecucion == null) { await tx.RollbackAsync(); return NotFound(); }
                if (ejecucion.EstatusID != ProduccionEstatus.EnProduccion) { await tx.RollbackAsync(); TempData["Error"] = "Solo puedes completar producto incompleto cuando la OF está en producción."; return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID }); }
                if (await TieneParoAbiertoAsync(vm.EjecucionProduccionID, cn, tx)) { await tx.RollbackAsync(); TempData["Error"] = "No puedes completar cajas mientras exista un paro abierto."; return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID }); }
                const string sqlCaja = @"
SELECT c.CajaProduccionID,c.EjecucionProduccionID,ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0)) AS CantidadPiezas,
ISNULL(c.CapacidadObjetivoCaja,0) AS CapacidadObjetivoCaja,ISNULL(c.CantidadPendienteCompletar,0) AS CantidadPendienteCompletar,
ISNULL(c.EstadoProductoIncompleto,N'') AS EstadoProductoIncompleto,c.EjecucionReservaID,c.EtiquetaBlanca,eOrigen.ParteID,
a.ProductoIncompletoApartadoID
FROM dbo.Planeacion_ProductoIncompletoApartado a WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Produccion_Cajas c WITH(UPDLOCK,HOLDLOCK) ON c.CajaProduccionID=a.CajaProduccionID
INNER JOIN dbo.Produccion_Ejecucion eOrigen ON eOrigen.EjecucionProduccionID=c.EjecucionProduccionID
WHERE a.CajaProduccionID=@CajaProduccionID
  AND a.EjecucionProduccionID=@EjecucionProduccionID
  AND a.ProgramaProduccionID=@ProgramaProduccionID
  AND a.Activo=1
  AND a.EstatusID=4
  AND c.Activo=1
  AND ISNULL(c.EsProductoIncompleto,0)=1;";
                int cantidadActual, capacidad, pendiente, parteOrigen;
                int? reservaId;
                string estado, etiquetaBlanca;
                await using (var cmd = new SqlCommand(sqlCaja, cn, tx))
                {
                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = vm.CajaProduccionID;
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = vm.EjecucionProduccionID;
                    cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = ejecucion.ProgramaProduccionID;
                    await using var rd = await cmd.ExecuteReaderAsync();
                    if (!await rd.ReadAsync()) { await tx.RollbackAsync(); TempData["Error"] = "La etiqueta blanca no fue asignada por Planeación a esta ejecución o ya fue aplicada."; return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID }); }
                    cantidadActual = Convert.ToInt32(rd["CantidadPiezas"]);
                    capacidad = Convert.ToInt32(rd["CapacidadObjetivoCaja"]);
                    pendiente = Convert.ToInt32(rd["CantidadPendienteCompletar"]);
                    parteOrigen = rd["ParteID"] == DBNull.Value ? 0 : Convert.ToInt32(rd["ParteID"]);
                    reservaId = rd["EjecucionReservaID"] == DBNull.Value ? null : Convert.ToInt32(rd["EjecucionReservaID"]);
                    estado = rd["EstadoProductoIncompleto"]?.ToString()?.Trim().ToUpperInvariant() ?? string.Empty;
                    etiquetaBlanca = rd["EtiquetaBlanca"]?.ToString() ?? vm.CajaProduccionID.ToString();
                }
                if (!reservaId.HasValue || reservaId.Value != vm.EjecucionProduccionID) { await tx.RollbackAsync(); TempData["Error"] = $"La etiqueta blanca {etiquetaBlanca} no está relacionada con esta ejecución."; return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID }); }
                if (estado != ProduccionProductoIncompletoEstado.Reservada && estado != ProduccionProductoIncompletoEstado.EnCompletado) { await tx.RollbackAsync(); TempData["Error"] = "La etiqueta blanca no se encuentra en estado válido para completarse."; return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID }); }
                if (!ejecucion.ParteID.HasValue || ejecucion.ParteID.Value != parteOrigen) { await tx.RollbackAsync(); TempData["Error"] = "La pieza de la OF actual no coincide con la etiqueta blanca asignada."; return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID }); }
                if (capacidad <= 0 || pendiente <= 0) { await tx.RollbackAsync(); TempData["Error"] = "La etiqueta blanca no tiene una capacidad pendiente válida."; return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID }); }
                if (vm.CantidadPiezas > pendiente) { await tx.RollbackAsync(); TempData["Error"] = $"Solo faltan {pendiente:N0} pieza(s) para completar {etiquetaBlanca}."; return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID }); }
                var disponible = await ObtenerCantidadDisponibleParaCajaAsync(vm.EjecucionProduccionID, ProduccionCajaTipo.Ok, cn, tx);
                if (vm.CantidadPiezas > disponible) { await tx.RollbackAsync(); TempData["Error"] = $"La OF solamente tiene {disponible:N0} pieza(s) OK disponibles para agregar a la caja."; return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID }); }
                var nuevaCantidad = cantidadActual + vm.CantidadPiezas;
                var nuevoPendiente = Math.Max(0, capacidad - nuevaCantidad);
                const string sqlDetalle = @"
INSERT INTO dbo.Produccion_CajaOrigenDetalle
(CajaProduccionID,EjecucionProduccionID,ProgramaProduccionID,SolicitudProduccionID,SolicitudProduccionDetalleID,ReleaseID,ReleaseDetalleID,CantidadPiezas,TipoMovimiento,Observaciones,UsuarioCreacionID,FechaCreacion,Activo)
VALUES
(@CajaProduccionID,@EjecucionProduccionID,@ProgramaProduccionID,@SolicitudProduccionID,@SolicitudProduccionDetalleID,@ReleaseID,@ReleaseDetalleID,@CantidadPiezas,N'COMPLETADO',@Observaciones,@UsuarioID,SYSDATETIME(),1);";
                await using (var cmd = new SqlCommand(sqlDetalle, cn, tx))
                {
                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = vm.CajaProduccionID;
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucion.EjecucionProduccionID;
                    cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = ejecucion.ProgramaProduccionID;
                    cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = (object?)ejecucion.SolicitudProduccionID ?? DBNull.Value;
                    cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = (object?)ejecucion.SolicitudProduccionDetalleID ?? DBNull.Value;
                    cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = (object?)ejecucion.ReleaseID ?? DBNull.Value;
                    cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = (object?)ejecucion.ReleaseDetalleID ?? DBNull.Value;
                    cmd.Parameters.Add("@CantidadPiezas", SqlDbType.Int).Value = vm.CantidadPiezas;
                    cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(vm.Observaciones) ? $"Piezas agregadas por la OF actual para completar {etiquetaBlanca}." : vm.Observaciones.Trim();
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    await cmd.ExecuteNonQueryAsync();
                }
                if (nuevoPendiente > 0)
                {
                    const string sqlParcial = @"
UPDATE dbo.Produccion_Cajas
SET CantidadPiezas=@Cantidad,Cantidad=@Cantidad,CantidadPendienteCompletar=@Pendiente,EstadoProductoIncompleto=N'EN_COMPLETADO',
UsuarioModificacionID=@UsuarioID,FechaModificacion=SYSDATETIME()
WHERE CajaProduccionID=@CajaProduccionID AND Activo=1 AND EsProductoIncompleto=1 AND EjecucionReservaID=@EjecucionProduccionID;
IF @@ROWCOUNT<>1 THROW 51121,'La etiqueta blanca cambió de estado mientras se agregaban las piezas.',1;";
                    await using var cmd = new SqlCommand(sqlParcial, cn, tx);
                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = vm.CajaProduccionID;
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = vm.EjecucionProduccionID;
                    cmd.Parameters.Add("@Cantidad", SqlDbType.Int).Value = nuevaCantidad;
                    cmd.Parameters.Add("@Pendiente", SqlDbType.Int).Value = nuevoPendiente;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    await cmd.ExecuteNonQueryAsync();
                    await tx.CommitAsync();
                    TempData["Success"] = $"Se agregaron {vm.CantidadPiezas:N0} pieza(s) a {etiquetaBlanca}. Ahora contiene {nuevaCantidad:N0}/{capacidad:N0}; faltan {nuevoPendiente:N0}.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                var siguienteNumero = await ObtenerSiguienteNumeroCajaAsync(vm.EjecucionProduccionID, cn, tx);
                var nuevoFolio = CrearFolioCajaOperador(ejecucion, siguienteNumero);
                const string sqlCompleta = @"
UPDATE dbo.Produccion_Cajas
SET EjecucionProduccionID=@EjecucionProduccionID,ProgramaProduccionID=@ProgramaProduccionID,
SolicitudProduccionID=@SolicitudProduccionID,SolicitudProduccionDetalleID=@SolicitudProduccionDetalleID,
ReleaseID=@ReleaseID,ReleaseDetalleID=@ReleaseDetalleID,NumeroCaja=@NumeroCaja,FolioCaja=@FolioCaja,
CantidadPiezas=@Cantidad,Cantidad=@Cantidad,TipoCaja=N'OK',EsProductoIncompleto=0,
EstadoProductoIncompleto=N'COMPLETA',CantidadPendienteCompletar=0,FechaCompletadoIncompleto=SYSDATETIME(),
EstadoCajaID=@EstadoCajaID,EstadoCajaNombre=@EstadoCajaNombre,EstatusCalidad=N'FORMADA',
EtiquetaFolio=@FolioCaja,Etiqueta=@FolioCaja,EtiquetaVerde=0,
UsuarioModificacionID=@UsuarioID,FechaModificacion=SYSDATETIME()
WHERE CajaProduccionID=@CajaProduccionID AND Activo=1 AND EsProductoIncompleto=1 AND EjecucionReservaID=@EjecucionProduccionID;
IF @@ROWCOUNT<>1 THROW 51120,'La etiqueta blanca cambió de estado mientras se completaba.',1;

UPDATE dbo.Planeacion_ProductoIncompletoApartado
SET EstatusID=5,UsuarioAplicacionID=@UsuarioID,FechaAplicacion=SYSDATETIME(),Activo=0,
Observaciones=LEFT(COALESCE(NULLIF(Observaciones,N'')+N' | ',N'')+N'Producto incompleto aplicado y caja completada por ejecución '+CONVERT(NVARCHAR(20),@EjecucionProduccionID)+N'.',500)
WHERE CajaProduccionID=@CajaProduccionID
  AND EjecucionProduccionID=@EjecucionProduccionID
  AND ProgramaProduccionID=@ProgramaProduccionID
  AND Activo=1
  AND EstatusID=4;
IF @@ROWCOUNT<>1 THROW 51122,'No fue posible cerrar el apartado de producto incompleto como aplicado.',1;";
                await using (var cmd = new SqlCommand(sqlCompleta, cn, tx))
                {
                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = vm.CajaProduccionID;
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucion.EjecucionProduccionID;
                    cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = ejecucion.ProgramaProduccionID;
                    cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = (object?)ejecucion.SolicitudProduccionID ?? DBNull.Value;
                    cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = (object?)ejecucion.SolicitudProduccionDetalleID ?? DBNull.Value;
                    cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = (object?)ejecucion.ReleaseID ?? DBNull.Value;
                    cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = (object?)ejecucion.ReleaseDetalleID ?? DBNull.Value;
                    cmd.Parameters.Add("@NumeroCaja", SqlDbType.Int).Value = siguienteNumero;
                    cmd.Parameters.Add("@FolioCaja", SqlDbType.NVarChar, 100).Value = nuevoFolio;
                    cmd.Parameters.Add("@Cantidad", SqlDbType.Int).Value = capacidad;
                    cmd.Parameters.Add("@EstadoCajaID", SqlDbType.Int).Value = ProduccionCajaEstatus.FormadaProduccion;
                    cmd.Parameters.Add("@EstadoCajaNombre", SqlDbType.NVarChar, 100).Value = ProduccionCajaEstatus.Nombre(ProduccionCajaEstatus.FormadaProduccion);
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    await cmd.ExecuteNonQueryAsync();
                }
                await tx.CommitAsync();
                TempData["Success"] = $"Etiqueta blanca {etiquetaBlanca} completada con {capacidad:N0} pieza(s). Ahora es la caja {nuevoFolio} y puede continuar al flujo de Calidad.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible completar la etiqueta blanca: " + ex.Message;
            }
            return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
        }

        private static string NormalizarTipoCajaOperador(string? tipoCaja)
        {
            var valor = string.IsNullOrWhiteSpace(tipoCaja) ? string.Empty : tipoCaja.Trim().ToUpperInvariant();
            if (valor == "OK") return ProduccionCajaTipo.Ok;
            if (valor == "SOSPECHOSA" || valor == "SOSPECHOSO") return ProduccionCajaTipo.Sospechoso;
            if (valor == "SCRAP") return ProduccionCajaTipo.Scrap;
            if (valor == "RETENCION" || valor == "RETENCIÓN") return ProduccionCajaTipo.Retencion;
            if (valor == "INCOMPLETA" || valor == "INCOMPLETO") return ProduccionCajaTipo.Incompleta;
            return string.Empty;
        }

        private static string CrearFolioCajaOperador(ProduccionEjecucionVm ejecucion, int numeroCaja)
        {
            if (ejecucion == null) throw new ArgumentNullException(nameof(ejecucion));
            if (ejecucion.EjecucionProduccionID <= 0) throw new InvalidOperationException("La ejecución de Producción no es válida.");
            if (numeroCaja <= 0) throw new ArgumentOutOfRangeException(nameof(numeroCaja));
            return $"PROD-{ejecucion.EjecucionProduccionID}-C{numeroCaja:000}";
        }

        private async Task CrearOActualizarSolicitudReliberacionCalidadPorParoAsync(int ejecucionProduccionId, int paroId, int duracionMinutos, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            if (ejecucionProduccionId <= 0) throw new ArgumentException("La ejecución de producción no es válida.", nameof(ejecucionProduccionId));
            if (paroId <= 0) throw new ArgumentException("El paro de producción no es válido.", nameof(paroId));
            if (duracionMinutos <= 15) throw new InvalidOperationException("Solo se debe solicitar reliberación cuando el paro sea mayor a 15 minutos.");
            if (usuarioId <= 0) throw new InvalidOperationException("No se pudo identificar al usuario que solicita la reliberación.");

            const string sqlObtenerInspeccion = @"
SELECT TOP (1)
    ci.InspeccionID,
    ISNULL(ci.Estado,N'') AS Estado,
    ci.ChecklistArranqueID,
    ISNULL(ci.ConfiguracionInvalidada,0) AS ConfiguracionInvalidada
FROM dbo.Calidad_Inspecciones ci WITH (UPDLOCK,HOLDLOCK)
WHERE ci.EjecucionProduccionID=@EjecucionProduccionID
  AND ISNULL(ci.Estado,N'')<>N'CERRADA'
ORDER BY ci.InspeccionID DESC;";

            int inspeccionId;
            string estadoAnterior;
            int? checklistArranqueId;
            bool configuracionInvalidada;

            await using (var cmd = new SqlCommand(sqlObtenerInspeccion, cn, tx))
            {
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                await using var rd = await cmd.ExecuteReaderAsync();
                if (!await rd.ReadAsync())
                {
                    throw new InvalidOperationException("No existe una inspección activa de Calidad asociada con esta ejecución. Primero debe completarse y enviarse el checklist de arranque.");
                }

                inspeccionId = Convert.ToInt32(rd["InspeccionID"]);
                estadoAnterior = rd["Estado"]?.ToString()?.Trim() ?? string.Empty;
                checklistArranqueId = rd["ChecklistArranqueID"] == DBNull.Value ? null : Convert.ToInt32(rd["ChecklistArranqueID"]);
                configuracionInvalidada = Convert.ToBoolean(rd["ConfiguracionInvalidada"]);
            }

            if (!checklistArranqueId.HasValue || checklistArranqueId.Value <= 0)
            {
                throw new InvalidOperationException("La inspección de Calidad no tiene un checklist de arranque relacionado.");
            }

            if (configuracionInvalidada)
            {
                throw new InvalidOperationException("La configuración de la inspección fue invalidada por un cambio de Planeación. Debe corregirse esa condición antes de solicitar una reliberación por paro.");
            }

            const string sqlValidarParo = @"
SELECT COUNT(1)
FROM dbo.Produccion_Paros WITH (UPDLOCK,HOLDLOCK)
WHERE ParoID=@ParoID
  AND EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1
  AND FechaFinParo IS NOT NULL
  AND ISNULL(EsMayorA15Minutos,0)=1;";

            await using (var cmd = new SqlCommand(sqlValidarParo, cn, tx))
            {
                cmd.Parameters.Add("@ParoID", SqlDbType.Int).Value = paroId;
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                var total = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                if (total <= 0)
                {
                    throw new InvalidOperationException("El paro no está cerrado, no pertenece a la ejecución o no fue marcado como mayor a 15 minutos.");
                }
            }

            var observacion = $"Solicitud automática de reliberación por paro mayor a 15 minutos. ParoID: {paroId}. Duración registrada: {duracionMinutos} minuto(s).";
            var observacionCancelacion = $"Monitoreo cancelado por interrupción del ciclo. ParoID: {paroId}. Duración: {duracionMinutos} minuto(s). Se generará un nuevo periodo cuando Producción reinicie la serie.";

            const string sqlActualizarInspeccion = @"
UPDATE dbo.Calidad_Inspecciones
SET RequiereReliberacion=1,
    Liberado=0,
    Estado=N'PENDIENTE_RELIBERACION',
    ResultadoCalidad=NULL,
    Etiqueta=NULL,
    CincoDisparosSegregados=0,
    CantidadDisparosConformes=0,
    ValidacionDimensional=NULL,
    ValidacionApariencia=NULL,
    ValidacionGauge=NULL,
    ValidacionConductividad=NULL,
    FechaNotificacionCalidad=GETDATE(),
    UsuarioNotificoID=@UsuarioID,
    MotivoDevolucion=N'Paro mayor a 15 minutos. Se requieren cinco disparos y reliberación de Calidad.',
    Observaciones=CASE
        WHEN Observaciones IS NULL OR LTRIM(RTRIM(Observaciones))=N'' THEN @Observacion
        WHEN Observaciones LIKE N'%ParoID: '+CONVERT(NVARCHAR(20),@ParoID)+N'%' THEN Observaciones
        ELSE Observaciones+CHAR(13)+CHAR(10)+@Observacion
    END,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE InspeccionID=@InspeccionID
  AND EjecucionProduccionID=@EjecucionProduccionID
  AND ISNULL(Estado,N'')<>N'CERRADA';";

            await using (var cmd = new SqlCommand(sqlActualizarInspeccion, cn, tx))
            {
                cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId;
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                cmd.Parameters.Add("@ParoID", SqlDbType.Int).Value = paroId;
                cmd.Parameters.Add("@Observacion", SqlDbType.NVarChar, 1000).Value = observacion;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                var actualizadas = await cmd.ExecuteNonQueryAsync();
                if (actualizadas != 1)
                {
                    throw new InvalidOperationException("No fue posible marcar la inspección como pendiente de reliberación.");
                }
            }
            const string sqlCancelarMonitoreos = @"
UPDATE dbo.Calidad_MonitoreosProceso
SET
    /*
     * Resultado se conserva como PENDIENTE porque CANCELADO
     * no pertenece al catálogo permitido por la restricción
     * CK_CalidadMonitoreos_Resultado.
     *
     * Activo = 0 es lo que retira el periodo del flujo vigente.
     */
    Observaciones =
        CASE
            WHEN Observaciones IS NULL
              OR LTRIM(RTRIM(Observaciones)) = N''
                THEN @ObservacionCancelacion

            WHEN Observaciones LIKE
                 N'%ParoID: '
                 + CONVERT(NVARCHAR(20), @ParoID)
                 + N'%'
                THEN Observaciones

            ELSE
                Observaciones
                + CHAR(13)
                + CHAR(10)
                + @ObservacionCancelacion
        END,

    Activo = 0,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()

WHERE InspeccionID = @InspeccionID
  AND EjecucionProduccionID = @EjecucionProduccionID
  AND Activo = 1
  AND RegistroHoraID IS NULL
  AND UPPER(
        LTRIM(
            RTRIM(
                ISNULL(Resultado, N'')
            )
        )
      ) = N'PENDIENTE';";

            int monitoreosCancelados;
            await using (var cmd = new SqlCommand(sqlCancelarMonitoreos, cn, tx))
            {
                cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId;
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                cmd.Parameters.Add("@ParoID", SqlDbType.Int).Value = paroId;
                cmd.Parameters.Add("@ObservacionCancelacion", SqlDbType.NVarChar, 1000).Value = observacionCancelacion;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                monitoreosCancelados = await cmd.ExecuteNonQueryAsync();
            }

            const string sqlObtenerReliberacion = @"
SELECT TOP (1) ReliberacionID
FROM dbo.Calidad_Reliberaciones WITH (UPDLOCK,HOLDLOCK)
WHERE InspeccionID=@InspeccionID
  AND EjecucionProduccionID=@EjecucionProduccionID
  AND ParoID=@ParoID
  AND Activo=1
ORDER BY ReliberacionID DESC;";

            int? reliberacionId;
            await using (var cmd = new SqlCommand(sqlObtenerReliberacion, cn, tx))
            {
                cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId;
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                cmd.Parameters.Add("@ParoID", SqlDbType.Int).Value = paroId;
                var resultado = await cmd.ExecuteScalarAsync();
                reliberacionId = resultado == null || resultado == DBNull.Value ? null : Convert.ToInt32(resultado);
            }

            if (reliberacionId.HasValue)
            {
                const string sqlActualizarReliberacion = @"
UPDATE dbo.Calidad_Reliberaciones
SET Resultado=N'PENDIENTE',
    FechaSolicitud=GETDATE(),
    FechaValidacion=NULL,
    UsuarioSolicitudID=@UsuarioID,
    UsuarioCalidadID=NULL,
    Observaciones=CASE
        WHEN Observaciones IS NULL OR LTRIM(RTRIM(Observaciones))=N'' THEN @Observacion
        WHEN Observaciones LIKE N'%ParoID: '+CONVERT(NVARCHAR(20),@ParoID)+N'%' THEN Observaciones
        ELSE Observaciones+CHAR(13)+CHAR(10)+@Observacion
    END,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE ReliberacionID=@ReliberacionID
  AND Activo=1;";

                await using var cmd = new SqlCommand(sqlActualizarReliberacion, cn, tx);
                cmd.Parameters.Add("@ReliberacionID", SqlDbType.Int).Value = reliberacionId.Value;
                cmd.Parameters.Add("@ParoID", SqlDbType.Int).Value = paroId;
                cmd.Parameters.Add("@Observacion", SqlDbType.NVarChar, 1000).Value = observacion;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                var actualizadas = await cmd.ExecuteNonQueryAsync();
                if (actualizadas != 1)
                {
                    throw new InvalidOperationException("No fue posible reactivar la solicitud de reliberación.");
                }
            }
            else
            {
                const string sqlInsertarReliberacion = @"
DECLARE @NumeroReliberacion INT;

SELECT @NumeroReliberacion=ISNULL(MAX(NumeroReliberacion),0)+1
FROM dbo.Calidad_Reliberaciones WITH (UPDLOCK,HOLDLOCK)
WHERE EjecucionProduccionID=@EjecucionProduccionID;

INSERT INTO dbo.Calidad_Reliberaciones
(
    InspeccionID,
    EjecucionProduccionID,
    ParoID,
    NumeroReliberacion,
    Motivo,
    FechaSolicitud,
    UsuarioSolicitudID,
    Resultado,
    Observaciones,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
VALUES
(
    @InspeccionID,
    @EjecucionProduccionID,
    @ParoID,
    @NumeroReliberacion,
    @Motivo,
    GETDATE(),
    @UsuarioID,
    N'PENDIENTE',
    @Observaciones,
    @UsuarioID,
    GETDATE(),
    1
);";

                await using var cmd = new SqlCommand(sqlInsertarReliberacion, cn, tx);
                cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId;
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                cmd.Parameters.Add("@ParoID", SqlDbType.Int).Value = paroId;
                cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 500).Value = $"Paro mayor a 15 minutos. Duración registrada: {duracionMinutos} minuto(s).";
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = "Producción debe ejecutar nuevamente cinco disparos de prueba y Calidad debe autorizar la reliberación antes de reiniciar la serie.";
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                await cmd.ExecuteNonQueryAsync();
            }

            const string sqlHistorial = @"
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
    N'SOLICITUD_RELIBERACION',
    @EstadoAnterior,
    N'PENDIENTE_RELIBERACION',
    NULL,
    NULL,
    @ComentarioReliberacion,
    @UsuarioID,
    GETDATE()
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.Calidad_InspeccionHistorial h
    WHERE h.InspeccionID=@InspeccionID
      AND h.Movimiento=N'SOLICITUD_RELIBERACION'
      AND h.Comentario LIKE N'%ParoID: '+CONVERT(NVARCHAR(20),@ParoID)+N'%'
);

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
    N'CICLO_MONITOREO_INTERRUMPIDO',
    @EstadoAnterior,
    N'PENDIENTE_RELIBERACION',
    NULL,
    NULL,
    @ComentarioCiclo,
    @UsuarioID,
    GETDATE()
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.Calidad_InspeccionHistorial h
    WHERE h.InspeccionID=@InspeccionID
      AND h.Movimiento=N'CICLO_MONITOREO_INTERRUMPIDO'
      AND h.Comentario LIKE N'%ParoID: '+CONVERT(NVARCHAR(20),@ParoID)+N'%'
);";

            var comentarioReliberacion = $"{observacion} Producción regresó a preparación y queda bloqueada hasta la autorización de Calidad.";
            var comentarioCiclo = $"Ciclo de monitoreo interrumpido. ParoID: {paroId}. Se cancelaron {monitoreosCancelados} monitoreo(s) pendiente(s) sin captura. Los monitoreos vinculados o revisados conservaron su trazabilidad.";

            await using (var cmd = new SqlCommand(sqlHistorial, cn, tx))
            {
                cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId;
                cmd.Parameters.Add("@ParoID", SqlDbType.Int).Value = paroId;
                cmd.Parameters.Add("@EstadoAnterior", SqlDbType.NVarChar, 50).Value = string.IsNullOrWhiteSpace(estadoAnterior) ? DBNull.Value : estadoAnterior;
                cmd.Parameters.Add("@ComentarioReliberacion", SqlDbType.NVarChar, 1000).Value = comentarioReliberacion.Length > 1000 ? comentarioReliberacion[..1000] : comentarioReliberacion;
                cmd.Parameters.Add("@ComentarioCiclo", SqlDbType.NVarChar, 1000).Value = comentarioCiclo.Length > 1000 ? comentarioCiclo[..1000] : comentarioCiclo;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private async Task<List<ProduccionOperadorTabletVm>>
    ObtenerProgramasEnProduccionAsync(
        int personaId,
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

    dt.ObjetivoHora,
    dt.Ciclo,
    dt.Cavidades,

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

OUTER APPLY
(
    SELECT TOP (1)
        dt0.ObjetivoHora,
        dt0.Ciclo,
        dt0.Cavidades
    FROM dbo.ERP_ParteDatosTecnicos dt0
    WHERE dt0.ParteID = e.ParteID
      AND dt0.Activo = 1
    ORDER BY dt0.ParteDatoTecnicoID DESC
) dt

WHERE e.Activo = 1
  AND e.EstatusID IN (@EnProduccion, @Pausado)
  AND EXISTS
  (
      SELECT 1
      FROM dbo.Planeacion_ProgramaOperadores po
      WHERE po.ProgramaProduccionID = e.ProgramaProduccionID
        AND po.PersonaID = @PersonaID
        AND po.Activo = 1
        AND UPPER(LTRIM(RTRIM(ISNULL(po.RolOperador,N''))))
            IN (N'PRINCIPAL',N'AUXILIAR')
  )

ORDER BY
    e.MaquinaCodigo,
    ISNULL(pp.FechaInicioProgramada, e.FechaInicioReal),
    e.EjecucionProduccionID;";

            await using var cmd = new SqlCommand(sql, cn);

            cmd.Parameters.Add(
                "@PersonaID",
                SqlDbType.Int).Value = personaId;

            cmd.Parameters.Add(
                "@EnProduccion",
                SqlDbType.Int).Value =
                ProduccionEstatus.EnProduccion;

            cmd.Parameters.Add(
                "@Pausado",
                SqlDbType.Int).Value =
                ProduccionEstatus.Pausado;

            await using var rd =
                await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var vm = MapearTabletVm(rd);

                AsignarHoraSugerida(vm);

                lista.Add(vm);
            }

            return lista;
        }

        private async Task<List<ProduccionCambioTurnoHistorialVm>> ObtenerHistorialCambiosTurnoAsync(int ejecucionProduccionId, SqlConnection cn, SqlTransaction? tx = null)
        {
            const string sql = @"
SELECT
    ct.CambioTurnoID,
    ct.OperadorSalienteID,
    LTRIM(RTRIM(CONCAT(ISNULL(ps.Nombre,N''),N' ',ISNULL(ps.ApellidoPaterno,N''),N' ',ISNULL(ps.ApellidoMaterno,N'')))) AS OperadorSalienteNombre,
    ct.OperadorEntranteID,
    LTRIM(RTRIM(CONCAT(ISNULL(pe.Nombre,N''),N' ',ISNULL(pe.ApellidoPaterno,N''),N' ',ISNULL(pe.ApellidoMaterno,N'')))) AS OperadorEntranteNombre,
    ct.TurnoSalienteNombre,
    ct.TurnoEntranteNombre,
    ct.FechaEntrega,
    ct.FechaRecepcion,
    ct.EstadoCambioTurno,
    ct.OrigenOperadorEntrante,
    ISNULL(ct.CantidadOKAcumulada,0) AS CantidadOK,
    ISNULL(ct.CantidadSospechosaAcumulada,0) AS CantidadSospechosa,
    ISNULL(ct.CantidadScrapAcumulada,0) AS CantidadScrap,
    ISNULL(ct.TotalCajasFormadas,0) AS TotalCajas,
    ISNULL(ct.TotalCajasEntregadas,0) AS TotalCajasEntregadas,
    ISNULL(ct.TotalCajasPendientes,0) AS TotalCajasPendientes,
    ct.Observaciones,
    ct.ObservacionesRecepcion
FROM dbo.Produccion_CambiosTurno ct
LEFT JOIN dbo.Persona ps ON ps.PersonaID=ct.OperadorSalienteID
LEFT JOIN dbo.Persona pe ON pe.PersonaID=ct.OperadorEntranteID
WHERE ct.EjecucionProduccionID=@EjecucionProduccionID
  AND ct.Activo=1
ORDER BY ct.FechaEntrega,ct.CambioTurnoID;";
            var lista = new List<ProduccionCambioTurnoHistorialVm>();
            await using var cmd = tx == null ? new SqlCommand(sql, cn) : new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                lista.Add(new ProduccionCambioTurnoHistorialVm
                {
                    CambioTurnoID = Convert.ToInt32(rd["CambioTurnoID"]),
                    OperadorSalienteID = Convert.ToInt32(rd["OperadorSalienteID"]),
                    OperadorSalienteNombre = rd["OperadorSalienteNombre"]?.ToString()?.Trim() ?? string.Empty,
                    OperadorEntranteID = Convert.ToInt32(rd["OperadorEntranteID"]),
                    OperadorEntranteNombre = rd["OperadorEntranteNombre"]?.ToString()?.Trim() ?? string.Empty,
                    TurnoSalienteNombre = rd["TurnoSalienteNombre"] == DBNull.Value ? null : rd["TurnoSalienteNombre"].ToString(),
                    TurnoEntranteNombre = rd["TurnoEntranteNombre"] == DBNull.Value ? null : rd["TurnoEntranteNombre"].ToString(),
                    FechaEntrega = Convert.ToDateTime(rd["FechaEntrega"]),
                    FechaRecepcion = rd["FechaRecepcion"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaRecepcion"]),
                    Estado = rd["EstadoCambioTurno"]?.ToString()?.Trim() ?? string.Empty,
                    OrigenOperadorEntrante = rd["OrigenOperadorEntrante"] == DBNull.Value ? null : rd["OrigenOperadorEntrante"].ToString(),
                    CantidadOK = Convert.ToInt32(rd["CantidadOK"]),
                    CantidadSospechosa = Convert.ToInt32(rd["CantidadSospechosa"]),
                    CantidadScrap = Convert.ToInt32(rd["CantidadScrap"]),
                    TotalCajas = Convert.ToInt32(rd["TotalCajas"]),
                    TotalCajasEntregadas = Convert.ToInt32(rd["TotalCajasEntregadas"]),
                    TotalCajasPendientes = Convert.ToInt32(rd["TotalCajasPendientes"]),
                    ObservacionesEntrega = rd["Observaciones"] == DBNull.Value ? null : rd["Observaciones"].ToString(),
                    ObservacionesRecepcion = rd["ObservacionesRecepcion"] == DBNull.Value ? null : rd["ObservacionesRecepcion"].ToString()
                });
            }
            return lista;
        }

        private static List<ProduccionHistorialTurnoVm> ConstruirHistorialTurnos(List<ProduccionCapturaHoraFilaVm> horas, List<ProduccionCambioTurnoHistorialVm> cambios)
        {
            var resultado = new List<ProduccionHistorialTurnoVm>();
            var capturadas = horas.Where(x => x.Capturada).OrderBy(x => ObtenerInicioHora(x)).ThenBy(x => x.NumeroHora).ToList();
            if (capturadas.Count == 0) return resultado;
            var segmentos = new List<List<ProduccionCapturaHoraFilaVm>>();
            List<ProduccionCapturaHoraFilaVm>? segmentoActual = null;
            int? operadorAnterior = null;
            foreach (var fila in capturadas)
            {
                var cambioOperador = segmentoActual == null || fila.OperadorID != operadorAnterior;
                if (cambioOperador)
                {
                    segmentoActual = new List<ProduccionCapturaHoraFilaVm>();
                    segmentos.Add(segmentoActual);
                    operadorAnterior = fila.OperadorID;
                }
                segmentoActual!.Add(fila);
            }
            for (var i = 0; i < segmentos.Count; i++)
            {
                var filas = segmentos[i];
                var primera = filas.First();
                var ultima = filas.Last();
                var operadorId = primera.OperadorID ?? 0;
                var inicioHoras = ObtenerInicioHora(primera);
                var finHoras = ObtenerFinHora(ultima);
                ProduccionCambioTurnoHistorialVm? cambioEntrada = null;
                ProduccionCambioTurnoHistorialVm? cambioSalida = null;
                if (operadorId > 0)
                {
                    cambioEntrada = cambios.Where(x => x.OperadorEntranteID == operadorId && x.FechaRecepcion.HasValue && x.FechaRecepcion.Value <= finHoras).OrderByDescending(x => x.FechaRecepcion).FirstOrDefault();
                    cambioSalida = cambios.Where(x => x.OperadorSalienteID == operadorId && x.FechaEntrega >= inicioHoras).OrderBy(x => x.FechaEntrega).FirstOrDefault();
                }
                var fechaInicio = cambioEntrada?.FechaRecepcion ?? inicioHoras;
                var fechaFin = cambioSalida?.FechaRecepcion ?? cambioSalida?.FechaEntrega;
                if (fechaFin.HasValue && fechaFin.Value < fechaInicio) fechaFin = null;
                var objetivoTotal = filas.Sum(x => x.ObjetivoBloque ?? x.ObjetivoHora ?? 0);
                var cantidadOK = filas.Sum(x => x.CantidadOK);
                var cantidadSospechosa = filas.Sum(x => x.CantidadSospechosa);
                var cantidadScrap = filas.Sum(x => x.CantidadScrap);
                var porcentaje = objetivoTotal > 0 ? Math.Round(cantidadOK * 100m / objetivoTotal, 1) : 0m;
                string turnoNombre;
                if (!string.IsNullOrWhiteSpace(cambioEntrada?.TurnoEntranteNombre)) turnoNombre = cambioEntrada!.TurnoEntranteNombre!;
                else if (!string.IsNullOrWhiteSpace(cambioSalida?.TurnoSalienteNombre)) turnoNombre = cambioSalida!.TurnoSalienteNombre!;
                else turnoNombre = NombreTurnoSecuencial(i + 1);
                resultado.Add(new ProduccionHistorialTurnoVm
                {
                    NumeroTurno = i + 1,
                    TurnoNombre = turnoNombre,
                    OperadorID = operadorId,
                    OperadorNombre = !string.IsNullOrWhiteSpace(primera.OperadorNombre) ? primera.OperadorNombre! : "Sin operador",
                    FechaInicio = fechaInicio,
                    FechaFin = fechaFin,
                    CantidadOK = cantidadOK,
                    CantidadSospechosa = cantidadSospechosa,
                    CantidadScrap = cantidadScrap,
                    ObjetivoTotal = objetivoTotal,
                    PorcentajeCumplimiento = porcentaje,
                    CumplioObjetivo = objetivoTotal > 0 && cantidadOK >= objetivoTotal,
                    Horas = filas
                });
            }
            return resultado;
        }
        private static DateTime ObtenerInicioHora(ProduccionCapturaHoraFilaVm fila)
        {
            return fila.FechaProduccion.Date.Add(fila.HoraInicio);
        }
        private static DateTime ObtenerFinHora(ProduccionCapturaHoraFilaVm fila)
        {
            var inicio = ObtenerInicioHora(fila);
            var fin = fila.FechaProduccion.Date.Add(fila.HoraFin);
            if (fin <= inicio) fin = fin.AddDays(1);
            return fin;
        }
        private static string NombreTurnoSecuencial(int numero)
        {
            return numero switch
            {
                1 => "1er Turno",
                2 => "2do Turno",
                3 => "3er Turno",
                _ => $"Turno {numero}"
            };
        }
        private async Task<ProduccionOperadorTabletVm?>
       ObtenerTabletVmAsync(
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

    dt.ObjetivoHora,
    dt.Ciclo,
    dt.Cavidades,

    e.EstatusID,

    CASE
        WHEN EXISTS
        (
            SELECT 1
            FROM dbo.Produccion_Paros p
            WHERE p.EjecucionProduccionID =
                  e.EjecucionProduccionID
              AND p.Activo = 1
              AND p.FechaFinParo IS NULL
        )
        THEN 1
        ELSE 0
    END AS TieneParoAbierto,

    (
        SELECT TOP (1)
            p.ParoID
        FROM dbo.Produccion_Paros p
        WHERE p.EjecucionProduccionID =
              e.EjecucionProduccionID
          AND p.Activo = 1
          AND p.FechaFinParo IS NULL
        ORDER BY p.ParoID DESC
    ) AS ParoAbiertoID

FROM dbo.Produccion_Ejecucion e

LEFT JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID =
       e.SolicitudProduccionID

OUTER APPLY
(
    SELECT TOP (1)
        dt0.ObjetivoHora,
        dt0.Ciclo,
        dt0.Cavidades
    FROM dbo.ERP_ParteDatosTecnicos dt0
    WHERE dt0.ParteID = e.ParteID
      AND dt0.Activo = 1
    ORDER BY dt0.ParteDatoTecnicoID DESC
) dt

WHERE e.EjecucionProduccionID =
      @EjecucionProduccionID
  AND e.Activo = 1
  AND e.EstatusID IN (
      @EnProduccion,
      @Pausado
  );";

            await using var cmd =
                new SqlCommand(sql, cn);

            cmd.Parameters.Add(
                "@EjecucionProduccionID",
                SqlDbType.Int).Value =
                ejecucionProduccionId;

            cmd.Parameters.Add(
                "@EnProduccion",
                SqlDbType.Int).Value =
                ProduccionEstatus.EnProduccion;

            cmd.Parameters.Add(
                "@Pausado",
                SqlDbType.Int).Value =
                ProduccionEstatus.Pausado;

            await using var rd =
                await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return null;

            var vm =
                MapearTabletVm(rd);

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

        private async Task<ProduccionEjecucionVm?> ObtenerEjecucionOperadorAsync(int ejecucionProduccionId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
SELECT
    EjecucionProduccionID,ProgramaProduccionID,SolicitudProduccionID,SolicitudProduccionDetalleID,ReleaseID,ReleaseDetalleID,
    MaquinaID,MaquinaCodigo,MaquinaNombre,ParteID,NumeroParte,ReferenciaSAP,DescripcionParte,MoldeID,MoldeCodigo,
    OperadorID,OperadorNombre,FechaInicioReal,FechaFinReal,
    FechaLiberacionMaquina,UsuarioLiberacionMaquinaID,ObservacionesLiberacionMaquina,
    CantidadPlaneada,CantidadOKTotal,CantidadSospechosaTotal,CantidadScrapTotal,
    EstatusID,Observaciones,UsuarioCreacionID,FechaCreacion,UsuarioModificacionID,FechaModificacion,Activo
FROM dbo.Produccion_Ejecucion
WHERE EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
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
                FechaLiberacionMaquina = NullableFecha(rd, "FechaLiberacionMaquina"),
                UsuarioLiberacionMaquinaID = NullableEntero(rd, "UsuarioLiberacionMaquinaID"),
                ObservacionesLiberacionMaquina = TextoNullable(rd, "ObservacionesLiberacionMaquina"),
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

        private async Task<int> InsertarRegistroHoraAsync(
    ProduccionEjecucionVm ejecucion,
    ProduccionRegistroHoraPostVm vm,
    TimeSpan horaInicio,
    TimeSpan horaFin,
    int operadorPersonaId,
    int usuarioId,
    CalculoProduccionContadorHora calculo,
    SqlConnection cn,
    SqlTransaction tx)
        {
            bool? cumplioObjetivo = null;
            int? diferenciaObjetivo = null;
            decimal? porcentajeCumplimiento = null;

            if (calculo.ObjetivoBloque > 0)
            {
                diferenciaObjetivo =
                    vm.CantidadOK -
                    calculo.ObjetivoBloque;

                cumplioObjetivo =
                    vm.CantidadOK >=
                    calculo.ObjetivoBloque;

                porcentajeCumplimiento =
                    Math.Round(
                        (decimal)vm.CantidadOK *
                        100m /
                        calculo.ObjetivoBloque,
                        2);
            }

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

    ObjetivoHora,
    ObjetivoBloque,
    CumplioObjetivo,
    DiferenciaObjetivo,
    PorcentajeCumplimiento,

    PiezasCalculadasContador,
    MinutosProductivos,
    EsTiempoExtra,
    TipoBloque,
    TieneCambioConfiguracion,
    TieneReinicioContador,

    Observaciones,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
OUTPUT INSERTED.RegistroHoraID
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

    @ObjetivoHora,
    @ObjetivoBloque,
    @CumplioObjetivo,
    @DiferenciaObjetivo,
    @PorcentajeCumplimiento,

    @PiezasCalculadasContador,
    @MinutosProductivos,
    @EsTiempoExtra,
    @TipoBloque,
    @TieneCambioConfiguracion,
    @TieneReinicioContador,

    @Observaciones,
    @UsuarioID,
    SYSDATETIME(),
    1
);";

            await using var cmd =
                new SqlCommand(
                    sql,
                    cn,
                    tx);

            cmd.Parameters.Add(
                "@EjecucionProduccionID",
                SqlDbType.Int).Value =
                ejecucion.EjecucionProduccionID;

            cmd.Parameters.Add(
                "@ProgramaProduccionID",
                SqlDbType.Int).Value =
                ejecucion.ProgramaProduccionID;

            cmd.Parameters.Add(
                "@SolicitudProduccionID",
                SqlDbType.Int).Value =
                (object?)ejecucion.SolicitudProduccionID ??
                DBNull.Value;

            cmd.Parameters.Add(
                "@MaquinaID",
                SqlDbType.Int).Value =
                (object?)ejecucion.MaquinaID ??
                DBNull.Value;

            cmd.Parameters.Add(
                "@OperadorID",
                SqlDbType.Int).Value =
                operadorPersonaId;

            cmd.Parameters.Add(
                "@FechaProduccion",
                SqlDbType.Date).Value =
                vm.FechaProduccion.Date;

            cmd.Parameters.Add(
                "@HoraInicio",
                SqlDbType.Time).Value =
                horaInicio;

            cmd.Parameters.Add(
                "@HoraFin",
                SqlDbType.Time).Value =
                horaFin;

            cmd.Parameters.Add(
                "@CantidadOK",
                SqlDbType.Int).Value =
                vm.CantidadOK;

            cmd.Parameters.Add(
                "@CantidadSospechosa",
                SqlDbType.Int).Value =
                vm.CantidadSospechosa;

            cmd.Parameters.Add(
                "@CantidadScrap",
                SqlDbType.Int).Value =
                vm.CantidadScrap;

            cmd.Parameters.Add(
                "@ObjetivoHora",
                SqlDbType.Int).Value =
                calculo.ObjetivoHora;

            cmd.Parameters.Add(
                "@ObjetivoBloque",
                SqlDbType.Int).Value =
                calculo.ObjetivoBloque;

            cmd.Parameters.Add(
                "@CumplioObjetivo",
                SqlDbType.Bit).Value =
                (object?)cumplioObjetivo ??
                DBNull.Value;

            cmd.Parameters.Add(
                "@DiferenciaObjetivo",
                SqlDbType.Int).Value =
                (object?)diferenciaObjetivo ??
                DBNull.Value;

            var pPorcentaje =
                cmd.Parameters.Add(
                    "@PorcentajeCumplimiento",
                    SqlDbType.Decimal);

            pPorcentaje.Precision = 8;
            pPorcentaje.Scale = 2;
            pPorcentaje.Value =
                (object?)porcentajeCumplimiento ??
                DBNull.Value;

            cmd.Parameters.Add(
                "@PiezasCalculadasContador",
                SqlDbType.Int).Value =
                calculo.PiezasCalculadas;

            var pMinutos =
                cmd.Parameters.Add(
                    "@MinutosProductivos",
                    SqlDbType.Decimal);

            pMinutos.Precision = 10;
            pMinutos.Scale = 2;
            pMinutos.Value =
                calculo.MinutosProductivos;

            cmd.Parameters.Add(
                "@EsTiempoExtra",
                SqlDbType.Bit).Value =
                vm.EsTiempoExtra;

            cmd.Parameters.Add(
                "@TipoBloque",
                SqlDbType.NVarChar,
                30).Value =
                vm.EsTiempoExtra
                    ? "TIEMPO_EXTRA"
                    : "NORMAL";

            cmd.Parameters.Add(
                "@TieneCambioConfiguracion",
                SqlDbType.Bit).Value =
                calculo.TieneCambioConfiguracion;

            cmd.Parameters.Add(
                "@TieneReinicioContador",
                SqlDbType.Bit).Value =
                calculo.TieneReinicioContador;

            cmd.Parameters.Add(
                "@Observaciones",
                SqlDbType.NVarChar,
                500).Value =
                string.IsNullOrWhiteSpace(
                    vm.Observaciones)
                    ? DBNull.Value
                    : vm.Observaciones.Trim();

            cmd.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value =
                usuarioId;

            var resultado =
                await cmd.ExecuteScalarAsync();

            if (resultado == null ||
                resultado == DBNull.Value)
            {
                throw new InvalidOperationException(
                    "No fue posible obtener el identificador del registro horario.");
            }

            return Convert.ToInt32(
                resultado);
        }

        private static string ConstruirMensajeCumplimientoHora(int numeroHora, int cantidadOK, int cantidadSospechosa, int cantidadScrap, long contadorActual, CalculoProduccionContadorHora calculo)
        {
            var mensaje = $"Hora {numeroHora} guardada. Contador: {contadorActual:N0}. Producción física: {calculo.PiezasCalculadas:N0} pieza(s). OK calculadas automáticamente: {cantidadOK:N0}; sospechosas: {cantidadSospechosa:N0}; posible scrap: {cantidadScrap:N0}.";

            if (calculo.ObjetivoBloque > 0)
            {
                var porcentaje = Math.Round((decimal)cantidadOK * 100m / calculo.ObjetivoBloque, 1);
                var diferencia = cantidadOK - calculo.ObjetivoBloque;

                if (diferencia > 0)
                    mensaje += $" Objetivo: {calculo.ObjetivoBloque:N0}. Cumplimiento {porcentaje:0.#}%. Superaste el objetivo por {diferencia:N0} pieza(s) OK.";
                else if (diferencia == 0)
                    mensaje += $" Objetivo: {calculo.ObjetivoBloque:N0}. Cumplimiento 100%.";
                else
                    mensaje += $" Objetivo: {calculo.ObjetivoBloque:N0}. Cumplimiento {porcentaje:0.#}%. Faltaron {Math.Abs(diferencia):N0} pieza(s) OK.";
            }

            if (cantidadOK > 0)
                mensaje += $" Se abonaron provisionalmente +{cantidadOK:N0} pieza(s) OK al bonus del operador.";

            if (cantidadSospechosa > 0 || cantidadScrap > 0)
                mensaje += " El material sospechoso o posible scrap queda pendiente de resolución por Calidad.";

            if (calculo.TieneReinicioContador)
                mensaje += " Se detectó un reinicio del contador de máquina.";

            return mensaje;
        }
        private static async Task VincularRegistroHoraConCalidadAsync(ProduccionEjecucionVm ejecucion, ProduccionRegistroHoraPostVm vm, TimeSpan horaInicio, TimeSpan horaFin, int registroHoraId, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            if (ejecucion == null) throw new ArgumentNullException(nameof(ejecucion));
            if (vm == null) throw new ArgumentNullException(nameof(vm));
            if (ejecucion.EjecucionProduccionID <= 0) throw new InvalidOperationException("La ejecución de Producción no es válida.");
            if (registroHoraId <= 0) throw new InvalidOperationException("El registro horario no es válido.");
            if (usuarioId <= 0) throw new InvalidOperationException("No se pudo identificar al usuario que capturó la hora.");
            var fechaHoraInicio = vm.FechaProduccion.Date.Add(horaInicio);
            var fechaHoraFin = vm.FechaProduccion.Date.Add(horaFin);
            if (fechaHoraFin <= fechaHoraInicio) fechaHoraFin = fechaHoraFin.AddDays(1);
            var cantidadPeriodo = vm.CantidadOK + vm.CantidadSospechosa + vm.CantidadScrap;
            var cantidadPendienteRevision = vm.CantidadSospechosa + vm.CantidadScrap;
            const string sql = @"
DECLARE @InspeccionID INT;
DECLARE @EstadoInspeccion NVARCHAR(50);
DECLARE @MonitoreoID INT;
DECLARE @RegistroHoraVinculado INT;
SELECT TOP(1)
    @InspeccionID=ci.InspeccionID,
    @EstadoInspeccion=UPPER(LTRIM(RTRIM(ISNULL(ci.Estado,N''))))
FROM dbo.Calidad_Inspecciones ci WITH(UPDLOCK,HOLDLOCK)
WHERE ci.EjecucionProduccionID=@EjecucionProduccionID
  AND ISNULL(ci.Estado,N'')<>N'CERRADA'
ORDER BY ci.InspeccionID DESC;
IF @InspeccionID IS NULL
    THROW 51050,'No existe una inspección activa de Calidad para la ejecución.',1;
IF @EstadoInspeccion<>N'MONITOREO_ACTIVO'
    THROW 51051,'La inspección de Calidad no se encuentra en monitoreo activo.',1;
SELECT TOP(1)
    @MonitoreoID=m.MonitoreoID,
    @RegistroHoraVinculado=m.RegistroHoraID
FROM dbo.Calidad_MonitoreosProceso m WITH(UPDLOCK,HOLDLOCK)
WHERE m.InspeccionID=@InspeccionID
  AND m.EjecucionProduccionID=@EjecucionProduccionID
  AND m.RegistroHoraID=@RegistroHoraID
  AND m.Activo=1
ORDER BY m.MonitoreoID DESC;
IF @MonitoreoID IS NULL
BEGIN
    SELECT TOP(1)
        @MonitoreoID=m.MonitoreoID,
        @RegistroHoraVinculado=m.RegistroHoraID
    FROM dbo.Calidad_MonitoreosProceso m WITH(UPDLOCK,HOLDLOCK)
    WHERE m.InspeccionID=@InspeccionID
      AND m.EjecucionProduccionID=@EjecucionProduccionID
      AND m.Activo=1
      AND m.RegistroHoraID IS NULL
      AND UPPER(LTRIM(RTRIM(ISNULL(m.Resultado,N''))))=N'PENDIENTE'
    ORDER BY m.NumeroHora,m.FechaHoraProgramada,m.MonitoreoID;
END;
IF @MonitoreoID IS NULL
    THROW 51052,'No existe un monitoreo horario pendiente para vincular la captura de Producción.',1;
IF @RegistroHoraVinculado IS NOT NULL AND @RegistroHoraVinculado<>@RegistroHoraID
    THROW 51053,'El monitoreo seleccionado ya se encuentra vinculado con otra captura horaria.',1;
IF EXISTS
(
    SELECT 1
    FROM dbo.Calidad_MonitoreosProceso m WITH(UPDLOCK,HOLDLOCK)
    WHERE m.RegistroHoraID=@RegistroHoraID
      AND m.MonitoreoID<>@MonitoreoID
      AND m.Activo=1
)
    THROW 51054,'La captura horaria ya se encuentra vinculada con otro monitoreo de Calidad.',1;
UPDATE dbo.Calidad_MonitoreosProceso
SET RegistroHoraID=@RegistroHoraID,
    CantidadProducidaPeriodo=@CantidadProducidaPeriodo,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=SYSDATETIME()
WHERE MonitoreoID=@MonitoreoID
  AND InspeccionID=@InspeccionID
  AND EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1
  AND (RegistroHoraID IS NULL OR RegistroHoraID=@RegistroHoraID);
IF @@ROWCOUNT<>1
    THROW 51055,'El monitoreo cambió de estado mientras se vinculaba la captura horaria.',1;
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
    N'CAPTURA_HORARIA_RECIBIDA',
    N'MONITOREO_ACTIVO',
    N'MONITOREO_ACTIVO',
    CASE WHEN @CantidadPendienteRevision>0 THEN N'PENDIENTE_REVISION' ELSE NULL END,
    CASE WHEN @CantidadPendienteRevision>0 THEN N'AMARILLA' ELSE NULL END,
    CONCAT(
        N'Captura horaria recibida desde Producción. RegistroHoraID: ',@RegistroHoraID,
        N'. Periodo: ',CONVERT(NVARCHAR(19),@FechaHoraInicio,120),
        N' a ',CONVERT(NVARCHAR(19),@FechaHoraFin,120),
        N'. OK: ',@CantidadOK,
        N'. Sospechoso: ',@CantidadSospechosa,
        N'. Scrap reportado: ',@CantidadScrap,
        CASE WHEN @CantidadPendienteRevision>0 THEN N'. Material segregado pendiente de revisión por Calidad.' ELSE N'.' END
    ),
    @UsuarioID,
    SYSDATETIME()
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.Calidad_InspeccionHistorial h
    WHERE h.InspeccionID=@InspeccionID
      AND h.Movimiento=N'CAPTURA_HORARIA_RECIBIDA'
      AND h.Comentario LIKE N'%RegistroHoraID: '+CONVERT(NVARCHAR(20),@RegistroHoraID)+N'%'
);";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucion.EjecucionProduccionID;
            cmd.Parameters.Add("@RegistroHoraID", SqlDbType.Int).Value = registroHoraId;
            cmd.Parameters.Add("@FechaHoraInicio", SqlDbType.DateTime2).Value = fechaHoraInicio;
            cmd.Parameters.Add("@FechaHoraFin", SqlDbType.DateTime2).Value = fechaHoraFin;
            cmd.Parameters.Add("@CantidadProducidaPeriodo", SqlDbType.Int).Value = cantidadPeriodo;
            cmd.Parameters.Add("@CantidadOK", SqlDbType.Int).Value = vm.CantidadOK;
            cmd.Parameters.Add("@CantidadSospechosa", SqlDbType.Int).Value = vm.CantidadSospechosa;
            cmd.Parameters.Add("@CantidadScrap", SqlDbType.Int).Value = vm.CantidadScrap;
            cmd.Parameters.Add("@CantidadPendienteRevision", SqlDbType.Int).Value = cantidadPendienteRevision;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
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
                   puesto.Contains("OPERADOR", StringComparison.OrdinalIgnoreCase) ||
                   puesto.Equals("AUXILIAR DE PRODUCCION", StringComparison.OrdinalIgnoreCase);
        }


        private async Task<List<ProduccionAlertaProximoProgramaVm>> ObtenerAlertasProximosProgramasAsync(int personaId, SqlConnection cn, int minutosAntes)
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
          AND UPPER(LTRIM(RTRIM(ISNULL(po.RolOperador, '')))) = 'PRINCIPAL'
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
          AND UPPER(LTRIM(RTRIM(ISNULL(po.RolOperador, '')))) = 'AUXILIAR'
        ORDER BY po.ProgramaOperadorID
    ) opAuxiliar
    WHERE pp.Activo = 1
      AND pp.MaquinaID IS NOT NULL
      AND ISNULL(pp.EstatusID, 1) IN
      (
          @EstatusPendiente,
          @EstatusEnPreparacion
      )
      AND EXISTS
      (
          SELECT 1
          FROM dbo.Planeacion_ProgramaOperadores poFiltro
          WHERE poFiltro.ProgramaProduccionID = pp.ProgramaProduccionID
            AND poFiltro.PersonaID = @PersonaID
            AND poFiltro.Activo = 1
            AND UPPER(LTRIM(RTRIM(ISNULL(poFiltro.RolOperador, ''))))
                IN ('PRINCIPAL', 'AUXILIAR')
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
            cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = personaId;
            cmd.Parameters.Add("@MinutosAntes", SqlDbType.Int).Value = minutosAntes;
            cmd.Parameters.Add("@EstatusPendiente", SqlDbType.Int).Value = ProgramaProduccionEstatus.Pendiente;
            cmd.Parameters.Add("@EstatusEnPreparacion", SqlDbType.Int).Value = ProgramaProduccionEstatus.EnPreparacion;
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
                "Acceso denegado. Esta pantalla es exclusiva para usuarios OPERADOR o AUXILIAR DE PRODUCCION activos.",
                "text/plain");
        }
        private static decimal? DecimalFlexibleProduccion(SqlDataReader rd, string columna)
        {
            var valor = rd[columna];
            if (valor == null || valor == DBNull.Value)
                return null;

            if (valor is decimal d)
                return d;

            if (valor is int i)
                return i;

            if (valor is long l)
                return l;

            if (valor is double db)
                return Convert.ToDecimal(db);

            var texto = valor.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(texto))
                return null;

            if (decimal.TryParse(texto, out var directo))
                return directo;

            var match = System.Text.RegularExpressions.Regex.Match(
                texto,
                @"[-+]?\d+(?:[\.,]\d+)?");

            if (!match.Success)
                return null;

            var numero = match.Value.Replace(',', '.');

            return decimal.TryParse(
                numero,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture,
                out var convertido)
                    ? convertido
                    : null;
        }



        private static ProduccionOperadorTabletVm
    MapearTabletVm(
        SqlDataReader rd)
        {
            return new ProduccionOperadorTabletVm
            {
                EjecucionProduccionID =
                    Entero(
                        rd,
                        "EjecucionProduccionID"),

                ProgramaProduccionID =
                    Entero(
                        rd,
                        "ProgramaProduccionID"),

                SolicitudProduccionID =
                    NullableEntero(
                        rd,
                        "SolicitudProduccionID"),

                FolioSolicitud =
                    TextoNullable(
                        rd,
                        "FolioSolicitud"),

                NumeroOFRecibida =
                    TextoNullable(
                        rd,
                        "NumeroOFRecibida"),

                MaquinaID =
                    NullableEntero(
                        rd,
                        "MaquinaID"),

                MaquinaCodigo =
                    TextoNullable(
                        rd,
                        "MaquinaCodigo"),

                MaquinaNombre =
                    TextoNullable(
                        rd,
                        "MaquinaNombre"),

                ParteID =
                    NullableEntero(
                        rd,
                        "ParteID"),

                NumeroParte =
                    TextoNullable(
                        rd,
                        "NumeroParte"),

                ReferenciaSAP =
                    TextoNullable(
                        rd,
                        "ReferenciaSAP"),

                DescripcionParte =
                    TextoNullable(
                        rd,
                        "DescripcionParte"),

                OperadorID =
                    NullableEntero(
                        rd,
                        "OperadorID"),

                OperadorNombre =
                    TextoNullable(
                        rd,
                        "OperadorNombre"),

                CantidadPlaneada =
                    NullableEntero(
                        rd,
                        "CantidadPlaneada"),

                CantidadOKTotal =
                    Entero(
                        rd,
                        "CantidadOKTotal"),

                CantidadSospechosaTotal =
                    Entero(
                        rd,
                        "CantidadSospechosaTotal"),

                CantidadScrapTotal =
                    Entero(
                        rd,
                        "CantidadScrapTotal"),

                ObjetivoHora =
                    rd["ObjetivoHora"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(
                            rd["ObjetivoHora"]),

                Ciclo = DecimalFlexibleProduccion(rd, "Ciclo"),

                Cavidades =
                    rd["Cavidades"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(
                            rd["Cavidades"]),

                EstatusID =
                    Entero(
                        rd,
                        "EstatusID"),

                TieneParoAbierto =
                    Booleano(
                        rd,
                        "TieneParoAbierto"),

                ParoAbiertoID =
                    NullableEntero(
                        rd,
                        "ParoAbiertoID")
            };
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerResumenCambioTurno(int ejecucionProduccionId)
        {
            if (!UsuarioEnSesion()) return Unauthorized();
            if (ejecucionProduccionId <= 0) return BadRequest(new { ok = false, mensaje = "La ejecución no es válida." });
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            var usuarioId = ObtenerUsuarioID();
            if (!await UsuarioEsOperadorAsync(usuarioId, cn)) return StatusCode(StatusCodes.Status403Forbidden);
            var personaId = await ObtenerPersonaIDUsuarioAsync(usuarioId, cn);
            if (!personaId.HasValue) return Unauthorized();
            var resumen = await ConstruirResumenCambioTurnoAsync(ejecucionProduccionId, personaId.Value, cn);
            if (resumen == null) return NotFound(new { ok = false, mensaje = "No se encontró la ejecución de producción." });
            return Json(new { ok = true, resumen });
        }

        private async Task<ProduccionCambioTurnoResumenVm?> ConstruirResumenCambioTurnoAsync(int ejecucionProduccionId, int personaSalienteId, SqlConnection cn, SqlTransaction? tx = null)
        {
            const string sql = @"
SELECT TOP(1)
    e.EjecucionProduccionID,
    e.ProgramaProduccionID,
    e.MaquinaID,
    e.MaquinaCodigo,
    e.MaquinaNombre,
    e.ParteID,
    e.NumeroParte,
    e.ReferenciaSAP,
    e.OperadorID,
    e.OperadorNombre,
    ISNULL(e.CantidadOKTotal,0) AS CantidadOKTotal,
    ISNULL(e.CantidadSospechosaTotal,0) AS CantidadSospechosaTotal,
    ISNULL(e.CantidadScrapTotal,0) AS CantidadScrapTotal,
    e.EstatusID,
    ultimo.RegistroHoraID,
    ultimo.FechaProduccion,
    ultimo.HoraInicio,
    ultimo.HoraFin
FROM dbo.Produccion_Ejecucion e
OUTER APPLY
(
    SELECT TOP(1)
        rh.RegistroHoraID,
        rh.FechaProduccion,
        rh.HoraInicio,
        rh.HoraFin
    FROM dbo.Produccion_RegistroHora rh
    WHERE rh.EjecucionProduccionID=e.EjecucionProduccionID
      AND rh.Activo=1
    ORDER BY rh.FechaProduccion DESC,rh.HoraInicio DESC,rh.RegistroHoraID DESC
) ultimo
WHERE e.EjecucionProduccionID=@EjecucionProduccionID
  AND e.Activo=1;";
            ProduccionCambioTurnoResumenVm? vm = null;
            var estatusId = 0;
            await using (var cmd = tx == null ? new SqlCommand(sql, cn) : new SqlCommand(sql, cn, tx))
            {
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                await using var rd = await cmd.ExecuteReaderAsync();
                if (!await rd.ReadAsync()) return null;
                var operadorId = rd["OperadorID"] == DBNull.Value ? 0 : Convert.ToInt32(rd["OperadorID"]);
                estatusId = Convert.ToInt32(rd["EstatusID"]);
                string? ultimaHora = null;
                if (rd["RegistroHoraID"] != DBNull.Value)
                {
                    var fecha = Convert.ToDateTime(rd["FechaProduccion"]);
                    var inicio = (TimeSpan)rd["HoraInicio"];
                    var fin = (TimeSpan)rd["HoraFin"];
                    ultimaHora = $"{fecha:dd/MM/yyyy} {inicio:hh\\:mm} - {fin:hh\\:mm}";
                }
                vm = new ProduccionCambioTurnoResumenVm
                {
                    EjecucionProduccionID = Convert.ToInt32(rd["EjecucionProduccionID"]),
                    ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                    MaquinaID = rd["MaquinaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaID"]),
                    MaquinaCodigo = rd["MaquinaCodigo"] == DBNull.Value ? null : rd["MaquinaCodigo"].ToString(),
                    MaquinaNombre = rd["MaquinaNombre"] == DBNull.Value ? null : rd["MaquinaNombre"].ToString(),
                    ParteID = rd["ParteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]),
                    NumeroParte = rd["NumeroParte"] == DBNull.Value ? null : rd["NumeroParte"].ToString(),
                    ReferenciaSAP = rd["ReferenciaSAP"] == DBNull.Value ? null : rd["ReferenciaSAP"].ToString(),
                    OperadorSalienteID = operadorId,
                    OperadorSalienteNombre = rd["OperadorNombre"]?.ToString()?.Trim() ?? string.Empty,
                    CantidadOK = Convert.ToInt32(rd["CantidadOKTotal"]),
                    CantidadSospechosa = Convert.ToInt32(rd["CantidadSospechosaTotal"]),
                    CantidadScrap = Convert.ToInt32(rd["CantidadScrapTotal"]),
                    UltimoRegistroHoraID = rd["RegistroHoraID"] == DBNull.Value ? null : Convert.ToInt32(rd["RegistroHoraID"]),
                    UltimaHoraTexto = ultimaHora
                };
            }
            const string sqlCajas = @"
SELECT
    COUNT(1) AS TotalCajas,
    SUM(CASE WHEN EstadoCajaID=@Formada THEN 0 ELSE 1 END) AS Entregadas,
    SUM(CASE WHEN EstadoCajaID=@Formada THEN 1 ELSE 0 END) AS Pendientes
FROM dbo.Produccion_Cajas
WHERE EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1;";
            await using (var cmd = tx == null ? new SqlCommand(sqlCajas, cn) : new SqlCommand(sqlCajas, cn, tx))
            {
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                cmd.Parameters.Add("@Formada", SqlDbType.Int).Value = ProduccionCajaEstatus.FormadaProduccion;
                await using var rd = await cmd.ExecuteReaderAsync();
                if (await rd.ReadAsync())
                {
                    vm.TotalCajas = rd["TotalCajas"] == DBNull.Value ? 0 : Convert.ToInt32(rd["TotalCajas"]);
                    vm.TotalCajasEntregadas = rd["Entregadas"] == DBNull.Value ? 0 : Convert.ToInt32(rd["Entregadas"]);
                    vm.TotalCajasPendientes = rd["Pendientes"] == DBNull.Value ? 0 : Convert.ToInt32(rd["Pendientes"]);
                }
            }
            var candidatos = await ObtenerCandidatosCambioTurnoAsync(vm.ParteID, vm.MaquinaID, personaSalienteId, DateTime.Now, cn, tx);
            vm.TieneMatrizPolivalencia = candidatos.TieneMatriz;
            vm.EscalaEncontrada = candidatos.EscalaEncontrada;
            vm.EscalaFolio = candidatos.EscalaFolio;
            vm.Operadores = candidatos.Operadores;
            var sugerenciaTecnico = await ObtenerSugerenciaTecnicoCambioTurnoAsync(ejecucionProduccionId, cn, tx);
            var sugeridoTecnico = sugerenciaTecnico == null
                ? null
                : vm.Operadores.FirstOrDefault(x => x.PersonaID == sugerenciaTecnico.OperadorSugeridoID);
            if (sugerenciaTecnico != null && sugeridoTecnico != null)
            {
                sugeridoTecnico.EsSugerido = true;
                vm.OperadorSugeridoID = sugeridoTecnico.PersonaID;
                vm.OperadorSugeridoNombre = sugeridoTecnico.Nombre;
                vm.TurnoSugeridoNombre = sugeridoTecnico.TurnoNombre;
                vm.SugeridoPorTecnico = true;
                vm.CambioTurnoSugerenciaID = sugerenciaTecnico.CambioTurnoSugerenciaID;
                vm.UsuarioTecnicoSugerenciaID = sugerenciaTecnico.UsuarioTecnicoID;
                vm.TecnicoSugerenciaNombre = sugerenciaTecnico.TecnicoNombre;
                vm.FechaSugerenciaTecnico = sugerenciaTecnico.FechaSugerencia;
                vm.ObservacionesSugerenciaTecnico = sugerenciaTecnico.Observaciones;
            }
            else
            {
                var sugeridoEscala = vm.Operadores
                    .Where(x => x.EnEscala)
                    .OrderBy(x => x.MinutosParaInicio ?? int.MaxValue)
                    .ThenByDescending(x => x.Nivel ?? 0)
                    .FirstOrDefault();
                if (sugeridoEscala != null)
                {
                    sugeridoEscala.EsSugerido = true;
                    vm.OperadorSugeridoID = sugeridoEscala.PersonaID;
                    vm.OperadorSugeridoNombre = sugeridoEscala.Nombre;
                    vm.TurnoSugeridoNombre = sugeridoEscala.TurnoNombre;
                    vm.SugeridoPorTecnico = false;
                }
            }
            if (vm.OperadorSalienteID != personaSalienteId)
            {
                vm.PuedeEntregar = false;
                vm.MotivoBloqueo = "La ejecución ya no está asignada al operador conectado.";
                return vm;
            }
            if (estatusId != ProduccionEstatus.EnProduccion)
            {
                vm.PuedeEntregar = false;
                vm.MotivoBloqueo = "El cambio de turno solo puede realizarse mientras la ejecución está en producción.";
                return vm;
            }
            var bloqueo = await ValidarEntregaTurnoAsync(ejecucionProduccionId, vm.ProgramaProduccionID, cn, tx);
            vm.PuedeEntregar = string.IsNullOrWhiteSpace(bloqueo);
            vm.MotivoBloqueo = bloqueo;
            return vm;
        }

        private async Task<string?> ValidarEntregaTurnoAsync(int ejecucionProduccionId, int programaProduccionId, SqlConnection cn, SqlTransaction? tx = null)
        {
            const string sql = @"
SELECT
    CASE WHEN EXISTS
    (
        SELECT 1
        FROM dbo.Produccion_Paros
        WHERE EjecucionProduccionID=@EjecucionProduccionID
          AND Activo=1
          AND FechaFinParo IS NULL
    ) THEN 1 ELSE 0 END AS ParoAbierto,
    (
        SELECT COUNT(1)
        FROM dbo.Produccion_Cajas
        WHERE EjecucionProduccionID=@EjecucionProduccionID
          AND Activo=1
          AND EstadoCajaID=@FormadaProduccion
    ) AS CajasPendientes,
    CASE WHEN EXISTS
    (
        SELECT 1
        FROM dbo.Produccion_CambiosTurno
        WHERE EjecucionProduccionID=@EjecucionProduccionID
          AND EstadoCambioTurno=N'PENDIENTE_RECEPCION'
          AND Activo=1
    ) THEN 1 ELSE 0 END AS CambioPendiente;";
            await using (var cmd = tx == null ? new SqlCommand(sql, cn) : new SqlCommand(sql, cn, tx))
            {
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                cmd.Parameters.Add("@FormadaProduccion", SqlDbType.Int).Value = ProduccionCajaEstatus.FormadaProduccion;
                await using var rd = await cmd.ExecuteReaderAsync();
                await rd.ReadAsync();
                if (Convert.ToBoolean(rd["ParoAbierto"])) return "No puedes entregar turno mientras exista un paro abierto.";
                var cajasPendientes = Convert.ToInt32(rd["CajasPendientes"]);
                if (cajasPendientes > 0) return $"No puedes entregar turno. Existen {cajasPendientes:N0} caja(s) que todavía no han sido entregadas a Calidad.";
                if (Convert.ToBoolean(rd["CambioPendiente"])) return "Ya existe una entrega de turno pendiente de recepción.";
            }
            var horas = await ObtenerFilasCapturaHoraAsync(ejecucionProduccionId, programaProduccionId, cn, tx);
            var horasPendientes = horas.Count(x => !x.Capturada && x.Vencida);
            if (horasPendientes > 0) return $"No puedes entregar turno. Existen {horasPendientes:N0} hora(s) de producción vencida(s) sin capturar.";
            return null;
        }
        private async Task<ResultadoCandidatosCambioTurno> ObtenerCandidatosCambioTurnoAsync(int? parteId, int? maquinaId, int operadorSalienteId, DateTime ahora, SqlConnection cn, SqlTransaction? tx = null)
        {
            var resultado = new ResultadoCandidatosCambioTurno();
            const string sqlEscala = @"
SELECT TOP (1) EscalaID,Folio
FROM dbo.RRHH_EscalasPersonal
WHERE Activo=1
  AND Estado IN(N'Publicada',N'Borrador')
  AND CONVERT(date,@Ahora) BETWEEN FechaInicio AND FechaFin
ORDER BY CASE WHEN Estado=N'Publicada' THEN 0 ELSE 1 END,
         ISNULL(FechaPublicacion,FechaRegistro) DESC,
         EscalaID DESC;";
            await using (var cmd = tx == null ? new SqlCommand(sqlEscala, cn) : new SqlCommand(sqlEscala, cn, tx))
            {
                cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
                await using var rd = await cmd.ExecuteReaderAsync();
                if (await rd.ReadAsync())
                {
                    resultado.EscalaEncontrada = true;
                    resultado.EscalaID = Convert.ToInt32(rd["EscalaID"]);
                    resultado.EscalaFolio = rd["Folio"]?.ToString();
                }
            }
            if (parteId.HasValue && parteId.Value > 0)
            {
                const string sqlMatriz = @"
SELECT CONVERT(bit,CASE WHEN EXISTS
(
    SELECT 1
    FROM dbo.vw_RRHH_PolivalenciaOperadoresParte
    WHERE ParteID=@ParteID
) THEN 1 ELSE 0 END);";
                await using var cmd = tx == null ? new SqlCommand(sqlMatriz, cn) : new SqlCommand(sqlMatriz, cn, tx);
                cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId.Value;
                resultado.TieneMatriz = Convert.ToBoolean(await cmd.ExecuteScalarAsync() ?? false);
            }
            var sql = resultado.TieneMatriz ? @"
SELECT DISTINCT
    p.PersonaID,
    LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))) AS Nombre,
    CONVERT(INT,v.Nivel) AS Nivel,
    escala.EscalaTurnoID AS TurnoID,
    escala.TurnoNombre,
    escala.HoraInicio,
    CONVERT(bit,CASE WHEN escala.EscalaTurnoID IS NULL THEN 0 ELSE 1 END) AS EnEscala,
    escala.MinutosParaInicio
FROM dbo.vw_RRHH_PolivalenciaOperadoresParte v
INNER JOIN dbo.Persona p ON p.PersonaID=v.PersonalID AND ISNULL(p.EsColaboradorActivo,1)=1
OUTER APPLY
(
    SELECT TOP (1)
        a.EscalaTurnoID,
        et.Nombre AS TurnoNombre,
        et.HoraInicio,
        CASE
            WHEN et.HoraInicio IS NULL THEN NULL
            WHEN DATEDIFF(MINUTE,CONVERT(time,@Ahora),et.HoraInicio)>0 THEN DATEDIFF(MINUTE,CONVERT(time,@Ahora),et.HoraInicio)
            ELSE DATEDIFF(MINUTE,CONVERT(time,@Ahora),et.HoraInicio)+1440
        END AS MinutosParaInicio
    FROM dbo.RRHH_EscalaAsignaciones a
    INNER JOIN dbo.RRHH_FuncionesPersonal f ON f.FuncionID=a.FuncionID AND f.Activo=1
    LEFT JOIN dbo.RRHH_EscalaTurnos et ON et.EscalaTurnoID=a.EscalaTurnoID AND et.Activo=1
    WHERE @EscalaID IS NOT NULL
      AND a.EscalaID=@EscalaID
      AND a.PersonalID=p.PersonaID
      AND a.Activo=1
      AND UPPER(LTRIM(RTRIM(f.Nombre)))=N'OPERADOR'
      AND (@MaquinaID IS NULL OR a.MaquinaID=@MaquinaID)
      AND CONVERT(date,@Ahora) BETWEEN a.FechaInicio AND a.FechaFin
    ORDER BY
        CASE WHEN et.HoraInicio IS NULL THEN 1 ELSE 0 END,
        CASE WHEN DATEDIFF(MINUTE,CONVERT(time,@Ahora),et.HoraInicio)>0
             THEN DATEDIFF(MINUTE,CONVERT(time,@Ahora),et.HoraInicio)
             ELSE DATEDIFF(MINUTE,CONVERT(time,@Ahora),et.HoraInicio)+1440 END,
        a.AsignacionID DESC
) escala
WHERE v.ParteID=@ParteID
  AND v.Nivel BETWEEN 1 AND 4
  AND p.PersonaID<>@OperadorSalienteID
  AND
  (
      UPPER(LTRIM(RTRIM(ISNULL(v.PuestoMatriz,N''))))=N'OPERADOR'
      OR UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N''))))=N'OPERADOR'
      OR EXISTS
      (
          SELECT 1
          FROM dbo.RRHH_EscalaAsignaciones ah
          INNER JOIN dbo.RRHH_FuncionesPersonal fh ON fh.FuncionID=ah.FuncionID AND fh.Activo=1
          WHERE ah.PersonalID=p.PersonaID
            AND ah.Activo=1
            AND UPPER(LTRIM(RTRIM(fh.Nombre)))=N'OPERADOR'
      )
  )
ORDER BY EnEscala DESC,MinutosParaInicio,CONVERT(INT,v.Nivel) DESC,Nombre;" : @"
SELECT DISTINCT
    p.PersonaID,
    LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))) AS Nombre,
    CAST(NULL AS INT) AS Nivel,
    escala.EscalaTurnoID AS TurnoID,
    escala.TurnoNombre,
    escala.HoraInicio,
    CONVERT(bit,CASE WHEN escala.EscalaTurnoID IS NULL THEN 0 ELSE 1 END) AS EnEscala,
    escala.MinutosParaInicio
FROM dbo.Persona p
OUTER APPLY
(
    SELECT TOP (1)
        a.EscalaTurnoID,
        et.Nombre AS TurnoNombre,
        et.HoraInicio,
        CASE
            WHEN et.HoraInicio IS NULL THEN NULL
            WHEN DATEDIFF(MINUTE,CONVERT(time,@Ahora),et.HoraInicio)>0 THEN DATEDIFF(MINUTE,CONVERT(time,@Ahora),et.HoraInicio)
            ELSE DATEDIFF(MINUTE,CONVERT(time,@Ahora),et.HoraInicio)+1440
        END AS MinutosParaInicio
    FROM dbo.RRHH_EscalaAsignaciones a
    INNER JOIN dbo.RRHH_FuncionesPersonal f ON f.FuncionID=a.FuncionID AND f.Activo=1
    LEFT JOIN dbo.RRHH_EscalaTurnos et ON et.EscalaTurnoID=a.EscalaTurnoID AND et.Activo=1
    WHERE @EscalaID IS NOT NULL
      AND a.EscalaID=@EscalaID
      AND a.PersonalID=p.PersonaID
      AND a.Activo=1
      AND UPPER(LTRIM(RTRIM(f.Nombre)))=N'OPERADOR'
      AND (@MaquinaID IS NULL OR a.MaquinaID=@MaquinaID)
      AND CONVERT(date,@Ahora) BETWEEN a.FechaInicio AND a.FechaFin
    ORDER BY
        CASE WHEN et.HoraInicio IS NULL THEN 1 ELSE 0 END,
        CASE WHEN DATEDIFF(MINUTE,CONVERT(time,@Ahora),et.HoraInicio)>0
             THEN DATEDIFF(MINUTE,CONVERT(time,@Ahora),et.HoraInicio)
             ELSE DATEDIFF(MINUTE,CONVERT(time,@Ahora),et.HoraInicio)+1440 END,
        a.AsignacionID DESC
) escala
WHERE ISNULL(p.EsColaboradorActivo,1)=1
  AND p.PersonaID<>@OperadorSalienteID
  AND
  (
      UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N''))))=N'OPERADOR'
      OR EXISTS
      (
          SELECT 1
          FROM dbo.RRHH_EscalaAsignaciones ah
          INNER JOIN dbo.RRHH_FuncionesPersonal fh ON fh.FuncionID=ah.FuncionID AND fh.Activo=1
          WHERE ah.PersonalID=p.PersonaID
            AND ah.Activo=1
            AND UPPER(LTRIM(RTRIM(fh.Nombre)))=N'OPERADOR'
      )
  )
ORDER BY EnEscala DESC,MinutosParaInicio,Nombre;";
            await using (var cmd = tx == null ? new SqlCommand(sql, cn) : new SqlCommand(sql, cn, tx))
            {
                cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId.HasValue && parteId.Value > 0 ? parteId.Value : DBNull.Value;
                cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId.HasValue && maquinaId.Value > 0 ? maquinaId.Value : DBNull.Value;
                cmd.Parameters.Add("@OperadorSalienteID", SqlDbType.Int).Value = operadorSalienteId;
                cmd.Parameters.Add("@EscalaID", SqlDbType.Int).Value = resultado.EscalaID.HasValue ? resultado.EscalaID.Value : DBNull.Value;
                cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    resultado.Operadores.Add(new ProduccionCambioTurnoCandidatoVm
                    {
                        PersonaID = Convert.ToInt32(rd["PersonaID"]),
                        Nombre = rd["Nombre"]?.ToString()?.Trim() ?? string.Empty,
                        Nivel = rd["Nivel"] == DBNull.Value ? null : Convert.ToInt32(rd["Nivel"]),
                        TurnoID = rd["TurnoID"] == DBNull.Value ? null : Convert.ToInt32(rd["TurnoID"]),
                        TurnoNombre = rd["TurnoNombre"] == DBNull.Value ? null : rd["TurnoNombre"].ToString(),
                        HoraInicioTurno = rd["HoraInicio"] == DBNull.Value ? null : (TimeSpan?)rd["HoraInicio"],
                        EnEscala = rd["EnEscala"] != DBNull.Value && Convert.ToBoolean(rd["EnEscala"]),
                        MinutosParaInicio = rd["MinutosParaInicio"] == DBNull.Value ? null : Convert.ToInt32(rd["MinutosParaInicio"])
                    });
                }
            }
            return resultado;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EntregarTurno(ProduccionCambioTurnoEntregaPostVm vm)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            if (vm.EjecucionProduccionID <= 0 || vm.OperadorEntranteID <= 0)
            {
                TempData["Error"] = "Selecciona correctamente al operador que recibirá el turno.";
                return RedirectToAction(nameof(Index));
            }
            if (!string.IsNullOrWhiteSpace(vm.Observaciones) && vm.Observaciones.Trim().Length > 1000)
            {
                TempData["Error"] = "Las observaciones no pueden superar 1000 caracteres.";
                return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
            }
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            var usuarioId = ObtenerUsuarioID();
            if (!await UsuarioEsOperadorAsync(usuarioId, cn)) return AccesoDenegadoOperador();
            var personaSalienteId = await ObtenerPersonaIDUsuarioAsync(usuarioId, cn);
            if (!personaSalienteId.HasValue) return Unauthorized();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var resumen = await ConstruirResumenCambioTurnoAsync(vm.EjecucionProduccionID, personaSalienteId.Value, cn, tx);
                if (resumen == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }
                if (!resumen.PuedeEntregar)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = resumen.MotivoBloqueo ?? "El turno no puede entregarse.";
                    return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
                }
                var operadorEntrante = resumen.Operadores.FirstOrDefault(x => x.PersonaID == vm.OperadorEntranteID);
                if (operadorEntrante == null)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = resumen.TieneMatrizPolivalencia
                        ? "El operador seleccionado no tiene nivel de polivalencia autorizado para esta parte."
                        : "El operador seleccionado no se encuentra activo o no pertenece al catálogo de operadores.";
                    return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
                }
                string origen;
                if (resumen.SugeridoPorTecnico && resumen.OperadorSugeridoID == operadorEntrante.PersonaID)
                {
                    origen = ProduccionCambioTurnoOrigen.Tecnico;
                }
                else if (!resumen.SugeridoPorTecnico && resumen.OperadorSugeridoID == operadorEntrante.PersonaID && operadorEntrante.EnEscala)
                {
                    origen = ProduccionCambioTurnoOrigen.Escala;
                }
                else
                {
                    origen = ProduccionCambioTurnoOrigen.Manual;
                }
                const string sqlInsert = @"
INSERT INTO dbo.Produccion_CambiosTurno
(
    EjecucionProduccionID,
    ProgramaProduccionID,
    OperadorSalienteID,
    OperadorEntranteID,
    OperadorAuxiliarSalienteID,
    OperadorAuxiliarEntranteID,
    FechaEntrega,
    CantidadOKAcumulada,
    CantidadSospechosaAcumulada,
    CantidadScrapAcumulada,
    UltimoRegistroHoraID,
    TotalCajasFormadas,
    TotalCajasEntregadas,
    TotalCajasPendientes,
    Observaciones,
    UsuarioEntregaID,
    FechaCreacion,
    Activo,
    EstadoCambioTurno,
    TurnoEntranteID,
    TurnoEntranteNombre,
    OrigenOperadorEntrante
)
OUTPUT INSERTED.CambioTurnoID
VALUES
(
    @EjecucionProduccionID,
    @ProgramaProduccionID,
    @OperadorSalienteID,
    @OperadorEntranteID,
    NULL,
    NULL,
    GETDATE(),
    @CantidadOK,
    @CantidadSospechosa,
    @CantidadScrap,
    @UltimoRegistroHoraID,
    @TotalCajas,
    @TotalCajasEntregadas,
    @TotalCajasPendientes,
    @Observaciones,
    @UsuarioID,
    GETDATE(),
    1,
    N'PENDIENTE_RECEPCION',
    @TurnoEntranteID,
    @TurnoEntranteNombre,
    @OrigenOperadorEntrante
);";
                int cambioTurnoId;
                await using (var cmd = new SqlCommand(sqlInsert, cn, tx))
                {
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = resumen.EjecucionProduccionID;
                    cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = resumen.ProgramaProduccionID;
                    cmd.Parameters.Add("@OperadorSalienteID", SqlDbType.Int).Value = resumen.OperadorSalienteID;
                    cmd.Parameters.Add("@OperadorEntranteID", SqlDbType.Int).Value = operadorEntrante.PersonaID;
                    cmd.Parameters.Add("@CantidadOK", SqlDbType.Int).Value = resumen.CantidadOK;
                    cmd.Parameters.Add("@CantidadSospechosa", SqlDbType.Int).Value = resumen.CantidadSospechosa;
                    cmd.Parameters.Add("@CantidadScrap", SqlDbType.Int).Value = resumen.CantidadScrap;
                    cmd.Parameters.Add("@UltimoRegistroHoraID", SqlDbType.Int).Value = (object?)resumen.UltimoRegistroHoraID ?? DBNull.Value;
                    cmd.Parameters.Add("@TotalCajas", SqlDbType.Int).Value = resumen.TotalCajas;
                    cmd.Parameters.Add("@TotalCajasEntregadas", SqlDbType.Int).Value = resumen.TotalCajasEntregadas;
                    cmd.Parameters.Add("@TotalCajasPendientes", SqlDbType.Int).Value = resumen.TotalCajasPendientes;
                    cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = string.IsNullOrWhiteSpace(vm.Observaciones) ? DBNull.Value : vm.Observaciones.Trim();
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@TurnoEntranteID", SqlDbType.Int).Value = (object?)operadorEntrante.TurnoID ?? DBNull.Value;
                    cmd.Parameters.Add("@TurnoEntranteNombre", SqlDbType.NVarChar, 100).Value = string.IsNullOrWhiteSpace(operadorEntrante.TurnoNombre) ? DBNull.Value : operadorEntrante.TurnoNombre;
                    cmd.Parameters.Add("@OrigenOperadorEntrante", SqlDbType.NVarChar, 20).Value = origen;
                    var resultado = await cmd.ExecuteScalarAsync();
                    if (resultado == null || resultado == DBNull.Value) throw new InvalidOperationException("No fue posible recuperar el identificador del cambio de turno.");
                    cambioTurnoId = Convert.ToInt32(resultado);
                }
                const string sqlCajas = @"
INSERT INTO dbo.Produccion_CambioTurnoCajas
(
    CambioTurnoID,
    CajaProduccionID,
    NumeroCaja,
    FolioCaja,
    CantidadPiezas,
    TipoCaja,
    EstadoCajaID,
    EstadoCajaNombre,
    FechaCreacion,
    Activo
)
SELECT
    @CambioTurnoID,
    CajaProduccionID,
    ISNULL(NumeroCaja,0),
    COALESCE(NULLIF(FolioCaja,N''),NULLIF(EtiquetaFolio,N''),NULLIF(Etiqueta,N''),CONVERT(NVARCHAR(100),CajaProduccionID)),
    ISNULL(CantidadPiezas,ISNULL(Cantidad,0)),
    ISNULL(TipoCaja,N'OK'),
    ISNULL(EstadoCajaID,1),
    ISNULL(EstadoCajaNombre,N''),
    GETDATE(),
    1
FROM dbo.Produccion_Cajas
WHERE EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1;";
                await using (var cmd = new SqlCommand(sqlCajas, cn, tx))
                {
                    cmd.Parameters.Add("@CambioTurnoID", SqlDbType.Int).Value = cambioTurnoId;
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = resumen.EjecucionProduccionID;
                    await cmd.ExecuteNonQueryAsync();
                }
                if (resumen.CambioTurnoSugerenciaID.HasValue && resumen.CambioTurnoSugerenciaID.Value > 0)
                {
                    if (origen == ProduccionCambioTurnoOrigen.Tecnico)
                    {
                        const string sqlSugerenciaUsada = @"
UPDATE dbo.Produccion_CambioTurnoSugerencias
SET Utilizada=1,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=SYSDATETIME()
WHERE CambioTurnoSugerenciaID=@CambioTurnoSugerenciaID
  AND EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1
  AND ISNULL(Utilizada,0)=0;";
                        await using var cmd = new SqlCommand(sqlSugerenciaUsada, cn, tx);
                        cmd.Parameters.Add("@CambioTurnoSugerenciaID", SqlDbType.Int).Value = resumen.CambioTurnoSugerenciaID.Value;
                        cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = resumen.EjecucionProduccionID;
                        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                        await cmd.ExecuteNonQueryAsync();
                    }
                    else
                    {
                        const string sqlSugerenciaDescartada = @"
UPDATE dbo.Produccion_CambioTurnoSugerencias
SET Activo=0,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=SYSDATETIME()
WHERE CambioTurnoSugerenciaID=@CambioTurnoSugerenciaID
  AND EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1
  AND ISNULL(Utilizada,0)=0;";
                        await using var cmd = new SqlCommand(sqlSugerenciaDescartada, cn, tx);
                        cmd.Parameters.Add("@CambioTurnoSugerenciaID", SqlDbType.Int).Value = resumen.CambioTurnoSugerenciaID.Value;
                        cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = resumen.EjecucionProduccionID;
                        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
                await tx.CommitAsync();
                TempData["Success"] = origen switch
                {
                    ProduccionCambioTurnoOrigen.Tecnico => $"Turno enviado a {operadorEntrante.Nombre}, respetando la sugerencia del técnico. Está pendiente de confirmación del operador entrante.",
                    ProduccionCambioTurnoOrigen.Escala => $"Turno enviado a {operadorEntrante.Nombre}, usando la sugerencia de escala. Está pendiente de confirmación del operador entrante.",
                    _ => $"Turno enviado a {operadorEntrante.Nombre}. El operador saliente modificó la sugerencia y la entrega quedó pendiente de confirmación."
                };
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible entregar el turno: " + ex.Message;
                return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
            }
        }

        [HttpGet]
        public async Task<IActionResult> Historial(
    string? busqueda,
    DateTime? fechaDesde,
    DateTime? fechaHasta)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction(
                    "Login",
                    "Login");

            await using var cn =
                new SqlConnection(
                    ConnectionString);

            await cn.OpenAsync();

            var usuarioId =
                ObtenerUsuarioID();

            if (!await UsuarioEsOperadorAsync(
                usuarioId,
                cn))
            {
                return AccesoDenegadoOperador();
            }

            var personaId =
                await ObtenerPersonaIDUsuarioAsync(
                    usuarioId,
                    cn);

            if (!personaId.HasValue ||
                personaId.Value <= 0)
            {
                return AccesoDenegadoOperador();
            }

            busqueda =
                string.IsNullOrWhiteSpace(
                    busqueda)
                    ? null
                    : busqueda.Trim();

            if (fechaDesde.HasValue &&
                fechaHasta.HasValue &&
                fechaDesde.Value.Date >
                fechaHasta.Value.Date)
            {
                var temporal =
                    fechaDesde;

                fechaDesde =
                    fechaHasta;

                fechaHasta =
                    temporal;
            }

            var vm =
                new ProduccionHistorialVm
                {
                    Busqueda =
                        busqueda,

                    FechaDesde =
                        fechaDesde,

                    FechaHasta =
                        fechaHasta,

                    EsVistaOperador =
                        true
                };

            vm.Producciones =
                await ObtenerHistorialOperadorAsync(
                    personaId.Value,
                    busqueda,
                    fechaDesde,
                    fechaHasta,
                    cn);

            return View(vm);
        }

        private async Task<List<ProduccionHistorialEjecucionVm>>
    ObtenerHistorialOperadorAsync(
        int personaId,
        string? busqueda,
        DateTime? fechaDesde,
        DateTime? fechaHasta,
        SqlConnection cn,
        SqlTransaction? tx = null)
        {
            var resultado =
                new List<ProduccionHistorialEjecucionVm>();

            if (personaId <= 0)
                return resultado;

            const string sql = @"
SELECT
    e.EjecucionProduccionID,
    e.ProgramaProduccionID,
    e.SolicitudProduccionID,

    e.MaquinaID,
    e.MaquinaCodigo,
    e.MaquinaNombre,

    e.ParteID,
    e.NumeroParte,
    e.ReferenciaSAP,
    e.DescripcionParte,

    e.FechaInicioReal,
    e.FechaFinReal,

    ISNULL(e.CantidadPlaneada,0)
        AS CantidadPlaneada,

    ISNULL(e.CantidadOKTotal,0)
        AS CantidadOKTotal,

    ISNULL(e.CantidadSospechosaTotal,0)
        AS CantidadSospechosaTotal,

    ISNULL(e.CantidadScrapTotal,0)
        AS CantidadScrapTotal,

    e.EstatusID,
    e.OperadorNombre,

    ISNULL(totalHoras.HorasCapturadas,0)
        AS HorasCapturadas,

    ISNULL(totalHoras.ObjetivoAcumulado,0)
        AS ObjetivoAcumulado,

    CAST(
        CASE
            WHEN ISNULL(
                totalHoras.ObjetivoAcumulado,
                0
            ) <= 0
                THEN 0
            ELSE
                ISNULL(
                    e.CantidadOKTotal,
                    0
                ) * 100.0
                /
                totalHoras.ObjetivoAcumulado
        END
        AS DECIMAL(18,2)
    ) AS PorcentajeCumplimiento,

    ISNULL(miProduccion.CantidadOK,0)
        AS CantidadOKOperador,

    ISNULL(
        miProduccion.CantidadSospechosa,
        0
    ) AS CantidadSospechosaOperador,

    ISNULL(
        miProduccion.CantidadScrap,
        0
    ) AS CantidadScrapOperador,

    ISNULL(
        miProduccion.ObjetivoAcumulado,
        0
    ) AS ObjetivoOperador,

    ISNULL(
        miProduccion.HorasCapturadas,
        0
    ) AS HorasOperador,

    CAST(
        CASE
            WHEN ISNULL(
                miProduccion.ObjetivoAcumulado,
                0
            ) <= 0
                THEN 0
            ELSE
                ISNULL(
                    miProduccion.CantidadOK,
                    0
                ) * 100.0
                /
                miProduccion.ObjetivoAcumulado
        END
        AS DECIMAL(18,2)
    ) AS PorcentajeCumplimientoOperador,

    ISNULL(
        cambios.TotalCambiosTurno,
        0
    ) AS TotalCambiosTurno,

    ISNULL(
        paros.TotalParos,
        0
    ) AS TotalParos

FROM dbo.Produccion_Ejecucion e

OUTER APPLY
(
    SELECT
        COUNT(1) AS HorasCapturadas,

        SUM(
            ISNULL(
                NULLIF(
                    rh.ObjetivoBloque,
                    0
                ),
                ISNULL(
                    rh.ObjetivoHora,
                    0
                )
            )
        ) AS ObjetivoAcumulado

    FROM dbo.Produccion_RegistroHora rh

    WHERE rh.EjecucionProduccionID =
          e.EjecucionProduccionID

      AND rh.Activo = 1
) totalHoras

OUTER APPLY
(
    SELECT
        COUNT(1) AS HorasCapturadas,

        SUM(
            ISNULL(
                rh.CantidadOK,
                0
            )
        ) AS CantidadOK,

        SUM(
            ISNULL(
                rh.CantidadSospechosa,
                0
            )
        ) AS CantidadSospechosa,

        SUM(
            ISNULL(
                rh.CantidadScrap,
                0
            )
        ) AS CantidadScrap,

        SUM(
            ISNULL(
                NULLIF(
                    rh.ObjetivoBloque,
                    0
                ),
                ISNULL(
                    rh.ObjetivoHora,
                    0
                )
            )
        ) AS ObjetivoAcumulado

    FROM dbo.Produccion_RegistroHora rh

    WHERE rh.EjecucionProduccionID =
          e.EjecucionProduccionID

      AND rh.OperadorID =
          @PersonaID

      AND rh.Activo = 1
) miProduccion

OUTER APPLY
(
    SELECT
        COUNT(1)
            AS TotalCambiosTurno

    FROM dbo.Produccion_CambiosTurno ct

    WHERE ct.EjecucionProduccionID =
          e.EjecucionProduccionID

      AND ct.Activo = 1
) cambios

OUTER APPLY
(
    SELECT
        COUNT(1)
            AS TotalParos

    FROM dbo.Produccion_Paros p

    WHERE p.EjecucionProduccionID =
          e.EjecucionProduccionID

      AND p.Activo = 1
) paros

WHERE e.Activo = 1

  AND e.EstatusID IN
  (
      @TerminadoParcial,
      @Terminado,
      @ListaCierreDocumental,
      @Cerrado
  )

  /*
     El operador participó si:
     1. tiene registros horarios;
     2. entregó un turno;
     3. recibió un turno;
     4. o quedó como operador actual/final.
  */
  AND
  (
      EXISTS
      (
          SELECT 1
          FROM dbo.Produccion_RegistroHora rhp
          WHERE rhp.EjecucionProduccionID =
                e.EjecucionProduccionID
            AND rhp.OperadorID =
                @PersonaID
            AND rhp.Activo = 1
      )

      OR EXISTS
      (
          SELECT 1
          FROM dbo.Produccion_CambiosTurno ctp
          WHERE ctp.EjecucionProduccionID =
                e.EjecucionProduccionID
            AND ctp.Activo = 1
            AND
            (
                ctp.OperadorSalienteID =
                @PersonaID

                OR

                ctp.OperadorEntranteID =
                @PersonaID
            )
      )

      OR e.OperadorID =
         @PersonaID
  )

  AND
  (
      @FechaDesde IS NULL

      OR CAST(
            ISNULL(
                e.FechaFinReal,
                e.FechaModificacion
            ) AS DATE
         ) >= @FechaDesde
  )

  AND
  (
      @FechaHasta IS NULL

      OR CAST(
            ISNULL(
                e.FechaFinReal,
                e.FechaModificacion
            ) AS DATE
         ) <= @FechaHasta
  )

  AND
  (
      @Busqueda IS NULL

      OR e.MaquinaCodigo
         LIKE '%' + @Busqueda + '%'

      OR e.MaquinaNombre
         LIKE '%' + @Busqueda + '%'

      OR e.NumeroParte
         LIKE '%' + @Busqueda + '%'

      OR e.ReferenciaSAP
         LIKE '%' + @Busqueda + '%'

      OR e.DescripcionParte
         LIKE '%' + @Busqueda + '%'

      OR CONVERT(
            NVARCHAR(30),
            e.ProgramaProduccionID
         ) LIKE '%' + @Busqueda + '%'

      OR CONVERT(
            NVARCHAR(30),
            e.EjecucionProduccionID
         ) LIKE '%' + @Busqueda + '%'
  )

ORDER BY
    ISNULL(
        e.FechaFinReal,
        e.FechaModificacion
    ) DESC,

    e.EjecucionProduccionID DESC;";

            await using var cmd =
                tx == null
                    ? new SqlCommand(sql, cn)
                    : new SqlCommand(
                        sql,
                        cn,
                        tx);

            cmd.Parameters.Add(
                "@PersonaID",
                SqlDbType.Int).Value =
                personaId;

            cmd.Parameters.Add(
                "@TerminadoParcial",
                SqlDbType.Int).Value =
                ProduccionEstatus
                    .TerminadoParcial;

            cmd.Parameters.Add(
                "@Terminado",
                SqlDbType.Int).Value =
                ProduccionEstatus
                    .Terminado;

            cmd.Parameters.Add(
                "@ListaCierreDocumental",
                SqlDbType.Int).Value =
                ProduccionEstatus
                    .ListaCierreDocumental;

            cmd.Parameters.Add(
                "@Cerrado",
                SqlDbType.Int).Value =
                ProduccionEstatus
                    .Cerrado;

            cmd.Parameters.Add(
                "@FechaDesde",
                SqlDbType.Date).Value =
                fechaDesde.HasValue
                    ? fechaDesde.Value.Date
                    : DBNull.Value;

            cmd.Parameters.Add(
                "@FechaHasta",
                SqlDbType.Date).Value =
                fechaHasta.HasValue
                    ? fechaHasta.Value.Date
                    : DBNull.Value;

            cmd.Parameters.Add(
                "@Busqueda",
                SqlDbType.NVarChar,
                200).Value =
                string.IsNullOrWhiteSpace(
                    busqueda)
                    ? DBNull.Value
                    : busqueda.Trim();

            await using var rd =
                await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                resultado.Add(
                    new ProduccionHistorialEjecucionVm
                    {
                        EjecucionProduccionID =
                            Convert.ToInt32(
                                rd[
                                    "EjecucionProduccionID"
                                ]),

                        ProgramaProduccionID =
                            Convert.ToInt32(
                                rd[
                                    "ProgramaProduccionID"
                                ]),

                        SolicitudProduccionID =
                            rd["SolicitudProduccionID"] ==
                            DBNull.Value
                                ? null
                                : Convert.ToInt32(
                                    rd[
                                        "SolicitudProduccionID"
                                    ]),

                        MaquinaID =
                            rd["MaquinaID"] ==
                            DBNull.Value
                                ? null
                                : Convert.ToInt32(
                                    rd["MaquinaID"]),

                        MaquinaCodigo =
                            rd["MaquinaCodigo"] ==
                            DBNull.Value
                                ? null
                                : rd["MaquinaCodigo"]
                                    .ToString(),

                        MaquinaNombre =
                            rd["MaquinaNombre"] ==
                            DBNull.Value
                                ? null
                                : rd["MaquinaNombre"]
                                    .ToString(),

                        ParteID =
                            rd["ParteID"] ==
                            DBNull.Value
                                ? null
                                : Convert.ToInt32(
                                    rd["ParteID"]),

                        NumeroParte =
                            rd["NumeroParte"] ==
                            DBNull.Value
                                ? null
                                : rd["NumeroParte"]
                                    .ToString(),

                        ReferenciaSAP =
                            rd["ReferenciaSAP"] ==
                            DBNull.Value
                                ? null
                                : rd["ReferenciaSAP"]
                                    .ToString(),

                        DescripcionParte =
                            rd["DescripcionParte"] ==
                            DBNull.Value
                                ? null
                                : rd["DescripcionParte"]
                                    .ToString(),

                        FechaInicioReal =
                            rd["FechaInicioReal"] ==
                            DBNull.Value
                                ? null
                                : Convert.ToDateTime(
                                    rd[
                                        "FechaInicioReal"
                                    ]),

                        FechaFinReal =
                            rd["FechaFinReal"] ==
                            DBNull.Value
                                ? null
                                : Convert.ToDateTime(
                                    rd[
                                        "FechaFinReal"
                                    ]),

                        CantidadPlaneada =
                            Convert.ToInt32(
                                rd[
                                    "CantidadPlaneada"
                                ]),

                        CantidadOK =
                            Convert.ToInt32(
                                rd[
                                    "CantidadOKTotal"
                                ]),

                        CantidadSospechosa =
                            Convert.ToInt32(
                                rd[
                                    "CantidadSospechosaTotal"
                                ]),

                        CantidadScrap =
                            Convert.ToInt32(
                                rd[
                                    "CantidadScrapTotal"
                                ]),

                        ObjetivoAcumulado =
                            Convert.ToInt32(
                                rd[
                                    "ObjetivoAcumulado"
                                ]),

                        HorasCapturadas =
                            Convert.ToInt32(
                                rd[
                                    "HorasCapturadas"
                                ]),

                        PorcentajeCumplimiento =
                            Convert.ToDecimal(
                                rd[
                                    "PorcentajeCumplimiento"
                                ]),

                        EstatusID =
                            Convert.ToInt32(
                                rd["EstatusID"]),

                        OperadorPrincipalNombre =
                            rd["OperadorNombre"] ==
                            DBNull.Value
                                ? null
                                : rd["OperadorNombre"]
                                    .ToString(),

                        TotalCambiosTurno =
                            Convert.ToInt32(
                                rd[
                                    "TotalCambiosTurno"
                                ]),

                        TotalParos =
                            Convert.ToInt32(
                                rd["TotalParos"]),

                        PersonaConsultaID =
                            personaId,

                        CantidadOKOperador =
                            Convert.ToInt32(
                                rd[
                                    "CantidadOKOperador"
                                ]),

                        CantidadSospechosaOperador =
                            Convert.ToInt32(
                                rd[
                                    "CantidadSospechosaOperador"
                                ]),

                        CantidadScrapOperador =
                            Convert.ToInt32(
                                rd[
                                    "CantidadScrapOperador"
                                ]),

                        ObjetivoOperador =
                            Convert.ToInt32(
                                rd[
                                    "ObjetivoOperador"
                                ]),

                        HorasOperador =
                            Convert.ToInt32(
                                rd[
                                    "HorasOperador"
                                ]),

                        PorcentajeCumplimientoOperador =
                            Convert.ToDecimal(
                                rd[
                                    "PorcentajeCumplimientoOperador"
                                ])
                    });
            }

            return resultado;
        }

        private async Task<bool>
    OperadorParticipoEnEjecucionAsync(
        int ejecucionProduccionId,
        int personaId,
        SqlConnection cn,
        SqlTransaction? tx = null)
        {
            if (ejecucionProduccionId <= 0 ||
                personaId <= 0)
            {
                return false;
            }

            const string sql = @"
SELECT CAST(
    CASE
        WHEN EXISTS
        (
            SELECT 1
            FROM dbo.Produccion_RegistroHora rh
            WHERE rh.EjecucionProduccionID =
                  @EjecucionProduccionID
              AND rh.OperadorID =
                  @PersonaID
              AND rh.Activo = 1
        )
        OR EXISTS
        (
            SELECT 1
            FROM dbo.Produccion_CambiosTurno ct
            WHERE ct.EjecucionProduccionID =
                  @EjecucionProduccionID
              AND ct.Activo = 1
              AND
              (
                  ct.OperadorSalienteID =
                  @PersonaID

                  OR

                  ct.OperadorEntranteID =
                  @PersonaID
              )
        )
        OR EXISTS
        (
            SELECT 1
            FROM dbo.Produccion_Ejecucion e
            WHERE e.EjecucionProduccionID =
                  @EjecucionProduccionID
              AND e.Activo = 1
              AND e.OperadorID =
                  @PersonaID
        )
        THEN 1
        ELSE 0
    END
AS BIT);";

            await using var cmd =
                tx == null
                    ? new SqlCommand(sql, cn)
                    : new SqlCommand(
                        sql,
                        cn,
                        tx);

            cmd.Parameters.Add(
                "@EjecucionProduccionID",
                SqlDbType.Int).Value =
                ejecucionProduccionId;

            cmd.Parameters.Add(
                "@PersonaID",
                SqlDbType.Int).Value =
                personaId;

            return Convert.ToBoolean(
                await cmd.ExecuteScalarAsync());
        }

        [HttpGet]
        public async Task<IActionResult> HistorialDetalle(int id)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction(
                    "Login",
                    "Login");

            if (id <= 0)
                return NotFound();

            await using var cn =
                new SqlConnection(ConnectionString);

            await cn.OpenAsync();

            var usuarioId =
                ObtenerUsuarioID();

            if (!await UsuarioEsOperadorAsync(
                usuarioId,
                cn))
            {
                return AccesoDenegadoOperador();
            }

            var personaId =
                await ObtenerPersonaIDUsuarioAsync(
                    usuarioId,
                    cn);

            if (!personaId.HasValue ||
                personaId.Value <= 0)
            {
                return AccesoDenegadoOperador();
            }

           
            var participo =
                await OperadorParticipoEnEjecucionAsync(
                    id,
                    personaId.Value,
                    cn);

            if (!participo)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden);
            }

            var vm =
                await ObtenerTabletHistorialVmAsync(
                    id,
                    cn);

            if (vm == null)
                return NotFound();

            vm.MotivosParo =
                await CargarMotivosParoAsync(
                    cn);

            vm.HorasCaptura =
                await ObtenerFilasCapturaHoraAsync(
                    vm.EjecucionProduccionID,
                    vm.ProgramaProduccionID,
                    cn);

            vm.HistorialCambiosTurno =
                await ObtenerHistorialCambiosTurnoAsync(
                    vm.EjecucionProduccionID,
                    cn);

            vm.HistorialTurnos =
                ConstruirHistorialTurnos(
                    vm.HorasCaptura,
                    vm.HistorialCambiosTurno);

            return View(
                "Captura",
                vm);
        }


        [HttpGet]
        public async Task<IActionResult> ObtenerTurnosPendientesRecepcion()
        {
            if (!UsuarioEnSesion()) return Unauthorized();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            var usuarioId = ObtenerUsuarioID();
            if (!await UsuarioEsOperadorAsync(usuarioId, cn)) return StatusCode(StatusCodes.Status403Forbidden);
            var personaId = await ObtenerPersonaIDUsuarioAsync(usuarioId, cn);
            if (!personaId.HasValue) return Unauthorized();
            const string sql = @"
SELECT
    ct.CambioTurnoID,
    ct.EjecucionProduccionID,
    ct.ProgramaProduccionID,
    ct.OperadorSalienteID,
    sal.NombreCompleto AS OperadorSalienteNombre,
    ct.OperadorEntranteID,
    ent.NombreCompleto AS OperadorEntranteNombre,
    ct.FechaEntrega,
    ct.CantidadOKAcumulada,
    ct.CantidadSospechosaAcumulada,
    ct.CantidadScrapAcumulada,
    ct.TotalCajasFormadas,
    ct.TotalCajasEntregadas,
    ct.Observaciones,
    e.MaquinaCodigo,
    e.ReferenciaSAP,
    e.NumeroParte
FROM dbo.Produccion_CambiosTurno ct
INNER JOIN dbo.Produccion_Ejecucion e ON e.EjecucionProduccionID=ct.EjecucionProduccionID
OUTER APPLY
(
    SELECT LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))) AS NombreCompleto
    FROM dbo.Persona p
    WHERE p.PersonaID=ct.OperadorSalienteID
) sal
OUTER APPLY
(
    SELECT LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))) AS NombreCompleto
    FROM dbo.Persona p
    WHERE p.PersonaID=ct.OperadorEntranteID
) ent
WHERE ct.OperadorEntranteID=@PersonaID
  AND ct.EstadoCambioTurno=N'PENDIENTE_RECEPCION'
  AND ct.Activo=1
ORDER BY ct.FechaEntrega;";
            var lista = new List<ProduccionCambioTurnoPendienteVm>();
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = personaId.Value;
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                lista.Add(new ProduccionCambioTurnoPendienteVm
                {
                    CambioTurnoID = Convert.ToInt32(rd["CambioTurnoID"]),
                    EjecucionProduccionID = Convert.ToInt32(rd["EjecucionProduccionID"]),
                    ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                    OperadorSalienteID = Convert.ToInt32(rd["OperadorSalienteID"]),
                    OperadorSalienteNombre = rd["OperadorSalienteNombre"]?.ToString()?.Trim() ?? string.Empty,
                    OperadorEntranteID = Convert.ToInt32(rd["OperadorEntranteID"]),
                    OperadorEntranteNombre = rd["OperadorEntranteNombre"]?.ToString()?.Trim() ?? string.Empty,
                    FechaEntrega = Convert.ToDateTime(rd["FechaEntrega"]),
                    CantidadOK = Convert.ToInt32(rd["CantidadOKAcumulada"]),
                    CantidadSospechosa = Convert.ToInt32(rd["CantidadSospechosaAcumulada"]),
                    CantidadScrap = Convert.ToInt32(rd["CantidadScrapAcumulada"]),
                    TotalCajas = Convert.ToInt32(rd["TotalCajasFormadas"]),
                    TotalCajasEntregadas = Convert.ToInt32(rd["TotalCajasEntregadas"]),
                    Observaciones = rd["Observaciones"] == DBNull.Value ? null : rd["Observaciones"].ToString(),
                    MaquinaCodigo = rd["MaquinaCodigo"] == DBNull.Value ? null : rd["MaquinaCodigo"].ToString(),
                    ReferenciaSAP = rd["ReferenciaSAP"] == DBNull.Value ? null : rd["ReferenciaSAP"].ToString(),
                    NumeroParte = rd["NumeroParte"] == DBNull.Value ? null : rd["NumeroParte"].ToString()
                });
            }
            return Json(new { ok = true, turnos = lista });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecibirTurno(ProduccionCambioTurnoRecepcionPostVm vm)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            if (vm.CambioTurnoID <= 0)
            {
                TempData["Error"] = "No se recibió correctamente la entrega de turno.";
                return RedirectToAction(nameof(Index));
            }
            if (!string.IsNullOrWhiteSpace(vm.ObservacionesRecepcion) && vm.ObservacionesRecepcion.Trim().Length > 1000)
            {
                TempData["Error"] = "Las observaciones de recepción no pueden superar 1000 caracteres.";
                return RedirectToAction(nameof(Index));
            }
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            var usuarioId = ObtenerUsuarioID();
            if (!await UsuarioEsOperadorAsync(usuarioId, cn)) return AccesoDenegadoOperador();
            var personaEntranteId = await ObtenerPersonaIDUsuarioAsync(usuarioId, cn);
            if (!personaEntranteId.HasValue) return Unauthorized();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                const string sqlCambio = @"
SELECT TOP (1)
    CambioTurnoID,
    EjecucionProduccionID,
    ProgramaProduccionID,
    OperadorSalienteID,
    OperadorEntranteID,
    OrigenOperadorEntrante,
    EstadoCambioTurno
FROM dbo.Produccion_CambiosTurno WITH (UPDLOCK,HOLDLOCK)
WHERE CambioTurnoID=@CambioTurnoID
  AND Activo=1;";
                int ejecucionId;
                int programaId;
                int operadorSalienteId;
                int operadorEntranteId;
                string origen;
                await using (var cmd = new SqlCommand(sqlCambio, cn, tx))
                {
                    cmd.Parameters.Add("@CambioTurnoID", SqlDbType.Int).Value = vm.CambioTurnoID;
                    await using var rd = await cmd.ExecuteReaderAsync();
                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();
                        return NotFound();
                    }
                    var estado = rd["EstadoCambioTurno"]?.ToString()?.Trim() ?? string.Empty;
                    if (!string.Equals(estado, ProduccionCambioTurnoEstado.PendienteRecepcion, StringComparison.OrdinalIgnoreCase))
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "Esta entrega de turno ya fue atendida o cancelada.";
                        return RedirectToAction(nameof(Index));
                    }
                    ejecucionId = Convert.ToInt32(rd["EjecucionProduccionID"]);
                    programaId = Convert.ToInt32(rd["ProgramaProduccionID"]);
                    operadorSalienteId = Convert.ToInt32(rd["OperadorSalienteID"]);
                    operadorEntranteId = Convert.ToInt32(rd["OperadorEntranteID"]);
                    origen = rd["OrigenOperadorEntrante"]?.ToString()?.Trim() ?? ProduccionCambioTurnoOrigen.Manual;
                }
                if (operadorEntranteId != personaEntranteId.Value)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Esta entrega de turno está asignada a otro operador.";
                    return RedirectToAction(nameof(Index));
                }
                const string sqlPersona = @"
SELECT
    ISNULL(EsColaboradorActivo,1) AS Activo,
    LTRIM(RTRIM(CONCAT(ISNULL(Nombre,N''),N' ',ISNULL(ApellidoPaterno,N''),N' ',ISNULL(ApellidoMaterno,N'')))) AS Nombre
FROM dbo.Persona
WHERE PersonaID=@PersonaID;";
                string nombreEntrante;
                await using (var cmd = new SqlCommand(sqlPersona, cn, tx))
                {
                    cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = operadorEntranteId;
                    await using var rd = await cmd.ExecuteReaderAsync();
                    if (!await rd.ReadAsync() || !Convert.ToBoolean(rd["Activo"]))
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "El operador entrante ya no se encuentra activo.";
                        return RedirectToAction(nameof(Index));
                    }
                    nombreEntrante = rd["Nombre"]?.ToString()?.Trim() ?? string.Empty;
                }
                const string sqlActualizar = @"
UPDATE dbo.Produccion_CambiosTurno
SET EstadoCambioTurno=N'RECIBIDO',
    FechaRecepcion=GETDATE(),
    UsuarioRecepcionID=@UsuarioID,
    ObservacionesRecepcion=@ObservacionesRecepcion
WHERE CambioTurnoID=@CambioTurnoID
  AND EstadoCambioTurno=N'PENDIENTE_RECEPCION'
  AND Activo=1;

IF @@ROWCOUNT<>1
    THROW 51090,'La entrega de turno cambió de estado mientras se confirmaba.',1;

UPDATE dbo.Produccion_Ejecucion
SET OperadorID=@OperadorEntranteID,
    OperadorNombre=@OperadorEntranteNombre,
    OperadoresModificadosManual=CASE WHEN @Origen=N'MANUAL' THEN 1 ELSE OperadoresModificadosManual END,
    MotivoCambioOperadores=CASE WHEN @Origen=N'MANUAL' THEN N'Cambio de turno con operador seleccionado manualmente.' ELSE MotivoCambioOperadores END,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1;

IF @@ROWCOUNT<>1
    THROW 51091,'No fue posible actualizar el operador de la ejecución.',1;

UPDATE dbo.Planeacion_ProgramaOperadores
SET PersonaID=@OperadorEntranteID
WHERE ProgramaProduccionID=@ProgramaProduccionID
  AND PersonaID=@OperadorSalienteID
  AND Activo=1
  AND UPPER(LTRIM(RTRIM(ISNULL(RolOperador,N''))))=N'PRINCIPAL';

IF @@ROWCOUNT<>1
    THROW 51092,'No fue posible sincronizar al operador principal del programa.',1;";
                await using (var cmd = new SqlCommand(sqlActualizar, cn, tx))
                {
                    cmd.Parameters.Add("@CambioTurnoID", SqlDbType.Int).Value = vm.CambioTurnoID;
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionId;
                    cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaId;
                    cmd.Parameters.Add("@OperadorSalienteID", SqlDbType.Int).Value = operadorSalienteId;
                    cmd.Parameters.Add("@OperadorEntranteID", SqlDbType.Int).Value = operadorEntranteId;
                    cmd.Parameters.Add("@OperadorEntranteNombre", SqlDbType.NVarChar, 250).Value = nombreEntrante;
                    cmd.Parameters.Add("@Origen", SqlDbType.NVarChar, 20).Value = origen;
                    cmd.Parameters.Add("@ObservacionesRecepcion", SqlDbType.NVarChar, 1000).Value = string.IsNullOrWhiteSpace(vm.ObservacionesRecepcion) ? DBNull.Value : vm.ObservacionesRecepcion.Trim();
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    await cmd.ExecuteNonQueryAsync();
                }
                await tx.CommitAsync();
                TempData["Success"] = "Turno recibido correctamente. A partir de este momento la producción quedó bajo tu responsabilidad.";
                return RedirectToAction(nameof(Captura), new { id = ejecucionId });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible recibir el turno: " + ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }


        private sealed class ResultadoCandidatosCambioTurno
        {
            public bool TieneMatriz { get; set; }
            public bool EscalaEncontrada { get; set; }
            public int? EscalaID { get; set; }
            public string? EscalaFolio { get; set; }
            public List<ProduccionCambioTurnoCandidatoVm> Operadores { get; set; } = new();
        }

        private static async Task<ContextoEscaneoCaja?> ObtenerContextoEscaneoCajaAsync(int ejecucionProduccionId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP(1)
    e.EjecucionProduccionID,
    e.ProgramaProduccionID,
    e.SolicitudProduccionID,
    e.SolicitudProduccionDetalleID,
    e.ReleaseID,
    e.ReleaseDetalleID,
    e.ParteID,
    COALESCE(
        NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)),N''),
        NULLIF(LTRIM(RTRIM(s.FolioSolicitud)),N''),
        CASE
            WHEN e.SolicitudProduccionID IS NOT NULL
                THEN CONCAT(N'OF-ID-',e.SolicitudProduccionID)
            ELSE NULL
        END
    ) AS NumeroOF,
    e.NumeroParte,
    e.ReferenciaSAP,
    ISNULL(e.CantidadPlaneada,0) AS CantidadPlaneada,
    d.PiezasPorEmbalaje,
    d.CantidadEmbalajes,
    e.EstatusID
FROM dbo.Produccion_Ejecucion e WITH(UPDLOCK,HOLDLOCK)
LEFT JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID=e.SolicitudProduccionID
   AND s.Activo=1
LEFT JOIN dbo.SolicitudesProduccionDetalle d
    ON d.SolicitudProduccionDetalleID=e.SolicitudProduccionDetalleID
   AND d.Activo=1
WHERE e.EjecucionProduccionID=@EjecucionProduccionID
  AND e.Activo=1;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add(
                "@EjecucionProduccionID",
                SqlDbType.Int).Value =
                ejecucionProduccionId;

            await using var rd =
                await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return null;

            return new ContextoEscaneoCaja
            {
                EjecucionProduccionID =
                    Convert.ToInt32(
                        rd["EjecucionProduccionID"]),

                ProgramaProduccionID =
                    Convert.ToInt32(
                        rd["ProgramaProduccionID"]),

                SolicitudProduccionID =
                    rd["SolicitudProduccionID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(
                            rd["SolicitudProduccionID"]),

                SolicitudProduccionDetalleID =
                    rd["SolicitudProduccionDetalleID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(
                            rd["SolicitudProduccionDetalleID"]),

                ReleaseID =
                    rd["ReleaseID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(
                            rd["ReleaseID"]),

                ReleaseDetalleID =
                    rd["ReleaseDetalleID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(
                            rd["ReleaseDetalleID"]),

                ParteID =
                    rd["ParteID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(
                            rd["ParteID"]),

                NumeroOF =
                    rd["NumeroOF"] == DBNull.Value
                        ? null
                        : rd["NumeroOF"]
                            ?.ToString()
                            ?.Trim(),

                NumeroParte =
                    rd["NumeroParte"] == DBNull.Value
                        ? null
                        : rd["NumeroParte"]
                            ?.ToString()
                            ?.Trim(),

                ReferenciaSAP =
                    rd["ReferenciaSAP"] == DBNull.Value
                        ? null
                        : rd["ReferenciaSAP"]
                            ?.ToString()
                            ?.Trim(),

                CantidadPlaneada =
                    Convert.ToInt32(
                        rd["CantidadPlaneada"]),

                PiezasPorEmbalaje =
                    rd["PiezasPorEmbalaje"] == DBNull.Value
                        ? null
                        : Convert.ToDecimal(
                            rd["PiezasPorEmbalaje"]),

                CantidadEmbalajes =
                    rd["CantidadEmbalajes"] == DBNull.Value
                        ? null
                        : Convert.ToDecimal(
                            rd["CantidadEmbalajes"]),

                EstatusID =
                    Convert.ToInt32(
                        rd["EstatusID"])
            };
        }
        private async Task DesplazarFinProgramadoParoCortoAsync(
    int programaProduccionId,
    int ejecucionProduccionId,
    int minutosInterrupcion,
    int usuarioId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            if (minutosInterrupcion <= 0)
                return;

            const string sql = @"
UPDATE dbo.Planeacion_ProgramaProduccion
SET
    FechaFinProgramada =
        CASE
            WHEN FechaFinProgramada IS NULL
                THEN FechaFinProgramada
            ELSE DATEADD(MINUTE, @MinutosInterrupcion, FechaFinProgramada)
        END,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE ProgramaProduccionID = @ProgramaProduccionID
  AND Activo = 1;

UPDATE dbo.Calidad_Inspecciones
SET
    FechaFinProgramada =
        CASE
            WHEN FechaFinProgramada IS NULL
                THEN FechaFinProgramada
            ELSE DATEADD(MINUTE, @MinutosInterrupcion, FechaFinProgramada)
        END,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE EjecucionProduccionID = @EjecucionProduccionID
  AND ISNULL(Estado, N'') <> N'CERRADA';";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add(
                "@ProgramaProduccionID",
                SqlDbType.Int).Value =
                programaProduccionId;

            cmd.Parameters.Add(
                "@EjecucionProduccionID",
                SqlDbType.Int).Value =
                ejecucionProduccionId;

            cmd.Parameters.Add(
                "@MinutosInterrupcion",
                SqlDbType.Int).Value =
                minutosInterrupcion;

            cmd.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value =
                usuarioId;

            await cmd.ExecuteNonQueryAsync();
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

        private async Task<List<ProduccionCapturaHoraFilaVm>> ObtenerFilasCapturaHoraAsync(
       int ejecucionProduccionId,
       int programaProduccionId,
       SqlConnection cn,
       SqlTransaction? tx = null)
        {
            var filas = new List<ProduccionCapturaHoraFilaVm>();

            DateTime? inicioReal = null;
            DateTime? finReal = null;
            int? objetivoHora = null;
            int cantidadPlaneada = 0;

            const string sqlPrograma = @"
SELECT TOP(1)
    COALESCE(
        (
            SELECT TOP(1)
                h.FechaMovimiento
            FROM dbo.Calidad_InspeccionHistorial h
            INNER JOIN dbo.Calidad_Inspecciones ci
                ON ci.InspeccionID=h.InspeccionID
            WHERE ci.EjecucionProduccionID=e.EjecucionProduccionID
              AND h.Movimiento=N'CONFIRMACION_INICIO_SERIE_PRODUCCION'
            ORDER BY h.FechaMovimiento
        ),
        pp.FechaInicioReal,
        e.FechaInicioReal
    ) AS FechaInicioReal,

    COALESCE(
        pp.FechaFinReal,
        e.FechaFinReal
    ) AS FechaFinReal,

    ISNULL(
        e.CantidadPlaneada,
        0
    ) AS CantidadPlaneada,

    dt.ObjetivoHora

FROM dbo.Produccion_Ejecucion e

INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.ProgramaProduccionID=e.ProgramaProduccionID
   AND pp.Activo=1

OUTER APPLY
(
    SELECT TOP(1)
        dt0.ObjetivoHora
    FROM dbo.ERP_ParteDatosTecnicos dt0
    WHERE dt0.ParteID=e.ParteID
      AND dt0.Activo=1
    ORDER BY dt0.ParteDatoTecnicoID DESC
) dt

WHERE e.EjecucionProduccionID=@EjecucionProduccionID
  AND e.ProgramaProduccionID=@ProgramaProduccionID
  AND e.Activo=1;";

            await using (var cmd =
                tx == null
                    ? new SqlCommand(sqlPrograma, cn)
                    : new SqlCommand(sqlPrograma, cn, tx))
            {
                cmd.Parameters.Add(
                    "@EjecucionProduccionID",
                    SqlDbType.Int).Value =
                    ejecucionProduccionId;

                cmd.Parameters.Add(
                    "@ProgramaProduccionID",
                    SqlDbType.Int).Value =
                    programaProduccionId;

                await using var rd =
                    await cmd.ExecuteReaderAsync();

                if (await rd.ReadAsync())
                {
                    inicioReal =
                        rd["FechaInicioReal"] == DBNull.Value
                            ? null
                            : Convert.ToDateTime(
                                rd["FechaInicioReal"]);

                    finReal =
                        rd["FechaFinReal"] == DBNull.Value
                            ? null
                            : Convert.ToDateTime(
                                rd["FechaFinReal"]);

                    cantidadPlaneada =
                        rd["CantidadPlaneada"] == DBNull.Value
                            ? 0
                            : Convert.ToInt32(
                                rd["CantidadPlaneada"]);

                    objetivoHora =
                        rd["ObjetivoHora"] == DBNull.Value
                            ? null
                            : Convert.ToInt32(
                                rd["ObjetivoHora"]);
                }
            }

            if (!inicioReal.HasValue)
                return filas;

            DateTime AlMinuto(DateTime value)
            {
                return new DateTime(
                    value.Year,
                    value.Month,
                    value.Day,
                    value.Hour,
                    value.Minute,
                    0,
                    value.Kind);
            }

            var inicio =
                AlMinuto(
                    inicioReal.Value);

            var ahora =
                DateTime.Now;

            // ============================================================
            // REGISTROS HORARIOS YA CAPTURADOS
            // ============================================================

            var registros =
                new List<ProduccionRegistroHoraVm>();

            const string sqlRegistros = @"
SELECT
    RegistroHoraID,
    EjecucionProduccionID,
    ProgramaProduccionID,
    SolicitudProduccionID,
    MaquinaID,
    OperadorID,
    FechaProduccion,
    HoraInicio,
    HoraFin,
    ISNULL(CantidadOK,0) AS CantidadOK,
    ISNULL(CantidadSospechosa,0) AS CantidadSospechosa,
    ISNULL(CantidadScrap,0) AS CantidadScrap,
    ObjetivoHora,
    ObjetivoBloque,
    CumplioObjetivo,
    DiferenciaObjetivo,
    PorcentajeCumplimiento,
    Observaciones,
    UsuarioCreacionID,
    FechaCreacion,
    UsuarioModificacionID,
    FechaModificacion,
    Activo
FROM dbo.Produccion_RegistroHora
WHERE EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1
ORDER BY
    FechaProduccion,
    HoraInicio;";

            await using (var cmd =
                tx == null
                    ? new SqlCommand(sqlRegistros, cn)
                    : new SqlCommand(sqlRegistros, cn, tx))
            {
                cmd.Parameters.Add(
                    "@EjecucionProduccionID",
                    SqlDbType.Int).Value =
                    ejecucionProduccionId;

                await using var rd =
                    await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    registros.Add(
                        new ProduccionRegistroHoraVm
                        {
                            RegistroHoraID =
                                Convert.ToInt32(
                                    rd["RegistroHoraID"]),

                            EjecucionProduccionID =
                                Convert.ToInt32(
                                    rd["EjecucionProduccionID"]),

                            ProgramaProduccionID =
                                Convert.ToInt32(
                                    rd["ProgramaProduccionID"]),

                            SolicitudProduccionID =
                                rd["SolicitudProduccionID"] ==
                                DBNull.Value
                                    ? null
                                    : Convert.ToInt32(
                                        rd["SolicitudProduccionID"]),

                            MaquinaID =
                                rd["MaquinaID"] ==
                                DBNull.Value
                                    ? null
                                    : Convert.ToInt32(
                                        rd["MaquinaID"]),

                            OperadorID =
                                rd["OperadorID"] ==
                                DBNull.Value
                                    ? null
                                    : Convert.ToInt32(
                                        rd["OperadorID"]),

                            FechaProduccion =
                                Convert.ToDateTime(
                                    rd["FechaProduccion"]),

                            HoraInicio =
                                (TimeSpan)rd["HoraInicio"],

                            HoraFin =
                                (TimeSpan)rd["HoraFin"],

                            CantidadOK =
                                Convert.ToInt32(
                                    rd["CantidadOK"]),

                            CantidadSospechosa =
                                Convert.ToInt32(
                                    rd["CantidadSospechosa"]),

                            CantidadScrap =
                                Convert.ToInt32(
                                    rd["CantidadScrap"]),

                            ObjetivoHora =
                                rd["ObjetivoHora"] ==
                                DBNull.Value
                                    ? null
                                    : Convert.ToInt32(
                                        rd["ObjetivoHora"]),

                            ObjetivoBloque =
                                rd["ObjetivoBloque"] ==
                                DBNull.Value
                                    ? null
                                    : Convert.ToInt32(
                                        rd["ObjetivoBloque"]),

                            CumplioObjetivo =
                                rd["CumplioObjetivo"] ==
                                DBNull.Value
                                    ? null
                                    : Convert.ToBoolean(
                                        rd["CumplioObjetivo"]),

                            DiferenciaObjetivo =
                                rd["DiferenciaObjetivo"] ==
                                DBNull.Value
                                    ? null
                                    : Convert.ToInt32(
                                        rd["DiferenciaObjetivo"]),

                            PorcentajeCumplimiento =
                                rd["PorcentajeCumplimiento"] ==
                                DBNull.Value
                                    ? null
                                    : Convert.ToDecimal(
                                        rd["PorcentajeCumplimiento"]),

                            Observaciones =
                                rd["Observaciones"] ==
                                DBNull.Value
                                    ? null
                                    : rd["Observaciones"]
                                        .ToString(),

                            UsuarioCreacionID =
                                rd["UsuarioCreacionID"] ==
                                DBNull.Value
                                    ? null
                                    : Convert.ToInt32(
                                        rd["UsuarioCreacionID"]),

                            FechaCreacion =
                                Convert.ToDateTime(
                                    rd["FechaCreacion"]),

                            UsuarioModificacionID =
                                rd["UsuarioModificacionID"] ==
                                DBNull.Value
                                    ? null
                                    : Convert.ToInt32(
                                        rd["UsuarioModificacionID"]),

                            FechaModificacion =
                                rd["FechaModificacion"] ==
                                DBNull.Value
                                    ? null
                                    : Convert.ToDateTime(
                                        rd["FechaModificacion"]),

                            Activo =
                                rd["Activo"] !=
                                DBNull.Value &&
                                Convert.ToBoolean(
                                    rd["Activo"])
                        });
                }
            }

            // ============================================================
            // HORAS TEÓRICAS
            // ============================================================

            var horasRequeridas = 0;

            if (cantidadPlaneada > 0 &&
                objetivoHora.HasValue &&
                objetivoHora.Value > 0)
            {
                horasRequeridas =
                    (int)Math.Ceiling(
                        (decimal)cantidadPlaneada /
                        objetivoHora.Value);
            }

            if (horasRequeridas <= 0)
            {
                horasRequeridas =
                    Math.Max(
                        1,
                        registros.Count + 1);
            }

            // ============================================================
            // NOMBRES DE OPERADORES DE REGISTROS EXISTENTES
            // ============================================================

            var nombresOperadores =
                new Dictionary<int, string>();

            var operadoresRegistro =
                registros
                    .Where(x =>
                        x.OperadorID.HasValue &&
                        x.OperadorID.Value > 0)
                    .Select(x =>
                        x.OperadorID!.Value)
                    .Distinct()
                    .ToList();

            if (operadoresRegistro.Count > 0)
            {
                var parametros =
                    new List<string>();

                await using var cmd =
                    tx == null
                        ? new SqlCommand()
                        : new SqlCommand(
                            string.Empty,
                            cn,
                            tx);

                cmd.Connection = cn;

                for (var i = 0;
                     i < operadoresRegistro.Count;
                     i++)
                {
                    var nombreParametro =
                        "@Operador" + i;

                    parametros.Add(
                        nombreParametro);

                    cmd.Parameters
                        .Add(
                            nombreParametro,
                            SqlDbType.Int)
                        .Value =
                        operadoresRegistro[i];
                }

                cmd.CommandText = $@"
SELECT
    PersonaID,
    LTRIM(
        RTRIM(
            CONCAT(
                ISNULL(Nombre,N''),
                N' ',
                ISNULL(ApellidoPaterno,N''),
                N' ',
                ISNULL(ApellidoMaterno,N'')
            )
        )
    ) AS NombreCompleto
FROM dbo.Persona
WHERE PersonaID IN(
    {string.Join(",", parametros)}
);";

                await using var rd =
                    await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    var personaId =
                        Convert.ToInt32(
                            rd["PersonaID"]);

                    var nombre =
                        rd["NombreCompleto"]
                            ?.ToString()
                            ?.Trim();

                    nombresOperadores[personaId] =
                        string.IsNullOrWhiteSpace(
                            nombre)
                            ? $"Operador {personaId}"
                            : nombre;
                }
            }

            // ============================================================
            // PAROS
            // ============================================================

            var paros =
                new List<(
                    int ParoID,
                    DateTime Inicio,
                    DateTime? Fin,
                    bool MayorA15)>();

            const string sqlParos = @"
SELECT
    ParoID,
    FechaInicioParo,
    FechaFinParo,
    CASE
        WHEN ISNULL(EsMayorA15Minutos,0)=1
            THEN CAST(1 AS BIT)

        WHEN FechaFinParo IS NOT NULL
         AND DATEDIFF(
                SECOND,
                FechaInicioParo,
                FechaFinParo
             ) > 900
            THEN CAST(1 AS BIT)

        ELSE CAST(0 AS BIT)
    END AS EsMayorA15Minutos
FROM dbo.Produccion_Paros
WHERE EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1
ORDER BY
    FechaInicioParo,
    ParoID;";

            await using (var cmd =
                tx == null
                    ? new SqlCommand(sqlParos, cn)
                    : new SqlCommand(sqlParos, cn, tx))
            {
                cmd.Parameters.Add(
                    "@EjecucionProduccionID",
                    SqlDbType.Int).Value =
                    ejecucionProduccionId;

                await using var rd =
                    await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    paros.Add(
                        (
                            Convert.ToInt32(
                                rd["ParoID"]),

                            Convert.ToDateTime(
                                rd["FechaInicioParo"]),

                            rd["FechaFinParo"] ==
                            DBNull.Value
                                ? null
                                : Convert.ToDateTime(
                                    rd["FechaFinParo"]),

                            rd["EsMayorA15Minutos"] !=
                            DBNull.Value &&
                            Convert.ToBoolean(
                                rd["EsMayorA15Minutos"])
                        ));
                }
            }

            // ============================================================
            // CONFIRMACIONES DE INICIO / REINICIO DE SERIE
            // ============================================================

            var confirmacionesSerie =
                new List<DateTime>();

            const string sqlConfirmaciones = @"
SELECT
    h.FechaMovimiento
FROM dbo.Calidad_InspeccionHistorial h
INNER JOIN dbo.Calidad_Inspecciones ci
    ON ci.InspeccionID=h.InspeccionID
WHERE ci.EjecucionProduccionID=@EjecucionProduccionID
  AND h.Movimiento=N'CONFIRMACION_INICIO_SERIE_PRODUCCION'
ORDER BY
    h.FechaMovimiento;";

            await using (var cmd =
                tx == null
                    ? new SqlCommand(
                        sqlConfirmaciones,
                        cn)
                    : new SqlCommand(
                        sqlConfirmaciones,
                        cn,
                        tx))
            {
                cmd.Parameters.Add(
                    "@EjecucionProduccionID",
                    SqlDbType.Int).Value =
                    ejecucionProduccionId;

                await using var rd =
                    await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    confirmacionesSerie.Add(
                        Convert.ToDateTime(
                            rd["FechaMovimiento"]));
                }
            }

            // ============================================================
            // CONSTRUIR INTERVALOS NO PRODUCTIVOS
            //
            // PARO CORTO:
            // Inicio paro -> Fin paro
            //
            // PARO > 15:
            // Inicio paro -> Nueva confirmación de serie
            //
            // PARO ABIERTO / SIN RELIBERACIÓN:
            // Inicio paro -> indefinido
            // ============================================================

            var interrupciones =
                new List<(
                    DateTime Inicio,
                    DateTime? Fin)>();

            foreach (var paro in paros)
            {
                var inicioParo =
                    AlMinuto(
                        paro.Inicio);

                if (paro.Fin.HasValue)
                {
                    var finParo =
                        AlMinuto(
                            paro.Fin.Value);

                    if (finParo <
                        inicioParo)
                    {
                        finParo =
                            inicioParo;
                    }

                    if (!paro.MayorA15)
                    {
                        // Paro corto:
                        // solamente el periodo real del paro es no productivo.
                        interrupciones.Add(
                            (
                                inicioParo,
                                finParo
                            ));

                        continue;
                    }

                    // Paro > 15:
                    // Producción no vuelve a ser productiva
                    // hasta que exista nueva confirmación de serie.
                    var confirmacion =
                        confirmacionesSerie
                            .Where(x =>
                                AlMinuto(x) >=
                                finParo)
                            .OrderBy(x => x)
                            .Select(AlMinuto)
                            .FirstOrDefault();

                    if (confirmacion ==
                        DateTime.MinValue)
                    {
                        interrupciones.Add(
                            (
                                inicioParo,
                                null
                            ));
                    }
                    else
                    {
                        interrupciones.Add(
                            (
                                inicioParo,
                                confirmacion
                            ));
                    }

                    continue;
                }

                // Paro todavía abierto.
                interrupciones.Add(
                    (
                        inicioParo,
                        null
                    ));
            }

            // ============================================================
            // NORMALIZAR Y FUSIONAR PAROS TRASLAPADOS
            //
            // Con esto evitamos que datos duplicados/solapados generen
            // falsos huecos productivos entre dos registros de paro.
            // ============================================================

            var interrupcionesNormalizadas =
                interrupciones
                    .Where(x =>
                        !x.Fin.HasValue ||
                        x.Fin.Value >
                        inicio)
                    .Select(x =>
                        (
                            Inicio:
                                x.Inicio < inicio
                                    ? inicio
                                    : x.Inicio,

                            Fin:
                                x.Fin
                        ))
                    .OrderBy(x =>
                        x.Inicio)
                    .ToList();

            var interrupcionesFusionadas =
                new List<(
                    DateTime Inicio,
                    DateTime? Fin)>();

            foreach (var actual in
                     interrupcionesNormalizadas)
            {
                if (interrupcionesFusionadas.Count == 0)
                {
                    interrupcionesFusionadas.Add(
                        actual);

                    continue;
                }

                var ultimo =
                    interrupcionesFusionadas[
                        interrupcionesFusionadas.Count - 1
                    ];

                // Si el anterior es abierto, ya cubre todo lo posterior.
                if (!ultimo.Fin.HasValue)
                    continue;

                if (actual.Inicio <=
                    ultimo.Fin.Value)
                {
                    DateTime? nuevoFin;

                    if (!actual.Fin.HasValue)
                    {
                        nuevoFin = null;
                    }
                    else
                    {
                        nuevoFin =
                            actual.Fin.Value >
                            ultimo.Fin.Value
                                ? actual.Fin.Value
                                : ultimo.Fin.Value;
                    }

                    interrupcionesFusionadas[
                        interrupcionesFusionadas.Count - 1
                    ] =
                        (
                            ultimo.Inicio,
                            nuevoFin
                        );

                    continue;
                }

                interrupcionesFusionadas.Add(
                    actual);
            }

            // ============================================================
            // LÍMITE DE GENERACIÓN
            // ============================================================

            var limiteSeguridad =
                inicio.AddHours(500);

            DateTime limite;

            if (finReal.HasValue)
            {
                limite =
                    AlMinuto(
                        finReal.Value);
            }
            else
            {
                var minutosInterrupcionesCerradas =
                    interrupcionesFusionadas
                        .Where(x =>
                            x.Fin.HasValue)
                        .Sum(x =>
                            Math.Max(
                                0,
                                (
                                    x.Fin!.Value -
                                    x.Inicio
                                ).TotalMinutes));

                /*
                 * Las interrupciones cerradas desplazan el tiempo
                 * necesario para completar las horas productivas.
                 */
                var limiteTeorico =
                    inicio
                        .AddHours(
                            horasRequeridas)
                        .AddMinutes(
                            minutosInterrupcionesCerradas);

                /*
                 * Si la ejecución está tardando más que lo teórico,
                 * debemos seguir permitiendo nuevas capturas.
                 *
                 * Dejamos preparada como máximo una hora hacia adelante
                 * respecto del momento actual.
                 */
                var limitePorOperacion =
                    AlMinuto(ahora)
                        .AddHours(1);

                limite =
                    limiteTeorico >
                    limitePorOperacion
                        ? limiteTeorico
                        : limitePorOperacion;
            }

            if (limite >
                limiteSeguridad)
            {
                limite =
                    limiteSeguridad;
            }

            if (limite <= inicio)
            {
                limite =
                    inicio.AddHours(1);
            }

            // ============================================================
            // BUSCAR UN REGISTRO EXISTENTE PARA UNA FILA GENERADA
            // ============================================================

            var registrosDisponibles =
                registros.ToList();

            ProduccionRegistroHoraVm?
                BuscarRegistroBloque(
                    DateTime bloqueInicio,
                    DateTime bloqueFin)
            {
                foreach (var item in
                         registrosDisponibles.ToList())
                {
                    var registroInicio =
                        item.FechaProduccion.Date
                            .Add(
                                item.HoraInicio);

                    var registroFin =
                        item.FechaProduccion.Date
                            .Add(
                                item.HoraFin);

                    if (registroFin <=
                        registroInicio)
                    {
                        registroFin =
                            registroFin.AddDays(1);
                    }

                    registroInicio =
                        AlMinuto(
                            registroInicio);

                    registroFin =
                        AlMinuto(
                            registroFin);

                    if (registroInicio ==
                            bloqueInicio &&
                        registroFin ==
                            bloqueFin)
                    {
                        registrosDisponibles.Remove(
                            item);

                        return item;
                    }
                }

                ProduccionRegistroHoraVm?
                    mejorRegistro = null;

                var mejorTraslapeMinutos =
                    0d;

                foreach (var item in
                         registrosDisponibles)
                {
                    var registroInicio =
                        item.FechaProduccion.Date
                            .Add(
                                item.HoraInicio);

                    var registroFin =
                        item.FechaProduccion.Date
                            .Add(
                                item.HoraFin);

                    if (registroFin <=
                        registroInicio)
                    {
                        registroFin =
                            registroFin.AddDays(1);
                    }

                    var inicioTraslape =
                        registroInicio >
                        bloqueInicio
                            ? registroInicio
                            : bloqueInicio;

                    var finTraslape =
                        registroFin <
                        bloqueFin
                            ? registroFin
                            : bloqueFin;

                    var minutosTraslape =
                        Math.Max(
                            0,
                            (
                                finTraslape -
                                inicioTraslape
                            ).TotalMinutes);

                    if (minutosTraslape >
                        mejorTraslapeMinutos)
                    {
                        mejorTraslapeMinutos =
                            minutosTraslape;

                        mejorRegistro =
                            item;
                    }
                }

                var duracionBloqueMinutos =
                    Math.Max(
                        1,
                        (
                            bloqueFin -
                            bloqueInicio
                        ).TotalMinutes);

                var traslapeMinimo =
                    Math.Min(
                        30,
                        duracionBloqueMinutos /
                        2d);

                if (mejorRegistro == null ||
                    mejorTraslapeMinutos <
                    traslapeMinimo)
                {
                    return null;
                }

                registrosDisponibles.Remove(
                    mejorRegistro);

                return mejorRegistro;
            }

            // ============================================================
            // GENERAR FILAS ÚNICAMENTE DE TIEMPO PRODUCTIVO
            // ============================================================

            var numeroHora =
                1;

            void AgregarSegmentoProductivo(
                DateTime segmentoInicio,
                DateTime segmentoFin)
            {
                segmentoInicio =
                    AlMinuto(
                        segmentoInicio);

                segmentoFin =
                    AlMinuto(
                        segmentoFin);

                if (segmentoFin <=
                    segmentoInicio)
                {
                    return;
                }

                var inicioBloque =
                    segmentoInicio;

                while (inicioBloque <
                       segmentoFin)
                {
                    var finBloque =
                        inicioBloque
                            .AddHours(1);

                    /*
                     * Si el segmento productivo termina por un paro,
                     * se conserva el bloque parcial.
                     *
                     * Ejemplo:
                     * Producción 12:00 - 12:03
                     * Paro inicia 12:03
                     *
                     * Resultado:
                     * fila de 3 minutos productivos.
                     */
                    if (finBloque >
                        segmentoFin)
                    {
                        finBloque =
                            segmentoFin;
                    }

                    if (finBloque >
                        limite)
                    {
                        finBloque =
                            limite;
                    }

                    if (finReal.HasValue &&
                        finBloque >
                        AlMinuto(
                            finReal.Value))
                    {
                        finBloque =
                            AlMinuto(
                                finReal.Value);
                    }

                    if (finBloque <=
                        inicioBloque)
                    {
                        break;
                    }

                    /*
                     * PROTECCIÓN EXTRA:
                     * una fila productiva jamás puede traslaparse
                     * con un paro.
                     */
                    var traslapaParo =
                        interrupcionesFusionadas
                            .Any(x =>
                            {
                                var finInterrupcion =
                                    x.Fin ??
                                    limiteSeguridad;

                                return
                                    x.Inicio <
                                        finBloque &&
                                    finInterrupcion >
                                        inicioBloque;
                            });

                    if (traslapaParo)
                    {
                        throw new InvalidOperationException(
                            $"Se intentó generar un bloque productivo " +
                            $"{inicioBloque:dd/MM HH:mm} - " +
                            $"{finBloque:dd/MM HH:mm} " +
                            "que se traslapa con un paro. " +
                            "La captura fue bloqueada para evitar contabilizar tiempo detenido.");
                    }

                    var registro =
                        BuscarRegistroBloque(
                            inicioBloque,
                            finBloque);

                    var capturada =
                        registro != null;

                    var bloqueTerminado =
                        ahora >=
                        finBloque;

                    var minutosBloque =
                        (
                            finBloque -
                            inicioBloque
                        ).TotalMinutes;

                    int?
                        objetivoBloqueCalculado =
                            null;

                    if (objetivoHora.HasValue &&
                        objetivoHora.Value > 0 &&
                        minutosBloque > 0)
                    {
                        objetivoBloqueCalculado =
                            (int)Math.Round(
                                objetivoHora.Value *
                                minutosBloque /
                                60d,
                                MidpointRounding
                                    .AwayFromZero);
                    }

                    var objetivoHoraFila =
                        capturada
                            ? registro!.ObjetivoHora ??
                              objetivoHora
                            : objetivoHora;

                    var objetivoBloqueFila =
                        capturada
                            ? registro!.ObjetivoBloque ??
                              objetivoBloqueCalculado
                            : objetivoBloqueCalculado;

                    var operadorRegistroId =
                        registro?.OperadorID;

                    string?
                        operadorRegistroNombre =
                            null;

                    if (operadorRegistroId
                            .HasValue &&
                        nombresOperadores
                            .TryGetValue(
                                operadorRegistroId.Value,
                                out var nombreEncontrado))
                    {
                        operadorRegistroNombre =
                            nombreEncontrado;
                    }

                    filas.Add(
                        new ProduccionCapturaHoraFilaVm
                        {
                            NumeroHora =
                                numeroHora,

                            FechaProduccion =
                                inicioBloque.Date,

                            HoraInicio =
                                inicioBloque.TimeOfDay,

                            HoraFin =
                                finBloque.TimeOfDay,

                            RegistroHoraID =
                                registro?.RegistroHoraID,

                            OperadorID =
                                operadorRegistroId,

                            OperadorNombre =
                                operadorRegistroNombre,

                            CantidadOK =
                                registro?.CantidadOK ??
                                0,

                            CantidadSospechosa =
                                registro
                                    ?.CantidadSospechosa ??
                                0,

                            CantidadScrap =
                                registro?.CantidadScrap ??
                                0,

                            ObjetivoHora =
                                objetivoHoraFila,

                            ObjetivoBloque =
                                objetivoBloqueFila,

                            Observaciones =
                                registro?.Observaciones,

                            Capturada =
                                capturada,

                            Disponible =
                                !capturada &&
                                bloqueTerminado,

                            Vencida =
                                !capturada &&
                                bloqueTerminado
                        });

                    inicioBloque =
                        finBloque;

                    numeroHora++;
                }
            }

            // ============================================================
            // GENERAR EL COMPLEMENTO PRODUCTIVO DE LOS PAROS
            // ============================================================

            var cursorProductivo =
                inicio;

            foreach (var interrupcion in
                     interrupcionesFusionadas)
            {
                var inicioInterrupcion =
                    interrupcion.Inicio;

                if (inicioInterrupcion >=
                    limite)
                {
                    break;
                }

                /*
                 * Todo lo que exista ANTES del paro sí es producción.
                 */
                if (inicioInterrupcion >
                    cursorProductivo)
                {
                    var finProductivo =
                        inicioInterrupcion >
                        limite
                            ? limite
                            : inicioInterrupcion;

                    AgregarSegmentoProductivo(
                        cursorProductivo,
                        finProductivo);
                }

                /*
                 * PARO ABIERTO:
                 * no existe producción después de él todavía.
                 */
                if (!interrupcion.Fin.HasValue)
                {
                    cursorProductivo =
                        limite;

                    break;
                }

                /*
                 * Saltamos COMPLETAMENTE el periodo detenido.
                 *
                 * Ejemplo:
                 *
                 * paro 12:29 - 12:32
                 *
                 * cursor pasa directamente a 12:32.
                 * JAMÁS se genera 12:29 - 12:32.
                 */
                if (interrupcion.Fin.Value >
                    cursorProductivo)
                {
                    cursorProductivo =
                        interrupcion.Fin.Value;
                }
            }

            /*
             * Después del último paro, continúa la producción.
             */
            if (cursorProductivo <
                limite)
            {
                AgregarSegmentoProductivo(
                    cursorProductivo,
                    limite);
            }

            // ============================================================
            // SOLAMENTE LA PRIMERA HORA PENDIENTE TERMINADA SE HABILITA
            // ============================================================

            var primeraPendiente =
                filas
                    .Where(x =>
                        !x.Capturada &&
                        x.Disponible)
                    .OrderBy(x =>
                        x.NumeroHora)
                    .FirstOrDefault();

            foreach (var fila in
                     filas.Where(x =>
                         !x.Capturada))
            {
                fila.Disponible =
                    primeraPendiente != null &&
                    fila.NumeroHora ==
                    primeraPendiente.NumeroHora;

                if (!fila.Disponible)
                {
                    var fechaInicioFila =
                        fila.FechaProduccion.Date
                            .Add(
                                fila.HoraInicio);

                    var fechaFinFila =
                        fila.FechaProduccion.Date
                            .Add(
                                fila.HoraFin);

                    if (fechaFinFila <=
                        fechaInicioFila)
                    {
                        fechaFinFila =
                            fechaFinFila.AddDays(1);
                    }

                    fila.Vencida =
                        ahora >=
                        fechaFinFila;
                }
            }

            return filas;
        }

        private bool UsuarioEnSesion()
        {
            return HttpContext.Session.GetInt32("UsuarioID").HasValue;
        }

        private int ObtenerUsuarioID()
        {
            return HttpContext.Session.GetInt32("UsuarioID") ?? 0;
        }

        private sealed class ValidacionEnvioCajaCalidad
        {
            public bool Permitido { get; set; }
            public int? InspeccionID { get; set; }
            public string Mensaje { get; set; } = string.Empty;
        }
        private static async Task<ValidacionEnvioCajaCalidad> ValidarEnvioCajaCalidadAsync(int ejecucionProduccionId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
DECLARE @InspeccionID INT;
DECLARE @Estado NVARCHAR(50);
DECLARE @ConfiguracionInvalidada BIT;
DECLARE @RequiereReliberacion BIT;
DECLARE @Liberado BIT;
DECLARE @DisposicionesPendientes INT;
SELECT TOP (1)
    @InspeccionID=ci.InspeccionID,
    @Estado=UPPER(LTRIM(RTRIM(ISNULL(ci.Estado,N'')))),
    @ConfiguracionInvalidada=ISNULL(ci.ConfiguracionInvalidada,0),
    @RequiereReliberacion=ISNULL(ci.RequiereReliberacion,0),
    @Liberado=ISNULL(ci.Liberado,0)
FROM dbo.Calidad_Inspecciones ci WITH (UPDLOCK,HOLDLOCK)
WHERE ci.EjecucionProduccionID=@EjecucionProduccionID
  AND ci.Estado<>N'CERRADA'
ORDER BY ci.InspeccionID DESC;
IF @InspeccionID IS NULL
BEGIN
    SELECT CAST(0 AS BIT) Permitido,CAST(NULL AS INT) InspeccionID,N'No existe una inspección activa de Calidad relacionada con la ejecución.' Mensaje;
    RETURN;
END;
IF @ConfiguracionInvalidada=1
BEGIN
    SELECT CAST(0 AS BIT) Permitido,@InspeccionID InspeccionID,N'La configuración de la corrida fue invalidada. Debe completarse la revisión de Calidad antes de enviar cajas.' Mensaje;
    RETURN;
END;
IF @RequiereReliberacion=1 OR @Estado=N'PENDIENTE_RELIBERACION'
BEGIN
    SELECT CAST(0 AS BIT) Permitido,@InspeccionID InspeccionID,N'La corrida requiere reliberación de Calidad después de un paro. No se pueden enviar cajas mientras esté pendiente.' Mensaje;
    RETURN;
END;
IF @Liberado=0
BEGIN
    SELECT CAST(0 AS BIT) Permitido,@InspeccionID InspeccionID,N'Calidad no tiene liberada actualmente la producción.' Mensaje;
    RETURN;
END;
IF @Estado<>N'MONITOREO_ACTIVO'
BEGIN
    SELECT CAST(0 AS BIT) Permitido,@InspeccionID InspeccionID,N'La inspección debe encontrarse en monitoreo activo para recibir cajas de Producción.' Mensaje;
    RETURN;
END;
SELECT @DisposicionesPendientes=COUNT(1)
FROM dbo.Calidad_DisposicionesMaterial d WITH (UPDLOCK,HOLDLOCK)
WHERE d.InspeccionID=@InspeccionID
  AND d.Activo=1
  AND UPPER(LTRIM(RTRIM(ISNULL(d.ResultadoFinal,N''))))=N'PENDIENTE';
IF ISNULL(@DisposicionesPendientes,0)>0
BEGIN
    SELECT CAST(0 AS BIT) Permitido,@InspeccionID InspeccionID,CONCAT(N'Existen ',@DisposicionesPendientes,N' disposición(es) de material pendientes. Calidad debe resolverlas antes de recibir o liberar nuevas cajas.') Mensaje;
    RETURN;
END;
IF EXISTS
(
    SELECT 1
    FROM dbo.Calidad_MonitoreosProceso m
    WHERE m.InspeccionID=@InspeccionID
      AND m.Activo=1
      AND m.RegistroHoraID IS NOT NULL
      AND UPPER(LTRIM(RTRIM(ISNULL(m.Resultado,N''))))=N'PENDIENTE'
      AND (ISNULL(m.CantidadSospechosa,0)>0 OR ISNULL(m.CantidadNoRecuperable,0)>0)
)
BEGIN
    SELECT CAST(0 AS BIT) Permitido,@InspeccionID InspeccionID,N'Existen capturas con material sospechoso o scrap reportado que todavía no han sido evaluadas por Calidad.' Mensaje;
    RETURN;
END;
SELECT CAST(1 AS BIT) Permitido,@InspeccionID InspeccionID,N'La caja puede enviarse a Calidad.' Mensaje;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) return new ValidacionEnvioCajaCalidad { Permitido = false, Mensaje = "No fue posible validar el estado de Calidad." };
            return new ValidacionEnvioCajaCalidad
            {
                Permitido = rd["Permitido"] != DBNull.Value && Convert.ToBoolean(rd["Permitido"]),
                InspeccionID = rd["InspeccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["InspeccionID"]),
                Mensaje = rd["Mensaje"]?.ToString() ?? "La caja no puede enviarse a Calidad."
            };
        }

        private async Task<bool> ExisteRegistroHoraAsync(int ejecucionProduccionId, DateTime fechaProduccion, TimeSpan horaInicio, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
SELECT CAST(CASE WHEN EXISTS
(
    SELECT 1
    FROM dbo.Produccion_RegistroHora
    WHERE EjecucionProduccionID=@EjecucionProduccionID AND FechaProduccion=@FechaProduccion AND HoraInicio=@HoraInicio AND Activo=1
) THEN 1 ELSE 0 END AS BIT);";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
            cmd.Parameters.Add("@FechaProduccion", SqlDbType.Date).Value = fechaProduccion.Date;
            cmd.Parameters.Add("@HoraInicio", SqlDbType.Time).Value = horaInicio;
            return Convert.ToBoolean(await cmd.ExecuteScalarAsync());
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

        private async Task<ProduccionCambioTurnoSugerenciaVm?> ObtenerSugerenciaTecnicoCambioTurnoAsync(int ejecucionProduccionId, SqlConnection cn, SqlTransaction? tx = null)
        {
            if (ejecucionProduccionId <= 0) return null;
            const string sql = @"
SELECT TOP(1)
    s.CambioTurnoSugerenciaID,
    s.EjecucionProduccionID,
    s.ProgramaProduccionID,
    s.OperadorSugeridoID,
    LTRIM(RTRIM(CONCAT(ISNULL(op.Nombre,N''),N' ',ISNULL(op.ApellidoPaterno,N''),N' ',ISNULL(op.ApellidoMaterno,N'')))) AS OperadorSugeridoNombre,
    s.UsuarioTecnicoID,
    s.FechaSugerencia,
    s.Observaciones,
    ISNULL(s.Utilizada,0) AS Utilizada,
    ISNULL(s.Activo,1) AS Activo,
    s.UsuarioModificacionID,
    s.FechaModificacion
FROM dbo.Produccion_CambioTurnoSugerencias s
INNER JOIN dbo.Persona op ON op.PersonaID=s.OperadorSugeridoID
WHERE s.EjecucionProduccionID=@EjecucionProduccionID
  AND s.Activo=1
  AND ISNULL(s.Utilizada,0)=0
  AND ISNULL(op.EsColaboradorActivo,1)=1
ORDER BY s.FechaSugerencia DESC,s.CambioTurnoSugerenciaID DESC;";
            ProduccionCambioTurnoSugerenciaVm? sugerencia = null;
            await using (var cmd = tx == null ? new SqlCommand(sql, cn) : new SqlCommand(sql, cn, tx))
            {
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                await using var rd = await cmd.ExecuteReaderAsync();
                if (!await rd.ReadAsync()) return null;
                sugerencia = new ProduccionCambioTurnoSugerenciaVm
                {
                    CambioTurnoSugerenciaID = Convert.ToInt32(rd["CambioTurnoSugerenciaID"]),
                    EjecucionProduccionID = Convert.ToInt32(rd["EjecucionProduccionID"]),
                    ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                    OperadorSugeridoID = Convert.ToInt32(rd["OperadorSugeridoID"]),
                    OperadorSugeridoNombre = rd["OperadorSugeridoNombre"]?.ToString()?.Trim() ?? string.Empty,
                    UsuarioTecnicoID = Convert.ToInt32(rd["UsuarioTecnicoID"]),
                    FechaSugerencia = Convert.ToDateTime(rd["FechaSugerencia"]),
                    Observaciones = rd["Observaciones"] == DBNull.Value ? null : rd["Observaciones"]?.ToString()?.Trim(),
                    Utilizada = Convert.ToBoolean(rd["Utilizada"]),
                    Activo = Convert.ToBoolean(rd["Activo"]),
                    UsuarioModificacionID = rd["UsuarioModificacionID"] == DBNull.Value ? null : Convert.ToInt32(rd["UsuarioModificacionID"]),
                    FechaModificacion = rd["FechaModificacion"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaModificacion"])
                };
            }
            const string sqlTecnico = @"
SELECT TOP(1)
    LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))) AS Nombre
FROM dbo.Persona p
WHERE p.PersonaID=@PersonaID;";
            await using (var cmd = tx == null ? new SqlCommand(sqlTecnico, cn) : new SqlCommand(sqlTecnico, cn, tx))
            {
                cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = sugerencia.UsuarioTecnicoID;
                var resultado = await cmd.ExecuteScalarAsync();
                sugerencia.TecnicoNombre = resultado == null || resultado == DBNull.Value
                    ? "Técnico de Producción"
                    : resultado.ToString()?.Trim() ?? "Técnico de Producción";
            }
            return sugerencia;
        }

        private async Task<ProduccionOperadorTabletVm?>
    ObtenerTabletHistorialVmAsync(
        int ejecucionProduccionId,
        SqlConnection cn)
        {
            if (ejecucionProduccionId <= 0)
                return null;

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
    ISNULL(e.CantidadOKTotal,0) AS CantidadOKTotal,
    ISNULL(e.CantidadSospechosaTotal,0) AS CantidadSospechosaTotal,
    ISNULL(e.CantidadScrapTotal,0) AS CantidadScrapTotal,

    dt.ObjetivoHora,
    dt.Ciclo,
    dt.Cavidades,

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
        THEN 1
        ELSE 0
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

OUTER APPLY
(
    SELECT TOP (1)
        dt0.ObjetivoHora,
        dt0.Ciclo,
        dt0.Cavidades
    FROM dbo.ERP_ParteDatosTecnicos dt0
    WHERE dt0.ParteID = e.ParteID
      AND dt0.Activo = 1
    ORDER BY dt0.ParteDatoTecnicoID DESC
) dt

WHERE e.EjecucionProduccionID = @EjecucionProduccionID
  AND e.Activo = 1
  AND e.EstatusID IN
  (
      @TerminadoParcial,
      @Terminado,
      @ListaCierreDocumental,
      @Cerrado
  );";

            await using var cmd = new SqlCommand(sql, cn);

            cmd.Parameters.Add(
                "@EjecucionProduccionID",
                SqlDbType.Int).Value =
                ejecucionProduccionId;

            cmd.Parameters.Add(
                "@TerminadoParcial",
                SqlDbType.Int).Value =
                ProduccionEstatus.TerminadoParcial;

            cmd.Parameters.Add(
                "@Terminado",
                SqlDbType.Int).Value =
                ProduccionEstatus.Terminado;

            cmd.Parameters.Add(
                "@ListaCierreDocumental",
                SqlDbType.Int).Value =
                ProduccionEstatus.ListaCierreDocumental;

            cmd.Parameters.Add(
                "@Cerrado",
                SqlDbType.Int).Value =
                ProduccionEstatus.Cerrado;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return null;

            var vm = MapearTabletVm(rd);

            AsignarHoraSugerida(vm);

            return vm;
        }

        private async Task<int?> ObtenerPersonaIDUsuarioAsync(
    int usuarioId,
    SqlConnection cn)
        {
            const string sql = @"
SELECT TOP (1)
    u.PersonaID
FROM dbo.Usuarios u
WHERE u.UsuarioID = @UsuarioID
  AND u.Activo = 1
  AND u.PersonaID IS NOT NULL;";

            await using var cmd = new SqlCommand(sql, cn);

            cmd.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value =
                usuarioId;

            var resultado =
                await cmd.ExecuteScalarAsync();

            if (resultado == null ||
                resultado == DBNull.Value)
            {
                return null;
            }

            return Convert.ToInt32(resultado);
        }

        private static async Task<int> ObtenerCantidadCajasNormalesAsync(int ejecucionProduccionId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
SELECT COUNT(1)
FROM dbo.Produccion_Cajas c WITH(UPDLOCK,HOLDLOCK)
WHERE c.EjecucionProduccionID=@EjecucionProduccionID
  AND c.Activo=1
  AND UPPER(LTRIM(RTRIM(ISNULL(c.TipoCaja,N'OK'))))=N'OK'
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Produccion_CajaOrigenDetalle od
      WHERE od.CajaProduccionID=c.CajaProduccionID
        AND od.Activo=1
  );";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }


        private static async Task<bool> ExisteCodigoBarrasCajaAsync(string codigoBarras, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
SELECT CAST(CASE WHEN EXISTS
(
    SELECT 1
    FROM dbo.Produccion_Cajas WITH(UPDLOCK,HOLDLOCK)
    WHERE Activo=1
      AND CodigoBarrasOrigen=@CodigoBarrasOrigen
) THEN 1 ELSE 0 END AS BIT);";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@CodigoBarrasOrigen", SqlDbType.NVarChar, 500).Value = codigoBarras.Trim();
            return Convert.ToBoolean(await cmd.ExecuteScalarAsync());
        }

        private static string NormalizarValorEscaneo(string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor)) return string.Empty;
            return new string(valor.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        }
        private static long EnteroLargo(SqlDataReader rd, string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? 0L
                : Convert.ToInt64(rd.GetValue(ordinal));
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

        private async Task<List<ProduccionTiempoExtraVm>> ObtenerHistorialTiempoExtraAsync(int ejecucionProduccionId, SqlConnection cn)
        {
            var lista = new List<ProduccionTiempoExtraVm>();
            if (ejecucionProduccionId <= 0) return lista;

            const string sql = @"
SELECT TOP(20)
    te.TiempoExtraID,
    te.EjecucionProduccionID,
    te.OperadorInicioID,
    LTRIM(RTRIM(CONCAT(ISNULL(pi.Nombre,N''),N' ',ISNULL(pi.ApellidoPaterno,N''),N' ',ISNULL(pi.ApellidoMaterno,N'')))) AS OperadorInicioNombre,
    te.OperadorFinID,
    LTRIM(RTRIM(CONCAT(ISNULL(pf.Nombre,N''),N' ',ISNULL(pf.ApellidoPaterno,N''),N' ',ISNULL(pf.ApellidoMaterno,N'')))) AS OperadorFinNombre,
    te.ConfiguracionCorridaInicioID,
    te.FechaHoraInicio,
    te.FechaHoraUltimoCorte,
    te.FechaHoraFin,
    te.ContadorInicio,
    te.ContadorUltimoCorte,
    te.ContadorFin,
    te.Estado,
    te.Motivo,
    te.Observaciones,
    te.UsuarioCreacionID,
    te.FechaCreacion,
    te.UsuarioModificacionID,
    te.FechaModificacion,
    te.UsuarioCancelacionID,
    te.FechaCancelacion,
    te.MotivoCancelacion,
    te.Activo
FROM dbo.Produccion_TiempoExtra te
LEFT JOIN dbo.Persona pi ON pi.PersonaID=te.OperadorInicioID
LEFT JOIN dbo.Persona pf ON pf.PersonaID=te.OperadorFinID
WHERE te.EjecucionProduccionID=@EjecucionProduccionID
  AND te.Activo=1
ORDER BY te.TiempoExtraID DESC;";

            await using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                await using var rd = await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync()) lista.Add(MapearTiempoExtra(rd));
            }

            foreach (var item in lista) item.Cortes = await ObtenerCortesTiempoExtraAsync(item.TiempoExtraID, cn);

            return lista;
        }

        private async Task<List<ProduccionTiempoExtraCorteVm>> ObtenerCortesTiempoExtraAsync(int tiempoExtraId, SqlConnection cn, SqlTransaction? tx = null)
        {
            var lista = new List<ProduccionTiempoExtraCorteVm>();
            if (tiempoExtraId <= 0) return lista;

            const string sql = @"
SELECT
    rh.RegistroHoraID,
    rh.TiempoExtraID,
    ISNULL(rh.NumeroCorteTiempoExtra,0) AS NumeroCorteTiempoExtra,
    ISNULL(rh.OperadorID,0) AS OperadorID,
    LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))) AS OperadorNombre,
    rh.FechaProduccion,
    rh.HoraInicio,
    rh.HoraFin,
    si.ContadorInicial,
    sf.ContadorFinal,
    ISNULL(rh.PiezasCalculadasContador,0) AS PiezasCalculadasContador,
    ISNULL(rh.CantidadOK,0) AS CantidadOK,
    ISNULL(rh.CantidadSospechosa,0) AS CantidadSospechosa,
    ISNULL(rh.CantidadScrap,0) AS CantidadScrap,
    ISNULL(rh.MinutosProductivos,0) AS MinutosProductivos,
    rh.ObjetivoBloque,
    rh.Observaciones,
    CASE
        WHEN te.FechaHoraFin IS NOT NULL
         AND rh.NumeroCorteTiempoExtra=
            (
                SELECT MAX(r2.NumeroCorteTiempoExtra)
                FROM dbo.Produccion_RegistroHora r2
                WHERE r2.TiempoExtraID=rh.TiempoExtraID
                  AND r2.Activo=1
            )
        THEN CAST(1 AS BIT)
        ELSE CAST(0 AS BIT)
    END AS EsCorteFinal
FROM dbo.Produccion_RegistroHora rh
INNER JOIN dbo.Produccion_TiempoExtra te ON te.TiempoExtraID=rh.TiempoExtraID
LEFT JOIN dbo.Persona p ON p.PersonaID=rh.OperadorID
OUTER APPLY
(
    SELECT TOP(1) s.ContadorInicial
    FROM dbo.Produccion_RegistroHoraSegmentos s
    WHERE s.RegistroHoraID=rh.RegistroHoraID
      AND s.Activo=1
    ORDER BY s.NumeroSegmento
) si
OUTER APPLY
(
    SELECT TOP(1) s.ContadorFinal
    FROM dbo.Produccion_RegistroHoraSegmentos s
    WHERE s.RegistroHoraID=rh.RegistroHoraID
      AND s.Activo=1
    ORDER BY s.NumeroSegmento DESC
) sf
WHERE rh.TiempoExtraID=@TiempoExtraID
  AND rh.Activo=1
  AND ISNULL(rh.EsTiempoExtra,0)=1
ORDER BY rh.NumeroCorteTiempoExtra,rh.RegistroHoraID;";

            await using var cmd = tx == null ? new SqlCommand(sql, cn) : new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@TiempoExtraID", SqlDbType.Int).Value = tiempoExtraId;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var fecha = Convert.ToDateTime(rd["FechaProduccion"]).Date;
                var horaInicio = (TimeSpan)rd["HoraInicio"];
                var horaFin = (TimeSpan)rd["HoraFin"];
                var fechaInicio = fecha.Add(horaInicio);
                var fechaFin = fecha.Add(horaFin);
                if (fechaFin <= fechaInicio) fechaFin = fechaFin.AddDays(1);

                lista.Add(new ProduccionTiempoExtraCorteVm
                {
                    RegistroHoraID = Convert.ToInt32(rd["RegistroHoraID"]),
                    TiempoExtraID = Convert.ToInt32(rd["TiempoExtraID"]),
                    NumeroCorte = Convert.ToInt32(rd["NumeroCorteTiempoExtra"]),
                    OperadorID = Convert.ToInt32(rd["OperadorID"]),
                    OperadorNombre = rd["OperadorNombre"] == DBNull.Value ? null : rd["OperadorNombre"]?.ToString()?.Trim(),
                    FechaHoraInicio = fechaInicio,
                    FechaHoraFin = fechaFin,
                    ContadorInicial = rd["ContadorInicial"] == DBNull.Value ? null : Convert.ToInt64(rd["ContadorInicial"]),
                    ContadorFinal = rd["ContadorFinal"] == DBNull.Value ? null : Convert.ToInt64(rd["ContadorFinal"]),
                    PiezasCalculadasContador = Convert.ToInt32(rd["PiezasCalculadasContador"]),
                    CantidadOK = Convert.ToInt32(rd["CantidadOK"]),
                    CantidadSospechosa = Convert.ToInt32(rd["CantidadSospechosa"]),
                    CantidadScrap = Convert.ToInt32(rd["CantidadScrap"]),
                    MinutosProductivos = Convert.ToDecimal(rd["MinutosProductivos"]),
                    ObjetivoBloque = rd["ObjetivoBloque"] == DBNull.Value ? null : Convert.ToInt32(rd["ObjetivoBloque"]),
                    EsCorteFinal = Convert.ToBoolean(rd["EsCorteFinal"]),
                    Observaciones = rd["Observaciones"] == DBNull.Value ? null : rd["Observaciones"]?.ToString()?.Trim()
                });
            }

            return lista;
        }

        private async Task<bool> PuedeIniciarTiempoExtraAsync(int ejecucionProduccionId, SqlConnection cn, SqlTransaction? tx = null)
        {
            if (ejecucionProduccionId <= 0) return false;

            const string sql = @"
SELECT TOP(1)
    ISNULL(e.CantidadPlaneada,0) AS CantidadPlaneada,
    TRY_CONVERT(DECIMAL(18,4),dt.ObjetivoHora) AS ObjetivoHora,
    ISNULL
    (
        (
            SELECT SUM
            (
                CASE
                    WHEN rh.MinutosProductivos IS NOT NULL
                     AND rh.MinutosProductivos>0
                        THEN rh.MinutosProductivos
                    WHEN rh.HoraFin>=rh.HoraInicio
                        THEN DATEDIFF(MINUTE,rh.HoraInicio,rh.HoraFin)
                    ELSE 1440+DATEDIFF(MINUTE,rh.HoraInicio,rh.HoraFin)
                END
            )
            FROM dbo.Produccion_RegistroHora rh
            WHERE rh.EjecucionProduccionID=e.EjecucionProduccionID
              AND rh.Activo=1
              AND ISNULL(rh.EsTiempoExtra,0)=0
        ),
        0
    ) AS MinutosNormalesCapturados,
    ISNULL
    (
        (
            SELECT COUNT(1)
            FROM dbo.Produccion_Paros p
            WHERE p.EjecucionProduccionID=e.EjecucionProduccionID
              AND p.Activo=1
              AND p.FechaFinParo IS NULL
        ),
        0
    ) AS ParosAbiertos,
    CASE
        WHEN e.FechaLiberacionMaquina IS NULL THEN CAST(0 AS BIT)
        ELSE CAST(1 AS BIT)
    END AS MaquinaLiberada,
    e.EstatusID
FROM dbo.Produccion_Ejecucion e
OUTER APPLY
(
    SELECT TOP(1) dt0.ObjetivoHora
    FROM dbo.ERP_ParteDatosTecnicos dt0
    WHERE dt0.ParteID=e.ParteID
      AND dt0.Activo=1
    ORDER BY dt0.ParteDatoTecnicoID DESC
) dt
WHERE e.EjecucionProduccionID=@EjecucionProduccionID
  AND e.Activo=1;";

            await using var cmd = tx == null ? new SqlCommand(sql, cn) : new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;

            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) return false;

            var estatusId = Convert.ToInt32(rd["EstatusID"]);
            var maquinaLiberada = Convert.ToBoolean(rd["MaquinaLiberada"]);
            var parosAbiertos = Convert.ToInt32(rd["ParosAbiertos"]);
            var cantidadPlaneada = Convert.ToInt32(rd["CantidadPlaneada"]);
            var objetivoHora = rd["ObjetivoHora"] == DBNull.Value ? 0m : Convert.ToDecimal(rd["ObjetivoHora"]);
            var minutosCapturados = rd["MinutosNormalesCapturados"] == DBNull.Value ? 0m : Convert.ToDecimal(rd["MinutosNormalesCapturados"]);

            if (estatusId != ProduccionEstatus.EnProduccion || maquinaLiberada || parosAbiertos > 0) return false;
            if (cantidadPlaneada <= 0 || objetivoHora <= 0) return false;

            var horasNormalesRequeridas = (int)Math.Ceiling(cantidadPlaneada / objetivoHora);
            var minutosNormalesRequeridos = horasNormalesRequeridas * 60m;

            return minutosCapturados >= minutosNormalesRequeridos;
        }

        private static ProduccionTiempoExtraVm MapearTiempoExtra(SqlDataReader rd)
        {
            return new ProduccionTiempoExtraVm
            {
                TiempoExtraID = Convert.ToInt32(rd["TiempoExtraID"]),
                EjecucionProduccionID = Convert.ToInt32(rd["EjecucionProduccionID"]),
                OperadorInicioID = Convert.ToInt32(rd["OperadorInicioID"]),
                OperadorInicioNombre = rd["OperadorInicioNombre"] == DBNull.Value ? null : rd["OperadorInicioNombre"]?.ToString()?.Trim(),
                OperadorFinID = rd["OperadorFinID"] == DBNull.Value ? null : Convert.ToInt32(rd["OperadorFinID"]),
                OperadorFinNombre = rd["OperadorFinNombre"] == DBNull.Value ? null : rd["OperadorFinNombre"]?.ToString()?.Trim(),
                ConfiguracionCorridaInicioID = rd["ConfiguracionCorridaInicioID"] == DBNull.Value ? null : Convert.ToInt32(rd["ConfiguracionCorridaInicioID"]),
                FechaHoraInicio = Convert.ToDateTime(rd["FechaHoraInicio"]),
                FechaHoraUltimoCorte = Convert.ToDateTime(rd["FechaHoraUltimoCorte"]),
                FechaHoraFin = rd["FechaHoraFin"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaHoraFin"]),
                ContadorInicio = Convert.ToInt64(rd["ContadorInicio"]),
                ContadorUltimoCorte = Convert.ToInt64(rd["ContadorUltimoCorte"]),
                ContadorFin = rd["ContadorFin"] == DBNull.Value ? null : Convert.ToInt64(rd["ContadorFin"]),
                Estado = rd["Estado"]?.ToString()?.Trim() ?? ProduccionTiempoExtraEstado.EnCurso,
                Motivo = rd["Motivo"]?.ToString()?.Trim() ?? string.Empty,
                Observaciones = rd["Observaciones"] == DBNull.Value ? null : rd["Observaciones"]?.ToString()?.Trim(),
                UsuarioCreacionID = Convert.ToInt32(rd["UsuarioCreacionID"]),
                FechaCreacion = Convert.ToDateTime(rd["FechaCreacion"]),
                UsuarioModificacionID = rd["UsuarioModificacionID"] == DBNull.Value ? null : Convert.ToInt32(rd["UsuarioModificacionID"]),
                FechaModificacion = rd["FechaModificacion"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaModificacion"]),
                UsuarioCancelacionID = rd["UsuarioCancelacionID"] == DBNull.Value ? null : Convert.ToInt32(rd["UsuarioCancelacionID"]),
                FechaCancelacion = rd["FechaCancelacion"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaCancelacion"]),
                MotivoCancelacion = rd["MotivoCancelacion"] == DBNull.Value ? null : rd["MotivoCancelacion"]?.ToString()?.Trim(),
                Activo = rd["Activo"] != DBNull.Value && Convert.ToBoolean(rd["Activo"])
            };
        }

        private static string? NormalizarMotivoTiempoExtra(string? motivo)
        {
            if (string.IsNullOrWhiteSpace(motivo)) return null;

            var valor = motivo.Trim().ToUpperInvariant().Replace(' ', '_');

            return valor switch
            {
                ProduccionTiempoExtraMotivo.RecuperarAtraso => ProduccionTiempoExtraMotivo.RecuperarAtraso,
                ProduccionTiempoExtraMotivo.CompletarOF => ProduccionTiempoExtraMotivo.CompletarOF,
                ProduccionTiempoExtraMotivo.ProduccionAdicionalAutorizada => ProduccionTiempoExtraMotivo.ProduccionAdicionalAutorizada,
                ProduccionTiempoExtraMotivo.AjusteProceso => ProduccionTiempoExtraMotivo.AjusteProceso,
                ProduccionTiempoExtraMotivo.Otro => ProduccionTiempoExtraMotivo.Otro,
                _ => null
            };
        }

        private sealed class ContextoEscaneoCaja
        {
            public int EjecucionProduccionID { get; set; }
            public int ProgramaProduccionID { get; set; }
            public int? SolicitudProduccionID { get; set; }
            public int? SolicitudProduccionDetalleID { get; set; }
            public int? ReleaseID { get; set; }
            public int? ReleaseDetalleID { get; set; }
            public int? ParteID { get; set; }
            public string? NumeroOF { get; set; }
            public string? NumeroParte { get; set; }
            public string? ReferenciaSAP { get; set; }
            public int CantidadPlaneada { get; set; }
            public decimal? PiezasPorEmbalaje { get; set; }
            public decimal? CantidadEmbalajes { get; set; }
            public int EstatusID { get; set; }
        }
        private static DateTime? NullableFecha(SqlDataReader rd, string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? null
                : Convert.ToDateTime(rd.GetValue(ordinal));
        }

        private sealed class ConfiguracionPeriodoContador
        {
            public int ConfiguracionCorridaID { get; set; }

            public int CavidadesUsadas { get; set; }

            public decimal TiempoCicloSegundos { get; set; }

            public decimal ObjetivoHoraCalculado { get; set; }

            public long? ContadorInicioVigencia { get; set; }

            public long? ContadorFinVigencia { get; set; }

            public DateTime FechaInicioVigencia { get; set; }

            public DateTime? FechaFinVigencia { get; set; }
        }

        private sealed class CalculoProduccionContadorHora
        {
            public long ContadorInicialReferencia { get; set; }

            public long ContadorFinal { get; set; }

            public int PiezasCalculadas { get; set; }

            public int ObjetivoHora { get; set; }

            public int ObjetivoBloque { get; set; }

            public decimal MinutosProductivos { get; set; }

            public bool TieneCambioConfiguracion { get; set; }

            public bool TieneReinicioContador { get; set; }

            public List<ProduccionRegistroHoraSegmentoVm>
                Segmentos
            { get; set; } = new();
        }

        private async Task<CalculoProduccionContadorHora>
    CalcularProduccionContadorHoraAsync(
        int ejecucionProduccionId,
        DateTime fechaInicio,
        DateTime fechaFin,
        long contadorActual,
        SqlConnection cn,
        SqlTransaction tx)
        {
            if (ejecucionProduccionId <= 0)
            {
                throw new InvalidOperationException(
                    "La ejecución no es válida.");
            }

            if (fechaFin <= fechaInicio)
            {
                throw new InvalidOperationException(
                    "El periodo de producción no es válido.");
            }

            if (contadorActual < 0)
            {
                throw new InvalidOperationException(
                    "El contador actual no puede ser negativo.");
            }

            var configuraciones =
                new List<ConfiguracionPeriodoContador>();

            const string sqlConfiguraciones = @"
SELECT
    ConfiguracionCorridaID,
    CavidadesUsadas,
    TiempoCicloSegundos,
    ObjetivoHoraCalculado,
    ContadorInicioVigencia,
    ContadorFinVigencia,
    FechaInicioVigencia,
    FechaFinVigencia
FROM dbo.Produccion_ConfiguracionCorrida WITH(UPDLOCK,HOLDLOCK)
WHERE EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1
  AND FechaInicioVigencia < @FechaFin
  AND
  (
      FechaFinVigencia IS NULL
      OR FechaFinVigencia > @FechaInicio
  )
ORDER BY
    FechaInicioVigencia,
    ConfiguracionCorridaID;";

            await using (var cmd =
                new SqlCommand(
                    sqlConfiguraciones,
                    cn,
                    tx))
            {
                cmd.Parameters.Add(
                    "@EjecucionProduccionID",
                    SqlDbType.Int).Value =
                    ejecucionProduccionId;

                cmd.Parameters.Add(
                    "@FechaInicio",
                    SqlDbType.DateTime2).Value =
                    fechaInicio;

                cmd.Parameters.Add(
                    "@FechaFin",
                    SqlDbType.DateTime2).Value =
                    fechaFin;

                await using var rd =
                    await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    configuraciones.Add(
                        new ConfiguracionPeriodoContador
                        {
                            ConfiguracionCorridaID =
                                Convert.ToInt32(
                                    rd["ConfiguracionCorridaID"]),

                            CavidadesUsadas =
                                Convert.ToInt32(
                                    rd["CavidadesUsadas"]),

                            TiempoCicloSegundos =
                                Convert.ToDecimal(
                                    rd["TiempoCicloSegundos"]),

                            ObjetivoHoraCalculado =
                                Convert.ToDecimal(
                                    rd["ObjetivoHoraCalculado"]),

                            ContadorInicioVigencia =
                                rd["ContadorInicioVigencia"] ==
                                DBNull.Value
                                    ? null
                                    : Convert.ToInt64(
                                        rd["ContadorInicioVigencia"]),

                            ContadorFinVigencia =
                                rd["ContadorFinVigencia"] ==
                                DBNull.Value
                                    ? null
                                    : Convert.ToInt64(
                                        rd["ContadorFinVigencia"]),

                            FechaInicioVigencia =
                                Convert.ToDateTime(
                                    rd["FechaInicioVigencia"]),

                            FechaFinVigencia =
                                rd["FechaFinVigencia"] ==
                                DBNull.Value
                                    ? null
                                    : Convert.ToDateTime(
                                        rd["FechaFinVigencia"])
                        });
                }
            }

            if (configuraciones.Count == 0)
            {
                throw new InvalidOperationException(
                    "No existe una configuración técnica para el periodo capturado.");
            }

            long? ultimaLecturaAnterior = null;

            const string sqlLecturaAnterior = @"
SELECT TOP(1)
    ValorContador
FROM dbo.Produccion_ContadorMaquinaLecturas WITH(UPDLOCK,HOLDLOCK)
WHERE EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1
  AND FechaLectura<=@FechaInicio
ORDER BY
    FechaLectura DESC,
    LecturaContadorID DESC;";

            await using (var cmd =
                new SqlCommand(
                    sqlLecturaAnterior,
                    cn,
                    tx))
            {
                cmd.Parameters.Add(
                    "@EjecucionProduccionID",
                    SqlDbType.Int).Value =
                    ejecucionProduccionId;

                cmd.Parameters.Add(
                    "@FechaInicio",
                    SqlDbType.DateTime2).Value =
                    fechaInicio;

                var valor =
                    await cmd.ExecuteScalarAsync();

                if (valor != null &&
                    valor != DBNull.Value)
                {
                    ultimaLecturaAnterior =
                        Convert.ToInt64(
                            valor);
                }
            }

            var configuracionInicio =
                configuraciones
                    .FirstOrDefault(
                        x =>
                            x.FechaInicioVigencia <=
                                fechaInicio.AddMinutes(1) &&
                            (
                                !x.FechaFinVigencia.HasValue ||
                                x.FechaFinVigencia.Value >
                                fechaInicio
                            ));

            if (configuracionInicio == null)
            {
                throw new InvalidOperationException(
                    "No fue posible identificar la configuración técnica al inicio del bloque.");
            }

            var contadorInicialReferencia =
                ultimaLecturaAnterior ??
                configuracionInicio.ContadorInicioVigencia;

            if (!contadorInicialReferencia.HasValue)
            {
                throw new InvalidOperationException(
                    "No existe una lectura base del contador para calcular esta hora.");
            }

            // =========================================================
            // PAROS QUE TRASLAPAN EL PERIODO
            // =========================================================
            var paros =
                new List<(DateTime Inicio, DateTime Fin)>();

            const string sqlParos = @"
SELECT
    FechaInicioParo,
    COALESCE(FechaFinParo,@FechaFin) AS FechaFinParo
FROM dbo.Produccion_Paros
WHERE EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1
  AND FechaInicioParo < @FechaFin
  AND COALESCE(FechaFinParo,@FechaFin) > @FechaInicio
ORDER BY FechaInicioParo;";

            await using (var cmd =
                new SqlCommand(
                    sqlParos,
                    cn,
                    tx))
            {
                cmd.Parameters.Add(
                    "@EjecucionProduccionID",
                    SqlDbType.Int).Value =
                    ejecucionProduccionId;

                cmd.Parameters.Add(
                    "@FechaInicio",
                    SqlDbType.DateTime2).Value =
                    fechaInicio;

                cmd.Parameters.Add(
                    "@FechaFin",
                    SqlDbType.DateTime2).Value =
                    fechaFin;

                await using var rd =
                    await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    paros.Add(
                        (
                            Convert.ToDateTime(
                                rd["FechaInicioParo"]),

                            Convert.ToDateTime(
                                rd["FechaFinParo"])
                        ));
                }
            }

            decimal CalcularMinutosProductivos(
                DateTime inicioSegmento,
                DateTime finSegmento)
            {
                var minutos =
                    (decimal)
                    (finSegmento -
                     inicioSegmento)
                    .TotalMinutes;

                foreach (var paro in paros)
                {
                    var inicioTraslape =
                        paro.Inicio >
                        inicioSegmento
                            ? paro.Inicio
                            : inicioSegmento;

                    var finTraslape =
                        paro.Fin <
                        finSegmento
                            ? paro.Fin
                            : finSegmento;

                    if (finTraslape <=
                        inicioTraslape)
                    {
                        continue;
                    }

                    minutos -=
                        (decimal)
                        (finTraslape -
                         inicioTraslape)
                        .TotalMinutes;
                }

                if (minutos < 0)
                    minutos = 0;

                return Math.Round(
                    minutos,
                    2);
            }

            var resultado =
                new CalculoProduccionContadorHora
                {
                    ContadorInicialReferencia =
                        contadorInicialReferencia.Value,

                    ContadorFinal =
                        contadorActual
                };

            var contadorCursor =
                contadorInicialReferencia.Value;

            var fechaCursor =
                fechaInicio;

            long piezasTotal = 0;

            decimal objetivoBloqueTotal = 0;

            var numeroSegmento = 1;

            for (var i = 0;
                 i < configuraciones.Count;
                 i++)
            {
                var configuracion =
                    configuraciones[i];

                var inicioSegmento =
                    configuracion.FechaInicioVigencia >
                    fechaCursor
                        ? configuracion.FechaInicioVigencia
                        : fechaCursor;

                // La configuración y el inicio de serie pueden quedar
                // separados por algunos segundos debido a normalización.
                if (inicioSegmento >
                    fechaCursor)
                {
                    var diferencia =
                        (inicioSegmento -
                         fechaCursor)
                        .TotalSeconds;

                    if (diferencia <= 60)
                    {
                        inicioSegmento =
                            fechaCursor;
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            "Existe un intervalo sin configuración técnica dentro de la hora capturada.");
                    }
                }

                var finSegmento =
                    configuracion
                        .FechaFinVigencia
                        .HasValue &&
                    configuracion
                        .FechaFinVigencia
                        .Value <
                    fechaFin
                        ? configuracion
                            .FechaFinVigencia
                            .Value
                        : fechaFin;

                if (finSegmento <=
                    inicioSegmento)
                {
                    continue;
                }

                var esPrimerSegmento =
                    numeroSegmento == 1;

                var esUltimoSegmento =
                    finSegmento >=
                    fechaFin.AddSeconds(-1);

                long contadorInicioSegmento;

                if (esPrimerSegmento)
                {
                    contadorInicioSegmento =
                        contadorCursor;
                }
                else if (configuracion
                         .ContadorInicioVigencia
                         .HasValue)
                {
                    contadorInicioSegmento =
                        configuracion
                            .ContadorInicioVigencia
                            .Value;
                }
                else
                {
                    contadorInicioSegmento =
                        contadorCursor;
                }

                long contadorFinSegmento;

                if (esUltimoSegmento)
                {
                    contadorFinSegmento =
                        contadorActual;
                }
                else if (configuracion
                         .ContadorFinVigencia
                         .HasValue)
                {
                    contadorFinSegmento =
                        configuracion
                            .ContadorFinVigencia
                            .Value;
                }
                else
                {
                    var siguiente =
                        i + 1 <
                        configuraciones.Count
                            ? configuraciones[i + 1]
                            : null;

                    if (siguiente == null ||
                        !siguiente
                            .ContadorInicioVigencia
                            .HasValue)
                    {
                        throw new InvalidOperationException(
                            "No existe una lectura de contador para cerrar uno de los cambios de configuración.");
                    }

                    contadorFinSegmento =
                        siguiente
                            .ContadorInicioVigencia
                            .Value;
                }

                var reinicioContador =
                    contadorFinSegmento <
                    contadorInicioSegmento;

                long contadorInicialCalculo =
                    contadorInicioSegmento;

                if (reinicioContador)
                {
                    /*
                     * Regla operativa:
                     *
                     * Si el contador actual es menor al anterior,
                     * interpretamos que la máquina fue reiniciada.
                     *
                     * Por tanto:
                     * ciclos = contador actual - 0
                     */
                    contadorInicialCalculo = 0;

                    resultado
                        .TieneReinicioContador =
                        true;
                }

                var ciclos =
                    contadorFinSegmento -
                    contadorInicialCalculo;

                if (ciclos < 0)
                {
                    throw new InvalidOperationException(
                        "No fue posible calcular los ciclos de máquina del periodo.");
                }

                var piezasSegmento =
                    checked(
                        ciclos *
                        (long)
                        configuracion
                            .CavidadesUsadas);

                piezasTotal =
                    checked(
                        piezasTotal +
                        piezasSegmento);

                var minutosProductivos =
                    CalcularMinutosProductivos(
                        inicioSegmento,
                        finSegmento);

                if (minutosProductivos <= 0 &&
                    piezasSegmento > 0)
                {
                    throw new InvalidOperationException(
                        "El contador indica producción en un periodo sin minutos productivos disponibles.");
                }

                var objetivoSegmento =
                    configuracion
                        .ObjetivoHoraCalculado *
                    minutosProductivos /
                    60m;

                objetivoBloqueTotal +=
                    objetivoSegmento;

                if (minutosProductivos > 0)
                {
                    resultado.Segmentos.Add(
                        new ProduccionRegistroHoraSegmentoVm
                        {
                            EjecucionProduccionID =
                                ejecucionProduccionId,

                            ConfiguracionCorridaID =
                                configuracion
                                    .ConfiguracionCorridaID,

                            NumeroSegmento =
                                numeroSegmento,

                            FechaHoraInicio =
                                inicioSegmento,

                            FechaHoraFin =
                                finSegmento,

                            MinutosProductivos =
                                minutosProductivos,

                            ContadorInicial =
                                contadorInicialCalculo,

                            ContadorFinal =
                                contadorFinSegmento,

                            CiclosPeriodo =
                                ciclos,

                            CavidadesUsadas =
                                configuracion
                                    .CavidadesUsadas,

                            TiempoCicloSegundos =
                                configuracion
                                    .TiempoCicloSegundos,

                            PiezasCalculadas =
                                piezasSegmento,

                            ObjetivoHoraCalculado =
                                configuracion
                                    .ObjetivoHoraCalculado,

                            ObjetivoSegmentoCalculado =
                                Math.Round(
                                    objetivoSegmento,
                                    4),

                            Observaciones =
                                reinicioContador
                                    ? "Reinicio del contador detectado automáticamente."
                                    : null,

                            Activo = true
                        });

                    numeroSegmento++;
                }

                contadorCursor =
                    contadorFinSegmento;

                fechaCursor =
                    finSegmento;

                if (esUltimoSegmento)
                    break;
            }

            if (fechaCursor <
                fechaFin.AddSeconds(-1))
            {
                throw new InvalidOperationException(
                    "No existe configuración técnica para cubrir todo el periodo capturado.");
            }

            if (piezasTotal >
                int.MaxValue)
            {
                throw new InvalidOperationException(
                    "La cantidad calculada de piezas excede el máximo permitido.");
            }

            if (resultado.Segmentos.Count == 0)
            {
                throw new InvalidOperationException(
                    "No fue posible generar los segmentos productivos del periodo.");
            }

            resultado.PiezasCalculadas =
                Convert.ToInt32(
                    piezasTotal);

            resultado.MinutosProductivos =
                resultado.Segmentos.Sum(
                    x => x.MinutosProductivos);

            resultado.ObjetivoBloque =
                objetivoBloqueTotal > 0
                    ? (int)Math.Round(
                        objetivoBloqueTotal,
                        0,
                        MidpointRounding.AwayFromZero)
                    : 0;

            var ultimaConfiguracion =
                configuraciones
                    .Last();

            resultado.ObjetivoHora =
                ultimaConfiguracion
                    .ObjetivoHoraCalculado >
                0
                    ? (int)Math.Round(
                        ultimaConfiguracion
                            .ObjetivoHoraCalculado,
                        0,
                        MidpointRounding.AwayFromZero)
                    : 0;

            resultado.TieneCambioConfiguracion =
                resultado.Segmentos
                    .Select(
                        x =>
                            x.ConfiguracionCorridaID)
                    .Distinct()
                    .Count() > 1;

            return resultado;
        }

        private static async Task InsertarSegmentosRegistroHoraAsync(
    int registroHoraId,
    int ejecucionProduccionId,
    CalculoProduccionContadorHora calculo,
    int usuarioId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            if (registroHoraId <= 0)
            {
                throw new InvalidOperationException(
                    "El registro horario no es válido.");
            }

            const string sql = @"
INSERT INTO dbo.Produccion_RegistroHoraSegmentos
(
    RegistroHoraID,
    EjecucionProduccionID,
    ConfiguracionCorridaID,
    NumeroSegmento,
    FechaHoraInicio,
    FechaHoraFin,
    MinutosProductivos,
    ContadorInicial,
    ContadorFinal,
    CavidadesUsadas,
    TiempoCicloSegundos,
    ObjetivoHoraCalculado,
    ObjetivoSegmentoCalculado,
    Observaciones,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
VALUES
(
    @RegistroHoraID,
    @EjecucionProduccionID,
    @ConfiguracionCorridaID,
    @NumeroSegmento,
    @FechaHoraInicio,
    @FechaHoraFin,
    @MinutosProductivos,
    @ContadorInicial,
    @ContadorFinal,
    @CavidadesUsadas,
    @TiempoCicloSegundos,
    @ObjetivoHoraCalculado,
    @ObjetivoSegmentoCalculado,
    @Observaciones,
    @UsuarioID,
    SYSDATETIME(),
    1
);";

            foreach (var segmento in
                     calculo.Segmentos)
            {
                await using var cmd =
                    new SqlCommand(
                        sql,
                        cn,
                        tx);

                cmd.Parameters.Add(
                    "@RegistroHoraID",
                    SqlDbType.Int).Value =
                    registroHoraId;

                cmd.Parameters.Add(
                    "@EjecucionProduccionID",
                    SqlDbType.Int).Value =
                    ejecucionProduccionId;

                cmd.Parameters.Add(
                    "@ConfiguracionCorridaID",
                    SqlDbType.Int).Value =
                    segmento.ConfiguracionCorridaID;

                cmd.Parameters.Add(
                    "@NumeroSegmento",
                    SqlDbType.Int).Value =
                    segmento.NumeroSegmento;

                cmd.Parameters.Add(
                    "@FechaHoraInicio",
                    SqlDbType.DateTime2).Value =
                    segmento.FechaHoraInicio;

                cmd.Parameters.Add(
                    "@FechaHoraFin",
                    SqlDbType.DateTime2).Value =
                    segmento.FechaHoraFin;

                var pMinutos =
                    cmd.Parameters.Add(
                        "@MinutosProductivos",
                        SqlDbType.Decimal);

                pMinutos.Precision = 10;
                pMinutos.Scale = 2;
                pMinutos.Value =
                    segmento.MinutosProductivos;

                cmd.Parameters.Add(
                    "@ContadorInicial",
                    SqlDbType.BigInt).Value =
                    segmento.ContadorInicial;

                cmd.Parameters.Add(
                    "@ContadorFinal",
                    SqlDbType.BigInt).Value =
                    segmento.ContadorFinal;

                cmd.Parameters.Add(
                    "@CavidadesUsadas",
                    SqlDbType.Int).Value =
                    segmento.CavidadesUsadas;

                var pCiclo =
                    cmd.Parameters.Add(
                        "@TiempoCicloSegundos",
                        SqlDbType.Decimal);

                pCiclo.Precision = 18;
                pCiclo.Scale = 4;
                pCiclo.Value =
                    segmento.TiempoCicloSegundos;

                var pObjetivoHora =
                    cmd.Parameters.Add(
                        "@ObjetivoHoraCalculado",
                        SqlDbType.Decimal);

                pObjetivoHora.Precision = 18;
                pObjetivoHora.Scale = 4;
                pObjetivoHora.Value =
                    segmento.ObjetivoHoraCalculado;

                var pObjetivoSegmento =
                    cmd.Parameters.Add(
                        "@ObjetivoSegmentoCalculado",
                        SqlDbType.Decimal);

                pObjetivoSegmento.Precision = 18;
                pObjetivoSegmento.Scale = 4;
                pObjetivoSegmento.Value =
                    (object?)
                    segmento
                        .ObjetivoSegmentoCalculado ??
                    DBNull.Value;

                cmd.Parameters.Add(
                    "@Observaciones",
                    SqlDbType.NVarChar,
                    500).Value =
                    string.IsNullOrWhiteSpace(
                        segmento.Observaciones)
                        ? DBNull.Value
                        : segmento
                            .Observaciones
                            .Trim();

                cmd.Parameters.Add(
                    "@UsuarioID",
                    SqlDbType.Int).Value =
                    usuarioId;

                await cmd.ExecuteNonQueryAsync();
            }
        }

        private static async Task RegistrarLecturaContadorHoraAsync(
    ProduccionEjecucionVm ejecucion,
    int registroHoraId,
    int operadorPersonaId,
    int usuarioId,
    DateTime fechaLectura,
    long valorContador,
    CalculoProduccionContadorHora calculo,
    SqlConnection cn,
    SqlTransaction tx)
        {
            if (calculo.Segmentos.Count == 0)
            {
                throw new InvalidOperationException(
                    "No existe un segmento técnico para relacionar la lectura del contador.");
            }

            var configuracionFinalId =
                calculo.Segmentos
                    .OrderByDescending(
                        x => x.NumeroSegmento)
                    .First()
                    .ConfiguracionCorridaID;

            var tipoLectura =
                calculo.TieneReinicioContador
                    ? ProduccionTipoLecturaContador.ReinicioContador
                    : ProduccionTipoLecturaContador.FinBloque;

            var motivoReinicio =
                calculo.TieneReinicioContador
                    ? "El contador capturado fue menor a la lectura anterior. El sistema interpretó un reinicio del contador."
                    : null;

            const string sql = @"
INSERT INTO dbo.Produccion_ContadorMaquinaLecturas
(
    EjecucionProduccionID,
    ConfiguracionCorridaID,
    MaquinaID,
    OperadorID,
    TipoLectura,
    ValorContador,
    FechaLectura,
    EsReinicioContador,
    MotivoReinicio,
    Observaciones,
    RegistroHoraID,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
VALUES
(
    @EjecucionProduccionID,
    @ConfiguracionCorridaID,
    @MaquinaID,
    @OperadorID,
    @TipoLectura,
    @ValorContador,
    @FechaLectura,
    @EsReinicioContador,
    @MotivoReinicio,
    @Observaciones,
    @RegistroHoraID,
    @UsuarioID,
    SYSDATETIME(),
    1
);";

            await using var cmd =
                new SqlCommand(
                    sql,
                    cn,
                    tx);

            cmd.Parameters.Add(
                "@EjecucionProduccionID",
                SqlDbType.Int).Value =
                ejecucion.EjecucionProduccionID;

            cmd.Parameters.Add(
                "@ConfiguracionCorridaID",
                SqlDbType.Int).Value =
                configuracionFinalId;

            cmd.Parameters.Add(
                "@MaquinaID",
                SqlDbType.Int).Value =
                (object?)ejecucion.MaquinaID ??
                DBNull.Value;

            cmd.Parameters.Add(
                "@OperadorID",
                SqlDbType.Int).Value =
                operadorPersonaId;

            cmd.Parameters.Add(
                "@TipoLectura",
                SqlDbType.NVarChar,
                50).Value =
                tipoLectura;

            cmd.Parameters.Add(
                "@ValorContador",
                SqlDbType.BigInt).Value =
                valorContador;

            cmd.Parameters.Add(
                "@FechaLectura",
                SqlDbType.DateTime2).Value =
                fechaLectura;

            cmd.Parameters.Add(
                "@EsReinicioContador",
                SqlDbType.Bit).Value =
                calculo.TieneReinicioContador;

            cmd.Parameters.Add(
                "@MotivoReinicio",
                SqlDbType.NVarChar,
                500).Value =
                (object?)motivoReinicio ??
                DBNull.Value;

            cmd.Parameters.Add(
                "@Observaciones",
                SqlDbType.NVarChar,
                500).Value =
                $"Lectura asociada al RegistroHoraID {registroHoraId}.";

            cmd.Parameters.Add(
                "@RegistroHoraID",
                SqlDbType.Int).Value =
                registroHoraId;

            cmd.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value =
                usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task RegistrarBonusProduccionHoraAsync(
    int operadorId,
    int ejecucionProduccionId,
    int registroHoraId,
    int cantidadOK,
    int piezasFisicas,
    int usuarioId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            if (cantidadOK <= 0)
                return;

            var referenciaEvento =
                $"REGISTRO_HORA:{registroHoraId}:PRODUCCION_OK";

            var motivo =
                $"Abono provisional por captura horaria. " +
                $"RegistroHoraID {registroHoraId}. " +
                $"Piezas físicas calculadas por contador: {piezasFisicas:N0}. " +
                $"Piezas reportadas OK por el operador: {cantidadOK:N0}.";

            if (motivo.Length > 1000)
                motivo = motivo[..1000];

            const string sql = @"
INSERT INTO dbo.Produccion_BonusOperadorMovimientos
(
    OperadorID,
    EjecucionProduccionID,
    RegistroHoraID,
    MonitoreoID,
    DisposicionID,
    TipoMovimiento,
    PiezasMovimiento,
    PiezasReferencia,
    Motivo,
    ReferenciaEvento,
    UsuarioCreacionID,
    FechaMovimiento,
    Activo
)
SELECT
    @OperadorID,
    @EjecucionProduccionID,
    @RegistroHoraID,
    NULL,
    NULL,
    @TipoMovimiento,
    @PiezasMovimiento,
    @PiezasReferencia,
    @Motivo,
    @ReferenciaEvento,
    @UsuarioID,
    SYSDATETIME(),
    1
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.Produccion_BonusOperadorMovimientos
    WHERE ReferenciaEvento=@ReferenciaEvento
      AND Activo=1
);";

            await using var cmd =
                new SqlCommand(
                    sql,
                    cn,
                    tx);

            cmd.Parameters.Add(
                "@OperadorID",
                SqlDbType.Int).Value =
                operadorId;

            cmd.Parameters.Add(
                "@EjecucionProduccionID",
                SqlDbType.Int).Value =
                ejecucionProduccionId;

            cmd.Parameters.Add(
                "@RegistroHoraID",
                SqlDbType.Int).Value =
                registroHoraId;

            cmd.Parameters.Add(
                "@TipoMovimiento",
                SqlDbType.NVarChar,
                60).Value =
                ProduccionTipoMovimientoBonus
                    .ProduccionHoraProvisional;

            cmd.Parameters.Add(
                "@PiezasMovimiento",
                SqlDbType.Int).Value =
                cantidadOK;

            cmd.Parameters.Add(
                "@PiezasReferencia",
                SqlDbType.Int).Value =
                piezasFisicas;

            cmd.Parameters.Add(
                "@Motivo",
                SqlDbType.NVarChar,
                1000).Value =
                motivo;

            cmd.Parameters.Add(
                "@ReferenciaEvento",
                SqlDbType.NVarChar,
                200).Value =
                referenciaEvento;

            cmd.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value =
                usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task<bool> PersonaAsignadaAEjecucionAsync(
    int ejecucionProduccionId,
    int personaId,
    SqlConnection cn,
    SqlTransaction? tx = null)
        {
            const string sql = @"
SELECT
    CASE
        WHEN EXISTS
        (
            SELECT 1
            FROM dbo.Produccion_Ejecucion e
            INNER JOIN dbo.Planeacion_ProgramaOperadores po
                ON po.ProgramaProduccionID=e.ProgramaProduccionID
               AND po.Activo=1
            WHERE e.EjecucionProduccionID=@EjecucionProduccionID
              AND e.Activo=1
              AND po.PersonaID=@PersonaID
              AND UPPER(LTRIM(RTRIM(ISNULL(po.RolOperador,N''))))
                  IN(N'PRINCIPAL',N'AUXILIAR')
        )
        THEN CAST(1 AS BIT)
        ELSE CAST(0 AS BIT)
    END;";

            await using var cmd =
                tx == null
                    ? new SqlCommand(
                        sql,
                        cn)
                    : new SqlCommand(
                        sql,
                        cn,
                        tx);

            cmd.Parameters.Add(
                "@EjecucionProduccionID",
                SqlDbType.Int).Value =
                ejecucionProduccionId;

            cmd.Parameters.Add(
                "@PersonaID",
                SqlDbType.Int).Value =
                personaId;

            var resultado =
                await cmd.ExecuteScalarAsync();

            return resultado != null &&
                   resultado != DBNull.Value &&
                   Convert.ToBoolean(
                       resultado);
        }

        private static async Task<ProduccionConfiguracionCorridaVm?>
            ObtenerConfiguracionActualOperadorAsync(
                int ejecucionProduccionId,
                SqlConnection cn,
                SqlTransaction? tx = null)
        {
            const string sql = @"
SELECT TOP(1)
    ConfiguracionCorridaID,
    EjecucionProduccionID,
    CavidadesUsadas,
    TiempoCicloSegundos,
    ObjetivoHoraCalculado,
    ContadorInicioVigencia,
    ContadorFinVigencia,
    FechaInicioVigencia,
    FechaFinVigencia,
    EsConfiguracionInicial,
    MotivoCambio,
    TecnicoProduccionID,
    UsuarioCreacionID,
    FechaCreacion,
    UsuarioModificacionID,
    FechaModificacion,
    Activo
FROM dbo.Produccion_ConfiguracionCorrida
WHERE EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1
  AND FechaFinVigencia IS NULL
ORDER BY ConfiguracionCorridaID DESC;";

            await using var cmd =
                tx == null
                    ? new SqlCommand(
                        sql,
                        cn)
                    : new SqlCommand(
                        sql,
                        cn,
                        tx);

            cmd.Parameters.Add(
                "@EjecucionProduccionID",
                SqlDbType.Int).Value =
                ejecucionProduccionId;

            await using var rd =
                await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return null;

            return new ProduccionConfiguracionCorridaVm
            {
                ConfiguracionCorridaID =
                    Convert.ToInt32(
                        rd["ConfiguracionCorridaID"]),

                EjecucionProduccionID =
                    Convert.ToInt32(
                        rd["EjecucionProduccionID"]),

                CavidadesUsadas =
                    Convert.ToInt32(
                        rd["CavidadesUsadas"]),

                TiempoCicloSegundos =
                    Convert.ToDecimal(
                        rd["TiempoCicloSegundos"]),

                ObjetivoHoraCalculado =
                    Convert.ToDecimal(
                        rd["ObjetivoHoraCalculado"]),

                ContadorInicioVigencia =
                    rd["ContadorInicioVigencia"] ==
                    DBNull.Value
                        ? null
                        : Convert.ToInt64(
                            rd["ContadorInicioVigencia"]),

                ContadorFinVigencia =
                    rd["ContadorFinVigencia"] ==
                    DBNull.Value
                        ? null
                        : Convert.ToInt64(
                            rd["ContadorFinVigencia"]),

                FechaInicioVigencia =
                    Convert.ToDateTime(
                        rd["FechaInicioVigencia"]),

                FechaFinVigencia =
                    rd["FechaFinVigencia"] ==
                    DBNull.Value
                        ? null
                        : Convert.ToDateTime(
                            rd["FechaFinVigencia"]),

                EsConfiguracionInicial =
                    Convert.ToBoolean(
                        rd["EsConfiguracionInicial"]),

                MotivoCambio =
                    rd["MotivoCambio"] ==
                    DBNull.Value
                        ? null
                        : rd["MotivoCambio"]?.ToString(),

                TecnicoProduccionID =
                    rd["TecnicoProduccionID"] ==
                    DBNull.Value
                        ? null
                        : Convert.ToInt32(
                            rd["TecnicoProduccionID"]),

                UsuarioCreacionID =
                    rd["UsuarioCreacionID"] ==
                    DBNull.Value
                        ? null
                        : Convert.ToInt32(
                            rd["UsuarioCreacionID"]),

                FechaCreacion =
                    Convert.ToDateTime(
                        rd["FechaCreacion"]),

                UsuarioModificacionID =
                    rd["UsuarioModificacionID"] ==
                    DBNull.Value
                        ? null
                        : Convert.ToInt32(
                            rd["UsuarioModificacionID"]),

                FechaModificacion =
                    rd["FechaModificacion"] ==
                    DBNull.Value
                        ? null
                        : Convert.ToDateTime(
                            rd["FechaModificacion"]),

                Activo =
                    Convert.ToBoolean(
                        rd["Activo"])
            };
        }

        private static async Task<long?>
            ObtenerUltimaLecturaContadorMaquinaAsync(
                int ejecucionProduccionId,
                SqlConnection cn,
                SqlTransaction? tx = null)
        {
            const string sql = @"
SELECT TOP(1)
    ValorContador
FROM dbo.Produccion_ContadorMaquinaLecturas
WHERE EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1
ORDER BY
    FechaLectura DESC,
    LecturaContadorID DESC;";

            await using var cmd =
                tx == null
                    ? new SqlCommand(
                        sql,
                        cn)
                    : new SqlCommand(
                        sql,
                        cn,
                        tx);

            cmd.Parameters.Add(
                "@EjecucionProduccionID",
                SqlDbType.Int).Value =
                ejecucionProduccionId;

            var resultado =
                await cmd.ExecuteScalarAsync();

            if (resultado == null ||
                resultado == DBNull.Value)
            {
                return null;
            }

            return Convert.ToInt64(
                resultado);
        }

        private static async Task<int>
            ObtenerBonusOperadorActualAsync(
                int operadorId,
                SqlConnection cn,
                SqlTransaction? tx = null)
        {
            const string sql = @"
SELECT
    ISNULL(
        SUM(
            CONVERT(
                BIGINT,
                PiezasMovimiento
            )
        ),
        0
    )
FROM dbo.Produccion_BonusOperadorMovimientos
WHERE OperadorID=@OperadorID
  AND Activo=1;";

            await using var cmd =
                tx == null
                    ? new SqlCommand(
                        sql,
                        cn)
                    : new SqlCommand(
                        sql,
                        cn,
                        tx);

            cmd.Parameters.Add(
                "@OperadorID",
                SqlDbType.Int).Value =
                operadorId;

            var resultado =
                await cmd.ExecuteScalarAsync();

            var total =
                resultado == null ||
                resultado == DBNull.Value
                    ? 0L
                    : Convert.ToInt64(
                        resultado);

            if (total > int.MaxValue)
                return int.MaxValue;

            if (total < int.MinValue)
                return int.MinValue;

            return Convert.ToInt32(
                total);
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