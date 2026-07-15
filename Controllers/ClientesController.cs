using ERP.NSQuell.Models;
using ERP.NSQuell.Models.ViewModels;
using ERP.NSQuell.Seguridad;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers
{
    public class ClientesController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ClientesController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET:
        // /Clientes
        // /Clientes/Index
        // /Usuarios/Clientes
        // /Usuarios/Clientes/Index
        [HttpGet]
        [Route("Clientes")]
        [Route("Clientes/Index")]
        [Route("Usuarios/Clientes")]
        [Route("Usuarios/Clientes/Index")]
        [AutorizarAccion("Ver Cliente", "Ver")]
        public async Task<IActionResult> Index(string? busqueda, string? estadoFiltro)
        {
            ViewData["Title"] = "Alta de Clientes";

            var query = _context.ERPClientes
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(busqueda))
            {
                var termino = busqueda.Trim();

                query = query.Where(c =>
                    c.Codigo.Contains(termino) ||
                    c.Nombre.Contains(termino) ||
                    (c.RFC != null && c.RFC.Contains(termino)) ||
                    (c.Contacto != null && c.Contacto.Contains(termino)));
            }

            estadoFiltro = (estadoFiltro ?? string.Empty).Trim().ToLowerInvariant();

            if (estadoFiltro == "activos")
            {
                query = query.Where(c => c.Activo);
            }
            else if (estadoFiltro == "inactivos")
            {
                query = query.Where(c => !c.Activo);
            }

            var clientes = await query
                .OrderByDescending(c => c.Activo)
                .ThenBy(c => c.Nombre)
                .Select(c => new ClienteListadoItemViewModel
                {
                    ClienteID = c.ClienteID,
                    Codigo = c.Codigo,
                    Nombre = c.Nombre,
                    RFC = c.RFC,
                    Contacto = c.Contacto,
                    Activo = c.Activo
                })
                .ToListAsync();

            var model = new ClienteIndexViewModel
            {
                Busqueda = busqueda,
                EstadoFiltro = estadoFiltro,
                TotalMostrados = clientes.Count,
                TotalActivos = await _context.ERPClientes.CountAsync(c => c.Activo),
                TotalInactivos = await _context.ERPClientes.CountAsync(c => !c.Activo),
                Clientes = clientes
            };

            return View(model);
        }

        // GET:
        // /Clientes/Crear
        // /Usuarios/Clientes/Crear
        [HttpGet]
        [Route("Clientes/Crear")]
        [Route("Usuarios/Clientes/Crear")]
        [AutorizarAccion("Crear Cliente", "Crear")]
        public IActionResult Crear()
        {
            ViewData["Title"] = "Crear Cliente";

            var model = new ClienteFormViewModel
            {
                Activo = true
            };

            return View(model);
        }

        // POST:
        // /Clientes/Crear
        // /Usuarios/Clientes/Crear
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Route("Clientes/Crear")]
        [Route("Usuarios/Clientes/Crear")]
        [AutorizarAccion("Crear Cliente", "Crear")]
        public async Task<IActionResult> Crear(ClienteFormViewModel model)
        {
            ViewData["Title"] = "Crear Cliente";

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            model.Codigo = model.Codigo.Trim();
            model.Nombre = model.Nombre.Trim();
            model.RFC = string.IsNullOrWhiteSpace(model.RFC) ? null : model.RFC.Trim();
            model.Contacto = string.IsNullOrWhiteSpace(model.Contacto) ? null : model.Contacto.Trim();

            var existeCodigo = await _context.ERPClientes
                .AnyAsync(c => c.Codigo == model.Codigo);

            if (existeCodigo)
            {
                ModelState.AddModelError(nameof(model.Codigo), "Ya existe un cliente con este código.");
                return View(model);
            }

            var usuarioID = HttpContext.Session.GetInt32("UsuarioID");

            var cliente = new ERPCliente
            {
                Codigo = model.Codigo,
                Nombre = model.Nombre,
                RFC = model.RFC,
                Contacto = model.Contacto,
                Activo = model.Activo,
                UsuarioCreacionID = usuarioID,
                FechaCreacion = DateTime.Now
            };

            _context.ERPClientes.Add(cliente);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "El cliente fue registrado correctamente.";

            return RedirectToAction(nameof(Index));
        }

        // GET:
        // /Clientes/Editar/5
        // /Clientes/Editar?id=5
        // /Usuarios/Clientes/Editar/5
        // /Usuarios/Clientes/Editar?id=5
        [HttpGet]
        [Route("Clientes/Editar")]
        [Route("Clientes/Editar/{id:int}")]
        [Route("Usuarios/Clientes/Editar")]
        [Route("Usuarios/Clientes/Editar/{id:int}")]
        [AutorizarAccion("Editar Cliente", "Editar")]
        public async Task<IActionResult> Editar(int id)
        {
            ViewData["Title"] = "Editar Cliente";

            if (id <= 0)
            {
                return BadRequest();
            }

            var cliente = await _context.ERPClientes
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.ClienteID == id);

            if (cliente == null)
            {
                return NotFound();
            }

            var model = new ClienteFormViewModel
            {
                ClienteID = cliente.ClienteID,
                Codigo = cliente.Codigo,
                Nombre = cliente.Nombre,
                RFC = cliente.RFC,
                Contacto = cliente.Contacto,
                Activo = cliente.Activo
            };

            return View(model);
        }

        // POST:
        // /Clientes/Editar/5
        // /Usuarios/Clientes/Editar/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Route("Clientes/Editar")]
        [Route("Clientes/Editar/{id:int}")]
        [Route("Usuarios/Clientes/Editar")]
        [Route("Usuarios/Clientes/Editar/{id:int}")]
        [AutorizarAccion("Editar Cliente", "Editar")]
        public async Task<IActionResult> Editar(int id, ClienteFormViewModel model)
        {
            ViewData["Title"] = "Editar Cliente";

            if (id <= 0)
            {
                return BadRequest();
            }

            if (!model.ClienteID.HasValue || model.ClienteID.Value != id)
            {
                return BadRequest();
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            model.Codigo = model.Codigo.Trim();
            model.Nombre = model.Nombre.Trim();
            model.RFC = string.IsNullOrWhiteSpace(model.RFC) ? null : model.RFC.Trim();
            model.Contacto = string.IsNullOrWhiteSpace(model.Contacto) ? null : model.Contacto.Trim();

            var cliente = await _context.ERPClientes
                .FirstOrDefaultAsync(c => c.ClienteID == id);

            if (cliente == null)
            {
                return NotFound();
            }

            var existeCodigo = await _context.ERPClientes
                .AnyAsync(c => c.ClienteID != id && c.Codigo == model.Codigo);

            if (existeCodigo)
            {
                ModelState.AddModelError(nameof(model.Codigo), "Ya existe otro cliente con este código.");
                return View(model);
            }

            var usuarioID = HttpContext.Session.GetInt32("UsuarioID");

            cliente.Codigo = model.Codigo;
            cliente.Nombre = model.Nombre;
            cliente.RFC = model.RFC;
            cliente.Contacto = model.Contacto;
            cliente.Activo = model.Activo;
            cliente.UsuarioModificacionID = usuarioID;
            cliente.FechaModificacion = DateTime.Now;

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "El cliente fue actualizado correctamente.";

            return RedirectToAction(nameof(Index));
        }

        // POST:
        // /Clientes/CambiarEstado/5
        // /Usuarios/Clientes/CambiarEstado/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Route("Clientes/CambiarEstado/{id:int}")]
        [Route("Usuarios/Clientes/CambiarEstado/{id:int}")]
        [AutorizarAccion("Eliminar Cliente", "Eliminar")]
        public async Task<IActionResult> CambiarEstado(int id)
        {
            if (id <= 0)
            {
                return BadRequest();
            }

            var cliente = await _context.ERPClientes
                .FirstOrDefaultAsync(c => c.ClienteID == id);

            if (cliente == null)
            {
                return NotFound();
            }

            var usuarioID = HttpContext.Session.GetInt32("UsuarioID");

            cliente.Activo = !cliente.Activo;
            cliente.UsuarioModificacionID = usuarioID;
            cliente.FechaModificacion = DateTime.Now;

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = cliente.Activo
                ? "El cliente fue reactivado correctamente."
                : "El cliente fue desactivado correctamente.";

            return RedirectToAction(nameof(Index));
        }
    }
}