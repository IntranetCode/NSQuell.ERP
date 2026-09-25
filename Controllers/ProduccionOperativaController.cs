using ERP.NSQuell.Models;
using ERP.NSQuell.Servicios.Produccion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;
using System.Text;

namespace ERP.NSQuell.Controllers;

[Route("Produccion/Operativa")]
public sealed partial class ProduccionOperativaController : Controller
{
    private readonly IConfiguration _configuration;
    private readonly AgendaOperativaService _agendaOperativaService;
    private readonly ILogger<ProduccionOperativaController> _logger;
    private readonly ICompositeViewEngine _viewEngine;

    public ProduccionOperativaController(
        IConfiguration configuration,
        AgendaOperativaService agendaOperativaService,
        ILogger<ProduccionOperativaController> logger,
        ICompositeViewEngine viewEngine)
    {
        _configuration = configuration;
        _agendaOperativaService = agendaOperativaService;
        _logger = logger;
        _viewEngine = viewEngine;
    }

    private string ConnectionString =>
        _configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("No se encontro la cadena de conexion DefaultConnection.");

    [HttpGet("")]
    [HttpGet("Centro")]
    [HttpGet("Centro/{programaProduccionId:int}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Centro(
        int programaProduccionId,
        string? origen = null,
        bool soloLectura = false,
        CancellationToken cancellationToken = default)
    {
        if (!UsuarioEnSesion()) return RespuestaSesionExpirada();
        if (programaProduccionId <= 0) return BadRequest(new { ok = false, mensaje = "Programa de Produccion no valido." });
        try
        {
            var vm = await ConstruirCentroOperativoAsync(programaProduccionId, origen, soloLectura, cancellationToken);
            if (vm == null) return NotFound(new { ok = false, mensaje = "No se encontro el programa de Produccion solicitado." });
            return PartialView("~/Views/ProduccionOperativa/_CentroOperativo.cshtml", vm);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar Centro Operativo. ProgramaProduccionID: {ProgramaProduccionID}. UsuarioID: {UsuarioID}", programaProduccionId, ObtenerUsuarioID());
            return StatusCode(StatusCodes.Status500InternalServerError, new { ok = false, mensaje = "No fue posible abrir el Centro Operativo: " + ex.Message });
        }
    }

    [HttpGet("Datos/{programaProduccionId:int}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Datos(
        int programaProduccionId,
        string? origen = null,
        bool soloLectura = false,
        CancellationToken cancellationToken = default)
    {
        if (!UsuarioEnSesion()) return RespuestaSesionExpirada();
        if (programaProduccionId <= 0) return BadRequest(new { ok = false, mensaje = "Programa de Produccion no valido." });
        try
        {
            var vm = await ConstruirCentroOperativoAsync(programaProduccionId, origen, soloLectura, cancellationToken);
            if (vm == null) return NotFound(new { ok = false, mensaje = "No se encontro el programa de Produccion solicitado." });
            return Json(new { ok = true, centro = vm });
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener datos del Centro Operativo. ProgramaProduccionID: {ProgramaProduccionID}. UsuarioID: {UsuarioID}", programaProduccionId, ObtenerUsuarioID());
            return StatusCode(StatusCodes.Status500InternalServerError, new { ok = false, mensaje = "No fue posible actualizar el Centro Operativo: " + ex.Message });
        }
    }

    [HttpGet("Estado/{programaProduccionId:int}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Estado(
        int programaProduccionId,
        CancellationToken cancellationToken = default)
    {
        if (!UsuarioEnSesion()) return RespuestaSesionExpirada();
        if (programaProduccionId <= 0) return BadRequest(new { ok = false, mensaje = "Programa de Produccion no valido." });
        try
        {
            var vm = await ConstruirCentroOperativoAsync(programaProduccionId, ProduccionOperativaOrigen.Calendario, false, cancellationToken);
            if (vm == null) return NotFound(new { ok = false, mensaje = "No se encontro el programa de Produccion solicitado." });
            return Json(new
            {
                ok = true,
                programaProduccionId = vm.ProgramaProduccionID,
                ejecucionProduccionId = vm.EjecucionProduccionID,
                of = vm.OFTexto,
                maquina = vm.MaquinaTexto,
                estado = vm.EstadoGeneral,
                estadoTexto = vm.EstadoGeneralTexto,
                estadoCentro = vm.EstadoCentro,
                prioridad = vm.Prioridad,
                requiereAtencion = vm.RequiereAtencionInmediata,
                bloqueada = vm.EstaBloqueada,
                motivoBloqueo = vm.MotivoBloqueoGeneral,
                etapa = vm.EtapaActual,
                etapaTexto = vm.EtapaActualTexto,
                porcentaje = vm.Metricas.PorcentajeAvance,
                accionActual = vm.AccionActual,
                siguienteAccion = vm.SiguienteAccion,
                parejaLhRh = vm.EsParejaLhRh ? vm.LhRh : null,
                fechaConsulta = vm.FechaConsulta
            });
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al consultar estado operativo. ProgramaProduccionID: {ProgramaProduccionID}. UsuarioID: {UsuarioID}", programaProduccionId, ObtenerUsuarioID());
            return StatusCode(StatusCodes.Status500InternalServerError, new { ok = false, mensaje = "No fue posible consultar el estado operativo: " + ex.Message });
        }
    }

    [HttpGet("Acciones/{programaProduccionId:int}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Acciones(
        int programaProduccionId,
        bool soloLectura = false,
        CancellationToken cancellationToken = default)
    {
        if (!UsuarioEnSesion()) return RespuestaSesionExpirada();
        if (programaProduccionId <= 0) return BadRequest(new { ok = false, mensaje = "Programa de Produccion no valido." });
        try
        {
            var vm = await ConstruirCentroOperativoAsync(programaProduccionId, ProduccionOperativaOrigen.Calendario, soloLectura, cancellationToken);
            if (vm == null) return NotFound(new { ok = false, mensaje = "No se encontro el programa de Produccion solicitado." });
            return Json(new
            {
                ok = true,
                programaProduccionId = vm.ProgramaProduccionID,
                ejecucionProduccionId = vm.EjecucionProduccionID,
                accionActual = vm.AccionActual,
                siguienteAccion = vm.SiguienteAccion,
                acciones = vm.AccionesVisibles,
                soloLectura = vm.Permisos.SoloLectura
            });
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al consultar acciones operativas. ProgramaProduccionID: {ProgramaProduccionID}. UsuarioID: {UsuarioID}", programaProduccionId, ObtenerUsuarioID());
            return StatusCode(StatusCodes.Status500InternalServerError, new { ok = false, mensaje = "No fue posible consultar las acciones disponibles: " + ex.Message });
        }
    }

    [HttpGet("ResolverAccion/{programaProduccionId:int}/{accionClave}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ResolverAccion(
        int programaProduccionId,
        string accionClave,
        bool soloLectura = false,
        CancellationToken cancellationToken = default)
    {
        if (!UsuarioEnSesion()) return RespuestaSesionExpirada();
        if (programaProduccionId <= 0 || string.IsNullOrWhiteSpace(accionClave)) return BadRequest(new { ok = false, mensaje = "La accion solicitada no es valida." });
        try
        {
            var vm = await ConstruirCentroOperativoAsync(programaProduccionId, ProduccionOperativaOrigen.Calendario, soloLectura, cancellationToken);
            if (vm == null) return NotFound(new { ok = false, mensaje = "No se encontro el programa de Produccion solicitado." });
            var accion = BuscarAccion(vm, accionClave);
            if (accion == null) return NotFound(new { ok = false, mensaje = "La accion no esta disponible para el estado actual de la OF." });
            return Json(new
            {
                ok = true,
                programaProduccionId = vm.ProgramaProduccionID,
                ejecucionProduccionId = vm.EjecucionProduccionID,
                accion,
                puedeEjecutar = accion.DisponibleParaUsuario,
                soloLectura = accion.SoloLectura || vm.Permisos.SoloLectura
            });
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al resolver accion del Centro Operativo. ProgramaProduccionID: {ProgramaProduccionID}. Accion: {Accion}. UsuarioID: {UsuarioID}", programaProduccionId, accionClave, ObtenerUsuarioID());
            return StatusCode(StatusCodes.Status500InternalServerError, new { ok = false, mensaje = "No fue posible resolver la accion: " + ex.Message });
        }
    }

    [HttpGet("Contenido/{programaProduccionId:int}/{accionClave}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Contenido(
        int programaProduccionId,
        string accionClave,
        string? origen = null,
        bool soloLectura = false,
        CancellationToken cancellationToken = default)
    {
        if (!UsuarioEnSesion()) return RespuestaSesionExpirada();
        if (programaProduccionId <= 0 || string.IsNullOrWhiteSpace(accionClave)) return BadRequest(new { ok = false, mensaje = "La accion solicitada no es valida." });
        try
        {
            var vm = await ConstruirCentroOperativoAsync(programaProduccionId, origen, soloLectura, cancellationToken);
            if (vm == null) return NotFound(new { ok = false, mensaje = "No se encontro el programa de Produccion solicitado." });
            var accion = BuscarAccion(vm, accionClave);
            if (accion == null) return NotFound(new { ok = false, mensaje = "La accion no esta disponible para el estado actual de la OF." });
            ViewData["AccionOperativa"] = accion;
            ViewData["AccionClave"] = accion.Clave;
            ViewData["SoloLectura"] = accion.SoloLectura || vm.Permisos.SoloLectura;
            if (EsAccionExternaAlCentroOperativo(accion.Clave, accion.AreaResponsable))
                return PartialView("~/Views/ProduccionOperativa/_EsperaAreaExterna.cshtml", vm);
            if (!string.IsNullOrWhiteSpace(accion.VistaParcial) && ExisteVista(accion.VistaParcial)) return PartialView(accion.VistaParcial, vm);
            return PartialView("~/Views/ProduccionOperativa/_ContenidoAccion.cshtml", vm);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar contenido operativo. ProgramaProduccionID: {ProgramaProduccionID}. Accion: {Accion}. UsuarioID: {UsuarioID}", programaProduccionId, accionClave, ObtenerUsuarioID());
            return StatusCode(StatusCodes.Status500InternalServerError, new { ok = false, mensaje = "No fue posible abrir la accion: " + ex.Message });
        }
    }

    private async Task<ProduccionCentroOperativoVm?> ConstruirCentroOperativoAsync(
        int programaProduccionId,
        string? origen,
        bool soloLectura,
        CancellationToken cancellationToken)
    {
        var usuarioId = ObtenerUsuarioID();
        if (usuarioId <= 0) return null;
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(cancellationToken);
        var basePrograma = await ObtenerProgramaBaseAsync(programaProduccionId, cn, cancellationToken);
        if (basePrograma == null) return null;
        var usuarioInterno = await ObtenerUsuarioOperativoAsync(usuarioId, cn, cancellationToken);
        if (usuarioInterno == null) throw new InvalidOperationException("No fue posible identificar el usuario activo en el ERP.");
        var usuario = MapearUsuario(usuarioInterno);
        var permisos = ConstruirPermisos(usuarioInterno, soloLectura);
        var agendaItem = await ObtenerAgendaItemAsync(basePrograma, usuarioId, cancellationToken);
        var vm = MapearCentroBase(basePrograma, agendaItem, usuario, permisos, origen);
        vm.Metricas = await ObtenerMetricasAsync(basePrograma.EjecucionProduccionID, basePrograma.ParteID, cn, cancellationToken);
        CompletarMetricasDesdeCentro(vm);
        MapearPasosYAcciones(vm, agendaItem, usuarioInterno);
        await AgregarAccionesComplementariasAsync(vm, usuarioInterno, cn, cancellationToken);
        AplicarOrdenFlujoOperativo(vm);
        NormalizarAccionActualYSiguiente(vm);
        NormalizarBloqueoActual(vm);
        vm.EtapaActual = ResolverEtapaActual(vm);
        if (vm.AccionActual != null) vm.EtapaActual = vm.AccionActual.Etapa;
        vm.Resumen = ConstruirResumenProceso(vm);
        vm.Bloqueos = ConstruirBloqueos(vm);
        vm.Alertas = ConstruirAlertas(vm);
        vm.EstadoCentro = ResolverEstadoCentro(vm);
        return vm;
    }

    private async Task<AgendaOperativaItemVm?> ObtenerAgendaItemAsync(
        ProgramaCentroDto programa,
        int usuarioId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var inicioReferencia = programa.FechaInicioProgramada ?? programa.FechaInicioReal ?? DateTime.Now;
        var finReferencia = programa.FechaFinProgramada ?? programa.FechaFinReal ?? inicioReferencia.AddDays(1);
        var desde = inicioReferencia.AddDays(-2);
        var hasta = finReferencia.AddDays(2);
        if (hasta <= desde) hasta = desde.AddDays(4);
        var filtros = new AgendaOperativaFiltroVm
        {
            MaquinaID = programa.MaquinaID,
            Busqueda = null,
            Area = null,
            Estado = null,
            SoloAtencion = false,
            SoloBloqueadas = false,
            IncluirProduciendo = true,
            VentanaHoras = 72
        };
        var agenda = await _agendaOperativaService.ObtenerAgendaAsync(filtros, usuarioId, desde, hasta);
        return agenda.Items.FirstOrDefault(x => x.ProgramaProduccionID == programa.ProgramaProduccionID);
    }

    private static ProduccionCentroOperativoVm MapearCentroBase(
        ProgramaCentroDto p,
        AgendaOperativaItemVm? agenda,
        ProduccionOperativaUsuarioVm usuario,
        ProduccionOperativaPermisosVm permisos,
        string? origen)
    {
        var estado = agenda?.EstadoGeneral ?? ResolverEstadoGeneralBase(p);
        return new ProduccionCentroOperativoVm
        {
            FechaConsulta = DateTime.Now,
            Origen = NormalizarOrigen(origen),
            ProgramaProduccionID = p.ProgramaProduccionID,
            EjecucionProduccionID = p.EjecucionProduccionID,
            SolicitudProduccionID = p.SolicitudProduccionID,
            SolicitudProduccionDetalleID = p.SolicitudProduccionDetalleID,
            ReleaseDetalleID = p.ReleaseDetalleID,
            NumeroOF = PrimeroNoVacio(agenda?.NumeroOF, p.NumeroOFRecibida, p.FolioSolicitud),
            NumeroOFRecibida = p.NumeroOFRecibida,
            FolioSolicitud = p.FolioSolicitud,
            ClienteID = p.ClienteID,
            ClienteNombre = p.ClienteNombre,
            ParteID = p.ParteID,
            NumeroParte = PrimeroNoVacio(agenda?.NumeroParte, p.NumeroParte),
            ReferenciaSAP = PrimeroNoVacio(agenda?.ReferenciaSAP, p.ReferenciaSAP),
            DescripcionParte = PrimeroNoVacio(agenda?.DescripcionParte, p.DescripcionParte),
            MaquinaID = p.MaquinaID,
            MaquinaCodigo = PrimeroNoVacio(agenda?.MaquinaCodigo, p.MaquinaCodigo),
            MaquinaNombre = PrimeroNoVacio(agenda?.MaquinaNombre, p.MaquinaNombre),
            MoldeID = p.MoldeID,
            MoldeCodigo = PrimeroNoVacio(agenda?.MoldeCodigo, p.MoldeCodigo),
            MoldeNombre = agenda?.MoldeNombre,
            CantidadProgramada = agenda?.CantidadProgramada ?? p.CantidadProgramada,
            CantidadProducida = agenda?.CantidadProducida ?? p.CantidadProducida,
            FechaInicioProgramada = agenda?.FechaInicioProgramada ?? p.FechaInicioProgramada,
            FechaFinProgramada = agenda?.FechaFinProgramada ?? p.FechaFinProgramada,
            FechaInicioReal = agenda?.FechaInicioReal ?? p.FechaInicioReal,
            FechaFinReal = agenda?.FechaFinReal ?? p.FechaFinReal,
            EstatusProgramaID = agenda?.EstatusProgramaID ?? p.EstatusProgramaID,
            EstatusEjecucionID = agenda?.EstatusEjecucionID ?? p.EstatusEjecucionID,
            EstadoGeneral = estado,
            EstadoGeneralDetalle = agenda?.EstadoGeneralDetalle,
            Prioridad = agenda?.Prioridad ?? AgendaOperativaPrioridad.Normal,
            EsUrgente = agenda?.EsUrgente ?? false,
            TieneParoAbierto = agenda?.TieneParoAbierto ?? false,
            TieneInterrupcionUrgente = agenda?.TieneInterrupcionUrgente ?? false,
            MaquinaLiberada = agenda?.MaquinaLiberada ?? p.FechaLiberacionMaquina.HasValue,
            RequiereAtencionInmediata = agenda?.RequiereAtencionInmediata ?? false,
            EstaBloqueada = agenda?.EstaBloqueada ?? false,
            MotivoBloqueoGeneral = agenda?.MotivoBloqueo,
            Usuario = usuario,
            Permisos = permisos,
            Agenda = agenda,
            LhRh = MapearLhRh(agenda?.ProduccionLhRh, p.ProgramaProduccionID, p.EjecucionProduccionID)
        };
    }

    private static void MapearPasosYAcciones(
        ProduccionCentroOperativoVm vm,
        AgendaOperativaItemVm? agenda,
        UsuarioOperativoDto usuario)
    {
        if (agenda == null) return;
        vm.Pasos = agenda.Pasos
            .OrderBy(x => x.Orden)
            .Select(x => MapearPaso(x, vm.Permisos, usuario, vm.Permisos.SoloLectura))
            .ToList();
        vm.AccionesDisponibles = agenda.Pasos
            .Where(x => x.Aplica && !string.Equals(x.Estado, AgendaOperativaEstadoPaso.NoAplica, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Orden)
            .Select(x => MapearAccionPaso(x, agenda, vm.Permisos, usuario, vm.Permisos.SoloLectura))
            .ToList();
        if (agenda.AccionActual != null)
        {
            vm.AccionActual = MapearAccionAgenda(agenda.AccionActual, agenda, vm.Permisos, usuario, vm.Permisos.SoloLectura);
            ReemplazarAccionMismaClave(vm.AccionesDisponibles, vm.AccionActual);
        }
        if (agenda.SiguienteAccion != null)
        {
            vm.SiguienteAccion = MapearAccionAgenda(agenda.SiguienteAccion, agenda, vm.Permisos, usuario, vm.Permisos.SoloLectura);
            ReemplazarAccionMismaClave(vm.AccionesDisponibles, vm.SiguienteAccion);
        }
    }

    private async Task AgregarAccionesComplementariasAsync(
        ProduccionCentroOperativoVm vm,
        UsuarioOperativoDto usuario,
        SqlConnection cn,
        CancellationToken cancellationToken)
    {
        if (vm.Pasos.Any(x => x.Aplica && string.Equals(x.Clave, AgendaOperativaPasoClave.CambioMolde, StringComparison.OrdinalIgnoreCase)))
        {
            var preparacionId = await ObtenerPreparacionCambioMoldeIdAsync(vm.ProgramaProduccionID, cn, cancellationToken);
            if (preparacionId.HasValue)
            {
                var estadoChecklistMolde = await ObtenerEstadoChecklistCambioMoldeOperativoAsync(preparacionId.Value, cn, cancellationToken);
                var estadoPasoChecklist = estadoChecklistMolde.Completo
                    ? AgendaOperativaEstadoPaso.Completado
                    : estadoChecklistMolde.EnProceso
                        ? AgendaOperativaEstadoPaso.EnProceso
                        : AgendaOperativaEstadoPaso.Pendiente;
                var detalleChecklist = estadoChecklistMolde.Completo
                    ? "Checklist GQ-F-PR01-03 completo. El cambio de molde puede cerrarse cuando la actividad física también haya concluido."
                    : estadoChecklistMolde.EnProceso
                        ? "Checklist GQ-F-PR01-03 en captura. Se trabaja en paralelo con el desmontaje/montaje y debe finalizar antes de cerrar el cambio de molde."
                        : "Checklist GQ-F-PR01-03 pendiente. Debe atenderse en paralelo con el cambio de molde y completarse antes de finalizarlo.";

                ReemplazarPasoMismaClave(vm.Pasos, new ProduccionOperativaPasoVm
                {
                    Orden = 61,
                    Clave = ProduccionOperativaAccionClave.ChecklistCambioMolde,
                    Nombre = "Checklist GQ-F-PR01-03",
                    Descripcion = "Checklist físico de desmontaje y montaje de molde.",
                    Etapa = ProduccionOperativaEtapa.Preparacion,
                    AreaResponsable = AgendaOperativaArea.Smed,
                    Estado = estadoPasoChecklist,
                    Aplica = true,
                    Completado = estadoChecklistMolde.Completo,
                    EnProceso = estadoChecklistMolde.EnProceso,
                    Bloqueado = false,
                    BloqueaFlujo = false,
                    PuedeEjecutarUsuario = !vm.Permisos.SoloLectura && PuedeEjecutarAccion(ProduccionOperativaAccionClave.ChecklistCambioMolde, AgendaOperativaArea.Smed, vm.Permisos, usuario),
                    Detalle = detalleChecklist,
                    AccionClave = ProduccionOperativaAccionClave.ChecklistCambioMolde
                });

                ReemplazarAccionMismaClave(vm.AccionesDisponibles, new ProduccionOperativaAccionVm
                {
                    Orden = 61,
                    Clave = ProduccionOperativaAccionClave.ChecklistCambioMolde,
                    Titulo = "Checklist GQ-F-PR01-03",
                    Descripcion = detalleChecklist,
                    Etapa = ProduccionOperativaEtapa.Preparacion,
                    AreaResponsable = AgendaOperativaArea.Smed,
                    Estado = estadoPasoChecklist,
                    Prioridad = AgendaOperativaPrioridad.Normal,
                    Aplica = true,
                    Visible = true,
                    Completada = estadoChecklistMolde.Completo,
                    EnProceso = estadoChecklistMolde.EnProceso,
                    EsEjecutable = !estadoChecklistMolde.Completo,
                    PuedeEjecutarUsuario = !estadoChecklistMolde.Completo && PuedeEjecutarAccion(ProduccionOperativaAccionClave.ChecklistCambioMolde, AgendaOperativaArea.Smed, vm.Permisos, usuario),
                    SoloLectura = vm.Permisos.SoloLectura || estadoChecklistMolde.Completo,
                    Icono = "bi-ui-checks-grid",
                    TextoBoton = estadoChecklistMolde.Completo ? "Ver checklist" : "Abrir checklist",
                    TipoContenido = ProduccionOperativaTipoContenido.Checklist,
                    ModoApertura = ProduccionOperativaModoApertura.PartialAjax,
                    VistaParcial = ResolverVistaParcialFutura(ProduccionOperativaAccionClave.ChecklistCambioMolde),
                    Destino = new ProduccionOperativaDestinoVm
                    {
                        Controlador = "ProduccionPreparacion",
                        Accion = "ChecklistCambioMolde",
                        ParametroId = "id",
                        IdDestino = preparacionId.Value,
                        MetodoHttp = "GET",
                        UsarAjax = true
                    }
                });
            }
        }
        if (vm.EjecucionProduccionID.HasValue)
        {
            var existeAccionParo = vm.AccionesDisponibles.Any(x => string.Equals(x.Clave, AgendaOperativaPasoClave.Paro, StringComparison.OrdinalIgnoreCase));
            var ejecucionPermiteParos = vm.EstatusEjecucionID is ProduccionEstatus.EnProduccion or ProduccionEstatus.Pausado;
            if (!existeAccionParo && ejecucionPermiteParos)
            {
                AgregarSiNoExiste(vm.AccionesDisponibles, new ProduccionOperativaAccionVm
                {
                    Orden = 140,
                    Clave = AgendaOperativaPasoClave.Paro,
                    Titulo = "Paros / interrupciones",
                    Descripcion = vm.TieneParoAbierto
                        ? "Existe un paro abierto. Consulta o cierra la interrupción conforme a las reglas vigentes."
                        : "Registrar un paro operativo cuando la producción en serie deba detenerse.",
                    Etapa = ProduccionOperativaEtapa.Produccion,
                    AreaResponsable = AgendaOperativaArea.Produccion,
                    Estado = vm.TieneParoAbierto ? AgendaOperativaEstadoPaso.EnProceso : AgendaOperativaEstadoPaso.Listo,
                    Prioridad = vm.TieneParoAbierto ? AgendaOperativaPrioridad.Alta : AgendaOperativaPrioridad.Normal,
                    Aplica = true,
                    Visible = true,
                    EnProceso = vm.TieneParoAbierto,
                    Bloqueada = false,
                    BloqueaFlujo = vm.TieneParoAbierto,
                    EsEjecutable = true,
                    PuedeEjecutarUsuario = vm.Permisos.PuedeGestionarParos,
                    SoloLectura = vm.Permisos.SoloLectura,
                    Icono = "bi-pause-circle",
                    TextoBoton = vm.TieneParoAbierto ? "Atender paro" : "Registrar paro",
                    TipoContenido = ProduccionOperativaTipoContenido.Paro,
                    ModoApertura = ProduccionOperativaModoApertura.PartialAjax,
                    VistaParcial = ResolverVistaParcialFutura(AgendaOperativaPasoClave.Paro),
                    Destino = DestinoDetalleProduccion(vm)
                });
            }

            var cambioTurno = await ObtenerDisponibilidadCambioTurnoOperativoAsync(vm, cn, cancellationToken);
            if (cambioTurno.Mostrar)
            {
                var accionCambioTurno = CrearAccionComplementaria(
                    vm,
                    usuario,
                    175,
                    ProduccionOperativaAccionClave.CambioTurno,
                    "Cambio de turno",
                    cambioTurno.Descripcion,
                    ProduccionOperativaEtapa.Produccion,
                    AgendaOperativaArea.Produccion,
                    ProduccionOperativaTipoContenido.CambioTurno,
                    "bi-arrow-left-right",
                    vm.Permisos.PuedeGestionarCambioTurno);
                accionCambioTurno.TextoBoton = cambioTurno.TextoBoton;
                accionCambioTurno.FechaObjetivo = cambioTurno.FechaObjetivo;
                accionCambioTurno.Prioridad = cambioTurno.TieneEntregaPendiente || cambioTurno.YaInicio
                    ? AgendaOperativaPrioridad.Alta
                    : AgendaOperativaPrioridad.Normal;
                accionCambioTurno.EstaVencida = cambioTurno.YaInicio && !cambioTurno.TieneEntregaPendiente;
                accionCambioTurno.MinutosDesfase = cambioTurno.MinutosDesfase;
                AgregarSiNoExiste(vm.AccionesDisponibles, accionCambioTurno);
            }
            var tiempoExtra = await ObtenerDisponibilidadTiempoExtraOperativoAsync(vm, cn, cancellationToken);
            if (tiempoExtra.Mostrar)
            {
                var accionTiempoExtra = CrearAccionComplementaria(
                    vm,
                    usuario,
                    176,
                    ProduccionOperativaAccionClave.TiempoExtra,
                    "Tiempo extra",
                    tiempoExtra.Descripcion,
                    ProduccionOperativaEtapa.Produccion,
                    AgendaOperativaArea.Operador,
                    ProduccionOperativaTipoContenido.TiempoExtra,
                    "bi-clock-history",
                    vm.Permisos.PuedeGestionarTiempoExtra);
                accionTiempoExtra.TextoBoton = tiempoExtra.TextoBoton;
                accionTiempoExtra.FechaObjetivo = tiempoExtra.FechaObjetivo;
                accionTiempoExtra.Prioridad = tiempoExtra.CorteVencido
                    ? AgendaOperativaPrioridad.Alta
                    : AgendaOperativaPrioridad.Normal;
                accionTiempoExtra.EstaVencida = tiempoExtra.CorteVencido;
                accionTiempoExtra.MinutosDesfase = tiempoExtra.MinutosDesfase;
                AgregarSiNoExiste(vm.AccionesDisponibles, accionTiempoExtra);
            }
        }
        AgregarSiNoExiste(vm.AccionesDisponibles, new ProduccionOperativaAccionVm
        {
            Orden = 900,
            Clave = ProduccionOperativaAccionClave.Historial,
            Titulo = "Historial operativo",
            Descripcion = "Consultar los movimientos y eventos de la OF.",
            Etapa = vm.EtapaActual,
            AreaResponsable = AgendaOperativaArea.Produccion,
            Estado = AgendaOperativaEstadoPaso.Listo,
            Prioridad = AgendaOperativaPrioridad.Baja,
            Aplica = true,
            Visible = true,
            EsEjecutable = true,
            PuedeEjecutarUsuario = vm.Permisos.PuedeVerHistorial,
            SoloLectura = false,
            Icono = "bi-clock-history",
            TextoBoton = "Ver historial",
            TipoContenido = ProduccionOperativaTipoContenido.Historial,
            ModoApertura = ProduccionOperativaModoApertura.PartialAjax,
            VistaParcial = ResolverVistaParcialFutura(ProduccionOperativaAccionClave.Historial),
            Destino = DestinoDetalleProduccion(vm)
        });
        AgregarSiNoExiste(vm.AccionesDisponibles, new ProduccionOperativaAccionVm
        {
            Orden = 990,
            Clave = ProduccionOperativaAccionClave.DetalleCompleto,
            Titulo = "Detalle completo",
            Descripcion = "Vista de respaldo con toda la informacion de Produccion.",
            Etapa = vm.EtapaActual,
            AreaResponsable = AgendaOperativaArea.Produccion,
            Estado = AgendaOperativaEstadoPaso.Listo,
            Prioridad = AgendaOperativaPrioridad.Baja,
            Aplica = true,
            Visible = true,
            EsEjecutable = true,
            PuedeEjecutarUsuario = vm.Permisos.PuedeVerDetalleCompleto,
            SoloLectura = false,
            Icono = "bi-box-arrow-up-right",
            TextoBoton = "Abrir detalle",
            TipoContenido = ProduccionOperativaTipoContenido.Resumen,
            ModoApertura = ProduccionOperativaModoApertura.PaginaRespaldo,
            VistaParcial = ResolverVistaParcialFutura(ProduccionOperativaAccionClave.DetalleCompleto),
            Destino = DestinoDetalleProduccion(vm)
        });
        vm.AccionesDisponibles = vm.AccionesDisponibles.OrderBy(x => x.Orden).ThenBy(x => x.Titulo).ToList();
    }

    private static ProduccionOperativaAccionVm CrearAccionComplementaria(
        ProduccionCentroOperativoVm vm,
        UsuarioOperativoDto usuario,
        int orden,
        string clave,
        string titulo,
        string descripcion,
        string etapa,
        string area,
        string tipoContenido,
        string icono,
        bool permiso)
    {
        var ejecucionActiva = vm.EstatusEjecucionID is ProduccionEstatus.EnPreparacion or ProduccionEstatus.EnProduccion or ProduccionEstatus.Pausado or ProduccionEstatus.TerminadoParcial;
        return new ProduccionOperativaAccionVm
        {
            Orden = orden,
            Clave = clave,
            Titulo = titulo,
            Descripcion = descripcion,
            Etapa = etapa,
            AreaResponsable = area,
            Estado = ejecucionActiva ? AgendaOperativaEstadoPaso.Listo : AgendaOperativaEstadoPaso.Esperando,
            Prioridad = AgendaOperativaPrioridad.Normal,
            Aplica = vm.EjecucionProduccionID.HasValue,
            Visible = vm.EjecucionProduccionID.HasValue,
            EsEjecutable = ejecucionActiva,
            PuedeEjecutarUsuario = permiso && PuedeEjecutarAccion(clave, area, vm.Permisos, usuario),
            SoloLectura = vm.Permisos.SoloLectura,
            Icono = icono,
            TextoBoton = "Atender",
            TipoContenido = tipoContenido,
            ModoApertura = ProduccionOperativaModoApertura.PartialAjax,
            VistaParcial = ResolverVistaParcialFutura(clave),
            Destino = DestinoDetalleProduccion(vm)
        };
    }

    private static ProduccionOperativaPasoVm MapearPaso(
        AgendaOperativaPasoVm p,
        ProduccionOperativaPermisosVm permisos,
        UsuarioOperativoDto usuario,
        bool soloLectura)
    {
        return new ProduccionOperativaPasoVm
        {
            Orden = p.Orden,
            Clave = p.Clave,
            Nombre = p.Nombre,
            Descripcion = p.Descripcion,
            Etapa = ResolverEtapaPorClave(p.Clave),
            AreaResponsable = p.AreaResponsable,
            Estado = p.Estado,
            Aplica = p.Aplica,
            Completado = p.Completado,
            EnProceso = p.EnProceso,
            Bloqueado = p.Bloqueado,
            BloqueaFlujo = p.BloqueaFlujo,
            PuedeEjecutarUsuario = !soloLectura && PuedeEjecutarAccion(p.Clave, p.AreaResponsable, permisos, usuario),
            MotivoBloqueo = p.MotivoBloqueo,
            Detalle = p.Detalle,
            FechaObjetivo = p.FechaObjetivo,
            FechaInicioReal = p.FechaInicioReal,
            FechaFinReal = p.FechaFinReal,
            EstaVencido = p.EstaVencido,
            MinutosDesfase = p.MinutosDesfase,
            AccionClave = p.Clave
        };
    }

    private static ProduccionOperativaAccionVm MapearAccionPaso(AgendaOperativaPasoVm p, AgendaOperativaItemVm item, ProduccionOperativaPermisosVm permisos, UsuarioOperativoDto usuario, bool soloLectura)
    {
        var esExterna = EsAccionExternaAlCentroOperativo(p.Clave, p.AreaResponsable);
        var esEjecutableBase = !esExterna && (!string.Equals(p.Estado, AgendaOperativaEstadoPaso.Esperando, StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(p.Controlador));
        var textoNormal = esExterna ? TextoEsperaAreaExterna(p.AreaResponsable) : TextoBotonAccion(p.Clave);
        var politica = ResolverPoliticaReaperturaPaso(p.Clave, p.Completado, item.EjecucionProduccionID, soloLectura, esExterna, textoNormal);
        var puedeEjecutar = !politica.SoloLectura && !esExterna && PuedeEjecutarAccion(p.Clave, p.AreaResponsable, permisos, usuario);

        return new ProduccionOperativaAccionVm
        {
            Orden = p.Orden,
            Clave = p.Clave,
            Titulo = TituloAccion(p.Clave, p.Nombre),
            Descripcion = p.Detalle ?? p.MotivoBloqueo ?? p.Descripcion,
            Etapa = ResolverEtapaPorClave(p.Clave),
            AreaResponsable = p.AreaResponsable,
            Estado = p.Estado,
            Prioridad = p.Bloqueado ? AgendaOperativaPrioridad.Critica : p.EstaVencido ? AgendaOperativaPrioridad.Alta : AgendaOperativaPrioridad.Normal,
            Aplica = p.Aplica,
            Visible = p.Aplica && politica.Visible,
            Completada = p.Completado,
            EnProceso = p.EnProceso,
            Bloqueada = p.Bloqueado,
            BloqueaFlujo = p.BloqueaFlujo,
            EsEjecutable = esEjecutableBase && !politica.SoloLectura,
            PuedeEjecutarUsuario = puedeEjecutar,
            SoloLectura = politica.SoloLectura,
            MotivoBloqueo = p.MotivoBloqueo,
            FechaObjetivo = p.FechaObjetivo,
            FechaInicioReal = p.FechaInicioReal,
            FechaFinReal = p.FechaFinReal,
            EstaVencida = p.EstaVencido,
            MinutosDesfase = p.MinutosDesfase,
            Icono = IconoAccion(p.Clave),
            TextoBoton = politica.TextoBoton,
            TipoContenido = ResolverTipoContenido(p.Clave),
            ModoApertura = esExterna ? ProduccionOperativaModoApertura.SoloLectura : ProduccionOperativaModoApertura.PartialAjax,
            VistaParcial = esExterna ? null : ResolverVistaParcialFutura(p.Clave),
            Destino = esExterna ? new ProduccionOperativaDestinoVm() : ResolverDestinoPaso(p, item)
        };
    }
    private static ProduccionOperativaAccionVm MapearAccionAgenda(AgendaOperativaAccionVm a, AgendaOperativaItemVm item, ProduccionOperativaPermisosVm permisos, UsuarioOperativoDto usuario, bool soloLectura)
    {
        var paso = item.Pasos.FirstOrDefault(x => string.Equals(x.Clave, a.Clave, StringComparison.OrdinalIgnoreCase));
        var esExterna = EsAccionExternaAlCentroOperativo(a.Clave, a.AreaResponsable);
        var completada = paso?.Completado == true;
        var textoNormal = esExterna ? TextoEsperaAreaExterna(a.AreaResponsable) : (string.IsNullOrWhiteSpace(a.TextoBoton) ? TextoBotonAccion(a.Clave) : a.TextoBoton);
        var politica = ResolverPoliticaReaperturaPaso(a.Clave, completada, item.EjecucionProduccionID, soloLectura, esExterna, textoNormal);
        var puedeEjecutar = !politica.SoloLectura && !esExterna && PuedeEjecutarAccion(a.Clave, a.AreaResponsable, permisos, usuario);

        return new ProduccionOperativaAccionVm
        {
            Orden = paso?.Orden ?? 500,
            Clave = a.Clave,
            Titulo = a.Titulo,
            Descripcion = a.Descripcion,
            Etapa = ResolverEtapaPorClave(a.Clave),
            AreaResponsable = a.AreaResponsable,
            ResponsableNombre = a.ResponsableNombre,
            ResponsableUsuarioID = a.ResponsableUsuarioID,
            Estado = paso?.Estado ?? AgendaOperativaEstadoPaso.Pendiente,
            Prioridad = a.Prioridad,
            Aplica = true,
            Visible = politica.Visible,
            Completada = completada,
            EnProceso = paso?.EnProceso == true,
            Bloqueada = paso?.Bloqueado == true,
            BloqueaFlujo = a.BloqueaFlujo,
            EsEjecutable = !politica.SoloLectura && !esExterna && a.EsEjecutable,
            PuedeEjecutarUsuario = puedeEjecutar,
            SoloLectura = politica.SoloLectura,
            MotivoBloqueo = paso?.MotivoBloqueo,
            FechaObjetivo = a.FechaObjetivo,
            FechaDisponibleDesde = a.FechaDisponibleDesde,
            EstaVencida = a.EstaVencida,
            MinutosDesfase = a.MinutosDesfase,
            RequiereConfirmacion = !politica.SoloLectura && !esExterna && a.RequiereConfirmacion,
            TextoConfirmacion = !politica.SoloLectura && !esExterna ? a.TextoConfirmacion : null,
            Icono = string.IsNullOrWhiteSpace(a.Icono) ? IconoAccion(a.Clave) : a.Icono,
            TextoBoton = politica.TextoBoton,
            TipoContenido = ResolverTipoContenido(a.Clave),
            ModoApertura = esExterna ? ProduccionOperativaModoApertura.SoloLectura : ProduccionOperativaModoApertura.PartialAjax,
            VistaParcial = esExterna ? null : ResolverVistaParcialFutura(a.Clave),
            Destino = esExterna ? new ProduccionOperativaDestinoVm() : ResolverDestinoAgenda(a, item)
        };
    }
    private static ProduccionOperativaDestinoVm ResolverDestinoPaso(AgendaOperativaPasoVm p, AgendaOperativaItemVm item)
    {
        var destino = new ProduccionOperativaDestinoVm
        {
            Controlador = p.Controlador,
            Accion = p.Accion,
            IdDestino = p.IdDestino,
            MetodoHttp = "GET",
            UsarAjax = true
        };
        CompletarDestino(destino, p.Clave, item);
        return destino;
    }

    private static ProduccionOperativaDestinoVm ResolverDestinoAgenda(AgendaOperativaAccionVm a, AgendaOperativaItemVm item)
    {
        var destino = new ProduccionOperativaDestinoVm
        {
            Controlador = a.Controlador,
            Accion = a.Accion,
            ParametroId = a.ParametroId,
            IdDestino = a.IdDestino,
            MetodoHttp = "GET",
            UsarAjax = true,
            ParametrosRuta = new Dictionary<string, string?>(a.ParametrosRuta, StringComparer.OrdinalIgnoreCase)
        };
        CompletarDestino(destino, a.Clave, item);
        return destino;
    }

    private static void CompletarDestino(ProduccionOperativaDestinoVm destino, string clave, AgendaOperativaItemVm item)
    {
        if (string.Equals(clave, AgendaOperativaPasoClave.Material, StringComparison.OrdinalIgnoreCase))
        {
            destino.Controlador = "ProduccionPreparacion";
            destino.Accion = "Materiales";
            destino.ParametroId = null;
            destino.IdDestino = null;
            destino.ParametrosRuta["filtro"] = item.OFTexto;
            if (item.MaquinaID.HasValue) destino.ParametrosRuta["maquinaId"] = item.MaquinaID.Value.ToString(CultureInfo.InvariantCulture);
            return;
        }
        if (string.Equals(clave, AgendaOperativaPasoClave.Secado, StringComparison.OrdinalIgnoreCase))
        {
            destino.Controlador = "ProduccionPreparacion";
            destino.Accion = "Secado";
            destino.ParametroId = null;
            destino.IdDestino = null;
            destino.ParametrosRuta["filtro"] = item.OFTexto;
            if (item.MaquinaID.HasValue) destino.ParametrosRuta["maquinaId"] = item.MaquinaID.Value.ToString(CultureInfo.InvariantCulture);
            return;
        }
        if (string.Equals(clave, AgendaOperativaPasoClave.Embalaje, StringComparison.OrdinalIgnoreCase))
        {
            destino.Controlador = "ProduccionPreparacion";
            destino.Accion = "Embalajes";
            destino.ParametroId = null;
            destino.IdDestino = null;
            destino.ParametrosRuta["filtro"] = item.OFTexto;
            if (item.MaquinaID.HasValue) destino.ParametrosRuta["maquinaId"] = item.MaquinaID.Value.ToString(CultureInfo.InvariantCulture);
            return;
        }
        if (string.Equals(clave, AgendaOperativaPasoClave.CambioMolde, StringComparison.OrdinalIgnoreCase))
        {
            destino.Controlador = "ProduccionPreparacion";
            destino.Accion = "CambioMolde";
            destino.ParametroId = null;
            destino.IdDestino = null;
            destino.ParametrosRuta["filtro"] = item.OFTexto;
            if (item.MaquinaID.HasValue) destino.ParametrosRuta["maquinaId"] = item.MaquinaID.Value.ToString(CultureInfo.InvariantCulture);
            return;
        }
        if (string.Equals(destino.Controlador, "Produccion", StringComparison.OrdinalIgnoreCase) && string.Equals(destino.Accion, "Detalle", StringComparison.OrdinalIgnoreCase) && item.EjecucionProduccionID.HasValue)
        {
            destino.ParametroId = "id";
            destino.IdDestino = item.EjecucionProduccionID.Value;
            return;
        }
        if (string.Equals(destino.Controlador, "Calidad", StringComparison.OrdinalIgnoreCase) && string.Equals(destino.Accion, "Detalle", StringComparison.OrdinalIgnoreCase))
        {
            destino.ParametroId = "id";
            return;
        }
        if (string.Equals(clave, AgendaOperativaPasoClave.Personal, StringComparison.OrdinalIgnoreCase))
        {
            destino.Controlador = "ProduccionPersonal";
            destino.Accion = "Index";
            destino.ParametroId = null;
            destino.IdDestino = null;
        }
    }

    private static ProduccionOperativaDestinoVm DestinoDetalleProduccion(ProduccionCentroOperativoVm vm)
    {
        if (vm.EjecucionProduccionID.HasValue)
        {
            return new ProduccionOperativaDestinoVm
            {
                Controlador = "Produccion",
                Accion = "Detalle",
                ParametroId = "id",
                IdDestino = vm.EjecucionProduccionID.Value,
                MetodoHttp = "GET",
                UsarAjax = true
            };
        }
        return new ProduccionOperativaDestinoVm
        {
            Controlador = "Produccion",
            Accion = "Index",
            MetodoHttp = "GET",
            UsarAjax = true,
            ParametrosRuta = new Dictionary<string, string?>
            {
                ["busqueda"] = vm.OFTexto,
                ["maquinaId"] = vm.MaquinaID?.ToString(CultureInfo.InvariantCulture)
            }
        };
    }

    private static ProduccionOperativaLhRhVm MapearLhRh(AgendaOperativaLhRhVm? origen, int programaId, int? ejecucionId)
    {
        if (origen == null || !origen.EsPareja) return new ProduccionOperativaLhRhVm { EsPareja = false, ProgramaActualID = programaId, EjecucionActualID = ejecucionId };
        return new ProduccionOperativaLhRhVm
        {
            EsPareja = true,
            GrupoLhRh = origen.GrupoLhRh,
            LadoActual = origen.LadoActual,
            LadoPareja = origen.LadoPareja,
            ProgramaActualID = origen.ProgramaActualID,
            ProgramaParejaID = origen.ProgramaParejaID > 0 ? origen.ProgramaParejaID : null,
            EjecucionActualID = origen.EjecucionActualID,
            EjecucionParejaID = origen.EjecucionParejaID,
            SolicitudParejaID = origen.SolicitudParejaID,
            NumeroOFPareja = origen.OFPareja,
            NumeroPartePareja = origen.NumeroPartePareja,
            ReferenciaSAPPareja = origen.ReferenciaSAPPareja,
            EstadoPareja = origen.EstadoPareja,
            CantidadProgramadaPareja = origen.CantidadProgramadaPareja,
            CantidadProducidaPareja = origen.CantidadProducidaPareja,
            ParejaConsistente = origen.ParejaConsistente,
            MotivoInconsistencia = origen.MotivoInconsistencia
        };
    }

    private static ProduccionOperativaResumenProcesoVm ConstruirResumenProceso(ProduccionCentroOperativoVm vm)
    {
        var resumen = new ProduccionOperativaResumenProcesoVm
        {
            PersonalCompleto = EstadoBooleanoPaso(vm, AgendaOperativaPasoClave.Personal),
            MateriaPrimaCompleta = EstadoBooleanoPaso(vm, AgendaOperativaPasoClave.Material),
            EmbalajeCompleto = EstadoBooleanoPaso(vm, AgendaOperativaPasoClave.Embalaje),
            SecadoCompleto = EstadoBooleanoPaso(vm, AgendaOperativaPasoClave.Secado),
            CambioMoldeCompleto = EstadoBooleanoPaso(vm, AgendaOperativaPasoClave.CambioMolde),
            ChecklistArranqueCompleto = EstadoBooleanoPaso(vm, AgendaOperativaPasoClave.ChecklistArranque),
            CalidadInicialLiberada = EstadoBooleanoPaso(vm, AgendaOperativaPasoClave.Calidad),
            ConfiguracionTecnicaCompleta = EstadoBooleanoPaso(vm, AgendaOperativaPasoClave.ConfiguracionCorrida),
            SerieIniciada = EstadoBooleanoPaso(vm, AgendaOperativaPasoClave.InicioSerie),
            ProduccionActiva = vm.EstadoGeneral == AgendaOperativaEstadoGeneral.Produciendo,
            TieneParoAbierto = vm.TieneParoAbierto,
            MaquinaLiberada = vm.MaquinaLiberada,
            CalidadFinalCompleta = EstadoBooleanoPaso(vm, AgendaOperativaPasoClave.CalidadFinal),
            GP12Completo = null,
            CierreDocumentalCompleto = vm.EstatusProgramaID == ProgramaProduccionEstatus.Cerrado || vm.EstatusEjecucionID == ProduccionEstatus.Cerrado,
            EstadoMaterial = EstadoTextoPaso(vm, AgendaOperativaPasoClave.Material),
            EstadoEmbalaje = EstadoTextoPaso(vm, AgendaOperativaPasoClave.Embalaje),
            EstadoSecado = EstadoTextoPaso(vm, AgendaOperativaPasoClave.Secado),
            EstadoCambioMolde = EstadoTextoPaso(vm, AgendaOperativaPasoClave.CambioMolde),
            EstadoChecklistArranque = EstadoTextoPaso(vm, AgendaOperativaPasoClave.ChecklistArranque),
            EstadoCalidad = EstadoTextoPaso(vm, AgendaOperativaPasoClave.Calidad),
            EstadoConfiguracion = EstadoTextoPaso(vm, AgendaOperativaPasoClave.ConfiguracionCorrida),
            EstadoProduccion = EstadoTextoPaso(vm, AgendaOperativaPasoClave.Produccion),
            EstadoCierre = EstadoTextoPaso(vm, AgendaOperativaPasoClave.Cierre)
        };
        resumen.PorcentajePreparacion = PorcentajeEtapa(vm, ProduccionOperativaEtapa.Preparacion);
        resumen.PorcentajeLiberacion = PorcentajeEtapa(vm, ProduccionOperativaEtapa.Liberacion, ProduccionOperativaEtapa.Configuracion);
        resumen.PorcentajeProduccion = PorcentajeEtapa(vm, ProduccionOperativaEtapa.Produccion);
        resumen.PorcentajeCierre = PorcentajeEtapa(vm, ProduccionOperativaEtapa.Cierre, ProduccionOperativaEtapa.Finalizada);
        var aplicables = vm.Pasos.Where(x => x.Aplica).ToList();
        resumen.PorcentajeGeneral = aplicables.Count == 0 ? (vm.EstaFinalizada ? 100m : 0m) : Math.Round(aplicables.Count(x => x.Completado) * 100m / aplicables.Count, 1);
        return resumen;
    }

    private static List<ProduccionOperativaBloqueoVm> ConstruirBloqueos(ProduccionCentroOperativoVm vm)
    {
        var bloqueos = new List<ProduccionOperativaBloqueoVm>();

        // Un pendiente normal de una etapa futura NO es un bloqueo visible.
        // Solo mostramos estados realmente bloqueados de la etapa actual o de una etapa anterior.
        var ordenActual = vm.AccionActual?.Orden ?? int.MaxValue;
        var bloqueosDeFlujoActual = vm.Pasos
            .Where(x => x.Aplica && !x.Completado && x.Bloqueado && x.Orden <= ordenActual)
            .OrderBy(x => x.Orden)
            .ThenBy(x => x.Nombre)
            .Take(2)
            .Select(x => new ProduccionOperativaBloqueoVm
            {
                Clave = "PASO_" + x.Clave,
                Titulo = x.Nombre,
                Descripcion = x.MotivoBloqueo ?? x.Detalle,
                AccionClave = x.AccionClave ?? x.Clave,
                AreaResponsable = x.AreaResponsable,
                Severidad = ProduccionOperativaSeveridad.Peligro,
                Activo = true,
                BloqueaFlujo = true,
                FechaDeteccion = DateTime.Now
            });

        bloqueos.AddRange(bloqueosDeFlujoActual);

        if (vm.EsParejaLhRh && !vm.LhRh.ParejaConsistente)
        {
            bloqueos.Insert(0, new ProduccionOperativaBloqueoVm
            {
                Clave = "LHRH_INCONSISTENTE",
                Titulo = "Pareja LH/RH inconsistente",
                Descripcion = vm.LhRh.MotivoInconsistencia,
                AreaResponsable = AgendaOperativaArea.Planeacion,
                Severidad = ProduccionOperativaSeveridad.Peligro,
                Activo = true,
                BloqueaFlujo = true,
                FechaDeteccion = DateTime.Now
            });
        }

        return bloqueos
            .GroupBy(x => x.Clave, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .Take(2)
            .ToList();
    }

    private static List<ProduccionOperativaAlertaVm> ConstruirAlertas(ProduccionCentroOperativoVm vm)
    {
        var alertas = new List<ProduccionOperativaAlertaVm>();
        if (vm.EsUrgente)
        {
            alertas.Add(new ProduccionOperativaAlertaVm { Clave = "URGENTE", Titulo = "OF prioritaria", Mensaje = "Planeacion marco esta OF con atencion prioritaria.", Severidad = ProduccionOperativaSeveridad.Advertencia, Icono = "bi-exclamation-triangle", Fecha = DateTime.Now, RequiereAtencion = true });
        }
        if (vm.TieneInterrupcionUrgente)
        {
            alertas.Add(new ProduccionOperativaAlertaVm { Clave = "INTERRUPCION_URGENTE", Titulo = "Interrupcion urgente activa", Mensaje = vm.Agenda?.Interrupcion?.Motivo, Severidad = ProduccionOperativaSeveridad.Peligro, Icono = "bi-stop-circle", Fecha = vm.Agenda?.Interrupcion?.FechaInicio ?? DateTime.Now, RequiereAtencion = true });
        }
        else if (vm.TieneParoAbierto)
        {
            alertas.Add(new ProduccionOperativaAlertaVm { Clave = "PARO_ABIERTO", Titulo = "Paro abierto", Mensaje = vm.Agenda?.Interrupcion?.Motivo, Severidad = ProduccionOperativaSeveridad.Advertencia, Icono = "bi-pause-circle", Fecha = vm.Agenda?.Interrupcion?.FechaInicio ?? DateTime.Now, RequiereAtencion = true });
        }
        if (vm.EsParejaLhRh && !vm.LhRh.ParejaConsistente)
        {
            alertas.Add(new ProduccionOperativaAlertaVm { Clave = "LHRH_INCONSISTENTE", Titulo = "Revisar pareja LH/RH", Mensaje = vm.LhRh.MotivoInconsistencia, Severidad = ProduccionOperativaSeveridad.Peligro, Icono = "bi-link-45deg", Fecha = DateTime.Now, RequiereAtencion = true });
        }
        return alertas;
    }

    private static void AplicarOrdenFlujoOperativo(ProduccionCentroOperativoVm vm)
    {
        foreach (var paso in vm.Pasos)
            paso.Orden = OrdenFlujoOperativo(paso.Clave, paso.Orden);

        foreach (var accion in vm.AccionesDisponibles)
            accion.Orden = OrdenFlujoOperativo(accion.Clave, accion.Orden);

        vm.Pasos = vm.Pasos.OrderBy(x => x.Orden).ThenBy(x => x.Nombre).ToList();
        vm.AccionesDisponibles = vm.AccionesDisponibles.OrderBy(x => x.Orden).ThenBy(x => x.Titulo).ToList();
    }

    private static int OrdenFlujoOperativo(string? clave, int ordenActual)
    {
        var k = (clave ?? string.Empty).Trim().ToUpperInvariant();
        return k switch
        {
            AgendaOperativaPasoClave.Planeacion => 10,
            AgendaOperativaPasoClave.Personal => 20,
            AgendaOperativaPasoClave.Material => 30,
            AgendaOperativaPasoClave.Secado => 40,
            AgendaOperativaPasoClave.Embalaje => 50,
            AgendaOperativaPasoClave.CambioMolde => 60,
            ProduccionOperativaAccionClave.ChecklistCambioMolde => 61,
            "INICIAR_PREPARACION" => 70,
            AgendaOperativaPasoClave.ChecklistArranque => 80,
            AgendaOperativaPasoClave.ConfiguracionCorrida => 90,
            AgendaOperativaPasoClave.PrimerasPiezas => 100,
            AgendaOperativaPasoClave.Calidad => 110,
            AgendaOperativaPasoClave.InicioSerie => 120,
            AgendaOperativaPasoClave.Produccion => 130,
            AgendaOperativaPasoClave.Paro => 140,
            AgendaOperativaPasoClave.Capturas => 150,
            AgendaOperativaPasoClave.Cajas => 160,
            AgendaOperativaPasoClave.CalidadFinal => 170,
            AgendaOperativaPasoClave.LiberacionMaquina => 180,
            AgendaOperativaPasoClave.Cierre => 190,
            _ => ordenActual
        };
    }

    private static bool EsAccionComplementariaFueraSecuencia(string? clave)
    {
        var k = (clave ?? string.Empty).Trim().ToUpperInvariant();
        return k == ProduccionOperativaAccionClave.ChecklistCambioMolde
            || k == ProduccionOperativaAccionClave.CambioTurno
            || k == ProduccionOperativaAccionClave.RelevoOperador
            || k == ProduccionOperativaAccionClave.MonitoreoTurno
            || k == ProduccionOperativaAccionClave.TiempoExtra
            || k == ProduccionOperativaAccionClave.Historial
            || k == ProduccionOperativaAccionClave.DetalleCompleto;
    }

    private static void NormalizarAccionActualYSiguiente(ProduccionCentroOperativoVm vm)
    {
        bool EsParcialNoBloqueante(ProduccionOperativaAccionVm x)
        {
            var materialParcial = string.Equals(x.Clave, AgendaOperativaPasoClave.Material, StringComparison.OrdinalIgnoreCase) && x.EnProceso && !x.Completada && !x.Bloqueada && !x.BloqueaFlujo;
            var secadoParcial = string.Equals(x.Clave, AgendaOperativaPasoClave.Secado, StringComparison.OrdinalIgnoreCase) && x.EnProceso && !x.Completada && !x.Bloqueada && !x.BloqueaFlujo;
            return materialParcial || secadoParcial;
        }

        var accionesPrincipales = vm.AccionesDisponibles
            .Where(x => x.Aplica && x.Visible && !x.Completada && x.Orden < 900)
            .Where(x => !EsAccionComplementariaFueraSecuencia(x.Clave))
            .Where(x => vm.TieneParoAbierto || !string.Equals(x.Clave, AgendaOperativaPasoClave.Paro, StringComparison.OrdinalIgnoreCase))
            .Where(x => !EsParcialNoBloqueante(x))
            .OrderBy(x => x.Orden)
            .ThenBy(x => x.Titulo)
            .ToList();

        ProduccionOperativaAccionVm? actual = null;

        if (vm.EsParejaLhRh && !vm.LhRh.ParejaConsistente)
        {
            actual = vm.AccionesDisponibles.FirstOrDefault(x => string.Equals(x.Clave, "LHRH_INCONSISTENTE", StringComparison.OrdinalIgnoreCase));
        }

        if (actual == null && vm.TieneParoAbierto)
        {
            actual = accionesPrincipales.FirstOrDefault(x => string.Equals(x.Clave, AgendaOperativaPasoClave.Paro, StringComparison.OrdinalIgnoreCase));
        }

        if (actual == null && vm.EstatusEjecucionID == ProduccionEstatus.EnProduccion && !vm.MaquinaLiberada)
        {
            var liberacion = accionesPrincipales.FirstOrDefault(x => string.Equals(x.Clave, AgendaOperativaPasoClave.LiberacionMaquina, StringComparison.OrdinalIgnoreCase));
            if (liberacion != null && string.Equals(liberacion.Estado, AgendaOperativaEstadoPaso.Listo, StringComparison.OrdinalIgnoreCase)) actual = liberacion;
        }

        actual ??= accionesPrincipales.FirstOrDefault();
        vm.AccionActual = actual;

        if (actual == null)
        {
            vm.SiguienteAccion = null;
            return;
        }

        var actualFueraDeSecuencia =
            string.Equals(actual.Clave, AgendaOperativaPasoClave.LiberacionMaquina, StringComparison.OrdinalIgnoreCase) &&
            accionesPrincipales.Any(x => x.Orden < actual.Orden && !string.Equals(x.Clave, actual.Clave, StringComparison.OrdinalIgnoreCase));

        vm.SiguienteAccion = accionesPrincipales
            .Where(x => !string.Equals(x.Clave, actual.Clave, StringComparison.OrdinalIgnoreCase))
            .Where(x => actualFueraDeSecuencia || x.Orden > actual.Orden)
            .OrderBy(x => x.Orden)
            .ThenBy(x => x.Titulo)
            .FirstOrDefault();

        vm.EtapaActual = actual.Etapa;
    }
    private static void NormalizarBloqueoActual(ProduccionCentroOperativoVm vm)
    {
        if (vm.EsParejaLhRh && !vm.LhRh.ParejaConsistente)
        {
            vm.EstaBloqueada = true;
            vm.MotivoBloqueoGeneral = vm.LhRh.MotivoInconsistencia;
            return;
        }

        var actual = vm.AccionActual;
        var bloqueadaActual = actual != null &&
            (actual.Bloqueada || string.Equals(actual.Estado, AgendaOperativaEstadoPaso.Bloqueado, StringComparison.OrdinalIgnoreCase));

        vm.EstaBloqueada = bloqueadaActual;
        vm.MotivoBloqueoGeneral = bloqueadaActual
            ? (actual!.MotivoBloqueo ?? actual.Descripcion)
            : null;
    }

    private static string ResolverEstadoCentro(ProduccionCentroOperativoVm vm)
    {
        if (vm.EstaFinalizada) return ProduccionOperativaEstadoCentro.Finalizado;
        if (vm.EstaBloqueada || vm.TieneBloqueos) return ProduccionOperativaEstadoCentro.Bloqueado;
        if (vm.AccionActual == null) return ProduccionOperativaEstadoCentro.SinAccion;
        return ProduccionOperativaEstadoCentro.Listo;
    }

    private static string ResolverEtapaActual(ProduccionCentroOperativoVm vm)
    {
        if (vm.EstaFinalizada) return ProduccionOperativaEtapa.Finalizada;
        if (vm.AccionActual != null) return vm.AccionActual.Etapa;
        var pendiente = vm.Pasos.Where(x => x.Aplica && !x.Completado).OrderBy(x => x.Orden).FirstOrDefault();
        return pendiente?.Etapa ?? ProduccionOperativaEtapa.Produccion;
    }

    private static string ResolverEtapaPorClave(string? clave)
    {
        var k = (clave ?? string.Empty).Trim().ToUpperInvariant();
        return k switch
        {
            AgendaOperativaPasoClave.Planeacion or AgendaOperativaPasoClave.Personal or AgendaOperativaPasoClave.Material or AgendaOperativaPasoClave.Secado or AgendaOperativaPasoClave.Embalaje or AgendaOperativaPasoClave.PreparacionMolde or AgendaOperativaPasoClave.CambioMolde or "INICIAR_PREPARACION" or "CHECKLIST_CAMBIO_MOLDE" => ProduccionOperativaEtapa.Preparacion,
            AgendaOperativaPasoClave.ChecklistArranque or AgendaOperativaPasoClave.PrimerasPiezas or AgendaOperativaPasoClave.Calidad => ProduccionOperativaEtapa.Liberacion,
            AgendaOperativaPasoClave.ConfiguracionCorrida => ProduccionOperativaEtapa.Configuracion,
            AgendaOperativaPasoClave.InicioSerie or AgendaOperativaPasoClave.Produccion or AgendaOperativaPasoClave.Paro or AgendaOperativaPasoClave.Capturas or AgendaOperativaPasoClave.Cajas or "CAMBIO_TURNO" or "RELEVO_OPERADOR" or "MONITOREO_TURNO" or "TIEMPO_EXTRA" or "PRODUCTO_INCOMPLETO" or "AJUSTE_PRODUCCION" => ProduccionOperativaEtapa.Produccion,
            AgendaOperativaPasoClave.CalidadFinal or AgendaOperativaPasoClave.LiberacionMaquina or AgendaOperativaPasoClave.Cierre or "GP12" or "CIERRE_DOCUMENTAL" => ProduccionOperativaEtapa.Cierre,
            _ => ProduccionOperativaEtapa.Produccion
        };
    }

    private static string ResolverTipoContenido(string? clave)
    {
        var k = (clave ?? string.Empty).Trim().ToUpperInvariant();
        return k switch
        {
            AgendaOperativaPasoClave.Personal => ProduccionOperativaTipoContenido.Personal,
            "INICIAR_PREPARACION" => ProduccionOperativaTipoContenido.Preparacion,
            AgendaOperativaPasoClave.Material => ProduccionOperativaTipoContenido.Material,
            AgendaOperativaPasoClave.Embalaje => ProduccionOperativaTipoContenido.Embalaje,
            AgendaOperativaPasoClave.Secado => ProduccionOperativaTipoContenido.Secado,
            AgendaOperativaPasoClave.CambioMolde => ProduccionOperativaTipoContenido.CambioMolde,
            "CHECKLIST_CAMBIO_MOLDE" or AgendaOperativaPasoClave.ChecklistArranque => ProduccionOperativaTipoContenido.Checklist,
            AgendaOperativaPasoClave.ConfiguracionCorrida => ProduccionOperativaTipoContenido.ConfiguracionTecnica,
            AgendaOperativaPasoClave.PrimerasPiezas or AgendaOperativaPasoClave.Calidad or AgendaOperativaPasoClave.CalidadFinal => ProduccionOperativaTipoContenido.Calidad,
            AgendaOperativaPasoClave.InicioSerie => ProduccionOperativaTipoContenido.InicioSerie,
            AgendaOperativaPasoClave.Capturas or AgendaOperativaPasoClave.Produccion => ProduccionOperativaTipoContenido.CapturaHora,
            AgendaOperativaPasoClave.Paro => ProduccionOperativaTipoContenido.Paro,
            AgendaOperativaPasoClave.Cajas or "PRODUCTO_INCOMPLETO" => ProduccionOperativaTipoContenido.Cajas,
            "CAMBIO_TURNO" or "RELEVO_OPERADOR" or "MONITOREO_TURNO" => ProduccionOperativaTipoContenido.CambioTurno,
            "TIEMPO_EXTRA" => ProduccionOperativaTipoContenido.TiempoExtra,
            AgendaOperativaPasoClave.LiberacionMaquina or AgendaOperativaPasoClave.Cierre => ProduccionOperativaTipoContenido.Terminacion,
            "CIERRE_DOCUMENTAL" => ProduccionOperativaTipoContenido.Cierre,
            "HISTORIAL" => ProduccionOperativaTipoContenido.Historial,
            _ => ProduccionOperativaTipoContenido.Resumen
        };
    }

    private static string? ResolverVistaParcialFutura(string? clave)
    {
        var k = (clave ?? string.Empty).Trim().ToUpperInvariant();
        return k switch
        {
            AgendaOperativaPasoClave.Personal => "~/Views/ProduccionOperativa/Acciones/_Personal.cshtml",
            "INICIAR_PREPARACION" => "~/Views/ProduccionOperativa/Acciones/_IniciarPreparacion.cshtml",
            AgendaOperativaPasoClave.Material => "~/Views/ProduccionOperativa/Acciones/_Material.cshtml",
            AgendaOperativaPasoClave.Embalaje => "~/Views/ProduccionOperativa/Acciones/_Embalaje.cshtml",
            AgendaOperativaPasoClave.Secado => "~/Views/ProduccionOperativa/Acciones/_Secado.cshtml",
            AgendaOperativaPasoClave.CambioMolde => "~/Views/ProduccionOperativa/Acciones/_CambioMolde.cshtml",
            "CHECKLIST_CAMBIO_MOLDE" => "~/Views/ProduccionOperativa/Acciones/_ChecklistCambioMolde.cshtml",
            AgendaOperativaPasoClave.ChecklistArranque => "~/Views/ProduccionOperativa/Acciones/_ChecklistArranque.cshtml",
            AgendaOperativaPasoClave.ConfiguracionCorrida => "~/Views/ProduccionOperativa/Acciones/_ConfiguracionTecnica.cshtml",
            AgendaOperativaPasoClave.PrimerasPiezas or AgendaOperativaPasoClave.Calidad or AgendaOperativaPasoClave.CalidadFinal => "~/Views/ProduccionOperativa/Acciones/_Calidad.cshtml",
            AgendaOperativaPasoClave.InicioSerie => "~/Views/ProduccionOperativa/Acciones/_InicioSerie.cshtml",
            AgendaOperativaPasoClave.Produccion => "~/Views/ProduccionOperativa/Acciones/_Produccion.cshtml",
            AgendaOperativaPasoClave.Capturas => "~/Views/ProduccionOperativa/Acciones/_CapturaHora.cshtml",
            AgendaOperativaPasoClave.Paro => "~/Views/ProduccionOperativa/Acciones/_Paros.cshtml",
            AgendaOperativaPasoClave.Cajas or "PRODUCTO_INCOMPLETO" => "~/Views/ProduccionOperativa/Acciones/_Cajas.cshtml",
            "CAMBIO_TURNO" or "RELEVO_OPERADOR" or "MONITOREO_TURNO" => "~/Views/ProduccionOperativa/Acciones/_CambioTurno.cshtml",
            "TIEMPO_EXTRA" => "~/Views/ProduccionOperativa/Acciones/_TiempoExtra.cshtml",
            AgendaOperativaPasoClave.LiberacionMaquina => "~/Views/ProduccionOperativa/Acciones/_LiberacionMaquina.cshtml",
            AgendaOperativaPasoClave.Cierre => "~/Views/ProduccionOperativa/Acciones/_Cierre.cshtml",
            "HISTORIAL" => "~/Views/ProduccionOperativa/Acciones/_Historial.cshtml",
            "DETALLE_COMPLETO" => "~/Views/ProduccionOperativa/Acciones/_DetalleCompleto.cshtml",
            _ => null
        };
    }

    private static bool EsAccionExternaAlCentroOperativo(string? clave, string? area)
    {
        var k = (clave ?? string.Empty).Trim().ToUpperInvariant();
        var a = (area ?? string.Empty).Trim().ToUpperInvariant();
        if (a == AgendaOperativaArea.Calidad) return true;
        return k == AgendaOperativaPasoClave.PrimerasPiezas
            || k == AgendaOperativaPasoClave.Calidad
            || k == AgendaOperativaPasoClave.CalidadFinal
            || k == "RELIBERACION_CALIDAD"
            || k == "GP12";
    }

    private static string TextoEsperaAreaExterna(string? area)
    {
        var a = (area ?? string.Empty).Trim().ToUpperInvariant();
        if (a == AgendaOperativaArea.Calidad) return "Esperando a Calidad";
        return "Esperando área responsable";
    }

    private static bool PuedeEjecutarAccion(
        string? clave,
        string? area,
        ProduccionOperativaPermisosVm p,
        UsuarioOperativoDto usuario)
    {
        if (p.SoloLectura) return false;
        if (EsAccionExternaAlCentroOperativo(clave, area)) return false;
        if (usuario.EsAdministradorERP) return true;
        var k = (clave ?? string.Empty).Trim().ToUpperInvariant();
        if (k == AgendaOperativaPasoClave.Personal) return p.PuedeGestionarPersonal;
        if (k == AgendaOperativaPasoClave.Material) return p.PuedeRecibirMateriaPrima;
        if (k == AgendaOperativaPasoClave.Embalaje) return p.PuedeGestionarEmbalaje;
        if (k == AgendaOperativaPasoClave.Secado || k == "CAMBIO_TOLVA") return p.PuedeGestionarSecado;
        if (k == AgendaOperativaPasoClave.CambioMolde || k == AgendaOperativaPasoClave.PreparacionMolde) return p.PuedeGestionarCambioMolde;
        if (k == "CHECKLIST_CAMBIO_MOLDE") return p.PuedeGestionarChecklistCambioMolde;
        if (k == "INICIAR_PREPARACION") return p.PuedeSupervisar || usuario.EsAuxiliarProduccion || usuario.EsTecnicoProduccion;
        if (k == AgendaOperativaPasoClave.ChecklistArranque) return p.PuedeGestionarChecklistArranque;
        if (k == AgendaOperativaPasoClave.ConfiguracionCorrida) return p.PuedeGestionarConfiguracionTecnica;
        if (k == AgendaOperativaPasoClave.PrimerasPiezas || k == AgendaOperativaPasoClave.Calidad || k == AgendaOperativaPasoClave.CalidadFinal || k == "RELIBERACION_CALIDAD") return p.PuedeGestionarCalidad || p.PuedeGestionarReliberacion;
        if (k == AgendaOperativaPasoClave.InicioSerie) return p.PuedeIniciarSerie;
        if (k == AgendaOperativaPasoClave.Produccion || k == AgendaOperativaPasoClave.Capturas) return p.PuedeRegistrarCapturaHora;
        if (k == AgendaOperativaPasoClave.Paro || k == "CERRAR_PARO") return p.PuedeGestionarParos;
        if (k == "CAMBIO_TURNO" || k == "RELEVO_OPERADOR") return p.PuedeGestionarCambioTurno;
        if (k == "MONITOREO_TURNO") return p.PuedeGestionarMonitoreoTurno;
        if (k == AgendaOperativaPasoClave.Cajas) return p.PuedeGestionarCajas;
        if (k == "PRODUCTO_INCOMPLETO") return p.PuedeGestionarProductoIncompleto;
        if (k == "TIEMPO_EXTRA") return p.PuedeGestionarTiempoExtra;
        if (k == "AJUSTE_PRODUCCION") return p.PuedeAjustarProduccion;
        if (k == AgendaOperativaPasoClave.LiberacionMaquina) return p.PuedeLiberarMaquina;
        if (k == AgendaOperativaPasoClave.Cierre) return p.PuedeTerminarProduccion;
        if (k == "GP12") return p.PuedeGestionarGP12;
        if (k == "CIERRE_DOCUMENTAL") return p.PuedeGestionarCierreDocumental;
        if (k == ProduccionOperativaAccionClave.Historial) return p.PuedeVerHistorial;
        if (k == ProduccionOperativaAccionClave.DetalleCompleto) return p.PuedeVerDetalleCompleto;
        var a = (area ?? string.Empty).Trim().ToUpperInvariant();
        if (a == AgendaOperativaArea.Calidad) return p.PuedeGestionarCalidad;
        if (a == AgendaOperativaArea.Smed) return p.PuedeGestionarCambioMolde || p.PuedeGestionarChecklistCambioMolde;
        if (a == AgendaOperativaArea.TecnicoProduccion) return p.PuedeGestionarConfiguracionTecnica || p.PuedeGestionarChecklistArranque;
        if (a == AgendaOperativaArea.Operador) return usuario.EsOperadorProduccion || usuario.EsAuxiliarProduccion || p.PuedeSupervisar;
        if (a == AgendaOperativaArea.Produccion) return p.TienePermisosOperativos;
        if (a == AgendaOperativaArea.Mantenimiento) return usuario.EsMantenimiento;
        return false;
    }

    private static ProduccionOperativaPermisosVm ConstruirPermisos(UsuarioOperativoDto u, bool soloLecturaSolicitado)
    {
        var supervisor = u.EsAdministradorERP || u.EsEncargadoProduccion;
        var supervisorKiosco = supervisor || u.EsAuxiliarProduccion;
        var p = new ProduccionOperativaPermisosVm
        {
            PuedeVerCentroOperativo = u.Activo,
            PuedeVerDetalleCompleto = u.Activo,
            PuedeVerHistorial = u.Activo,
            PuedeGestionarPersonal = supervisor,
            PuedeRecibirMateriaPrima = supervisor || u.EsAuxiliarProduccion,
            PuedeGestionarEmbalaje = supervisor || u.EsAuxiliarProduccion,
            PuedeGestionarSecado = supervisor || u.EsTecnicoProduccion || u.EsSMED || u.EsAuxiliarProduccion,
            PuedeGestionarCambioMolde = supervisor || u.EsTecnicoProduccion || u.EsSMED,
            PuedeGestionarChecklistCambioMolde = supervisor || u.EsTecnicoProduccion || u.EsSMED,
            PuedeGestionarChecklistArranque = supervisor || u.EsAuxiliarProduccion || u.EsTecnicoProduccion || u.EsSMED,
            PuedeGestionarCalidad = u.EsAdministradorERP || u.EsCalidad,
            PuedeGestionarReliberacion = u.EsAdministradorERP || u.EsCalidad,
            PuedeGestionarConfiguracionTecnica = supervisor || u.EsAuxiliarProduccion || u.EsTecnicoProduccion,
            PuedeIniciarSerie = supervisorKiosco || u.EsTecnicoProduccion || u.EsOperadorProduccion,
            PuedeRegistrarCapturaHora = supervisorKiosco || u.EsOperadorProduccion,
            PuedeGestionarParos = supervisorKiosco || u.EsOperadorProduccion,
            PuedeGestionarCambioTurno = u.EsAdministradorERP || u.EsOperadorProduccion || u.EsAuxiliarProduccion,
            PuedeGestionarMonitoreoTurno = supervisorKiosco || u.EsTecnicoProduccion || u.EsSMED,
            PuedeGestionarCajas = u.EsAdministradorERP || u.EsAuxiliarProduccion || u.EsOperadorProduccion,
            PuedeGestionarProductoIncompleto = u.EsAdministradorERP || u.EsAuxiliarProduccion || u.EsOperadorProduccion,
            PuedeGestionarTiempoExtra = u.EsAdministradorERP || u.EsAuxiliarProduccion || u.EsOperadorProduccion,
            PuedeAjustarProduccion = supervisorKiosco,
            PuedeLiberarMaquina = supervisorKiosco || u.EsTecnicoProduccion || u.EsOperadorProduccion,
            PuedeTerminarProduccion = supervisorKiosco || u.EsTecnicoProduccion || u.EsOperadorProduccion,
            PuedeGestionarCalidadFinal = u.EsAdministradorERP || u.EsCalidad,
            PuedeGestionarGP12 = u.EsAdministradorERP || u.EsCalidad,
            PuedeGestionarCierreDocumental = supervisorKiosco,
            PuedeSupervisar = supervisorKiosco
        };
        p.SoloLectura = soloLecturaSolicitado || !p.TienePermisosOperativos;
        return p;
    }

    private static ProduccionOperativaUsuarioVm MapearUsuario(UsuarioOperativoDto u)
    {
        var vm = new ProduccionOperativaUsuarioVm
        {
            UsuarioID = u.UsuarioID,
            UsuarioNombre = u.UsuarioNombre,
            NombreCompleto = u.NombreCompleto,
            Departamento = u.Departamento,
            Puesto = u.Puesto,
            EsAdministradorERP = u.EsAdministradorERP,
            EsEncargadoProduccion = u.EsEncargadoProduccion,
            EsTecnicoProduccion = u.EsTecnicoProduccion,
            EsSMED = u.EsSMED,
            EsAuxiliarProduccion = u.EsAuxiliarProduccion,
            EsOperadorProduccion = u.EsOperadorProduccion,
            EsCalidad = u.EsCalidad,
            EsMantenimiento = u.EsMantenimiento
        };
        if (u.EsAdministradorERP) vm.Roles.Add("ADMINISTRADOR_ERP");
        if (u.EsEncargadoProduccion) vm.Roles.Add("ENCARGADO_PRODUCCION");
        if (u.EsTecnicoProduccion) vm.Roles.Add("TECNICO_PRODUCCION");
        if (u.EsSMED) vm.Roles.Add("SMED");
        if (u.EsAuxiliarProduccion) vm.Roles.Add("AUXILIAR_PRODUCCION");
        if (u.EsOperadorProduccion) vm.Roles.Add("OPERADOR_PRODUCCION");
        if (u.EsCalidad) vm.Roles.Add("CALIDAD");
        if (u.EsMantenimiento) vm.Roles.Add("MANTENIMIENTO");
        if (u.EsAdministradorERP || u.EsEncargadoProduccion || u.EsTecnicoProduccion || u.EsSMED || u.EsAuxiliarProduccion || u.EsOperadorProduccion) vm.Areas.Add(AgendaOperativaArea.Produccion);
        if (u.EsTecnicoProduccion) vm.Areas.Add(AgendaOperativaArea.TecnicoProduccion);
        if (u.EsSMED) vm.Areas.Add(AgendaOperativaArea.Smed);
        if (u.EsOperadorProduccion) vm.Areas.Add(AgendaOperativaArea.Operador);
        if (u.EsAuxiliarProduccion)
        {
            vm.Areas.Add(AgendaOperativaArea.Materiales);
            vm.Areas.Add(AgendaOperativaArea.Embalaje);
            vm.Areas.Add(AgendaOperativaArea.Secado);
        }
        if (u.EsCalidad) vm.Areas.Add(AgendaOperativaArea.Calidad);
        if (u.EsMantenimiento) vm.Areas.Add(AgendaOperativaArea.Mantenimiento);
        vm.Roles = vm.Roles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        vm.Areas = vm.Areas.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        vm.EsConsulta = vm.Roles.Count == 0;
        return vm;
    }

    private async Task<ProgramaCentroDto?> ObtenerProgramaBaseAsync(int programaProduccionId, SqlConnection cn, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TOP(1)
    pp.ProgramaProduccionID,
    pp.SolicitudProduccionID,
    pp.SolicitudProduccionDetalleID,
    pp.ReleaseDetalleID,
    s.FolioSolicitud,
    s.NumeroOFRecibida,
    s.ClienteID,
    COALESCE(NULLIF(LTRIM(RTRIM(c.Nombre)),N''),NULLIF(LTRIM(RTRIM(s.ClienteNombre)),N'')) AS ClienteNombre,
    pp.ParteID,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.DesignacionDescripcionSAP AS DescripcionParte,
    pp.MaquinaID,
    COALESCE(NULLIF(LTRIM(RTRIM(pp.MaquinaCodigo)),N''),m.Codigo) AS MaquinaCodigo,
    COALESCE(NULLIF(LTRIM(RTRIM(pp.MaquinaNombre)),N''),m.Nombre) AS MaquinaNombre,
    pp.MoldeID,
    pp.MoldeCodigo,
    CONVERT(INT,ISNULL(pp.CantidadProgramada,0)) AS CantidadProgramada,
    CONVERT(INT,ISNULL(e.CantidadOKTotal,ISNULL(pp.CantidadProducida,0))) AS CantidadProducida,
    pp.FechaInicioProgramada,
    pp.FechaFinProgramada,
    ISNULL(pp.EstatusID,1) AS EstatusProgramaID,
    e.EjecucionProduccionID,
    e.EstatusID AS EstatusEjecucionID,
    e.FechaInicioReal,
    e.FechaFinReal,
    e.FechaLiberacionMaquina
FROM dbo.Planeacion_ProgramaProduccion pp
LEFT JOIN dbo.SolicitudesProduccion s ON s.SolicitudProduccionID=pp.SolicitudProduccionID AND s.Activo=1
LEFT JOIN dbo.ERP_Clientes c ON c.ClienteID=s.ClienteID
LEFT JOIN dbo.ERP_Maquinas m ON m.MaquinaID=pp.MaquinaID
OUTER APPLY
(
    SELECT TOP(1)
        ex.EjecucionProduccionID,
        ex.EstatusID,
        ex.FechaInicioReal,
        ex.FechaFinReal,
        ex.FechaLiberacionMaquina,
        ex.CantidadOKTotal
    FROM dbo.Produccion_Ejecucion ex
    WHERE ex.ProgramaProduccionID=pp.ProgramaProduccionID
      AND ex.Activo=1
    ORDER BY ex.EjecucionProduccionID DESC
) e
WHERE pp.ProgramaProduccionID=@ProgramaProduccionID
  AND pp.Activo=1;";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await rd.ReadAsync(cancellationToken)) return null;
        return new ProgramaCentroDto
        {
            ProgramaProduccionID = Int(rd, "ProgramaProduccionID"),
            SolicitudProduccionID = NInt(rd, "SolicitudProduccionID"),
            SolicitudProduccionDetalleID = NInt(rd, "SolicitudProduccionDetalleID"),
            ReleaseDetalleID = NInt(rd, "ReleaseDetalleID"),
            FolioSolicitud = Txt(rd, "FolioSolicitud"),
            NumeroOFRecibida = Txt(rd, "NumeroOFRecibida"),
            ClienteID = NInt(rd, "ClienteID"),
            ClienteNombre = Txt(rd, "ClienteNombre"),
            ParteID = NInt(rd, "ParteID"),
            NumeroParte = Txt(rd, "NumeroParte"),
            ReferenciaSAP = Txt(rd, "ReferenciaSAP"),
            DescripcionParte = Txt(rd, "DescripcionParte"),
            MaquinaID = NInt(rd, "MaquinaID"),
            MaquinaCodigo = Txt(rd, "MaquinaCodigo"),
            MaquinaNombre = Txt(rd, "MaquinaNombre"),
            MoldeID = NInt(rd, "MoldeID"),
            MoldeCodigo = Txt(rd, "MoldeCodigo"),
            CantidadProgramada = Int(rd, "CantidadProgramada"),
            CantidadProducida = Int(rd, "CantidadProducida"),
            FechaInicioProgramada = NDate(rd, "FechaInicioProgramada"),
            FechaFinProgramada = NDate(rd, "FechaFinProgramada"),
            EstatusProgramaID = Int(rd, "EstatusProgramaID"),
            EjecucionProduccionID = NInt(rd, "EjecucionProduccionID"),
            EstatusEjecucionID = NInt(rd, "EstatusEjecucionID"),
            FechaInicioReal = NDate(rd, "FechaInicioReal"),
            FechaFinReal = NDate(rd, "FechaFinReal"),
            FechaLiberacionMaquina = NDate(rd, "FechaLiberacionMaquina")
        };
    }

    private async Task<UsuarioOperativoDto?> ObtenerUsuarioOperativoAsync(int usuarioId, SqlConnection cn, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TOP(1)
    u.UsuarioID,
    u.PersonaID,
    u.RolID,
    u.DepartamentoID,
    ISNULL(u.Activo,0) AS UsuarioActivo,
    LTRIM(RTRIM(ISNULL(u.Username,N''))) AS UsuarioNombre,
    LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))) AS NombreCompleto,
    LTRIM(RTRIM(ISNULL(d.NombreDepartamento,N''))) AS Departamento,
    LTRIM(RTRIM(ISNULL(p.Puesto,N''))) AS Puesto,
    ISNULL(p.EsColaboradorActivo,0) AS EsColaboradorActivo
FROM dbo.Usuarios u
LEFT JOIN dbo.Persona p ON p.PersonaID=u.PersonaID
LEFT JOIN dbo.Departamentos d ON d.DepartamentoID=u.DepartamentoID
WHERE u.UsuarioID=@UsuarioID
  AND u.Activo=1;";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await rd.ReadAsync(cancellationToken)) return null;
        var puesto = Txt(rd, "Puesto") ?? string.Empty;
        var departamento = Txt(rd, "Departamento") ?? string.Empty;
        var puestoN = NormalizarTexto(puesto);
        var deptoN = NormalizarTexto(departamento);
        var activo = Bool(rd, "UsuarioActivo") && (Bool(rd, "EsColaboradorActivo") || Int(rd, "RolID") == 1);
        var esProduccion = deptoN.Contains("PRODUC", StringComparison.Ordinal) || puestoN.Contains("PRODUC", StringComparison.Ordinal);
        var dto = new UsuarioOperativoDto
        {
            UsuarioID = Int(rd, "UsuarioID"),
            PersonaID = NInt(rd, "PersonaID"),
            RolID = NInt(rd, "RolID"),
            UsuarioNombre = Txt(rd, "UsuarioNombre"),
            NombreCompleto = Txt(rd, "NombreCompleto"),
            Departamento = departamento,
            Puesto = puesto,
            Activo = activo,
            EsAdministradorERP = Int(rd, "RolID") == 1,
            EsEncargadoProduccion = activo && esProduccion && puestoN.Contains("ENCARGAD", StringComparison.Ordinal),
            EsTecnicoProduccion = activo && esProduccion && puestoN.Contains("TECN", StringComparison.Ordinal),
            EsSMED = activo && puestoN.Contains("SMED", StringComparison.Ordinal),
            EsAuxiliarProduccion = activo && esProduccion && puestoN.Contains("AUXILIAR", StringComparison.Ordinal),
            EsOperadorProduccion = activo && ((puestoN == "OPERADOR") || (puestoN.Contains("OPERADOR", StringComparison.Ordinal) && esProduccion)),
            EsCalidad = activo && (deptoN.Contains("CALIDAD", StringComparison.Ordinal) || puestoN.Contains("CALIDAD", StringComparison.Ordinal)),
            EsMantenimiento = activo && (deptoN.Contains("MANTEN", StringComparison.Ordinal) || puestoN.Contains("MANTEN", StringComparison.Ordinal))
        };
        if (dto.EsAdministradorERP) dto.Activo = true;
        return dto;
    }

    private async Task<ProduccionOperativaMetricasVm> ObtenerMetricasAsync(
        int? ejecucionProduccionId,
        int? parteId,
        SqlConnection cn,
        CancellationToken cancellationToken)
    {
        var vm = new ProduccionOperativaMetricasVm();
        if (!ejecucionProduccionId.HasValue || ejecucionProduccionId.Value <= 0)
        {
            if (parteId.HasValue) await CargarReferenciaTecnicaSinEjecucionAsync(vm, parteId.Value, cn, cancellationToken);
            return vm;
        }
        const string sql = @"
SELECT TOP(1)
    ISNULL(e.CantidadPlaneada,0) AS CantidadPlaneada,
    ISNULL(e.CantidadOKTotal,0) AS CantidadOKTotal,
    ISNULL(e.CantidadSospechosaTotal,0) AS CantidadSospechosaTotal,
    ISNULL(e.CantidadScrapTotal,0) AS CantidadScrapTotal,
    TRY_CONVERT(INT,dt.Cavidades) AS CavidadesReferencia,
    TRY_CONVERT(DECIMAL(18,4),REPLACE(CONVERT(NVARCHAR(100),dt.Ciclo),N',',N'.')) AS CicloReferencia,
    TRY_CONVERT(INT,dt.ObjetivoHora) AS ObjetivoHoraReferencia,
    cfg.CavidadesUsadas AS CavidadesActuales,
    cfg.TiempoCicloSegundos AS CicloActual,
    TRY_CONVERT(INT,ROUND(cfg.ObjetivoHoraCalculado,0)) AS ObjetivoHoraActual,
    ult.ValorContador AS ContadorActual,
    capt.TotalCapturas,
    capt.FechaUltimaCaptura,
    paro.MinutosParoActual,
    cajas.CajasFormadas,
    cajas.CajasPendientesCalidad,
    cajas.CajasLiberadasCalidad,
    cajas.CajasZonaVerde,
    cajas.CajasPendientesAlmacen,
    cajas.CajasRecibidasAlmacen
FROM dbo.Produccion_Ejecucion e
OUTER APPLY
(
    SELECT TOP(1) dt0.Cavidades,dt0.Ciclo,dt0.ObjetivoHora
    FROM dbo.ERP_ParteDatosTecnicos dt0
    WHERE dt0.ParteID=e.ParteID AND dt0.Activo=1
    ORDER BY dt0.ParteDatoTecnicoID DESC
) dt
OUTER APPLY
(
    SELECT TOP(1) c.CavidadesUsadas,c.TiempoCicloSegundos,c.ObjetivoHoraCalculado
    FROM dbo.Produccion_ConfiguracionCorrida c
    WHERE c.EjecucionProduccionID=e.EjecucionProduccionID
      AND c.Activo=1
      AND c.FechaFinVigencia IS NULL
    ORDER BY c.ConfiguracionCorridaID DESC
) cfg
OUTER APPLY
(
    SELECT TOP(1) l.ValorContador
    FROM dbo.Produccion_ContadorMaquinaLecturas l
    WHERE l.EjecucionProduccionID=e.EjecucionProduccionID AND l.Activo=1
    ORDER BY l.FechaLectura DESC,l.LecturaContadorID DESC
) ult
OUTER APPLY
(
    SELECT COUNT(1) AS TotalCapturas,MAX(CAST(r.FechaProduccion AS DATETIME2)) AS FechaUltimaCaptura
    FROM dbo.Produccion_RegistroHora r
    WHERE r.EjecucionProduccionID=e.EjecucionProduccionID AND r.Activo=1
) capt
OUTER APPLY
(
    SELECT TOP(1) DATEDIFF(MINUTE,p.FechaInicioParo,GETDATE()) AS MinutosParoActual
    FROM dbo.Produccion_Paros p
    WHERE p.EjecucionProduccionID=e.EjecucionProduccionID AND p.Activo=1 AND p.FechaFinParo IS NULL
    ORDER BY p.FechaInicioParo DESC,p.ParoID DESC
) paro
OUTER APPLY
(
    SELECT
        SUM(CASE WHEN c.EstadoCajaID=1 THEN 1 ELSE 0 END) AS CajasFormadas,
        SUM(CASE WHEN c.EstadoCajaID=2 THEN 1 ELSE 0 END) AS CajasPendientesCalidad,
        SUM(CASE WHEN c.EstadoCajaID=3 THEN 1 ELSE 0 END) AS CajasLiberadasCalidad,
        SUM(CASE WHEN c.EstadoCajaID=5 THEN 1 ELSE 0 END) AS CajasZonaVerde,
        SUM(CASE WHEN c.EstadoCajaID=6 THEN 1 ELSE 0 END) AS CajasPendientesAlmacen,
        SUM(CASE WHEN c.EstadoCajaID=7 THEN 1 ELSE 0 END) AS CajasRecibidasAlmacen
    FROM dbo.Produccion_Cajas c
    WHERE c.EjecucionProduccionID=e.EjecucionProduccionID AND c.Activo=1
) cajas
WHERE e.EjecucionProduccionID=@EjecucionProduccionID AND e.Activo=1;";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId.Value;
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await rd.ReadAsync(cancellationToken))
        {
            vm.CantidadProgramada = Int(rd, "CantidadPlaneada");
            vm.CantidadProducida = Int(rd, "CantidadOKTotal");
            vm.CantidadOK = Int(rd, "CantidadOKTotal");
            vm.CantidadSospechosa = Int(rd, "CantidadSospechosaTotal");
            vm.CantidadScrap = Int(rd, "CantidadScrapTotal");
            vm.CavidadesReferencia = NInt(rd, "CavidadesReferencia");
            vm.CicloReferencia = NDec(rd, "CicloReferencia");
            vm.ObjetivoHoraReferencia = NInt(rd, "ObjetivoHoraReferencia");
            vm.CavidadesActuales = NInt(rd, "CavidadesActuales");
            vm.CicloActual = NDec(rd, "CicloActual");
            vm.ObjetivoHoraActual = NInt(rd, "ObjetivoHoraActual");
            vm.ContadorActual = NLong(rd, "ContadorActual");
            vm.CapturasCompletadas = Int(rd, "TotalCapturas");
            vm.FechaUltimaCaptura = NDate(rd, "FechaUltimaCaptura");
            vm.MinutosParoActual = Math.Max(0, Int(rd, "MinutosParoActual"));
            vm.CajasFormadas = Int(rd, "CajasFormadas");
            vm.CajasPendientesCalidad = Int(rd, "CajasPendientesCalidad");
            vm.CajasLiberadasCalidad = Int(rd, "CajasLiberadasCalidad");
            vm.CajasEnZonaVerde = Int(rd, "CajasZonaVerde");
            vm.CajasPendientesAlmacen = Int(rd, "CajasPendientesAlmacen");
            vm.CajasRecibidasAlmacen = Int(rd, "CajasRecibidasAlmacen");
        }
        return vm;
    }

    private static async Task CargarReferenciaTecnicaSinEjecucionAsync(
        ProduccionOperativaMetricasVm vm,
        int parteId,
        SqlConnection cn,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TOP(1)
    TRY_CONVERT(INT,Cavidades) AS CavidadesReferencia,
    TRY_CONVERT(DECIMAL(18,4),REPLACE(CONVERT(NVARCHAR(100),Ciclo),N',',N'.')) AS CicloReferencia,
    TRY_CONVERT(INT,ObjetivoHora) AS ObjetivoHoraReferencia
FROM dbo.ERP_ParteDatosTecnicos
WHERE ParteID=@ParteID AND Activo=1
ORDER BY ParteDatoTecnicoID DESC;";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId;
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await rd.ReadAsync(cancellationToken)) return;
        vm.CavidadesReferencia = NInt(rd, "CavidadesReferencia");
        vm.CicloReferencia = NDec(rd, "CicloReferencia");
        vm.ObjetivoHoraReferencia = NInt(rd, "ObjetivoHoraReferencia");
    }

    private static void CompletarMetricasDesdeCentro(ProduccionCentroOperativoVm vm)
    {
        if (vm.Metricas.CantidadProgramada <= 0) vm.Metricas.CantidadProgramada = vm.CantidadProgramada;
        if (vm.Metricas.CantidadProducida <= 0) vm.Metricas.CantidadProducida = vm.CantidadProducida;
        vm.CantidadProducida = Math.Max(vm.CantidadProducida, vm.Metricas.CantidadProducida);
    }

    private static async Task<int?> ObtenerPreparacionCambioMoldeIdAsync(
        int programaProduccionId,
        SqlConnection cn,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TOP(1) PreparacionAnticipadaID
FROM dbo.Produccion_PreparacionAnticipada
WHERE ProgramaProduccionID=@ProgramaProduccionID
  AND UPPER(LTRIM(RTRIM(ISNULL(TipoTarea,N''))))=N'CAMBIO_MOLDE'
  AND Activo=1
ORDER BY PreparacionAnticipadaID DESC;";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
        var valor = await cmd.ExecuteScalarAsync(cancellationToken);
        return valor == null || valor == DBNull.Value ? null : Convert.ToInt32(valor);
    }

    private static (bool Visible, bool SoloLectura, string TextoBoton) ResolverPoliticaReaperturaPaso(string? clave, bool completado, int? ejecucionProduccionId, bool soloLecturaGlobal, bool esExterna, string textoNormal)
    {
        var k = (clave ?? string.Empty).Trim().ToUpperInvariant();

        if (!completado)
            return (true, soloLecturaGlobal || esExterna, textoNormal);

        if (esExterna)
            return (true, true, "Ver resultado");

        if (k == AgendaOperativaPasoClave.Personal)
        {
            var puedeEditar = !soloLecturaGlobal && !ejecucionProduccionId.HasValue;
            return (true, !puedeEditar, puedeEditar ? "Editar personal" : "Ver personal");
        }

        return k switch
        {
            AgendaOperativaPasoClave.Material => (true, true, "Ver material"),
            AgendaOperativaPasoClave.Secado => (true, true, "Ver secado"),
            AgendaOperativaPasoClave.Embalaje => (true, true, "Ver embalaje"),
            AgendaOperativaPasoClave.CambioMolde => (true, true, "Ver cambio de molde"),
            ProduccionOperativaAccionClave.ChecklistCambioMolde => (true, true, "Ver checklist"),
            "INICIAR_PREPARACION" => (true, true, "Ver inicio"),
            AgendaOperativaPasoClave.ChecklistArranque => (true, true, "Ver checklist"),
            AgendaOperativaPasoClave.ConfiguracionCorrida => (true, true, "Ver configuración"),
            AgendaOperativaPasoClave.PrimerasPiezas => (true, true, "Ver primeras piezas"),
            AgendaOperativaPasoClave.Calidad => (true, true, "Ver Calidad"),
            AgendaOperativaPasoClave.InicioSerie => (true, true, "Ver inicio de serie"),
            AgendaOperativaPasoClave.Produccion => (true, true, "Ver Producción"),
            AgendaOperativaPasoClave.Capturas => (true, true, "Ver capturas"),
            AgendaOperativaPasoClave.Cajas => (true, true, "Ver cajas"),
            AgendaOperativaPasoClave.CalidadFinal => (true, true, "Ver Calidad"),
            AgendaOperativaPasoClave.LiberacionMaquina => (true, true, "Ver liberación"),
            AgendaOperativaPasoClave.Cierre => (true, true, "Ver cierre"),
            _ => (false, true, "Ver")
        };
    }
    private static async Task<(bool Existe, bool Completo, bool EnProceso, int? ChecklistID)> ObtenerEstadoChecklistCambioMoldeOperativoAsync(
        int preparacionAnticipadaId,
        SqlConnection cn,
        CancellationToken cancellationToken)
    {
        if (preparacionAnticipadaId <= 0) return (false, false, false, null);

        const string sqlTabla = @"
SELECT CASE
    WHEN OBJECT_ID(N'dbo.Produccion_ChecklistArranqueProgramas',N'U') IS NOT NULL THEN N'dbo.Produccion_ChecklistArranqueProgramas'
    WHEN OBJECT_ID(N'dbo.Produccion_ChecklistArranquePrograma',N'U') IS NOT NULL THEN N'dbo.Produccion_ChecklistArranquePrograma'
    WHEN OBJECT_ID(N'dbo.Produccion_ChecklistProgramas',N'U') IS NOT NULL THEN N'dbo.Produccion_ChecklistProgramas'
    ELSE NULL
END;";

        string? tablaVinculo;
        await using (var cmdTabla = new SqlCommand(sqlTabla, cn))
        {
            var valorTabla = await cmdTabla.ExecuteScalarAsync(cancellationToken);
            tablaVinculo = valorTabla == null || valorTabla == DBNull.Value ? null : valorTabla.ToString();
        }

        if (string.IsNullOrWhiteSpace(tablaVinculo))
            return (false, false, false, null);

        var sql = $@"
SELECT TOP(1)
    c.ChecklistArranqueID,
    c.EstatusID,
    UPPER(LTRIM(RTRIM(ISNULL(c.EstadoFlujo,N'')))) AS EstadoFlujo
FROM dbo.Produccion_ChecklistArranque c
INNER JOIN {tablaVinculo} cp
    ON cp.ChecklistArranqueID=c.ChecklistArranqueID
   AND cp.Activo=1
WHERE cp.PreparacionAnticipadaID=@PreparacionAnticipadaID
  AND c.Activo=1
  AND c.CodigoFormato=@CodigoFormato
  AND c.VersionFormato=@VersionFormato
  AND c.TipoChecklist=@TipoChecklist
ORDER BY c.ChecklistArranqueID DESC;";

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@PreparacionAnticipadaID", SqlDbType.Int).Value = preparacionAnticipadaId;
        cmd.Parameters.Add("@CodigoFormato", SqlDbType.NVarChar, 100).Value = ProduccionChecklistFormato.CambioMoldeCodigo;
        cmd.Parameters.Add("@VersionFormato", SqlDbType.NVarChar, 60).Value = ProduccionChecklistFormato.CambioMoldeVersion;
        cmd.Parameters.Add("@TipoChecklist", SqlDbType.NVarChar, 100).Value = ProduccionChecklistTipo.CambioMolde;

        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await rd.ReadAsync(cancellationToken)) return (false, false, false, null);

        int? checklistId = rd["ChecklistArranqueID"] == DBNull.Value ? (int?)null : Convert.ToInt32(rd["ChecklistArranqueID"]);
        var estatusId = rd["EstatusID"] == DBNull.Value ? 0 : Convert.ToInt32(rd["EstatusID"]);
        var estadoFlujo = rd["EstadoFlujo"] == DBNull.Value ? string.Empty : rd["EstadoFlujo"].ToString()?.Trim() ?? string.Empty;
        var completo = string.Equals(estadoFlujo, ProduccionChecklistEstadoFlujo.Completo, StringComparison.OrdinalIgnoreCase)
            || estatusId == ProduccionChecklistEstatus.CapturadoPorProduccion;
        var enProceso = !completo && string.Equals(estadoFlujo, ProduccionChecklistEstadoFlujo.EnProceso, StringComparison.OrdinalIgnoreCase);
        return (true, completo, enProceso, checklistId);
    }

    private static bool? EstadoBooleanoPaso(ProduccionCentroOperativoVm vm, string clave)
    {
        var p = vm.Pasos.FirstOrDefault(x => string.Equals(x.Clave, clave, StringComparison.OrdinalIgnoreCase));
        if (p == null || !p.Aplica) return null;
        return p.Completado;
    }

    private static string? EstadoTextoPaso(ProduccionCentroOperativoVm vm, string clave)
    {
        var p = vm.Pasos.FirstOrDefault(x => string.Equals(x.Clave, clave, StringComparison.OrdinalIgnoreCase));
        return p == null || !p.Aplica ? null : p.EstadoTexto;
    }

    private static decimal PorcentajeEtapa(ProduccionCentroOperativoVm vm, params string[] etapas)
    {
        var set = etapas.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pasos = vm.Pasos.Where(x => x.Aplica && set.Contains(x.Etapa)).ToList();
        if (pasos.Count == 0) return 0m;
        return Math.Round(pasos.Count(x => x.Completado) * 100m / pasos.Count, 1);
    }

    private static ProduccionOperativaAccionVm? BuscarAccion(ProduccionCentroOperativoVm vm, string accionClave)
    {
        var clave = accionClave.Trim();
        if (vm.AccionActual != null && string.Equals(vm.AccionActual.Clave, clave, StringComparison.OrdinalIgnoreCase)) return vm.AccionActual;
        if (vm.SiguienteAccion != null && string.Equals(vm.SiguienteAccion.Clave, clave, StringComparison.OrdinalIgnoreCase)) return vm.SiguienteAccion;
        return vm.AccionesDisponibles.FirstOrDefault(x => string.Equals(x.Clave, clave, StringComparison.OrdinalIgnoreCase));
    }

    private static void ReemplazarPasoMismaClave(List<ProduccionOperativaPasoVm> lista, ProduccionOperativaPasoVm paso)
    {
        var idx = lista.FindIndex(x => string.Equals(x.Clave, paso.Clave, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) lista[idx] = paso;
        else lista.Add(paso);
    }

    private static void ReemplazarAccionMismaClave(List<ProduccionOperativaAccionVm> lista, ProduccionOperativaAccionVm accion)
    {
        var idx = lista.FindIndex(x => string.Equals(x.Clave, accion.Clave, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) lista[idx] = accion;
        else lista.Add(accion);
    }

    private static void AgregarSiNoExiste(List<ProduccionOperativaAccionVm> lista, ProduccionOperativaAccionVm accion)
    {
        if (lista.Any(x => string.Equals(x.Clave, accion.Clave, StringComparison.OrdinalIgnoreCase))) return;
        lista.Add(accion);
    }

    private bool ExisteVista(string ruta)
    {
        if (string.IsNullOrWhiteSpace(ruta)) return false;
        var directa = _viewEngine.GetView(null, ruta, false);
        if (directa.Success) return true;
        var porNombre = _viewEngine.FindView(ControllerContext, ruta, false);
        return porNombre.Success;
    }

    private static string TituloAccion(string clave, string nombre)
    {
        return clave switch
        {
            AgendaOperativaPasoClave.Personal => "Asignar personal a la OF",
            AgendaOperativaPasoClave.Material => "Recibir / revisar materia prima",
            AgendaOperativaPasoClave.Secado => "Atender secado de material",
            AgendaOperativaPasoClave.Embalaje => "Preparar embalaje",
            AgendaOperativaPasoClave.CambioMolde => "Atender cambio de molde",
            "INICIAR_PREPARACION" => "Iniciar preparacion de Produccion",
            AgendaOperativaPasoClave.ChecklistArranque => "Completar checklist de arranque",
            AgendaOperativaPasoClave.ConfiguracionCorrida => "Confirmar configuracion tecnica",
            AgendaOperativaPasoClave.PrimerasPiezas => "Validar primeras piezas",
            AgendaOperativaPasoClave.InicioSerie => "Iniciar o reiniciar serie",
            AgendaOperativaPasoClave.Paro => "Atender paro / interrupcion",
            AgendaOperativaPasoClave.Capturas => "Registrar produccion",
            AgendaOperativaPasoClave.Cajas => "Atender cajas de Produccion",
            AgendaOperativaPasoClave.CalidadFinal => "Resolver pendientes de Calidad",
            AgendaOperativaPasoClave.LiberacionMaquina => "Liberar maquina",
            AgendaOperativaPasoClave.Cierre => "Cerrar Produccion",
            _ => nombre
        };
    }

    private static string TextoBotonAccion(string clave)
    {
        return clave switch
        {
            AgendaOperativaPasoClave.Material => "Atender material",
            AgendaOperativaPasoClave.Secado => "Atender secado",
            AgendaOperativaPasoClave.Embalaje => "Atender embalaje",
            AgendaOperativaPasoClave.CambioMolde => "Atender molde",
            "INICIAR_PREPARACION" => "Iniciar preparación",
            AgendaOperativaPasoClave.ChecklistArranque => "Abrir checklist",
            AgendaOperativaPasoClave.ConfiguracionCorrida => "Configurar corrida",
            AgendaOperativaPasoClave.PrimerasPiezas or AgendaOperativaPasoClave.Calidad or AgendaOperativaPasoClave.CalidadFinal => "Atender Calidad",
            AgendaOperativaPasoClave.InicioSerie => "Iniciar serie",
            AgendaOperativaPasoClave.Capturas => "Capturar hora",
            AgendaOperativaPasoClave.Cajas => "Atender cajas",
            AgendaOperativaPasoClave.LiberacionMaquina => "Liberar maquina",
            AgendaOperativaPasoClave.Cierre => "Revisar cierre",
            _ => "Atender"
        };
    }

    private static string IconoAccion(string clave)
    {
        return clave switch
        {
            AgendaOperativaPasoClave.Personal => "bi-people",
            AgendaOperativaPasoClave.Material => "bi-box-seam",
            AgendaOperativaPasoClave.Secado => "bi-thermometer-half",
            AgendaOperativaPasoClave.Embalaje => "bi-box2",
            AgendaOperativaPasoClave.CambioMolde => "bi-tools",
            "INICIAR_PREPARACION" => "bi-play-circle-fill",
            AgendaOperativaPasoClave.ChecklistArranque => "bi-ui-checks",
            AgendaOperativaPasoClave.ConfiguracionCorrida => "bi-sliders",
            AgendaOperativaPasoClave.PrimerasPiezas => "bi-patch-check",
            AgendaOperativaPasoClave.Calidad => "bi-shield-check",
            AgendaOperativaPasoClave.InicioSerie => "bi-play-circle",
            AgendaOperativaPasoClave.Paro => "bi-pause-circle",
            AgendaOperativaPasoClave.Capturas => "bi-speedometer2",
            AgendaOperativaPasoClave.Cajas => "bi-boxes",
            AgendaOperativaPasoClave.CalidadFinal => "bi-shield-check",
            AgendaOperativaPasoClave.LiberacionMaquina => "bi-unlock",
            AgendaOperativaPasoClave.Cierre => "bi-check2-circle",
            _ => "bi-arrow-right-circle"
        };
    }

    private static string ResolverEstadoGeneralBase(ProgramaCentroDto p)
    {
        if (p.EstatusEjecucionID.HasValue)
        {
            if (p.FechaLiberacionMaquina.HasValue) return AgendaOperativaEstadoGeneral.MaquinaLiberada;
            return p.EstatusEjecucionID.Value switch
            {
                ProduccionEstatus.EnPreparacion => AgendaOperativaEstadoGeneral.Preparacion,
                ProduccionEstatus.EnProduccion => AgendaOperativaEstadoGeneral.Produciendo,
                ProduccionEstatus.Pausado => AgendaOperativaEstadoGeneral.Pausada,
                ProduccionEstatus.TerminadoParcial or ProduccionEstatus.Terminado or ProduccionEstatus.Cerrado => AgendaOperativaEstadoGeneral.Terminada,
                _ => AgendaOperativaEstadoGeneral.Programada
            };
        }
        return p.EstatusProgramaID switch
        {
            ProgramaProduccionEstatus.EnPreparacion => AgendaOperativaEstadoGeneral.Preparacion,
            ProgramaProduccionEstatus.EnProduccion => AgendaOperativaEstadoGeneral.Produciendo,
            ProgramaProduccionEstatus.Pausado => AgendaOperativaEstadoGeneral.Pausada,
            ProgramaProduccionEstatus.Terminado or ProgramaProduccionEstatus.Cerrado => AgendaOperativaEstadoGeneral.Terminada,
            _ => AgendaOperativaEstadoGeneral.Programada
        };
    }

    private static string NormalizarOrigen(string? origen)
    {
        var v = (origen ?? string.Empty).Trim().ToUpperInvariant();
        return v switch
        {
            ProduccionOperativaOrigen.Agenda => ProduccionOperativaOrigen.Agenda,
            ProduccionOperativaOrigen.Detalle => ProduccionOperativaOrigen.Detalle,
            ProduccionOperativaOrigen.Tablet => ProduccionOperativaOrigen.Tablet,
            ProduccionOperativaOrigen.Notificacion => ProduccionOperativaOrigen.Notificacion,
            ProduccionOperativaOrigen.Otro => ProduccionOperativaOrigen.Otro,
            _ => ProduccionOperativaOrigen.Calendario
        };
    }

    private static string NormalizarTexto(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = value.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string? PrimeroNoVacio(params string?[] valores)
    {
        foreach (var valor in valores)
        {
            if (!string.IsNullOrWhiteSpace(valor)) return valor.Trim();
        }
        return null;
    }

    private bool UsuarioEnSesion() => HttpContext.Session.GetInt32("UsuarioID").HasValue;
    private int ObtenerUsuarioID() => HttpContext.Session.GetInt32("UsuarioID") ?? 0;

    private IActionResult RespuestaSesionExpirada() => Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesion termino. Vuelve a iniciar sesion." });

    private static int Int(SqlDataReader rd, string c)
    {
        var i = rd.GetOrdinal(c);
        return rd.IsDBNull(i) ? 0 : Convert.ToInt32(rd.GetValue(i));
    }

    private static int? NInt(SqlDataReader rd, string c)
    {
        var i = rd.GetOrdinal(c);
        return rd.IsDBNull(i) ? null : Convert.ToInt32(rd.GetValue(i));
    }

    private static long? NLong(SqlDataReader rd, string c)
    {
        var i = rd.GetOrdinal(c);
        return rd.IsDBNull(i) ? null : Convert.ToInt64(rd.GetValue(i));
    }

    private static decimal? NDec(SqlDataReader rd, string c)
    {
        var i = rd.GetOrdinal(c);
        return rd.IsDBNull(i) ? null : Convert.ToDecimal(rd.GetValue(i));
    }

    private static DateTime? NDate(SqlDataReader rd, string c)
    {
        var i = rd.GetOrdinal(c);
        return rd.IsDBNull(i) ? null : Convert.ToDateTime(rd.GetValue(i));
    }

    private static string? Txt(SqlDataReader rd, string c)
    {
        var i = rd.GetOrdinal(c);
        return rd.IsDBNull(i) ? null : rd.GetValue(i)?.ToString()?.Trim();
    }

    private static bool Bool(SqlDataReader rd, string c)
    {
        var i = rd.GetOrdinal(c);
        return !rd.IsDBNull(i) && Convert.ToBoolean(rd.GetValue(i));
    }

    private sealed class ProgramaCentroDto
    {
        public int ProgramaProduccionID { get; set; }
        public int? SolicitudProduccionID { get; set; }
        public int? SolicitudProduccionDetalleID { get; set; }
        public int? ReleaseDetalleID { get; set; }
        public string? FolioSolicitud { get; set; }
        public string? NumeroOFRecibida { get; set; }
        public int? ClienteID { get; set; }
        public string? ClienteNombre { get; set; }
        public int? ParteID { get; set; }
        public string? NumeroParte { get; set; }
        public string? ReferenciaSAP { get; set; }
        public string? DescripcionParte { get; set; }
        public int? MaquinaID { get; set; }
        public string? MaquinaCodigo { get; set; }
        public string? MaquinaNombre { get; set; }
        public int? MoldeID { get; set; }
        public string? MoldeCodigo { get; set; }
        public int CantidadProgramada { get; set; }
        public int CantidadProducida { get; set; }
        public DateTime? FechaInicioProgramada { get; set; }
        public DateTime? FechaFinProgramada { get; set; }
        public int EstatusProgramaID { get; set; }
        public int? EjecucionProduccionID { get; set; }
        public int? EstatusEjecucionID { get; set; }
        public DateTime? FechaInicioReal { get; set; }
        public DateTime? FechaFinReal { get; set; }
        public DateTime? FechaLiberacionMaquina { get; set; }
    }

    private sealed class UsuarioOperativoDto
    {
        public int UsuarioID { get; set; }
        public int? PersonaID { get; set; }
        public int? RolID { get; set; }
        public string? UsuarioNombre { get; set; }
        public string? NombreCompleto { get; set; }
        public string? Departamento { get; set; }
        public string? Puesto { get; set; }
        public bool Activo { get; set; }
        public bool EsAdministradorERP { get; set; }
        public bool EsEncargadoProduccion { get; set; }
        public bool EsTecnicoProduccion { get; set; }
        public bool EsSMED { get; set; }
        public bool EsAuxiliarProduccion { get; set; }
        public bool EsOperadorProduccion { get; set; }
        public bool EsCalidad { get; set; }
        public bool EsMantenimiento { get; set; }
    }
}
