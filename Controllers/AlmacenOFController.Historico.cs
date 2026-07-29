using ERP.NSQuell.Models.ViewModels.Almacen;
using Microsoft.AspNetCore.Mvc;

namespace ERP.NSQuell.Controllers;

// ALMACEN_OF_HISTORICO_V4_2
public sealed partial class AlmacenOFController
{
    [HttpGet]
    public async Task<IActionResult> Historico(
        string? q,
        DateTime? desde,
        DateTime? hasta,
        CancellationToken cancellationToken = default)
    {
        var resultado = await Index(
            q: q,
            estatus: null,
            area: null,
            desde: desde,
            hasta: hasta,
            pagina: 1,
            cancellationToken: cancellationToken);

        if (resultado is ViewResult vista
            && vista.Model is AlmacenOFIndexVm modelo)
        {
            return View("Historico", modelo);
        }

        return resultado;
    }
}
