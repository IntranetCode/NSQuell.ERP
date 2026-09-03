// NSQ_HCC_CREAR_HISTORIAL_V1_2
using ERP.NSQuell.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace ERP.NSQuell.Controllers;

public partial class CalidadController
{
    /// <summary>
    /// Vista operativa para iniciar un Control de Calidad.
    /// No crea registros huérfanos: reutiliza los requerimientos
    /// automáticos ya generados por Producción/Calidad y su HCC vigente.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> CrearControlCalidad(
        string? busqueda)
    {
        var resultado =
            await HojasControl(
                busqueda,
                HccPendiente);

        if (
            resultado is ViewResult view
            && view.Model is CalidadHCCBandejaViewModel vm
        )
        {
            vm.Estado=HccPendiente;
            return View(
                "CrearControlCalidad",
                vm);
        }

        return resultado;
    }

    /// <summary>
    /// Historial de controles HCC finalizados.
    /// Reutiliza exactamente la misma fuente de verdad de HojasControl.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> HistorialControlCalidad(
        string? busqueda)
    {
        var resultado =
            await HojasControl(
                busqueda,
                HccCompletada);

        if (
            resultado is ViewResult view
            && view.Model is CalidadHCCBandejaViewModel vm
        )
        {
            vm.Estado=HccCompletada;
            return View(
                "HistorialControlCalidad",
                vm);
        }

        return resultado;
    }
}