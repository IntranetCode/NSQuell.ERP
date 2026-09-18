using System;
using System.Collections.Generic;
using System.Linq;

namespace ERP.NSQuell.Models;

public static class ProduccionOperativaOrigen
{
    public const string Calendario = "CALENDARIO";
    public const string Agenda = "AGENDA";
    public const string Detalle = "DETALLE";
    public const string Tablet = "TABLET";
    public const string Notificacion = "NOTIFICACION";
    public const string Otro = "OTRO";
}

public static class ProduccionOperativaEtapa
{
    public const string Preparacion = "PREPARACION";
    public const string Liberacion = "LIBERACION";
    public const string Configuracion = "CONFIGURACION";
    public const string Produccion = "PRODUCCION";
    public const string Cierre = "CIERRE";
    public const string Finalizada = "FINALIZADA";

    public static string Nombre(string? etapa)
    {
        return (etapa ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            Preparacion => "Preparación",
            Liberacion => "Liberación",
            Configuracion => "Configuración",
            Produccion => "Producción",
            Cierre => "Cierre",
            Finalizada => "Finalizada",
            _ => "Producción"
        };
    }

    public static int Orden(string? etapa)
    {
        return (etapa ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            Preparacion => 1,
            Liberacion => 2,
            Configuracion => 3,
            Produccion => 4,
            Cierre => 5,
            Finalizada => 6,
            _ => 99
        };
    }

    public static string Icono(string? etapa)
    {
        return (etapa ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            Preparacion => "bi-box-seam",
            Liberacion => "bi-patch-check",
            Configuracion => "bi-sliders",
            Produccion => "bi-gear-wide-connected",
            Cierre => "bi-flag",
            Finalizada => "bi-check-circle",
            _ => "bi-diagram-3"
        };
    }
}

public static class ProduccionOperativaEstadoCentro
{
    public const string Cargando = "CARGANDO";
    public const string Listo = "LISTO";
    public const string SinAccion = "SIN_ACCION";
    public const string Bloqueado = "BLOQUEADO";
    public const string Finalizado = "FINALIZADO";
    public const string Error = "ERROR";
}

public static class ProduccionOperativaTipoContenido
{
    public const string Ninguno = "NINGUNO";
    public const string Resumen = "RESUMEN";
    public const string Preparacion = "PREPARACION";
    public const string Material = "MATERIAL";
    public const string Embalaje = "EMBALAJE";
    public const string Secado = "SECADO";
    public const string Checklist = "CHECKLIST";
    public const string CambioMolde = "CAMBIO_MOLDE";
    public const string Calidad = "CALIDAD";
    public const string ConfiguracionTecnica = "CONFIGURACION_TECNICA";
    public const string InicioSerie = "INICIO_SERIE";
    public const string CapturaHora = "CAPTURA_HORA";
    public const string Paro = "PARO";
    public const string CambioTurno = "CAMBIO_TURNO";
    public const string Cajas = "CAJAS";
    public const string TiempoExtra = "TIEMPO_EXTRA";
    public const string Terminacion = "TERMINACION";
    public const string Cierre = "CIERRE";
    public const string Personal = "PERSONAL";
    public const string Historial = "HISTORIAL";
    public const string Documento = "DOCUMENTO";
    public const string Mensaje = "MENSAJE";
}

public static class ProduccionOperativaModoApertura
{
    public const string Centro = "CENTRO";
    public const string PartialAjax = "PARTIAL_AJAX";
    public const string SoloLectura = "SOLO_LECTURA";
    public const string PaginaRespaldo = "PAGINA_RESPALDO";
}

public static class ProduccionOperativaSeveridad
{
    public const string Info = "INFO";
    public const string Exito = "EXITO";
    public const string Advertencia = "ADVERTENCIA";
    public const string Peligro = "PELIGRO";
}

