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
        public async Task<IActionResult> Index(
    string? busqueda,
    string? estadoFiltro)
        {
            ViewData["Title"] = "Alta de Partes";

            busqueda = busqueda?.Trim();
            estadoFiltro = estadoFiltro?.Trim().ToLowerInvariant();

            var consulta =
                from p in _context.ERPPartes.AsNoTracking()

                join c in _context.ERPClientes.AsNoTracking()
                    on p.ClienteID equals c.ClienteID into clientesJoin
                from c in clientesJoin.DefaultIfEmpty()

                join dt in _context.ERPParteDatosTecnicos.AsNoTracking()
                    on p.ParteID equals dt.ParteID into datosTecnicosJoin
                from dt in datosTecnicosJoin.DefaultIfEmpty()

                join maq in _context.ERPMaquinas.AsNoTracking()
                    on dt.MaquinaPrincipalID equals maq.MaquinaID into maquinasJoin
                from maq in maquinasJoin.DefaultIfEmpty()

                join molde in _context.ERPMoldes.AsNoTracking()
                    on dt.MoldePrincipalID equals molde.MoldeID into moldesJoin
                from molde in moldesJoin.DefaultIfEmpty()

                select new
                {
                    Parte = p,
                    Cliente = c,
                    DatosTecnicos = dt,
                    Maquina = maq,
                    Molde = molde
                };

            if (!string.IsNullOrWhiteSpace(busqueda))
            {
                consulta = consulta.Where(x =>
                    x.Parte.NumeroParte.Contains(busqueda) ||
                    (x.Parte.ReferenciaSAP != null &&
                     x.Parte.ReferenciaSAP.Contains(busqueda)) ||
                    x.Parte.Descripcion.Contains(busqueda) ||
                    (x.Parte.Designacion != null &&
                     x.Parte.Designacion.Contains(busqueda)) ||
                    (x.DatosTecnicos != null &&
                     x.DatosTecnicos.Color != null &&
                     x.DatosTecnicos.Color.Contains(busqueda)) ||
                    (x.DatosTecnicos != null &&
                     x.DatosTecnicos.MaterialCodigo != null &&
                     x.DatosTecnicos.MaterialCodigo.Contains(busqueda)) ||
                    (x.DatosTecnicos != null &&
                     x.DatosTecnicos.MaterialDescripcion != null &&
                     x.DatosTecnicos.MaterialDescripcion.Contains(busqueda)) ||
                    (x.Maquina != null &&
                     (x.Maquina.Codigo.Contains(busqueda) ||
                      x.Maquina.Nombre.Contains(busqueda))) ||
                    (x.Molde != null &&
                     (x.Molde.CodigoMolde.Contains(busqueda) ||
                      (x.Molde.NombreMolde != null &&
                       x.Molde.NombreMolde.Contains(busqueda))))
                );
            }

            if (!string.IsNullOrWhiteSpace(estadoFiltro))
            {
                switch (estadoFiltro)
                {
                    case "activas":
                        consulta = consulta.Where(x => x.Parte.Activo);
                        break;

                    case "inactivas":
                        consulta = consulta.Where(x => !x.Parte.Activo);
                        break;

                    case "stock":
                        consulta = consulta.Where(x => x.Parte.StockConfigurado);
                        break;
                }
            }

            var partes = await consulta
                .OrderBy(x => x.Parte.NumeroParte)
                .Select(x => new ParteListadoItemViewModel
                {
                    ParteID = x.Parte.ParteID,
                    NumeroParte = x.Parte.NumeroParte,
                    ReferenciaSAP = x.Parte.ReferenciaSAP,
                    Descripcion = x.Parte.Descripcion,

                    ClienteNombre = x.Cliente != null
                        ? x.Cliente.Codigo + " - " + x.Cliente.Nombre
                        : "Cliente ID " + x.Parte.ClienteID,

                    MaquinaPrincipal = x.Maquina != null
                        ? x.Maquina.Codigo + " - " + x.Maquina.Nombre
                        : null,

                    MoldePrincipal = x.Molde != null
                        ? x.Molde.CodigoMolde + " - " +
                          (x.Molde.NombreMolde ?? "Sin nombre")
                        : null,

                    StockMinimo = x.Parte.StockMinimo,
                    StockAviso = x.Parte.StockAviso,
                    StockConfigurado = x.Parte.StockConfigurado,
                    Activo = x.Parte.Activo,

                    TieneDatosTecnicos = x.DatosTecnicos != null
                })
                .ToListAsync();

            var totalActivas = await _context.ERPPartes
                .CountAsync(p => p.Activo);

            var totalInactivas = await _context.ERPPartes
                .CountAsync(p => !p.Activo);

            var totalStockConfigurado = await _context.ERPPartes
                .CountAsync(p => p.StockConfigurado);

            var model = new ParteIndexViewModel
            {
                Busqueda = busqueda,
                EstadoFiltro = estadoFiltro,
                TotalMostradas = partes.Count,
                TotalActivas = totalActivas,
                TotalInactivas = totalInactivas,
                TotalStockConfigurado = totalStockConfigurado,
                Partes = partes,

                Maquinas = await CargarMaquinasAsync(),
                Moldes = await CargarMoldesAsync()
            };

            return View(model);
        }

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

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Route("Partes/Crear")]
        [AutorizarAccion("Crear Partes", "Crear")]
        public async Task<IActionResult> Crear(ParteFormViewModel model)
        {
            if (await ExisteNumeroParteAsync(
                model.NumeroParte,
                model.ClienteID))
            {
                ModelState.AddModelError(
                    nameof(model.NumeroParte),
                    "Ya existe una parte con este número para el cliente seleccionado.");
            }

            ValidarStock(model);
            ValidarPrecioVenta(model);

            if (!ModelState.IsValid)
            {
                model.Clientes =
                    await CargarClientesAsync(model.ClienteID);

                return View(model);
            }

            var usuarioId =
                HttpContext.Session.GetInt32("UsuarioID");

            var parte = new ERPParte
            {
                ClienteID = model.ClienteID,

                NumeroParte = model.NumeroParte.Trim(),

                ReferenciaSAP =
                    string.IsNullOrWhiteSpace(model.ReferenciaSAP)
                        ? null
                        : model.ReferenciaSAP.Trim(),

                Descripcion = model.Descripcion.Trim(),

                Designacion =
                    string.IsNullOrWhiteSpace(model.Designacion)
                        ? null
                        : model.Designacion.Trim(),

                Notas =
                    string.IsNullOrWhiteSpace(model.Notas)
                        ? null
                        : model.Notas.Trim(),

                RequiereGP12 = model.RequiereGP12,
                RequiereCertificado = model.RequiereCertificado,

                Activo = model.Activo,

                StockMinimo = model.StockMinimo,
                StockAviso = model.StockAviso,
                StockConfigurado = model.StockConfigurado,

                PrecioVentaUnitario =
                    model.PrecioVentaUnitario,

                MonedaPrecioVenta =
                    string.IsNullOrWhiteSpace(model.MonedaPrecioVenta)
                        ? null
                        : model.MonedaPrecioVenta.Trim().ToUpperInvariant(),

                UnidadPrecioVenta =
                    string.IsNullOrWhiteSpace(model.UnidadPrecioVenta)
                        ? null
                        : model.UnidadPrecioVenta.Trim(),

                FuentePrecioVenta =
                    string.IsNullOrWhiteSpace(model.FuentePrecioVenta)
                        ? null
                        : model.FuentePrecioVenta.Trim(),

                ClavePrecioVentaOrigen =
                    string.IsNullOrWhiteSpace(model.ClavePrecioVentaOrigen)
                        ? null
                        : model.ClavePrecioVentaOrigen.Trim(),

                DescripcionPrecioVentaOrigen =
                    string.IsNullOrWhiteSpace(model.DescripcionPrecioVentaOrigen)
                        ? null
                        : model.DescripcionPrecioVentaOrigen.Trim(),

                FechaPrecioVenta =
                    model.PrecioVentaUnitario.HasValue
                        ? model.FechaPrecioVenta ?? DateTime.Now
                        : null,

                UsuarioCreacionID = usuarioId,
                FechaCreacion = DateTime.Now,
                UsuarioModificacionID = null,
                FechaModificacion = null
            };

            _context.ERPPartes.Add(parte);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] =
                "La parte fue registrada correctamente.";

            return RedirectToAction(nameof(Index));
        }

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
                Notas = parte.Notas,

                RequiereGP12 = parte.RequiereGP12,
                RequiereCertificado = parte.RequiereCertificado,

                Activo = parte.Activo,

                StockMinimo = parte.StockMinimo,
                StockAviso = parte.StockAviso,
                StockConfigurado = parte.StockConfigurado,

                PrecioVentaUnitario =
                    parte.PrecioVentaUnitario,

                MonedaPrecioVenta =
                    parte.MonedaPrecioVenta,

                UnidadPrecioVenta =
                    parte.UnidadPrecioVenta,

                FuentePrecioVenta =
                    parte.FuentePrecioVenta,

                ClavePrecioVentaOrigen =
                    parte.ClavePrecioVentaOrigen,

                DescripcionPrecioVentaOrigen =
                    parte.DescripcionPrecioVentaOrigen,

                FechaPrecioVenta =
                    parte.FechaPrecioVenta,

                Clientes =
                    await CargarClientesAsync(parte.ClienteID)
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Route("Partes/Editar/{id:int}")]
        [AutorizarAccion("Editar Partes", "Editar")]
        public async Task<IActionResult> Editar(
    int id,
    ParteFormViewModel model)
        {
            if (!model.ParteID.HasValue ||
                id != model.ParteID.Value)
            {
                return BadRequest();
            }

            if (await ExisteNumeroParteAsync(
                model.NumeroParte,
                model.ClienteID,
                model.ParteID))
            {
                ModelState.AddModelError(
                    nameof(model.NumeroParte),
                    "Ya existe otra parte con este número para el cliente seleccionado.");
            }

            ValidarStock(model);
            ValidarPrecioVenta(model);

            if (!ModelState.IsValid)
            {
                model.Clientes =
                    await CargarClientesAsync(model.ClienteID);

                return View(model);
            }

            var parte = await _context.ERPPartes
                .FirstOrDefaultAsync(p => p.ParteID == id);

            if (parte == null)
            {
                return NotFound();
            }

            var usuarioId =
                HttpContext.Session.GetInt32("UsuarioID");

            parte.ClienteID = model.ClienteID;
            parte.NumeroParte = model.NumeroParte.Trim();

            parte.ReferenciaSAP =
                string.IsNullOrWhiteSpace(model.ReferenciaSAP)
                    ? null
                    : model.ReferenciaSAP.Trim();

            parte.Descripcion = model.Descripcion.Trim();

            parte.Designacion =
                string.IsNullOrWhiteSpace(model.Designacion)
                    ? null
                    : model.Designacion.Trim();

            parte.Notas =
                string.IsNullOrWhiteSpace(model.Notas)
                    ? null
                    : model.Notas.Trim();

            parte.RequiereGP12 = model.RequiereGP12;
            parte.RequiereCertificado =
                model.RequiereCertificado;

            parte.Activo = model.Activo;

            parte.StockMinimo = model.StockMinimo;
            parte.StockAviso = model.StockAviso;
            parte.StockConfigurado =
                model.StockConfigurado;

            parte.PrecioVentaUnitario =
                model.PrecioVentaUnitario;

            parte.MonedaPrecioVenta =
                string.IsNullOrWhiteSpace(model.MonedaPrecioVenta)
                    ? null
                    : model.MonedaPrecioVenta
                        .Trim()
                        .ToUpperInvariant();

            parte.UnidadPrecioVenta =
                string.IsNullOrWhiteSpace(model.UnidadPrecioVenta)
                    ? null
                    : model.UnidadPrecioVenta.Trim();

            parte.FuentePrecioVenta =
                string.IsNullOrWhiteSpace(model.FuentePrecioVenta)
                    ? null
                    : model.FuentePrecioVenta.Trim();

            parte.ClavePrecioVentaOrigen =
                string.IsNullOrWhiteSpace(model.ClavePrecioVentaOrigen)
                    ? null
                    : model.ClavePrecioVentaOrigen.Trim();

            parte.DescripcionPrecioVentaOrigen =
                string.IsNullOrWhiteSpace(
                    model.DescripcionPrecioVentaOrigen)
                    ? null
                    : model.DescripcionPrecioVentaOrigen.Trim();

            parte.FechaPrecioVenta =
                model.PrecioVentaUnitario.HasValue
                    ? model.FechaPrecioVenta ?? DateTime.Now
                    : null;

            parte.UsuarioModificacionID = usuarioId;
            parte.FechaModificacion = DateTime.Now;

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] =
                "La parte fue actualizada correctamente.";

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

                    color = datoTecnico?.Color,
                    cavidades = datoTecnico?.Cavidades,
                    objetivoHora = datoTecnico?.ObjetivoHora,
                    piezasPorCaja = datoTecnico?.PiezasPorCaja,

                    maquinaPrincipalID = datoTecnico?.MaquinaPrincipalID,
                    maquinaSustitutaID = datoTecnico?.MaquinaSustitutaID,
                    moldePrincipalID = datoTecnico?.MoldePrincipalID,

                    activo = datoTecnico?.Activo ?? true
                }
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Route("Partes/GuardarDatosTecnicos")]
        [AutorizarAccion("Editar Partes", "Editar")]
        public async Task<IActionResult> GuardarDatosTecnicos(
     ParteDatoTecnicoModalViewModel model)
        {
            if (model.MaquinaPrincipalID.HasValue &&
                model.MaquinaSustitutaID.HasValue &&
                model.MaquinaPrincipalID.Value == model.MaquinaSustitutaID.Value)
            {
                ModelState.AddModelError(
                    nameof(model.MaquinaSustitutaID),
                    "La máquina sustituta debe ser diferente de la máquina principal.");
            }

            if (model.MaquinaPrincipalID.HasValue)
            {
                var existeMaquinaPrincipal = await _context.ERPMaquinas
                    .AsNoTracking()
                    .AnyAsync(m =>
                        m.MaquinaID == model.MaquinaPrincipalID.Value &&
                        m.Activo);

                if (!existeMaquinaPrincipal)
                {
                    ModelState.AddModelError(
                        nameof(model.MaquinaPrincipalID),
                        "La máquina principal seleccionada no es válida.");
                }
            }

            if (model.MaquinaSustitutaID.HasValue)
            {
                var existeMaquinaSustituta = await _context.ERPMaquinas
                    .AsNoTracking()
                    .AnyAsync(m =>
                        m.MaquinaID == model.MaquinaSustitutaID.Value &&
                        m.Activo);

                if (!existeMaquinaSustituta)
                {
                    ModelState.AddModelError(
                        nameof(model.MaquinaSustitutaID),
                        "La máquina sustituta seleccionada no es válida.");
                }
            }

            if (model.MoldePrincipalID.HasValue)
            {
                var existeMolde = await _context.ERPMoldes
                    .AsNoTracking()
                    .AnyAsync(m =>
                        m.MoldeID == model.MoldePrincipalID.Value &&
                        m.Activo);

                if (!existeMolde)
                {
                    ModelState.AddModelError(
                        nameof(model.MoldePrincipalID),
                        "El molde seleccionado no es válido.");
                }
            }

            if (!ModelState.IsValid)
            {
                var errores = ModelState
                    .Where(x => x.Value?.Errors.Count > 0)
                    .SelectMany(x => x.Value!.Errors)
                    .Select(error =>
                        !string.IsNullOrWhiteSpace(error.ErrorMessage)
                            ? error.ErrorMessage
                            : "Uno de los valores enviados no es válido.")
                    .Distinct()
                    .ToList();

                return Json(new
                {
                    success = false,
                    message = string.Join(" ", errores)
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

            var fechaActual = DateTime.Now;

            if (datoTecnico == null)
            {
                datoTecnico = new ERPParteDatoTecnico
                {
                    ParteID = model.ParteID,
                    FechaCreacion = fechaActual
                };

                _context.ERPParteDatosTecnicos.Add(datoTecnico);
            }
            else
            {
                datoTecnico.FechaModificacion = fechaActual;
            }

            datoTecnico.Ciclo =
                string.IsNullOrWhiteSpace(model.Ciclo)
                    ? null
                    : model.Ciclo.Trim();

            datoTecnico.TipoSecado =
                string.IsNullOrWhiteSpace(model.TipoSecado)
                    ? null
                    : model.TipoSecado.Trim();

            datoTecnico.HorasSecado = model.HorasSecado;
            datoTecnico.PesoBrutoPieza = model.PesoBrutoPieza;

            datoTecnico.EmbalajeCodigo =
                string.IsNullOrWhiteSpace(model.EmbalajeCodigo)
                    ? null
                    : model.EmbalajeCodigo.Trim();

            datoTecnico.EmbalajeDescripcion =
                string.IsNullOrWhiteSpace(model.EmbalajeDescripcion)
                    ? null
                    : model.EmbalajeDescripcion.Trim();

            datoTecnico.PiezasPorEmbalaje = model.PiezasPorEmbalaje;

            datoTecnico.MaterialCodigo =
                string.IsNullOrWhiteSpace(model.MaterialCodigo)
                    ? null
                    : model.MaterialCodigo.Trim();

            datoTecnico.MaterialDescripcion =
                string.IsNullOrWhiteSpace(model.MaterialDescripcion)
                    ? null
                    : model.MaterialDescripcion.Trim();

            datoTecnico.MaterialID = model.MaterialID;

            datoTecnico.Color =
                string.IsNullOrWhiteSpace(model.Color)
                    ? null
                    : model.Color.Trim();

            datoTecnico.Cavidades = model.Cavidades;
            datoTecnico.ObjetivoHora = model.ObjetivoHora;
            datoTecnico.PiezasPorCaja = model.PiezasPorCaja;

            datoTecnico.MaquinaPrincipalID = model.MaquinaPrincipalID;
            datoTecnico.MaquinaSustitutaID = model.MaquinaSustitutaID;
            datoTecnico.MoldePrincipalID = model.MoldePrincipalID;

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

        private void ValidarStock(ParteFormViewModel model)
        {
            if (!model.StockConfigurado)
            {
                return;
            }

            if (model.StockMinimo < 0)
            {
                ModelState.AddModelError(
                    nameof(model.StockMinimo),
                    "El stock mínimo no puede ser negativo.");
            }

            if (model.StockAviso < 0)
            {
                ModelState.AddModelError(
                    nameof(model.StockAviso),
                    "El stock de aviso no puede ser negativo.");
            }

            if (model.StockAviso < model.StockMinimo)
            {
                ModelState.AddModelError(
                    nameof(model.StockAviso),
                    "El stock de aviso debe ser igual o mayor que el stock mínimo.");
            }
        }

        private void ValidarPrecioVenta(
            ParteFormViewModel model)
        {
            if (!model.PrecioVentaUnitario.HasValue)
            {
                return;
            }

            if (model.PrecioVentaUnitario.Value <= 0)
            {
                ModelState.AddModelError(
                    nameof(model.PrecioVentaUnitario),
                    "El precio de venta debe ser mayor que cero.");
            }

            if (string.IsNullOrWhiteSpace(
                model.MonedaPrecioVenta))
            {
                ModelState.AddModelError(
                    nameof(model.MonedaPrecioVenta),
                    "Selecciona la moneda del precio de venta.");
            }

            if (string.IsNullOrWhiteSpace(
                model.UnidadPrecioVenta))
            {
                ModelState.AddModelError(
                    nameof(model.UnidadPrecioVenta),
                    "Captura la unidad del precio de venta.");
            }
        }


    }
}
