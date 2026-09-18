using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers;

public sealed partial class ProduccionController
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IniciarOperativa(
        int programaProduccionId,
        string? observaciones = null,
        List<long>? etiquetasBlancasSeleccionadas = null)
    {
        if (!UsuarioEnSesion())
            return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });

        if (programaProduccionId <= 0)
            return BadRequest(new { ok = false, mensaje = "No se recibió correctamente el programa de Producción." });

        TempData.Remove("Error");
        TempData.Remove("Success");
        TempData.Remove("Info");

        IActionResult resultado;
        try
        {
            resultado = await Iniciar(
                programaProduccionId,
                operadorId: null,
                operadorNombre: null,
                operadorAuxiliarId: null,
                operadorAuxiliarNombre: null,
                tecnicoProduccionId: null,
                smedId: null,
                personalInicioConfirmado: false,
                observaciones: observaciones,
                etiquetasBlancasSeleccionadas: etiquetasBlancasSeleccionadas);
        }
        catch (Exception ex)
        {
            return BadRequest(new { ok = false, mensaje = "No fue posible iniciar la preparación: " + ex.Message });
        }

        var error = TempData["Error"]?.ToString();
        var success = TempData["Success"]?.ToString();
        var info = TempData["Info"]?.ToString();

        TempData.Remove("Error");
        TempData.Remove("Success");
        TempData.Remove("Info");

        if (!string.IsNullOrWhiteSpace(error))
            return BadRequest(new { ok = false, mensaje = error });

        if (resultado is RedirectToActionResult redirect)
        {
            int? ejecucionProduccionId = null;
            if (redirect.RouteValues != null && redirect.RouteValues.TryGetValue("id", out var rawId) && rawId != null && int.TryParse(Convert.ToString(rawId), out var id) && id > 0)
                ejecucionProduccionId = id;

            return Json(new
            {
                ok = true,
                programaProduccionId,
                ejecucionProduccionId,
                mensaje = !string.IsNullOrWhiteSpace(success)
                    ? success
                    : !string.IsNullOrWhiteSpace(info)
                        ? info
                        : "Preparación iniciada correctamente.",
                refrescarCentro = true,
                refrescarCalendario = true
            });
        }

        if (resultado is UnauthorizedResult)
            return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });

        if (resultado is NotFoundResult)
            return NotFound(new { ok = false, mensaje = "La OF ya no está disponible para iniciar preparación." });

        return Json(new
        {
            ok = true,
            programaProduccionId,
            mensaje = !string.IsNullOrWhiteSpace(success) ? success : !string.IsNullOrWhiteSpace(info) ? info : "Operación completada.",
            refrescarCentro = true,
            refrescarCalendario = true
        });
    }
}