public static class ProduccionOperativaAccionClave
{
    public const string Resumen = "RESUMEN";
    public const string Planeacion = AgendaOperativaPasoClave.Planeacion;
    public const string Personal = AgendaOperativaPasoClave.Personal;
    public const string Material = AgendaOperativaPasoClave.Material;
    public const string Embalaje = AgendaOperativaPasoClave.Embalaje;
    public const string Secado = AgendaOperativaPasoClave.Secado;
    public const string CambioTolva = "CAMBIO_TOLVA";
    public const string PreparacionMolde = AgendaOperativaPasoClave.PreparacionMolde;
    public const string ChecklistCambioMolde = "CHECKLIST_CAMBIO_MOLDE";
    public const string CambioMolde = AgendaOperativaPasoClave.CambioMolde;
    public const string ChecklistArranque = AgendaOperativaPasoClave.ChecklistArranque;
    public const string PrimerasPiezas = AgendaOperativaPasoClave.PrimerasPiezas;
    public const string Calidad = AgendaOperativaPasoClave.Calidad;
    public const string ReliberacionCalidad = "RELIBERACION_CALIDAD";
    public const string ConfiguracionCorrida = AgendaOperativaPasoClave.ConfiguracionCorrida;
    public const string InicioSerie = AgendaOperativaPasoClave.InicioSerie;
    public const string Produccion = AgendaOperativaPasoClave.Produccion;
    public const string Capturas = AgendaOperativaPasoClave.Capturas;
    public const string Paro = AgendaOperativaPasoClave.Paro;
    public const string CerrarParo = "CERRAR_PARO";
    public const string CambioTurno = "CAMBIO_TURNO";
    public const string RelevoOperador = "RELEVO_OPERADOR";
    public const string MonitoreoTurno = "MONITOREO_TURNO";
    public const string Cajas = AgendaOperativaPasoClave.Cajas;
    public const string ProductoIncompleto = "PRODUCTO_INCOMPLETO";
    public const string TiempoExtra = "TIEMPO_EXTRA";
    public const string AjusteProduccion = "AJUSTE_PRODUCCION";
    public const string Mantenimiento = "MANTENIMIENTO";
    public const string LiberacionMaquina = AgendaOperativaPasoClave.LiberacionMaquina;
    public const string CalidadFinal = AgendaOperativaPasoClave.CalidadFinal;
    public const string GP12 = "GP12";
    public const string Terminacion = "TERMINACION";
    public const string Cierre = AgendaOperativaPasoClave.Cierre;
    public const string CierreDocumental = "CIERRE_DOCUMENTAL";
    public const string Historial = "HISTORIAL";
    public const string DetalleCompleto = "DETALLE_COMPLETO";
}

public sealed class ProduccionCentroOperativoVm
{
    public DateTime FechaConsulta { get; set; } = DateTime.Now;
    public string Origen { get; set; } = ProduccionOperativaOrigen.Calendario;
    public string EstadoCentro { get; set; } = ProduccionOperativaEstadoCentro.Listo;

    public int ProgramaProduccionID { get; set; }
    public int? EjecucionProduccionID { get; set; }
    public int? SolicitudProduccionID { get; set; }
    public int? SolicitudProduccionDetalleID { get; set; }
    public int? ReleaseID { get; set; }
    public int? ReleaseDetalleID { get; set; }

    public string? NumeroOF { get; set; }
    public string? NumeroOFRecibida { get; set; }
    public string? FolioSolicitud { get; set; }

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
    public string? MoldeNombre { get; set; }

    public int CantidadProgramada { get; set; }
    public int CantidadProducida { get; set; }

    public DateTime? FechaInicioProgramada { get; set; }
    public DateTime? FechaFinProgramada { get; set; }
    public DateTime? FechaInicioReal { get; set; }
    public DateTime? FechaFinReal { get; set; }

    public int EstatusProgramaID { get; set; }
    public int? EstatusEjecucionID { get; set; }

    public string EstadoGeneral { get; set; } = AgendaOperativaEstadoGeneral.Programada;
    public string? EstadoGeneralDetalle { get; set; }
    public string Prioridad { get; set; } = AgendaOperativaPrioridad.Normal;

    public bool EsUrgente { get; set; }
    public bool TieneParoAbierto { get; set; }
    public bool TieneInterrupcionUrgente { get; set; }
    public bool MaquinaLiberada { get; set; }
    public bool RequiereAtencionInmediata { get; set; }
    public bool EstaBloqueada { get; set; }
    public string? MotivoBloqueoGeneral { get; set; }

    public string EtapaActual { get; set; } = ProduccionOperativaEtapa.Preparacion;

