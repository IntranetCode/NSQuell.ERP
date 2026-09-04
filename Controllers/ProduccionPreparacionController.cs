using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers
{
    public sealed partial class ProduccionPreparacionController : Controller
    {
        private readonly IConfiguration _configuration;
        // NSQ_DEVOLUCION_MATERIALES_V1_2
        private readonly Microsoft.AspNetCore.Hosting.IWebHostEnvironment _environment;

        private const int PreparacionMinutosAvisoCambioMolde = 30;
        private const int PreparacionMinutosAnticipacionEmbalaje = 120;
        private const int PreparacionDiasHorizonte = 7;
        private const int PreparacionDiasHistorial = 30;
        private const int PreparacionMinutosAnticipacionMateriaPrima = 120;

        public ProduccionPreparacionController(
            IConfiguration configuration,
            Microsoft.AspNetCore.Hosting.IWebHostEnvironment environment)
        {
            _configuration = configuration;
            _environment = environment;
        }

        private string ConnectionString =>
            _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("No se encontró la cadena de conexión DefaultConnection.");

        

        [HttpGet]
        public async Task<IActionResult> Index(string? filtro = null, int? maquinaId = null)
        {
            return await ConstruirVistaAsync("Index", null, filtro, maquinaId, false);
        }

        [HttpGet]
        public async Task<IActionResult> CambioMolde(string? filtro = null, int? maquinaId = null)
        {
            return await ConstruirVistaAsync("CambioMolde", ProduccionPreparacionTipo.CambioMolde, filtro, maquinaId, false);
        }

        [HttpGet]
        public async Task<IActionResult> Embalajes(string? filtro = null, int? maquinaId = null)
        {
            return await ConstruirVistaAsync("Embalajes", ProduccionPreparacionTipo.PrepararEmbalaje, filtro, maquinaId, false);
        }

        [HttpGet]
        public async Task<IActionResult> Secado(string? filtro = null, int? maquinaId = null)
        {
            return await ConstruirSecadoOperativoAsync(filtro, maquinaId);
        }

        [HttpGet]
        public async Task<IActionResult> Historial(string? filtro = null, int? maquinaId = null)
        {
            return await ConstruirVistaAsync("Historial", null, filtro, maquinaId, true);
        }

        private async Task<IActionResult> ConstruirVistaAsync(string vista, string? tipoTarea, string? filtro, int? maquinaId, bool soloHistorial)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            filtro = string.IsNullOrWhiteSpace(filtro) ? null : filtro.Trim();
            if (maquinaId.HasValue && maquinaId.Value <= 0) maquinaId = null;
            var usuarioId = ObtenerUsuarioID();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            var permisos = await ObtenerPermisosPreparacionUsuarioAsync(usuarioId, cn);
            if (!permisos.PuedeVerModulo) return StatusCode(StatusCodes.Status403Forbidden);
            if (!soloHistorial)
            {
                await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
                try
                {
                    await SincronizarPreparacionAnticipadaAsync(usuarioId, cn, tx);
                    await tx.CommitAsync();
                }
                catch (Exception ex)
                {
                    try { await tx.RollbackAsync(); } catch { }
                    TempData["Error"] = "No fue posible sincronizar la preparación de Producción: " + ex.Message;
                }
            }
            var ahora = DateTime.Now;
            var tareas = await CargarPreparacionAnticipadaAsync(tipoTarea, filtro, maquinaId, ahora, soloHistorial, cn);
            if (!soloHistorial && string.IsNullOrWhiteSpace(tipoTarea))
            {
                var tareasMateriaPrima = await CargarTareasMateriaPrimaIndexAsync(filtro, maquinaId, ahora, cn);
                tareas.AddRange(tareasMateriaPrima);
            }
            await EnriquecerTareasParejaLhRhAsync(tareas, cn);
            if (!soloHistorial) tareas = ConsolidarCambiosMoldeLhRhVisuales(tareas);
            await EnriquecerChecklistCambioMoldeAsync(tareas, cn);
            var vm = new ProduccionPreparacionIndexVm
            {
                FechaConsulta = ahora,
                Filtro = filtro,
                MaquinaID = maquinaId,
                TipoTarea = tipoTarea,
                PuedeVerTodo = permisos.PuedeVerTodo,
                PuedeGestionarCambioMolde = permisos.PuedeGestionarCambioMolde,
                PuedeGestionarEmbalaje = permisos.PuedeGestionarEmbalaje,
                PuedeGestionarSecado = permisos.PuedeGestionarSecado,
                Maquinas = await CargarMaquinasPreparacionAsync(cn),
                Tareas = tareas
            };
            return View(vista, vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> IniciarCambioMolde(ProduccionPreparacionIniciarCambioVm vm)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            if (vm.PreparacionAnticipadaID <= 0)
            {
                TempData["Error"] = "No se recibió correctamente la tarea de cambio de molde.";
                return RedirectToAction(nameof(CambioMolde));
            }
            var usuarioId = ObtenerUsuarioID();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            var permisos = await ObtenerPermisosPreparacionUsuarioAsync(usuarioId, cn);
            if (!permisos.PuedeGestionarCambioMolde) return StatusCode(StatusCodes.Status403Forbidden);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var tareas = await CargarGrupoCambioMoldeAsync(vm.PreparacionAnticipadaID, cn, tx);
                if (tareas.Count == 0)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La tarea de cambio de molde ya no existe o ya no está disponible.";
                    return RedirectToAction(nameof(CambioMolde));
                }
                var origen = tareas.FirstOrDefault(x => x.PreparacionAnticipadaID == vm.PreparacionAnticipadaID) ?? tareas[0];
                var esPareja = origen.GrupoLhRh.HasValue;
                if (esPareja && tareas.Select(x => x.ProgramaProduccionID).Distinct().Count() != 2)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La programación está marcada como pareja LH/RH, pero no se encontraron exactamente las dos tareas de cambio de molde. No se iniciará un cambio parcial.";
                    return RedirectToAction(nameof(CambioMolde), new { maquinaId = origen.MaquinaID });
                }
                var errorGrupo = ValidarGrupoFisicoCambioMolde(tareas);
                if (!string.IsNullOrWhiteSpace(errorGrupo))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = errorGrupo;
                    return RedirectToAction(nameof(CambioMolde), new { maquinaId = origen.MaquinaID });
                }
                if (tareas.Any(x => !string.Equals(x.Estado, ProduccionPreparacionEstado.Pendiente, StringComparison.OrdinalIgnoreCase) && !string.Equals(x.Estado, ProduccionPreparacionEstado.EnProceso, StringComparison.OrdinalIgnoreCase)))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = esPareja ? "Las dos tareas LH/RH deben estar pendientes o en proceso. Se detectó un estado inconsistente y no se realizará un inicio parcial." : "La tarea de cambio de molde ya fue atendida o ya no está pendiente.";
                    return RedirectToAction(nameof(CambioMolde), new { maquinaId = origen.MaquinaID });
                }
                await ObtenerOCrearChecklistCambioMoldeAsync(vm.PreparacionAnticipadaID, usuarioId, cn, tx);
                if (tareas.All(x => string.Equals(x.Estado, ProduccionPreparacionEstado.EnProceso, StringComparison.OrdinalIgnoreCase)))
                {
                    await tx.CommitAsync();
                    TempData["Warning"] = esPareja ? "El cambio de molde LH/RH ya se encuentra en proceso para ambas OF." : "El cambio de molde ya se encuentra en proceso.";
                    return RedirectToAction(nameof(CambioMolde), new { maquinaId = origen.MaquinaID });
                }
                var maquinaId = origen.MaquinaID;
                if (!maquinaId.HasValue || maquinaId.Value <= 0)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "El programa no tiene una máquina válida asociada.";
                    return RedirectToAction(nameof(CambioMolde));
                }
                var limiteMinutos = ObtenerLimiteGrupoCambioMolde(tareas);
                var tarea1 = tareas[0].PreparacionAnticipadaID;
                var tarea2 = tareas.Count > 1 ? tareas[1].PreparacionAnticipadaID : -1;
                const string sqlOtroCambio = @"
SELECT COUNT(1)
FROM dbo.Produccion_PreparacionAnticipada pa WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Planeacion_ProgramaProduccion pp ON pp.ProgramaProduccionID=pa.ProgramaProduccionID
WHERE pa.Activo=1
AND pa.TipoTarea=@TipoTarea
AND pa.Estado=@EstadoEnProceso
AND pp.MaquinaID=@MaquinaID
AND pa.PreparacionAnticipadaID<>@Tarea1
AND pa.PreparacionAnticipadaID<>@Tarea2;";
                await using (var cmd = new SqlCommand(sqlOtroCambio, cn, tx))
                {
                    cmd.Parameters.Add("@TipoTarea", SqlDbType.NVarChar, 40).Value = ProduccionPreparacionTipo.CambioMolde;
                    cmd.Parameters.Add("@EstadoEnProceso", SqlDbType.NVarChar, 30).Value = ProduccionPreparacionEstado.EnProceso;
                    cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId.Value;
                    cmd.Parameters.Add("@Tarea1", SqlDbType.Int).Value = tarea1;
                    cmd.Parameters.Add("@Tarea2", SqlDbType.Int).Value = tarea2;
                    if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "La máquina ya tiene otro cambio de molde en proceso. Finalízalo antes de iniciar uno nuevo.";
                        return RedirectToAction(nameof(CambioMolde), new { maquinaId });
                    }
                }
                var fechaInicioExistente = tareas.Where(x => string.Equals(x.Estado, ProduccionPreparacionEstado.EnProceso, StringComparison.OrdinalIgnoreCase) && x.FechaInicioReal.HasValue).Select(x => x.FechaInicioReal).OrderBy(x => x).FirstOrDefault();
                var fechaInicio = fechaInicioExistente ?? DateTime.Now;
                var usuarioInicio = tareas.Where(x => string.Equals(x.Estado, ProduccionPreparacionEstado.EnProceso, StringComparison.OrdinalIgnoreCase) && x.UsuarioInicioID.HasValue).Select(x => x.UsuarioInicioID).FirstOrDefault() ?? usuarioId;
                const string sqlIniciar = @"
UPDATE dbo.Produccion_PreparacionAnticipada
SET Estado=@EstadoEnProceso,
UsuarioInicioID=@UsuarioInicioID,
FechaInicioReal=@FechaInicioReal,
FechaFinReal=NULL,
DuracionRealMinutos=NULL,
LimiteMinutosAplicado=@LimiteMinutos,
ExcedioLimite=0,
MotivoExceso=NULL,
UsuarioConfirmacionID=NULL,
FechaConfirmacion=NULL,
UsuarioModificacionID=@UsuarioModificacionID,
FechaModificacion=SYSDATETIME()
WHERE PreparacionAnticipadaID IN(@Tarea1,@Tarea2)
AND TipoTarea=@TipoTarea
AND Activo=1
AND Estado IN(@EstadoPendiente,@EstadoEnProceso);";
                await using (var cmd = new SqlCommand(sqlIniciar, cn, tx))
                {
                    cmd.Parameters.Add("@EstadoEnProceso", SqlDbType.NVarChar, 30).Value = ProduccionPreparacionEstado.EnProceso;
                    cmd.Parameters.Add("@UsuarioInicioID", SqlDbType.Int).Value = usuarioInicio;
                    cmd.Parameters.Add("@FechaInicioReal", SqlDbType.DateTime2).Value = fechaInicio;
                    cmd.Parameters.Add("@LimiteMinutos", SqlDbType.Int).Value = limiteMinutos;
                    cmd.Parameters.Add("@UsuarioModificacionID", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@Tarea1", SqlDbType.Int).Value = tarea1;
                    cmd.Parameters.Add("@Tarea2", SqlDbType.Int).Value = tarea2;
                    cmd.Parameters.Add("@TipoTarea", SqlDbType.NVarChar, 40).Value = ProduccionPreparacionTipo.CambioMolde;
                    cmd.Parameters.Add("@EstadoPendiente", SqlDbType.NVarChar, 30).Value = ProduccionPreparacionEstado.Pendiente;
                    var filas = await cmd.ExecuteNonQueryAsync();
                    if (filas != tareas.Count) throw new InvalidOperationException("Una de las tareas cambió de estado mientras intentabas iniciar el cambio de molde.");
                }
                await tx.CommitAsync();
                TempData["Success"] = esPareja ? $"Cambio de molde LH/RH iniciado como una sola operación física. Las dos OF quedaron sincronizadas. Límite operativo: {limiteMinutos} minutos." : $"Cambio de molde iniciado. Esta máquina tiene un límite operativo de {limiteMinutos} minutos.";
                return RedirectToAction(nameof(CambioMolde), new { maquinaId });
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                TempData["Error"] = "No fue posible iniciar el cambio de molde: " + ex.Message;
                return RedirectToAction(nameof(CambioMolde));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FinalizarCambioMolde(ProduccionPreparacionFinalizarCambioVm vm)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            if (vm.PreparacionAnticipadaID <= 0)
            {
                TempData["Error"] = "No se recibió correctamente la tarea de cambio de molde.";
                return RedirectToAction(nameof(CambioMolde));
            }
            vm.Observaciones = string.IsNullOrWhiteSpace(vm.Observaciones) ? null : vm.Observaciones.Trim();
            vm.MotivoExceso = string.IsNullOrWhiteSpace(vm.MotivoExceso) ? null : vm.MotivoExceso.Trim();
            if (vm.Observaciones?.Length > 500)
            {
                TempData["Error"] = "Las observaciones no pueden superar 500 caracteres.";
                return RedirectToAction(nameof(CambioMolde));
            }
            if (vm.MotivoExceso?.Length > 500)
            {
                TempData["Error"] = "El motivo de excedente no puede superar 500 caracteres.";
                return RedirectToAction(nameof(CambioMolde));
            }
            var usuarioId = ObtenerUsuarioID();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            var permisos = await ObtenerPermisosPreparacionUsuarioAsync(usuarioId, cn);
            if (!permisos.PuedeGestionarCambioMolde) return StatusCode(StatusCodes.Status403Forbidden);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var tareas = await CargarGrupoCambioMoldeAsync(vm.PreparacionAnticipadaID, cn, tx);
                if (tareas.Count == 0)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La tarea de cambio de molde ya no existe o ya no está disponible.";
                    return RedirectToAction(nameof(CambioMolde));
                }
                var origen = tareas.FirstOrDefault(x => x.PreparacionAnticipadaID == vm.PreparacionAnticipadaID) ?? tareas[0];
                var esPareja = origen.GrupoLhRh.HasValue;
                if (esPareja && tareas.Select(x => x.ProgramaProduccionID).Distinct().Count() != 2)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La programación está marcada como pareja LH/RH, pero no se encontraron exactamente las dos tareas. No se realizará un cierre parcial.";
                    return RedirectToAction(nameof(CambioMolde), new { maquinaId = origen.MaquinaID });
                }
                var errorGrupo = ValidarGrupoFisicoCambioMolde(tareas);
                if (!string.IsNullOrWhiteSpace(errorGrupo))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = errorGrupo;
                    return RedirectToAction(nameof(CambioMolde), new { maquinaId = origen.MaquinaID });
                }
                if (tareas.All(x => string.Equals(x.Estado, ProduccionPreparacionEstado.Confirmada, StringComparison.OrdinalIgnoreCase)))
                {
                    await tx.RollbackAsync();
                    TempData["Warning"] = esPareja ? "El cambio de molde LH/RH ya fue finalizado para ambas OF." : "El cambio de molde ya fue finalizado.";
                    return RedirectToAction(nameof(CambioMolde), new { maquinaId = origen.MaquinaID });
                }
                if (tareas.Any(x => !string.Equals(x.Estado, ProduccionPreparacionEstado.EnProceso, StringComparison.OrdinalIgnoreCase)))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = esPareja ? "Las dos tareas LH/RH deben estar EN PROCESO para finalizar el cambio de molde. No se realizará un cierre parcial." : "Solo puedes finalizar un cambio de molde que esté EN PROCESO.";
                    return RedirectToAction(nameof(CambioMolde), new { maquinaId = origen.MaquinaID });
                }
                var checklist = await ObtenerChecklistCambioMoldePorPreparacionAsync(vm.PreparacionAnticipadaID, cn, tx);
                if (checklist == null)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "No existe el checklist GQ-F-PR01-03 para este cambio de molde. Debes atender la actividad A1 antes de finalizar.";
                    return RedirectToAction(nameof(CambioMolde), new { maquinaId = origen.MaquinaID });
                }
                if (!checklist.EstaCompleto)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"El checklist GQ-F-PR01-03 todavía no está completo. Avance actual: {checklist.TextoAvance}. Debes completar y finalizar la actividad A1 antes de cerrar el cambio de molde.";
                    return RedirectToAction(nameof(CambioMolde), new { maquinaId = origen.MaquinaID });
                }
                var fechasInicio = tareas.Where(x => x.FechaInicioReal.HasValue).Select(x => x.FechaInicioReal!.Value).OrderBy(x => x).ToList();
                if (fechasInicio.Count != tareas.Count)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "No todas las tareas tienen registrada la fecha real de inicio del cambio.";
                    return RedirectToAction(nameof(CambioMolde), new { maquinaId = origen.MaquinaID });
                }
                var fechaInicioReal = fechasInicio.First();
                var limiteMinutos = ObtenerLimiteGrupoCambioMolde(tareas);
                var ahora = DateTime.Now;
                var duracionMinutos = Math.Max(0, (int)Math.Ceiling((ahora - fechaInicioReal).TotalMinutes));
                var excedioLimite = duracionMinutos > limiteMinutos;
                if (excedioLimite && string.IsNullOrWhiteSpace(vm.MotivoExceso))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"El cambio duró {duracionMinutos} minutos y superó el máximo de {limiteMinutos}. Debes registrar el motivo del excedente.";
                    return RedirectToAction(nameof(CambioMolde), new { maquinaId = origen.MaquinaID });
                }
                var tarea1 = tareas[0].PreparacionAnticipadaID;
                var tarea2 = tareas.Count > 1 ? tareas[1].PreparacionAnticipadaID : -1;
                const string sqlFinalizar = @"
