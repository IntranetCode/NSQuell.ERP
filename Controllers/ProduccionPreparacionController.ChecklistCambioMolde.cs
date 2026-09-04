using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers
{
    public sealed partial class ProduccionPreparacionController
    {
        [HttpGet]
        public async Task<IActionResult> ChecklistCambioMolde(int id)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            if (id <= 0)
            {
                TempData["Error"] = "No se recibió una tarea de cambio de molde válida.";
                return RedirectToAction(nameof(Index));
            }
            var usuarioId = ObtenerUsuarioID();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            var permisos = await ObtenerPermisosPreparacionUsuarioAsync(usuarioId, cn);
            if (!permisos.PuedeVerModulo) return StatusCode(StatusCodes.Status403Forbidden);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var tareas = await CargarGrupoCambioMoldeAsync(id, cn, tx);
                if (tareas.Count == 0)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La tarea de cambio de molde ya no existe o no está disponible.";
                    return RedirectToAction(nameof(Index));
                }
                var origen = tareas.FirstOrDefault(x => x.PreparacionAnticipadaID == id) ?? tareas[0];
                var esPareja = origen.GrupoLhRh.HasValue;
                if (esPareja && tareas.Select(x => x.ProgramaProduccionID).Distinct().Count() != 2)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La operación está marcada como LH/RH, pero no se encontraron correctamente las dos OF.";
                    return RedirectToAction(nameof(Index));
                }
                var errorGrupo = ValidarGrupoFisicoCambioMolde(tareas);
                if (!string.IsNullOrWhiteSpace(errorGrupo))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = errorGrupo;
                    return RedirectToAction(nameof(Index));
                }
                var checklist = await ObtenerOCrearChecklistCambioMoldeAsync(id, usuarioId, cn, tx);
                await tx.CommitAsync();
                ViewBag.PreparacionAnticipadaID = id;
                ViewBag.PuedeGestionarCambioMolde = permisos.PuedeGestionarCambioMolde;
                ViewBag.EstadoCambioMolde = origen.Estado;
                ViewBag.FechaInicioCambioMolde = origen.FechaInicioReal;
                ViewBag.LimiteCambioMoldeMinutos = ObtenerLimiteGrupoCambioMolde(tareas);
                ViewBag.EsParejaLhRh = esPareja;
                ViewBag.GrupoLhRh = origen.GrupoLhRh;
                return View("ChecklistCambioMolde", checklist);
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                TempData["Error"] = "No fue posible abrir el checklist de cambio de molde: " + ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarSeccionChecklistCambioMolde(ProduccionChecklistGuardarSeccionVm vm)
        {
            if (!UsuarioEnSesion()) return Unauthorized();
            if (vm.PreparacionAnticipadaID <= 0 || vm.OrdenSeccion <= 0) return BadRequest(new { ok = false, mensaje = "No se recibió correctamente la sección del checklist." });
            var usuarioId = ObtenerUsuarioID();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            var permisos = await ObtenerPermisosPreparacionUsuarioAsync(usuarioId, cn);
            if (!permisos.PuedeGestionarCambioMolde) return StatusCode(StatusCodes.Status403Forbidden);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var checklist = await ObtenerOCrearChecklistCambioMoldeAsync(vm.PreparacionAnticipadaID, usuarioId, cn, tx);
                if (vm.ChecklistArranqueID.HasValue && vm.ChecklistArranqueID.Value > 0 && vm.ChecklistArranqueID.Value != checklist.ChecklistArranqueID) throw new InvalidOperationException("El checklist recibido no corresponde al cambio de molde actual.");
                if (checklist.EstaCompleto) throw new InvalidOperationException("El checklist ya fue finalizado y no puede modificarse.");
                var seccion = checklist.Secciones.FirstOrDefault(x => x.OrdenSeccion == vm.OrdenSeccion);
                if (seccion == null) throw new InvalidOperationException("La sección indicada no pertenece al formato GQ-F-PR01-03 vigente.");
                var respuestas = vm.Respuestas ?? new List<ProduccionChecklistRespuestaCapturaVm>();
                if (respuestas.GroupBy(x => x.PreguntaID).Any(x => x.Count() > 1)) throw new InvalidOperationException("Se recibió una pregunta repetida dentro de la misma sección.");
                foreach (var respuesta in respuestas)
                {
                    var pregunta = seccion.Preguntas.FirstOrDefault(x => x.PreguntaID == respuesta.PreguntaID && x.Activo);
                    if (pregunta == null) throw new InvalidOperationException($"La pregunta {respuesta.PreguntaID} no pertenece a la sección seleccionada.");
                    var resultado = NormalizarResultadoChecklistCambioMolde(respuesta.Resultado);
                    var observaciones = string.IsNullOrWhiteSpace(respuesta.Observaciones) ? null : respuesta.Observaciones.Trim();
                    var valorCapturado = string.IsNullOrWhiteSpace(respuesta.ValorCapturado) ? null : respuesta.ValorCapturado.Trim();
                    if (observaciones?.Length > 1000) throw new InvalidOperationException($"Las observaciones de la pregunta {pregunta.OrdenPregunta} no pueden superar 1000 caracteres.");
                    if (valorCapturado?.Length > 200) throw new InvalidOperationException($"El valor capturado de la pregunta {pregunta.OrdenPregunta} no puede superar 200 caracteres.");
                    if (!string.IsNullOrWhiteSpace(resultado) && resultado != ProduccionChecklistResultado.Ok && resultado != ProduccionChecklistResultado.Nok && resultado != ProduccionChecklistResultado.Na) throw new InvalidOperationException($"La respuesta de la pregunta {pregunta.OrdenPregunta} no es válida.");
                    if (resultado == ProduccionChecklistResultado.Na && !pregunta.PermiteNA) throw new InvalidOperationException($"La pregunta {pregunta.OrdenPregunta} no permite N/A.");
                    if (resultado == ProduccionChecklistResultado.Nok && pregunta.RequiereObservacionSiNOK && string.IsNullOrWhiteSpace(observaciones)) throw new InvalidOperationException($"Debes registrar una observación en la pregunta {pregunta.OrdenPregunta} porque la respuesta es NO/NOK.");
                    if (resultado == ProduccionChecklistResultado.Na && pregunta.RequiereObservacionSiNA && string.IsNullOrWhiteSpace(observaciones)) throw new InvalidOperationException($"Debes registrar una observación en la pregunta {pregunta.OrdenPregunta} porque la respuesta es N/A.");
                    var tieneRespuesta = !string.IsNullOrWhiteSpace(resultado) || !string.IsNullOrWhiteSpace(valorCapturado);
                    if (tieneRespuesta && string.Equals(pregunta.TipoRespuesta, "ESTADO", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(resultado)) throw new InvalidOperationException($"Debes seleccionar SI, NO o N/A en la pregunta {pregunta.OrdenPregunta}.");
                    const string sql = @"
UPDATE dbo.Produccion_ChecklistArranqueDetalle
SET Resultado=@Resultado,
    Observaciones=@Observaciones,
    UsuarioRespuestaID=@UsuarioRespuestaID,
    FechaRespuesta=@FechaRespuesta,
    Confirmado=@Confirmado,
    ValorCapturado=@ValorCapturado,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE(),
    Activo=1
WHERE ChecklistArranqueID=@ChecklistArranqueID
  AND PreguntaID=@PreguntaID
  AND Activo=1;
IF @@ROWCOUNT=0
BEGIN
    INSERT INTO dbo.Produccion_ChecklistArranqueDetalle
    (
        ChecklistArranqueID,PreguntaID,Resultado,Observaciones,UsuarioRespuestaID,FechaRespuesta,
        UsuarioCreacionID,FechaCreacion,Activo,Confirmado,ValorCapturado
    )
    VALUES
    (
        @ChecklistArranqueID,@PreguntaID,@Resultado,@Observaciones,@UsuarioRespuestaID,@FechaRespuesta,
        @UsuarioID,GETDATE(),1,@Confirmado,@ValorCapturado
    );
END;";
                    await using var cmd = new SqlCommand(sql, cn, tx);
                    cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value = checklist.ChecklistArranqueID;
                    cmd.Parameters.Add("@PreguntaID", SqlDbType.Int).Value = pregunta.PreguntaID;
                    cmd.Parameters.Add("@Resultado", SqlDbType.NVarChar, 20).Value = string.IsNullOrWhiteSpace(resultado) ? DBNull.Value : resultado;
                    cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = string.IsNullOrWhiteSpace(observaciones) ? DBNull.Value : observaciones;
                    cmd.Parameters.Add("@UsuarioRespuestaID", SqlDbType.Int).Value = tieneRespuesta ? usuarioId : DBNull.Value;
                    cmd.Parameters.Add("@FechaRespuesta", SqlDbType.DateTime).Value = tieneRespuesta ? DateTime.Now : DBNull.Value;
                    cmd.Parameters.Add("@Confirmado", SqlDbType.Bit).Value = tieneRespuesta;
                    cmd.Parameters.Add("@ValorCapturado", SqlDbType.NVarChar, 200).Value = string.IsNullOrWhiteSpace(valorCapturado) ? DBNull.Value : valorCapturado;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    await cmd.ExecuteNonQueryAsync();
                }
                const string sqlEstado = @"
UPDATE c
SET EstadoFlujo=CASE
        WHEN EXISTS
        (
            SELECT 1
            FROM dbo.Produccion_ChecklistArranqueDetalle d
            WHERE d.ChecklistArranqueID=c.ChecklistArranqueID
              AND d.Activo=1
              AND d.Confirmado=1
        ) THEN @EnProceso
        ELSE @Pendiente
    END,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
FROM dbo.Produccion_ChecklistArranque c
WHERE c.ChecklistArranqueID=@ChecklistArranqueID
  AND c.Activo=1
  AND ISNULL(c.EstadoFlujo,N'')<>@Completo;";
                await using (var cmd = new SqlCommand(sqlEstado, cn, tx))
                {
                    cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value = checklist.ChecklistArranqueID;
                    cmd.Parameters.Add("@Pendiente", SqlDbType.NVarChar, 60).Value = ProduccionChecklistEstadoFlujo.Pendiente;
                    cmd.Parameters.Add("@EnProceso", SqlDbType.NVarChar, 60).Value = ProduccionChecklistEstadoFlujo.EnProceso;
                    cmd.Parameters.Add("@Completo", SqlDbType.NVarChar, 60).Value = ProduccionChecklistEstadoFlujo.Completo;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    await cmd.ExecuteNonQueryAsync();
                }
                await tx.CommitAsync();
                var actualizado = await ObtenerChecklistCambioMoldePorPreparacionAsync(vm.PreparacionAnticipadaID, cn);
                return Json(new { ok = true, mensaje = $"Sección {vm.OrdenSeccion} guardada correctamente.", checklist = actualizado });
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                return BadRequest(new { ok = false, mensaje = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FinalizarChecklistCambioMolde(ProduccionChecklistFinalizarVm vm)
        {
            if (!UsuarioEnSesion()) return Unauthorized();
            if (vm.PreparacionAnticipadaID <= 0 || vm.ChecklistArranqueID <= 0) return BadRequest(new { ok = false, mensaje = "No se recibió correctamente el checklist." });
            vm.ObservacionesGenerales = string.IsNullOrWhiteSpace(vm.ObservacionesGenerales) ? null : vm.ObservacionesGenerales.Trim();
            if (vm.ObservacionesGenerales?.Length > 2000) return BadRequest(new { ok = false, mensaje = "Las observaciones generales no pueden superar 2000 caracteres." });
            var usuarioId = ObtenerUsuarioID();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            var permisos = await ObtenerPermisosPreparacionUsuarioAsync(usuarioId, cn);
            if (!permisos.PuedeGestionarCambioMolde) return StatusCode(StatusCodes.Status403Forbidden);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var checklist = await ObtenerChecklistCambioMoldePorPreparacionAsync(vm.PreparacionAnticipadaID, cn, tx);
                if (checklist == null) throw new InvalidOperationException("No existe el checklist correspondiente a este cambio de molde.");
                if (checklist.ChecklistArranqueID != vm.ChecklistArranqueID) throw new InvalidOperationException("El checklist recibido no corresponde a este cambio de molde.");
                if (checklist.EstaCompleto)
                {
                    await tx.RollbackAsync();
                    return Json(new { ok = true, mensaje = "El checklist ya se encuentra completo.", checklist });
                }
                if (checklist.TotalPreguntas <= 0) throw new InvalidOperationException("El formato GQ-F-PR01-03 no tiene preguntas activas configuradas.");
                if (!checklist.PuedeFinalizar)
                {
                    var pendientes = checklist.PreguntasObligatoriasPendientes;
                    throw new InvalidOperationException($"El checklist todavía no puede finalizarse. Avance: {checklist.TextoAvance}. Preguntas obligatorias pendientes: {pendientes}.");
                }
                const string sql = @"
UPDATE dbo.Produccion_ChecklistArranque
SET EstadoFlujo=@Completo,
    EstatusID=@EstatusCapturado,
    UsuarioProduccionID=@UsuarioID,
    FechaCapturaProduccion=GETDATE(),
    FechaHoraCapturaReal=SYSDATETIME(),
    ObservacionesGenerales=CASE WHEN @Observaciones IS NULL THEN ObservacionesGenerales ELSE @Observaciones END,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE ChecklistArranqueID=@ChecklistArranqueID
  AND Activo=1
  AND CodigoFormato=@CodigoFormato
  AND VersionFormato=@VersionFormato
  AND ISNULL(EstadoFlujo,N'')<>@Completo;
IF @@ROWCOUNT<>1
    THROW 51830,'El checklist cambió de estado mientras intentabas finalizarlo.',1;";
                await using (var cmd = new SqlCommand(sql, cn, tx))
                {
                    cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value = checklist.ChecklistArranqueID;
                    cmd.Parameters.Add("@Completo", SqlDbType.NVarChar, 60).Value = ProduccionChecklistEstadoFlujo.Completo;
                    cmd.Parameters.Add("@EstatusCapturado", SqlDbType.Int).Value = ProduccionChecklistEstatus.CapturadoPorProduccion;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 2000).Value = string.IsNullOrWhiteSpace(vm.ObservacionesGenerales) ? DBNull.Value : vm.ObservacionesGenerales;
                    cmd.Parameters.Add("@CodigoFormato", SqlDbType.NVarChar, 100).Value = ProduccionChecklistFormato.CambioMoldeCodigo;
                    cmd.Parameters.Add("@VersionFormato", SqlDbType.NVarChar, 60).Value = ProduccionChecklistFormato.CambioMoldeVersion;
                    await cmd.ExecuteNonQueryAsync();
                }
                await tx.CommitAsync();
                var actualizado = await ObtenerChecklistCambioMoldePorPreparacionAsync(vm.PreparacionAnticipadaID, cn);
                return Json(new { ok = true, mensaje = "Checklist GQ-F-PR01-03 completado correctamente.", checklist = actualizado });
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                return BadRequest(new { ok = false, mensaje = ex.Message });
            }
        }

        private async Task EnriquecerChecklistCambioMoldeAsync(List<ProduccionPreparacionTareaVm> tareas, SqlConnection cn)
        {
            if (tareas == null || tareas.Count == 0) return;
            foreach (var tarea in tareas.Where(x => x.EsCambioMolde && x.PreparacionAnticipadaID > 0))
            {
                tarea.ChecklistCambioMolde = await ObtenerChecklistCambioMoldePorPreparacionAsync(tarea.PreparacionAnticipadaID, cn);
            }
        }

        private async Task<ProduccionChecklistVm> ObtenerOCrearChecklistCambioMoldeAsync(int preparacionAnticipadaId, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            if (preparacionAnticipadaId <= 0) throw new InvalidOperationException("La tarea de cambio de molde no es válida.");
            await ValidarCatalogoChecklistCambioMoldeAsync(cn, tx);
            var tareas = await CargarGrupoCambioMoldeAsync(preparacionAnticipadaId, cn, tx);
            if (tareas.Count == 0) throw new InvalidOperationException("No se encontró la tarea de cambio de molde.");
            var errorGrupo = ValidarGrupoFisicoCambioMolde(tareas);
            if (!string.IsNullOrWhiteSpace(errorGrupo)) throw new InvalidOperationException(errorGrupo);
            var contextos = new List<ChecklistProgramaCambioMoldeInterno>();
            foreach (var tarea in tareas)
            {
                var contexto = await CargarContextoProgramaChecklistCambioMoldeAsync(tarea.PreparacionAnticipadaID, cn, tx);
                if (contexto == null) throw new InvalidOperationException($"No fue posible obtener el contexto del Programa {tarea.ProgramaProduccionID}.");
                contextos.Add(contexto);
            }
            AjustarLadosChecklistCambioMolde(contextos);
            var tablaVinculo = await ObtenerTablaVinculoChecklistProgramasAsync(cn, tx);
            var tarea1 = tareas[0].PreparacionAnticipadaID;
            var tarea2 = tareas.Count > 1 ? tareas[1].PreparacionAnticipadaID : (int?)null;
            var sqlExistente = $@"
SELECT TOP(1) c.ChecklistArranqueID
FROM dbo.Produccion_ChecklistArranque c WITH(UPDLOCK,HOLDLOCK)
INNER JOIN {tablaVinculo} cp WITH(UPDLOCK,HOLDLOCK)
    ON cp.ChecklistArranqueID=c.ChecklistArranqueID
   AND cp.Activo=1
WHERE c.Activo=1
  AND c.CodigoFormato=@CodigoFormato
  AND c.VersionFormato=@VersionFormato
  AND c.TipoChecklist=@TipoChecklist
  AND
  (
      cp.PreparacionAnticipadaID=@Tarea1
      OR (@Tarea2 IS NOT NULL AND cp.PreparacionAnticipadaID=@Tarea2)
  )
ORDER BY c.ChecklistArranqueID DESC;";
            int? checklistId = null;
            await using (var cmd = new SqlCommand(sqlExistente, cn, tx))
            {
                cmd.Parameters.Add("@CodigoFormato", SqlDbType.NVarChar, 100).Value = ProduccionChecklistFormato.CambioMoldeCodigo;
                cmd.Parameters.Add("@VersionFormato", SqlDbType.NVarChar, 60).Value = ProduccionChecklistFormato.CambioMoldeVersion;
                cmd.Parameters.Add("@TipoChecklist", SqlDbType.NVarChar, 100).Value = ProduccionChecklistTipo.CambioMolde;
                cmd.Parameters.Add("@Tarea1", SqlDbType.Int).Value = tarea1;
                cmd.Parameters.Add("@Tarea2", SqlDbType.Int).Value = tarea2.HasValue ? tarea2.Value : DBNull.Value;
                var valor = await cmd.ExecuteScalarAsync();
                if (valor != null && valor != DBNull.Value) checklistId = Convert.ToInt32(valor);
            }
            var principal = contextos.FirstOrDefault(x => x.PreparacionAnticipadaID == preparacionAnticipadaId) ?? contextos.OrderBy(x => x.ProgramaProduccionID).First();
            if (!checklistId.HasValue)
            {
                var fechaChecklist = DateTime.Now;
                var fechaOperacion = principal.FechaInicioProgramada?.Date;
                var fechaHoraProgramada = principal.FechaInicioProgramada.HasValue && principal.Cambio.HasValue ? ConstruirFechaPreparacion(principal.FechaInicioProgramada.Value, principal.Cambio) : (DateTime?)null;
                const string sqlInsertar = @"
INSERT INTO dbo.Produccion_ChecklistArranque
(
    EjecucionProduccionID,ProgramaProduccionID,SolicitudProduccionID,SolicitudProduccionDetalleID,ReleaseID,ReleaseDetalleID,
    FechaChecklist,MaquinaID,MaquinaCodigo,MaquinaNombre,MoldeID,MoldeCodigo,ParteID,NumeroParte,ReferenciaSAP,DescripcionParte,
    CodigoFormato,VersionFormato,EstatusID,UsuarioProduccionID,FechaCapturaProduccion,UsuarioCalidadID,FechaValidacionCalidad,
    ObservacionesGenerales,ObservacionesCalidad,UsuarioCreacionID,FechaCreacion,Activo,EstadoFlujo,EsReliberacion,
    TipoChecklist,MomentoProceso,FechaOperacion,NumeroAplicacion,EsRecurrente,RequiereCambioMolde,HoraProgramada,FechaHoraProgramada
)
OUTPUT INSERTED.ChecklistArranqueID
VALUES
(
    @EjecucionProduccionID,@ProgramaProduccionID,@SolicitudProduccionID,@SolicitudProduccionDetalleID,@ReleaseID,@ReleaseDetalleID,
    @FechaChecklist,@MaquinaID,@MaquinaCodigo,@MaquinaNombre,@MoldeID,@MoldeCodigo,@ParteID,@NumeroParte,@ReferenciaSAP,@DescripcionParte,
    @CodigoFormato,@VersionFormato,@EstatusID,NULL,NULL,NULL,NULL,
    NULL,NULL,@UsuarioID,@FechaChecklist,1,@EstadoFlujo,0,
    @TipoChecklist,@MomentoProceso,@FechaOperacion,1,0,1,@HoraProgramada,@FechaHoraProgramada
);";
                await using var cmd = new SqlCommand(sqlInsertar, cn, tx);
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = principal.EjecucionProduccionID.HasValue ? principal.EjecucionProduccionID.Value : DBNull.Value;
                cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = principal.ProgramaProduccionID;
                cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = principal.SolicitudProduccionID.HasValue ? principal.SolicitudProduccionID.Value : DBNull.Value;
                cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = principal.SolicitudProduccionDetalleID.HasValue ? principal.SolicitudProduccionDetalleID.Value : DBNull.Value;
                cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = principal.ReleaseID.HasValue ? principal.ReleaseID.Value : DBNull.Value;
                cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = principal.ReleaseDetalleID.HasValue ? principal.ReleaseDetalleID.Value : DBNull.Value;
                cmd.Parameters.Add("@FechaChecklist", SqlDbType.DateTime).Value = fechaChecklist;
                cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = principal.MaquinaID.HasValue ? principal.MaquinaID.Value : DBNull.Value;
                cmd.Parameters.Add("@MaquinaCodigo", SqlDbType.NVarChar, 200).Value = string.IsNullOrWhiteSpace(principal.MaquinaCodigo) ? DBNull.Value : principal.MaquinaCodigo;
                cmd.Parameters.Add("@MaquinaNombre", SqlDbType.NVarChar, 400).Value = string.IsNullOrWhiteSpace(principal.MaquinaNombre) ? DBNull.Value : principal.MaquinaNombre;
                cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value = principal.MoldeID.HasValue ? principal.MoldeID.Value : DBNull.Value;
                cmd.Parameters.Add("@MoldeCodigo", SqlDbType.NVarChar, 200).Value = string.IsNullOrWhiteSpace(principal.MoldeCodigo) ? DBNull.Value : principal.MoldeCodigo;
                cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = principal.ParteID.HasValue ? principal.ParteID.Value : DBNull.Value;
                cmd.Parameters.Add("@NumeroParte", SqlDbType.NVarChar, 240).Value = string.IsNullOrWhiteSpace(principal.NumeroParte) ? DBNull.Value : principal.NumeroParte;
                cmd.Parameters.Add("@ReferenciaSAP", SqlDbType.NVarChar, 300).Value = string.IsNullOrWhiteSpace(principal.ReferenciaSAP) ? DBNull.Value : principal.ReferenciaSAP;
                cmd.Parameters.Add("@DescripcionParte", SqlDbType.NVarChar, 600).Value = string.IsNullOrWhiteSpace(principal.DescripcionParte) ? DBNull.Value : principal.DescripcionParte;
                cmd.Parameters.Add("@CodigoFormato", SqlDbType.NVarChar, 100).Value = ProduccionChecklistFormato.CambioMoldeCodigo;
                cmd.Parameters.Add("@VersionFormato", SqlDbType.NVarChar, 60).Value = ProduccionChecklistFormato.CambioMoldeVersion;
                cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value = ProduccionChecklistEstatus.PendienteProduccion;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                cmd.Parameters.Add("@EstadoFlujo", SqlDbType.NVarChar, 60).Value = ProduccionChecklistEstadoFlujo.Pendiente;
                cmd.Parameters.Add("@TipoChecklist", SqlDbType.NVarChar, 100).Value = ProduccionChecklistTipo.CambioMolde;
                cmd.Parameters.Add("@MomentoProceso", SqlDbType.NVarChar, 80).Value = ProduccionChecklistMomento.CambioMolde;
                cmd.Parameters.Add("@FechaOperacion", SqlDbType.Date).Value = fechaOperacion.HasValue ? fechaOperacion.Value : DBNull.Value;
                cmd.Parameters.Add("@HoraProgramada", SqlDbType.Time).Value = principal.Cambio.HasValue ? principal.Cambio.Value : DBNull.Value;
                cmd.Parameters.Add("@FechaHoraProgramada", SqlDbType.DateTime2).Value = fechaHoraProgramada.HasValue ? fechaHoraProgramada.Value : DBNull.Value;
                checklistId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            }
            await AsegurarVinculosChecklistCambioMoldeAsync(checklistId.Value, principal.PreparacionAnticipadaID, contextos, tablaVinculo, cn, tx);
            return await CargarChecklistCambioMoldePorIdAsync(checklistId.Value, cn, tx) ?? throw new InvalidOperationException("No fue posible cargar el checklist después de crearlo.");
        }

        private async Task<ProduccionChecklistVm?> ObtenerChecklistCambioMoldePorPreparacionAsync(int preparacionAnticipadaId, SqlConnection cn, SqlTransaction? tx = null)
        {
            if (preparacionAnticipadaId <= 0) return null;
            var tablaVinculo = await ObtenerTablaVinculoChecklistProgramasAsync(cn, tx);
            var sql = $@"
SELECT TOP(1) c.ChecklistArranqueID
FROM dbo.Produccion_ChecklistArranque c
INNER JOIN {tablaVinculo} cp
    ON cp.ChecklistArranqueID=c.ChecklistArranqueID
   AND cp.Activo=1
WHERE cp.PreparacionAnticipadaID=@PreparacionAnticipadaID
  AND c.Activo=1
  AND c.CodigoFormato=@CodigoFormato
  AND c.VersionFormato=@VersionFormato
  AND c.TipoChecklist=@TipoChecklist
ORDER BY c.ChecklistArranqueID DESC;";
            int? checklistId = null;
            await using (var cmd = CrearComandoChecklist(sql, cn, tx))
            {
                cmd.Parameters.Add("@PreparacionAnticipadaID", SqlDbType.Int).Value = preparacionAnticipadaId;
                cmd.Parameters.Add("@CodigoFormato", SqlDbType.NVarChar, 100).Value = ProduccionChecklistFormato.CambioMoldeCodigo;
                cmd.Parameters.Add("@VersionFormato", SqlDbType.NVarChar, 60).Value = ProduccionChecklistFormato.CambioMoldeVersion;
                cmd.Parameters.Add("@TipoChecklist", SqlDbType.NVarChar, 100).Value = ProduccionChecklistTipo.CambioMolde;
                var valor = await cmd.ExecuteScalarAsync();
                if (valor != null && valor != DBNull.Value) checklistId = Convert.ToInt32(valor);
            }
            return checklistId.HasValue ? await CargarChecklistCambioMoldePorIdAsync(checklistId.Value, cn, tx) : null;
        }

        private async Task<ProduccionChecklistVm?> CargarChecklistCambioMoldePorIdAsync(int checklistArranqueId, SqlConnection cn, SqlTransaction? tx = null)
        {
            const string sqlEncabezado = @"
SELECT TOP(1)
    ChecklistArranqueID,EjecucionProduccionID,ProgramaProduccionID,SolicitudProduccionID,SolicitudProduccionDetalleID,
    ReleaseID,ReleaseDetalleID,FechaChecklist,MaquinaID,MaquinaCodigo,MaquinaNombre,MoldeID,MoldeCodigo,ParteID,
    NumeroParte,ReferenciaSAP,DescripcionParte,CodigoFormato,VersionFormato,EstatusID,EstadoFlujo,TipoChecklist,
    MomentoProceso,ObservacionesGenerales,UsuarioCreacionID,FechaCreacion,UsuarioModificacionID,FechaModificacion,Activo
FROM dbo.Produccion_ChecklistArranque
WHERE ChecklistArranqueID=@ChecklistArranqueID
  AND Activo=1;";
            ProduccionChecklistVm? vm = null;
            await using (var cmd = CrearComandoChecklist(sqlEncabezado, cn, tx))
            {
                cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value = checklistArranqueId;
                await using var rd = await cmd.ExecuteReaderAsync();
                if (!await rd.ReadAsync()) return null;
                vm = new ProduccionChecklistVm
                {
                    ChecklistArranqueID = Convert.ToInt32(rd["ChecklistArranqueID"]),
                    EjecucionProduccionID = ChecklistNullableInt(rd, "EjecucionProduccionID"),
                    ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                    SolicitudProduccionID = ChecklistNullableInt(rd, "SolicitudProduccionID"),
                    SolicitudProduccionDetalleID = ChecklistNullableInt(rd, "SolicitudProduccionDetalleID"),
                    ReleaseID = ChecklistNullableInt(rd, "ReleaseID"),
                    ReleaseDetalleID = ChecklistNullableInt(rd, "ReleaseDetalleID"),
                    FechaChecklist = Convert.ToDateTime(rd["FechaChecklist"]),
                    MaquinaID = ChecklistNullableInt(rd, "MaquinaID"),
                    MaquinaCodigo = ChecklistTexto(rd, "MaquinaCodigo"),
                    MaquinaNombre = ChecklistTexto(rd, "MaquinaNombre"),
                    MoldeID = ChecklistNullableInt(rd, "MoldeID"),
                    MoldeCodigo = ChecklistTexto(rd, "MoldeCodigo"),
                    ParteID = ChecklistNullableInt(rd, "ParteID"),
                    NumeroParte = ChecklistTexto(rd, "NumeroParte"),
                    ReferenciaSAP = ChecklistTexto(rd, "ReferenciaSAP"),
                    DescripcionParte = ChecklistTexto(rd, "DescripcionParte"),
                    CodigoFormato = ChecklistTexto(rd, "CodigoFormato") ?? string.Empty,
                    VersionFormato = ChecklistTexto(rd, "VersionFormato"),
                    EstatusID = Convert.ToInt32(rd["EstatusID"]),
                    EstadoFlujo = ChecklistTexto(rd, "EstadoFlujo") ?? ProduccionChecklistEstadoFlujo.Pendiente,
                    TipoChecklist = ChecklistTexto(rd, "TipoChecklist"),
                    MomentoProceso = ChecklistTexto(rd, "MomentoProceso"),
                    ObservacionesGenerales = ChecklistTexto(rd, "ObservacionesGenerales"),
                    UsuarioCreacionID = ChecklistNullableInt(rd, "UsuarioCreacionID"),
                    FechaCreacion = ChecklistNullableDateTime(rd, "FechaCreacion"),
                    UsuarioModificacionID = ChecklistNullableInt(rd, "UsuarioModificacionID"),
                    FechaModificacion = ChecklistNullableDateTime(rd, "FechaModificacion"),
                    Activo = rd["Activo"] != DBNull.Value && Convert.ToBoolean(rd["Activo"])
                };
            }
            var tablaVinculo = await ObtenerTablaVinculoChecklistProgramasAsync(cn, tx);
            var sqlProgramas = $@"
SELECT
    cp.ChecklistProgramaID,cp.ChecklistArranqueID,cp.ProgramaProduccionID,cp.PreparacionAnticipadaID,
    cp.EjecucionProduccionID,cp.LadoLhRh,ISNULL(cp.EsPrincipal,0) AS EsPrincipal,
    COALESCE(NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)),N''),NULLIF(LTRIM(RTRIM(s.FolioSolicitud)),N''),N'') AS NumeroOF,
    pp.ParteID,pp.NumeroParte,pp.ReferenciaSAP,pp.DesignacionDescripcionSAP AS DescripcionParte,
    grupo.GrupoLhRh
