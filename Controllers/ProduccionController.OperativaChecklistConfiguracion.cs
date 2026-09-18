using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers;

public sealed partial class ProduccionController
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ChecklistArranqueOperativa(int ejecucionProduccionId)
    {
        if (!UsuarioEnSesion())
            return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });
        if (ejecucionProduccionId <= 0)
            return BadRequest(new { ok = false, mensaje = "La ejecución de Producción no es válida." });

        var usuarioId = ObtenerUsuarioID();
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        var ejecucion = await ObtenerEjecucionAsync(ejecucionProduccionId, cn);
        if (ejecucion == null)
            return NotFound(new { ok = false, mensaje = "No se encontró la ejecución de Producción." });

        int checklistArranqueId;
        await using (var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            try
            {
                checklistArranqueId = await ObtenerOCrearChecklistsInicialesAsync(ejecucion, usuarioId, cn, tx);
                await tx.CommitAsync();
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                return StatusCode(StatusCodes.Status500InternalServerError, new { ok = false, mensaje = "No fue posible preparar el checklist de arranque: " + ex.Message });
            }
        }

        var checklist = await ObtenerChecklistArranqueAsync(checklistArranqueId, cn);
        if (checklist == null)
            return NotFound(new { ok = false, mensaje = "No fue posible obtener el checklist de arranque." });

        await CargarEstadoCalidadChecklistAsync(checklist, cn);

        var puedeGestionar = await UsuarioPuedeGestionarChecklistArranqueAsync(usuarioId, cn);
        var puedeEditar = puedeGestionar && ProduccionChecklistEstatus.PuedeEditarProduccion(checklist.EstatusID);

        ViewData["PuedeGestionarChecklistArranqueOperativa"] = puedeGestionar;
        ViewData["PuedeEditarChecklistArranqueOperativa"] = puedeEditar;

        return PartialView("~/Views/ProduccionOperativa/Acciones/_ChecklistArranqueContenido.cshtml", checklist);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarChecklistArranqueOperativa(ProduccionChecklistGuardarVm vm)
    {
        if (!UsuarioEnSesion())
            return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });

        LimpiarTempDataOperativaTecnica();

        IActionResult resultado;
        try
        {
            resultado = await GuardarChecklistArranque(vm);
        }
        catch (Exception ex)
        {
            return BadRequest(new { ok = false, mensaje = "No fue posible guardar el checklist: " + ex.Message });
        }

        return ConvertirResultadoOperativaTecnica(
            resultado,
            vm.EnviarACalidad
                ? "Checklist capturado y enviado a validación de Calidad."
                : "Checklist guardado correctamente.",
            esperandoCalidad: vm.EnviarACalidad);
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ConfiguracionTecnicaOperativa(int ejecucionProduccionId)
    {
        if (!UsuarioEnSesion())
            return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });
        if (ejecucionProduccionId <= 0)
            return BadRequest(new { ok = false, mensaje = "La ejecución de Producción no es válida." });

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        var contexto = await ObtenerContextoConfiguracionCorridaAsync(ejecucionProduccionId, cn);
        if (contexto == null)
            return NotFound(new { ok = false, mensaje = "No se encontró la ejecución de Producción." });

        var checklist = await ObtenerChecklistArranquePorEjecucionAsync(ejecucionProduccionId, cn);
        if (checklist == null)
            return StatusCode(StatusCodes.Status409Conflict, new { ok = false, mensaje = "Primero debes completar el checklist de arranque GQ-F-PR01-06." });

        await CargarEstadoCalidadChecklistAsync(checklist, cn);
        if (checklist.EstatusID != ProduccionChecklistEstatus.ValidadoPorCalidad)
        {
            var esperandoCalidad = checklist.EstatusID == ProduccionChecklistEstatus.PendienteValidacionCalidad;
            var mensaje = esperandoCalidad
                ? "Producción ya terminó el checklist. Debes esperar la validación de Calidad antes de confirmar la configuración técnica."
                : checklist.EstatusID == ProduccionChecklistEstatus.RechazadoRequiereAjuste
                    ? "Calidad devolvió el checklist. Corrige el GQ-F-PR01-06 antes de continuar con la configuración técnica."
                    : "Completa y envía el checklist GQ-F-PR01-06 a Calidad antes de continuar con la configuración técnica.";
            return StatusCode(StatusCodes.Status409Conflict, new { ok = false, esperandoCalidad, mensaje });
        }

        var vm = await ConstruirConfiguracionTecnicoAsync(contexto, cn);
        var permisos = await ObtenerPermisosProduccionUsuarioAsync(ObtenerUsuarioID(), cn);
        var puedeModificar = PuedeModificarConfiguracionCorrida(permisos, contexto) && EjecucionPermiteConfiguracionCorrida(contexto);

        if (vm.ParejaLhRh?.TieneEjecucionPareja == true)
        {
            var contextoPareja = await ObtenerContextoConfiguracionCorridaAsync(vm.ParejaLhRh.EjecucionParejaID!.Value, cn);
            if (contextoPareja == null || !PuedeModificarConfiguracionCorrida(permisos, contextoPareja) || !EjecucionPermiteConfiguracionCorrida(contextoPareja))
                puedeModificar = false;
        }

        ViewData["PuedeModificarConfiguracionOperativa"] = puedeModificar;

        return PartialView("~/Views/ProduccionOperativa/Acciones/_ConfiguracionTecnicaContenido.cshtml", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarConfiguracionCorridaOperativa(ProduccionConfiguracionTecnicoPostVm vm)
    {
        if (!UsuarioEnSesion())
            return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });

        var validacionChecklist = await ValidarChecklistParaConfiguracionOperativaAsync(vm.EjecucionProduccionID);
        if (!validacionChecklist.Permitido)
            return StatusCode(StatusCodes.Status409Conflict, new { ok = false, esperandoCalidad = validacionChecklist.EsperandoCalidad, mensaje = validacionChecklist.Mensaje });

        LimpiarTempDataOperativaTecnica();

        IActionResult resultado;
        try
        {
            resultado = await GuardarConfiguracionCorrida(vm);
        }
        catch (Exception ex)
        {
            return BadRequest(new { ok = false, mensaje = "No fue posible guardar la configuración técnica: " + ex.Message });
        }

        return ConvertirResultadoOperativaTecnica(
            resultado,
            "Configuración técnica confirmada correctamente.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CambiarConfiguracionCorridaOperativa(ProduccionConfiguracionTecnicoPostVm vm)
    {
        if (!UsuarioEnSesion())
            return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });

        var validacionChecklist = await ValidarChecklistParaConfiguracionOperativaAsync(vm.EjecucionProduccionID);
        if (!validacionChecklist.Permitido)
            return StatusCode(StatusCodes.Status409Conflict, new { ok = false, esperandoCalidad = validacionChecklist.EsperandoCalidad, mensaje = validacionChecklist.Mensaje });

        LimpiarTempDataOperativaTecnica();

        IActionResult resultado;
        try
        {
            resultado = await CambiarConfiguracionCorrida(vm);
        }
        catch (Exception ex)
        {
            return BadRequest(new { ok = false, mensaje = "No fue posible cambiar la configuración técnica: " + ex.Message });
        }

        return ConvertirResultadoOperativaTecnica(
            resultado,
            "Configuración técnica actualizada correctamente.");
    }


    private async Task<(bool Permitido, bool EsperandoCalidad, string Mensaje)> ValidarChecklistParaConfiguracionOperativaAsync(int ejecucionProduccionId)
    {
        if (ejecucionProduccionId <= 0)
            return (false, false, "La ejecución de Producción no es válida.");

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        var checklist = await ObtenerChecklistArranquePorEjecucionAsync(ejecucionProduccionId, cn);
        if (checklist == null)
            return (false, false, "Primero debes completar el checklist de arranque GQ-F-PR01-06.");

        await CargarEstadoCalidadChecklistAsync(checklist, cn);

        if (checklist.EstatusID == ProduccionChecklistEstatus.ValidadoPorCalidad)
            return (true, false, string.Empty);

        if (checklist.EstatusID == ProduccionChecklistEstatus.PendienteValidacionCalidad)
            return (false, true, "Producción ya terminó el checklist. Debes esperar la validación de Calidad antes de confirmar la configuración técnica.");

        if (checklist.EstatusID == ProduccionChecklistEstatus.RechazadoRequiereAjuste)
            return (false, false, "Calidad devolvió el checklist. Corrige el GQ-F-PR01-06 antes de continuar con la configuración técnica.");

        return (false, false, "Completa y envía el checklist GQ-F-PR01-06 a Calidad antes de continuar con la configuración técnica.");
    }

    private IActionResult ConvertirResultadoOperativaTecnica(IActionResult resultado, string mensajePredeterminado, bool esperandoCalidad = false)
    {
        if (resultado is UnauthorizedResult)
            return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });

        if (resultado is ForbidResult)
            return StatusCode(StatusCodes.Status403Forbidden, new { ok = false, mensaje = "No tienes permiso para realizar esta operación." });

        if (resultado is NotFoundResult)
            return NotFound(new { ok = false, mensaje = "La información solicitada ya no está disponible." });

        if (resultado is StatusCodeResult status && status.StatusCode >= 400)
            return StatusCode(status.StatusCode, new { ok = false, mensaje = "No fue posible completar la operación." });

        if (resultado is RedirectToActionResult redirect && string.Equals(redirect.ControllerName, "Login", StringComparison.OrdinalIgnoreCase))
            return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });

        var error = ConsumirTempDataOperativaTecnica("Error");
        var warning = ConsumirTempDataOperativaTecnica("Warning");
        var info = ConsumirTempDataOperativaTecnica("Info");
        var success = ConsumirTempDataOperativaTecnica("Success");

        if (!string.IsNullOrWhiteSpace(error))
            return BadRequest(new { ok = false, mensaje = error });

        return Json(new
        {
            ok = true,
            mensaje = !string.IsNullOrWhiteSpace(success)
                ? success
                : !string.IsNullOrWhiteSpace(warning)
                    ? warning
                    : !string.IsNullOrWhiteSpace(info)
                        ? info
                        : mensajePredeterminado,
            advertencia = !string.IsNullOrWhiteSpace(warning),
            esperandoCalidad,
            refrescarCentro = true,
            refrescarCalendario = true
        });
    }

    private string? ConsumirTempDataOperativaTecnica(string clave)
    {
        if (!TempData.ContainsKey(clave)) return null;
        var valor = TempData[clave]?.ToString();
        TempData.Remove(clave);
        return valor;
    }

    private void LimpiarTempDataOperativaTecnica()
    {
        TempData.Remove("Error");
        TempData.Remove("Warning");
        TempData.Remove("Info");
        TempData.Remove("Success");
    }
}
