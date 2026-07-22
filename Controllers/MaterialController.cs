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
    public class MaterialController : Controller
    {
        private readonly ApplicationDbContext _context;

        public MaterialController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET:
        // /Material
        // /Material/Index
        [HttpGet]
        [Route("Material")]
        [Route("Material/Index")]
        [AutorizarAccion("Ver Material", "Ver")]
        public async Task<IActionResult> Index(string? busqueda, string? estadoFiltro)
        {
            ViewData["Title"] = "Alta de Materiales";

            var query = _context.ERPMateriales
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(busqueda))
            {
                var termino = busqueda.Trim();

                query = query.Where(m =>
                    m.Codigo.Contains(termino) ||
                    m.Nombre.Contains(termino) ||
                    (m.TipoMaterial != null && m.TipoMaterial.Contains(termino)) ||
                    (m.UnidadDefault != null && m.UnidadDefault.Contains(termino)) ||
                    (m.Proveedor != null && m.Proveedor.Contains(termino)) ||
                    (m.MonedaCosto != null && m.MonedaCosto.Contains(termino)) ||
                    (m.FuenteCosto != null && m.FuenteCosto.Contains(termino)) ||
                    (m.ClaveCostoOrigen != null && m.ClaveCostoOrigen.Contains(termino)));
            }

            estadoFiltro = (estadoFiltro ?? string.Empty).Trim().ToLowerInvariant();

            if (estadoFiltro == "activos")
            {
                query = query.Where(m => m.Activo);
            }
            else if (estadoFiltro == "inactivos")
            {
                query = query.Where(m => !m.Activo);
            }
            else if (estadoFiltro == "stock")
            {
                query = query.Where(m => m.StockConfigurado);
            }
            else if (estadoFiltro == "costo")
            {
                query = query.Where(m => m.CostoUnitario != null);
            }
            else if (estadoFiltro == "lote")
            {
                query = query.Where(m => m.RequiereLote);
            }

            var materiales = await query
                .OrderByDescending(m => m.Activo)
                .ThenBy(m => m.Codigo)
                .Select(m => new MaterialListadoItemViewModel
                {
                    MaterialID = m.MaterialID,
                    Codigo = m.Codigo,
                    Nombre = m.Nombre,
                    TipoMaterial = m.TipoMaterial,
                    UnidadDefault = m.UnidadDefault,
                    Proveedor = m.Proveedor,
                    RequiereLote = m.RequiereLote,
                    Activo = m.Activo,
                    StockMinimo = m.StockMinimo,
                    StockAviso = m.StockAviso,
                    StockConfigurado = m.StockConfigurado,
                    CostoUnitario = m.CostoUnitario,
                    MonedaCosto = m.MonedaCosto,
                    UnidadCosto = m.UnidadCosto,
                    FuenteCosto = m.FuenteCosto,
                    FechaCosto = m.FechaCosto
                })
                .ToListAsync();

            var model = new MaterialIndexViewModel
            {
                Busqueda = busqueda,
                EstadoFiltro = estadoFiltro,
                TotalMostrados = materiales.Count,
                TotalActivos = await _context.ERPMateriales.CountAsync(m => m.Activo),
                TotalInactivos = await _context.ERPMateriales.CountAsync(m => !m.Activo),
                TotalStockConfigurado = await _context.ERPMateriales.CountAsync(m => m.StockConfigurado),
                TotalConCosto = await _context.ERPMateriales.CountAsync(m => m.CostoUnitario != null),
                Materiales = materiales
            };

            return View(model);
        }

        // GET:
        // /Material/Crear
        [HttpGet]
        [Route("Material/Crear")]
        [AutorizarAccion("Crear Material", "Crear")]
        public IActionResult Crear()
        {
            ViewData["Title"] = "Crear Material";

            var model = new MaterialFormViewModel
            {
                Activo = true,
                RequiereLote = false,
                StockConfigurado = false,
                StockMinimo = 0,
                StockAviso = 0,
                UnidadDefault = "KG",
                MonedaCosto = "MXN",
                UnidadCosto = "KG"
            };

            return View(model);
        }

        // POST:
        // /Material/Crear
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Route("Material/Crear")]
        [AutorizarAccion("Crear Material", "Crear")]
        public async Task<IActionResult> Crear(MaterialFormViewModel model)
        {
            ViewData["Title"] = "Crear Material";

            NormalizarModelo(model);

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var existeCodigo = await _context.ERPMateriales
                .AnyAsync(m => m.Codigo == model.Codigo);

            if (existeCodigo)
            {
                ModelState.AddModelError(nameof(model.Codigo), "Ya existe un material con este código.");
                return View(model);
            }

            var creadoPor = ObtenerUsuarioActual();

            var material = new ERPMaterial
            {
                Codigo = model.Codigo,
                Nombre = model.Nombre,
                TipoMaterial = model.TipoMaterial,
                UnidadDefault = model.UnidadDefault,
                Proveedor = model.Proveedor,
                RequiereLote = model.RequiereLote,
                FechaCreacion = DateTime.Now,
                CreadoPor = creadoPor,
                Activo = model.Activo,

                StockMinimo = model.StockMinimo,
                StockAviso = model.StockAviso,
                StockConfigurado = model.StockConfigurado,

                CostoUnitario = model.CostoUnitario,
                MonedaCosto = model.MonedaCosto,
                UnidadCosto = model.UnidadCosto,
                FuenteCosto = model.FuenteCosto,
                ClaveCostoOrigen = model.ClaveCostoOrigen,
                DescripcionCostoOrigen = model.DescripcionCostoOrigen,
                FechaCosto = model.FechaCosto
            };

            _context.ERPMateriales.Add(material);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "El material fue registrado correctamente.";

            return RedirectToAction(nameof(Index));
        }

        // GET:
        // /Material/Editar/5
        // /Material/Editar?id=5
        [HttpGet]
        [Route("Material/Editar")]
        [Route("Material/Editar/{id:int}")]
        [AutorizarAccion("Editar Material", "Editar")]
        public async Task<IActionResult> Editar(int id)
        {
            ViewData["Title"] = "Editar Material";

            if (id <= 0)
            {
                return BadRequest();
            }

            var material = await _context.ERPMateriales
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.MaterialID == id);

            if (material == null)
            {
                return NotFound();
            }

            var model = new MaterialFormViewModel
            {
                MaterialID = material.MaterialID,
                Codigo = material.Codigo,
                Nombre = material.Nombre,
                TipoMaterial = material.TipoMaterial,
                UnidadDefault = material.UnidadDefault,
                Proveedor = material.Proveedor,
                RequiereLote = material.RequiereLote,
                Activo = material.Activo,

                StockMinimo = material.StockMinimo,
                StockAviso = material.StockAviso,
                StockConfigurado = material.StockConfigurado,

                CostoUnitario = material.CostoUnitario,
                MonedaCosto = material.MonedaCosto,
                UnidadCosto = material.UnidadCosto,
                FuenteCosto = material.FuenteCosto,
                ClaveCostoOrigen = material.ClaveCostoOrigen,
                DescripcionCostoOrigen = material.DescripcionCostoOrigen,
                FechaCosto = material.FechaCosto
            };

            return View(model);
        }

        // POST:
        // /Material/Editar/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Route("Material/Editar")]
        [Route("Material/Editar/{id:int}")]
        [AutorizarAccion("Editar Material", "Editar")]
        public async Task<IActionResult> Editar(int id, MaterialFormViewModel model)
        {
            ViewData["Title"] = "Editar Material";

            if (id <= 0)
            {
                return BadRequest();
            }

            if (!model.MaterialID.HasValue || model.MaterialID.Value != id)
            {
                return BadRequest();
            }

            NormalizarModelo(model);

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var material = await _context.ERPMateriales
                .FirstOrDefaultAsync(m => m.MaterialID == id);

            if (material == null)
            {
                return NotFound();
            }

            var existeCodigo = await _context.ERPMateriales
                .AnyAsync(m => m.MaterialID != id && m.Codigo == model.Codigo);

            if (existeCodigo)
            {
                ModelState.AddModelError(nameof(model.Codigo), "Ya existe otro material con este código.");
                return View(model);
            }

            var actualizadoPor = ObtenerUsuarioActual();

            material.Codigo = model.Codigo;
            material.Nombre = model.Nombre;
            material.TipoMaterial = model.TipoMaterial;
            material.UnidadDefault = model.UnidadDefault;
            material.Proveedor = model.Proveedor;
            material.RequiereLote = model.RequiereLote;
            material.Activo = model.Activo;

            material.StockMinimo = model.StockMinimo;
            material.StockAviso = model.StockAviso;
            material.StockConfigurado = model.StockConfigurado;

            material.CostoUnitario = model.CostoUnitario;
            material.MonedaCosto = model.MonedaCosto;
            material.UnidadCosto = model.UnidadCosto;
            material.FuenteCosto = model.FuenteCosto;
            material.ClaveCostoOrigen = model.ClaveCostoOrigen;
            material.DescripcionCostoOrigen = model.DescripcionCostoOrigen;
            material.FechaCosto = model.FechaCosto;

            material.ActualizadoPor = actualizadoPor;
            material.FechaModificacion = DateTime.Now;

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "El material fue actualizado correctamente.";

            return RedirectToAction(nameof(Index));
        }

        // POST:
        // /Material/CambiarEstado/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Route("Material/CambiarEstado/{id:int}")]
        [AutorizarAccion("Eliminacion de Material", "Eliminar")]
        public async Task<IActionResult> CambiarEstado(int id)
        {
            if (id <= 0)
            {
                return BadRequest();
            }

            var material = await _context.ERPMateriales
                .FirstOrDefaultAsync(m => m.MaterialID == id);

            if (material == null)
            {
                return NotFound();
            }

            material.Activo = !material.Activo;
            material.ActualizadoPor = ObtenerUsuarioActual();
            material.FechaModificacion = DateTime.Now;

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = material.Activo
                ? "El material fue reactivado correctamente."
                : "El material fue desactivado correctamente.";

            return RedirectToAction(nameof(Index));
        }

        private static void NormalizarModelo(MaterialFormViewModel model)
        {
            model.Codigo = (model.Codigo ?? string.Empty).Trim().ToUpperInvariant();
            model.Nombre = (model.Nombre ?? string.Empty).Trim();
            model.UnidadDefault = (model.UnidadDefault ?? string.Empty).Trim().ToUpperInvariant();

            model.TipoMaterial = string.IsNullOrWhiteSpace(model.TipoMaterial)
                ? null
                : model.TipoMaterial.Trim();

            model.Proveedor = string.IsNullOrWhiteSpace(model.Proveedor)
                ? null
                : model.Proveedor.Trim();

            model.MonedaCosto = string.IsNullOrWhiteSpace(model.MonedaCosto)
                ? null
                : model.MonedaCosto.Trim().ToUpperInvariant();

            model.UnidadCosto = string.IsNullOrWhiteSpace(model.UnidadCosto)
                ? null
                : model.UnidadCosto.Trim().ToUpperInvariant();

            model.FuenteCosto = string.IsNullOrWhiteSpace(model.FuenteCosto)
                ? null
                : model.FuenteCosto.Trim();

            model.ClaveCostoOrigen = string.IsNullOrWhiteSpace(model.ClaveCostoOrigen)
                ? null
                : model.ClaveCostoOrigen.Trim();

            model.DescripcionCostoOrigen = string.IsNullOrWhiteSpace(model.DescripcionCostoOrigen)
                ? null
                : model.DescripcionCostoOrigen.Trim();

            if (model.StockMinimo < 0)
            {
                model.StockMinimo = 0;
            }

            if (model.StockAviso < 0)
            {
                model.StockAviso = 0;
            }

            if (model.CostoUnitario.HasValue && model.CostoUnitario.Value < 0)
            {
                model.CostoUnitario = null;
            }
        }

        private string ObtenerUsuarioActual()
        {
            var sessionUser = HttpContext.Session.GetString("NombreUsuario");

            if (!string.IsNullOrWhiteSpace(sessionUser))
            {
                return sessionUser;
            }

            var userName = User?.Identity?.Name;

            if (!string.IsNullOrWhiteSpace(userName))
            {
                return userName;
            }

            var usuarioID = HttpContext.Session.GetInt32("UsuarioID");

            if (usuarioID.HasValue)
            {
                return $"UsuarioID:{usuarioID.Value}";
            }

            return "Sistema";
        }
    }
}