FROM {tablaVinculo} cp
LEFT JOIN dbo.Planeacion_ProgramaProduccion pp ON pp.ProgramaProduccionID=cp.ProgramaProduccionID
LEFT JOIN dbo.SolicitudesProduccion s ON s.SolicitudProduccionID=pp.SolicitudProduccionID
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
WHERE cp.ChecklistArranqueID=@ChecklistArranqueID
  AND cp.Activo=1
ORDER BY ISNULL(cp.EsPrincipal,0) DESC,cp.ProgramaProduccionID;";
            await using (var cmd = CrearComandoChecklist(sqlProgramas, cn, tx))
            {
                cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value = checklistArranqueId;
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    var grupo = ChecklistNullableInt(rd, "GrupoLhRh");
                    if (grupo.HasValue) vm.GrupoLhRh = grupo;
                    vm.Programas.Add(new ProduccionChecklistProgramaVm
                    {
                        ChecklistProgramaID = Convert.ToInt64(rd["ChecklistProgramaID"]),
                        ChecklistArranqueID = Convert.ToInt32(rd["ChecklistArranqueID"]),
                        ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                        PreparacionAnticipadaID = ChecklistNullableInt(rd, "PreparacionAnticipadaID"),
                        EjecucionProduccionID = ChecklistNullableInt(rd, "EjecucionProduccionID"),
                        LadoLhRh = ChecklistTexto(rd, "LadoLhRh"),
                        EsPrincipal = rd["EsPrincipal"] != DBNull.Value && Convert.ToBoolean(rd["EsPrincipal"]),
                        NumeroOF = ChecklistTexto(rd, "NumeroOF"),
                        ParteID = ChecklistNullableInt(rd, "ParteID"),
                        NumeroParte = ChecklistTexto(rd, "NumeroParte"),
                        ReferenciaSAP = ChecklistTexto(rd, "ReferenciaSAP"),
                        DescripcionParte = ChecklistTexto(rd, "DescripcionParte"),
                        Activo = true
                    });
                }
            }
            vm.EsOperacionLhRh = vm.Programas.Count > 1 && vm.GrupoLhRh.HasValue;
            const string sqlPreguntas = @"