UPDATE dbo.Produccion_PreparacionAnticipada
SET Estado=@EstadoConfirmada,
FechaInicioReal=@FechaInicioReal,
FechaFinReal=@FechaFinReal,
DuracionRealMinutos=@DuracionRealMinutos,
LimiteMinutosAplicado=@LimiteMinutos,
ExcedioLimite=@ExcedioLimite,
MotivoExceso=@MotivoExceso,
UsuarioConfirmacionID=@UsuarioID,
FechaConfirmacion=@FechaFinReal,
Observaciones=LEFT(CASE WHEN @Observaciones IS NULL THEN Observaciones WHEN Observaciones IS NULL OR LTRIM(RTRIM(Observaciones))=N'' THEN @Observaciones ELSE Observaciones+CHAR(13)+CHAR(10)+@Observaciones END,500),
UsuarioModificacionID=@UsuarioID,
FechaModificacion=SYSDATETIME()
WHERE PreparacionAnticipadaID IN(@Tarea1,@Tarea2)
AND TipoTarea=@TipoTarea
AND Activo=1
AND Estado=@EstadoEnProceso;";
                await using (var cmd = new SqlCommand(sqlFinalizar, cn, tx))
                {
                    cmd.Parameters.Add("@EstadoConfirmada", SqlDbType.NVarChar, 30).Value = ProduccionPreparacionEstado.Confirmada;
                    cmd.Parameters.Add("@FechaInicioReal", SqlDbType.DateTime2).Value = fechaInicioReal;
                    cmd.Parameters.Add("@FechaFinReal", SqlDbType.DateTime2).Value = ahora;
                    cmd.Parameters.Add("@DuracionRealMinutos", SqlDbType.Int).Value = duracionMinutos;
                    cmd.Parameters.Add("@LimiteMinutos", SqlDbType.Int).Value = limiteMinutos;
                    cmd.Parameters.Add("@ExcedioLimite", SqlDbType.Bit).Value = excedioLimite;
                    cmd.Parameters.Add("@MotivoExceso", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(vm.MotivoExceso) ? DBNull.Value : vm.MotivoExceso;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(vm.Observaciones) ? DBNull.Value : vm.Observaciones;
                    cmd.Parameters.Add("@Tarea1", SqlDbType.Int).Value = tarea1;
                    cmd.Parameters.Add("@Tarea2", SqlDbType.Int).Value = tarea2;
                    cmd.Parameters.Add("@TipoTarea", SqlDbType.NVarChar, 40).Value = ProduccionPreparacionTipo.CambioMolde;
                    cmd.Parameters.Add("@EstadoEnProceso", SqlDbType.NVarChar, 30).Value = ProduccionPreparacionEstado.EnProceso;
                    var filas = await cmd.ExecuteNonQueryAsync();
                    if (filas != tareas.Count) throw new InvalidOperationException("Una de las tareas cambió de estado mientras intentabas finalizar el cambio de molde.");
                }
                await tx.CommitAsync();
                if (esPareja)
                {
                    TempData[excedioLimite ? "Warning" : "Success"] = excedioLimite ? $"Cambio de molde LH/RH finalizado para ambas OF en {duracionMinutos} minutos. Excedió {duracionMinutos - limiteMinutos} minuto(s) el límite permitido." : $"Cambio de molde LH/RH finalizado correctamente para ambas OF en {duracionMinutos} minutos.";
                }
                else
                {
                    TempData[excedioLimite ? "Warning" : "Success"] = excedioLimite ? $"Cambio de molde finalizado en {duracionMinutos} minutos. Excedió {duracionMinutos - limiteMinutos} minuto(s) el límite permitido." : $"Cambio de molde finalizado correctamente en {duracionMinutos} minutos. Límite de la máquina: {limiteMinutos} minutos.";
                }
                return RedirectToAction(nameof(CambioMolde), new { maquinaId = origen.MaquinaID });
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                TempData["Error"] = "No fue posible finalizar el cambio de molde: " + ex.Message;
                return RedirectToAction(nameof(CambioMolde));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmarPreparacion(ProduccionPreparacionConfirmarVm vm)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            if (vm.PreparacionAnticipadaID <= 0)
            {
                TempData["Error"] = "No se recibió correctamente la tarea de preparación.";
                return RedirectToAction(nameof(Index));
            }

            var observaciones = string.IsNullOrWhiteSpace(vm.Observaciones) ? null : vm.Observaciones.Trim();

            if (observaciones?.Length > 500)
            {
                TempData["Error"] = "Las observaciones no pueden superar 500 caracteres.";
                return RedirectToAction(nameof(Index));
            }

            var usuarioId = ObtenerUsuarioID();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var permisos = await ObtenerPermisosPreparacionUsuarioAsync(usuarioId, cn);
            if (!permisos.PuedeVerModulo)
                return StatusCode(StatusCodes.Status403Forbidden);

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                const string sqlObtener = @"
SELECT TOP(1) TipoTarea,Estado
FROM dbo.Produccion_PreparacionAnticipada WITH(UPDLOCK,HOLDLOCK)
WHERE PreparacionAnticipadaID=@PreparacionAnticipadaID
  AND Activo=1;";

                string tipoTarea;
                string estado;

                await using (var cmd = new SqlCommand(sqlObtener, cn, tx))
                {
                    cmd.Parameters.Add("@PreparacionAnticipadaID", SqlDbType.Int).Value = vm.PreparacionAnticipadaID;
                    await using var rd = await cmd.ExecuteReaderAsync();

                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "La tarea ya no existe o ya no está disponible.";
                        return RedirectToAction(nameof(Index));
                    }

                    tipoTarea = rd["TipoTarea"]?.ToString()?.Trim() ?? string.Empty;
                    estado = rd["Estado"]?.ToString()?.Trim() ?? string.Empty;
                }

                if (string.Equals(tipoTarea, ProduccionPreparacionTipo.CambioMolde, StringComparison.OrdinalIgnoreCase))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "El cambio de molde debe atenderse con Iniciar cambio y Finalizar cambio.";
                    return RedirectToAction(nameof(CambioMolde));
                }

                if (string.Equals(tipoTarea, ProduccionPreparacionTipo.SecadoMaterial, StringComparison.OrdinalIgnoreCase))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "El secado debe atenderse desde la pantalla de Secado, seleccionando tolva e iniciando la carga.";
                    return RedirectToAction(nameof(Secado));
                }


                if (string.Equals(tipoTarea, ProduccionPreparacionTipo.PrepararEmbalaje, StringComparison.OrdinalIgnoreCase) &&
                    !permisos.PuedeGestionarEmbalaje)
                {
                    await tx.RollbackAsync();
                    return StatusCode(StatusCodes.Status403Forbidden);
                }

                if (string.Equals(tipoTarea, ProduccionPreparacionTipo.SecadoMaterial, StringComparison.OrdinalIgnoreCase) &&
                    !permisos.PuedeGestionarSecado)
                {
                    await tx.RollbackAsync();
                    return StatusCode(StatusCodes.Status403Forbidden);
                }

                if (!string.Equals(estado, ProduccionPreparacionEstado.Pendiente, StringComparison.OrdinalIgnoreCase))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La tarea ya fue atendida, cancelada o ya no se encuentra pendiente.";
                    return RedirigirSegunTipo(tipoTarea);
                }

                const string sql = @"
UPDATE dbo.Produccion_PreparacionAnticipada
SET
    Estado=@EstadoConfirmada,
    UsuarioConfirmacionID=@UsuarioID,
    FechaConfirmacion=SYSDATETIME(),
    Observaciones=
        LEFT(
            CASE
                WHEN @Observaciones IS NULL THEN Observaciones
                WHEN Observaciones IS NULL OR LTRIM(RTRIM(Observaciones))=N'' THEN @Observaciones
                ELSE Observaciones+CHAR(13)+CHAR(10)+@Observaciones
            END,
            500
        ),
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=SYSDATETIME()
WHERE PreparacionAnticipadaID=@PreparacionAnticipadaID
  AND Activo=1
  AND Estado=@EstadoPendiente;";

                await using (var cmd = new SqlCommand(sql, cn, tx))
                {
                    cmd.Parameters.Add("@EstadoConfirmada", SqlDbType.NVarChar, 30).Value = ProduccionPreparacionEstado.Confirmada;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value =
                        string.IsNullOrWhiteSpace(observaciones) ? DBNull.Value : observaciones;
                    cmd.Parameters.Add("@PreparacionAnticipadaID", SqlDbType.Int).Value = vm.PreparacionAnticipadaID;
                    cmd.Parameters.Add("@EstadoPendiente", SqlDbType.NVarChar, 30).Value = ProduccionPreparacionEstado.Pendiente;

                    var filas = await cmd.ExecuteNonQueryAsync();
                    if (filas <= 0)
                        throw new InvalidOperationException("La tarea cambió de estado mientras intentabas confirmarla.");
                }

                await tx.CommitAsync();

                TempData["Success"] =
                    string.Equals(tipoTarea, ProduccionPreparacionTipo.PrepararEmbalaje, StringComparison.OrdinalIgnoreCase)
                        ? "Embalaje confirmado como preparado."
                        : string.Equals(tipoTarea, ProduccionPreparacionTipo.SecadoMaterial, StringComparison.OrdinalIgnoreCase)
                            ? "Inicio de secado registrado correctamente."
                            : "Preparación confirmada correctamente.";

                return RedirigirSegunTipo(tipoTarea);
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                TempData["Error"] = "No fue posible confirmar la preparación: " + ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }
        private async Task<List<ProduccionPreparacionTareaVm>> CargarTareasMateriaPrimaIndexAsync(string? filtro, int? maquinaId, DateTime ahora, SqlConnection cn)
        {
            var lista = new List<ProduccionPreparacionTareaVm>();
            var desde = ahora.Date.AddDays(-1);
            var hasta = ahora.Date.AddDays(PreparacionDiasHorizonte + 1);

            const string sql = @"
SELECT
    pp.ProgramaProduccionID,
    pp.SolicitudProduccionID,
    pp.SolicitudProduccionDetalleID,
    ejecucion.EjecucionProduccionID,
    COALESCE(NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)),N''),NULLIF(LTRIM(RTRIM(s.FolioSolicitud)),N''),N'') AS NumeroOF,
    pp.MaquinaID,
    COALESCE(NULLIF(LTRIM(RTRIM(pp.MaquinaCodigo)),N''),maq.Codigo) AS MaquinaCodigo,
    COALESCE(NULLIF(LTRIM(RTRIM(pp.MaquinaNombre)),N''),maq.Nombre) AS MaquinaNombre,
    pp.ParteID,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.DesignacionDescripcionSAP AS DescripcionParte,
    CONVERT(INT,ISNULL(pp.CantidadProgramada,0)) AS CantidadProgramada,
    pp.FechaInicioProgramada,
    pp.FechaFinProgramada,
    pp.Arranque,
    d.TipoSecado,
    d.HorasSecado,
    d.MaterialCodigo,
    d.MaterialDescripcion,
    CONVERT(DECIMAL(18,4),ISNULL(d.CantidadMpKg,0)) AS CantidadMpKg,
    CONVERT(DECIMAL(18,4),ISNULL(recepcion.CantidadRecibidaProduccionKg,0)) AS CantidadMpRecibidaProduccionKg
