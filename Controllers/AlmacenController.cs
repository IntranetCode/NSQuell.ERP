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

        return View();
    }
}
