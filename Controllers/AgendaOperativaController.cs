using ERP.NSQuell.Models;
using ERP.NSQuell.Servicios.Produccion;
using Microsoft.AspNetCore.Mvc;

namespace ERP.NSQuell.Controllers;

[Route("AgendaOperativa")]
public sealed class AgendaOperativaController : Controller
{
    private readonly AgendaOperativaService _agendaOperativaService;
    private readonly ILogger<AgendaOperativaController> _logger;

    public AgendaOperativaController(AgendaOperativaService agendaOperativaService, ILogger<AgendaOperativaController> logger)
    {
        _agendaOperativaService = agendaOperativaService;
        _logger = logger;
    }

    private bool UsuarioEnSesion() => HttpContext.Session.GetInt32("UsuarioID").HasValue;
    private int ObtenerUsuarioID() => HttpContext.Session.GetInt32("UsuarioID") ?? 0;

    [HttpGet("")]
    [HttpGet("Index")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Index(int? maquinaId = null, string? busqueda = null, string? area = null, string? estado = null, bool soloAtencion = false, bool soloBloqueadas = false, bool incluirProduciendo = true, int ventanaHoras = 8)
    {
        if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");

        var usuarioId = ObtenerUsuarioID();
        if (usuarioId <= 0) return RedirectToAction("Login", "Login");

        var filtros = ConstruirFiltros(maquinaId, busqueda, area, estado, soloAtencion, soloBloqueadas, incluirProduciendo, ventanaHoras);

        try
        {
            var vm = await _agendaOperativaService.ObtenerAgendaAsync(filtros, usuarioId);
            return View(vm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar Agenda Operativa. UsuarioID: {UsuarioID}", usuarioId);
            TempData["Error"] = "No fue posible cargar la Agenda Operativa: " + ex.Message;

            return View(new AgendaOperativaVm
            {
                FechaConsulta = DateTime.Now,
                Filtros = filtros,
                Resumen = new AgendaOperativaResumenVm(),
                Items = new List<AgendaOperativaItemVm>(),
                Maquinas = new List<AgendaOperativaOpcionVm>(),
                Areas = ConstruirAreasRespaldo(area)
            });
        }
    }

    [HttpGet("Datos")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Datos(int? maquinaId = null, string? busqueda = null, string? area = null, string? estado = null, bool soloAtencion = false, bool soloBloqueadas = false, bool incluirProduciendo = true, int ventanaHoras = 8)
    {
        if (!UsuarioEnSesion())
        {
            return Unauthorized(new
            {
                ok = false,
                sesionExpirada = true,
                mensaje = "La sesión terminó. Vuelve a iniciar sesión."
            });
        }

        var usuarioId = ObtenerUsuarioID();

        if (usuarioId <= 0)
        {
            return Unauthorized(new
            {
                ok = false,
                sesionExpirada = true,
                mensaje = "No fue posible identificar al usuario."
            });
        }

        var filtros = ConstruirFiltros(maquinaId, busqueda, area, estado, soloAtencion, soloBloqueadas, incluirProduciendo, ventanaHoras);

        try
        {
            var vm = await _agendaOperativaService.ObtenerAgendaAsync(filtros, usuarioId);

            return Json(new
            {
                ok = true,
                fechaConsulta = vm.FechaConsulta,
                resumen = vm.Resumen,
                filtros = vm.Filtros,
                maquinas = vm.Maquinas,
                areas = vm.Areas,
                items = vm.Items
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al actualizar Agenda Operativa. UsuarioID: {UsuarioID}", usuarioId);

            return StatusCode(500, new
            {
                ok = false,
                mensaje = "No fue posible actualizar la Agenda Operativa: " + ex.Message
            });
        }
    }

    [HttpGet("Resumen")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Resumen(int? maquinaId = null, string? busqueda = null, string? area = null, string? estado = null, bool soloAtencion = false, bool soloBloqueadas = false, bool incluirProduciendo = true, int ventanaHoras = 8)
    {
        if (!UsuarioEnSesion())
        {
            return Unauthorized(new
            {
                ok = false,
                sesionExpirada = true,
                mensaje = "La sesión terminó. Vuelve a iniciar sesión."
            });
        }

        var usuarioId = ObtenerUsuarioID();

        if (usuarioId <= 0)
        {
            return Unauthorized(new
            {
                ok = false,
                sesionExpirada = true,
                mensaje = "No fue posible identificar al usuario."
            });
        }

        var filtros = ConstruirFiltros(maquinaId, busqueda, area, estado, soloAtencion, soloBloqueadas, incluirProduciendo, ventanaHoras);

        try
        {
            var vm = await _agendaOperativaService.ObtenerAgendaAsync(filtros, usuarioId);

            return Json(new
            {
                ok = true,
                fechaConsulta = vm.FechaConsulta,
                total = vm.Resumen.Total,
                atencionInmediata = vm.Resumen.AtencionInmediata,
                bloqueadas = vm.Resumen.Bloqueadas,
                proximas = vm.Resumen.Proximas,
                enPreparacion = vm.Resumen.EnPreparacion,
                esperandoCalidad = vm.Resumen.EsperandoCalidad,
                produciendo = vm.Resumen.Produciendo,
                pausadas = vm.Resumen.Pausadas,
                interrumpidasUrgente = vm.Resumen.InterrumpidasUrgente,
                reliberaciones = vm.Resumen.Reliberaciones,
                maquinasLiberadas = vm.Resumen.MaquinasLiberadas
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener resumen de Agenda Operativa. UsuarioID: {UsuarioID}", usuarioId);

            return StatusCode(500, new
            {
                ok = false,
                mensaje = "No fue posible obtener el resumen de la Agenda Operativa: " + ex.Message
            });
        }
    }

    private static AgendaOperativaFiltroVm ConstruirFiltros(int? maquinaId, string? busqueda, string? area, string? estado, bool soloAtencion, bool soloBloqueadas, bool incluirProduciendo, int ventanaHoras)
    {
        if (maquinaId.HasValue && maquinaId.Value <= 0) maquinaId = null;

        busqueda = string.IsNullOrWhiteSpace(busqueda)
            ? null
            : busqueda.Trim();

        area = string.IsNullOrWhiteSpace(area)
            ? null
            : area.Trim().ToUpperInvariant();

        estado = string.IsNullOrWhiteSpace(estado)
            ? null
            : estado.Trim().ToUpperInvariant();

        ventanaHoras = Math.Clamp(ventanaHoras <= 0 ? 8 : ventanaHoras, 1, 72);

        return new AgendaOperativaFiltroVm
        {
            MaquinaID = maquinaId,
            Busqueda = busqueda,
            Area = area,
            Estado = estado,
            SoloAtencion = soloAtencion,
            SoloBloqueadas = soloBloqueadas,
            IncluirProduciendo = incluirProduciendo,
            VentanaHoras = ventanaHoras
        };
    }

    private static List<AgendaOperativaOpcionVm> ConstruirAreasRespaldo(string? areaSeleccionada)
    {
        areaSeleccionada = string.IsNullOrWhiteSpace(areaSeleccionada)
            ? null
            : areaSeleccionada.Trim().ToUpperInvariant();

        var areas = new[]
        {
            AgendaOperativaArea.Planeacion,
            AgendaOperativaArea.Produccion,
            AgendaOperativaArea.TecnicoProduccion,
            AgendaOperativaArea.Smed,
            AgendaOperativaArea.Calidad,
            AgendaOperativaArea.Almacen,
            AgendaOperativaArea.Materiales,
            AgendaOperativaArea.Secado,
            AgendaOperativaArea.Embalaje,
            AgendaOperativaArea.Operador,
            AgendaOperativaArea.Mantenimiento
        };

        return areas
            .Select(x => new AgendaOperativaOpcionVm
            {
                Valor = x,
                Texto = AgendaOperativaArea.Nombre(x),
                Seleccionado = string.Equals(x, areaSeleccionada, StringComparison.OrdinalIgnoreCase)
            })
            .ToList();
    }
}