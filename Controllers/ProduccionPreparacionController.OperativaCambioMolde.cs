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
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> CambioMoldeOperativa(int programaProduccionId)
        {
            if (!UsuarioEnSesion())
                return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });

            if (programaProduccionId <= 0)
                return BadRequest(new { ok = false, mensaje = "El programa de Producción no es válido." });

            var usuarioId = ObtenerUsuarioID();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var permisos = await ObtenerPermisosPreparacionUsuarioAsync(usuarioId, cn);
            if (!permisos.PuedeVerModulo)
                return StatusCode(StatusCodes.Status403Forbidden);

            var sincronizacion = await SincronizarCambioMoldeOperativaAsync(usuarioId, cn);
            if (!sincronizacion.Ok)
                return StatusCode(StatusCodes.Status500InternalServerError, new { ok = false, mensaje = sincronizacion.Mensaje });

            var tarea = await ResolverTareaCambioMoldeOperativaAsync(programaProduccionId, cn);
            var tareasVista = tarea == null
                ? new List<ProduccionPreparacionTareaVm>()
                : new List<ProduccionPreparacionTareaVm> { tarea };

            if (tareasVista.Count > 0)
                await EnriquecerChecklistCambioMoldeAsync(tareasVista, cn);

            var vm = new ProduccionPreparacionIndexVm
            {
                FechaConsulta = DateTime.Now,
                TipoTarea = ProduccionPreparacionTipo.CambioMolde,
                PuedeVerTodo = permisos.PuedeVerTodo,
                PuedeGestionarCambioMolde = permisos.PuedeGestionarCambioMolde,
                PuedeGestionarEmbalaje = permisos.PuedeGestionarEmbalaje,
                PuedeGestionarSecado = permisos.PuedeGestionarSecado,
                Tareas = tareasVista
            };

            ViewData["ProgramaProduccionID"] = programaProduccionId;

            return PartialView(
                "~/Views/ProduccionOperativa/Acciones/_CambioMoldeContenido.cshtml",
                vm);
        }

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> ChecklistCambioMoldeOperativa(int programaProduccionId)
        {
            if (!UsuarioEnSesion())
                return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });

            if (programaProduccionId <= 0)
                return BadRequest(new { ok = false, mensaje = "El programa de Producción no es válido." });

            var usuarioId = ObtenerUsuarioID();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var permisos = await ObtenerPermisosPreparacionUsuarioAsync(usuarioId, cn);
            if (!permisos.PuedeVerModulo)
                return StatusCode(StatusCodes.Status403Forbidden);

            var sincronizacion = await SincronizarCambioMoldeOperativaAsync(usuarioId, cn);
            if (!sincronizacion.Ok)
                return StatusCode(StatusCodes.Status500InternalServerError, new { ok = false, mensaje = sincronizacion.Mensaje });

            var tareaVisual = await ResolverTareaCambioMoldeOperativaAsync(programaProduccionId, cn);
            if (tareaVisual == null)
                return NotFound(new { ok = false, mensaje = "Esta OF no tiene una tarea de cambio de molde activa o reciente." });

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var tareasGrupo = await CargarGrupoCambioMoldeAsync(tareaVisual.PreparacionAnticipadaID, cn, tx);
                if (tareasGrupo.Count == 0)
                    throw new InvalidOperationException("La tarea de cambio de molde ya no existe o dejó de estar disponible.");

                var origen = tareasGrupo.FirstOrDefault(x => x.ProgramaProduccionID == programaProduccionId)
                             ?? tareasGrupo.FirstOrDefault(x => x.PreparacionAnticipadaID == tareaVisual.PreparacionAnticipadaID)
                             ?? tareasGrupo[0];

                var esPareja = origen.GrupoLhRh.HasValue;
                if (esPareja && tareasGrupo.Select(x => x.ProgramaProduccionID).Distinct().Count() != 2)
                    throw new InvalidOperationException("La operación está marcada como LH/RH, pero no se encontraron correctamente las dos OF.");

                var errorGrupo = ValidarGrupoFisicoCambioMolde(tareasGrupo);
                if (!string.IsNullOrWhiteSpace(errorGrupo))
                    throw new InvalidOperationException(errorGrupo);

                var checklist = await ObtenerOCrearChecklistCambioMoldeAsync(
                    origen.PreparacionAnticipadaID,
                    usuarioId,
                    cn,
                    tx);

                await tx.CommitAsync();

                ViewData["ProgramaProduccionID"] = programaProduccionId;
                ViewData["PreparacionAnticipadaID"] = origen.PreparacionAnticipadaID;
                ViewData["PuedeGestionarCambioMolde"] = permisos.PuedeGestionarCambioMolde;
                ViewData["EstadoCambioMolde"] = origen.Estado;
                ViewData["FechaInicioCambioMolde"] = origen.FechaInicioReal;
                ViewData["LimiteCambioMoldeMinutos"] = ObtenerLimiteGrupoCambioMolde(tareasGrupo);
                ViewData["EsParejaLhRh"] = esPareja;
                ViewData["GrupoLhRh"] = origen.GrupoLhRh;

                return PartialView(
                    "~/Views/ProduccionOperativa/Acciones/_ChecklistCambioMoldeContenido.cshtml",
                    checklist);
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                return BadRequest(new { ok = false, mensaje = "No fue posible abrir el checklist de cambio de molde: " + ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> IniciarCambioMoldeOperativa(ProduccionPreparacionIniciarCambioVm vm)
        {
            if (!UsuarioEnSesion())
                return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });

            LimpiarMensajesCambioMoldeOperativa();
            var resultado = await IniciarCambioMolde(vm);

            return ConvertirResultadoCambioMoldeOperativa(
                resultado,
                "Cambio de molde iniciado correctamente.");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FinalizarCambioMoldeOperativa(ProduccionPreparacionFinalizarCambioVm vm)
        {
            if (!UsuarioEnSesion())
                return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });

            LimpiarMensajesCambioMoldeOperativa();
            var resultado = await FinalizarCambioMolde(vm);

            return ConvertirResultadoCambioMoldeOperativa(
                resultado,
                "Cambio de molde finalizado correctamente.");
        }

        private async Task<(bool Ok, string? Mensaje)> SincronizarCambioMoldeOperativaAsync(
            int usuarioId,
            SqlConnection cn)
        {
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                await SincronizarPreparacionAnticipadaAsync(usuarioId, cn, tx);
                await tx.CommitAsync();
                return (true, null);
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                return (false, "No fue posible sincronizar la preparación de Producción: " + ex.Message);
            }
        }

        private async Task<ProduccionPreparacionTareaVm?> ResolverTareaCambioMoldeOperativaAsync(
            int programaProduccionId,
            SqlConnection cn)
        {
            var ahora = DateTime.Now;
            var tareas = await CargarPreparacionAnticipadaAsync(
                ProduccionPreparacionTipo.CambioMolde,
                null,
                null,
                ahora,
                false,
                cn);

            await EnriquecerTareasParejaLhRhAsync(tareas, cn);
            tareas = ConsolidarCambiosMoldeLhRhVisuales(tareas);

            return tareas
                .Where(x =>
                    x.ProgramaProduccionID == programaProduccionId ||
                    x.ProgramaParejaID == programaProduccionId)
                .OrderBy(x => x.EstaEnProceso ? 0 : x.EstaPendiente ? 1 : x.EstaConfirmada ? 2 : 3)
                .ThenByDescending(x => x.FechaObjetivo)
                .ThenByDescending(x => x.PreparacionAnticipadaID)
                .FirstOrDefault();
        }

        private IActionResult ConvertirResultadoCambioMoldeOperativa(
            IActionResult resultado,
            string mensajePredeterminado)
        {
            if (resultado is UnauthorizedResult)
            {
                return Unauthorized(new
                {
                    ok = false,
                    sesionExpirada = true,
                    mensaje = "La sesión terminó. Vuelve a iniciar sesión."
                });
            }

            if (resultado is ForbidResult)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new
                    {
                        ok = false,
                        mensaje = "No tienes permiso para gestionar el cambio de molde."
                    });
            }

            if (resultado is StatusCodeResult statusCode && statusCode.StatusCode >= 400)
            {
                return StatusCode(
                    statusCode.StatusCode,
                    new
                    {
                        ok = false,
                        mensaje = statusCode.StatusCode == StatusCodes.Status403Forbidden
                            ? "No tienes permiso para gestionar el cambio de molde."
                            : "No fue posible completar la operación de cambio de molde."
                    });
            }

            if (resultado is RedirectToActionResult redirect &&
                string.Equals(redirect.ControllerName, "Login", StringComparison.OrdinalIgnoreCase))
            {
                return Unauthorized(new
                {
                    ok = false,
                    sesionExpirada = true,
                    mensaje = "La sesión terminó. Vuelve a iniciar sesión."
                });
            }

            var error = ConsumirTempDataCambioMoldeOperativa("Error");
            var warning = ConsumirTempDataCambioMoldeOperativa("Warning");
            var success = ConsumirTempDataCambioMoldeOperativa("Success");

            if (!string.IsNullOrWhiteSpace(error))
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = error
                });
            }

            return Json(new
            {
                ok = true,
                mensaje = !string.IsNullOrWhiteSpace(success)
                    ? success
                    : !string.IsNullOrWhiteSpace(warning)
                        ? warning
                        : mensajePredeterminado,
                advertencia = !string.IsNullOrWhiteSpace(warning),
                warning
            });
        }

        private string? ConsumirTempDataCambioMoldeOperativa(string clave)
        {
            if (!TempData.ContainsKey(clave))
                return null;

            var valor = TempData[clave]?.ToString();
            TempData.Remove(clave);
            return valor;
        }

        private void LimpiarMensajesCambioMoldeOperativa()
        {
            TempData.Remove("Success");
            TempData.Remove("Warning");
            TempData.Remove("Error");
        }
    }
}
