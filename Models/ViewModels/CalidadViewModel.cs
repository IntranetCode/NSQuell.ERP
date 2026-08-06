using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace ERP.NSQuell.Models.ViewModels
{
    public class CalidadIndexViewModel
    {
        public string? Busqueda { get; set; }
        public string? EstadoFiltro { get; set; }
        public int TotalMostrados { get; set; }

        public int TotalPendientePrearranque { get; set; }
        public int TotalDevueltoPrearranque { get; set; }
        public int TotalArranqueAutorizado { get; set; }
        public int TotalPendientePrimerasPiezas { get; set; }
        public int TotalAjustesSolicitados { get; set; }
        public int TotalProduccionLiberada { get; set; }
        public int TotalMonitoreoActivo { get; set; }
        public int TotalPendienteLiberacionCaja { get; set; }
        public int TotalPendienteReliberacion { get; set; }
        public int TotalPendienteGP12 { get; set; }
        public int TotalEnGP12 { get; set; }
        public int TotalMaterialNoConforme { get; set; }
        public int TotalCerradas { get; set; }

        public int TotalCajasPendientes { get; set; }
        public List<CalidadCajaProduccionItemViewModel> CajasPendientes { get; set; } = new();

        // Compatibilidad con el Index anterior.
        public int TotalAbiertas { get; set; }
        public int TotalLiberadas { get; set; }
        public int TotalGPI2 { get; set; }
        public int TotalContencion { get; set; }
        public int TotalScrap { get; set; }

        public List<CalidadListadoItemViewModel> Inspecciones { get; set; } = new();
    }

    public class CalidadListadoItemViewModel
    {
        public int InspeccionID { get; set; }

        public int? ProgramaProduccionID { get; set; }
        public int? EjecucionProduccionID { get; set; }
        public int? ChecklistArranqueID { get; set; }
        public int? SolicitudProduccionID { get; set; }
        public int? SolicitudProduccionDetalleID { get; set; }
        public int? ReleaseID { get; set; }
        public int? ReleaseDetalleID { get; set; }

        public string? CodigoBarras { get; set; }
        public string? OrdenTrabajo { get; set; }
        public string? ClienteNombre { get; set; }
        public string? NumeroParte { get; set; }
        public string? Material { get; set; }
        public string? Proceso { get; set; }
        public string? Maquina { get; set; }
        public string? Molde { get; set; }
        public string? OperadorPrincipalNombre { get; set; }
        public string? OperadorAuxiliarNombre { get; set; }
        public string? TecnicoInyeccionNombre { get; set; }

        public decimal CantidadTotal { get; set; }
        public decimal CantidadRevisada { get; set; }
        public decimal CantidadPendiente { get; set; }

        public DateTime? FechaInicioProgramada { get; set; }
        public DateTime? FechaFinProgramada { get; set; }
        public DateTime? FechaNotificacionCalidad { get; set; }
        public DateTime? FechaLiberacionProduccion { get; set; }

        public string? ResultadoCalidad { get; set; }
        public string? Etiqueta { get; set; }
        public string Estado { get; set; } = CalidadEstados.PendientePrearranque;

        public bool RequiereReliberacion { get; set; }
        public bool ConfiguracionInvalidada { get; set; }
        public string? MotivoInvalidacion { get; set; }
        public DateTime FechaCreacion { get; set; }
    }

    public class CalidadFormViewModel
    {
        [StringLength(150)]
        public string? CodigoBarras { get; set; }

        [StringLength(120)]
        public string? OrdenTrabajo { get; set; }

        [StringLength(120)]
        public string? NumeroParte { get; set; }

        [StringLength(250)]
        public string? Material { get; set; }

        [StringLength(200)]
        public string? Proceso { get; set; }

        [StringLength(150)]
        public string? Maquina { get; set; }

        [Range(0, 999999999)]
        public decimal CantidadTotal { get; set; }

        [Range(0, 999999999)]
        public decimal CantidadRevisada { get; set; }

        public bool ChecklistValidado { get; set; }
        public bool HojaInspeccionProducto { get; set; }
        public bool HojaValidacionCalidad { get; set; }

        [StringLength(30)]
        public string? ResultadoCalidad { get; set; }

        [StringLength(1000)]
        public string? Observaciones { get; set; }
    }

    public class CalidadDetalleViewModel
    {
        public int InspeccionID { get; set; }

        public int? ProgramaProduccionID { get; set; }
        public int? EjecucionProduccionID { get; set; }
        public int? ChecklistArranqueID { get; set; }
        public int? SolicitudProduccionID { get; set; }
        public int? SolicitudProduccionDetalleID { get; set; }
        public int? ReleaseID { get; set; }
        public int? ReleaseDetalleID { get; set; }

        public int? ClienteID { get; set; }
        public int? ParteID { get; set; }
        public int? MaquinaID { get; set; }
        public int? MoldeID { get; set; }
        public int? MaterialID { get; set; }

        public string? CodigoBarras { get; set; }
        public string? OrdenTrabajo { get; set; }
        public string? ClienteNombre { get; set; }
        public string? NumeroParte { get; set; }
        public string? Material { get; set; }
        public string? Proceso { get; set; }
        public string? Maquina { get; set; }
        public string? Molde { get; set; }

        public int? OperadorPrincipalPersonaID { get; set; }
        public string? OperadorPrincipalNombre { get; set; }
        public int? OperadorAuxiliarPersonaID { get; set; }
        public string? OperadorAuxiliarNombre { get; set; }
        public int? TecnicoInyeccionPersonaID { get; set; }
        public string? TecnicoInyeccionNombre { get; set; }

        public DateTime? FechaInicioProgramada { get; set; }
        public DateTime? FechaFinProgramada { get; set; }

        public decimal CantidadTotal { get; set; }
        public decimal CantidadRevisada { get; set; }
        public decimal CantidadPendiente { get; set; }

        public bool ChecklistValidado { get; set; }
        public bool HojaInspeccionProducto { get; set; }
        public bool HojaValidacionCalidad { get; set; }
        public bool AyudaVisualColocada { get; set; }
        public bool? AlertaCalidadAplica { get; set; }
        public bool? AlertaCalidadColocada { get; set; }
        public bool HIPColocada { get; set; }
        public bool HCCColocada { get; set; }
        public bool MatrizPolivalenciaValidada { get; set; }

        public DateTime? FechaNotificacionCalidad { get; set; }
        public int? UsuarioNotificoID { get; set; }

        public DateTime? FechaInicioValidacionPrearranque { get; set; }
        public DateTime? FechaFinValidacionPrearranque { get; set; }
        public int? MinutosLiberacionInicial { get; set; }
        public bool? CumplioTiempoObjetivoInicial { get; set; }
        public DateTime? FechaAutorizacionPrearranque { get; set; }
        public int? UsuarioAutorizacionPrearranqueID { get; set; }
        public string? MotivoDevolucion { get; set; }

        public bool CincoDisparosSegregados { get; set; }
        public int CantidadDisparosConformes { get; set; }
        public bool? ValidacionDimensional { get; set; }
        public bool? ValidacionApariencia { get; set; }
        public bool? ValidacionGauge { get; set; }
        public bool? ValidacionConductividad { get; set; }
        public DateTime? FechaValidacionPrimerasPiezas { get; set; }
        public int? UsuarioValidacionPrimerasPiezasID { get; set; }

        public string? ResultadoCalidad { get; set; }
        public string? Etiqueta { get; set; }
        public bool Liberado { get; set; }
        public bool RequiereGP12 { get; set; }

        public bool RequiereGPI2
        {
            get => RequiereGP12;
            set => RequiereGP12 = value;
        }

        public bool EnContencion { get; set; }
        public bool EsScrap { get; set; }
        public DateTime? FechaLiberacionProduccion { get; set; }
        public int? UsuarioLiberacionProduccionID { get; set; }
        public bool RequiereReliberacion { get; set; }

        public bool ConfiguracionInvalidada { get; set; }
        public DateTime? FechaInvalidacion { get; set; }
        public int? UsuarioInvalidacionID { get; set; }
        public string? MotivoInvalidacion { get; set; }

        public string? Observaciones { get; set; }
        public string Estado { get; set; } = CalidadEstados.PendientePrearranque;
        public int? UsuarioCreacionID { get; set; }
        public DateTime FechaCreacion { get; set; }
        public int? UsuarioModificacionID { get; set; }
        public DateTime? FechaModificacion { get; set; }

        public List<CalidadHistorialItemViewModel> Historial { get; set; } = new();
        public List<CalidadPrimeraPiezaIntentoItemViewModel> IntentosPrimerasPiezas { get; set; } = new();
        public List<CalidadMonitoreoItemViewModel> Monitoreos { get; set; } = new();
        public List<CalidadDisposicionItemViewModel> Disposiciones { get; set; } = new();
        public List<CalidadCajaItemViewModel> Cajas { get; set; } = new();
        public List<CalidadCajaProduccionItemViewModel> CajasProduccion { get; set; } = new();
        public List<CalidadGP12ItemViewModel> RegistrosGP12 { get; set; } = new();
        public List<CalidadReliberacionItemViewModel> Reliberaciones { get; set; } = new();
        public List<CalidadChecklistPreguntaViewModel> PreguntasChecklistCalidad { get; set; } = new();
        public List<CalidadCatalogoDefectoItemViewModel> CatalogoDefectos { get; set; } = new();

        public int TotalMonitoreos => Monitoreos.Count;
        public int MonitoreosPendientes => Monitoreos.Count(x => x.EsPendiente);
        public int MonitoreosVencidos => Monitoreos.Count(x => x.EstaVencido);
        public int MonitoreosConformes => Monitoreos.Count(x => x.Resultado == CalidadResultadoMonitoreo.Conforme);
        public int MonitoreosConHallazgo => Monitoreos.Count(x =>
            x.Resultado == CalidadResultadoMonitoreo.Sospechoso ||
            x.Resultado == CalidadResultadoMonitoreo.NoConforme);
        public int MonitoreosReinspeccionados => Monitoreos.Count(x => x.Resultado == CalidadResultadoMonitoreo.Reinspeccion);
        public int DisposicionesPendientes => Disposiciones.Count(x => x.ResultadoFinal == CalidadResultadoDisposicion.Pendiente);
        public DateTime? ProximoMonitoreo => Monitoreos
            .Where(x => x.EsPendiente)
            .OrderBy(x => x.FechaHoraProgramada)
            .Select(x => (DateTime?)x.FechaHoraProgramada)
            .FirstOrDefault();
    }

    public class CalidadCorridaOrigenViewModel
    {
        public int ProgramaProduccionID { get; set; }
        public int? EjecucionProduccionID { get; set; }
        public int? ChecklistArranqueID { get; set; }
        public int? ReleaseID { get; set; }
        public int? ReleaseDetalleID { get; set; }
        public int? SolicitudProduccionID { get; set; }
        public int? SolicitudProduccionDetalleID { get; set; }
        public string? NumeroOF { get; set; }
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
        public int? MaterialID { get; set; }
        public string? MaterialCodigo { get; set; }
        public string? MaterialDescripcion { get; set; }
        public int CantidadProgramada { get; set; }
        public int CantidadProducida { get; set; }
        public DateTime? FechaInicioProgramada { get; set; }
        public DateTime? FechaFinProgramada { get; set; }
        public int EstatusProgramaID { get; set; }
        public int? OperadorPrincipalPersonaID { get; set; }
        public string? OperadorPrincipalNombre { get; set; }
        public int? OperadorAuxiliarPersonaID { get; set; }
        public string? OperadorAuxiliarNombre { get; set; }
        public int? TecnicoInyeccionPersonaID { get; set; }
        public string? TecnicoInyeccionNombre { get; set; }

        public bool TieneOF => SolicitudProduccionID.HasValue && SolicitudProduccionID.Value > 0;

        public bool TieneConfiguracionMinima =>
            TieneOF &&
            EjecucionProduccionID.HasValue &&
            ChecklistArranqueID.HasValue &&
            ParteID.HasValue &&
            MaquinaID.HasValue &&
            MoldeID.HasValue &&
            MaterialID.HasValue &&
            OperadorPrincipalPersonaID.HasValue;
    }

    public class CalidadPrearranqueViewModel
    {
        [Range(1, int.MaxValue)]
        public int InspeccionID { get; set; }

        public bool AyudaVisualColocada { get; set; }
        public bool? AlertaCalidadAplica { get; set; }
        public bool? AlertaCalidadColocada { get; set; }
        public bool HIPColocada { get; set; }
        public bool HCCColocada { get; set; }
        public bool MatrizPolivalenciaValidada { get; set; }

        [StringLength(1000)]
        public string? Motivo { get; set; }
    }

    public class CalidadPrimerasPiezasViewModel
    {
        [Range(1, int.MaxValue)]
        public int InspeccionID { get; set; }

        public bool CincoDisparosSegregados { get; set; }

        [Range(0, 5)]
        public int CantidadDisparosConformes { get; set; }

        public bool? ValidacionDimensional { get; set; }
        public bool? ValidacionApariencia { get; set; }
        public bool? ValidacionGauge { get; set; }
        public bool? ValidacionConductividad { get; set; }

        [StringLength(1000)]
        public string? Observaciones { get; set; }
    }

    public class CalidadChecklistPreguntaViewModel
    {
        public int ChecklistArranqueDetalleID { get; set; }
        public int PreguntaID { get; set; }
        public string Seccion { get; set; } = string.Empty;
        public int OrdenSeccion { get; set; }
        public int OrdenPregunta { get; set; }
        public string TextoPregunta { get; set; } = string.Empty;
        public bool RequiereObservacionSiNOK { get; set; }
        public string? Resultado { get; set; }
        public string? Observaciones { get; set; }
    }

    public class CalidadChecklistGuardarViewModel
    {
        [Range(1, int.MaxValue)]
        public int InspeccionID { get; set; }

        [Range(1, int.MaxValue)]
        public int ChecklistArranqueID { get; set; }

        [StringLength(1000)]
        public string? ObservacionesCalidad { get; set; }

        public List<CalidadChecklistRespuestaViewModel> Respuestas { get; set; } = new();
    }

    public class CalidadChecklistRespuestaViewModel
    {
        [Range(1, int.MaxValue)]
        public int ChecklistArranqueDetalleID { get; set; }

        public string? Resultado { get; set; }

        [StringLength(500)]
        public string? Observaciones { get; set; }
    }

    public class CalidadMonitoreoGuardarViewModel
    {
        [Range(1, int.MaxValue)]
        public int MonitoreoID { get; set; }

        [Range(1, int.MaxValue)]
        public int InspeccionID { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "La muestra revisada debe ser mayor a cero.")]
        public int CantidadRevisadaMuestra { get; set; }

        [Required]
        [StringLength(20)]
        public string Resultado { get; set; } = CalidadResultadoMonitoreo.Pendiente;

        [StringLength(20)]
        public string? DefectoCodigo { get; set; }

        [StringLength(500)]
        public string? DefectoDescripcion { get; set; }

        [Range(0, int.MaxValue)]
        public int CantidadSospechosa { get; set; }

        [Range(0, int.MaxValue)]
        public int CantidadNoRecuperable { get; set; }

        public bool RequiereSeleccion { get; set; }
        public bool RequiereRetrabajo { get; set; }

        [StringLength(20)]
        public string? ResponsableRetrabajo { get; set; }

        [StringLength(1000)]
        public string? Observaciones { get; set; }
    }

    public class CalidadDisposicionResolverViewModel
    {
        [Range(1, int.MaxValue)]
        public int DisposicionID { get; set; }

        [Range(1, int.MaxValue)]
        public int InspeccionID { get; set; }

        [Range(0, int.MaxValue)]
        public int CantidadLiberada { get; set; }

        [Range(0, int.MaxValue)]
        public int CantidadScrap { get; set; }

        [StringLength(1000)]
        public string? Observaciones { get; set; }
    }

    public class CalidadCajaDecisionViewModel
    {
        [Range(1, int.MaxValue)]
        public int InspeccionID { get; set; }

        [Range(typeof(long), "1", "9223372036854775807")]
        public long CajaProduccionID { get; set; }

        [Required]
        [StringLength(20)]
        public string Decision { get; set; } = string.Empty;

        public bool EstandarPackCumple { get; set; }
        public bool EtiquetaProductoCorrecta { get; set; }

        [StringLength(50)]
        public string? NumeroOperadorEtiqueta { get; set; }

        public bool TecnicoConfirmoInformacion { get; set; }

        [StringLength(100)]
        public string? Tarima { get; set; }

        [StringLength(500)]
        public string? MotivoGP12 { get; set; }

        [StringLength(1000)]
        public string? Observaciones { get; set; }
    }

    public class CalidadCajaGuardarViewModel
    {
        [Range(1, int.MaxValue)]
        public int InspeccionID { get; set; }

        [Required]
        [StringLength(100)]
        public string FolioCaja { get; set; } = string.Empty;

        [Range(1, int.MaxValue)]
        public int CantidadPiezas { get; set; }

        public bool EstandarPackCumple { get; set; }
        public bool EtiquetaProductoCorrecta { get; set; }

        [StringLength(50)]
        public string? NumeroOperadorEtiqueta { get; set; }

        public bool TecnicoConfirmoInformacion { get; set; }

        [StringLength(100)]
        public string? Tarima { get; set; }

        [Required]
        [StringLength(20)]
        public string Destino { get; set; } = CalidadDestinoCaja.Almacen;

        [StringLength(500)]
        public string? MotivoGP12 { get; set; }

        [StringLength(1000)]
        public string? Observaciones { get; set; }
    }

    public class CalidadReliberacionDecisionViewModel
    {
        [Range(1, int.MaxValue)]
        public int ReliberacionID { get; set; }

        public bool Autorizar { get; set; }

        [StringLength(1000)]
        public string? Observaciones { get; set; }
    }

    public class CalidadGP12RevisionGuardarViewModel
    {
        [Range(1, int.MaxValue)]
        public int InspeccionID { get; set; }

        [Range(1, int.MaxValue)]
        public int GP12ID { get; set; }

        [Range(1, int.MaxValue)]
        public int CantidadRevisada { get; set; }

        [Range(0, int.MaxValue)]
        public int CantidadOK { get; set; }

        [Range(0, int.MaxValue)]
        public int CantidadNOK { get; set; }

        [StringLength(1000)]
        public string? Observaciones { get; set; }

        public List<CalidadGP12DefectoGuardarViewModel> Defectos { get; set; } = new();
    }

    public class CalidadGP12DefectoGuardarViewModel
    {
        [Range(1, int.MaxValue)]
        public int CatalogoDefectoID { get; set; }

        [Range(1, int.MaxValue)]
        public int Cantidad { get; set; }

        [StringLength(500)]
        public string? Observaciones { get; set; }
    }

    public class CalidadPrimeraPiezaIntentoItemViewModel
    {
        public int IntentoID { get; set; }
        public int NumeroIntento { get; set; }
        public DateTime FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }
        public bool CincoDisparosSegregados { get; set; }
        public int CantidadDisparosPresentados { get; set; }
        public bool? ValidacionDimensional { get; set; }
        public bool? ValidacionApariencia { get; set; }
        public bool? ValidacionGauge { get; set; }
        public bool? ValidacionConductividad { get; set; }
        public string Resultado { get; set; } = CalidadResultadoIntento.Pendiente;
        public bool AjusteSolicitado { get; set; }
        public string? Observaciones { get; set; }
        public int? UsuarioCalidadID { get; set; }
    }

    public class CalidadMonitoreoItemViewModel
    {
        public int MonitoreoID { get; set; }
        public int? RegistroHoraID { get; set; }
        public int NumeroHora { get; set; }
        public DateTime FechaHoraProgramada { get; set; }
        public DateTime? FechaHoraRevision { get; set; }

        public DateTime? FechaProduccion { get; set; }
        public TimeSpan? HoraInicioProduccion { get; set; }
        public TimeSpan? HoraFinProduccion { get; set; }
        public int CantidadOKProduccion { get; set; }
        public int CantidadSospechosaProduccion { get; set; }
        public int CantidadScrapProduccion { get; set; }
        public string? ObservacionesProduccion { get; set; }

        public int CantidadProducidaPeriodo { get; set; }
        public int CantidadRevisadaMuestra { get; set; }
        public string Resultado { get; set; } = CalidadResultadoMonitoreo.Pendiente;
        public string? DefectoCodigo { get; set; }
        public string? DefectoDescripcion { get; set; }
        public int CantidadSospechosa { get; set; }
        public int CantidadNoRecuperable { get; set; }
        public bool RequiereSeleccion { get; set; }
        public bool RequiereRetrabajo { get; set; }
        public string? ResponsableRetrabajo { get; set; }
        public string? Observaciones { get; set; }

        public bool EsPendiente => Resultado == CalidadResultadoMonitoreo.Pendiente;
        public bool TieneRegistroProduccion => RegistroHoraID.HasValue;
        public bool PuedeCapturar => EsPendiente && TieneRegistroProduccion;
        public bool EstaVencido => EsPendiente && FechaHoraProgramada < DateTime.Now;

        public string RangoProduccion =>
            HoraInicioProduccion.HasValue && HoraFinProduccion.HasValue
                ? $"{HoraInicioProduccion.Value:hh\\:mm} - {HoraFinProduccion.Value:hh\\:mm}"
                : "Sin captura vinculada";
    }

    public class CalidadCatalogoDefectoItemViewModel
    {
        public int CatalogoDefectoID { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Texto => string.IsNullOrWhiteSpace(Codigo) ? Nombre : $"{Codigo} - {Nombre}";
    }

    public class CalidadDisposicionItemViewModel
    {
        public int DisposicionID { get; set; }
        public int? MonitoreoID { get; set; }
        public string TipoMaterial { get; set; } = string.Empty;
        public int CantidadAfectada { get; set; }
        public string? Etiqueta { get; set; }
        public string Disposicion { get; set; } = string.Empty;
        public string? Responsable { get; set; }
        public DateTime FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }
        public int CantidadLiberada { get; set; }
        public int CantidadScrap { get; set; }
        public string ResultadoFinal { get; set; } = string.Empty;
        public string? Observaciones { get; set; }
    }

    public class CalidadCajaItemViewModel
    {
        public int CajaLiberadaID { get; set; }
        public long? CajaProduccionID { get; set; }
        public string FolioCaja { get; set; } = string.Empty;
        public int CantidadPiezas { get; set; }
        public bool EstandarPackCumple { get; set; }
        public bool EtiquetaProductoCorrecta { get; set; }
        public string? NumeroOperadorEtiqueta { get; set; }
        public bool TecnicoConfirmoInformacion { get; set; }
        public DateTime? FechaValidacionCalidad { get; set; }
        public string? Tarima { get; set; }
        public string? Destino { get; set; }
        public string Estado { get; set; } = CalidadEstadoCaja.Pendiente;
    }

    public class CalidadCajaProduccionItemViewModel
    {
        public long CajaProduccionID { get; set; }
        public int InspeccionID { get; set; }
        public int EjecucionProduccionID { get; set; }
        public int ProgramaProduccionID { get; set; }
        public int NumeroCaja { get; set; }
        public string FolioCaja { get; set; } = string.Empty;
        public int CantidadPiezas { get; set; }
        public string TipoCaja { get; set; } = "OK";
        public string? LoteMaterial { get; set; }
        public string? EtiquetaFolio { get; set; }
        public bool EtiquetaVerde { get; set; }
        public int EstadoCajaID { get; set; }
        public string EstadoCajaNombre { get; set; } = string.Empty;
        public DateTime FechaFormacion { get; set; }
        public DateTime? FechaSolicitudCalidad { get; set; }
        public DateTime? FechaLiberacionCalidad { get; set; }
        public string? ResultadoCalidad { get; set; }
        public string? MotivoCalidad { get; set; }
        public string? OrdenTrabajo { get; set; }
        public string? ClienteNombre { get; set; }
        public string? NumeroParte { get; set; }
        public string? Maquina { get; set; }
        public string? Molde { get; set; }

        public bool EstaPendiente => EstadoCajaID == 2;

        public bool PuedeRevisar => EstadoCajaID == 2;
    }

    public class CalidadGP12ItemViewModel
    {
        public int GP12ID { get; set; }
        public int InspeccionID { get; set; }
        public int CajaLiberadaID { get; set; }
        public long? CajaProduccionID { get; set; }
        public string FolioCaja { get; set; } = string.Empty;
        public int CantidadEntrada { get; set; }
        public string? Motivo { get; set; }
        public string Estado { get; set; } = CalidadEstadoGP12.EnInspeccion;
        public DateTime FechaEntrada { get; set; }
        public DateTime? FechaSalida { get; set; }
        public int? CantidadSalida { get; set; }
        public string? Observaciones { get; set; }
        public List<CalidadGP12RevisionItemViewModel> Revisiones { get; set; } = new();

        public bool PuedeRevisar =>
            Estado == CalidadEstadoGP12.EnInspeccion ||
            Estado == CalidadEstadoGP12.NokReinspeccion;
    }

    public class CalidadGP12RevisionItemViewModel
    {
        public int RevisionGP12ID { get; set; }
        public int NumeroRevision { get; set; }
        public DateTime FechaRevision { get; set; }
        public int CantidadRevisada { get; set; }
        public int CantidadOK { get; set; }
        public int CantidadNOK { get; set; }
        public string Resultado { get; set; } = CalidadResultadoGP12.Nok;
        public string? Observaciones { get; set; }
        public List<CalidadGP12DefectoItemViewModel> Defectos { get; set; } = new();
    }

    public class CalidadGP12DefectoItemViewModel
    {
        public int DefectoGP12ID { get; set; }
        public int CatalogoDefectoID { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public int Cantidad { get; set; }
        public string? Observaciones { get; set; }
    }

    public class CalidadReliberacionItemViewModel
    {
        public int ReliberacionID { get; set; }
        public int ParoID { get; set; }
        public int NumeroReliberacion { get; set; }
        public string? Motivo { get; set; }
        public DateTime FechaSolicitud { get; set; }
        public DateTime? FechaValidacion { get; set; }
        public string Resultado { get; set; } = CalidadResultadoReliberacion.Pendiente;
        public string? Observaciones { get; set; }
    }

    public class CalidadHistorialItemViewModel
    {
        public int HistorialID { get; set; }
        public string Movimiento { get; set; } = string.Empty;
        public string? EstadoAnterior { get; set; }
        public string? EstadoNuevo { get; set; }
        public string? ResultadoCalidad { get; set; }
        public string? Etiqueta { get; set; }
        public string? Comentario { get; set; }
        public int? UsuarioID { get; set; }
        public DateTime FechaMovimiento { get; set; }
    }
}