SELECT
    p.PreguntaID,p.CodigoFormato,p.VersionFormato,p.Seccion,p.OrdenSeccion,p.OrdenPregunta,p.TextoPregunta,
    p.ResponsableSugerido,p.RequiereObservacionSiNOK,p.RequiereObservacionSiNA,p.TipoChecklist,p.MomentoProceso,
    p.TipoRespuesta,p.EstadoPredeterminado,p.EsPreguntaCalidad,p.GrupoResponsable,p.EsRecurrente,p.Activo,
    CONVERT(bit,1) AS EsObligatoria,
    CONVERT(bit,1) AS PermiteNA,
    detalle.ChecklistArranqueDetalleID,detalle.Resultado,detalle.Observaciones,detalle.UsuarioRespuestaID,
    detalle.FechaRespuesta,detalle.Confirmado,detalle.ValorCapturado,detalle.Unidad,detalle.Especificacion,detalle.Tolerancia,
    LTRIM(RTRIM(ISNULL(per.Nombre,N'')+N' '+ISNULL(per.ApellidoPaterno,N'')+N' '+ISNULL(per.ApellidoMaterno,N''))) AS UsuarioRespuestaNombre
FROM dbo.ERP_ChecklistArranquePreguntas p
OUTER APPLY
(
    SELECT TOP(1)
        d.ChecklistArranqueDetalleID,d.Resultado,d.Observaciones,d.UsuarioRespuestaID,d.FechaRespuesta,
        d.Confirmado,d.ValorCapturado,d.Unidad,d.Especificacion,d.Tolerancia
    FROM dbo.Produccion_ChecklistArranqueDetalle d
    WHERE d.ChecklistArranqueID=@ChecklistArranqueID
      AND d.PreguntaID=p.PreguntaID
      AND d.Activo=1
    ORDER BY d.ChecklistArranqueDetalleID DESC
) detalle
LEFT JOIN dbo.Usuarios usr ON usr.UsuarioID=detalle.UsuarioRespuestaID
LEFT JOIN dbo.Persona per ON per.PersonaID=usr.PersonaID
WHERE p.Activo=1
  AND p.CodigoFormato=@CodigoFormato
  AND p.VersionFormato=@VersionFormato