FROM dbo.Planeacion_ProgramaProduccion pp
LEFT JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID=pp.SolicitudProduccionID
LEFT JOIN dbo.SolicitudesProduccionDetalle d
    ON d.SolicitudProduccionDetalleID=pp.SolicitudProduccionDetalleID
   AND d.Activo=1
LEFT JOIN dbo.ERP_Maquinas maq
    ON maq.MaquinaID=pp.MaquinaID
OUTER APPLY
(
    SELECT TOP(1) e.EjecucionProduccionID
    FROM dbo.Produccion_Ejecucion e
    WHERE e.ProgramaProduccionID=pp.ProgramaProduccionID
      AND e.Activo=1
    ORDER BY e.EjecucionProduccionID DESC
) ejecucion
OUTER APPLY
(
    SELECT
        CONVERT
        (
            DECIMAL(18,4),
            ISNULL(SUM(ISNULL(r.CantidadRecibidaProduccion,0)),0)
        ) AS CantidadRecibidaProduccionKg
    FROM dbo.Produccion_RecepcionMateriales r
    WHERE r.Activo=1
      AND r.TipoOrigen=N'MP'
      AND r.EstadoRecepcion IN(N'RECIBIDO_COMPLETO',N'RECIBIDO_PARCIAL')
      AND r.SolicitudProduccionID=pp.SolicitudProduccionID
      AND
      (
          r.ProgramaProduccionID=pp.ProgramaProduccionID
          OR
          (
              r.ProgramaProduccionID IS NULL
              AND r.SolicitudProduccionDetalleID=pp.SolicitudProduccionDetalleID
          )
          OR
          (
              r.ProgramaProduccionID IS NULL
              AND r.SolicitudProduccionDetalleID IS NULL
              AND
              (
                  r.MaterialSolicitadoID=d.MaterialID
                  OR
                  (
                      d.MaterialID IS NULL
                      AND UPPER(LTRIM(RTRIM(ISNULL(r.CodigoSolicitadoSnapshot,N''))))=
                          UPPER(LTRIM(RTRIM(ISNULL(d.MaterialCodigo,N''))))
                  )
              )
          )
      )
) recepcion
WHERE pp.Activo=1
  AND pp.MaquinaID IS NOT NULL
  AND pp.FechaInicioProgramada IS NOT NULL
  AND pp.FechaInicioProgramada>=@Desde
  AND pp.FechaInicioProgramada<@Hasta
  AND ISNULL(pp.EstatusID,1) NOT IN(5,6,9,99)
  AND ISNULL(d.CantidadMpKg,0)>0.0005
  AND ISNULL(recepcion.CantidadRecibidaProduccionKg,0)+0.0005<ISNULL(d.CantidadMpKg,0)
  AND (@MaquinaID IS NULL OR pp.MaquinaID=@MaquinaID)
  AND
  (
      @Filtro IS NULL
      OR s.NumeroOFRecibida LIKE N'%'+@Filtro+N'%'
      OR s.FolioSolicitud LIKE N'%'+@Filtro+N'%'
      OR pp.NumeroParte LIKE N'%'+@Filtro+N'%'
      OR pp.ReferenciaSAP LIKE N'%'+@Filtro+N'%'
      OR pp.DesignacionDescripcionSAP LIKE N'%'+@Filtro+N'%'
      OR pp.MaquinaCodigo LIKE N'%'+@Filtro+N'%'
      OR pp.MaquinaNombre LIKE N'%'+@Filtro+N'%'
      OR d.MaterialCodigo LIKE N'%'+@Filtro+N'%'
      OR d.MaterialDescripcion LIKE N'%'+@Filtro+N'%'
  )