    public ProduccionOperativaUsuarioVm Usuario { get; set; } = new();
    public ProduccionOperativaPermisosVm Permisos { get; set; } = new();
    public ProduccionOperativaLhRhVm LhRh { get; set; } = new();
    public ProduccionOperativaResumenProcesoVm Resumen { get; set; } = new();
    public ProduccionOperativaMetricasVm Metricas { get; set; } = new();

    public ProduccionOperativaAccionVm? AccionActual { get; set; }
    public ProduccionOperativaAccionVm? SiguienteAccion { get; set; }

    public List<ProduccionOperativaPasoVm> Pasos { get; set; } = new();
    public List<ProduccionOperativaAccionVm> AccionesDisponibles { get; set; } = new();
    public List<ProduccionOperativaBloqueoVm> Bloqueos { get; set; } = new();
    public List<ProduccionOperativaAlertaVm> Alertas { get; set; } = new();
    public List<ProduccionOperativaHistorialItemVm> Historial { get; set; } = new();
    public List<ProduccionOperativaAdjuntoVm> Adjuntos { get; set; } = new();

    /*
     * FUENTES / PUENTES DE MIGRACION.
     *
     * No deben cargarse todos siempre.
     * El controlador llenara solamente los necesarios para la accion
     * que se esta mostrando dentro del Centro Operativo.
     */
    public AgendaOperativaItemVm? Agenda { get; set; }
    public ProduccionDetalleVm? DetalleProduccion { get; set; }
    public ProduccionPreparacionTareaVm? PreparacionActual { get; set; }
    public ProduccionSecadoMaterialVm? SecadoActual { get; set; }
    public ProduccionChecklistVm? ChecklistActual { get; set; }
    public ProduccionConfiguracionTecnicoVm? ConfiguracionTecnicaActual { get; set; }

    public string OFTexto
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(NumeroOFRecibida)) return NumeroOFRecibida.Trim();
            if (!string.IsNullOrWhiteSpace(NumeroOF)) return NumeroOF.Trim();
            if (!string.IsNullOrWhiteSpace(FolioSolicitud)) return FolioSolicitud.Trim();
            return "Sin OF";
        }
    }

    public string ProgramaTexto => $"Programa interno #{ProgramaProduccionID}";

    public string ParteTexto
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ReferenciaSAP)) return ReferenciaSAP.Trim();
            if (!string.IsNullOrWhiteSpace(NumeroParte)) return NumeroParte.Trim();
            return ParteID.HasValue ? $"Parte #{ParteID.Value}" : "Sin parte";
        }
    }

    public string MaquinaTexto
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(MaquinaCodigo) && !string.IsNullOrWhiteSpace(MaquinaNombre))
                return $"{MaquinaCodigo.Trim()} - {MaquinaNombre.Trim()}";
            if (!string.IsNullOrWhiteSpace(MaquinaCodigo)) return MaquinaCodigo.Trim();
            if (!string.IsNullOrWhiteSpace(MaquinaNombre)) return MaquinaNombre.Trim();
            return "Sin máquina";
        }
    }

    public string MoldeTexto
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(MoldeCodigo) && !string.IsNullOrWhiteSpace(MoldeNombre))
                return $"{MoldeCodigo.Trim()} - {MoldeNombre.Trim()}";
            if (!string.IsNullOrWhiteSpace(MoldeCodigo)) return MoldeCodigo.Trim();
            if (!string.IsNullOrWhiteSpace(MoldeNombre)) return MoldeNombre.Trim();
            return "Sin molde";
        }
    }

    public string EstadoGeneralTexto => AgendaOperativaEstadoGeneral.Nombre(EstadoGeneral);
    public string PrioridadTexto => AgendaOperativaPrioridad.Nombre(Prioridad);
    public string EtapaActualTexto => ProduccionOperativaEtapa.Nombre(EtapaActual);

    public bool EsParejaLhRh => LhRh.EsPareja;
    public bool TieneAccionActual => AccionActual != null;
    public bool TieneSiguienteAccion => SiguienteAccion != null;
    public bool TieneBloqueos => Bloqueos.Any(x => x.Activo);
    public bool TieneAlertas => Alertas.Any();
    public bool PuedeEjecutarAccionActual => AccionActual?.DisponibleParaUsuario == true;

    public bool EstaFinalizada =>
        string.Equals(EstadoGeneral, AgendaOperativaEstadoGeneral.Terminada, StringComparison.OrdinalIgnoreCase) ||
        EstatusProgramaID == ProgramaProduccionEstatus.Cerrado ||
        EstatusEjecucionID == ProduccionEstatus.Cerrado;

    public IEnumerable<ProduccionOperativaPasoVm> PasosVisibles =>
        Pasos.Where(x => x.Aplica).OrderBy(x => x.Orden);

    public IEnumerable<ProduccionOperativaAccionVm> AccionesVisibles =>
        AccionesDisponibles.Where(x => x.Aplica && x.Visible).OrderBy(x => x.Orden);
}