ORDER BY p.OrdenSeccion,p.OrdenPregunta,p.PreguntaID;";
            await using (var cmd = CrearComandoChecklist(sqlPreguntas, cn, tx))
            {
                cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value = checklistArranqueId;
                cmd.Parameters.Add("@CodigoFormato", SqlDbType.NVarChar, 100).Value = vm.CodigoFormato;
                cmd.Parameters.Add("@VersionFormato", SqlDbType.NVarChar, 60).Value = string.IsNullOrWhiteSpace(vm.VersionFormato) ? DBNull.Value : vm.VersionFormato;
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    var pregunta = new ProduccionChecklistPreguntaVm
                    {
                        PreguntaID = Convert.ToInt32(rd["PreguntaID"]),
                        CodigoFormato = ChecklistTexto(rd, "CodigoFormato") ?? string.Empty,
                        VersionFormato = ChecklistTexto(rd, "VersionFormato"),
                        Seccion = ChecklistTexto(rd, "Seccion") ?? string.Empty,
                        OrdenSeccion = Convert.ToInt32(rd["OrdenSeccion"]),
                        OrdenPregunta = Convert.ToInt32(rd["OrdenPregunta"]),
                        TextoPregunta = ChecklistTexto(rd, "TextoPregunta") ?? string.Empty,
                        ResponsableSugerido = ChecklistTexto(rd, "ResponsableSugerido"),
                        RequiereObservacionSiNOK = rd["RequiereObservacionSiNOK"] != DBNull.Value && Convert.ToBoolean(rd["RequiereObservacionSiNOK"]),
                        RequiereObservacionSiNA = rd["RequiereObservacionSiNA"] != DBNull.Value && Convert.ToBoolean(rd["RequiereObservacionSiNA"]),
                        TipoChecklist = ChecklistTexto(rd, "TipoChecklist"),
                        MomentoProceso = ChecklistTexto(rd, "MomentoProceso"),
                        TipoRespuesta = ChecklistTexto(rd, "TipoRespuesta"),
                        EstadoPredeterminado = ChecklistTexto(rd, "EstadoPredeterminado"),
                        EsPreguntaCalidad = rd["EsPreguntaCalidad"] != DBNull.Value && Convert.ToBoolean(rd["EsPreguntaCalidad"]),
                        GrupoResponsable = ChecklistTexto(rd, "GrupoResponsable"),
                        EsRecurrente = rd["EsRecurrente"] != DBNull.Value && Convert.ToBoolean(rd["EsRecurrente"]),
                        EsObligatoria = true,
                        PermiteNA = true,
                        Activo = true,
                        ChecklistArranqueDetalleID = ChecklistNullableInt(rd, "ChecklistArranqueDetalleID"),
                        Resultado = ChecklistTexto(rd, "Resultado"),
                        Observaciones = ChecklistTexto(rd, "Observaciones"),
                        UsuarioRespuestaID = ChecklistNullableInt(rd, "UsuarioRespuestaID"),
                        UsuarioRespuestaNombre = ChecklistTexto(rd, "UsuarioRespuestaNombre"),
                        FechaRespuesta = ChecklistNullableDateTime(rd, "FechaRespuesta"),
                        Confirmado = rd["Confirmado"] != DBNull.Value && Convert.ToBoolean(rd["Confirmado"]),
                        ValorCapturado = ChecklistTexto(rd, "ValorCapturado"),
                        Unidad = ChecklistTexto(rd, "Unidad"),
                        Especificacion = ChecklistTexto(rd, "Especificacion"),
                        Tolerancia = ChecklistTexto(rd, "Tolerancia")
                    };
                    var seccion = vm.Secciones.FirstOrDefault(x => x.OrdenSeccion == pregunta.OrdenSeccion && string.Equals(x.Seccion, pregunta.Seccion, StringComparison.OrdinalIgnoreCase));
                    if (seccion == null)
                    {
                        seccion = new ProduccionChecklistSeccionVm { Seccion = pregunta.Seccion, OrdenSeccion = pregunta.OrdenSeccion };
                        vm.Secciones.Add(seccion);
                    }
                    seccion.Preguntas.Add(pregunta);
                }
            }
            vm.Secciones = vm.Secciones.OrderBy(x => x.OrdenSeccion).ToList();
            foreach (var seccion in vm.Secciones) seccion.Preguntas = seccion.Preguntas.OrderBy(x => x.OrdenPregunta).ThenBy(x => x.PreguntaID).ToList();
            return vm;
        }

        private async Task<ChecklistProgramaCambioMoldeInterno?> CargarContextoProgramaChecklistCambioMoldeAsync(int preparacionAnticipadaId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP(1)
    pa.PreparacionAnticipadaID,
    pp.ProgramaProduccionID,
    ejecucion.EjecucionProduccionID,
    pp.SolicitudProduccionID,
    pp.SolicitudProduccionDetalleID,
    ejecucion.ReleaseID,
    ejecucion.ReleaseDetalleID,
    COALESCE(NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)),N''),NULLIF(LTRIM(RTRIM(s.FolioSolicitud)),N''),N'') AS NumeroOF,
    pp.MaquinaID,
    COALESCE(NULLIF(LTRIM(RTRIM(pp.MaquinaCodigo)),N''),maq.Codigo) AS MaquinaCodigo,
    COALESCE(NULLIF(LTRIM(RTRIM(pp.MaquinaNombre)),N''),maq.Nombre) AS MaquinaNombre,
    pp.MoldeID,pp.MoldeCodigo,pp.ParteID,pp.NumeroParte,pp.ReferenciaSAP,
    pp.DesignacionDescripcionSAP AS DescripcionParte,
    pp.FechaInicioProgramada,
    pp.Cambio
