using ERP.NSQuell.Models;
using ERP.NSQuell.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers
{
    public class CalidadController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CalidadController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? busqueda, string? estadoFiltro)
        {
            busqueda = busqueda?.Trim();
            estadoFiltro = estadoFiltro?.Trim().ToUpperInvariant();

            var baseQuery = _context.CalidadInspecciones
                .AsNoTracking();

            var query = baseQuery;

            if (!string.IsNullOrWhiteSpace(busqueda))
            {
                query = query.Where(x =>
                    (x.CodigoBarras != null && x.CodigoBarras.Contains(busqueda)) ||
                    (x.OrdenTrabajo != null && x.OrdenTrabajo.Contains(busqueda)) ||
                    (x.NumeroParte != null && x.NumeroParte.Contains(busqueda)) ||
                    (x.Material != null && x.Material.Contains(busqueda)) ||
                    (x.Proceso != null && x.Proceso.Contains(busqueda)) ||
                    (x.Maquina != null && x.Maquina.Contains(busqueda))
                );
            }

            if (!string.IsNullOrWhiteSpace(estadoFiltro))
            {
                query = query.Where(x => x.Estado == estadoFiltro);
            }

            var model = new CalidadIndexViewModel
            {
                Busqueda = busqueda,
                EstadoFiltro = estadoFiltro,

                TotalAbiertas = await baseQuery.CountAsync(x => x.Estado == "ABIERTA"),
                TotalLiberadas = await baseQuery.CountAsync(x => x.Estado == "LIBERADA"),
                TotalGPI2 = await baseQuery.CountAsync(x => x.Estado == "GPI2"),
                TotalContencion = await baseQuery.CountAsync(x => x.Estado == "CONTENCION"),
                TotalScrap = await baseQuery.CountAsync(x => x.Estado == "SCRAP"),
                TotalCerradas = await baseQuery.CountAsync(x => x.Estado == "CERRADA")
            };

            model.Inspecciones = await query
                .OrderByDescending(x => x.FechaCreacion)
                .Select(x => new CalidadListadoItemViewModel
                {
                    InspeccionID = x.InspeccionID,
                    CodigoBarras = x.CodigoBarras,
                    OrdenTrabajo = x.OrdenTrabajo,
                    NumeroParte = x.NumeroParte,
                    Material = x.Material,
                    Proceso = x.Proceso,
                    Maquina = x.Maquina,
                    CantidadTotal = x.CantidadTotal,
                    CantidadRevisada = x.CantidadRevisada,
                    CantidadPendiente = x.CantidadPendiente,
                    ResultadoCalidad = x.ResultadoCalidad,
                    Etiqueta = x.Etiqueta,
                    Estado = x.Estado,
                    FechaCreacion = x.FechaCreacion
                })
                .ToListAsync();

            model.TotalMostrados = model.Inspecciones.Count;

            return View(model);
        }

        [HttpGet]
        public IActionResult Crear()
        {
            var model = new CalidadFormViewModel
            {
                CantidadTotal = 0,
                CantidadRevisada = 0
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Crear(CalidadFormViewModel model)
        {
            NormalizarModelo(model);

            if (model.CantidadRevisada > model.CantidadTotal)
            {
                ModelState.AddModelError(
                    nameof(model.CantidadRevisada),
                    "La cantidad revisada no puede ser mayor a la cantidad total."
                );
            }

            if (!string.IsNullOrWhiteSpace(model.ResultadoCalidad))
            {
                bool documentosCompletos =
                    model.ChecklistValidado &&
                    model.HojaInspeccionProducto &&
                    model.HojaValidacionCalidad;

                if (!documentosCompletos)
                {
                    ModelState.AddModelError(
                        "",
                        "Para registrar un resultado de calidad primero debes validar checklist, hoja de inspección y hoja de validación."
                    );
                }
            }

            if (!ModelState.IsValid)
                return View(model);

            int? usuarioId = ObtenerUsuarioIdActual();

            var inspeccion = new CalidadInspeccion
            {
                CodigoBarras = model.CodigoBarras,
                OrdenTrabajo = model.OrdenTrabajo,
                NumeroParte = model.NumeroParte,
                Material = model.Material,
                Proceso = model.Proceso,
                Maquina = model.Maquina,

                CantidadTotal = model.CantidadTotal,
                CantidadRevisada = model.CantidadRevisada,
                CantidadPendiente = CalcularPendiente(model.CantidadTotal, model.CantidadRevisada),

                ChecklistValidado = model.ChecklistValidado,
                HojaInspeccionProducto = model.HojaInspeccionProducto,
                HojaValidacionCalidad = model.HojaValidacionCalidad,

                ResultadoCalidad = model.ResultadoCalidad,
                Observaciones = model.Observaciones,

                Estado = "ABIERTA",
                UsuarioCreacionID = usuarioId,
                FechaCreacion = DateTime.Now
            };

            AplicarResultado(inspeccion);

            _context.CalidadInspecciones.Add(inspeccion);
            await _context.SaveChangesAsync();

            await RegistrarHistorialAsync(
                inspeccion.InspeccionID,
                "CREACION",
                null,
                inspeccion.Estado,
                inspeccion.ResultadoCalidad,
                inspeccion.Etiqueta,
                "Registro de inspección creado.",
                usuarioId
            );

            TempData["Mensaje"] = "Inspección registrada correctamente.";

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Detalle(int id)
        {
            var inspeccion = await _context.CalidadInspecciones
                .AsNoTracking()
                .Include(x => x.Historial)
                .FirstOrDefaultAsync(x => x.InspeccionID == id);

            if (inspeccion == null)
                return NotFound();

            var model = new CalidadDetalleViewModel
            {
                InspeccionID = inspeccion.InspeccionID,
                CodigoBarras = inspeccion.CodigoBarras,
                OrdenTrabajo = inspeccion.OrdenTrabajo,
                NumeroParte = inspeccion.NumeroParte,
                Material = inspeccion.Material,
                Proceso = inspeccion.Proceso,
                Maquina = inspeccion.Maquina,

                CantidadTotal = inspeccion.CantidadTotal,
                CantidadRevisada = inspeccion.CantidadRevisada,
                CantidadPendiente = inspeccion.CantidadPendiente,

                ChecklistValidado = inspeccion.ChecklistValidado,
                HojaInspeccionProducto = inspeccion.HojaInspeccionProducto,
                HojaValidacionCalidad = inspeccion.HojaValidacionCalidad,

                ResultadoCalidad = inspeccion.ResultadoCalidad,
                Etiqueta = inspeccion.Etiqueta,

                Liberado = inspeccion.Liberado,
                RequiereGPI2 = inspeccion.RequiereGPI2,
                EnContencion = inspeccion.EnContencion,
                EsScrap = inspeccion.EsScrap,

                Observaciones = inspeccion.Observaciones,
                Estado = inspeccion.Estado,

                UsuarioCreacionID = inspeccion.UsuarioCreacionID,
                FechaCreacion = inspeccion.FechaCreacion,
                UsuarioModificacionID = inspeccion.UsuarioModificacionID,
                FechaModificacion = inspeccion.FechaModificacion,

                Historial = inspeccion.Historial
                    .OrderByDescending(x => x.FechaMovimiento)
                    .Select(x => new CalidadHistorialItemViewModel
                    {
                        HistorialID = x.HistorialID,
                        Movimiento = x.Movimiento,
                        EstadoAnterior = x.EstadoAnterior,
                        EstadoNuevo = x.EstadoNuevo,
                        ResultadoCalidad = x.ResultadoCalidad,
                        Etiqueta = x.Etiqueta,
                        Comentario = x.Comentario,
                        UsuarioID = x.UsuarioID,
                        FechaMovimiento = x.FechaMovimiento
                    })
                    .ToList()
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Liberar(int id)
        {
            var inspeccion = await _context.CalidadInspecciones
                .FirstOrDefaultAsync(x => x.InspeccionID == id);

            if (inspeccion == null)
                return NotFound();

            string estadoAnterior = inspeccion.Estado;
            int? usuarioId = ObtenerUsuarioIdActual();

            inspeccion.ResultadoCalidad = "VERDE";
            inspeccion.Etiqueta = "VERDE";
            inspeccion.Liberado = true;
            inspeccion.RequiereGPI2 = false;
            inspeccion.EnContencion = false;
            inspeccion.EsScrap = false;
            inspeccion.Estado = "LIBERADA";

            MarcarModificacion(inspeccion, usuarioId);

            await _context.SaveChangesAsync();

            await RegistrarHistorialAsync(
                inspeccion.InspeccionID,
                "LIBERACION",
                estadoAnterior,
                inspeccion.Estado,
                inspeccion.ResultadoCalidad,
                inspeccion.Etiqueta,
                "Proceso liberado por Calidad.",
                usuarioId
            );

            TempData["Mensaje"] = "Proceso liberado correctamente.";

            return RedirectToAction(nameof(Detalle), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EnviarGPI2(int id)
        {
            var inspeccion = await _context.CalidadInspecciones
                .FirstOrDefaultAsync(x => x.InspeccionID == id);

            if (inspeccion == null)
                return NotFound();

            string estadoAnterior = inspeccion.Estado;
            int? usuarioId = ObtenerUsuarioIdActual();

            inspeccion.ResultadoCalidad = "AMARILLO";
            inspeccion.Etiqueta = "AMARILLA";
            inspeccion.Liberado = false;
            inspeccion.RequiereGPI2 = true;
            inspeccion.EnContencion = false;
            inspeccion.EsScrap = false;
            inspeccion.Estado = "GPI2";

            MarcarModificacion(inspeccion, usuarioId);

            await _context.SaveChangesAsync();

            await RegistrarHistorialAsync(
                inspeccion.InspeccionID,
                "ENVIO_GPI2",
                estadoAnterior,
                inspeccion.Estado,
                inspeccion.ResultadoCalidad,
                inspeccion.Etiqueta,
                "Inspección enviada a GPI2 / segunda revisión.",
                usuarioId
            );

            TempData["Mensaje"] = "Inspección enviada a GPI2.";

            return RedirectToAction(nameof(Detalle), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EnviarContencion(int id)
        {
            var inspeccion = await _context.CalidadInspecciones
                .FirstOrDefaultAsync(x => x.InspeccionID == id);

            if (inspeccion == null)
                return NotFound();

            string estadoAnterior = inspeccion.Estado;
            int? usuarioId = ObtenerUsuarioIdActual();

            inspeccion.Liberado = false;
            inspeccion.EnContencion = true;
            inspeccion.Estado = "CONTENCION";

            MarcarModificacion(inspeccion, usuarioId);

            await _context.SaveChangesAsync();

            await RegistrarHistorialAsync(
                inspeccion.InspeccionID,
                "CONTENCION",
                estadoAnterior,
                inspeccion.Estado,
                inspeccion.ResultadoCalidad,
                inspeccion.Etiqueta,
                "Material enviado a contención.",
                usuarioId
            );

            TempData["Mensaje"] = "Material enviado a contención.";

            return RedirectToAction(nameof(Detalle), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarcarScrap(int id)
        {
            var inspeccion = await _context.CalidadInspecciones
                .FirstOrDefaultAsync(x => x.InspeccionID == id);

            if (inspeccion == null)
                return NotFound();

            string estadoAnterior = inspeccion.Estado;
            int? usuarioId = ObtenerUsuarioIdActual();

            inspeccion.ResultadoCalidad = "ROJO";
            inspeccion.Etiqueta = "ROJA";
            inspeccion.Liberado = false;
            inspeccion.RequiereGPI2 = false;
            inspeccion.EnContencion = true;
            inspeccion.EsScrap = true;
            inspeccion.Estado = "SCRAP";

            MarcarModificacion(inspeccion, usuarioId);

            await _context.SaveChangesAsync();

            await RegistrarHistorialAsync(
                inspeccion.InspeccionID,
                "SCRAP",
                estadoAnterior,
                inspeccion.Estado,
                inspeccion.ResultadoCalidad,
                inspeccion.Etiqueta,
                "Material marcado como scrap.",
                usuarioId
            );

            TempData["Mensaje"] = "Material marcado como scrap.";

            return RedirectToAction(nameof(Detalle), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Detener(int id)
        {
            var inspeccion = await _context.CalidadInspecciones
                .FirstOrDefaultAsync(x => x.InspeccionID == id);

            if (inspeccion == null)
                return NotFound();

            string estadoAnterior = inspeccion.Estado;
            int? usuarioId = ObtenerUsuarioIdActual();

            inspeccion.Liberado = false;
            inspeccion.Estado = "DETENIDA";

            MarcarModificacion(inspeccion, usuarioId);

            await _context.SaveChangesAsync();

            await RegistrarHistorialAsync(
                inspeccion.InspeccionID,
                "DETENCION",
                estadoAnterior,
                inspeccion.Estado,
                inspeccion.ResultadoCalidad,
                inspeccion.Etiqueta,
                "Proceso detenido / pendiente de corrección.",
                usuarioId
            );

            TempData["Mensaje"] = "Inspección detenida correctamente.";

            return RedirectToAction(nameof(Detalle), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cerrar(int id)
        {
            var inspeccion = await _context.CalidadInspecciones
                .FirstOrDefaultAsync(x => x.InspeccionID == id);

            if (inspeccion == null)
                return NotFound();

            string estadoAnterior = inspeccion.Estado;
            int? usuarioId = ObtenerUsuarioIdActual();

            inspeccion.Estado = "CERRADA";

            MarcarModificacion(inspeccion, usuarioId);

            await _context.SaveChangesAsync();

            await RegistrarHistorialAsync(
                inspeccion.InspeccionID,
                "CIERRE",
                estadoAnterior,
                inspeccion.Estado,
                inspeccion.ResultadoCalidad,
                inspeccion.Etiqueta,
                "Registro de calidad cerrado.",
                usuarioId
            );

            TempData["Mensaje"] = "Registro cerrado correctamente.";

            return RedirectToAction(nameof(Detalle), new { id });
        }

        private async Task RegistrarHistorialAsync(
            int inspeccionId,
            string movimiento,
            string? estadoAnterior,
            string? estadoNuevo,
            string? resultadoCalidad,
            string? etiqueta,
            string? comentario,
            int? usuarioId)
        {
            var historial = new CalidadInspeccionHistorial
            {
                InspeccionID = inspeccionId,
                Movimiento = movimiento,
                EstadoAnterior = estadoAnterior,
                EstadoNuevo = estadoNuevo,
                ResultadoCalidad = resultadoCalidad,
                Etiqueta = etiqueta,
                Comentario = comentario,
                UsuarioID = usuarioId,
                FechaMovimiento = DateTime.Now
            };

            _context.CalidadInspeccionHistorial.Add(historial);
            await _context.SaveChangesAsync();
        }

        private static decimal CalcularPendiente(decimal total, decimal revisada)
        {
            decimal pendiente = total - revisada;
            return pendiente < 0 ? 0 : pendiente;
        }

        private static void NormalizarModelo(CalidadFormViewModel model)
        {
            model.CodigoBarras = model.CodigoBarras?.Trim();
            model.OrdenTrabajo = model.OrdenTrabajo?.Trim();
            model.NumeroParte = model.NumeroParte?.Trim();
            model.Material = model.Material?.Trim();
            model.Proceso = model.Proceso?.Trim();
            model.Maquina = model.Maquina?.Trim();
            model.ResultadoCalidad = model.ResultadoCalidad?.Trim().ToUpperInvariant();
            model.Observaciones = model.Observaciones?.Trim();

            if (model.CantidadTotal < 0)
                model.CantidadTotal = 0;

            if (model.CantidadRevisada < 0)
                model.CantidadRevisada = 0;
        }

        private static void AplicarResultado(CalidadInspeccion inspeccion)
        {
            inspeccion.Liberado = false;
            inspeccion.RequiereGPI2 = false;
            inspeccion.EnContencion = false;
            inspeccion.EsScrap = false;

            if (string.IsNullOrWhiteSpace(inspeccion.ResultadoCalidad))
            {
                inspeccion.Etiqueta = null;
                inspeccion.Estado = "ABIERTA";
                return;
            }

            switch (inspeccion.ResultadoCalidad.ToUpperInvariant())
            {
                case "VERDE":
                    inspeccion.Etiqueta = "VERDE";
                    inspeccion.Liberado = true;
                    inspeccion.Estado = "LIBERADA";
                    break;

                case "AMARILLO":
                    inspeccion.Etiqueta = "AMARILLA";
                    inspeccion.RequiereGPI2 = true;
                    inspeccion.Estado = "GPI2";
                    break;

                case "ROJO":
                    inspeccion.Etiqueta = "ROJA";
                    inspeccion.EsScrap = true;
                    inspeccion.EnContencion = true;
                    inspeccion.Estado = "SCRAP";
                    break;

                default:
                    inspeccion.ResultadoCalidad = null;
                    inspeccion.Etiqueta = null;
                    inspeccion.Estado = "ABIERTA";
                    break;
            }
        }

        private static void MarcarModificacion(
            CalidadInspeccion inspeccion,
            int? usuarioId)
        {
            inspeccion.UsuarioModificacionID = usuarioId;
            inspeccion.FechaModificacion = DateTime.Now;
        }

        private int? ObtenerUsuarioIdActual()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier);

            if (claim != null && int.TryParse(claim.Value, out int usuarioId))
                return usuarioId;

            return null;
        }
    }
}