ORDER BY pp.FechaInicioProgramada,pp.ProgramaProduccionID;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@Desde", SqlDbType.DateTime).Value = desde;
            cmd.Parameters.Add("@Hasta", SqlDbType.DateTime).Value = hasta;
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId.HasValue && maquinaId.Value > 0 ? maquinaId.Value : DBNull.Value;
            cmd.Parameters.Add("@Filtro", SqlDbType.NVarChar, 200).Value = string.IsNullOrWhiteSpace(filtro) ? DBNull.Value : filtro.Trim();

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var inicio = Convert.ToDateTime(rd["FechaInicioProgramada"]);
                var arranque = PreparacionNullableTimeSpan(rd, "Arranque");
                var fechaArranque = ConstruirFechaPreparacion(inicio, arranque);
                var horasSecado = PreparacionNullableDecimal(rd, "HorasSecado");
                var fechaObjetivo = horasSecado.HasValue && horasSecado.Value > 0
                    ? fechaArranque.AddHours(-Convert.ToDouble(horasSecado.Value))
                    : fechaArranque;
                var fechaAviso = fechaObjetivo.AddMinutes(-PreparacionMinutosAnticipacionMateriaPrima);

                lista.Add(new ProduccionPreparacionTareaVm
                {
                    PreparacionAnticipadaID = 0,
                    ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                    EjecucionProduccionID = PreparacionNullableInt(rd, "EjecucionProduccionID"),
                    TipoTarea = ProduccionPreparacionTipo.MateriaPrima,
                    Estado = ProduccionPreparacionEstado.Pendiente,
                    FechaObjetivo = fechaObjetivo,
                    FechaAviso = fechaAviso,
                    SolicitudProduccionID = PreparacionNullableInt(rd, "SolicitudProduccionID"),
                    SolicitudProduccionDetalleID = PreparacionNullableInt(rd, "SolicitudProduccionDetalleID"),
                    NumeroOF = PreparacionTexto(rd, "NumeroOF"),
                    MaquinaID = PreparacionNullableInt(rd, "MaquinaID"),
                    MaquinaCodigo = PreparacionTexto(rd, "MaquinaCodigo"),
                    MaquinaNombre = PreparacionTexto(rd, "MaquinaNombre"),
                    ParteID = PreparacionNullableInt(rd, "ParteID"),
                    NumeroParte = PreparacionTexto(rd, "NumeroParte"),
                    ReferenciaSAP = PreparacionTexto(rd, "ReferenciaSAP"),
                    DescripcionParte = PreparacionTexto(rd, "DescripcionParte"),
                    CantidadProgramada = rd["CantidadProgramada"] == DBNull.Value ? 0 : Convert.ToInt32(rd["CantidadProgramada"]),
                    FechaInicioProgramada = inicio,
                    FechaFinProgramada = PreparacionNullableDateTime(rd, "FechaFinProgramada"),
                    FechaArranque = fechaArranque,
                    TipoSecado = PreparacionTexto(rd, "TipoSecado"),
                    HorasSecado = horasSecado,
                    MaterialCodigo = PreparacionTexto(rd, "MaterialCodigo"),
                    MaterialDescripcion = PreparacionTexto(rd, "MaterialDescripcion"),
                    CantidadMpKg = PreparacionNullableDecimal(rd, "CantidadMpKg"),
                    CantidadMpRecibidaProduccionKg = rd["CantidadMpRecibidaProduccionKg"] == DBNull.Value ? 0m : Convert.ToDecimal(rd["CantidadMpRecibidaProduccionKg"]),
                    Ahora = ahora
                });
            }

            return lista;
        }
        private IActionResult RedirigirSegunTipo(string? tipoTarea)
        {
            if (string.Equals(tipoTarea, ProduccionPreparacionTipo.CambioMolde, StringComparison.OrdinalIgnoreCase))
                return RedirectToAction(nameof(CambioMolde));

            if (string.Equals(tipoTarea, ProduccionPreparacionTipo.PrepararEmbalaje, StringComparison.OrdinalIgnoreCase))
                return RedirectToAction(nameof(Embalajes));

            if (string.Equals(tipoTarea, ProduccionPreparacionTipo.SecadoMaterial, StringComparison.OrdinalIgnoreCase))
                return RedirectToAction(nameof(Secado));

            return RedirectToAction(nameof(Index));
        }


        [HttpGet]
        public async Task<IActionResult> EstadoCambioMolde(int id)
        {
            if (!UsuarioEnSesion())
                return Unauthorized();

            if (id <= 0)
                return BadRequest();

            var usuarioId = ObtenerUsuarioID();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var permisos = await ObtenerPermisosPreparacionUsuarioAsync(usuarioId, cn);
            if (!permisos.PuedeVerModulo)
                return StatusCode(StatusCodes.Status403Forbidden);

            const string sql = @"
SELECT TOP(1)
    pa.PreparacionAnticipadaID,
    pa.Estado,
    pa.FechaInicioReal,
    pa.FechaFinReal,
    pa.FechaConfirmacion,
    pa.DuracionRealMinutos,
    pa.LimiteMinutosAplicado,
    pa.ExcedioLimite,
    pp.MaquinaID,
    COALESCE(NULLIF(LTRIM(RTRIM(pp.MaquinaCodigo)),N''),maq.Codigo) AS MaquinaCodigo,
    ISNULL(maq.MinutosMaxCambioMolde,60) AS MinutosMaxCambioMolde
FROM dbo.Produccion_PreparacionAnticipada pa
INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.ProgramaProduccionID=pa.ProgramaProduccionID
LEFT JOIN dbo.ERP_Maquinas maq
    ON maq.MaquinaID=pp.MaquinaID
WHERE pa.PreparacionAnticipadaID=@PreparacionAnticipadaID
  AND pa.TipoTarea=@TipoTarea;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@PreparacionAnticipadaID", SqlDbType.Int).Value = id;
            cmd.Parameters.Add("@TipoTarea", SqlDbType.NVarChar, 40).Value = ProduccionPreparacionTipo.CambioMolde;

            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync())
                return NotFound();

            var estado = rd["Estado"]?.ToString()?.Trim() ?? string.Empty;
            var fechaInicio = rd["FechaInicioReal"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(rd["FechaInicioReal"]);
            var fechaFin = rd["FechaFinReal"] == DBNull.Value
                ? rd["FechaConfirmacion"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(rd["FechaConfirmacion"])
                : Convert.ToDateTime(rd["FechaFinReal"]);

            var limite = rd["LimiteMinutosAplicado"] != DBNull.Value
                ? Convert.ToInt32(rd["LimiteMinutosAplicado"])
                : rd["MinutosMaxCambioMolde"] == DBNull.Value
                    ? 60
                    : Convert.ToInt32(rd["MinutosMaxCambioMolde"]);

            if (limite <= 0)
                limite = 60;

            var ahora = DateTime.Now;
            var minutos = 0;

            if (fechaInicio.HasValue)
            {
                minutos = Math.Max(
                    0,
                    (int)Math.Ceiling(((fechaFin ?? ahora) - fechaInicio.Value).TotalMinutes));
            }

            var nivel = "NINGUNA";

            if (string.Equals(estado, ProduccionPreparacionEstado.EnProceso, StringComparison.OrdinalIgnoreCase))
            {
                if (minutos >= limite) nivel = "EXCEDIDO";
                else if (minutos >= Math.Max(0, limite - 10)) nivel = "CRITICO";
                else if (minutos >= Math.Max(0, limite - 30)) nivel = "ADVERTENCIA";
                else nivel = "NORMAL";
            }

            return Json(new
            {
                preparacionAnticipadaID = id,
                estado,
                maquinaID = rd["MaquinaID"] == DBNull.Value
        ? (int?)null
        : Convert.ToInt32(rd["MaquinaID"]),
                maquinaCodigo = rd["MaquinaCodigo"]?.ToString()?.Trim(),
                fechaInicioReal = fechaInicio,
                fechaFinReal = fechaFin,
                limiteMinutos = limite,
                minutosTranscurridos = minutos,
                minutosRestantes = Math.Max(0, limite - minutos),
                minutosExceso = Math.Max(0, minutos - limite),
                nivel,
                excedioLimite =
        minutos > limite ||
        (
            rd["ExcedioLimite"] != DBNull.Value &&
            Convert.ToBoolean(rd["ExcedioLimite"])
        )
            });
        }

        [HttpGet]
        public async Task<IActionResult> AlertasCambioMolde()
        {
            if (!UsuarioEnSesion()) return Unauthorized();
            var usuarioId = ObtenerUsuarioID();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            var permisos = await ObtenerPermisosPreparacionUsuarioAsync(usuarioId, cn);
            if (!permisos.PuedeVerModulo) return StatusCode(StatusCodes.Status403Forbidden);
            var ahora = DateTime.Now;
            var tareas = await CargarPreparacionAnticipadaAsync(ProduccionPreparacionTipo.CambioMolde, null, null, ahora, false, cn);
            await EnriquecerTareasParejaLhRhAsync(tareas, cn);
            tareas = ConsolidarCambiosMoldeLhRhVisuales(tareas);
            var alertas = tareas
                .Where(x => x.EstaEnProceso && (x.NivelAlertaCambioMolde == "ADVERTENCIA" || x.NivelAlertaCambioMolde == "CRITICO" || x.NivelAlertaCambioMolde == "EXCEDIDO"))
                .Select(x => new
                {
                    x.PreparacionAnticipadaID,
                    x.ProgramaProduccionID,
                    x.MaquinaID,
                    x.MaquinaCodigo,
                    x.NumeroOF,
                    x.TextoCambioMolde,
                    x.EsParejaLhRh,
                    x.GrupoLhRh,
                    x.LadoLhRh,
                    x.ProgramaParejaID,
                    x.NumeroOFPareja,
                    x.NumeroPartePareja,
                    limiteMinutos = x.LimiteCambioMoldeMinutos,
                    minutosTranscurridos = x.MinutosTranscurridosCambioMolde,
                    minutosRestantes = x.MinutosRestantesLimiteCambioMolde,
                    minutosExceso = x.MinutosExcesoCambioMolde,
                    nivel = x.NivelAlertaCambioMolde
                })
                .ToList();
            return Json(alertas);
        }

        private async Task EnriquecerTareasParejaLhRhAsync(List<ProduccionPreparacionTareaVm> tareas, SqlConnection cn)
        {
            if (tareas == null || tareas.Count == 0) return;
            var programasNecesarios = tareas.Where(x => x.ProgramaProduccionID > 0).Select(x => x.ProgramaProduccionID).Distinct().ToHashSet();
            if (programasNecesarios.Count == 0) return;
            var relaciones = new Dictionary<int, RelacionPreparacionLhRhInterna>();
            const string sql = @"
SELECT
    pp.ProgramaProduccionID,
    grupo.GrupoLhRh,
    pareja.ProgramaProduccionID AS ProgramaParejaID,
    ejecucionPareja.EjecucionProduccionID AS EjecucionParejaID,
    COALESCE(NULLIF(LTRIM(RTRIM(sPareja.NumeroOFRecibida)),N''),NULLIF(LTRIM(RTRIM(sPareja.FolioSolicitud)),N''),N'') AS NumeroOFPareja,
    pareja.ParteID AS ParteParejaID,
    pareja.NumeroParte AS NumeroPartePareja,
    pareja.ReferenciaSAP AS ReferenciaSAPPareja,
    pareja.DesignacionDescripcionSAP AS DescripcionPartePareja
FROM dbo.Planeacion_ProgramaProduccion pp
OUTER APPLY
(
    SELECT CHARINDEX(N'NSQ_LHRH_PAIR:',ISNULL(pp.Observaciones,N'')) AS PosGrupo
) marca
OUTER APPLY
(
    SELECT TRY_CONVERT
    (
        INT,
        LEFT
        (
            SUBSTRING(pp.Observaciones,marca.PosGrupo+LEN(N'NSQ_LHRH_PAIR:'),50),
            CHARINDEX(N';',SUBSTRING(pp.Observaciones,marca.PosGrupo+LEN(N'NSQ_LHRH_PAIR:'),50)+N';')-1
        )
    ) AS GrupoLhRh
    WHERE marca.PosGrupo>0
) grupo
OUTER APPLY
(
    SELECT TOP(1)
        p2.ProgramaProduccionID,
        p2.SolicitudProduccionID,
        p2.ParteID,
        p2.NumeroParte,
        p2.ReferenciaSAP,
        p2.DesignacionDescripcionSAP
    FROM dbo.Planeacion_ProgramaProduccion p2
    WHERE grupo.GrupoLhRh IS NOT NULL
      AND p2.ProgramaProduccionID<>pp.ProgramaProduccionID
      AND p2.Observaciones LIKE N'%NSQ_LHRH_PAIR:'+CONVERT(NVARCHAR(20),grupo.GrupoLhRh)+N';%'
    ORDER BY p2.ProgramaProduccionID
) pareja
LEFT JOIN dbo.SolicitudesProduccion sPareja
    ON sPareja.SolicitudProduccionID=pareja.SolicitudProduccionID
OUTER APPLY
(
    SELECT TOP(1) e.EjecucionProduccionID
    FROM dbo.Produccion_Ejecucion e
    WHERE e.ProgramaProduccionID=pareja.ProgramaProduccionID
    ORDER BY e.EjecucionProduccionID DESC
) ejecucionPareja
WHERE grupo.GrupoLhRh IS NOT NULL
  AND pareja.ProgramaProduccionID IS NOT NULL;";
            await using (var cmd = new SqlCommand(sql, cn))
            await using (var rd = await cmd.ExecuteReaderAsync())
            {
                while (await rd.ReadAsync())
                {
                    var programaId = Convert.ToInt32(rd["ProgramaProduccionID"]);
                    if (!programasNecesarios.Contains(programaId)) continue;
                    relaciones[programaId] = new RelacionPreparacionLhRhInterna
                    {
                        GrupoLhRh = PreparacionNullableInt(rd, "GrupoLhRh"),
                        ProgramaParejaID = PreparacionNullableInt(rd, "ProgramaParejaID"),
                        EjecucionParejaID = PreparacionNullableInt(rd, "EjecucionParejaID"),
                        NumeroOFPareja = PreparacionTexto(rd, "NumeroOFPareja"),
                        ParteParejaID = PreparacionNullableInt(rd, "ParteParejaID"),
                        NumeroPartePareja = PreparacionTexto(rd, "NumeroPartePareja"),
                        ReferenciaSAPPareja = PreparacionTexto(rd, "ReferenciaSAPPareja"),
                        DescripcionPartePareja = PreparacionTexto(rd, "DescripcionPartePareja")
                    };
                }
            }
            foreach (var tarea in tareas)
            {
                if (!relaciones.TryGetValue(tarea.ProgramaProduccionID, out var relacion)) continue;
                tarea.GrupoLhRh = relacion.GrupoLhRh;
                tarea.ProgramaParejaID = relacion.ProgramaParejaID;
                tarea.EjecucionParejaID = relacion.EjecucionParejaID;
                tarea.NumeroOFPareja = relacion.NumeroOFPareja;
                tarea.ParteParejaID = relacion.ParteParejaID;
                tarea.NumeroPartePareja = relacion.NumeroPartePareja;
                tarea.ReferenciaSAPPareja = relacion.ReferenciaSAPPareja;
                tarea.DescripcionPartePareja = relacion.DescripcionPartePareja;
                var ladoActual = DeterminarLadoLhRhPreparacion(tarea.ReferenciaSAP, tarea.NumeroParte, tarea.DescripcionParte);
                var ladoPareja = DeterminarLadoLhRhPreparacion(relacion.ReferenciaSAPPareja, relacion.NumeroPartePareja, relacion.DescripcionPartePareja);
                if (string.IsNullOrWhiteSpace(ladoActual) && string.Equals(ladoPareja, "LH", StringComparison.OrdinalIgnoreCase)) ladoActual = "RH";
                else if (string.IsNullOrWhiteSpace(ladoActual) && string.Equals(ladoPareja, "RH", StringComparison.OrdinalIgnoreCase)) ladoActual = "LH";
                tarea.LadoLhRh = ladoActual;
            }
        }
        private static List<ProduccionPreparacionTareaVm> ConsolidarCambiosMoldeLhRhVisuales(List<ProduccionPreparacionTareaVm> tareas)
        {
            if (tareas == null || tareas.Count == 0) return tareas ?? new List<ProduccionPreparacionTareaVm>();
            var resultado = new List<ProduccionPreparacionTareaVm>();
            var procesados = new HashSet<int>();
            foreach (var tarea in tareas)
            {
                if (!tarea.EsCambioMolde || !tarea.EsParejaLhRh || !tarea.ProgramaParejaID.HasValue)
                {
                    resultado.Add(tarea);
                    continue;
                }
                if (procesados.Contains(tarea.ProgramaProduccionID)) continue;
                var pareja = tareas.FirstOrDefault(x => x.EsCambioMolde && x.ProgramaProduccionID == tarea.ProgramaParejaID.Value);
                if (pareja == null)
                {
                    resultado.Add(tarea);
                    procesados.Add(tarea.ProgramaProduccionID);
                    continue;
                }
                var representante = new[] { tarea, pareja }
                    .OrderBy(x => PrioridadEstadoCambioMoldeVisual(x.Estado))
                    .ThenBy(x => x.ProgramaProduccionID)
                    .First();
                resultado.Add(representante);
                procesados.Add(tarea.ProgramaProduccionID);
                procesados.Add(pareja.ProgramaProduccionID);
            }
            return resultado;
        }
        private static int PrioridadEstadoCambioMoldeVisual(string? estado)
        {
            if (string.Equals(estado, ProduccionPreparacionEstado.EnProceso, StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(estado, ProduccionPreparacionEstado.Pendiente, StringComparison.OrdinalIgnoreCase)) return 2;
            if (string.Equals(estado, ProduccionPreparacionEstado.Confirmada, StringComparison.OrdinalIgnoreCase)) return 3;
            return 4;
        }
        private static string? DeterminarLadoLhRhPreparacion(params string?[] valores)
        {
            var tieneLh = false;
            var tieneRh = false;
            foreach (var valor in valores)
            {
                if (string.IsNullOrWhiteSpace(valor)) continue;
                var texto = valor.Trim().ToUpperInvariant();
                if (System.Text.RegularExpressions.Regex.IsMatch(texto, @"(?<![A-Z0-9])LH(?![A-Z0-9])", System.Text.RegularExpressions.RegexOptions.CultureInvariant)) tieneLh = true;
                if (System.Text.RegularExpressions.Regex.IsMatch(texto, @"(?<![A-Z0-9])RH(?![A-Z0-9])", System.Text.RegularExpressions.RegexOptions.CultureInvariant)) tieneRh = true;
            }
            if (tieneLh == tieneRh) return null;
            return tieneLh ? "LH" : "RH";
        }
        private async Task<List<CambioMoldeOperacionInterna>> CargarGrupoCambioMoldeAsync(int preparacionAnticipadaId, SqlConnection cn, SqlTransaction tx)
        {
            var lista = new List<CambioMoldeOperacionInterna>();
            const string sql = @"
;WITH Origen AS
(
    SELECT
        pa.PreparacionAnticipadaID,
        pa.ProgramaProduccionID,
        grupo.GrupoLhRh
    FROM dbo.Produccion_PreparacionAnticipada pa WITH(UPDLOCK,HOLDLOCK)
    INNER JOIN dbo.Planeacion_ProgramaProduccion pp
        ON pp.ProgramaProduccionID=pa.ProgramaProduccionID
    OUTER APPLY
    (
        SELECT CHARINDEX(N'NSQ_LHRH_PAIR:',ISNULL(pp.Observaciones,N'')) AS PosGrupo
    ) marca
    OUTER APPLY
    (
        SELECT TRY_CONVERT
        (
            INT,
            LEFT
            (
                SUBSTRING(pp.Observaciones,marca.PosGrupo+LEN(N'NSQ_LHRH_PAIR:'),50),
                CHARINDEX(N';',SUBSTRING(pp.Observaciones,marca.PosGrupo+LEN(N'NSQ_LHRH_PAIR:'),50)+N';')-1
            )
        ) AS GrupoLhRh
        WHERE marca.PosGrupo>0
    ) grupo
    WHERE pa.PreparacionAnticipadaID=@PreparacionAnticipadaID
      AND pa.TipoTarea=@TipoTarea
      AND pa.Activo=1
)
SELECT
    pa.PreparacionAnticipadaID,
    pa.ProgramaProduccionID,
    pa.Estado,
    pa.UsuarioInicioID,
    pa.FechaInicioReal,
    pa.LimiteMinutosAplicado,
    o.GrupoLhRh,
    pp.MaquinaID,
    ISNULL(maq.MinutosMaxCambioMolde,60) AS MinutosMaxCambioMolde,
    pp.MoldeID,
    pp.MoldeCodigo,
    pp.FechaInicioProgramada,
    pp.Cambio,
    pp.Arranque
FROM Origen o
INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON
    (
        pp.ProgramaProduccionID=o.ProgramaProduccionID
        OR
        (
            o.GrupoLhRh IS NOT NULL
            AND pp.ProgramaProduccionID<>o.ProgramaProduccionID
            AND pp.Observaciones LIKE N'%NSQ_LHRH_PAIR:'+CONVERT(NVARCHAR(20),o.GrupoLhRh)+N';%'
        )
    )
INNER JOIN dbo.Produccion_PreparacionAnticipada pa WITH(UPDLOCK,HOLDLOCK)
    ON pa.ProgramaProduccionID=pp.ProgramaProduccionID
   AND pa.TipoTarea=@TipoTarea
   AND pa.Activo=1
LEFT JOIN dbo.ERP_Maquinas maq
    ON maq.MaquinaID=pp.MaquinaID
ORDER BY pp.ProgramaProduccionID,pa.PreparacionAnticipadaID DESC;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@PreparacionAnticipadaID", SqlDbType.Int).Value = preparacionAnticipadaId;
            cmd.Parameters.Add("@TipoTarea", SqlDbType.NVarChar, 40).Value = ProduccionPreparacionTipo.CambioMolde;
            await using var rd = await cmd.ExecuteReaderAsync();
            var programasAgregados = new HashSet<int>();
            while (await rd.ReadAsync())
            {
                var programaId = Convert.ToInt32(rd["ProgramaProduccionID"]);
                if (!programasAgregados.Add(programaId)) continue;
                lista.Add(new CambioMoldeOperacionInterna
                {
                    PreparacionAnticipadaID = Convert.ToInt32(rd["PreparacionAnticipadaID"]),
                    ProgramaProduccionID = programaId,
                    Estado = PreparacionTexto(rd, "Estado") ?? string.Empty,
                    UsuarioInicioID = PreparacionNullableInt(rd, "UsuarioInicioID"),
                    FechaInicioReal = PreparacionNullableDateTime(rd, "FechaInicioReal"),
                    LimiteMinutosAplicado = PreparacionNullableInt(rd, "LimiteMinutosAplicado"),
                    GrupoLhRh = PreparacionNullableInt(rd, "GrupoLhRh"),
                    MaquinaID = PreparacionNullableInt(rd, "MaquinaID"),
                    MinutosMaxCambioMolde = rd["MinutosMaxCambioMolde"] == DBNull.Value ? 60 : Convert.ToInt32(rd["MinutosMaxCambioMolde"]),
                    MoldeID = PreparacionNullableInt(rd, "MoldeID"),
                    MoldeCodigo = PreparacionTexto(rd, "MoldeCodigo"),
                    FechaInicioProgramada = PreparacionNullableDateTime(rd, "FechaInicioProgramada"),
                    Cambio = PreparacionNullableTimeSpan(rd, "Cambio"),
                    Arranque = PreparacionNullableTimeSpan(rd, "Arranque")
                });
            }
            return lista;
        }
        private static string? ValidarGrupoFisicoCambioMolde(List<CambioMoldeOperacionInterna> tareas)
        {
            if (tareas == null || tareas.Count == 0) return "No se encontraron tareas de cambio de molde.";
            var origen = tareas[0];
            if (!origen.MaquinaID.HasValue || origen.MaquinaID.Value <= 0) return "El programa no tiene una máquina válida asociada.";
            if (tareas.Any(x => x.MaquinaID != origen.MaquinaID)) return "Las OF de la pareja LH/RH ya no están programadas en la misma máquina. No se puede ejecutar un cambio de molde conjunto.";
            if (tareas.Count > 1)
            {
                if (tareas.Any(x => !MismoMoldePreparacion(origen, x))) return "Las OF de la pareja LH/RH ya no tienen el mismo molde programado. Revisa Planeación antes de continuar.";
                if (tareas.Any(x => x.FechaInicioProgramada != origen.FechaInicioProgramada || x.Cambio != origen.Cambio || x.Arranque != origen.Arranque)) return "Las OF de la pareja LH/RH ya no comparten la misma ventana programada de cambio y arranque. Revisa Planeación antes de continuar.";
            }
            return null;
        }
        private static bool MismoMoldePreparacion(CambioMoldeOperacionInterna a, CambioMoldeOperacionInterna b)
        {
            if (a.MoldeID.HasValue && b.MoldeID.HasValue) return a.MoldeID.Value == b.MoldeID.Value;
            var codigoA = string.IsNullOrWhiteSpace(a.MoldeCodigo) ? null : a.MoldeCodigo.Trim();
            var codigoB = string.IsNullOrWhiteSpace(b.MoldeCodigo) ? null : b.MoldeCodigo.Trim();
            if (codigoA == null && codigoB == null) return true;
            if (codigoA == null || codigoB == null) return false;
            return string.Equals(codigoA, codigoB, StringComparison.OrdinalIgnoreCase);
        }
        private static int ObtenerLimiteGrupoCambioMolde(List<CambioMoldeOperacionInterna> tareas)
        {
            var limites = tareas
                .Select(x => x.LimiteMinutosAplicado.HasValue && x.LimiteMinutosAplicado.Value > 0 ? x.LimiteMinutosAplicado.Value : x.MinutosMaxCambioMolde)
                .Where(x => x > 0)
                .ToList();
            return limites.Count > 0 ? limites.Min() : 60;
        }

        private async Task SincronizarPreparacionAnticipadaAsync(
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            var ahora = DateTime.Now;
            var desde = ahora.Date.AddDays(-1);
            var hasta = ahora.Date.AddDays(PreparacionDiasHorizonte + 1);

            var programas = await CargarProgramasParaPreparacionAsync(desde, hasta, cn, tx);

            foreach (var programa in programas)
            {
                var fechaArranque = programa.FechaArranque;

                var requiereSecado =
                    fechaArranque.HasValue &&
                    programa.HorasSecado.HasValue &&
                    programa.HorasSecado.Value > 0 &&
                    (
                        !string.IsNullOrWhiteSpace(programa.MaterialCodigo) ||
                        !string.IsNullOrWhiteSpace(programa.MaterialDescripcion)
                    );

                if (requiereSecado)
                {
                    var horas = Convert.ToDouble(programa.HorasSecado!.Value);
                    var fechaObjetivo = fechaArranque!.Value;
                    var fechaAviso = fechaObjetivo.AddHours(-horas);

                    await SincronizarTareaPreparacionAsync(
                        programa.ProgramaProduccionID,
                        ProduccionPreparacionTipo.SecadoMaterial,
                        fechaObjetivo,
                        fechaAviso,
                        usuarioId,
                        cn,
                        tx);
                }
                else
                {
                    await CancelarTareaPreparacionNoAplicableAsync(
                        programa.ProgramaProduccionID,
                        ProduccionPreparacionTipo.SecadoMaterial,
                        usuarioId,
                        cn,
                        tx);
                }

                var requiereEmbalaje =
                    fechaArranque.HasValue &&
                    (
                        !string.IsNullOrWhiteSpace(programa.EmbalajeCodigo) ||
                        !string.IsNullOrWhiteSpace(programa.EmbalajeDescripcion) ||
                        (programa.CantidadEmbalajes.HasValue && programa.CantidadEmbalajes.Value > 0)
                    );

                if (requiereEmbalaje)
                {
                    var fechaObjetivo = fechaArranque!.Value;
                    var fechaAviso = fechaObjetivo.AddMinutes(-PreparacionMinutosAnticipacionEmbalaje);

                    await SincronizarTareaPreparacionAsync(
                        programa.ProgramaProduccionID,
                        ProduccionPreparacionTipo.PrepararEmbalaje,
                        fechaObjetivo,
                        fechaAviso,
                        usuarioId,
                        cn,
                        tx);
                }
                else
                {
                    await CancelarTareaPreparacionNoAplicableAsync(
                        programa.ProgramaProduccionID,
                        ProduccionPreparacionTipo.PrepararEmbalaje,
                        usuarioId,
                        cn,
                        tx);
                }

                if (programa.RequiereCambioMolde && programa.FechaCambioMolde.HasValue)
                {
                    var fechaObjetivo = programa.FechaCambioMolde.Value;
                    var fechaAviso = fechaObjetivo.AddMinutes(-PreparacionMinutosAvisoCambioMolde);

                    await SincronizarTareaPreparacionAsync(
                        programa.ProgramaProduccionID,
                        ProduccionPreparacionTipo.CambioMolde,
                        fechaObjetivo,
                        fechaAviso,
                        usuarioId,
                        cn,
                        tx);
                }
                else
                {
                    await CancelarTareaPreparacionNoAplicableAsync(
                        programa.ProgramaProduccionID,
                        ProduccionPreparacionTipo.CambioMolde,
                        usuarioId,
                        cn,
                        tx);
                }
            }
        }

        private async Task<List<ProgramaPreparacionInterno>> CargarProgramasParaPreparacionAsync(
            DateTime desde,
            DateTime hasta,
            SqlConnection cn,
            SqlTransaction tx)
        {
            var lista = new List<ProgramaPreparacionInterno>();

            const string sql = @"
SELECT
    pp.ProgramaProduccionID,
    pp.SolicitudProduccionID,
    pp.SolicitudProduccionDetalleID,
    s.NumeroOFRecibida,
    pp.MaquinaID,
    COALESCE(NULLIF(LTRIM(RTRIM(pp.MaquinaCodigo)),N''),maq.Codigo) AS MaquinaCodigo,
    COALESCE(NULLIF(LTRIM(RTRIM(pp.MaquinaNombre)),N''),maq.Nombre) AS MaquinaNombre,
    ISNULL(maq.MinutosMaxCambioMolde,60) AS MinutosMaxCambioMolde,
    pp.ParteID,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.DesignacionDescripcionSAP AS DescripcionParte,
    pp.MoldeID,
    pp.MoldeCodigo,
    CONVERT(INT,ISNULL(pp.CantidadProgramada,0)) AS CantidadProgramada,
    pp.FechaInicioProgramada,
    pp.FechaFinProgramada,
    pp.Cambio,
    pp.Arranque,
    d.TipoSecado,
    d.HorasSecado,
    d.MaterialCodigo,
    d.MaterialDescripcion,
    d.EmbalajeCodigo,
    d.EmbalajeDescripcion,
    d.PiezasPorEmbalaje,
    d.CantidadEmbalajes,
    anterior.ProgramaProduccionID AS ProgramaAnteriorID,
    anterior.MoldeID AS MoldeAnteriorID,
    anterior.MoldeCodigo AS MoldeAnteriorCodigo
FROM dbo.Planeacion_ProgramaProduccion pp
LEFT JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID=pp.SolicitudProduccionID
LEFT JOIN dbo.SolicitudesProduccionDetalle d
    ON d.SolicitudProduccionDetalleID=pp.SolicitudProduccionDetalleID
   AND d.Activo=1
LEFT JOIN dbo.ERP_Maquinas maq
    ON maq.MaquinaID=pp.MaquinaID
OUTER APPLY
(
    SELECT TOP(1)
        ant.ProgramaProduccionID,
        ant.MoldeID,
        ant.MoldeCodigo
    FROM dbo.Planeacion_ProgramaProduccion ant
    WHERE ant.Activo=1
      AND ant.ProgramaProduccionID<>pp.ProgramaProduccionID
      AND ant.MaquinaID=pp.MaquinaID
      AND ant.FechaInicioProgramada<pp.FechaInicioProgramada
      AND ISNULL(ant.EstatusID,1)<>99
    ORDER BY ant.FechaInicioProgramada DESC,ant.ProgramaProduccionID DESC
) anterior
WHERE pp.Activo=1
  AND pp.MaquinaID IS NOT NULL
  AND pp.FechaInicioProgramada IS NOT NULL
  AND pp.FechaInicioProgramada>=@Desde
  AND pp.FechaInicioProgramada<@Hasta
  AND ISNULL(pp.EstatusID,1) NOT IN(5,6,9,99)
ORDER BY
    pp.FechaInicioProgramada,
    ISNULL(pp.SecuenciaMaquina,999999),
    pp.ProgramaProduccionID;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@Desde", SqlDbType.DateTime).Value = desde;
            cmd.Parameters.Add("@Hasta", SqlDbType.DateTime).Value = hasta;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var inicio = Convert.ToDateTime(rd["FechaInicioProgramada"]);
                var cambio = PreparacionNullableTimeSpan(rd, "Cambio");
                var arranque = PreparacionNullableTimeSpan(rd, "Arranque");

                var fechaCambio = ConstruirFechaPreparacion(inicio, cambio);
                var fechaArranque = ConstruirFechaPreparacion(inicio, arranque);

                var moldeActualId = PreparacionNullableInt(rd, "MoldeID");
                var moldeAnteriorId = PreparacionNullableInt(rd, "MoldeAnteriorID");
                var moldeActualCodigo = PreparacionTexto(rd, "MoldeCodigo");
                var moldeAnteriorCodigo = PreparacionTexto(rd, "MoldeAnteriorCodigo");

                var requiereCambioMolde = DeterminarCambioMoldePreparacion(
                    moldeAnteriorId,
                    moldeAnteriorCodigo,
                    moldeActualId,
                    moldeActualCodigo);

                lista.Add(new ProgramaPreparacionInterno
                {
                    ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                    SolicitudProduccionID = PreparacionNullableInt(rd, "SolicitudProduccionID"),
                    SolicitudProduccionDetalleID = PreparacionNullableInt(rd, "SolicitudProduccionDetalleID"),
                    NumeroOF = PreparacionTexto(rd, "NumeroOFRecibida"),
                    MaquinaID = PreparacionNullableInt(rd, "MaquinaID"),
                    MaquinaCodigo = PreparacionTexto(rd, "MaquinaCodigo"),
                    MaquinaNombre = PreparacionTexto(rd, "MaquinaNombre"),
                    MinutosMaxCambioMolde = rd["MinutosMaxCambioMolde"] == DBNull.Value ? 60 : Convert.ToInt32(rd["MinutosMaxCambioMolde"]),
                    ParteID = PreparacionNullableInt(rd, "ParteID"),
                    NumeroParte = PreparacionTexto(rd, "NumeroParte"),
                    ReferenciaSAP = PreparacionTexto(rd, "ReferenciaSAP"),
                    DescripcionParte = PreparacionTexto(rd, "DescripcionParte"),
                    MoldeID = moldeActualId,
                    MoldeCodigo = moldeActualCodigo,
                    MoldeAnteriorID = moldeAnteriorId,
                    MoldeAnteriorCodigo = moldeAnteriorCodigo,
                    CantidadProgramada = rd["CantidadProgramada"] == DBNull.Value ? 0 : Convert.ToInt32(rd["CantidadProgramada"]),
                    FechaInicioProgramada = inicio,
                    FechaFinProgramada = PreparacionNullableDateTime(rd, "FechaFinProgramada"),
                    Cambio = cambio,
                    Arranque = arranque,
                    FechaCambioMolde = fechaCambio,
                    FechaArranque = fechaArranque,
                    TipoSecado = PreparacionTexto(rd, "TipoSecado"),
                    HorasSecado = PreparacionNullableDecimal(rd, "HorasSecado"),
                    MaterialCodigo = PreparacionTexto(rd, "MaterialCodigo"),
                    MaterialDescripcion = PreparacionTexto(rd, "MaterialDescripcion"),
                    EmbalajeCodigo = PreparacionTexto(rd, "EmbalajeCodigo"),
                    EmbalajeDescripcion = PreparacionTexto(rd, "EmbalajeDescripcion"),
                    PiezasPorEmbalaje = PreparacionNullableDecimal(rd, "PiezasPorEmbalaje"),
                    CantidadEmbalajes = PreparacionNullableDecimal(rd, "CantidadEmbalajes"),
                    RequiereCambioMolde = requiereCambioMolde
                });
            }

            return lista;
        }
        private static async Task SincronizarTareaPreparacionAsync(int programaProduccionId, string tipoTarea, DateTime fechaObjetivo, DateTime fechaAviso, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            fechaObjetivo = NormalizarFechaPreparacion(fechaObjetivo);
            fechaAviso = NormalizarFechaPreparacion(fechaAviso);
            const string sql = @"
DECLARE @PreparacionAnticipadaID INT;
DECLARE @EstadoActual NVARCHAR(30);
DECLARE @EsRetornoUrgente BIT=0;
SELECT TOP(1)
    @PreparacionAnticipadaID=PreparacionAnticipadaID,
    @EstadoActual=Estado,
    @EsRetornoUrgente=CASE
        WHEN TipoTarea=N'CAMBIO_MOLDE'
         AND ISNULL(Observaciones,N'') LIKE N'%NSQ_RETORNO_URGENTE:%'
            THEN 1
        ELSE 0
    END
FROM dbo.Produccion_PreparacionAnticipada WITH(UPDLOCK,HOLDLOCK)
WHERE ProgramaProduccionID=@ProgramaProduccionID
  AND TipoTarea=@TipoTarea
ORDER BY PreparacionAnticipadaID DESC;
IF @PreparacionAnticipadaID IS NULL
BEGIN
    INSERT INTO dbo.Produccion_PreparacionAnticipada
    (
        ProgramaProduccionID,TipoTarea,FechaObjetivo,FechaAviso,Estado,
        UsuarioConfirmacionID,FechaConfirmacion,Observaciones,Activo,
        UsuarioCreacionID,FechaCreacion,UsuarioInicioID,FechaInicioReal,
        FechaFinReal,DuracionRealMinutos,LimiteMinutosAplicado,
        ExcedioLimite,MotivoExceso
    )
    VALUES
    (
        @ProgramaProduccionID,@TipoTarea,@FechaObjetivo,@FechaAviso,@EstadoPendiente,
        NULL,NULL,NULL,1,@UsuarioID,SYSDATETIME(),NULL,NULL,NULL,NULL,NULL,0,NULL
    );
END
ELSE IF @EsRetornoUrgente=1 AND @EstadoActual IN(@EstadoPendiente,@EstadoEnProceso)
BEGIN
    UPDATE dbo.Produccion_PreparacionAnticipada
    SET Activo=1,
        UsuarioModificacionID=@UsuarioID,
        FechaModificacion=SYSDATETIME()
    WHERE PreparacionAnticipadaID=@PreparacionAnticipadaID;
END
ELSE IF @EstadoActual=@EstadoEnProceso
BEGIN
    UPDATE dbo.Produccion_PreparacionAnticipada
    SET FechaObjetivo=@FechaObjetivo,
        FechaAviso=@FechaAviso,
        Activo=1,
        UsuarioModificacionID=@UsuarioID,
        FechaModificacion=SYSDATETIME()
    WHERE PreparacionAnticipadaID=@PreparacionAnticipadaID;
END
ELSE IF @EstadoActual<>@EstadoConfirmada
BEGIN
    UPDATE dbo.Produccion_PreparacionAnticipada
    SET FechaObjetivo=@FechaObjetivo,
        FechaAviso=@FechaAviso,
        Estado=@EstadoPendiente,
        UsuarioInicioID=NULL,
        FechaInicioReal=NULL,
        FechaFinReal=NULL,
        DuracionRealMinutos=NULL,
        LimiteMinutosAplicado=NULL,
        ExcedioLimite=0,
        MotivoExceso=NULL,
        UsuarioConfirmacionID=NULL,
        FechaConfirmacion=NULL,
        Activo=1,
        UsuarioModificacionID=@UsuarioID,
        FechaModificacion=SYSDATETIME()
    WHERE PreparacionAnticipadaID=@PreparacionAnticipadaID;
END;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
            cmd.Parameters.Add("@TipoTarea", SqlDbType.NVarChar, 40).Value = tipoTarea;
            cmd.Parameters.Add("@FechaObjetivo", SqlDbType.DateTime2).Value = fechaObjetivo;
            cmd.Parameters.Add("@FechaAviso", SqlDbType.DateTime2).Value = fechaAviso;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@EstadoPendiente", SqlDbType.NVarChar, 30).Value = ProduccionPreparacionEstado.Pendiente;
            cmd.Parameters.Add("@EstadoEnProceso", SqlDbType.NVarChar, 30).Value = ProduccionPreparacionEstado.EnProceso;
            cmd.Parameters.Add("@EstadoConfirmada", SqlDbType.NVarChar, 30).Value = ProduccionPreparacionEstado.Confirmada;
            await cmd.ExecuteNonQueryAsync();
        }
        private static async Task CancelarTareaPreparacionNoAplicableAsync(int programaProduccionId, string tipoTarea, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Produccion_PreparacionAnticipada
SET Estado=@EstadoCancelada,
    Activo=0,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=SYSDATETIME()
WHERE ProgramaProduccionID=@ProgramaProduccionID
  AND TipoTarea=@TipoTarea
  AND Estado=@EstadoPendiente
  AND Activo=1
  AND NOT
  (
      TipoTarea=N'CAMBIO_MOLDE'
      AND ISNULL(Observaciones,N'') LIKE N'%NSQ_RETORNO_URGENTE:%'
  );";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
            cmd.Parameters.Add("@TipoTarea", SqlDbType.NVarChar, 40).Value = tipoTarea;
            cmd.Parameters.Add("@EstadoCancelada", SqlDbType.NVarChar, 30).Value = ProduccionPreparacionEstado.Cancelada;
            cmd.Parameters.Add("@EstadoPendiente", SqlDbType.NVarChar, 30).Value = ProduccionPreparacionEstado.Pendiente;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            await cmd.ExecuteNonQueryAsync();
        }
        private async Task<List<ProduccionPreparacionTareaVm>> CargarPreparacionAnticipadaAsync(
            string? tipoTarea,
            string? filtro,
            int? maquinaId,
            DateTime ahora,
            bool soloHistorial,
            SqlConnection cn)
        {
            var lista = new List<ProduccionPreparacionTareaVm>();

            const string sql = @"
SELECT
    pa.PreparacionAnticipadaID,
    pa.ProgramaProduccionID,
    pa.TipoTarea,
    pa.FechaObjetivo,
    pa.FechaAviso,
    pa.Estado,
    pa.UsuarioInicioID,
    pa.FechaInicioReal,
    pa.FechaFinReal,
    pa.DuracionRealMinutos,
    pa.LimiteMinutosAplicado,
    ISNULL(pa.ExcedioLimite,0) AS ExcedioLimite,
    pa.MotivoExceso,
    pa.UsuarioConfirmacionID,
    pa.FechaConfirmacion,
    pa.Observaciones,

    LTRIM(RTRIM(
        ISNULL(pInicio.Nombre,N'')+N' '+
        ISNULL(pInicio.ApellidoPaterno,N'')+N' '+
        ISNULL(pInicio.ApellidoMaterno,N'')
    )) AS UsuarioInicioNombre,

    LTRIM(RTRIM(
        ISNULL(pConfirma.Nombre,N'')+N' '+
        ISNULL(pConfirma.ApellidoPaterno,N'')+N' '+
        ISNULL(pConfirma.ApellidoMaterno,N'')
    )) AS UsuarioConfirmacionNombre,

    ejecucion.EjecucionProduccionID,
    pp.SolicitudProduccionID,
    pp.SolicitudProduccionDetalleID,
    s.NumeroOFRecibida,
    pp.MaquinaID,
    COALESCE(NULLIF(LTRIM(RTRIM(pp.MaquinaCodigo)),N''),maq.Codigo) AS MaquinaCodigo,
    COALESCE(NULLIF(LTRIM(RTRIM(pp.MaquinaNombre)),N''),maq.Nombre) AS MaquinaNombre,
    ISNULL(maq.MinutosMaxCambioMolde,60) AS MinutosMaxCambioMolde,
    pp.ParteID,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.DesignacionDescripcionSAP AS DescripcionParte,
    pp.MoldeID,
    pp.MoldeCodigo,
    anterior.MoldeID AS MoldeAnteriorID,
    anterior.MoldeCodigo AS MoldeAnteriorCodigo,
    CONVERT(INT,ISNULL(pp.CantidadProgramada,0)) AS CantidadProgramada,
    pp.FechaInicioProgramada,
    pp.FechaFinProgramada,
    pp.Cambio,
    pp.Arranque,
    d.TipoSecado,
    d.HorasSecado,
d.CantidadMpKg,
CONVERT(DECIMAL(18,4),ISNULL
(
    (
        SELECT SUM(ISNULL(r.CantidadRecibidaProduccion,0))
        FROM dbo.Produccion_RecepcionMateriales r
        WHERE r.Activo=1
          AND r.TipoOrigen=N'MP'
          AND r.EstadoRecepcion IN(N'RECIBIDO_COMPLETO',N'RECIBIDO_PARCIAL')
          AND r.SolicitudProduccionID=pp.SolicitudProduccionID
          AND
          (
              r.ProgramaProduccionID=pp.ProgramaProduccionID
              OR
              (
                  r.ProgramaProduccionID IS NULL
                  AND r.SolicitudProduccionDetalleID=pp.SolicitudProduccionDetalleID
              )
              OR
              (
                  r.ProgramaProduccionID IS NULL
                  AND r.SolicitudProduccionDetalleID IS NULL
                  AND
                  (
                      r.MaterialSolicitadoID=d.MaterialID
                      OR
                      (
                          d.MaterialID IS NULL
                          AND UPPER(LTRIM(RTRIM(ISNULL(r.CodigoSolicitadoSnapshot,N''))))=
                              UPPER(LTRIM(RTRIM(ISNULL(d.MaterialCodigo,N''))))
                      )
                  )
              )
          )
    ),0
)) AS CantidadMpRecibidaProduccionKg,
    d.MaterialCodigo,
    d.MaterialDescripcion,
    d.EmbalajeCodigo,
    d.EmbalajeDescripcion,
    d.PiezasPorEmbalaje,
    d.CantidadEmbalajes,
    opPrincipal.PersonaID AS OperadorPrincipalID,
    opPrincipal.NombreCompleto AS OperadorPrincipalNombre,
    opAuxiliar.PersonaID AS OperadorAuxiliarID,
    opAuxiliar.NombreCompleto AS OperadorAuxiliarNombre
FROM dbo.Produccion_PreparacionAnticipada pa
INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.ProgramaProduccionID=pa.ProgramaProduccionID
LEFT JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID=pp.SolicitudProduccionID
LEFT JOIN dbo.SolicitudesProduccionDetalle d
    ON d.SolicitudProduccionDetalleID=pp.SolicitudProduccionDetalleID
   AND d.Activo=1
LEFT JOIN dbo.ERP_Maquinas maq
    ON maq.MaquinaID=pp.MaquinaID
LEFT JOIN dbo.Usuarios uInicio
    ON uInicio.UsuarioID=pa.UsuarioInicioID
LEFT JOIN dbo.Persona pInicio
    ON pInicio.PersonaID=uInicio.PersonaID
LEFT JOIN dbo.Usuarios uConfirma
    ON uConfirma.UsuarioID=pa.UsuarioConfirmacionID
LEFT JOIN dbo.Persona pConfirma
    ON pConfirma.PersonaID=uConfirma.PersonaID
OUTER APPLY
(
    SELECT TOP(1) e.EjecucionProduccionID
    FROM dbo.Produccion_Ejecucion e
    WHERE e.ProgramaProduccionID=pp.ProgramaProduccionID
      AND e.Activo=1
    ORDER BY e.EjecucionProduccionID DESC
) ejecucion
OUTER APPLY
(
    SELECT TOP(1)
        ant.MoldeID,
        ant.MoldeCodigo
    FROM dbo.Planeacion_ProgramaProduccion ant
    WHERE ant.Activo=1
      AND ant.ProgramaProduccionID<>pp.ProgramaProduccionID
      AND ant.MaquinaID=pp.MaquinaID
      AND ant.FechaInicioProgramada<pp.FechaInicioProgramada
      AND ISNULL(ant.EstatusID,1)<>99
    ORDER BY ant.FechaInicioProgramada DESC,ant.ProgramaProduccionID DESC
) anterior
OUTER APPLY
(
    SELECT TOP(1)
        po.PersonaID,
        LTRIM(RTRIM(
            ISNULL(p.Nombre,N'')+N' '+
            ISNULL(p.ApellidoPaterno,N'')+N' '+
            ISNULL(p.ApellidoMaterno,N'')
        )) AS NombreCompleto
    FROM dbo.Planeacion_ProgramaOperadores po
    LEFT JOIN dbo.Persona p ON p.PersonaID=po.PersonaID
    WHERE po.ProgramaProduccionID=pp.ProgramaProduccionID
      AND po.Activo=1
      AND UPPER(LTRIM(RTRIM(ISNULL(po.RolOperador,N''))))=N'PRINCIPAL'
    ORDER BY po.ProgramaOperadorID
) opPrincipal
OUTER APPLY
(
    SELECT TOP(1)
        po.PersonaID,
        LTRIM(RTRIM(
            ISNULL(p.Nombre,N'')+N' '+
            ISNULL(p.ApellidoPaterno,N'')+N' '+
            ISNULL(p.ApellidoMaterno,N'')
        )) AS NombreCompleto
    FROM dbo.Planeacion_ProgramaOperadores po
    LEFT JOIN dbo.Persona p ON p.PersonaID=po.PersonaID
    WHERE po.ProgramaProduccionID=pp.ProgramaProduccionID
      AND po.Activo=1
      AND UPPER(LTRIM(RTRIM(ISNULL(po.RolOperador,N''))))=N'AUXILIAR'
    ORDER BY po.ProgramaOperadorID
) opAuxiliar
WHERE
    (@TipoTarea IS NULL OR pa.TipoTarea=@TipoTarea)
    AND (@MaquinaID IS NULL OR pp.MaquinaID=@MaquinaID)
    AND
    (
        @Filtro IS NULL
        OR pp.NumeroParte LIKE N'%'+@Filtro+N'%'
        OR pp.ReferenciaSAP LIKE N'%'+@Filtro+N'%'
        OR pp.DesignacionDescripcionSAP LIKE N'%'+@Filtro+N'%'
        OR pp.MaquinaCodigo LIKE N'%'+@Filtro+N'%'
        OR pp.MaquinaNombre LIKE N'%'+@Filtro+N'%'
        OR pp.MoldeCodigo LIKE N'%'+@Filtro+N'%'
        OR s.NumeroOFRecibida LIKE N'%'+@Filtro+N'%'
        OR d.MaterialCodigo LIKE N'%'+@Filtro+N'%'
        OR d.MaterialDescripcion LIKE N'%'+@Filtro+N'%'
        OR d.EmbalajeCodigo LIKE N'%'+@Filtro+N'%'
    )
    AND
    (
        (
            @SoloHistorial=0
            AND pa.Activo=1
            AND pp.Activo=1
            AND pa.Estado IN(N'PENDIENTE',N'EN_PROCESO',N'CONFIRMADA')
            AND
            (
                pa.Estado<>N'CONFIRMADA'
                OR pa.FechaConfirmacion>=DATEADD(DAY,-7,GETDATE())
            )
        )
        OR
        (
            @SoloHistorial=1
            AND pa.Estado IN(N'CONFIRMADA',N'CANCELADA')
            AND COALESCE(pa.FechaConfirmacion,pa.FechaModificacion,pa.FechaCreacion)
                >=DATEADD(DAY,-@DiasHistorial,GETDATE())
        )
    )
ORDER BY
    CASE
        WHEN pa.Estado=N'EN_PROCESO' THEN 1
        WHEN pa.Estado=N'PENDIENTE' AND GETDATE()>pa.FechaObjetivo THEN 2
        WHEN pa.Estado=N'PENDIENTE' AND GETDATE()>=pa.FechaAviso THEN 3
        WHEN pa.Estado=N'PENDIENTE' THEN 4
        WHEN pa.Estado=N'CONFIRMADA' THEN 5
        ELSE 6
    END,
    pa.FechaAviso,
    pa.FechaObjetivo,
    pa.PreparacionAnticipadaID;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@TipoTarea", SqlDbType.NVarChar, 40).Value =
                string.IsNullOrWhiteSpace(tipoTarea) ? DBNull.Value : tipoTarea;
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value =
                maquinaId.HasValue && maquinaId.Value > 0 ? maquinaId.Value : DBNull.Value;
            cmd.Parameters.Add("@Filtro", SqlDbType.NVarChar, 200).Value =
                string.IsNullOrWhiteSpace(filtro) ? DBNull.Value : filtro.Trim();
            cmd.Parameters.Add("@SoloHistorial", SqlDbType.Bit).Value = soloHistorial;
            cmd.Parameters.Add("@DiasHistorial", SqlDbType.Int).Value = PreparacionDiasHistorial;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var inicio = PreparacionNullableDateTime(rd, "FechaInicioProgramada");
                var cambio = PreparacionNullableTimeSpan(rd, "Cambio");
                var arranque = PreparacionNullableTimeSpan(rd, "Arranque");

                DateTime? fechaCambio = null;
                DateTime? fechaArranque = null;

                if (inicio.HasValue)
                {
                    fechaCambio = ConstruirFechaPreparacion(inicio.Value, cambio);
                    fechaArranque = ConstruirFechaPreparacion(inicio.Value, arranque);
                }

                lista.Add(new ProduccionPreparacionTareaVm
                {
                    PreparacionAnticipadaID = Convert.ToInt32(rd["PreparacionAnticipadaID"]),
                    ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                    EjecucionProduccionID = PreparacionNullableInt(rd, "EjecucionProduccionID"),
                    TipoTarea = PreparacionTexto(rd, "TipoTarea") ?? string.Empty,
                    Estado = PreparacionTexto(rd, "Estado") ?? ProduccionPreparacionEstado.Pendiente,
                    FechaObjetivo = Convert.ToDateTime(rd["FechaObjetivo"]),
                    FechaAviso = Convert.ToDateTime(rd["FechaAviso"]),
                    UsuarioInicioID = PreparacionNullableInt(rd, "UsuarioInicioID"),
                    UsuarioInicioNombre = PreparacionTexto(rd, "UsuarioInicioNombre"),
                    FechaInicioReal = PreparacionNullableDateTime(rd, "FechaInicioReal"),
                    UsuarioConfirmacionID = PreparacionNullableInt(rd, "UsuarioConfirmacionID"),
                    UsuarioConfirmacionNombre = PreparacionTexto(rd, "UsuarioConfirmacionNombre"),
                    FechaConfirmacion = PreparacionNullableDateTime(rd, "FechaConfirmacion"),
                    FechaFinReal = PreparacionNullableDateTime(rd, "FechaFinReal"),
                    DuracionRealMinutos = PreparacionNullableInt(rd, "DuracionRealMinutos"),
                    LimiteMinutosAplicado = PreparacionNullableInt(rd, "LimiteMinutosAplicado"),
                    ExcedioLimite = rd["ExcedioLimite"] != DBNull.Value && Convert.ToBoolean(rd["ExcedioLimite"]),
                    MotivoExceso = PreparacionTexto(rd, "MotivoExceso"),
                    Observaciones = PreparacionTexto(rd, "Observaciones"),
                    SolicitudProduccionID = PreparacionNullableInt(rd, "SolicitudProduccionID"),
                    SolicitudProduccionDetalleID = PreparacionNullableInt(rd, "SolicitudProduccionDetalleID"),
                    NumeroOF = PreparacionTexto(rd, "NumeroOFRecibida"),
                    MaquinaID = PreparacionNullableInt(rd, "MaquinaID"),
                    MaquinaCodigo = PreparacionTexto(rd, "MaquinaCodigo"),
                    MaquinaNombre = PreparacionTexto(rd, "MaquinaNombre"),
                    MinutosMaxCambioMolde = rd["MinutosMaxCambioMolde"] == DBNull.Value ? 60 : Convert.ToInt32(rd["MinutosMaxCambioMolde"]),
                    ParteID = PreparacionNullableInt(rd, "ParteID"),
                    NumeroParte = PreparacionTexto(rd, "NumeroParte"),
                    ReferenciaSAP = PreparacionTexto(rd, "ReferenciaSAP"),
                    DescripcionParte = PreparacionTexto(rd, "DescripcionParte"),
                    MoldeID = PreparacionNullableInt(rd, "MoldeID"),
                    MoldeCodigo = PreparacionTexto(rd, "MoldeCodigo"),
                    MoldeAnteriorID = PreparacionNullableInt(rd, "MoldeAnteriorID"),
                    MoldeAnteriorCodigo = PreparacionTexto(rd, "MoldeAnteriorCodigo"),
                    CantidadProgramada = rd["CantidadProgramada"] == DBNull.Value ? 0 : Convert.ToInt32(rd["CantidadProgramada"]),
                    FechaInicioProgramada = inicio,
                    FechaFinProgramada = PreparacionNullableDateTime(rd, "FechaFinProgramada"),
                    FechaCambioMolde = fechaCambio,
                    FechaArranque = fechaArranque,
                    TipoSecado = PreparacionTexto(rd, "TipoSecado"),
                    HorasSecado = PreparacionNullableDecimal(rd, "HorasSecado"),
                    CantidadMpKg = PreparacionNullableDecimal(rd, "CantidadMpKg"),
                    CantidadMpRecibidaProduccionKg = rd["CantidadMpRecibidaProduccionKg"] == DBNull.Value ? 0m : Convert.ToDecimal(rd["CantidadMpRecibidaProduccionKg"]),
                    MaterialCodigo = PreparacionTexto(rd, "MaterialCodigo"),
                    MaterialDescripcion = PreparacionTexto(rd, "MaterialDescripcion"),
                    EmbalajeCodigo = PreparacionTexto(rd, "EmbalajeCodigo"),
                    EmbalajeDescripcion = PreparacionTexto(rd, "EmbalajeDescripcion"),
                    PiezasPorEmbalaje = PreparacionNullableDecimal(rd, "PiezasPorEmbalaje"),
                    CantidadEmbalajes = PreparacionNullableDecimal(rd, "CantidadEmbalajes"),
                    OperadorPrincipalID = PreparacionNullableInt(rd, "OperadorPrincipalID"),
                    OperadorPrincipalNombre = PreparacionTexto(rd, "OperadorPrincipalNombre"),
                    OperadorAuxiliarID = PreparacionNullableInt(rd, "OperadorAuxiliarID"),
                    OperadorAuxiliarNombre = PreparacionTexto(rd, "OperadorAuxiliarNombre"),
                    Ahora = ahora
                });
            }

            return lista;
        }

        private static async Task SincronizarRecepcionesFaltantesAsync(int usuarioId, SqlConnection cn)
        {
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                const string sql = @"
DECLARE @Nuevas TABLE
(
    RecepcionMaterialID BIGINT NOT NULL,
    TipoOrigen NVARCHAR(20) NOT NULL,
    MovimientoAlmacenID BIGINT NOT NULL
);


INSERT dbo.Produccion_RecepcionMateriales
(
    TipoOrigen,MovimientoAlmacenID,SolicitudProduccionID,SolicitudProduccionDetalleID,
    ProgramaProduccionID,EjecucionProduccionID,NumeroOFSnapshot,
    MaterialSolicitadoID,MaterialEntregadoID,EmbalajeSolicitadoID,EmbalajeEntregadoID,
    CodigoSolicitadoSnapshot,DescripcionSolicitadaSnapshot,
    CodigoEntregadoSnapshot,DescripcionEntregadaSnapshot,
    TipoMP,Lote,Unidad,CantidadEntregadaAlmacen,FechaEntregaAlmacen,
    UsuarioEntregaAlmacenID,UsuarioEntregaAlmacenNombre,ReferenciaOperacion,
    ObservacionesAlmacen,EstadoRecepcion,CantidadRecibidaProduccion,
    EstadoAclaracion,Activo,UsuarioCreacionID,FechaCreacion
)
OUTPUT INSERTED.RecepcionMaterialID,INSERTED.TipoOrigen,INSERTED.MovimientoAlmacenID
INTO @Nuevas(RecepcionMaterialID,TipoOrigen,MovimientoAlmacenID)
SELECT
    N'MP',
    m.MovimientoID,
    m.SolicitudProduccionID,
    NULL,
    NULL,
    NULL,
    COALESCE(NULLIF(LTRIM(RTRIM(m.NumeroOF)),N''),NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)),N''),NULLIF(LTRIM(RTRIM(s.FolioSolicitud)),N''),N''),
    COALESCE(m.MaterialSolicitadoID,m.MaterialID),
    m.MaterialID,
    NULL,
    NULL,
    solicitado.Codigo,
    solicitado.Nombre,
    entregado.Codigo,
    entregado.Nombre,
    CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(m.TipoMP,N'')))) IN(N'M',N'MOLIDO') THEN N'M' ELSE N'V' END,
    NULLIF(LTRIM(RTRIM(ISNULL(m.Lote,N''))),N''),
    ISNULL(NULLIF(LTRIM(RTRIM(m.Unidad)),N''),N'KG'),
    CONVERT(DECIMAL(18,4),m.Cantidad),
    m.FechaMovimiento,
    m.ResponsableUsuarioID,
    NULLIF(LTRIM(RTRIM(ISNULL(m.EntregadoPorNombre,m.CreadoPor))),N''),
    m.ReferenciaOperacion,
    m.Seguimiento,
    N'PENDIENTE',
    NULL,
    N'NO_APLICA',
    1,
    COALESCE(m.ResponsableUsuarioID,@UsuarioID),
    SYSDATETIME()
