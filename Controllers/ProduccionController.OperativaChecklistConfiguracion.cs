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
        var bloqueoConfiguracion = await ValidarConfiguracionAntesChecklistV13Async(ejecucionProduccionId, cn);
        var puedeEditar = puedeGestionar && string.IsNullOrWhiteSpace(bloqueoConfiguracion) && ProduccionChecklistEstatus.PuedeEditarProduccion(checklist.EstatusID);
        ViewData["BloqueoConfiguracionChecklistOperativa"] = bloqueoConfiguracion;

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


    // NSQ_ORDEN_TECNICO_V1_3: configuración técnica → checklist de Producción → Calidad.
    private async Task<string?> ValidarConfiguracionAntesChecklistV13Async(int ejecucionId, SqlConnection cn, SqlTransaction? tx = null)
    {
        var contexto = await ObtenerContextoConfiguracionCorridaAsync(ejecucionId, cn, tx);
        if (contexto == null || !EjecucionPermiteConfiguracionCorrida(contexto))
            return "No existe una ejecución activa para capturar el checklist de arranque.";
        if (!await TieneConfiguracionParaChecklistV13Async(ejecucionId, cn, tx))
            return "Primero confirma la configuración técnica: cavidades reales, tiempo de ciclo y contador base. Después se habilitará el checklist de arranque.";
        var pareja = await ObtenerParejaLhRhProduccionAsync(contexto.ProgramaProduccionID, cn, tx);
        if (pareja != null)
        {
            if (!pareja.TieneEjecucionPareja || !pareja.EsCompatibleFisicamente)
                return "Corrige la pareja LH/RH antes de capturar el checklist de arranque.";
            if (!await TieneConfiguracionParaChecklistV13Async(pareja.EjecucionParejaID!.Value, cn, tx))
                return "Primero confirma la configuración técnica de ambas OF LH/RH. Después se habilitará el checklist de arranque.";
        }
        return null;
    }

    private static async Task<bool> TieneConfiguracionParaChecklistV13Async(int ejecucionId, SqlConnection cn, SqlTransaction? tx)
    {
        const string sql = @"
SELECT COUNT(1)
FROM dbo.Produccion_ConfiguracionCorrida WITH(UPDLOCK,HOLDLOCK)
WHERE EjecucionProduccionID=@EjecucionID AND Activo=1 AND FechaFinVigencia IS NULL
  AND CavidadesUsadas>0 AND TiempoCicloSegundos>0 AND ContadorInicioVigencia IS NOT NULL;";
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@EjecucionID", SqlDbType.Int).Value = ejecucionId;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
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
