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
        public async Task<IActionResult> SecadoOperativa(int programaProduccionId)
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

            await using (var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                try
                {
                    await SincronizarPreparacionAnticipadaAsync(usuarioId, cn, tx);
                    await SincronizarSecadoMaterialConPlaneacionAsync(usuarioId, cn, tx);
                    await SincronizarSecadoParametrosTecnicosV12Async(usuarioId, cn, tx);
                    await ConsolidarSecadosPendientesSinIniciarAsync(usuarioId, cn, tx);
                    await tx.CommitAsync();
                }
                catch (Exception ex)
                {
                    try { await tx.RollbackAsync(); } catch { }
                    return StatusCode(
                        StatusCodes.Status500InternalServerError,
                        new
                        {
                            ok = false,
                            mensaje = "No fue posible sincronizar la información de Secado: " + ex.Message
                        });
                }
            }

            var ahora = await ObtenerFechaServidorSecadoAsync(cn);
            var configuracion = await CargarConfiguracionSecadoAsync(cn);
            var tolvas = await CargarTolvasSecadoAsync(cn);
            var materiales = await CargarMaterialesSecadoAsync(null, null, ahora, configuracion, cn);

            await EnriquecerSecadoLhRhAsync(materiales, ahora, configuracion, cn);

            var materialOrigen = materiales
                .FirstOrDefault(x => x.ProgramaProduccionID == programaProduccionId);

            List<ProduccionSecadoMaterialVm> materialesPrograma;

            if (materialOrigen?.GrupoLhRh.HasValue == true)
            {
                var grupo = materialOrigen.GrupoLhRh.Value;

                materialesPrograma = materiales
                    .Where(x => x.GrupoLhRh == grupo)
                    .OrderBy(x => x.LadoLhRh)
                    .ThenBy(x => x.SecadoMaterialID)
                    .ToList();
            }
            else
            {
                materialesPrograma = materiales
                    .Where(x => x.ProgramaProduccionID == programaProduccionId)
                    .OrderBy(x => x.SecadoMaterialID)
                    .ToList();
            }

            var programasRelacionados = materialesPrograma
                .Where(x => x.ProgramaProduccionID.HasValue)
                .Select(x => x.ProgramaProduccionID!.Value)
                .ToHashSet();

            programasRelacionados.Add(programaProduccionId);

            var tareasPlaneadas = await CargarPreparacionAnticipadaAsync(
                ProduccionPreparacionTipo.SecadoMaterial,
                null,
                null,
                ahora,
                false,
                cn);

            var programasConMaterial = materialesPrograma
                .Where(x => x.ProgramaProduccionID.HasValue)
                .Select(x => x.ProgramaProduccionID!.Value)
                .ToHashSet();

            var pendientesPlaneacion = tareasPlaneadas
                .Where(x =>
                    programasRelacionados.Contains(x.ProgramaProduccionID) &&
                    (x.EstaPendiente || x.EstaEnProceso) &&
                    (
                        x.CantidadMpKg.GetValueOrDefault() > ProduccionSecadoReglas.ToleranciaCantidad
                            ? x.CantidadMpPendienteRecepcionKg > ProduccionSecadoReglas.ToleranciaCantidad
                            : !programasConMaterial.Contains(x.ProgramaProduccionID)
                    ))
                .OrderBy(x => x.FechaAviso)
                .ThenBy(x => x.FechaObjetivo)
                .ThenBy(x => x.ProgramaProduccionID)
                .ToList();

            var vm = new ProduccionSecadoIndexVm
            {
                FechaConsulta = ahora,
                PuedeGestionarSecado = permisos.PuedeGestionarSecado,
                Configuracion = configuracion,
                Tolvas = tolvas,
                Materiales = materialesPrograma,
                PendientesPlaneacion = pendientesPlaneacion
            };

            ViewData["ProgramaProduccionID"] = programaProduccionId;

            return PartialView(
                "~/Views/ProduccionOperativa/Acciones/_SecadoContenido.cshtml",
                vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> IniciarSecadoOperativa(ProduccionIniciarSecadoVm vm)
        {
            if (!UsuarioEnSesion())
                return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });

            LimpiarMensajesSecadoOperativa();

            var resultado = await IniciarSecado(vm);

            return ConvertirResultadoSecadoOperativa(
                resultado,
                "Secado iniciado correctamente.");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarTolvaSecadoOperativa(ProduccionCambiarTolvaVm vm)
        {
            if (!UsuarioEnSesion())
                return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });

            LimpiarMensajesSecadoOperativa();

            var resultado = await CambiarTolvaSecado(vm);

            return ConvertirResultadoSecadoOperativa(
                resultado,
                "La tolva fue actualizada correctamente.");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FinalizarSecadoOperativa(ProduccionFinalizarSecadoVm vm)
        {
            if (!UsuarioEnSesion())
                return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });

            LimpiarMensajesSecadoOperativa();

            var resultado = await FinalizarSecado(vm);

            return ConvertirResultadoSecadoOperativa(
                resultado,
                "La carga de secado fue finalizada correctamente.");
        }

        private IActionResult ConvertirResultadoSecadoOperativa(
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
                        mensaje = "No tienes permiso para realizar esta operación de Secado."
                    });
            }

            if (resultado is StatusCodeResult statusCode &&
                statusCode.StatusCode >= 400)
            {
                return StatusCode(
                    statusCode.StatusCode,
                    new
                    {
                        ok = false,
                        mensaje = statusCode.StatusCode == StatusCodes.Status403Forbidden
                            ? "No tienes permiso para realizar esta operación de Secado."
                            : "No fue posible completar la operación de Secado."
                    });
            }

            if (resultado is RedirectToActionResult redirect &&
                string.Equals(
                    redirect.ControllerName,
                    "Login",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Unauthorized(new
                {
                    ok = false,
                    sesionExpirada = true,
                    mensaje = "La sesión terminó. Vuelve a iniciar sesión."
                });
            }

            var error = ConsumirTempDataSecadoOperativa("Error");
            var warning = ConsumirTempDataSecadoOperativa("Warning");
            var success = ConsumirTempDataSecadoOperativa("Success");

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

        private string? ConsumirTempDataSecadoOperativa(string clave)
        {
            if (!TempData.ContainsKey(clave))
                return null;

            var valor = TempData[clave]?.ToString();
            TempData.Remove(clave);
            return valor;
        }

        private void LimpiarMensajesSecadoOperativa()
        {
            TempData.Remove("Success");
            TempData.Remove("Warning");
            TempData.Remove("Error");
        }
    }
}