FROM dbo.AlmacenMP_Movimientos m WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID=m.SolicitudProduccionID
   AND s.Activo=1
INNER JOIN dbo.ERP_Materiales entregado
    ON entregado.MaterialID=m.MaterialID
   AND entregado.Activo=1
INNER JOIN dbo.ERP_Materiales solicitado
    ON solicitado.MaterialID=COALESCE(m.MaterialSolicitadoID,m.MaterialID)
   AND solicitado.Activo=1
WHERE m.Activo=1
  AND m.TipoMovimiento=N'Salida'
  AND m.SolicitudProduccionID IS NOT NULL
  AND ISNULL(m.Cantidad,0)>0.0005
  -- NSQ_MATERIALES_SYNC_ORIGEN_UNICO_V1_MP
  -- UX_Produccion_RecepcionMateriales_OrigenMovimiento protege la identidad
  -- TipoOrigen + MovimientoAlmacenID aunque una recepcion historica este inactiva.
  -- No se debe intentar crear una segunda recepcion para el mismo movimiento.
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Produccion_RecepcionMateriales r
      WHERE r.TipoOrigen=N'MP'
        AND r.MovimientoAlmacenID=m.MovimientoID
  )
  AND EXISTS
  (
      SELECT 1
      FROM dbo.SolicitudesProduccionDetalle d
      WHERE d.Activo=1
        AND d.SolicitudProduccionID=m.SolicitudProduccionID
        AND ISNULL(d.CantidadMpKg,0)>0
        AND
        (
            d.MaterialID=COALESCE(m.MaterialSolicitadoID,m.MaterialID)
            OR
            (
                d.MaterialID IS NULL
                AND UPPER(LTRIM(RTRIM(ISNULL(d.MaterialCodigo,N''))))=UPPER(LTRIM(RTRIM(solicitado.Codigo)))
            )
        )
  );

