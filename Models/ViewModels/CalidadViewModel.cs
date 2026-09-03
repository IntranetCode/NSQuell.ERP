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
        public List<CalidadMuestraResguardoItemViewModel> MuestrasResguardo { get; set; } = new();
        public CalidadCierreEstadoViewModel Cierre { get; set; } = new();

        // El checklist se divide por responsabilidad. Producción se muestra
        // únicamente como evidencia de origen; Calidad/Auditor es editable.
        public List<CalidadChecklistPreguntaViewModel> PreguntasChecklistProduccion { get; set; } = new();
        public List<CalidadChecklistPreguntaViewModel> PreguntasChecklistCalidad { get; set; } = new();

        public int? EstatusChecklistArranqueID { get; set; }
        public string? ObservacionesChecklistProduccion { get; set; }
        public string? ObservacionesChecklistCalidad { get; set; }

        public List<CalidadCatalogoDefectoItemViewModel> CatalogoDefectos { get; set; } = new();

        public int TotalPreguntasProduccion => PreguntasChecklistProduccion.Count;
        public int TotalRespondidasProduccion => PreguntasChecklistProduccion.Count(x => x.EstaRespondida);
        public int TotalNokProduccion => PreguntasChecklistProduccion.Count(x => x.EsNok);
        public int TotalPendientesProduccion => TotalPreguntasProduccion - TotalRespondidasProduccion;

        public int TotalPreguntasCalidad => PreguntasChecklistCalidad.Count;
        public int TotalRespondidasCalidad => PreguntasChecklistCalidad.Count(x => x.EstaRespondida);
        public int TotalNokCalidad => PreguntasChecklistCalidad.Count(x => x.EsNok);
        public int TotalPendientesCalidad => TotalPreguntasCalidad - TotalRespondidasCalidad;
        public int TotalNokCalidadSinObservacion => PreguntasChecklistCalidad.Count(x => x.EsNokSinObservacion);

        public bool ChecklistProduccionCompleto =>
            TotalPreguntasProduccion > 0 &&
            TotalPendientesProduccion == 0;

        public bool ChecklistCalidadCompleto =>
            TotalPreguntasCalidad > 0 &&
            TotalPendientesCalidad == 0;

        public bool ChecklistCalidadListoParaAutorizar =>
            ChecklistProduccionCompleto &&
            TotalNokProduccion == 0 &&
            ChecklistCalidadCompleto &&
            TotalNokCalidad == 0 &&
            TotalNokCalidadSinObservacion == 0;

        public bool EsReliberacion =>
            CalidadTipoProceso.EsReliberacion(Proceso) ||
            RequiereReliberacion;

        public int TotalMonitoreos => Monitoreos.Count;
        public int MonitoreosPendientes => Monitoreos.Count(x => x.EsPendiente);
        public int MonitoreosVencidos => Monitoreos.Count(x => x.EstaVencido);
        public int MonitoreosDisponiblesCaptura => Monitoreos.Count(x => x.PuedeCapturar);
        public int MonitoreosEsperandoProduccion => Monitoreos.Count(x => x.EsPendiente && !x.TieneRegistroProduccion);
        public int MonitoreosAtendidos => Monitoreos.Count(x => !x.EsPendiente);
        public int MonitoreosConformes => Monitoreos.Count(x =>
            x.EsFlujoAuditoriaV2
                ? !x.EsPendiente && !x.TieneHallazgo
                : x.Resultado == CalidadResultadoMonitoreo.Conforme);

        public int MonitoreosConHallazgo => Monitoreos.Count(x =>
            x.TieneHallazgo);
        public int MonitoreosReinspeccionados => Monitoreos.Count(x => x.Resultado == CalidadResultadoMonitoreo.Reinspeccion);
        public int TotalMuestraRevisada => Monitoreos.Sum(x => x.CantidadRevisadaMuestra);
        public int TotalMaterialAfectado => Monitoreos.Sum(x => x.CantidadAfectadaCalidad);
        public int DisposicionesPendientes => Disposiciones.Count(x => x.ResultadoFinal == CalidadResultadoDisposicion.Pendiente);

        public int TotalCajasProduccion => CajasProduccion.Count;
        public int CajasPendientesRevision => CajasProduccion.Count(x => x.PuedeRevisar);
        public int CajasLiberadasCalidad => CajasProduccion.Count(x => x.EsLiberada);
        public int CajasEnGP12 => CajasProduccion.Count(x => x.EstaEnGP12);
        public int CajasDevueltasCalidad => CajasProduccion.Count(x => x.EstaDevuelta);
        public int PiezasPendientesRevisionCaja => CajasProduccion
            .Where(x => x.PuedeRevisar)
            .Sum(x => x.CantidadPiezas);

        public int TotalRegistrosGP12 => RegistrosGP12.Count;
        public int GP12Pendientes => RegistrosGP12.Count(x => x.PuedeRevisar);
        public int GP12EnReinspeccion => RegistrosGP12.Count(x => x.RequiereReinspeccion);
        public int GP12Liberados => RegistrosGP12.Count(x => x.EsLiberado);

        public decimal PorcentajeMonitoreosAtendidos => TotalMonitoreos <= 0
            ? 0
            : Math.Round((decimal)MonitoreosAtendidos * 100m / TotalMonitoreos, 1);
        public DateTime? ProximoMonitoreo => Monitoreos
            .Where(x => x.EsPendiente)
            .OrderBy(x => x.FechaHoraProgramada)
            .Select(x => (DateTime?)x.FechaHoraProgramada)
            .FirstOrDefault();

        public int TotalReliberaciones => Reliberaciones.Count;
        public int ReliberacionesPendientes => Reliberaciones.Count(x => x.EsPendiente);
        public int ReliberacionesAutorizadas => Reliberaciones.Count(x => x.EsAutorizada);
        public int ReliberacionesRechazadas => Reliberaciones.Count(x => x.EsRechazada);

        public CalidadReliberacionItemViewModel? UltimaReliberacion =>
            Reliberaciones
                .OrderByDescending(x => x.NumeroReliberacion)
                .ThenByDescending(x => x.FechaSolicitud)
                .FirstOrDefault();

        public bool TieneReliberacionPendiente =>
            Reliberaciones.Any(x => x.EsPendiente);

        public CalidadMuestraResguardoItemViewModel? MuestraFinProduccion =>
            MuestrasResguardo
                .Where(x => x.Momento == CalidadMomentoMuestra.FinProduccion)
                .OrderByDescending(x => x.FechaModificacion ?? x.FechaCreacion)
                .FirstOrDefault();

        public bool MuestraFinProduccionCompleta =>
            MuestraFinProduccion?.EstaCompleta == true;

        public CalidadHistorialItemViewModel? MovimientoCierre =>
            Historial
                .Where(x => x.Movimiento == CalidadMovimientos.Cierre)
                .OrderByDescending(x => x.FechaMovimiento)
                .FirstOrDefault();

        public DateTime? FechaCierre => MovimientoCierre?.FechaMovimiento;
        public int? UsuarioCierreID => MovimientoCierre?.UsuarioID;

        public List<CalidadScrapEntregaItemViewModel> ScrapEntregas { get; set; } = new();
    }

    public class CalidadScrapEntregaItemViewModel
    {
        public long ScrapEntregaID { get; set; }
        public int InspeccionID { get; set; }
        public int DisposicionID { get; set; }

        public int? EjecucionProduccionID { get; set; }
        public int? ProgramaProduccionID { get; set; }

        public int? SolicitudProduccionID { get; set; }
        public int? SolicitudProduccionDetalleID { get; set; }

        public int? ReleaseID { get; set; }
        public int? ReleaseDetalleID { get; set; }

        public int? ParteID { get; set; }

        public string? NumeroParte { get; set; }
        public string? OrdenFabricacion { get; set; }

        public int CantidadScrap { get; set; }

        public string Estado { get; set; } =
            CalidadEstadoScrap.PendienteRecepcion;

        public int? UsuarioEntregaID { get; set; }
        public DateTime? FechaEntrega { get; set; }

        public int? UsuarioRecepcionID { get; set; }
        public DateTime? FechaRecepcion { get; set; }

        public string? UbicacionScrap { get; set; }

        public int? UsuarioMoliendaID { get; set; }
        public DateTime? FechaMolienda { get; set; }
        public decimal? CantidadMolida { get; set; }

        public string? Observaciones { get; set; }

        public DateTime FechaCreacion { get; set; }

        public bool EstaPendienteRecepcion =>
            Estado == CalidadEstadoScrap.PendienteRecepcion;

        public bool EstaRecibido =>
            Estado == CalidadEstadoScrap.RecibidoAlmacen ||
            Estado == CalidadEstadoScrap.PendienteMolienda ||
            Estado == CalidadEstadoScrap.Molido;

        public bool EstaMolido =>
            Estado == CalidadEstadoScrap.Molido;

        public string EstadoTexto => Estado switch
        {
            CalidadEstadoScrap.PendienteRecepcion =>
                "Pendiente de recepción en Almacén",

            CalidadEstadoScrap.RecibidoAlmacen =>
                "Recibido por Almacén",

            CalidadEstadoScrap.PendienteMolienda =>
                "Pendiente de molienda",

            CalidadEstadoScrap.Molido =>
                "Molido",

            CalidadEstadoScrap.Cancelado =>
                "Cancelado",

            _ => Estado.Replace("_", " ")
        };

        public string EstadoBadgeClase => Estado switch
        {
            CalidadEstadoScrap.PendienteRecepcion =>
                "bg-warning text-dark",

            CalidadEstadoScrap.RecibidoAlmacen =>
                "bg-info text-dark",

            CalidadEstadoScrap.PendienteMolienda =>
                "bg-primary",

            CalidadEstadoScrap.Molido =>
                "bg-success",

            CalidadEstadoScrap.Cancelado =>
                "bg-secondary",

            _ => "bg-secondary"
        };
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
        public string? ResponsableSugerido { get; set; }
        public bool RequiereObservacionSiNOK { get; set; }
        public string? Resultado { get; set; }
        public string? Observaciones { get; set; }

        public string? ResultadoNormalizado =>
            string.IsNullOrWhiteSpace(Resultado)
                ? null
                : Resultado.Trim().ToUpperInvariant() switch
                {
                    "N/A" => CalidadChecklistResultado.NoAplica,
                    var valor => valor
                };

        public bool EstaRespondida =>
            ResultadoNormalizado == CalidadChecklistResultado.Ok ||
            ResultadoNormalizado == CalidadChecklistResultado.Nok ||
            ResultadoNormalizado == CalidadChecklistResultado.NoAplica;

        public bool EsOk =>
            ResultadoNormalizado == CalidadChecklistResultado.Ok;

        public bool EsNok =>
            ResultadoNormalizado == CalidadChecklistResultado.Nok;

        public bool EsNoAplica =>
            ResultadoNormalizado == CalidadChecklistResultado.NoAplica;

        public bool EsNokSinObservacion =>
            EsNok &&
            string.IsNullOrWhiteSpace(Observaciones);

        public string ResultadoTexto =>
            EsOk
                ? "OK"
                : EsNok
                    ? "NOK"
                    : EsNoAplica
                        ? "N/A"
                        : "Pendiente";

        public string ResultadoClase =>
            EsOk
                ? "quality-result-ok"
                : EsNok
                    ? "quality-result-nok"
                    : EsNoAplica
                        ? "quality-result-na"
                        : "quality-result-pending";
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

    // NSQ_CALIDAD_MONITOREO_AUDITORIA_V2_MODELS
    public class CalidadMonitoreoScrapV2ViewModel
    {
        [Range(1, int.MaxValue)]
        public int MonitoreoID { get; set; }

        [Range(1, int.MaxValue)]
        public int InspeccionID { get; set; }

        [Range(0, int.MaxValue)]
        public int CantidadScrapValidadaProduccion { get; set; }

        [StringLength(1000)]
        public string? Observaciones { get; set; }
    }

    public class CalidadMonitoreoDisparoV2ViewModel
    {
        [Range(1, int.MaxValue)]
        public int MonitoreoID { get; set; }

        [Range(1, int.MaxValue)]
        public int InspeccionID { get; set; }

        public DateTime FechaHoraDisparo { get; set; }

        public bool MuestraDisparoEmbolsada { get; set; }

        public bool ConHallazgo { get; set; }


        [StringLength(1000)]
        public string? Observaciones { get; set; }
    }

    public class CalidadMonitoreoCajaV2ViewModel
    {
        [Range(1, int.MaxValue)]
        public int MonitoreoID { get; set; }

        [Range(1, int.MaxValue)]
        public int InspeccionID { get; set; }

        [Range(1, int.MaxValue)]
        public int CantidadRevisada { get; set; }

        [Required]
        [StringLength(20)]
        public string Resultado { get; set; } =
            CalidadResultadoMonitoreo.Pendiente;

        public bool MuestraCajaPTConfirmada { get; set; }

        public bool RequiereRetrabajo { get; set; }

        [StringLength(1000)]
        public string? Observaciones { get; set; }
    }
    public class CalidadMonitoreoGuardarViewModel
    {
        [Range(1, int.MaxValue)]
        public int MonitoreoID { get; set; }
        [Range(1, int.MaxValue)]
        public int InspeccionID { get; set; }
        public bool MaterialProduccionRevisado { get; set; }
        [Range(0, int.MaxValue)]
        public int CantidadProduccionLiberada { get; set; }
        [Range(0, int.MaxValue)]
        public int CantidadProduccionSeleccion { get; set; }
        [Range(0, int.MaxValue)]
        public int CantidadProduccionRetrabajo { get; set; }
        [Range(0, int.MaxValue)]
        public int CantidadProduccionScrapConfirmado { get; set; }
        [StringLength(1000)]
        public string? ObservacionesMaterialProduccion { get; set; }
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

    public class CalidadMuestraResguardoGuardarViewModel
    {
        [Range(1, int.MaxValue)]
        public int InspeccionID { get; set; }

        [Required]
        [StringLength(30)]
        public string Momento { get; set; } = CalidadMomentoMuestra.FinProduccion;

        [Range(1, 50, ErrorMessage = "La cantidad de disparos debe estar entre 1 y 50.")]
        public int CantidadDisparos { get; set; } = 2;

        public bool MuestraCalidadConfirmada { get; set; }
        public bool MuestraProduccionConfirmada { get; set; }

        [StringLength(250)]
        public string? UbicacionCalidad { get; set; }

        [StringLength(250)]
        public string? UbicacionProduccion { get; set; }

        [StringLength(1000)]
        public string? Observaciones { get; set; }
    }

    public class CalidadMuestraResguardoItemViewModel
    {
        public int MuestraResguardoID { get; set; }
        public int InspeccionID { get; set; }
        public int EjecucionProduccionID { get; set; }
        public string Momento { get; set; } = CalidadMomentoMuestra.FinProduccion;
        public int CantidadDisparos { get; set; }
        public bool MuestraCalidadConfirmada { get; set; }
        public bool MuestraProduccionConfirmada { get; set; }
        public string? UbicacionCalidad { get; set; }
        public string? UbicacionProduccion { get; set; }
        public DateTime? FechaResguardo { get; set; }
        public int? UsuarioResponsableID { get; set; }
        public string? Observaciones { get; set; }
        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaModificacion { get; set; }

        public bool EstaCompleta =>
            MuestraCalidadConfirmada &&
            MuestraProduccionConfirmada &&
            !string.IsNullOrWhiteSpace(UbicacionCalidad) &&
            !string.IsNullOrWhiteSpace(UbicacionProduccion) &&
            FechaResguardo.HasValue;

        public string MomentoTexto => Momento switch
        {
            CalidadMomentoMuestra.CambioMolde => "Cambio de molde",
            _ => "Fin de producción"
        };

        public string EstadoTexto => EstaCompleta
            ? "Resguardo confirmado"
            : "Resguardo incompleto";

        public string EstadoBadgeClase => EstaCompleta
            ? "bg-success"
            : "bg-warning text-dark";
    }

    public class CalidadCierreEstadoViewModel
    {
        public bool YaCerrada { get; set; }
        public bool ConfiguracionInvalidada { get; set; }
        public int MonitoreosPendientes { get; set; }
        public int DisposicionesPendientes { get; set; }
        public int CajasPendientesCalidad { get; set; }
        public int CajasDevueltasSinResolver { get; set; }
        public int GP12Abiertos { get; set; }
        public int ReliberacionesPendientes { get; set; }
        public bool MuestraFinProduccionCompleta { get; set; }

        public bool EjecucionProduccionExiste { get; set; }
        public bool EjecucionProduccionTerminada { get; set; }
        public bool FechaFinProduccionRegistrada { get; set; }
        public int ParosAbiertos { get; set; }
        public int CajasSinSalidaProduccion { get; set; }
        public bool PuedeCerrar =>
            !YaCerrada &&
            !ConfiguracionInvalidada &&
            MonitoreosPendientes == 0 &&
            DisposicionesPendientes == 0 &&
            CajasPendientesCalidad == 0 &&
            CajasDevueltasSinResolver == 0 &&
            GP12Abiertos == 0 &&
            ReliberacionesPendientes == 0 &&

            MuestraFinProduccionCompleta
            && EjecucionProduccionExiste
&& EjecucionProduccionTerminada
&& FechaFinProduccionRegistrada
&& ParosAbiertos == 0
&& CajasSinSalidaProduccion == 0;

        public List<string> Bloqueos
        {
            get
            {
                var bloqueos = new List<string>();

                if (YaCerrada)
                    bloqueos.Add("La inspección ya está cerrada.");

                if (ConfiguracionInvalidada)
                    bloqueos.Add("La configuración de la inspección está invalidada.");

                if (MonitoreosPendientes > 0)
                    bloqueos.Add($"Faltan {MonitoreosPendientes} monitoreo(s) por atender.");

                if (DisposicionesPendientes > 0)
                    bloqueos.Add($"Existen {DisposicionesPendientes} disposición(es) pendientes.");

                if (CajasPendientesCalidad > 0)
                    bloqueos.Add($"Existen {CajasPendientesCalidad} caja(s) pendientes de revisión de Calidad.");

                if (CajasDevueltasSinResolver > 0)
                    bloqueos.Add($"Existen {CajasDevueltasSinResolver} caja(s) devueltas aún sin resolver.");

                if (GP12Abiertos > 0)
                    bloqueos.Add($"Existen {GP12Abiertos} registro(s) GP12 abiertos.");

                if (ReliberacionesPendientes > 0)
                    bloqueos.Add($"Existen {ReliberacionesPendientes} reliberación(es) pendientes.");

                if (!MuestraFinProduccionCompleta)
                    bloqueos.Add("La muestra de resguardo de fin de producción no está completa.");


                if (!EjecucionProduccionExiste) bloqueos.Add("No existe una ejecución de Producción relacionada.");
                if (EjecucionProduccionExiste && !EjecucionProduccionTerminada) bloqueos.Add("Producción todavía no ha terminado la ejecución.");
                if (EjecucionProduccionExiste && !FechaFinProduccionRegistrada) bloqueos.Add("Producción todavía no registra la fecha real de finalización.");
                if (ParosAbiertos > 0) bloqueos.Add($"Existen {ParosAbiertos} paro(s) abierto(s).");
                if (CajasSinSalidaProduccion > 0) bloqueos.Add($"Existen {CajasSinSalidaProduccion} caja(s) que todavía no registran salida de Producción.");
                return bloqueos;
            }
        }
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

    public sealed class CalidadCajaEscaneoViewModel
    {
        [Required]
        [StringLength(500)]
        public string CodigoBarras { get; set; } = string.Empty;

        public int? InspeccionID { get; set; }
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

        // NSQ_CALIDAD_MONITOREO_AUDITORIA_V2_ITEM
        public byte FlujoMonitoreoVersion { get; set; } = 1;
        public int? CantidadScrapValidadaProduccion { get; set; }
        public string? ObservacionesScrapProduccion { get; set; }

        public DateTime? FechaHoraDisparo { get; set; }
        public int? UsuarioDisparoCalidadID { get; set; }
        public bool? MuestraDisparoEmbolsada { get; set; }
        public bool? DisparoConHallazgo { get; set; }
        public bool? DisparoRetrabajoSolicitado { get; set; }
        public string? ObservacionesDisparo { get; set; }

        public bool? MuestraCajaPTConfirmada { get; set; }

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

        public bool EsFlujoAuditoriaV2 =>
            FlujoMonitoreoVersion >= 2;

        public bool TieneRegistroProduccion =>
            RegistroHoraID.HasValue;

        public bool ScrapValidacionCompleta =>
            !EsFlujoAuditoriaV2 ||
            (
                TieneRegistroProduccion &&
                (
                    CantidadScrapProduccion <= 0 ||
                    CantidadScrapValidadaProduccion.HasValue
                )
            );

        public bool DisparoCompleto =>
            !EsFlujoAuditoriaV2 ||
            (
                FechaHoraDisparo.HasValue &&
                MuestraDisparoEmbolsada == true
            );

        public bool RevisionCajaCompleta =>
            !EsFlujoAuditoriaV2 ||
            (
                FechaHoraRevision.HasValue &&
                Resultado != CalidadResultadoMonitoreo.Pendiente &&
                (
                    Resultado != CalidadResultadoMonitoreo.Conforme ||
                    MuestraCajaPTConfirmada == true
                )
            );

        // NSQ_CALIDAD_MONITOREO_AUDITORIA_V3
        // V3 por hora: B) disparo auditado + C) validacion del conteo del operador.
        // El scrap de Produccion ya NO se resuelve hora por hora: se consolida una
        // sola vez al terminar la ejecucion mediante el registro final acumulado.
        public bool EsPendiente =>
            EsFlujoAuditoriaV2
                ? !(DisparoCompleto &&
                    RevisionCajaCompleta)
                : Resultado == CalidadResultadoMonitoreo.Pendiente;

        public bool PuedeCapturar =>
            EsFlujoAuditoriaV2
                ? FechaHoraProgramada <= DateTime.Now &&
                  TieneRegistroProduccion &&
                  (
                      !DisparoCompleto ||
                      !RevisionCajaCompleta
                  )
                : EsPendiente &&
                  TieneRegistroProduccion &&
                  CantidadProducidaPeriodo > 0;

        public bool EsperandoProduccion =>
            EsPendiente &&
            !TieneRegistroProduccion;

        public bool EstaVencido =>
            EsPendiente &&
            FechaHoraProgramada < DateTime.Now;

        public bool TieneHallazgo =>
            EsFlujoAuditoriaV2
                ? DisparoConHallazgo == true ||
                  Resultado == CalidadResultadoMonitoreo.NoConforme
                : Resultado == CalidadResultadoMonitoreo.Sospechoso ||
                  Resultado == CalidadResultadoMonitoreo.NoConforme;

        public int CantidadAfectadaCalidad =>
            CantidadSospechosa + CantidadNoRecuperable;

        public string ResultadoTexto => Resultado switch
        {
            CalidadResultadoMonitoreo.Conforme => "Conforme",
            CalidadResultadoMonitoreo.Sospechoso => "Sospechoso",
            CalidadResultadoMonitoreo.NoConforme => "No conforme",
            CalidadResultadoMonitoreo.Reinspeccion => "Reinspeccionado",
            _ => "Pendiente"
        };

        public string ResultadoBadgeClase => Resultado switch
        {
            CalidadResultadoMonitoreo.Conforme => "bg-success",
            CalidadResultadoMonitoreo.Sospechoso => "bg-warning text-dark",
            CalidadResultadoMonitoreo.NoConforme => "bg-danger",
            CalidadResultadoMonitoreo.Reinspeccion => "bg-info text-dark",
            _ when EstaVencido => "bg-danger",
            _ => "bg-secondary"
        };

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
        public int? NumeroHora { get; set; }
        public DateTime? FechaHoraRevision { get; set; }
        public string? ResultadoMonitoreoOrigen { get; set; }
        public string? DefectoCodigo { get; set; }
        public string? DefectoDescripcion { get; set; }
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

        public int? DepartamentoResponsableID { get; set; }
        public int? UsuarioResponsableID { get; set; }
        public string EstadoTratamiento { get; set; } = CalidadEstadoTratamiento.PendienteAsignacion;
        public DateTime? FechaInicioTratamiento { get; set; }
        public DateTime? FechaFinTratamiento { get; set; }

        public string OrigenHallazgo { get; set; } = "CALIDAD";
        public int? RegistroHoraID { get; set; }
        public CalidadScrapEntregaItemViewModel? EntregaScrap { get; set; }

        public bool EsPendiente =>
            ResultadoFinal == CalidadResultadoDisposicion.Pendiente;

        public bool EsLiberacionTotal =>
            !EsPendiente &&
            CantidadAfectada > 0 &&
            CantidadLiberada == CantidadAfectada &&
            CantidadScrap == 0;

        public bool EsLiberacionParcial =>
            !EsPendiente &&
            CantidadLiberada > 0 &&
            CantidadScrap > 0;

        public bool EsScrapTotal =>
            !EsPendiente &&
            CantidadAfectada > 0 &&
            CantidadLiberada == 0 &&
            CantidadScrap == CantidadAfectada;

        public int CantidadPendiente =>
            EsPendiente
                ? Math.Max(0, CantidadAfectada - CantidadLiberada - CantidadScrap)
                : 0;

        public string TratamientoTexto => Disposicion switch
        {
            CalidadTipoDisposicion.Seleccion => "Selección",
            CalidadTipoDisposicion.Retrabajo => "Retrabajo",
            CalidadTipoDisposicion.Liberado => "Liberación",
            CalidadTipoDisposicion.Scrap => "Scrap",
            _ => "Pendiente"
        };

        public string ResponsableTexto => Responsable switch
        {
            CalidadResponsable.Produccion => "Producción",
            CalidadResponsable.Calidad => "Calidad",
            _ => "Sin responsable"
        };

        public string ResultadoFinalTexto =>
            EsPendiente
                ? "Material bloqueado"
                : EsLiberacionTotal
                    ? "Liberado completamente"
                    : EsLiberacionParcial
                        ? "Liberado parcialmente con scrap"
                        : EsScrapTotal
                            ? "Scrap total"
                            : ResultadoFinal.Replace("_", " ");

        public string ResultadoFinalBadgeClase =>
            EsPendiente
                ? "bg-warning text-dark"
                : EsLiberacionTotal
                    ? "bg-success"
                    : EsLiberacionParcial
                        ? "bg-warning text-dark"
                        : "bg-danger";

        public string ResultadoFinalBordeClase =>
            EsPendiente
                ? "border-warning"
                : EsLiberacionTotal
                    ? "border-success"
                    : EsLiberacionParcial
                        ? "border-warning"
                        : "border-danger";
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

        public string? CodigoBarrasOrigen { get; set; }
        public string? NumeroOFEtiqueta { get; set; }
        public string? NumeroParteEtiqueta { get; set; }
        public int? CantidadEtiqueta { get; set; }

        public DateTime? FechaEscaneoProduccion { get; set; }
        public DateTime? FechaEscaneoCalidad { get; set; }

        public int? UsuarioEscaneoCalidadID { get; set; }

        public bool RecibidaFisicamente =>
            FechaEscaneoCalidad.HasValue;

        public bool PendienteRecepcionFisica =>
            EstadoCajaID == ProduccionCajaEstatus.PendienteCalidad &&
            !FechaEscaneoCalidad.HasValue;

        public bool EstaPendiente => EstadoCajaID == 2;
        public bool EsLiberada =>
            EstadoCajaID == 3 ||
            string.Equals(ResultadoCalidad, CalidadResultadoCaja.Liberada, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ResultadoCalidad, CalidadResultadoCaja.LiberadaGP12, StringComparison.OrdinalIgnoreCase);
        public bool EstaEnGP12 =>
            EstadoCajaID == 4 ||
            string.Equals(ResultadoCalidad, CalidadResultadoCaja.GP12, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ResultadoCalidad, CalidadResultadoCaja.GP12Nok, StringComparison.OrdinalIgnoreCase);
        public bool EstaDevuelta =>
            string.Equals(ResultadoCalidad, CalidadResultadoCaja.Devuelta, StringComparison.OrdinalIgnoreCase);

        public bool PuedeRevisar => EstadoCajaID == 2;

        public string EstadoVisualTexto =>
            PuedeRevisar
                ? "Pendiente de Calidad"
                : EsLiberada
                    ? "Liberada"
                    : EstaEnGP12
                        ? "En GP12"
                        : EstaDevuelta
                            ? "Devuelta"
                            : string.IsNullOrWhiteSpace(EstadoCajaNombre)
                                ? "Sin estado"
                                : EstadoCajaNombre;

        public string EstadoBadgeClase =>
            PuedeRevisar
                ? "bg-warning text-dark"
                : EsLiberada
                    ? "bg-success"
                    : EstaEnGP12
                        ? "bg-warning text-dark"
                        : EstaDevuelta
                            ? "bg-danger"
                            : "bg-secondary";

        public string EstadoBordeClase =>
            PuedeRevisar
                ? "border-warning"
                : EsLiberada
                    ? "border-success"
                    : EstaEnGP12
                        ? "border-warning"
                        : EstaDevuelta
                            ? "border-danger"
                            : "border-secondary";
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

        public bool RequiereReinspeccion =>
            Estado == CalidadEstadoGP12.NokReinspeccion;

        public bool EsLiberado =>
            Estado == CalidadEstadoGP12.Liberado ||
            Estado == CalidadEstadoGP12.Cerrado;

        public int TotalRevisiones => Revisiones.Count;
        public int TotalNokAcumulado => Revisiones.Sum(x => x.CantidadNOK);
        public CalidadGP12RevisionItemViewModel? UltimaRevision =>
            Revisiones.OrderByDescending(x => x.NumeroRevision).FirstOrDefault();

        public string EstadoTexto => Estado switch
        {
            CalidadEstadoGP12.EnInspeccion => "En inspección",
            CalidadEstadoGP12.NokReinspeccion => "Requiere reinspección",
            CalidadEstadoGP12.Liberado => "Liberado",
            CalidadEstadoGP12.Cerrado => "Cerrado",
            CalidadEstadoGP12.Cancelado => "Cancelado",
            _ => Estado.Replace("_", " ")
        };

        public string EstadoBadgeClase =>
            EsLiberado
                ? "bg-success"
                : RequiereReinspeccion
                    ? "bg-danger"
                    : Estado == CalidadEstadoGP12.Cancelado
                        ? "bg-secondary"
                        : "bg-warning text-dark";

        public string EstadoBordeClase =>
            EsLiberado
                ? "border-success"
                : RequiereReinspeccion
                    ? "border-danger"
                    : Estado == CalidadEstadoGP12.Cancelado
                        ? "border-secondary"
                        : "border-warning";
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

        public string ResultadoTexto =>
            Resultado == CalidadResultadoGP12.Ok ? "Conforme" : "No conforme";

        public string ResultadoBadgeClase =>
            Resultado == CalidadResultadoGP12.Ok ? "bg-success" : "bg-danger";

        public int TotalDefectos => Defectos.Sum(x => x.Cantidad);
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

        public DateTime? FechaInicioParo { get; set; }
        public DateTime? FechaFinParo { get; set; }
        public int DuracionMinutos { get; set; }
        public string? MotivoParoTexto { get; set; }
        public string? DescripcionParo { get; set; }
        public bool EsMayorA15Minutos { get; set; }
        public int? UsuarioSolicitudID { get; set; }
        public int? UsuarioCalidadID { get; set; }

        public bool EsPendiente =>
            string.Equals(
                Resultado,
                CalidadResultadoReliberacion.Pendiente,
                StringComparison.OrdinalIgnoreCase);

        public bool EsAutorizada =>
            string.Equals(
                Resultado,
                CalidadResultadoReliberacion.Autorizada,
                StringComparison.OrdinalIgnoreCase);

        public bool EsRechazada =>
            string.Equals(
                Resultado,
                CalidadResultadoReliberacion.Rechazada,
                StringComparison.OrdinalIgnoreCase);

        public bool EsCancelada =>
            string.Equals(
                Resultado,
                CalidadResultadoReliberacion.Cancelada,
                StringComparison.OrdinalIgnoreCase);

        public string ResultadoTexto =>
            EsPendiente
                ? "Pendiente de validación"
                : EsAutorizada
                    ? "Reliberación autorizada"
                    : EsRechazada
                        ? "Reliberación rechazada"
                        : EsCancelada
                            ? "Cancelada"
                            : string.IsNullOrWhiteSpace(Resultado)
                                ? "Sin resultado"
                                : Resultado.Replace("_", " ");

        public string ResultadoBadgeClase =>
            EsPendiente
                ? "bg-warning text-dark"
                : EsAutorizada
                    ? "bg-success"
                    : EsRechazada
                        ? "bg-danger"
                        : "bg-secondary";

        public string ResultadoBordeClase =>
            EsPendiente
                ? "border-warning"
                : EsAutorizada
                    ? "border-success"
                    : EsRechazada
                        ? "border-danger"
                        : "border-secondary";

        public string DuracionTexto
        {
            get
            {
                if (DuracionMinutos <= 0)
                    return "Sin duración registrada";

                if (DuracionMinutos < 60)
                    return $"{DuracionMinutos} min";

                var horas = DuracionMinutos / 60;
                var minutos = DuracionMinutos % 60;

                return minutos == 0
                    ? $"{horas} h"
                    : $"{horas} h {minutos} min";
            }
        }

        public string RangoParo =>
            FechaInicioParo.HasValue && FechaFinParo.HasValue
                ? $"{FechaInicioParo.Value:dd/MM/yyyy HH:mm} - {FechaFinParo.Value:dd/MM/yyyy HH:mm}"
                : FechaInicioParo.HasValue
                    ? $"Inició {FechaInicioParo.Value:dd/MM/yyyy HH:mm}"
                    : "Sin horario de paro";
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
