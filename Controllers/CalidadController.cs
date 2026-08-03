using ERP.NSQuell.Models;
using ERP.NSQuell.Models.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers
{
    public partial class CalidadController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;

        private static readonly string[] EstadosProcesoActivo =
        {
            CalidadEstados.PendientePrearranque,
            CalidadEstados.DevueltoPrearranque,
            CalidadEstados.ArranqueAutorizado,
            CalidadEstados.PendientePrimerasPiezas,
            CalidadEstados.AjustesSolicitados,
            CalidadEstados.ProduccionLiberada,
            CalidadEstados.MonitoreoActivo,
            CalidadEstados.PendienteLiberacionCaja,
            CalidadEstados.PendienteReliberacion,
            CalidadEstados.PendienteGP12,
            CalidadEstados.EnGP12,
            CalidadEstados.LegacyAbierta,
            CalidadEstados.LegacyDetenida,
            CalidadEstados.LegacyGPI2
        };

        public CalidadController(
            ApplicationDbContext context,
            IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        private string ConnectionString =>
            _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "No se encontró la cadena de conexión DefaultConnection."
            );

        // =========================================================
        // BANDEJA PRINCIPAL
        // =========================================================

        [HttpGet]
        public async Task<IActionResult> Index(
            string? busqueda,
            string? estadoFiltro)
        {
            busqueda = busqueda?.Trim();
            estadoFiltro = estadoFiltro?
                .Trim()
                .ToUpperInvariant();

            var baseQuery = _context.CalidadInspecciones
                .AsNoTracking();

            var query = baseQuery;

            if (!string.IsNullOrWhiteSpace(busqueda))
            {
                query = query.Where(x =>
                    (x.CodigoBarras != null &&
                     x.CodigoBarras.Contains(busqueda)) ||

                    (x.OrdenTrabajo != null &&
                     x.OrdenTrabajo.Contains(busqueda)) ||

                    (x.ClienteNombre != null &&
                     x.ClienteNombre.Contains(busqueda)) ||

                    (x.NumeroParte != null &&
                     x.NumeroParte.Contains(busqueda)) ||

                    (x.Material != null &&
                     x.Material.Contains(busqueda)) ||

                    (x.Proceso != null &&
                     x.Proceso.Contains(busqueda)) ||

                    (x.Maquina != null &&
                     x.Maquina.Contains(busqueda)) ||

                    (x.Molde != null &&
                     x.Molde.Contains(busqueda)) ||

                    (x.OperadorPrincipalNombre != null &&
                     x.OperadorPrincipalNombre.Contains(busqueda))
                );
            }

            if (!string.IsNullOrWhiteSpace(estadoFiltro))
            {
                query = query.Where(x =>
                    x.Estado == estadoFiltro);
            }

            var model = new CalidadIndexViewModel
            {
                Busqueda = busqueda,
                EstadoFiltro = estadoFiltro,

                TotalPendientePrearranque =
                    await baseQuery.CountAsync(x =>
                        x.Estado ==
                        CalidadEstados.PendientePrearranque),

                TotalDevueltoPrearranque =
                    await baseQuery.CountAsync(x =>
                        x.Estado ==
                        CalidadEstados.DevueltoPrearranque),

                TotalArranqueAutorizado =
                    await baseQuery.CountAsync(x =>
                        x.Estado ==
                        CalidadEstados.ArranqueAutorizado),

                TotalPendientePrimerasPiezas =
                    await baseQuery.CountAsync(x =>
                        x.Estado ==
                        CalidadEstados.PendientePrimerasPiezas),

                TotalAjustesSolicitados =
                    await baseQuery.CountAsync(x =>
                        x.Estado ==
                        CalidadEstados.AjustesSolicitados),

                TotalProduccionLiberada =
                    await baseQuery.CountAsync(x =>
                        x.Estado ==
                        CalidadEstados.ProduccionLiberada),

                TotalMonitoreoActivo =
                    await baseQuery.CountAsync(x =>
                        x.Estado ==
                        CalidadEstados.MonitoreoActivo),

                TotalPendienteLiberacionCaja =
                    await baseQuery.CountAsync(x =>
                        x.Estado ==
                        CalidadEstados.PendienteLiberacionCaja),

                TotalPendienteReliberacion =
                    await baseQuery.CountAsync(x =>
                        x.Estado ==
                        CalidadEstados.PendienteReliberacion),

                TotalPendienteGP12 =
                    await baseQuery.CountAsync(x =>
                        x.Estado ==
                        CalidadEstados.PendienteGP12),

                TotalEnGP12 =
                    await baseQuery.CountAsync(x =>
                        x.Estado ==
                        CalidadEstados.EnGP12),

                TotalMaterialNoConforme =
                    await baseQuery.CountAsync(x =>
                        x.Estado ==
                        CalidadEstados.MaterialNoConforme),

                TotalCerradas =
                    await baseQuery.CountAsync(x =>
                        x.Estado ==
                        CalidadEstados.Cerrada),

                /*
                 * Totales de compatibilidad para el Index anterior.
                 */
                TotalAbiertas =
                    await baseQuery.CountAsync(x =>
                        x.Estado ==
                            CalidadEstados.LegacyAbierta ||
                        x.Estado ==
                            CalidadEstados.PendientePrearranque ||
                        x.Estado ==
                            CalidadEstados.DevueltoPrearranque ||
                        x.Estado ==
                            CalidadEstados.ArranqueAutorizado ||
                        x.Estado ==
                            CalidadEstados.PendientePrimerasPiezas ||
                        x.Estado ==
                            CalidadEstados.AjustesSolicitados ||
                        x.Estado ==
                            CalidadEstados.ProduccionLiberada ||
                        x.Estado ==
                            CalidadEstados.MonitoreoActivo ||
                        x.Estado ==
                            CalidadEstados.PendienteLiberacionCaja ||
                        x.Estado ==
                            CalidadEstados.PendienteReliberacion),

                TotalLiberadas =
                    await baseQuery.CountAsync(x =>
                        x.Estado ==
                            CalidadEstados.LegacyLiberada ||
                        x.Estado ==
                            CalidadEstados.ProduccionLiberada ||
                        x.Estado ==
                            CalidadEstados.MaterialLiberado),

                TotalGPI2 =
                    await baseQuery.CountAsync(x =>
                        x.Estado ==
                            CalidadEstados.LegacyGPI2 ||
                        x.Estado ==
                            CalidadEstados.PendienteGP12 ||
                        x.Estado ==
                            CalidadEstados.EnGP12),

                TotalContencion =
                    await baseQuery.CountAsync(x =>
                        x.Estado ==
                            CalidadEstados.LegacyContencion ||
                        x.Estado ==
                            CalidadEstados.MaterialNoConforme ||
                        x.EnContencion),

                TotalScrap =
                    await baseQuery.CountAsync(x =>
                        x.Estado ==
                            CalidadEstados.LegacyScrap ||
                        x.EsScrap)
            };

            model.Inspecciones = await query
                .OrderByDescending(x =>
                    x.FechaNotificacionCalidad ??
                    x.FechaCreacion)
                .Select(x =>
                    new CalidadListadoItemViewModel
                    {
                        InspeccionID =
                            x.InspeccionID,

                        ProgramaProduccionID =
                            x.ProgramaProduccionID,

                        EjecucionProduccionID =
                            x.EjecucionProduccionID,

                        ChecklistArranqueID =
                            x.ChecklistArranqueID,

                        SolicitudProduccionID =
                            x.SolicitudProduccionID,

                        SolicitudProduccionDetalleID =
                            x.SolicitudProduccionDetalleID,

                        ReleaseID =
                            x.ReleaseID,

                        ReleaseDetalleID =
                            x.ReleaseDetalleID,

                        CodigoBarras =
                            x.CodigoBarras,

                        OrdenTrabajo =
                            x.OrdenTrabajo,

                        ClienteNombre =
                            x.ClienteNombre,

                        NumeroParte =
                            x.NumeroParte,

                        Material =
                            x.Material,

                        Proceso =
                            x.Proceso,

                        Maquina =
                            x.Maquina,

                        Molde =
                            x.Molde,

                        OperadorPrincipalNombre =
                            x.OperadorPrincipalNombre,

                        OperadorAuxiliarNombre =
                            x.OperadorAuxiliarNombre,

                        TecnicoInyeccionNombre =
                            x.TecnicoInyeccionNombre,

                        CantidadTotal =
                            x.CantidadTotal,

                        CantidadRevisada =
                            x.CantidadRevisada,

                        CantidadPendiente =
                            x.CantidadPendiente,

                        FechaInicioProgramada =
                            x.FechaInicioProgramada,

                        FechaFinProgramada =
                            x.FechaFinProgramada,

                        FechaNotificacionCalidad =
                            x.FechaNotificacionCalidad,

                        FechaLiberacionProduccion =
                            x.FechaLiberacionProduccion,

                        ResultadoCalidad =
                            x.ResultadoCalidad,

                        Etiqueta =
                            x.Etiqueta,

                        Estado =
                            x.Estado,

                        RequiereReliberacion =
                            x.RequiereReliberacion,

                        ConfiguracionInvalidada =
                            x.ConfiguracionInvalidada,

                        MotivoInvalidacion =
                            x.MotivoInvalidacion,

                        FechaCreacion =
                            x.FechaCreacion
                    }
                )
                .ToListAsync();

            model.TotalMostrados =
                model.Inspecciones.Count;

            return View(model);
        }

        // =========================================================
        // ENTRADA REAL DESDE PRODUCCIÓN
        // =========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecibirDesdeProduccion(
            int programaProduccionId)
        {
            var usuarioId =
                ObtenerUsuarioIdActual();

            if (!usuarioId.HasValue ||
                usuarioId.Value <= 0)
            {
                TempData["Error"] =
                    "No se pudo identificar el usuario de la sesión.";

                return RedirectToAction(nameof(Index));
            }

            if (programaProduccionId <= 0)
            {
                TempData["Error"] =
                    "No se recibió un programa de producción válido.";

                return RedirectToAction(nameof(Index));
            }

            var corrida =
                await ObtenerCorridaOrigenAsync(
                    programaProduccionId
                );

            if (corrida == null)
            {
                TempData["Error"] =
                    "No se encontró el programa de producción.";

                return RedirectToAction(nameof(Index));
            }

            if (corrida.EstatusProgramaID == 5 ||
                corrida.EstatusProgramaID == 9 ||
                corrida.EstatusProgramaID == 99)
            {
                TempData["Error"] =
                    "La corrida está terminada, cerrada o cancelada y no puede enviarse a Calidad.";

                return RedirectToAction(nameof(Index));
            }

            var faltantes =
                ObtenerFaltantesCorrida(corrida);

            if (faltantes.Count > 0)
            {
                TempData["Error"] =
                    "La corrida no puede enviarse a Calidad. Faltan: " +
                    string.Join(", ", faltantes) +
                    ".";

                return RedirectToAction(nameof(Index));
            }

            var inspeccionExistente =
                await _context.CalidadInspecciones
                    .AsNoTracking()
                    .Where(x =>
                        x.ProgramaProduccionID ==
                            programaProduccionId &&
                        !x.ConfiguracionInvalidada &&
                        x.Estado !=
                            CalidadEstados.Cerrada)
                    .OrderByDescending(x =>
                        x.FechaCreacion)
                    .FirstOrDefaultAsync();

            if (inspeccionExistente != null)
            {
                TempData["Mensaje"] =
                    "La corrida ya cuenta con un proceso de Calidad.";

                return RedirectToAction(
                    nameof(Detalle),
                    new
                    {
                        id = inspeccionExistente
                            .InspeccionID
                    }
                );
            }

            await using var tx =
                await _context.Database
                    .BeginTransactionAsync();

            try
            {
                var fechaAhora =
                    DateTime.Now;

                var inspeccion =
                    new CalidadInspeccion
                    {
                        ProgramaProduccionID =
                            corrida.ProgramaProduccionID,

                        SolicitudProduccionID =
                            corrida.SolicitudProduccionID,

                        SolicitudProduccionDetalleID =
                            corrida.SolicitudProduccionDetalleID,

                        ReleaseID =
                            corrida.ReleaseID,

                        ReleaseDetalleID =
                            corrida.ReleaseDetalleID,

                        ClienteID =
                            corrida.ClienteID,

                        ClienteNombre =
                            corrida.ClienteNombre,

                        ParteID =
                            corrida.ParteID,

                        MaquinaID =
                            corrida.MaquinaID,

                        MoldeID =
                            corrida.MoldeID,

                        MaterialID =
                            corrida.MaterialID,

                        OrdenTrabajo =
                            corrida.NumeroOF,

                        NumeroParte =
                            PrimerTextoDisponible(
                                corrida.ReferenciaSAP,
                                corrida.NumeroParte
                            ),

                        Material =
                            UnirCodigoDescripcion(
                                corrida.MaterialCodigo,
                                corrida.MaterialDescripcion
                            ),

                        Proceso =
                            "LIBERACIÓN DE CORRIDA",

                        Maquina =
                            UnirCodigoDescripcion(
                                corrida.MaquinaCodigo,
                                corrida.MaquinaNombre
                            ),

                        Molde =
                            corrida.MoldeCodigo,

                        FechaInicioProgramada =
                            corrida.FechaInicioProgramada,

                        FechaFinProgramada =
                            corrida.FechaFinProgramada,

                        OperadorPrincipalPersonaID =
                            corrida.OperadorPrincipalPersonaID,

                        OperadorPrincipalNombre =
                            corrida.OperadorPrincipalNombre,

                        OperadorAuxiliarPersonaID =
                            corrida.OperadorAuxiliarPersonaID,

                        OperadorAuxiliarNombre =
                            corrida.OperadorAuxiliarNombre,

                        CantidadTotal =
                            corrida.CantidadProgramada,

                        CantidadRevisada =
                            0,

                        CantidadPendiente =
                            corrida.CantidadProgramada,

                        /*
                         * Esta acción debe invocarse únicamente
                         * después de que Producción termine su
                         * checklist.
                         */
                        ChecklistValidado =
                            true,

                        HojaInspeccionProducto =
                            false,

                        HojaValidacionCalidad =
                            false,

                        FechaNotificacionCalidad =
                            fechaAhora,

                        UsuarioNotificoID =
                            usuarioId,

                        CincoDisparosSegregados =
                            false,

                        CantidadDisparosConformes =
                            0,

                        ResultadoCalidad =
                            null,

                        Etiqueta =
                            null,

                        Liberado =
                            false,

                        RequiereGP12 =
                            false,

                        EnContencion =
                            false,

                        EsScrap =
                            false,

                        ConfiguracionInvalidada =
                            false,

                        Observaciones =
                            "Solicitud recibida desde Producción para revisión de prearranque.",

                        Estado =
                            CalidadEstados
                                .PendientePrearranque,

                        UsuarioCreacionID =
                            usuarioId,

                        FechaCreacion =
                            fechaAhora
                    };

                _context.CalidadInspecciones
                    .Add(inspeccion);

                await _context.SaveChangesAsync();

                AgregarHistorial(
                    inspeccion,
                    CalidadMovimientos
                        .RecibidoDesdeProduccion,
                    null,
                    inspeccion.Estado,
                    null,
                    null,
                    "Producción envió la corrida a Calidad para revisión de prearranque.",
                    usuarioId
                );

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                TempData["Mensaje"] =
                    "La corrida fue enviada correctamente a Calidad.";

                return RedirectToAction(
                    nameof(Detalle),
                    new
                    {
                        id = inspeccion.InspeccionID
                    }
                );
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "No fue posible recibir la corrida en Calidad: " +
                    ex.Message;

                return RedirectToAction(nameof(Index));
            }
        }

        // =========================================================
        // DETALLE
        // =========================================================

        [HttpGet]
        public async Task<IActionResult> Detalle(int id)
        {
            var model = await ConstruirDetalleFlujoAsync(id);

            if (model == null)
                return NotFound();

            return View(model);
        }

        // =========================================================
        // PREARRANQUE
        // =========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AutorizarPrearranque(
            CalidadPrearranqueViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] =
                    "No se recibió una inspección válida.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID }
                );
            }

            var inspeccion =
                await _context.CalidadInspecciones
                    .FirstOrDefaultAsync(x =>
                        x.InspeccionID ==
                            model.InspeccionID);

            if (inspeccion == null)
                return NotFound();

            if (!CalidadEstados
                .PuedeAutorizarPrearranque(
                    inspeccion.Estado))
            {
                TempData["Error"] =
                    "La inspección ya no se encuentra pendiente de prearranque.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID }
                );
            }

            if (inspeccion.ConfiguracionInvalidada)
            {
                TempData["Error"] =
                    "La configuración de Planeación fue invalidada. Debe generarse una nueva revisión.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID }
                );
            }

            var validacion =
                await ValidarConfiguracionActualAsync(
                    inspeccion
                );

            if (!validacion.Valida)
            {
                await InvalidarConfiguracionAsync(
                    inspeccion,
                    validacion.Motivo
                );

                TempData["Error"] =
                    validacion.Motivo;

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID }
                );
            }

            var usuarioId =
                ObtenerUsuarioIdActual();

            var estadoAnterior =
                inspeccion.Estado;

            inspeccion.Estado =
                CalidadEstados.ArranqueAutorizado;

            inspeccion.FechaAutorizacionPrearranque =
                DateTime.Now;

            inspeccion.UsuarioAutorizacionPrearranqueID =
                usuarioId;

            inspeccion.MotivoDevolucion =
                null;

            MarcarModificacion(
                inspeccion,
                usuarioId
            );

            AgregarHistorial(
                inspeccion,
                CalidadMovimientos
                    .PrearranqueAutorizado,
                estadoAnterior,
                inspeccion.Estado,
                inspeccion.ResultadoCalidad,
                inspeccion.Etiqueta,
                string.IsNullOrWhiteSpace(model.Motivo)
                    ? "Calidad autorizó el arranque controlado."
                    : model.Motivo.Trim(),
                usuarioId
            );

            await _context.SaveChangesAsync();

            TempData["Mensaje"] =
                "Prearranque autorizado correctamente.";

            return RedirectToAction(
                nameof(Detalle),
                new { id = model.InspeccionID }
            );
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DevolverPrearranque(
            CalidadPrearranqueViewModel model)
        {
            model.Motivo =
                model.Motivo?.Trim();

            if (string.IsNullOrWhiteSpace(model.Motivo))
            {
                TempData["Error"] =
                    "Debes capturar el motivo de la devolución.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID }
                );
            }

            var inspeccion =
                await _context.CalidadInspecciones
                    .FirstOrDefaultAsync(x =>
                        x.InspeccionID ==
                            model.InspeccionID);

            if (inspeccion == null)
                return NotFound();

            if (!CalidadEstados
                .PuedeAutorizarPrearranque(
                    inspeccion.Estado))
            {
                TempData["Error"] =
                    "La inspección ya no se encuentra pendiente de prearranque.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID }
                );
            }

            var usuarioId =
                ObtenerUsuarioIdActual();

            var estadoAnterior =
                inspeccion.Estado;

            inspeccion.Estado =
                CalidadEstados.DevueltoPrearranque;

            inspeccion.MotivoDevolucion =
                model.Motivo;

            inspeccion.Liberado =
                false;

            MarcarModificacion(
                inspeccion,
                usuarioId
            );

            AgregarHistorial(
                inspeccion,
                CalidadMovimientos
                    .PrearranqueDevuelto,
                estadoAnterior,
                inspeccion.Estado,
                inspeccion.ResultadoCalidad,
                inspeccion.Etiqueta,
                model.Motivo,
                usuarioId
            );

            await _context.SaveChangesAsync();

            TempData["Mensaje"] =
                "La revisión fue devuelta a Producción.";

            return RedirectToAction(
                nameof(Detalle),
                new { id = model.InspeccionID }
            );
        }

        // =========================================================
        // PRIMERAS PIEZAS
        // =========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarPrimerasPiezas(
            CalidadPrimerasPiezasViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] =
                    "Revisa la información de las primeras piezas.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID }
                );
            }

            if (!model.CincoDisparosSegregados)
            {
                TempData["Error"] =
                    "Debes confirmar la segregación de los primeros cinco disparos.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID }
                );
            }

            var inspeccion =
                await _context.CalidadInspecciones
                    .FirstOrDefaultAsync(x =>
                        x.InspeccionID ==
                            model.InspeccionID);

            if (inspeccion == null)
                return NotFound();

            if (!CalidadEstados
                .PuedeValidarPrimerasPiezas(
                    inspeccion.Estado))
            {
                TempData["Error"] =
                    "La inspección no está disponible para registrar primeras piezas.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID }
                );
            }

            var usuarioId =
                ObtenerUsuarioIdActual();

            var estadoAnterior =
                inspeccion.Estado;

            AplicarPrimerasPiezas(
                inspeccion,
                model,
                usuarioId
            );

            inspeccion.Estado =
                CalidadEstados
                    .PendientePrimerasPiezas;

            MarcarModificacion(
                inspeccion,
                usuarioId
            );

            AgregarHistorial(
                inspeccion,
                CalidadMovimientos
                    .PrimerasPiezasRecibidas,
                estadoAnterior,
                inspeccion.Estado,
                inspeccion.ResultadoCalidad,
                inspeccion.Etiqueta,
                string.IsNullOrWhiteSpace(
                    model.Observaciones)
                    ? "Calidad registró la validación de primeras piezas."
                    : model.Observaciones.Trim(),
                usuarioId
            );

            await _context.SaveChangesAsync();

            TempData["Mensaje"] =
                "La validación de primeras piezas fue registrada.";

            return RedirectToAction(
                nameof(Detalle),
                new { id = model.InspeccionID }
            );
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SolicitarAjustes(
            CalidadPrimerasPiezasViewModel model)
        {
            model.Observaciones =
                model.Observaciones?.Trim();

            if (string.IsNullOrWhiteSpace(
                model.Observaciones))
            {
                TempData["Error"] =
                    "Captura las observaciones o ajustes requeridos.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID }
                );
            }

            var inspeccion =
                await _context.CalidadInspecciones
                    .FirstOrDefaultAsync(x =>
                        x.InspeccionID ==
                            model.InspeccionID);

            if (inspeccion == null)
                return NotFound();

            if (!CalidadEstados
                .PuedeValidarPrimerasPiezas(
                    inspeccion.Estado))
            {
                TempData["Error"] =
                    "La inspección no permite solicitar ajustes en su estado actual.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID }
                );
            }

            var usuarioId =
                ObtenerUsuarioIdActual();

            var estadoAnterior =
                inspeccion.Estado;

            AplicarPrimerasPiezas(
                inspeccion,
                model,
                usuarioId
            );

            inspeccion.ResultadoCalidad =
                "NOK";

            inspeccion.Etiqueta =
                null;

            inspeccion.Liberado =
                false;

            inspeccion.Estado =
                CalidadEstados
                    .AjustesSolicitados;

            inspeccion.Observaciones =
                model.Observaciones;

            MarcarModificacion(
                inspeccion,
                usuarioId
            );

            AgregarHistorial(
                inspeccion,
                CalidadMovimientos
                    .AjustesSolicitados,
                estadoAnterior,
                inspeccion.Estado,
                inspeccion.ResultadoCalidad,
                inspeccion.Etiqueta,
                model.Observaciones,
                usuarioId
            );

            await _context.SaveChangesAsync();

            TempData["Mensaje"] =
                "Los ajustes fueron solicitados a Producción.";

            return RedirectToAction(
                nameof(Detalle),
                new { id = model.InspeccionID }
            );
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LiberarProduccion(
            int id)
        {
            var inspeccion =
                await _context.CalidadInspecciones
                    .FirstOrDefaultAsync(x =>
                        x.InspeccionID == id);

            if (inspeccion == null)
                return NotFound();

            if (inspeccion.Estado !=
                    CalidadEstados
                        .PendientePrimerasPiezas &&
                inspeccion.Estado !=
                    CalidadEstados
                        .AjustesSolicitados &&
                inspeccion.Estado !=
                    CalidadEstados
                        .LegacyAbierta)
            {
                TempData["Error"] =
                    "La inspección no se encuentra lista para liberar producción.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id }
                );
            }

            if (inspeccion.ConfiguracionInvalidada)
            {
                TempData["Error"] =
                    "La configuración fue invalidada y no puede liberarse.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id }
                );
            }

            var validacionConfiguracion =
                await ValidarConfiguracionActualAsync(
                    inspeccion
                );

            if (!validacionConfiguracion.Valida)
            {
                await InvalidarConfiguracionAsync(
                    inspeccion,
                    validacionConfiguracion.Motivo
                );

                TempData["Error"] =
                    validacionConfiguracion.Motivo;

                return RedirectToAction(
                    nameof(Detalle),
                    new { id }
                );
            }

            if (!inspeccion
                    .CincoDisparosSegregados)
            {
                TempData["Error"] =
                    "No se confirmó la segregación de los primeros cinco disparos.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id }
                );
            }

            if (inspeccion
                .CantidadDisparosConformes < 3)
            {
                TempData["Error"] =
                    "Se requieren al menos tres disparos conformes para liberar producción.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id }
                );
            }

            if (inspeccion
                    .ValidacionDimensional != true ||
                inspeccion
                    .ValidacionApariencia != true)
            {
                TempData["Error"] =
                    "Las validaciones dimensional y de apariencia deben ser conformes.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id }
                );
            }

            /*
             * Gauge y conductividad pueden ser:
             * true  = cumplen
             * null  = no aplican
             * false = no cumplen
             */
            if (inspeccion.ValidacionGauge == false ||
                inspeccion.ValidacionConductividad == false)
            {
                TempData["Error"] =
                    "Gauge o conductividad presentan un resultado no conforme.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id }
                );
            }

            var usuarioId =
                ObtenerUsuarioIdActual();

            var estadoAnterior =
                inspeccion.Estado;

            inspeccion.ResultadoCalidad =
                "VERDE";

            inspeccion.Etiqueta =
                "VERDE";

            inspeccion.Liberado =
                true;

            inspeccion.RequiereGP12 =
                false;

            inspeccion.EnContencion =
                false;

            inspeccion.EsScrap =
                false;

            inspeccion.Estado =
                CalidadEstados
                    .ProduccionLiberada;

            inspeccion.FechaLiberacionProduccion =
                DateTime.Now;

            inspeccion.UsuarioLiberacionProduccionID =
                usuarioId;

            MarcarModificacion(
                inspeccion,
                usuarioId
            );

            AgregarHistorial(
                inspeccion,
                CalidadMovimientos
                    .ProduccionLiberada,
                estadoAnterior,
                inspeccion.Estado,
                inspeccion.ResultadoCalidad,
                inspeccion.Etiqueta,
                "Calidad liberó la producción y asignó etiqueta verde.",
                usuarioId
            );

            await _context.SaveChangesAsync();

            TempData["Mensaje"] =
                "Producción liberada correctamente.";

            return RedirectToAction(
                nameof(Detalle),
                new { id }
            );
        }

        // =========================================================
        // GP12 Y MATERIAL NO CONFORME
        // =========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EnviarGP12(
            int id,
            string? comentario)
        {
            var inspeccion =
                await _context.CalidadInspecciones
                    .FirstOrDefaultAsync(x =>
                        x.InspeccionID == id);

            if (inspeccion == null)
                return NotFound();

            if (inspeccion.Estado ==
                CalidadEstados.Cerrada)
            {
                TempData["Error"] =
                    "Una inspección cerrada no puede enviarse a GP12.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id }
                );
            }

            var usuarioId =
                ObtenerUsuarioIdActual();

            var estadoAnterior =
                inspeccion.Estado;

            inspeccion.ResultadoCalidad =
                "NOK";

            inspeccion.Etiqueta =
                "AMARILLA";

            inspeccion.Liberado =
                false;

            inspeccion.RequiereGP12 =
                true;

            inspeccion.EnContencion =
                false;

            inspeccion.EsScrap =
                false;

            inspeccion.Estado =
                CalidadEstados.PendienteGP12;

            inspeccion.Observaciones =
                string.IsNullOrWhiteSpace(comentario)
                    ? inspeccion.Observaciones
                    : comentario.Trim();

            MarcarModificacion(
                inspeccion,
                usuarioId
            );

            AgregarHistorial(
                inspeccion,
                CalidadMovimientos
                    .EnviadoGP12,
                estadoAnterior,
                inspeccion.Estado,
                inspeccion.ResultadoCalidad,
                inspeccion.Etiqueta,
                string.IsNullOrWhiteSpace(comentario)
                    ? "Material enviado a GP12 para inspección reforzada."
                    : comentario.Trim(),
                usuarioId
            );

            await _context.SaveChangesAsync();

            TempData["Mensaje"] =
                "Material enviado a GP12.";

            return RedirectToAction(
                nameof(Detalle),
                new { id }
            );
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EnviarContencion(
            int id,
            string? comentario)
        {
            var inspeccion =
                await _context.CalidadInspecciones
                    .FirstOrDefaultAsync(x =>
                        x.InspeccionID == id);

            if (inspeccion == null)
                return NotFound();

            var usuarioId =
                ObtenerUsuarioIdActual();

            var estadoAnterior =
                inspeccion.Estado;

            inspeccion.ResultadoCalidad =
                "NOK";

            inspeccion.Etiqueta =
                "ROJA";

            inspeccion.Liberado =
                false;

            inspeccion.RequiereGP12 =
                false;

            inspeccion.EnContencion =
                true;

            /*
             * Importante:
             * material no conforme no equivale automáticamente
             * a scrap.
             */
            inspeccion.EsScrap =
                false;

            inspeccion.Estado =
                CalidadEstados
                    .MaterialNoConforme;

            inspeccion.Observaciones =
                string.IsNullOrWhiteSpace(comentario)
                    ? inspeccion.Observaciones
                    : comentario.Trim();

            MarcarModificacion(
                inspeccion,
                usuarioId
            );

            AgregarHistorial(
                inspeccion,
                "MATERIAL_NO_CONFORME",
                estadoAnterior,
                inspeccion.Estado,
                inspeccion.ResultadoCalidad,
                inspeccion.Etiqueta,
                string.IsNullOrWhiteSpace(comentario)
                    ? "Material identificado como no conforme y bloqueado."
                    : comentario.Trim(),
                usuarioId
            );

            await _context.SaveChangesAsync();

            TempData["Mensaje"] =
                "Material identificado como no conforme.";

            return RedirectToAction(
                nameof(Detalle),
                new { id }
            );
        }

        // =========================================================
        // CIERRE
        // =========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cerrar(
            int id)
        {
            var inspeccion =
                await _context.CalidadInspecciones
                    .FirstOrDefaultAsync(x =>
                        x.InspeccionID == id);

            if (inspeccion == null)
                return NotFound();

            var esFinal =
                CalidadEstados
                    .EsEstadoFinal(
                        inspeccion.Estado) ||

                inspeccion.Estado ==
                    CalidadEstados
                        .LegacyLiberada ||

                inspeccion.Estado ==
                    CalidadEstados
                        .LegacyContencion ||

                inspeccion.Estado ==
                    CalidadEstados
                        .LegacyScrap;

            if (!esFinal)
            {
                TempData["Error"] =
                    "El proceso todavía está activo y no puede cerrarse.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id }
                );
            }

            var usuarioId =
                ObtenerUsuarioIdActual();

            var estadoAnterior =
                inspeccion.Estado;

            inspeccion.Estado =
                CalidadEstados.Cerrada;

            MarcarModificacion(
                inspeccion,
                usuarioId
            );

            AgregarHistorial(
                inspeccion,
                CalidadMovimientos.Cierre,
                estadoAnterior,
                inspeccion.Estado,
                inspeccion.ResultadoCalidad,
                inspeccion.Etiqueta,
                "Registro de Calidad cerrado.",
                usuarioId
            );

            await _context.SaveChangesAsync();

            TempData["Mensaje"] =
                "Registro cerrado correctamente.";

            return RedirectToAction(
                nameof(Detalle),
                new { id }
            );
        }

        // =========================================================
        // ACCIONES ANTERIORES DE COMPATIBILIDAD
        // =========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Liberar(
            int id)
        {
            return LiberarProduccion(id);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> EnviarGPI2(
            int id)
        {
            return EnviarGP12(
                id,
                "Envío realizado desde la acción anterior GPI2."
            );
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Detener(
            int id)
        {
            var inspeccion =
                await _context.CalidadInspecciones
                    .FirstOrDefaultAsync(x =>
                        x.InspeccionID == id);

            if (inspeccion == null)
                return NotFound();

            var usuarioId =
                ObtenerUsuarioIdActual();

            var estadoAnterior =
                inspeccion.Estado;

            inspeccion.Liberado =
                false;

            inspeccion.ResultadoCalidad =
                "NOK";

            inspeccion.Estado =
                CalidadEstados
                    .AjustesSolicitados;

            MarcarModificacion(
                inspeccion,
                usuarioId
            );

            AgregarHistorial(
                inspeccion,
                CalidadMovimientos
                    .AjustesSolicitados,
                estadoAnterior,
                inspeccion.Estado,
                inspeccion.ResultadoCalidad,
                inspeccion.Etiqueta,
                "Proceso detenido y devuelto para corrección.",
                usuarioId
            );

            await _context.SaveChangesAsync();

            TempData["Mensaje"] =
                "Proceso detenido y enviado a corrección.";

            return RedirectToAction(
                nameof(Detalle),
                new { id }
            );
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult MarcarScrap(
            int id)
        {
            /*
             * El flujo actual no confirma que Calidad pueda
             * convertir automáticamente material NOK en scrap.
             */
            TempData["Error"] =
                "El material no puede marcarse automáticamente como scrap. Debe permanecer bloqueado hasta definir su disposición.";

            return RedirectToAction(
                nameof(Detalle),
                new { id }
            );
        }

        // =========================================================
        // CREACIÓN MANUAL ANTERIOR
        // Se conserva hasta actualizar completamente las vistas.
        // =========================================================

        [HttpGet]
        public IActionResult Crear()
        {
            var model =
                new CalidadFormViewModel
                {
                    CantidadTotal = 0,
                    CantidadRevisada = 0
                };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Crear(
            CalidadFormViewModel model)
        {
            NormalizarModelo(model);

            if (model.CantidadRevisada >
                model.CantidadTotal)
            {
                ModelState.AddModelError(
                    nameof(model.CantidadRevisada),
                    "La cantidad revisada no puede ser mayor a la cantidad total."
                );
            }

            if (!string.IsNullOrWhiteSpace(
                    model.ResultadoCalidad))
            {
                var documentosCompletos =
                    model.ChecklistValidado &&
                    model.HojaInspeccionProducto &&
                    model.HojaValidacionCalidad;

                if (!documentosCompletos)
                {
                    ModelState.AddModelError(
                        "",
                        "Para registrar un resultado primero debes validar la documentación."
                    );
                }
            }

            if (!ModelState.IsValid)
                return View(model);

            var usuarioId =
                ObtenerUsuarioIdActual();

            var inspeccion =
                new CalidadInspeccion
                {
                    CodigoBarras =
                        model.CodigoBarras,

                    OrdenTrabajo =
                        model.OrdenTrabajo,

                    NumeroParte =
                        model.NumeroParte,

                    Material =
                        model.Material,

                    Proceso =
                        model.Proceso,

                    Maquina =
                        model.Maquina,

                    CantidadTotal =
                        model.CantidadTotal,

                    CantidadRevisada =
                        model.CantidadRevisada,

                    CantidadPendiente =
                        CalcularPendiente(
                            model.CantidadTotal,
                            model.CantidadRevisada
                        ),

                    ChecklistValidado =
                        model.ChecklistValidado,

                    HojaInspeccionProducto =
                        model.HojaInspeccionProducto,

                    HojaValidacionCalidad =
                        model.HojaValidacionCalidad,

                    ResultadoCalidad =
                        model.ResultadoCalidad,

                    Observaciones =
                        model.Observaciones,

                    Estado =
                        CalidadEstados
                            .LegacyAbierta,

                    UsuarioCreacionID =
                        usuarioId,

                    FechaCreacion =
                        DateTime.Now
                };

            AplicarResultadoLegado(
                inspeccion
            );

            _context.CalidadInspecciones
                .Add(inspeccion);

            await _context.SaveChangesAsync();

            AgregarHistorial(
                inspeccion,
                "CREACION_MANUAL_LEGADA",
                null,
                inspeccion.Estado,
                inspeccion.ResultadoCalidad,
                inspeccion.Etiqueta,
                "Registro manual creado desde la vista anterior.",
                usuarioId
            );

            await _context.SaveChangesAsync();

            TempData["Mensaje"] =
                "Inspección registrada correctamente.";

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // LECTURA DE PLANEACIÓN
        // =========================================================

        private async Task<CalidadCorridaOrigenViewModel?>
            ObtenerCorridaOrigenAsync(
                int programaProduccionId)
        {
            const string sql = @"
SELECT
    pp.ProgramaProduccionID,

    pp.ReleaseID,
    pp.ReleaseDetalleID,

    pp.SolicitudProduccionID,
    pp.SolicitudProduccionDetalleID,

    COALESCE(
        NULLIF(sp.NumeroOFRecibida, ''),
        NULLIF(sp.FolioSolicitud, '')
    ) AS NumeroOF,

    pp.ClienteID,
    pp.ClienteNombre,

    pp.ParteID,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.DesignacionDescripcionSAP,

    pp.MaquinaID,
    pp.MaquinaCodigo,
    pp.MaquinaNombre,

    pp.MoldeID,
    pp.MoldeCodigo,

    COALESCE(
        pp.MaterialID,
        t.MaterialID
    ) AS MaterialID,

    COALESCE(
        NULLIF(pp.MaterialCodigo, ''),
        t.MaterialCodigo
    ) AS MaterialCodigo,

    COALESCE(
        NULLIF(pp.MaterialDescripcion, ''),
        t.MaterialDescripcion
    ) AS MaterialDescripcion,

    ISNULL(pp.CantidadProgramada, 0)
        AS CantidadProgramada,

    ISNULL(pp.CantidadProducida, 0)
        AS CantidadProducida,

    pp.FechaInicioProgramada,
    pp.FechaFinProgramada,

    ISNULL(pp.EstatusID, 1)
        AS EstatusProgramaID

FROM dbo.Planeacion_ProgramaProduccion pp

LEFT JOIN dbo.SolicitudesProduccion sp
    ON sp.SolicitudProduccionID =
       pp.SolicitudProduccionID

LEFT JOIN dbo.ERP_ParteDatosTecnicos t
    ON t.ParteID = pp.ParteID
   AND t.Activo = 1

WHERE pp.ProgramaProduccionID =
      @ProgramaProduccionID

  AND pp.Activo = 1;";

            await using var cn =
                new SqlConnection(
                    ConnectionString
                );

            await cn.OpenAsync();

            CalidadCorridaOrigenViewModel?
                corrida = null;

            await using (
                var cmd =
                    new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add(
                    "@ProgramaProduccionID",
                    SqlDbType.Int
                ).Value =
                    programaProduccionId;

                await using var rd =
                    await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    return null;

                corrida =
                    new CalidadCorridaOrigenViewModel
                    {
                        ProgramaProduccionID =
                            Convert.ToInt32(
                                rd["ProgramaProduccionID"]
                            ),

                        ReleaseID =
                            LeerIntNullable(
                                rd["ReleaseID"]
                            ),

                        ReleaseDetalleID =
                            LeerIntNullable(
                                rd["ReleaseDetalleID"]
                            ),

                        SolicitudProduccionID =
                            LeerIntNullable(
                                rd["SolicitudProduccionID"]
                            ),

                        SolicitudProduccionDetalleID =
                            LeerIntNullable(
                                rd["SolicitudProduccionDetalleID"]
                            ),

                        NumeroOF =
                            rd["NumeroOF"] as string,

                        ClienteID =
                            LeerIntNullable(
                                rd["ClienteID"]
                            ),

                        ClienteNombre =
                            rd["ClienteNombre"] as string,

                        ParteID =
                            LeerIntNullable(
                                rd["ParteID"]
                            ),

                        NumeroParte =
                            rd["NumeroParte"] as string,

                        ReferenciaSAP =
                            rd["ReferenciaSAP"] as string,

                        DescripcionParte =
                            rd["DesignacionDescripcionSAP"]
                                as string,

                        MaquinaID =
                            LeerIntNullable(
                                rd["MaquinaID"]
                            ),

                        MaquinaCodigo =
                            rd["MaquinaCodigo"] as string,

                        MaquinaNombre =
                            rd["MaquinaNombre"] as string,

                        MoldeID =
                            LeerIntNullable(
                                rd["MoldeID"]
                            ),

                        MoldeCodigo =
                            rd["MoldeCodigo"] as string,

                        MaterialID =
                            LeerIntNullable(
                                rd["MaterialID"]
                            ),

                        MaterialCodigo =
                            rd["MaterialCodigo"] as string,

                        MaterialDescripcion =
                            rd["MaterialDescripcion"]
                                as string,

                        CantidadProgramada =
                            Convert.ToInt32(
                                rd["CantidadProgramada"]
                            ),

                        CantidadProducida =
                            Convert.ToInt32(
                                rd["CantidadProducida"]
                            ),

                        FechaInicioProgramada =
                            LeerFechaNullable(
                                rd["FechaInicioProgramada"]
                            ),

                        FechaFinProgramada =
                            LeerFechaNullable(
                                rd["FechaFinProgramada"]
                            ),

                        EstatusProgramaID =
                            Convert.ToInt32(
                                rd["EstatusProgramaID"]
                            )
                    };
            }

            const string sqlOperadores = @"
SELECT
    po.PersonaID,
    po.RolOperador,

    LTRIM(RTRIM(
        ISNULL(p.Nombre, '') + ' ' +
        ISNULL(p.ApellidoPaterno, '') + ' ' +
        ISNULL(p.ApellidoMaterno, '')
    )) AS NombreCompleto

FROM dbo.Planeacion_ProgramaOperadores po

LEFT JOIN dbo.Persona p
    ON p.PersonaID = po.PersonaID

WHERE po.ProgramaProduccionID =
      @ProgramaProduccionID

  AND po.Activo = 1

ORDER BY
    CASE
        WHEN po.RolOperador = 'PRINCIPAL'
            THEN 1
        WHEN po.RolOperador = 'AUXILIAR'
            THEN 2
        ELSE 3
    END,
    po.ProgramaOperadorID;";

            await using (
                var cmd =
                    new SqlCommand(
                        sqlOperadores,
                        cn
                    ))
            {
                cmd.Parameters.Add(
                    "@ProgramaProduccionID",
                    SqlDbType.Int
                ).Value =
                    programaProduccionId;

                await using var rd =
                    await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    var personaId =
                        Convert.ToInt32(
                            rd["PersonaID"]
                        );

                    var rol =
                        rd["RolOperador"]
                            ?.ToString()
                            ?.Trim()
                            .ToUpperInvariant();

                    var nombre =
                        rd["NombreCompleto"]
                            as string;

                    if (rol == "PRINCIPAL" &&
                        !corrida
                            .OperadorPrincipalPersonaID
                            .HasValue)
                    {
                        corrida
                            .OperadorPrincipalPersonaID =
                            personaId;

                        corrida
                            .OperadorPrincipalNombre =
                            nombre;
                    }
                    else if (
                        rol == "AUXILIAR" &&
                        !corrida
                            .OperadorAuxiliarPersonaID
                            .HasValue)
                    {
                        corrida
                            .OperadorAuxiliarPersonaID =
                            personaId;

                        corrida
                            .OperadorAuxiliarNombre =
                            nombre;
                    }
                }
            }

            return corrida;
        }

        // =========================================================
        // VALIDACIÓN CONTRA CAMBIOS DE PLANEACIÓN
        // =========================================================

        private async Task<(
            bool Valida,
            string Motivo)>
            ValidarConfiguracionActualAsync(
                CalidadInspeccion inspeccion)
        {
            if (!inspeccion
                .ProgramaProduccionID
                .HasValue)
            {
                /*
                 * Registro manual anterior.
                 * No tiene una corrida para comparar.
                 */
                return (
                    true,
                    string.Empty
                );
            }

            var actual =
                await ObtenerCorridaOrigenAsync(
                    inspeccion
                        .ProgramaProduccionID
                        .Value
                );

            if (actual == null)
            {
                return (
                    false,
                    "La corrida de Planeación ya no existe o fue desactivada."
                );
            }

            var cambios =
                new List<string>();

            if (actual.SolicitudProduccionID !=
                inspeccion.SolicitudProduccionID)
            {
                cambios.Add(
                    "la Orden de Fabricación"
                );
            }

            if (actual
                    .SolicitudProduccionDetalleID !=
                inspeccion
                    .SolicitudProduccionDetalleID)
            {
                cambios.Add(
                    "el renglón de la OF"
                );
            }

            if (actual.ParteID !=
                inspeccion.ParteID)
            {
                cambios.Add(
                    "la parte"
                );
            }

            if (actual.MaquinaID !=
                inspeccion.MaquinaID)
            {
                cambios.Add(
                    "la máquina"
                );
            }

            if (actual.MoldeID !=
                inspeccion.MoldeID)
            {
                cambios.Add(
                    "el molde"
                );
            }

            if (actual.MaterialID !=
                inspeccion.MaterialID)
            {
                cambios.Add(
                    "el material"
                );
            }

            if (actual
                    .OperadorPrincipalPersonaID !=
                inspeccion
                    .OperadorPrincipalPersonaID)
            {
                cambios.Add(
                    "el operador principal"
                );
            }

            if (FechasDiferentes(
                    actual.FechaInicioProgramada,
                    inspeccion
                        .FechaInicioProgramada))
            {
                cambios.Add(
                    "la fecha u hora de inicio"
                );
            }

            if (FechasDiferentes(
                    actual.FechaFinProgramada,
                    inspeccion
                        .FechaFinProgramada))
            {
                cambios.Add(
                    "la fecha u hora de término"
                );
            }

            if (actual.EstatusProgramaID == 99)
            {
                cambios.Add(
                    "el programa fue cancelado"
                );
            }

            if (cambios.Count == 0)
            {
                return (
                    true,
                    string.Empty
                );
            }

            return (
                false,
                "La configuración autorizada ya no coincide con Planeación. Cambió: " +
                string.Join(", ", cambios) +
                ". Debe generarse una nueva revisión de Calidad."
            );
        }

        private async Task InvalidarConfiguracionAsync(
            CalidadInspeccion inspeccion,
            string motivo)
        {
            var usuarioId =
                ObtenerUsuarioIdActual();

            var estadoAnterior =
                inspeccion.Estado;

            inspeccion.ConfiguracionInvalidada =
                true;

            inspeccion.FechaInvalidacion =
                DateTime.Now;

            inspeccion.UsuarioInvalidacionID =
                usuarioId;

            inspeccion.MotivoInvalidacion =
                motivo;

            inspeccion.MotivoDevolucion =
                motivo;

            inspeccion.Liberado =
                false;

            inspeccion.Estado =
                CalidadEstados
                    .DevueltoPrearranque;

            MarcarModificacion(
                inspeccion,
                usuarioId
            );

            AgregarHistorial(
                inspeccion,
                CalidadMovimientos
                    .ConfiguracionInvalidada,
                estadoAnterior,
                inspeccion.Estado,
                inspeccion.ResultadoCalidad,
                inspeccion.Etiqueta,
                motivo,
                usuarioId
            );

            await _context.SaveChangesAsync();
        }

        // =========================================================
        // HELPERS DE HISTORIAL Y MODELOS
        // =========================================================

        private void AgregarHistorial(
            CalidadInspeccion inspeccion,
            string movimiento,
            string? estadoAnterior,
            string? estadoNuevo,
            string? resultadoCalidad,
            string? etiqueta,
            string? comentario,
            int? usuarioId)
        {
            var historial =
                new CalidadInspeccionHistorial
                {
                    InspeccionID =
                        inspeccion.InspeccionID,

                    Movimiento =
                        movimiento,

                    EstadoAnterior =
                        estadoAnterior,

                    EstadoNuevo =
                        estadoNuevo,

                    ResultadoCalidad =
                        resultadoCalidad,

                    Etiqueta =
                        etiqueta,

                    Comentario =
                        comentario,

                    UsuarioID =
                        usuarioId,

                    FechaMovimiento =
                        DateTime.Now
                };

            _context
                .CalidadInspeccionHistorial
                .Add(historial);
        }

        private static void AplicarPrimerasPiezas(
            CalidadInspeccion inspeccion,
            CalidadPrimerasPiezasViewModel model,
            int? usuarioId)
        {
            inspeccion.CincoDisparosSegregados =
                model.CincoDisparosSegregados;

            inspeccion.CantidadDisparosConformes =
                model.CantidadDisparosConformes;

            inspeccion.ValidacionDimensional =
                model.ValidacionDimensional;

            inspeccion.ValidacionApariencia =
                model.ValidacionApariencia;

            inspeccion.ValidacionGauge =
                model.ValidacionGauge;

            inspeccion.ValidacionConductividad =
                model.ValidacionConductividad;

            inspeccion.FechaValidacionPrimerasPiezas =
                DateTime.Now;

            inspeccion.UsuarioValidacionPrimerasPiezasID =
                usuarioId;

            if (!string.IsNullOrWhiteSpace(
                model.Observaciones))
            {
                inspeccion.Observaciones =
                    model.Observaciones.Trim();
            }
        }

        private static decimal CalcularPendiente(
            decimal total,
            decimal revisada)
        {
            var pendiente =
                total - revisada;

            return pendiente < 0
                ? 0
                : pendiente;
        }

        private static void NormalizarModelo(
            CalidadFormViewModel model)
        {
            model.CodigoBarras =
                model.CodigoBarras?.Trim();

            model.OrdenTrabajo =
                model.OrdenTrabajo?.Trim();

            model.NumeroParte =
                model.NumeroParte?.Trim();

            model.Material =
                model.Material?.Trim();

            model.Proceso =
                model.Proceso?.Trim();

            model.Maquina =
                model.Maquina?.Trim();

            model.ResultadoCalidad =
                model.ResultadoCalidad?
                    .Trim()
                    .ToUpperInvariant();

            model.Observaciones =
                model.Observaciones?.Trim();

            if (model.CantidadTotal < 0)
                model.CantidadTotal = 0;

            if (model.CantidadRevisada < 0)
                model.CantidadRevisada = 0;
        }

        private static void AplicarResultadoLegado(
            CalidadInspeccion inspeccion)
        {
            inspeccion.Liberado =
                false;

            inspeccion.RequiereGP12 =
                false;

            inspeccion.EnContencion =
                false;

            inspeccion.EsScrap =
                false;

            if (string.IsNullOrWhiteSpace(
                inspeccion.ResultadoCalidad))
            {
                inspeccion.Etiqueta =
                    null;

                inspeccion.Estado =
                    CalidadEstados
                        .LegacyAbierta;

                return;
            }

            switch (
                inspeccion
                    .ResultadoCalidad
                    .ToUpperInvariant())
            {
                case "VERDE":
                    inspeccion.Etiqueta =
                        "VERDE";

                    inspeccion.Liberado =
                        true;

                    inspeccion.Estado =
                        CalidadEstados
                            .LegacyLiberada;

                    break;

                case "AMARILLO":
                    inspeccion.Etiqueta =
                        "AMARILLA";

                    inspeccion.RequiereGP12 =
                        true;

                    inspeccion.Estado =
                        CalidadEstados
                            .PendienteGP12;

                    break;

                case "ROJO":
                    inspeccion.Etiqueta =
                        "ROJA";

                    inspeccion.EnContencion =
                        true;

                    inspeccion.EsScrap =
                        false;

                    inspeccion.Estado =
                        CalidadEstados
                            .MaterialNoConforme;

                    break;

                default:
                    inspeccion.ResultadoCalidad =
                        null;

                    inspeccion.Etiqueta =
                        null;

                    inspeccion.Estado =
                        CalidadEstados
                            .LegacyAbierta;

                    break;
            }
        }

        private static void MarcarModificacion(
            CalidadInspeccion inspeccion,
            int? usuarioId)
        {
            inspeccion.UsuarioModificacionID =
                usuarioId;

            inspeccion.FechaModificacion =
                DateTime.Now;
        }

        // =========================================================
        // HELPERS GENERALES
        // =========================================================

        private static List<string>
            ObtenerFaltantesCorrida(
                CalidadCorridaOrigenViewModel corrida)
        {
            var faltantes =
                new List<string>();

            if (!corrida
                .SolicitudProduccionID
                .HasValue)
            {
                faltantes.Add(
                    "Orden de Fabricación"
                );
            }

            if (!corrida
                .SolicitudProduccionDetalleID
                .HasValue)
            {
                faltantes.Add(
                    "renglón de la OF"
                );
            }

            if (string.IsNullOrWhiteSpace(
                corrida.NumeroOF))
            {
                faltantes.Add(
                    "número o folio de OF"
                );
            }

            if (!corrida.ParteID.HasValue)
            {
                faltantes.Add(
                    "parte"
                );
            }

            if (!corrida.MaquinaID.HasValue)
            {
                faltantes.Add(
                    "máquina"
                );
            }

            if (!corrida.MoldeID.HasValue)
            {
                faltantes.Add(
                    "molde"
                );
            }

            if (!corrida.MaterialID.HasValue)
            {
                faltantes.Add(
                    "material"
                );
            }

            if (!corrida
                .OperadorPrincipalPersonaID
                .HasValue)
            {
                faltantes.Add(
                    "operador principal"
                );
            }

            if (corrida.CantidadProgramada <= 0)
            {
                faltantes.Add(
                    "cantidad programada"
                );
            }

            return faltantes;
        }

        private static string?
            PrimerTextoDisponible(
                params string?[] valores)
        {
            return valores
                .FirstOrDefault(x =>
                    !string.IsNullOrWhiteSpace(x))
                ?.Trim();
        }

        private static string?
            UnirCodigoDescripcion(
                string? codigo,
                string? descripcion)
        {
            codigo =
                codigo?.Trim();

            descripcion =
                descripcion?.Trim();

            if (string.IsNullOrWhiteSpace(codigo))
                return descripcion;

            if (string.IsNullOrWhiteSpace(descripcion))
                return codigo;

            if (string.Equals(
                codigo,
                descripcion,
                StringComparison.OrdinalIgnoreCase))
            {
                return codigo;
            }

            return codigo +
                   " | " +
                   descripcion;
        }

        private static int?
            LeerIntNullable(
                object valor)
        {
            return valor == DBNull.Value
                ? null
                : Convert.ToInt32(valor);
        }

        private static DateTime?
            LeerFechaNullable(
                object valor)
        {
            return valor == DBNull.Value
                ? null
                : Convert.ToDateTime(valor);
        }

        private static bool FechasDiferentes(
            DateTime? fechaA,
            DateTime? fechaB)
        {
            if (!fechaA.HasValue &&
                !fechaB.HasValue)
            {
                return false;
            }

            if (fechaA.HasValue !=
                fechaB.HasValue)
            {
                return true;
            }

            return Math.Abs(
                (
                    fechaA!.Value -
                    fechaB!.Value
                ).TotalMinutes
            ) > 1;
        }

        private int?
            ObtenerUsuarioIdActual()
        {
            var claimValue =
                User.FindFirst("UsuarioID")
                    ?.Value ??

                User.FindFirst("UserId")
                    ?.Value ??

                User.FindFirst(
                    ClaimTypes.NameIdentifier
                )?.Value;

            if (int.TryParse(
                    claimValue,
                    out var usuarioId) &&
                usuarioId > 0)
            {
                return usuarioId;
            }

            try
            {
                var sessionId =
                    HttpContext.Session
                        .GetInt32("UsuarioID");

                if (sessionId.HasValue &&
                    sessionId.Value > 0)
                {
                    return sessionId.Value;
                }
            }
            catch
            {
                // Session no disponible.
            }

            return null;
        }
    }
}