-- ============================================================
-- EMBALAJES
-- Solo recupera entregas dirigidas a una OF.
-- EmbalajeSolicitadoID se guarda únicamente en ese flujo.
-- ============================================================
INSERT dbo.Produccion_RecepcionMateriales
(
    TipoOrigen,MovimientoAlmacenID,SolicitudProduccionID,SolicitudProduccionDetalleID,
    ProgramaProduccionID,EjecucionProduccionID,NumeroOFSnapshot,
    MaterialSolicitadoID,MaterialEntregadoID,EmbalajeSolicitadoID,EmbalajeEntregadoID,
    CodigoSolicitadoSnapshot,DescripcionSolicitadaSnapshot,
    CodigoEntregadoSnapshot,DescripcionEntregadaSnapshot,
    TipoMP,Lote,Unidad,CantidadEntregadaAlmacen,FechaEntregaAlmacen,
    UsuarioEntregaAlmacenID,UsuarioEntregaAlmacenNombre,ReferenciaOperacion,
    ObservacionesAlmacen,EstadoRecepcion,CantidadRecibidaProduccion,
    EstadoAclaracion,Activo,UsuarioCreacionID,FechaCreacion
)
OUTPUT INSERTED.RecepcionMaterialID,INSERTED.TipoOrigen,INSERTED.MovimientoAlmacenID
INTO @Nuevas(RecepcionMaterialID,TipoOrigen,MovimientoAlmacenID)
SELECT
    N'EMBALAJE',
    m.MovimientoID,
    m.SolicitudProduccionID,
    NULL,
    NULL,
    NULL,
    COALESCE(NULLIF(LTRIM(RTRIM(m.NumeroOF)),N''),NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)),N''),NULLIF(LTRIM(RTRIM(s.FolioSolicitud)),N''),N''),
    NULL,
    NULL,
    m.EmbalajeSolicitadoID,
    m.EmbalajeID,
    solicitado.Codigo,
    solicitado.Nombre,
    entregado.Codigo,
    entregado.Nombre,
    NULL,
    NULLIF(LTRIM(RTRIM(ISNULL(m.Lote,N''))),N''),
    COALESCE(NULLIF(LTRIM(RTRIM(m.Unidad)),N''),NULLIF(LTRIM(RTRIM(entregado.UnidadDefault)),N''),N'PZS'),
    CONVERT(DECIMAL(18,4),m.Cantidad),
    m.FechaMovimiento,
    m.ResponsableUsuarioID,
    NULLIF(LTRIM(RTRIM(ISNULL(m.EntregadoPorNombre,m.CreadoPor))),N''),
    m.ReferenciaOperacion,
    m.Seguimiento,
    N'PENDIENTE',
    NULL,
    N'NO_APLICA',
    1,
    COALESCE(m.ResponsableUsuarioID,@UsuarioID),
    SYSDATETIME()