FROM dbo.Produccion_PreparacionAnticipada pa
INNER JOIN dbo.Planeacion_ProgramaProduccion pp ON pp.ProgramaProduccionID=pa.ProgramaProduccionID
LEFT JOIN dbo.SolicitudesProduccion s ON s.SolicitudProduccionID=pp.SolicitudProduccionID
LEFT JOIN dbo.ERP_Maquinas maq ON maq.MaquinaID=pp.MaquinaID
OUTER APPLY
(
    SELECT TOP(1) e.EjecucionProduccionID,e.ReleaseID,e.ReleaseDetalleID
    FROM dbo.Produccion_Ejecucion e
    WHERE e.ProgramaProduccionID=pp.ProgramaProduccionID
      AND e.Activo=1
    ORDER BY e.EjecucionProduccionID DESC
) ejecucion
WHERE pa.PreparacionAnticipadaID=@PreparacionAnticipadaID
  AND pa.TipoTarea=@TipoTarea
  AND pa.Activo=1
  AND pp.Activo=1;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@PreparacionAnticipadaID", SqlDbType.Int).Value = preparacionAnticipadaId;
            cmd.Parameters.Add("@TipoTarea", SqlDbType.NVarChar, 40).Value = ProduccionPreparacionTipo.CambioMolde;
            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) return null;
            var contexto = new ChecklistProgramaCambioMoldeInterno
            {
                PreparacionAnticipadaID = Convert.ToInt32(rd["PreparacionAnticipadaID"]),
                ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                EjecucionProduccionID = ChecklistNullableInt(rd, "EjecucionProduccionID"),
                SolicitudProduccionID = ChecklistNullableInt(rd, "SolicitudProduccionID"),
                SolicitudProduccionDetalleID = ChecklistNullableInt(rd, "SolicitudProduccionDetalleID"),
                ReleaseID = ChecklistNullableInt(rd, "ReleaseID"),
                ReleaseDetalleID = ChecklistNullableInt(rd, "ReleaseDetalleID"),
                NumeroOF = ChecklistTexto(rd, "NumeroOF"),
                MaquinaID = ChecklistNullableInt(rd, "MaquinaID"),
                MaquinaCodigo = ChecklistTexto(rd, "MaquinaCodigo"),
                MaquinaNombre = ChecklistTexto(rd, "MaquinaNombre"),
                MoldeID = ChecklistNullableInt(rd, "MoldeID"),
                MoldeCodigo = ChecklistTexto(rd, "MoldeCodigo"),
                ParteID = ChecklistNullableInt(rd, "ParteID"),
                NumeroParte = ChecklistTexto(rd, "NumeroParte"),
                ReferenciaSAP = ChecklistTexto(rd, "ReferenciaSAP"),
                DescripcionParte = ChecklistTexto(rd, "DescripcionParte"),
                FechaInicioProgramada = ChecklistNullableDateTime(rd, "FechaInicioProgramada"),
                Cambio = rd["Cambio"] == DBNull.Value ? null : (TimeSpan?)rd["Cambio"]
            };
            contexto.LadoLhRh = DeterminarLadoLhRhPreparacion(contexto.ReferenciaSAP, contexto.NumeroParte, contexto.DescripcionParte);
            return contexto;
        }

        private static void AjustarLadosChecklistCambioMolde(List<ChecklistProgramaCambioMoldeInterno> contextos)
        {
            if (contextos.Count != 2) return;
            var primero = contextos[0];
            var segundo = contextos[1];
            if (string.IsNullOrWhiteSpace(primero.LadoLhRh) && string.Equals(segundo.LadoLhRh, "LH", StringComparison.OrdinalIgnoreCase)) primero.LadoLhRh = "RH";
            else if (string.IsNullOrWhiteSpace(primero.LadoLhRh) && string.Equals(segundo.LadoLhRh, "RH", StringComparison.OrdinalIgnoreCase)) primero.LadoLhRh = "LH";
            if (string.IsNullOrWhiteSpace(segundo.LadoLhRh) && string.Equals(primero.LadoLhRh, "LH", StringComparison.OrdinalIgnoreCase)) segundo.LadoLhRh = "RH";
            else if (string.IsNullOrWhiteSpace(segundo.LadoLhRh) && string.Equals(primero.LadoLhRh, "RH", StringComparison.OrdinalIgnoreCase)) segundo.LadoLhRh = "LH";
        }

        private async Task AsegurarVinculosChecklistCambioMoldeAsync(int checklistArranqueId, int preparacionPrincipalId, List<ChecklistProgramaCambioMoldeInterno> contextos, string tablaVinculo, SqlConnection cn, SqlTransaction tx)
        {
            foreach (var contexto in contextos)
            {
                var sql = $@"
UPDATE {tablaVinculo}
SET EjecucionProduccionID=CASE WHEN @EjecucionProduccionID IS NULL THEN EjecucionProduccionID ELSE @EjecucionProduccionID END,
    LadoLhRh=CASE WHEN @LadoLhRh IS NULL THEN LadoLhRh ELSE @LadoLhRh END,
    Activo=1
WHERE ChecklistArranqueID=@ChecklistArranqueID
  AND ProgramaProduccionID=@ProgramaProduccionID;
IF @@ROWCOUNT=0
BEGIN
    INSERT INTO {tablaVinculo}
    (
        ChecklistArranqueID,ProgramaProduccionID,PreparacionAnticipadaID,EjecucionProduccionID,LadoLhRh,EsPrincipal,Activo
    )
    VALUES
    (
        @ChecklistArranqueID,@ProgramaProduccionID,@PreparacionAnticipadaID,@EjecucionProduccionID,@LadoLhRh,@EsPrincipal,1
    );
END;";
                await using var cmd = new SqlCommand(sql, cn, tx);
                cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value = checklistArranqueId;
                cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = contexto.ProgramaProduccionID;
                cmd.Parameters.Add("@PreparacionAnticipadaID", SqlDbType.Int).Value = contexto.PreparacionAnticipadaID;
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = contexto.EjecucionProduccionID.HasValue ? contexto.EjecucionProduccionID.Value : DBNull.Value;
                cmd.Parameters.Add("@LadoLhRh", SqlDbType.NVarChar, 10).Value = string.IsNullOrWhiteSpace(contexto.LadoLhRh) ? DBNull.Value : contexto.LadoLhRh;
                cmd.Parameters.Add("@EsPrincipal", SqlDbType.Bit).Value = contexto.PreparacionAnticipadaID == preparacionPrincipalId;
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private async Task ValidarCatalogoChecklistCambioMoldeAsync(SqlConnection cn, SqlTransaction? tx = null)
        {
            const string sql = @"
SELECT
    COUNT(1) AS TotalPreguntas,
    COUNT(DISTINCT OrdenSeccion) AS TotalSecciones
FROM dbo.ERP_ChecklistArranquePreguntas
WHERE Activo=1
  AND CodigoFormato=@CodigoFormato
  AND VersionFormato=@VersionFormato;";
            await using var cmd = CrearComandoChecklist(sql, cn, tx);
            cmd.Parameters.Add("@CodigoFormato", SqlDbType.NVarChar, 100).Value = ProduccionChecklistFormato.CambioMoldeCodigo;
            cmd.Parameters.Add("@VersionFormato", SqlDbType.NVarChar, 60).Value = ProduccionChecklistFormato.CambioMoldeVersion;
            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) throw new InvalidOperationException("No fue posible validar el catálogo GQ-F-PR01-03.");
            var totalPreguntas = Convert.ToInt32(rd["TotalPreguntas"]);
            var totalSecciones = Convert.ToInt32(rd["TotalSecciones"]);
            if (totalPreguntas != 39 || totalSecciones != 3) throw new InvalidOperationException($"El catálogo GQ-F-PR01-03 Ver.09 no está completo. Se esperaban 39 preguntas activas en 3 secciones y actualmente existen {totalPreguntas} preguntas activas en {totalSecciones} secciones.");
        }

        private async Task<string> ObtenerTablaVinculoChecklistProgramasAsync(SqlConnection cn, SqlTransaction? tx = null)
        {
            const string sql = @"
SELECT CASE
    WHEN OBJECT_ID(N'dbo.Produccion_ChecklistArranqueProgramas',N'U') IS NOT NULL THEN N'dbo.Produccion_ChecklistArranqueProgramas'
    WHEN OBJECT_ID(N'dbo.Produccion_ChecklistArranquePrograma',N'U') IS NOT NULL THEN N'dbo.Produccion_ChecklistArranquePrograma'
    WHEN OBJECT_ID(N'dbo.Produccion_ChecklistProgramas',N'U') IS NOT NULL THEN N'dbo.Produccion_ChecklistProgramas'
    ELSE NULL
END;";
            await using var cmd = CrearComandoChecklist(sql, cn, tx);
            var valor = await cmd.ExecuteScalarAsync();
            if (valor == null || valor == DBNull.Value || string.IsNullOrWhiteSpace(valor.ToString())) throw new InvalidOperationException("No existe la tabla puente que relaciona el checklist con sus Programas/OF. Debe existir Produccion_ChecklistArranqueProgramas o la tabla equivalente creada para el vínculo LH/RH.");
            return valor.ToString()!;
        }

        private static string? NormalizarResultadoChecklistCambioMolde(string? resultado)
        {
            if (string.IsNullOrWhiteSpace(resultado)) return null;
            var valor = resultado.Trim().ToUpperInvariant();
            if (valor == "SI" || valor == "SÍ" || valor == "OK") return ProduccionChecklistResultado.Ok;
            if (valor == "NO" || valor == "NOK") return ProduccionChecklistResultado.Nok;
            if (valor == "NA" || valor == "N/A" || valor == "NO APLICA") return ProduccionChecklistResultado.Na;
            return valor;
        }

        private static SqlCommand CrearComandoChecklist(string sql, SqlConnection cn, SqlTransaction? tx)
        {
            return tx == null ? new SqlCommand(sql, cn) : new SqlCommand(sql, cn, tx);
        }

        private static string? ChecklistTexto(SqlDataReader rd, string columna)
        {
            return rd[columna] == DBNull.Value ? null : rd[columna]?.ToString()?.Trim();
        }

        private static int? ChecklistNullableInt(SqlDataReader rd, string columna)
        {
            return rd[columna] == DBNull.Value ? null : Convert.ToInt32(rd[columna]);
        }

        private static DateTime? ChecklistNullableDateTime(SqlDataReader rd, string columna)
        {
            return rd[columna] == DBNull.Value ? null : Convert.ToDateTime(rd[columna]);
        }

        private sealed class ChecklistProgramaCambioMoldeInterno
        {
            public int PreparacionAnticipadaID { get; set; }
            public int ProgramaProduccionID { get; set; }
            public int? EjecucionProduccionID { get; set; }
            public int? SolicitudProduccionID { get; set; }
            public int? SolicitudProduccionDetalleID { get; set; }
            public int? ReleaseID { get; set; }
            public int? ReleaseDetalleID { get; set; }
            public string? NumeroOF { get; set; }
            public int? MaquinaID { get; set; }
            public string? MaquinaCodigo { get; set; }
            public string? MaquinaNombre { get; set; }
            public int? MoldeID { get; set; }
            public string? MoldeCodigo { get; set; }
            public int? ParteID { get; set; }
            public string? NumeroParte { get; set; }
            public string? ReferenciaSAP { get; set; }
            public string? DescripcionParte { get; set; }
            public DateTime? FechaInicioProgramada { get; set; }
            public TimeSpan? Cambio { get; set; }
            public string? LadoLhRh { get; set; }
        }
    }
}