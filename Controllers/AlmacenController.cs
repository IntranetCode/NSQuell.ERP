using Microsoft.AspNetCore.Mvc;

namespace ERP.NSQuell.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AlmacenController : AlmacenBaseController
{
    public AlmacenController(IConfiguration configuration) : base(configuration) { }

    [HttpGet]
    public IActionResult Index()
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;

        // /Menu/Grupo/1 es el menú real de Almacén.
        // Esta ruta se conserva solamente como redirección para enlaces antiguos.
        return RedirectToAction("Grupo", "Menu", new { id = 1 });
    }
}