FROM dbo.AlmacenEmbalajes_Movimientos m WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID=m.SolicitudProduccionID
   AND s.Activo=1
INNER JOIN dbo.ERP_Embalajes entregado
    ON entregado.EmbalajeID=m.EmbalajeID
   AND entregado.Activo=1
INNER JOIN dbo.ERP_Embalajes solicitado
    ON solicitado.EmbalajeID=m.EmbalajeSolicitadoID
   AND solicitado.Activo=1
WHERE m.Activo=1
  AND m.TipoMovimiento=N'Salida'
  AND m.SolicitudProduccionID IS NOT NULL
  AND m.EmbalajeSolicitadoID IS NOT NULL
  AND ISNULL(m.Cantidad,0)>0.0005
  -- NSQ_MATERIALES_SYNC_ORIGEN_UNICO_V1_EMBALAJE
  -- Misma idempotencia para embalajes: una recepcion historica/inactiva
  -- sigue ocupando la identidad TipoOrigen + MovimientoAlmacenID.
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Produccion_RecepcionMateriales r
      WHERE r.TipoOrigen=N'EMBALAJE'
        AND r.MovimientoAlmacenID=m.MovimientoID
  )
  AND EXISTS
  (
      SELECT 1
      FROM dbo.SolicitudesProduccionDetalle d
      WHERE d.Activo=1
        AND d.SolicitudProduccionID=m.SolicitudProduccionID
        AND ISNULL(d.CantidadEmbalajes,0)>0
        AND UPPER(LTRIM(RTRIM(ISNULL(d.EmbalajeCodigo,N''))))=UPPER(LTRIM(RTRIM(solicitado.Codigo)))
  );

