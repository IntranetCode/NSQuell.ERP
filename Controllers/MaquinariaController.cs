using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ERP.NSQuell.Models;
using ERP.NSQuell.Models.ViewModels;
using ERP.NSQuell.Seguridad;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers
{
    public class MaquinariaController : Controller
    {
        private readonly ApplicationDbContext _context;

        public MaquinariaController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: /Usuarios/Maquinaria
        // GET: /Usuarios/Maquinaria/Index
        // GET: /Maquinaria
        // GET: /Maquinaria/Index
        [HttpGet]
        [Route("Usuarios/Maquinaria")]
        [Route("Usuarios/Maquinaria/Index")]
        [Route("Maquinaria")]
        [Route("Maquinaria/Index")]
        [AutorizarAccion("Ver Usuarios", "Ver")]
        public IActionResult Index()
        {
            ViewData["Title"] = "Altas de Maquinaria";

            return View("~/Views/Maquinaria/Index.cshtml");
        }

        // Compatibilidad con enlaces viejos que todavía llamen a Maquinaria().
        [HttpGet]
        [Route("Usuarios/Maquinaria/Maquinaria")]
        [Route("Maquinaria/Maquinaria")]
        [AutorizarAccion("Ver Usuarios", "Ver")]
        public IActionResult Maquinaria()
        {
            return RedirectToAction(nameof(Index));
        }

        // GET: /Usuarios/Maquinaria/Crear
        // GET: /Maquinaria/Crear
        [HttpGet]
        [Route("Usuarios/Maquinaria/Crear")]
        [Route("Maquinaria/Crear")]
        [AutorizarAccion("Ver Usuarios", "Ver")]
        public async Task<IActionResult> CrearMaquinaria()
        {
            ViewData["Title"] = "Agregar máquina";

            var model = new MaquinariaFormViewModel
            {
                Activo = true,
                Area = "Inyección",
                EstadoOperativo = "Operativa"
            };

            await CargarMaquinasSustitutasDisponiblesAsync(model, null);

            return View("~/Views/Maquinaria/Crear.cshtml", model);
        }

        // POST: /Usuarios/Maquinaria/Crear
        // POST: /Maquinaria/Crear
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Route("Usuarios/Maquinaria/Crear")]
        [Route("Maquinaria/Crear")]
        [AutorizarAccion("Ver Usuarios", "Ver")]
        public async Task<IActionResult> CrearMaquinaria(MaquinariaFormViewModel model)
        {
            NormalizarSeleccionSustitutas(model);

            if (!ModelState.IsValid)
            {
                ViewData["Title"] = "Agregar máquina";
                await CargarMaquinasSustitutasDisponiblesAsync(model, null);
                return View("~/Views/Maquinaria/Crear.cshtml", model);
            }

            var codigo = model.Codigo.Trim();

            var existeCodigo = await _context.ERPMaquinas
                .AsNoTracking()
                .AnyAsync(x => x.Codigo != null && x.Codigo.Trim() == codigo);

            if (existeCodigo)
            {
                ModelState.AddModelError(nameof(model.Codigo), "Ya existe una máquina registrada con este código.");
                ViewData["Title"] = "Agregar máquina";
                await CargarMaquinasSustitutasDisponiblesAsync(model, null);
                return View("~/Views/Maquinaria/Crear.cshtml", model);
            }

            var maquina = new ERPMaquina
            {
                Codigo = codigo,
                Nombre = model.Nombre.Trim(),
                Area = model.Area.Trim(),
                EstadoOperativo = model.EstadoOperativo.Trim(),
                Descripcion = string.IsNullOrWhiteSpace(model.Descripcion) ? null : model.Descripcion.Trim(),
                Activo = model.Activo,
                FechaCreacion = DateTime.Now,
                FechaModificacion = null
            };

            _context.ERPMaquinas.Add(maquina);
            await _context.SaveChangesAsync();

            await GuardarMaquinasSustitutasAsync(
                maquina.MaquinaID,
                model.MaquinasSustitutasSeleccionadas);

            TempData["SuccessMessage"] = "La máquina fue registrada correctamente.";
            return RedirectToAction(nameof(Index));
        }

        // GET: /Usuarios/Maquinaria/Editar/5
        // GET: /Maquinaria/Editar/5
        [HttpGet]
        [Route("Usuarios/Maquinaria/Editar/{id:int}")]
        [Route("Maquinaria/Editar/{id:int}")]
        [AutorizarAccion("Ver Usuarios", "Ver")]
        public async Task<IActionResult> EditarMaquinaria(int id)
        {
            var maquina = await _context.ERPMaquinas
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.MaquinaID == id);

            if (maquina == null)
                return NotFound();

            var sustitutasSeleccionadas = await _context.ERPMaquinasSustitutas
                .AsNoTracking()
                .Where(x => x.MaquinaPrincipalID == id && x.Activo)
                .Select(x => x.MaquinaSustitutaID)
                .ToListAsync();

            var model = new MaquinariaFormViewModel
            {
                MaquinaID = maquina.MaquinaID,
                Codigo = maquina.Codigo ?? string.Empty,
                Nombre = maquina.Nombre ?? string.Empty,
                Area = maquina.Area ?? string.Empty,
                EstadoOperativo = maquina.EstadoOperativo ?? "Operativa",
                Descripcion = maquina.Descripcion,
                Activo = maquina.Activo,
                FechaCreacion = maquina.FechaCreacion,
                FechaModificacion = maquina.FechaModificacion,
                MaquinasSustitutasSeleccionadas = sustitutasSeleccionadas
            };

            await CargarMaquinasSustitutasDisponiblesAsync(model, id);

            ViewData["Title"] = "Editar máquina";
            return View("~/Views/Maquinaria/Editar.cshtml", model);
        }

        // POST: /Usuarios/Maquinaria/Editar/5
        // POST: /Maquinaria/Editar/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Route("Usuarios/Maquinaria/Editar/{id:int}")]
        [Route("Maquinaria/Editar/{id:int}")]
        [AutorizarAccion("Ver Usuarios", "Ver")]
        public async Task<IActionResult> EditarMaquinaria(int id, MaquinariaFormViewModel model)
        {
            if (!model.MaquinaID.HasValue || id != model.MaquinaID.Value)
                return BadRequest();

            NormalizarSeleccionSustitutas(model, id);

            if (!ModelState.IsValid)
            {
                ViewData["Title"] = "Editar máquina";
                await CargarMaquinasSustitutasDisponiblesAsync(model, id);
                return View("~/Views/Maquinaria/Editar.cshtml", model);
            }

            var maquina = await _context.ERPMaquinas
                .FirstOrDefaultAsync(x => x.MaquinaID == id);

            if (maquina == null)
                return NotFound();

            var codigo = model.Codigo.Trim();

            var existeCodigo = await _context.ERPMaquinas
                .AsNoTracking()
                .AnyAsync(x => x.MaquinaID != id && x.Codigo != null && x.Codigo.Trim() == codigo);

            if (existeCodigo)
            {
                ModelState.AddModelError(nameof(model.Codigo), "Ya existe otra máquina registrada con este código.");
                ViewData["Title"] = "Editar máquina";
                await CargarMaquinasSustitutasDisponiblesAsync(model, id);
                return View("~/Views/Maquinaria/Editar.cshtml", model);
            }

            maquina.Codigo = codigo;
            maquina.Nombre = model.Nombre.Trim();
            maquina.Area = model.Area.Trim();
            maquina.EstadoOperativo = model.EstadoOperativo.Trim();
            maquina.Descripcion = string.IsNullOrWhiteSpace(model.Descripcion) ? null : model.Descripcion.Trim();
            maquina.Activo = model.Activo;
            maquina.FechaModificacion = DateTime.Now;

            await _context.SaveChangesAsync();

            await GuardarMaquinasSustitutasAsync(
                id,
                model.MaquinasSustitutasSeleccionadas);

            TempData["SuccessMessage"] = "La máquina fue actualizada correctamente.";
            return RedirectToAction(nameof(Index));
        }

        // POST: /Usuarios/Maquinaria/CambiarEstado/5
        // POST: /Maquinaria/CambiarEstado/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Route("Usuarios/Maquinaria/CambiarEstado/{id:int}")]
        [Route("Maquinaria/CambiarEstado/{id:int}")]
        [AutorizarAccion("Ver Usuarios", "Ver")]
        public async Task<IActionResult> CambiarEstadoMaquinaria(int id, bool activo)
        {
            var maquina = await _context.ERPMaquinas
                .FirstOrDefaultAsync(x => x.MaquinaID == id);

            if (maquina == null)
                return NotFound();

            maquina.Activo = activo;
            maquina.FechaModificacion = DateTime.Now;

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = activo
                ? "La máquina fue activada correctamente."
                : "La máquina fue inactivada correctamente.";

            return RedirectToAction(nameof(Index));
        }

        private async Task CargarMaquinasSustitutasDisponiblesAsync(
            MaquinariaFormViewModel model,
            int? maquinaPrincipalId)
        {
            model.MaquinasSustitutasSeleccionadas ??= new List<int>();

            var seleccionadas = model.MaquinasSustitutasSeleccionadas
                .Where(x => x > 0)
                .Distinct()
                .ToHashSet();

            model.MaquinasSustitutasDisponibles = await _context.ERPMaquinas
                .AsNoTracking()
                .Where(x =>
                    x.Activo &&
                    (!maquinaPrincipalId.HasValue || x.MaquinaID != maquinaPrincipalId.Value))
                .OrderBy(x => x.Area)
                .ThenBy(x => x.Codigo)
                .ThenBy(x => x.Nombre)
                .Select(x => new MaquinaSustitutaOpcionViewModel
                {
                    MaquinaID = x.MaquinaID,
                    Codigo = x.Codigo ?? string.Empty,
                    Nombre = x.Nombre ?? string.Empty,
                    Area = x.Area,
                    EstadoOperativo = x.EstadoOperativo,
                    Seleccionada = seleccionadas.Contains(x.MaquinaID)
                })
                .ToListAsync();
        }

        private async Task GuardarMaquinasSustitutasAsync(
            int maquinaPrincipalId,
            List<int>? maquinasSustitutasSeleccionadas)
        {
            var seleccionadas = (maquinasSustitutasSeleccionadas ?? new List<int>())
                .Where(x => x > 0 && x != maquinaPrincipalId)
                .Distinct()
                .ToList();

            var relacionesActuales = await _context.ERPMaquinasSustitutas
                .Where(x => x.MaquinaPrincipalID == maquinaPrincipalId)
                .ToListAsync();

            int? usuarioId = ObtenerUsuarioIdActual();

            foreach (var relacion in relacionesActuales)
            {
                bool debeSeguirActiva = seleccionadas.Contains(relacion.MaquinaSustitutaID);

                relacion.Activo = debeSeguirActiva;

                if (debeSeguirActiva)
                {
                    relacion.Prioridad = seleccionadas.IndexOf(relacion.MaquinaSustitutaID) + 1;
                }
            }

            foreach (var maquinaSustitutaId in seleccionadas)
            {
                var relacionExistente = relacionesActuales
                    .FirstOrDefault(x => x.MaquinaSustitutaID == maquinaSustitutaId);

                if (relacionExistente != null)
                {
                    relacionExistente.Activo = true;
                    relacionExistente.Prioridad = seleccionadas.IndexOf(maquinaSustitutaId) + 1;
                    continue;
                }

                _context.ERPMaquinasSustitutas.Add(new ERPMaquinaSustituta
                {
                    MaquinaPrincipalID = maquinaPrincipalId,
                    MaquinaSustitutaID = maquinaSustitutaId,
                    Prioridad = seleccionadas.IndexOf(maquinaSustitutaId) + 1,
                    Observaciones = null,
                    Activo = true,
                    FechaCreacion = DateTime.Now,
                    UsuarioCreacionID = usuarioId
                });
            }

            await _context.SaveChangesAsync();
        }

        private static void NormalizarSeleccionSustitutas(
            MaquinariaFormViewModel model,
            int? maquinaPrincipalId = null)
        {
            model.MaquinasSustitutasSeleccionadas = (model.MaquinasSustitutasSeleccionadas ?? new List<int>())
                .Where(x => x > 0 && (!maquinaPrincipalId.HasValue || x != maquinaPrincipalId.Value))
                .Distinct()
                .ToList();
        }

        private int? ObtenerUsuarioIdActual()
        {
            int? usuarioSesion = HttpContext.Session.GetInt32("UsuarioID");

            if (usuarioSesion.HasValue && usuarioSesion.Value > 0)
                return usuarioSesion.Value;

            var claim = User.FindFirst(ClaimTypes.NameIdentifier);

            if (claim != null && int.TryParse(claim.Value, out int usuarioId))
                return usuarioId;

            return null;
        }
    }
}