public sealed class ProduccionOperativaUsuarioVm
{
    public int UsuarioID { get; set; }
    public string? UsuarioNombre { get; set; }
    public string? NombreCompleto { get; set; }
    public string? Departamento { get; set; }
    public string? Puesto { get; set; }

    public bool EsAdministradorERP { get; set; }
    public bool EsEncargadoProduccion { get; set; }
    public bool EsTecnicoProduccion { get; set; }
    public bool EsSMED { get; set; }
    public bool EsAuxiliarProduccion { get; set; }
    public bool EsOperadorProduccion { get; set; }
    public bool EsCalidad { get; set; }
    public bool EsMantenimiento { get; set; }
    public bool EsConsulta { get; set; }

    public List<string> Roles { get; set; } = new();
    public List<string> Areas { get; set; } = new();

    public string NombreVisible
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(NombreCompleto)) return NombreCompleto.Trim();
            if (!string.IsNullOrWhiteSpace(UsuarioNombre)) return UsuarioNombre.Trim();
            return $"Usuario #{UsuarioID}";
        }
    }

    public bool TieneRol(string? rol)
    {
        if (string.IsNullOrWhiteSpace(rol)) return false;
        return Roles.Any(x => string.Equals(x, rol, StringComparison.OrdinalIgnoreCase));
    }

    public bool PerteneceArea(string? area)
    {
        if (string.IsNullOrWhiteSpace(area)) return false;
        return Areas.Any(x => string.Equals(x, area, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class ProduccionOperativaPermisosVm
{
    public bool PuedeVerCentroOperativo { get; set; }
    public bool PuedeVerDetalleCompleto { get; set; }
    public bool PuedeVerHistorial { get; set; }

    public bool PuedeGestionarPersonal { get; set; }
    public bool PuedeRecibirMateriaPrima { get; set; }
    public bool PuedeGestionarEmbalaje { get; set; }
    public bool PuedeGestionarSecado { get; set; }
    public bool PuedeGestionarCambioMolde { get; set; }
    public bool PuedeGestionarChecklistCambioMolde { get; set; }
    public bool PuedeGestionarChecklistArranque { get; set; }

    public bool PuedeGestionarCalidad { get; set; }
    public bool PuedeGestionarReliberacion { get; set; }

    public bool PuedeGestionarConfiguracionTecnica { get; set; }
    public bool PuedeIniciarSerie { get; set; }

    public bool PuedeRegistrarCapturaHora { get; set; }
    public bool PuedeGestionarParos { get; set; }
    public bool PuedeGestionarCambioTurno { get; set; }
    public bool PuedeGestionarMonitoreoTurno { get; set; }
    public bool PuedeGestionarCajas { get; set; }
    public bool PuedeGestionarProductoIncompleto { get; set; }
    public bool PuedeGestionarTiempoExtra { get; set; }
    public bool PuedeAjustarProduccion { get; set; }

    public bool PuedeLiberarMaquina { get; set; }
    public bool PuedeTerminarProduccion { get; set; }
    public bool PuedeGestionarCalidadFinal { get; set; }
    public bool PuedeGestionarGP12 { get; set; }
    public bool PuedeGestionarCierreDocumental { get; set; }

    public bool PuedeSupervisar { get; set; }
    public bool SoloLectura { get; set; }

    public bool TienePermisosOperativos =>
        PuedeRecibirMateriaPrima ||
        PuedeGestionarEmbalaje ||
        PuedeGestionarSecado ||
        PuedeGestionarCambioMolde ||
        PuedeGestionarChecklistCambioMolde ||
        PuedeGestionarChecklistArranque ||
        PuedeGestionarCalidad ||
        PuedeGestionarConfiguracionTecnica ||
        PuedeIniciarSerie ||
        PuedeRegistrarCapturaHora ||
        PuedeGestionarParos ||
        PuedeGestionarCambioTurno ||
        PuedeGestionarCajas ||
        PuedeGestionarTiempoExtra ||
        PuedeLiberarMaquina ||
        PuedeTerminarProduccion ||
        PuedeGestionarCierreDocumental;
}

public sealed class ProduccionOperativaLhRhVm
{
    public bool EsPareja { get; set; }
    public int? GrupoLhRh { get; set; }

    public string? LadoActual { get; set; }
    public string? LadoPareja { get; set; }

    public int ProgramaActualID { get; set; }
    public int? ProgramaParejaID { get; set; }

    public int? EjecucionActualID { get; set; }
    public int? EjecucionParejaID { get; set; }

    public int? SolicitudParejaID { get; set; }
    public string? NumeroOFPareja { get; set; }

    public int? ParteParejaID { get; set; }
    public string? NumeroPartePareja { get; set; }
    public string? ReferenciaSAPPareja { get; set; }
    public string? DescripcionPartePareja { get; set; }

    public string? EstadoPareja { get; set; }

    public int CantidadProgramadaPareja { get; set; }
    public int CantidadProducidaPareja { get; set; }

    public bool ParejaConsistente { get; set; } = true;
    public string? MotivoInconsistencia { get; set; }

    public string OFParejaTexto =>
        !string.IsNullOrWhiteSpace(NumeroOFPareja)
            ? NumeroOFPareja.Trim()
            : "Sin OF pareja";

    public string ParteParejaTexto =>
        !string.IsNullOrWhiteSpace(ReferenciaSAPPareja)
            ? ReferenciaSAPPareja.Trim()
            : !string.IsNullOrWhiteSpace(NumeroPartePareja)
                ? NumeroPartePareja.Trim()
                : "Sin parte pareja";

    public decimal PorcentajeAvancePareja =>
        CantidadProgramadaPareja <= 0
            ? 0m
            : Math.Min(100m, Math.Round((decimal)CantidadProducidaPareja * 100m / CantidadProgramadaPareja, 2));
}

public sealed class ProduccionOperativaResumenProcesoVm
{
    public bool? PersonalCompleto { get; set; }

    public bool? MateriaPrimaCompleta { get; set; }
    public bool? EmbalajeCompleto { get; set; }
    public bool? SecadoCompleto { get; set; }
    public bool? ChecklistCambioMoldeCompleto { get; set; }
    public bool? CambioMoldeCompleto { get; set; }

    public bool? ChecklistArranqueCompleto { get; set; }
    public bool? CalidadInicialLiberada { get; set; }
    public bool? ConfiguracionTecnicaCompleta { get; set; }
    public bool? SerieIniciada { get; set; }

    public bool? ProduccionActiva { get; set; }
    public bool TieneParoAbierto { get; set; }

    public bool? MaquinaLiberada { get; set; }
    public bool? CalidadFinalCompleta { get; set; }
    public bool? GP12Completo { get; set; }
    public bool? CierreDocumentalCompleto { get; set; }

    public string? EstadoMaterial { get; set; }
    public string? EstadoEmbalaje { get; set; }
    public string? EstadoSecado { get; set; }
    public string? EstadoCambioMolde { get; set; }
    public string? EstadoChecklistCambioMolde { get; set; }
    public string? EstadoChecklistArranque { get; set; }
    public string? EstadoCalidad { get; set; }
    public string? EstadoConfiguracion { get; set; }
    public string? EstadoProduccion { get; set; }
    public string? EstadoCierre { get; set; }

    public decimal PorcentajePreparacion { get; set; }
    public decimal PorcentajeLiberacion { get; set; }
    public decimal PorcentajeProduccion { get; set; }
    public decimal PorcentajeCierre { get; set; }
    public decimal PorcentajeGeneral { get; set; }
}

public sealed class ProduccionOperativaMetricasVm
{
    public int CantidadProgramada { get; set; }
    public int CantidadProducida { get; set; }
    public int CantidadOK { get; set; }
    public int CantidadSospechosa { get; set; }
    public int CantidadScrap { get; set; }

    public int? ObjetivoHoraReferencia { get; set; }
    public int? ObjetivoHoraActual { get; set; }
    public decimal? CicloReferencia { get; set; }
    public decimal? CicloActual { get; set; }
    public int? CavidadesReferencia { get; set; }
    public int? CavidadesActuales { get; set; }
    public long? ContadorActual { get; set; }

    public DateTime? FechaUltimaCaptura { get; set; }
    public DateTime? FechaProximaCaptura { get; set; }

    public int CapturasRequeridas { get; set; }
    public int CapturasCompletadas { get; set; }

    public int MinutosParoActual { get; set; }

    public int CajasFormadas { get; set; }
    public int CajasPendientesCalidad { get; set; }
    public int CajasLiberadasCalidad { get; set; }
    public int CajasEnZonaVerde { get; set; }
    public int CajasPendientesAlmacen { get; set; }
    public int CajasRecibidasAlmacen { get; set; }

    public int CantidadPendiente => Math.Max(0, CantidadProgramada - CantidadProducida);

    public decimal PorcentajeAvance =>
        CantidadProgramada <= 0
            ? 0m
            : Math.Min(100m, Math.Round((decimal)CantidadProducida * 100m / CantidadProgramada, 2));

    public int CapturasPendientes => Math.Max(0, CapturasRequeridas - CapturasCompletadas);
}

public sealed class ProduccionOperativaAccionVm
{
    public int Orden { get; set; }

    public string Clave { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public string? Descripcion { get; set; }

    public string Etapa { get; set; } = ProduccionOperativaEtapa.Produccion;

    public string AreaResponsable { get; set; } = AgendaOperativaArea.Produccion;
    public string? ResponsableNombre { get; set; }
    public int? ResponsableUsuarioID { get; set; }

    public string Estado { get; set; } = AgendaOperativaEstadoPaso.Pendiente;
    public string Prioridad { get; set; } = AgendaOperativaPrioridad.Normal;

    public bool Aplica { get; set; } = true;
    public bool Visible { get; set; } = true;
    public bool Completada { get; set; }
    public bool EnProceso { get; set; }
    public bool Bloqueada { get; set; }
    public bool BloqueaFlujo { get; set; }

    /*
     * EsEjecutable = la accion tiene sentido por estado de negocio.
     * PuedeEjecutarUsuario = el usuario actual tiene permiso.
     * Ambas deben cumplirse para habilitar el boton.
     */
    public bool EsEjecutable { get; set; }
    public bool PuedeEjecutarUsuario { get; set; }
    public bool SoloLectura { get; set; }

    public string? MotivoBloqueo { get; set; }

    public DateTime? FechaObjetivo { get; set; }
    public DateTime? FechaDisponibleDesde { get; set; }
    public DateTime? FechaInicioReal { get; set; }
    public DateTime? FechaFinReal { get; set; }

    public bool EstaVencida { get; set; }
    public int MinutosDesfase { get; set; }

    public bool RequiereConfirmacion { get; set; }
    public string? TextoConfirmacion { get; set; }

    public bool EsAccionFisicaCompartidaLhRh { get; set; }
    public bool RequiereParejaLhRhCompleta { get; set; }

    public string Icono { get; set; } = "bi-arrow-right-circle";
    public string TextoBoton { get; set; } = "Atender";

    public string TipoContenido { get; set; } = ProduccionOperativaTipoContenido.Resumen;
    public string ModoApertura { get; set; } = ProduccionOperativaModoApertura.PartialAjax;

    /*
     * VistaParcial se usa si ProduccionOperativaController
     * renderiza directamente una partial reutilizable.
     */
    public string? VistaParcial { get; set; }

    public ProduccionOperativaDestinoVm Destino { get; set; } = new();

    public bool RefrescarCentroAlCompletar { get; set; } = true;
    public bool RefrescarCalendarioAlCompletar { get; set; } = true;
    public bool CerrarCentroAlCompletar { get; set; }

    /*
     * Metadatos es solo para informacion auxiliar de interfaz.
     * No debe sustituir modelos POST fuertemente tipados.
     */
    public Dictionary<string, string?> Metadatos { get; set; } = new();

    public bool DisponibleParaUsuario =>
        Aplica &&
        Visible &&
        !Completada &&
        !Bloqueada &&
        EsEjecutable &&
        PuedeEjecutarUsuario &&
        !SoloLectura;

    public string EstadoTexto => AgendaOperativaEstadoPaso.Nombre(Estado);
    public string PrioridadTexto => AgendaOperativaPrioridad.Nombre(Prioridad);
    public string AreaResponsableTexto => AgendaOperativaArea.Nombre(AreaResponsable);
    public string EtapaTexto => ProduccionOperativaEtapa.Nombre(Etapa);
}

public sealed class ProduccionOperativaDestinoVm
{
    /*
     * Destino utilizado para cargar contenido dentro del modal.
     * No sustituye las rutas POST originales de los formularios.
     */
    public string? Controlador { get; set; }
    public string? Accion { get; set; }
    public string? ParametroId { get; set; }
    public int? IdDestino { get; set; }

    public string MetodoHttp { get; set; } = "GET";
    public bool UsarAjax { get; set; } = true;

    public string? UrlDirecta { get; set; }
    public string? VistaParcial { get; set; }

    public Dictionary<string, string?> ParametrosRuta { get; set; } = new();

    public bool TieneDestino =>
        !string.IsNullOrWhiteSpace(UrlDirecta) ||
        (
            !string.IsNullOrWhiteSpace(Controlador) &&
            !string.IsNullOrWhiteSpace(Accion)
        ) ||
        !string.IsNullOrWhiteSpace(VistaParcial);
}

public sealed class ProduccionOperativaPasoVm
{
    public int Orden { get; set; }

    public string Clave { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }

    public string Etapa { get; set; } = ProduccionOperativaEtapa.Produccion;
    public string AreaResponsable { get; set; } = AgendaOperativaArea.Produccion;

    public string Estado { get; set; } = AgendaOperativaEstadoPaso.Pendiente;

    public bool Aplica { get; set; } = true;
    public bool Completado { get; set; }
    public bool EnProceso { get; set; }
    public bool Bloqueado { get; set; }
    public bool BloqueaFlujo { get; set; }

    public bool PuedeEjecutarUsuario { get; set; }

    public string? MotivoBloqueo { get; set; }
    public string? Detalle { get; set; }

    public DateTime? FechaObjetivo { get; set; }
    public DateTime? FechaInicioReal { get; set; }
    public DateTime? FechaFinReal { get; set; }

    public bool EstaVencido { get; set; }
    public int MinutosDesfase { get; set; }

    public string? AccionClave { get; set; }

    public string EstadoTexto => AgendaOperativaEstadoPaso.Nombre(Estado);
    public string AreaResponsableTexto => AgendaOperativaArea.Nombre(AreaResponsable);
    public string EtapaTexto => ProduccionOperativaEtapa.Nombre(Etapa);

    public bool EsPendienteOperativo =>
        Aplica &&
        !Completado &&
        !string.Equals(Estado, AgendaOperativaEstadoPaso.NoAplica, StringComparison.OrdinalIgnoreCase);
}

public sealed class ProduccionOperativaBloqueoVm
{
    public string Clave { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public string? Descripcion { get; set; }

    public string? AccionClave { get; set; }
    public string? AreaResponsable { get; set; }

    public string Severidad { get; set; } = ProduccionOperativaSeveridad.Advertencia;

    public bool Activo { get; set; } = true;
    public bool BloqueaFlujo { get; set; } = true;

    public DateTime? FechaDeteccion { get; set; }
    public int? UsuarioResponsableID { get; set; }
    public string? UsuarioResponsableNombre { get; set; }

    public string AreaResponsableTexto =>
        string.IsNullOrWhiteSpace(AreaResponsable)
            ? "Por definir"
            : AgendaOperativaArea.Nombre(AreaResponsable);
}

public sealed class ProduccionOperativaAlertaVm
{
    public string Clave { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public string? Mensaje { get; set; }

    public string Severidad { get; set; } = ProduccionOperativaSeveridad.Info;
    public string Icono { get; set; } = "bi-info-circle";

    public string? AccionClave { get; set; }

    public DateTime Fecha { get; set; } = DateTime.Now;
    public DateTime? FechaVencimiento { get; set; }

    public bool RequiereAtencion { get; set; }
    public bool Descartable { get; set; }
}

public sealed class ProduccionOperativaHistorialItemVm
{
    public long? HistorialID { get; set; }

    public string Tipo { get; set; } = string.Empty;
    public string Evento { get; set; } = string.Empty;
    public string? Descripcion { get; set; }

    public string? Etapa { get; set; }
    public string? AccionClave { get; set; }

    public int? UsuarioID { get; set; }
    public string? UsuarioNombre { get; set; }

    public DateTime Fecha { get; set; }

    public string? EstadoAnterior { get; set; }
    public string? EstadoNuevo { get; set; }

    public string? Referencia { get; set; }
    public string? Icono { get; set; }

    public Dictionary<string, string?> Metadatos { get; set; } = new();

    public string UsuarioTexto =>
        string.IsNullOrWhiteSpace(UsuarioNombre)
            ? UsuarioID.HasValue ? $"Usuario #{UsuarioID.Value}" : "Sistema"
            : UsuarioNombre.Trim();
}

public sealed class ProduccionOperativaAdjuntoVm
{
    public long? AdjuntoID { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public string? Ruta { get; set; }
    public string? Url { get; set; }

    public string? CodigoFormato { get; set; }
    public string? VersionFormato { get; set; }

    public DateTime? Fecha { get; set; }

    public bool PuedeVer { get; set; } = true;
    public bool PuedeDescargar { get; set; }
    public bool PuedeReemplazar { get; set; }
}

public sealed class ProduccionOperativaCargaRequestVm
{
    public int ProgramaProduccionID { get; set; }
    public int? EjecucionProduccionID { get; set; }

    public string? AccionClave { get; set; }

    public string Origen { get; set; } = ProduccionOperativaOrigen.Calendario;

    public bool SoloLectura { get; set; }
}

public sealed class ProduccionOperativaContenidoRequestVm
{
    public int ProgramaProduccionID { get; set; }
    public int? EjecucionProduccionID { get; set; }

    public string AccionClave { get; set; } = string.Empty;

    public string Origen { get; set; } = ProduccionOperativaOrigen.Calendario;

    public bool SoloLectura { get; set; }
    public bool Refrescar { get; set; }
}

public sealed class ProduccionOperativaRespuestaVm
{
    public bool Ok { get; set; }
    public bool SesionExpirada { get; set; }

    public string Mensaje { get; set; } = string.Empty;
    public string Severidad { get; set; } = ProduccionOperativaSeveridad.Exito;

    public int ProgramaProduccionID { get; set; }
    public int? EjecucionProduccionID { get; set; }

    public string? AccionCompletadaClave { get; set; }
    public string? AccionActualClave { get; set; }
    public string? SiguienteAccionClave { get; set; }

    public bool RefrescarCentro { get; set; } = true;
    public bool RefrescarCalendario { get; set; } = true;
    public bool CerrarModal { get; set; }

    public List<string> Errores { get; set; } = new();
    public Dictionary<string, string?> Metadatos { get; set; } = new();

    public static ProduccionOperativaRespuestaVm Exito(
        int programaProduccionId,
        int? ejecucionProduccionId,
        string mensaje)
    {
        return new ProduccionOperativaRespuestaVm
        {
            Ok = true,
            ProgramaProduccionID = programaProduccionId,
            EjecucionProduccionID = ejecucionProduccionId,
            Mensaje = mensaje,
            Severidad = ProduccionOperativaSeveridad.Exito,
            RefrescarCentro = true,
            RefrescarCalendario = true
        };
    }

    public static ProduccionOperativaRespuestaVm Error(
        int programaProduccionId,
        int? ejecucionProduccionId,
        string mensaje)
    {
        return new ProduccionOperativaRespuestaVm
        {
            Ok = false,
            ProgramaProduccionID = programaProduccionId,
            EjecucionProduccionID = ejecucionProduccionId,
            Mensaje = mensaje,
            Severidad = ProduccionOperativaSeveridad.Peligro,
            RefrescarCentro = false,
            RefrescarCalendario = false
        };
    }
}