-- ============================================================
-- MARCAR MOVIMIENTOS COMO PENDIENTES DE VALIDACIÓN
-- ============================================================
UPDATE m
SET m.RequiereValidacionProduccion=1,
    m.ValidadoProduccion=0
FROM dbo.AlmacenMP_Movimientos m
INNER JOIN @Nuevas n
    ON n.TipoOrigen=N'MP'
   AND n.MovimientoAlmacenID=m.MovimientoID;

UPDATE m
SET m.RequiereValidacionProduccion=1,
    m.ValidadoProduccion=0
FROM dbo.AlmacenEmbalajes_Movimientos m
INNER JOIN @Nuevas n
    ON n.TipoOrigen=N'EMBALAJE'
   AND n.MovimientoAlmacenID=m.MovimientoID;

INSERT dbo.Produccion_RecepcionMaterialesHistorial
(
    RecepcionMaterialID,Evento,EstadoRecepcionAnterior,EstadoRecepcionNuevo,
    CantidadRecibidaAnterior,CantidadRecibidaNueva,
    EstadoAclaracionAnterior,EstadoAclaracionNuevo,
    Comentario,UsuarioID,FechaEvento
)
SELECT
    n.RecepcionMaterialID,
    N'ENTREGA_ALMACEN_RECUPERADA',
    NULL,
    N'PENDIENTE',
    NULL,
    NULL,
    NULL,
    N'NO_APLICA',
    CASE
        WHEN n.TipoOrigen=N'EMBALAJE' THEN N'Se recuperó automáticamente una entrega de embalaje de Almacén pendiente de confirmación física por Producción.'
        ELSE N'Se recuperó automáticamente una entrega de materia prima de Almacén pendiente de confirmación física por Producción.'
    END,
    @UsuarioID,
    SYSDATETIME()
FROM @Nuevas n;";

                await using var cmd = new SqlCommand(sql, cn, tx);
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                await cmd.ExecuteNonQueryAsync();
                await tx.CommitAsync();
            }
            catch
            {
                try { await tx.RollbackAsync(); } catch { }
                throw;
            }
        }


        private static async Task<List<ProduccionPreparacionMaquinaVm>> CargarMaquinasPreparacionAsync(SqlConnection cn)
        {
            var lista = new List<ProduccionPreparacionMaquinaVm>();

            const string sql = @"
SELECT
    MaquinaID,
    Codigo,
    Nombre,
    ISNULL(MinutosMaxCambioMolde,60) AS MinutosMaxCambioMolde
FROM dbo.ERP_Maquinas
WHERE Activo=1
ORDER BY Codigo,Nombre;";

            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new ProduccionPreparacionMaquinaVm
                {
                    MaquinaID = Convert.ToInt32(rd["MaquinaID"]),
                    Codigo = PreparacionTexto(rd, "Codigo") ?? string.Empty,
                    Nombre = PreparacionTexto(rd, "Nombre"),
                    MinutosMaxCambioMolde = rd["MinutosMaxCambioMolde"] == DBNull.Value ? 60 : Convert.ToInt32(rd["MinutosMaxCambioMolde"])
                });
            }

            return lista;
        }

        private sealed class RelacionPreparacionLhRhInterna
        {
            public int? GrupoLhRh { get; set; }
            public int? ProgramaParejaID { get; set; }
            public int? EjecucionParejaID { get; set; }
            public string? NumeroOFPareja { get; set; }
            public int? ParteParejaID { get; set; }
            public string? NumeroPartePareja { get; set; }
            public string? ReferenciaSAPPareja { get; set; }
            public string? DescripcionPartePareja { get; set; }
        }
        private sealed class CambioMoldeOperacionInterna
        {
            public int PreparacionAnticipadaID { get; set; }
            public int ProgramaProduccionID { get; set; }
            public string Estado { get; set; } = string.Empty;
            public int? UsuarioInicioID { get; set; }
            public DateTime? FechaInicioReal { get; set; }
            public int? LimiteMinutosAplicado { get; set; }
            public int? GrupoLhRh { get; set; }
            public int? MaquinaID { get; set; }
            public int MinutosMaxCambioMolde { get; set; } = 60;
            public int? MoldeID { get; set; }
            public string? MoldeCodigo { get; set; }
            public DateTime? FechaInicioProgramada { get; set; }
            public TimeSpan? Cambio { get; set; }
            public TimeSpan? Arranque { get; set; }
        }

        //PERMISOS

        private sealed class PermisosPreparacionUsuario
        {
            // NSQ_FIX_PRODUCCION_CARGOS_DEPARTAMENTO_V1_2
            public bool EsAdministradorERP { get; set; }
            public bool EsDepartamentoProduccion { get; set; }
            public bool EsEncargadoProduccion { get; set; }
            public bool EsTecnicoProduccion { get; set; }
            public bool EsOperadorProduccion { get; set; }
            public bool EsSMED { get; set; }
            public bool EsAuxiliarProduccion { get; set; }

            public bool PuedeVerTodo =>
                EsAdministradorERP ||
                EsEncargadoProduccion;

            public bool PuedeVerModulo =>
                EsAdministradorERP ||
                (
                    EsDepartamentoProduccion &&
                    (
                        EsEncargadoProduccion ||
                        EsTecnicoProduccion ||
                        EsOperadorProduccion ||
                        EsAuxiliarProduccion ||
                        EsSMED
                    )
                );

            public bool PuedeGestionarCambioMolde =>
                PuedeVerTodo ||
                EsTecnicoProduccion ||
                EsSMED;

            public bool PuedeGestionarSecado =>
                PuedeVerTodo ||
                EsTecnicoProduccion ||
                EsSMED;

            public bool PuedeGestionarEmbalaje =>
                PuedeVerTodo ||
                EsAuxiliarProduccion;
        }

        private async Task<PermisosPreparacionUsuario> ObtenerPermisosPreparacionUsuarioAsync(
            int usuarioId,
            SqlConnection cn,
            SqlTransaction? tx = null)
        {
            var permisos = new PermisosPreparacionUsuario();
            if (usuarioId <= 0)
                return permisos;

            const string sql = @"
SELECT TOP(1)
    u.RolID,
    u.DepartamentoID,
    LTRIM(RTRIM(ISNULL(d.NombreDepartamento,N''))) AS NombreDepartamento,
    ISNULL(p.EsColaboradorActivo,0) AS EsColaboradorActivo,
    LTRIM(RTRIM(ISNULL(p.Puesto,N''))) AS Puesto
FROM dbo.Usuarios u
LEFT JOIN dbo.Persona p
    ON p.PersonaID=u.PersonaID
LEFT JOIN dbo.Departamentos d
    ON d.DepartamentoID=u.DepartamentoID
WHERE u.UsuarioID=@UsuarioID
  AND u.Activo=1;";

            await using var cmd = tx == null ? new SqlCommand(sql, cn) : new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync())
                return permisos;

            var rolId = rd["RolID"] == DBNull.Value ? 0 : Convert.ToInt32(rd["RolID"]);
            var colaboradorActivo = rd["EsColaboradorActivo"] != DBNull.Value && Convert.ToBoolean(rd["EsColaboradorActivo"]);
            var departamento = rd["NombreDepartamento"]?.ToString()?.Trim() ?? string.Empty;
            var puesto = rd["Puesto"]?.ToString()?.Trim() ?? string.Empty;

            permisos.EsAdministradorERP = rolId == 1;

            if (!colaboradorActivo && !permisos.EsAdministradorERP)
                return permisos;

            var departamentoNormalizado = departamento.ToUpperInvariant();
            var puestoNormalizado = puesto.ToUpperInvariant();

            permisos.EsDepartamentoProduccion =
                departamentoNormalizado.Contains("PRODUC");

            if (!permisos.EsDepartamentoProduccion)
                return permisos;

            /*
                El DEPARTAMENTO define que pertenece al modulo Produccion.
                Persona.Puesto define las reglas internas.

                Ejemplos reales soportados:
                  - Tecnico de Inyeccion
                  - ENCARGADO DE PRODUCCION
                  - Auxiliares de Produccion
                  - OPERADOR

                Se conserva SMED por compatibilidad con la logica existente.
            */
            permisos.EsEncargadoProduccion =
                puestoNormalizado.Contains("ENCARGAD");

            permisos.EsTecnicoProduccion =
                puestoNormalizado.Contains("TECNICO") ||
                puestoNormalizado.Contains("TÉCNICO");

            permisos.EsOperadorProduccion =
                puestoNormalizado.Contains("OPERADOR");

            permisos.EsSMED =
                puestoNormalizado.Contains("SMED");

            permisos.EsAuxiliarProduccion =
                puestoNormalizado.Contains("AUXILIAR");

            return permisos;
        }

        // ============================================================
        // DETECCION DE CAMBIO REAL DE MOLDE
        // ============================================================

        private static bool DeterminarCambioMoldePreparacion(
            int? moldeAnteriorId,
            string? moldeAnteriorCodigo,
            int? moldeActualId,
            string? moldeActualCodigo)
        {
            if (moldeAnteriorId.HasValue && moldeActualId.HasValue)
                return moldeAnteriorId.Value != moldeActualId.Value;

            var anterior = string.IsNullOrWhiteSpace(moldeAnteriorCodigo)
                ? null
                : moldeAnteriorCodigo.Trim();

            var actual = string.IsNullOrWhiteSpace(moldeActualCodigo)
                ? null
                : moldeActualCodigo.Trim();

            if (anterior == null || actual == null)
                return false;

            return !string.Equals(anterior, actual, StringComparison.OrdinalIgnoreCase);
        }

        // ============================================================
        // FECHAS / HELPERS
        // ============================================================

        private static DateTime ConstruirFechaPreparacion(
            DateTime fechaInicioPrograma,
            TimeSpan? hora)
        {
            if (!hora.HasValue)
                return NormalizarFechaPreparacion(fechaInicioPrograma);

            var fecha = fechaInicioPrograma.Date.Add(hora.Value);

            if (fecha < fechaInicioPrograma)
                fecha = fecha.AddDays(1);

            return NormalizarFechaPreparacion(fecha);
        }

        private static DateTime NormalizarFechaPreparacion(DateTime value)
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

        private bool UsuarioEnSesion()
        {
            return HttpContext.Session.GetInt32("UsuarioID").HasValue;
        }

        private int ObtenerUsuarioID()
        {
            return HttpContext.Session.GetInt32("UsuarioID") ?? 0;
        }

        private static string? PreparacionTexto(SqlDataReader rd, string columna)
        {
            return rd[columna] == DBNull.Value
                ? null
                : rd[columna].ToString()?.Trim();
        }

        private static int? PreparacionNullableInt(SqlDataReader rd, string columna)
        {
            return rd[columna] == DBNull.Value
                ? null
                : Convert.ToInt32(rd[columna]);
        }

        private static decimal? PreparacionNullableDecimal(SqlDataReader rd, string columna)
        {
            return rd[columna] == DBNull.Value
                ? null
                : Convert.ToDecimal(rd[columna]);
        }

        private static DateTime? PreparacionNullableDateTime(SqlDataReader rd, string columna)
        {
            return rd[columna] == DBNull.Value
                ? null
                : Convert.ToDateTime(rd[columna]);
        }

        private static TimeSpan? PreparacionNullableTimeSpan(SqlDataReader rd, string columna)
        {
            return rd[columna] == DBNull.Value
                ? null
                : (TimeSpan)rd[columna];
        }

        private sealed class ProgramaPreparacionInterno
        {
            public int ProgramaProduccionID { get; set; }
            public int? SolicitudProduccionID { get; set; }
            public int? SolicitudProduccionDetalleID { get; set; }
            public string? NumeroOF { get; set; }
            public int? MaquinaID { get; set; }
            public string? MaquinaCodigo { get; set; }
            public string? MaquinaNombre { get; set; }
            public int MinutosMaxCambioMolde { get; set; } = 60;
            public int? ParteID { get; set; }
            public string? NumeroParte { get; set; }
            public string? ReferenciaSAP { get; set; }
            public string? DescripcionParte { get; set; }
            public int? MoldeID { get; set; }
            public string? MoldeCodigo { get; set; }
            public int? MoldeAnteriorID { get; set; }
            public string? MoldeAnteriorCodigo { get; set; }
            public int CantidadProgramada { get; set; }
            public DateTime FechaInicioProgramada { get; set; }
            public DateTime? FechaFinProgramada { get; set; }
            public TimeSpan? Cambio { get; set; }
            public TimeSpan? Arranque { get; set; }
            public DateTime? FechaCambioMolde { get; set; }
            public DateTime? FechaArranque { get; set; }
            public string? TipoSecado { get; set; }
            public decimal? HorasSecado { get; set; }
            public string? MaterialCodigo { get; set; }
            public string? MaterialDescripcion { get; set; }
            public string? EmbalajeCodigo { get; set; }
            public string? EmbalajeDescripcion { get; set; }
            public decimal? PiezasPorEmbalaje { get; set; }
            public decimal? CantidadEmbalajes { get; set; }
            public bool RequiereCambioMolde { get; set; }
        }
    }
}
