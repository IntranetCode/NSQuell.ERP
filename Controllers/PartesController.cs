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
                    (p.Designacion != null && p.Designacion.Contains(busqueda)) ||
                    (p.Color != null && p.Color.Contains(busqueda))
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

                join maq in _context.ERPMaquinas.AsNoTracking()
                    on p.MaquinaPrincipalID equals maq.MaquinaID into maquinasJoin
                from maq in maquinasJoin.DefaultIfEmpty()

                join molde in _context.ERPMoldes.AsNoTracking()
                    on p.MoldePrincipalID equals molde.MoldeID into moldesJoin
                from molde in moldesJoin.DefaultIfEmpty()

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

                    MaquinaPrincipal = maq != null
                        ? maq.Codigo + " - " + maq.Nombre
                        : null,

                    MoldePrincipal = molde != null
                        ? molde.CodigoMolde + " - " + (molde.NombreMolde ?? "Sin nombre")
                        : null,

                    StockMinimo = p.StockMinimo,
                    StockAviso = p.StockAviso,
                    StockConfigurado = p.StockConfigurado,
                    Activo = p.Activo,
                    TieneDatosTecnicos = _context.ERPParteDatosTecnicos.Any(dt => dt.ParteID == p.ParteID)
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
                Clientes = await CargarClientesAsync(),
                Maquinas = await CargarMaquinasAsync(),
                Moldes = await CargarMoldesAsync()
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
                    "Ya existe una parte con este numero para el cliente seleccionado.");
            }

            if (!ModelState.IsValid)
            {
                model.Clientes = await CargarClientesAsync(model.ClienteID);
                model.Maquinas = await CargarMaquinasAsync(model.MaquinaPrincipalID);
                model.Moldes = await CargarMoldesAsync(model.MoldePrincipalID);

                return View(model);
            }

            var usuarioId = HttpContext.Session.GetInt32("UsuarioID");

            var parte = new ERPParte
            {
                ClienteID = model.ClienteID,
                NumeroParte = model.NumeroParte.Trim(),
                ReferenciaSAP = string.IsNullOrWhiteSpace(model.ReferenciaSAP) ? null : model.ReferenciaSAP.Trim(),
                Descripcion = model.Descripcion.Trim(),
                Designacion = string.IsNullOrWhiteSpace(model.Designacion) ? null : model.Designacion.Trim(),
                Color = string.IsNullOrWhiteSpace(model.Color) ? null : model.Color.Trim(),

                Cavidades = model.Cavidades,
                ObjetivoHora = model.ObjetivoHora,
                PiezasPorCaja = model.PiezasPorCaja,

                RequiereGP12 = model.RequiereGP12,
                RequiereCertificado = model.RequiereCertificado,

                MaquinaPrincipalID = model.MaquinaPrincipalID,
                MaquinaSustitutaID = model.MaquinaSustitutaID,
                MoldePrincipalID = model.MoldePrincipalID,

                Activo = model.Activo,

                StockMinimo = model.StockMinimo,
                StockAviso = model.StockAviso,
                StockConfigurado = model.StockConfigurado,

                UsuarioCreacionID = usuarioId,
                FechaCreacion = DateTime.Now,
                UsuarioModificacionID = null,
                FechaModificacion = null
            };

            _context.ERPPartes.Add(parte);
            await _context.SaveChangesAsync();

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

            var parte = await _context.ERPPartes
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.ParteID == id);

            if (parte == null)
            {
                return NotFound();
            }

            var model = new ParteFormViewModel
            {
                ParteID = parte.ParteID,
                ClienteID = parte.ClienteID,
                NumeroParte = parte.NumeroParte,
                ReferenciaSAP = parte.ReferenciaSAP,
                Descripcion = parte.Descripcion,
                Designacion = parte.Designacion,
                Color = parte.Color,

                Cavidades = parte.Cavidades,
                ObjetivoHora = parte.ObjetivoHora,
                PiezasPorCaja = parte.PiezasPorCaja,

                RequiereGP12 = parte.RequiereGP12,
                RequiereCertificado = parte.RequiereCertificado,

                MaquinaPrincipalID = parte.MaquinaPrincipalID,
                MaquinaSustitutaID = parte.MaquinaSustitutaID,
                MoldePrincipalID = parte.MoldePrincipalID,

                Activo = parte.Activo,

                StockMinimo = parte.StockMinimo,
                StockAviso = parte.StockAviso,
                StockConfigurado = parte.StockConfigurado,

                Clientes = await CargarClientesAsync(parte.ClienteID),
                Maquinas = await CargarMaquinasAsync(parte.MaquinaPrincipalID),
                Moldes = await CargarMoldesAsync(parte.MoldePrincipalID)
            };

            return View(model);
        }

        // POST: /Partes/Editar/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Route("Partes/Editar/{id:int}")]
        [AutorizarAccion("Editar Partes", "Editar")]
        public async Task<IActionResult> Editar(int id, ParteFormViewModel model)
        {
            if (!model.ParteID.HasValue || id != model.ParteID.Value)
            {
                return BadRequest();
            }

            if (await ExisteNumeroParteAsync(model.NumeroParte, model.ClienteID, model.ParteID))
            {
                ModelState.AddModelError(
                    nameof(model.NumeroParte),
                    "Ya existe otra parte con este numero para el cliente seleccionado.");
            }

            if (!ModelState.IsValid)
            {
                model.Clientes = await CargarClientesAsync(model.ClienteID);
                model.Maquinas = await CargarMaquinasAsync(model.MaquinaPrincipalID);
                model.Moldes = await CargarMoldesAsync(model.MoldePrincipalID);

                return View(model);
            }

            var parte = await _context.ERPPartes
                .FirstOrDefaultAsync(p => p.ParteID == id);

            if (parte == null)
            {
                return NotFound();
            }

            var usuarioId = HttpContext.Session.GetInt32("UsuarioID");

            parte.ClienteID = model.ClienteID;
            parte.NumeroParte = model.NumeroParte.Trim();
            parte.ReferenciaSAP = string.IsNullOrWhiteSpace(model.ReferenciaSAP) ? null : model.ReferenciaSAP.Trim();
            parte.Descripcion = model.Descripcion.Trim();
            parte.Designacion = string.IsNullOrWhiteSpace(model.Designacion) ? null : model.Designacion.Trim();
            parte.Color = string.IsNullOrWhiteSpace(model.Color) ? null : model.Color.Trim();

            parte.Cavidades = model.Cavidades;
            parte.ObjetivoHora = model.ObjetivoHora;
            parte.PiezasPorCaja = model.PiezasPorCaja;

            parte.RequiereGP12 = model.RequiereGP12;
            parte.RequiereCertificado = model.RequiereCertificado;

            parte.MaquinaPrincipalID = model.MaquinaPrincipalID;
            parte.MaquinaSustitutaID = model.MaquinaSustitutaID;
            parte.MoldePrincipalID = model.MoldePrincipalID;

            parte.Activo = model.Activo;

            parte.StockMinimo = model.StockMinimo;
            parte.StockAviso = model.StockAviso;
            parte.StockConfigurado = model.StockConfigurado;

            parte.UsuarioModificacionID = usuarioId;
            parte.FechaModificacion = DateTime.Now;

            await _context.SaveChangesAsync();

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
                .FirstOrDefaultAsync(p => p.ParteID == parteId);

            if (parte == null)
            {
                return Json(new
                {
                    success = false,
                    message = "No se encontro la parte seleccionada."
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
                    message = "La informacion enviada no es valida."
                });
            }

            var parte = await _context.ERPPartes
                .FirstOrDefaultAsync(p => p.ParteID == model.ParteID);

            if (parte == null)
            {
                return Json(new
                {
                    success = false,
                    message = "No se encontro la parte seleccionada."
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
                message = "Los datos tecnicos fueron guardados correctamente."
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

        private async Task<List<SelectListItem>> CargarMaquinasAsync(int? seleccionadoId = null)
        {
            return await _context.ERPMaquinas
                .AsNoTracking()
                .Where(m => m.Activo)
                .OrderBy(m => m.Codigo)
                .Select(m => new SelectListItem
                {
                    Value = m.MaquinaID.ToString(),
                    Text = m.Codigo + " - " + m.Nombre,
                    Selected = seleccionadoId.HasValue && m.MaquinaID == seleccionadoId.Value
                })
                .ToListAsync();
        }

        private async Task<List<SelectListItem>> CargarMoldesAsync(int? seleccionadoId = null)
        {
            return await _context.ERPMoldes
                .AsNoTracking()
                .Where(m => m.Activo)
                .OrderBy(m => m.CodigoMolde)
                .Select(m => new SelectListItem
                {
                    Value = m.MoldeID.ToString(),
                    Text = m.CodigoMolde + " - " + (m.NombreMolde ?? "Sin nombre"),
                    Selected = seleccionadoId.HasValue && m.MoldeID == seleccionadoId.Value
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
