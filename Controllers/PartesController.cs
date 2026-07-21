using ERP.NSQuell.Models;
using ERP.NSQuell.Models.ViewModels;
using ERP.NSQuell.Seguridad;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ERP.NSQuell.Controllers
{
    public class PartesController : Controller
    {
        private readonly ApplicationDbContext _context;

        public PartesController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: /Partes
        // GET: /Partes/Index
        [HttpGet]
        [Route("Partes")]
        [Route("Partes/Index")]
        [AutorizarAccion("Visualizar Partes", "Ver")]
        public async Task<IActionResult> Index(string? busqueda, string? estadoFiltro)
        {
            ViewData["Title"] = "Alta de Partes";

            busqueda = busqueda?.Trim();
            estadoFiltro = estadoFiltro?.Trim().ToLowerInvariant();

            var partesQuery = _context.ERPPartes
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(busqueda))
            {
                partesQuery = partesQuery.Where(p =>
                    p.NumeroParte.Contains(busqueda) ||
                    (p.ReferenciaSAP != null && p.ReferenciaSAP.Contains(busqueda)) ||
                    p.Descripcion.Contains(busqueda) ||
                    (p.Designacion != null && p.Designacion.Contains(busqueda))
                );
            }

            if (!string.IsNullOrWhiteSpace(estadoFiltro))
            {
                switch (estadoFiltro)
                {
                    case "activas":
                        partesQuery = partesQuery.Where(p => p.Activo);
                        break;

                    case "inactivas":
                        partesQuery = partesQuery.Where(p => !p.Activo);
                        break;

                    case "stock":
                        partesQuery = partesQuery.Where(p => p.StockConfigurado);
                        break;
                }
            }

            var partes = await (
                from p in partesQuery

                join c in _context.ERPClientes.AsNoTracking()
                    on p.ClienteID equals c.ClienteID into clientesJoin
                from c in clientesJoin.DefaultIfEmpty()

                orderby p.NumeroParte

                select new ParteListadoItemViewModel
                {
                    ParteID = p.ParteID,
                    NumeroParte = p.NumeroParte,
                    ReferenciaSAP = p.ReferenciaSAP,
                    Descripcion = p.Descripcion,

                    ClienteNombre = c != null
                        ? c.Codigo + " - " + c.Nombre
                        : "Cliente ID " + p.ClienteID,

                    MaquinaPrincipal = null,
                    MoldePrincipal = null,

                    StockMinimo = p.StockMinimo,
                    StockAviso = p.StockAviso,
                    StockConfigurado = p.StockConfigurado,
                    Activo = p.Activo,

                    TieneDatosTecnicos = _context.ERPParteDatosTecnicos
                        .Any(dt => dt.ParteID == p.ParteID)
                })
                .ToListAsync();

            var totalActivas = await _context.ERPPartes.CountAsync(p => p.Activo);
            var totalInactivas = await _context.ERPPartes.CountAsync(p => !p.Activo);
            var totalStockConfigurado = await _context.ERPPartes.CountAsync(p => p.StockConfigurado);

            var model = new ParteIndexViewModel
            {
                Busqueda = busqueda,
                EstadoFiltro = estadoFiltro,
                TotalMostradas = partes.Count,
                TotalActivas = totalActivas,
                TotalInactivas = totalInactivas,
                TotalStockConfigurado = totalStockConfigurado,
                Partes = partes
            };

            return View(model);
        }

        // GET: /Partes/Crear
        [HttpGet]
        [Route("Partes/Crear")]
        [AutorizarAccion("Crear Partes", "Crear")]
        public async Task<IActionResult> Crear()
        {
            ViewData["Title"] = "Crear Parte";

            var model = new ParteFormViewModel
            {
                Activo = true,
                StockMinimo = 0,
                StockAviso = 0,
                StockConfigurado = false,
                Clientes = await CargarClientesAsync()
            };

            return View(model);
        }

        // POST: /Partes/Crear
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Route("Partes/Crear")]
        [AutorizarAccion("Crear Partes", "Crear")]
        public async Task<IActionResult> Crear(ParteFormViewModel model)
        {
            if (await ExisteNumeroParteAsync(model.NumeroParte, model.ClienteID))
            {
                ModelState.AddModelError(
                    nameof(model.NumeroParte),
                    "Ya existe una parte con este número para el cliente seleccionado.");
            }

            if (!ModelState.IsValid)
            {
                model.Clientes = await CargarClientesAsync(model.ClienteID);
                return View(model);
            }

            var usuarioId = HttpContext.Session.GetInt32("UsuarioID");

            var numeroParte = model.NumeroParte.Trim();
            var referenciaSAP = string.IsNullOrWhiteSpace(model.ReferenciaSAP) ? null : model.ReferenciaSAP.Trim();
            var descripcion = model.Descripcion.Trim();
            var designacion = string.IsNullOrWhiteSpace(model.Designacion) ? null : model.Designacion.Trim();
            var fechaCreacion = DateTime.Now;

            await _context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO dbo.ERP_Partes
                (
                    ClienteID,
                    NumeroParte,
                    ReferenciaSAP,
                    Descripcion,
                    Designacion,
                    RequiereGP12,
                    RequiereCertificado,
                    Activo,
                    UsuarioCreacionID,
                    FechaCreacion,
                    UsuarioModificacionID,
                    FechaModificacion,
                    StockMinimo,
                    StockAviso,
                    StockConfigurado
                )
                VALUES
                (
                    {model.ClienteID},
                    {numeroParte},
                    {referenciaSAP},
                    {descripcion},
                    {designacion},
                    {model.RequiereGP12},
                    {model.RequiereCertificado},
                    {model.Activo},
                    {usuarioId},
                    {fechaCreacion},
                    {null},
                    {null},
                    {model.StockMinimo},
                    {model.StockAviso},
                    {model.StockConfigurado}
                );
            ");

            TempData["SuccessMessage"] = "La parte fue registrada correctamente.";

            return RedirectToAction(nameof(Index));
        }

        // GET: /Partes/Editar/5
        [HttpGet]
        [Route("Partes/Editar/{id:int}")]
        [AutorizarAccion("Editar Partes", "Editar")]
        public async Task<IActionResult> Editar(int id)
        {
            ViewData["Title"] = "Editar Parte";

            if (id <= 0)
            {
                return BadRequest();
            }

            var model = await _context.ERPPartes
                .AsNoTracking()
                .Where(p => p.ParteID == id)
                .Select(p => new ParteFormViewModel
                {
                    ParteID = p.ParteID,
                    ClienteID = p.ClienteID,
                    NumeroParte = p.NumeroParte,
                    ReferenciaSAP = p.ReferenciaSAP,
                    Descripcion = p.Descripcion,
                    Designacion = p.Designacion,

                    RequiereGP12 = p.RequiereGP12,
                    RequiereCertificado = p.RequiereCertificado,

                    Activo = p.Activo,

                    StockMinimo = p.StockMinimo,
                    StockAviso = p.StockAviso,
                    StockConfigurado = p.StockConfigurado
                })
                .FirstOrDefaultAsync();

            if (model == null)
            {
                return NotFound();
            }

            model.Clientes = await CargarClientesAsync(model.ClienteID);

            return View(model);
        }

        // POST: /Partes/Editar/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Route("Partes/Editar/{id:int}")]
        [AutorizarAccion("Editar Partes", "Editar")]
        public async Task<IActionResult> Editar(int id, ParteFormViewModel model)
        {
            if (id <= 0)
            {
                return BadRequest();
            }

            if (!model.ParteID.HasValue || id != model.ParteID.Value)
            {
                return BadRequest();
            }

            if (await ExisteNumeroParteAsync(model.NumeroParte, model.ClienteID, model.ParteID))
            {
                ModelState.AddModelError(
                    nameof(model.NumeroParte),
                    "Ya existe otra parte con este número para el cliente seleccionado.");
            }

            if (!ModelState.IsValid)
            {
                model.Clientes = await CargarClientesAsync(model.ClienteID);
                return View(model);
            }

            var existeParte = await _context.ERPPartes
                .AsNoTracking()
                .AnyAsync(p => p.ParteID == id);

            if (!existeParte)
            {
                return NotFound();
            }

            var usuarioId = HttpContext.Session.GetInt32("UsuarioID");

            var numeroParte = model.NumeroParte.Trim();
            var referenciaSAP = string.IsNullOrWhiteSpace(model.ReferenciaSAP) ? null : model.ReferenciaSAP.Trim();
            var descripcion = model.Descripcion.Trim();
            var designacion = string.IsNullOrWhiteSpace(model.Designacion) ? null : model.Designacion.Trim();
            var fechaModificacion = DateTime.Now;

            await _context.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE dbo.ERP_Partes
                SET
                    ClienteID = {model.ClienteID},
                    NumeroParte = {numeroParte},
                    ReferenciaSAP = {referenciaSAP},
                    Descripcion = {descripcion},
                    Designacion = {designacion},
                    RequiereGP12 = {model.RequiereGP12},
                    RequiereCertificado = {model.RequiereCertificado},
                    Activo = {model.Activo},
                    UsuarioModificacionID = {usuarioId},
                    FechaModificacion = {fechaModificacion},
                    StockMinimo = {model.StockMinimo},
                    StockAviso = {model.StockAviso},
                    StockConfigurado = {model.StockConfigurado}
                WHERE ParteID = {id};
            ");

            TempData["SuccessMessage"] = "La parte fue actualizada correctamente.";

            return RedirectToAction(nameof(Index));
        }

        // GET: /Partes/ObtenerDatosTecnicos/5
        [HttpGet]
        [Route("Partes/ObtenerDatosTecnicos/{parteId:int}")]
        [AutorizarAccion("Editar Partes", "Editar")]
        public async Task<IActionResult> ObtenerDatosTecnicos(int parteId)
        {
            var parte = await _context.ERPPartes
                .AsNoTracking()
                .Where(p => p.ParteID == parteId)
                .Select(p => new
                {
                    p.ParteID,
                    p.NumeroParte,
                    p.Descripcion
                })
                .FirstOrDefaultAsync();

            if (parte == null)
            {
                return Json(new
                {
                    success = false,
                    message = "No se encontró la parte seleccionada."
                });
            }

            var datoTecnico = await _context.ERPParteDatosTecnicos
                .AsNoTracking()
                .FirstOrDefaultAsync(dt => dt.ParteID == parteId);

            return Json(new
            {
                success = true,
                data = new
                {
                    parteDatoTecnicoID = datoTecnico?.ParteDatoTecnicoID,
                    parteID = parte.ParteID,
                    numeroParte = parte.NumeroParte,
                    descripcionParte = parte.Descripcion,

                    ciclo = datoTecnico?.Ciclo,
                    tipoSecado = datoTecnico?.TipoSecado,
                    horasSecado = datoTecnico?.HorasSecado,
                    pesoBrutoPieza = datoTecnico?.PesoBrutoPieza,
                    pesoNetoPieza = datoTecnico?.PesoNetoPieza,
                    embalajeCodigo = datoTecnico?.EmbalajeCodigo,
                    embalajeDescripcion = datoTecnico?.EmbalajeDescripcion,
                    piezasPorEmbalaje = datoTecnico?.PiezasPorEmbalaje,
                    materialCodigo = datoTecnico?.MaterialCodigo,
                    materialDescripcion = datoTecnico?.MaterialDescripcion,
                    materialID = datoTecnico?.MaterialID,
                    activo = datoTecnico?.Activo ?? true
                }
            });
        }

        // POST: /Partes/GuardarDatosTecnicos
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Route("Partes/GuardarDatosTecnicos")]
        [AutorizarAccion("Editar Partes", "Editar")]
        public async Task<IActionResult> GuardarDatosTecnicos(ParteDatoTecnicoModalViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return Json(new
                {
                    success = false,
                    message = "La información enviada no es válida."
                });
            }

            var existeParte = await _context.ERPPartes
                .AsNoTracking()
                .AnyAsync(p => p.ParteID == model.ParteID);

            if (!existeParte)
            {
                return Json(new
                {
                    success = false,
                    message = "No se encontró la parte seleccionada."
                });
            }

            var datoTecnico = await _context.ERPParteDatosTecnicos
                .FirstOrDefaultAsync(dt => dt.ParteID == model.ParteID);

            if (datoTecnico == null)
            {
                datoTecnico = new ERPParteDatoTecnico
                {
                    ParteID = model.ParteID,
                    FechaCreacion = DateTime.Now
                };

                _context.ERPParteDatosTecnicos.Add(datoTecnico);
            }
            else
            {
                datoTecnico.FechaModificacion = DateTime.Now;
            }

            datoTecnico.Ciclo = string.IsNullOrWhiteSpace(model.Ciclo) ? null : model.Ciclo.Trim();
            datoTecnico.TipoSecado = string.IsNullOrWhiteSpace(model.TipoSecado) ? null : model.TipoSecado.Trim();
            datoTecnico.HorasSecado = model.HorasSecado;
            datoTecnico.PesoBrutoPieza = model.PesoBrutoPieza;
            datoTecnico.PesoNetoPieza = model.PesoNetoPieza;

            datoTecnico.EmbalajeCodigo = string.IsNullOrWhiteSpace(model.EmbalajeCodigo) ? null : model.EmbalajeCodigo.Trim();
            datoTecnico.EmbalajeDescripcion = string.IsNullOrWhiteSpace(model.EmbalajeDescripcion) ? null : model.EmbalajeDescripcion.Trim();
            datoTecnico.PiezasPorEmbalaje = model.PiezasPorEmbalaje;

            datoTecnico.MaterialCodigo = string.IsNullOrWhiteSpace(model.MaterialCodigo) ? null : model.MaterialCodigo.Trim();
            datoTecnico.MaterialDescripcion = string.IsNullOrWhiteSpace(model.MaterialDescripcion) ? null : model.MaterialDescripcion.Trim();
            datoTecnico.MaterialID = model.MaterialID;

            datoTecnico.Activo = model.Activo;

            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                message = "Los datos técnicos fueron guardados correctamente."
            });
        }

        private async Task<List<SelectListItem>> CargarClientesAsync(int? seleccionadoId = null)
        {
            return await _context.ERPClientes
                .AsNoTracking()
                .OrderBy(c => c.Nombre)
                .Select(c => new SelectListItem
                {
                    Value = c.ClienteID.ToString(),
                    Text = c.Codigo + " - " + c.Nombre,
                    Selected = seleccionadoId.HasValue && c.ClienteID == seleccionadoId.Value
                })
                .ToListAsync();
        }

        private async Task<bool> ExisteNumeroParteAsync(
            string? numeroParte,
            int clienteId,
            int? excluirParteId = null)
        {
            if (string.IsNullOrWhiteSpace(numeroParte) || clienteId <= 0)
            {
                return false;
            }

            var numeroNormalizado = numeroParte.Trim();

            var query = _context.ERPPartes
                .AsNoTracking()
                .Where(p =>
                    p.ClienteID == clienteId &&
                    p.NumeroParte == numeroNormalizado);

            if (excluirParteId.HasValue)
            {
                query = query.Where(p => p.ParteID != excluirParteId.Value);
            }

            return await query.AnyAsync();
        }
    }
}