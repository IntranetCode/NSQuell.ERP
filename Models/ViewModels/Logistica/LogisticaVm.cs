// LOGISTICA_UX_RAPIDA_V1_4
using System.ComponentModel.DataAnnotations;

namespace ERP.NSQuell.Models.ViewModels.Logistica;

public sealed class LogisticaIndexVm
{
    public string? Busqueda { get; set; }
    public DateTime? FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }
    public string? Estatus { get; set; }
    public int DemandasPendientes { get; set; }
    public int EmbarquesActivos { get; set; }
    public int CargasHoy { get; set; }
    public int EntregasAtrasadas { get; set; }
    public long PiezasPendientes { get; set; }
    public long PiezasPTListas { get; set; }
    public List<LogisticaDemandaVm> Demandas { get; set; } = new();
    public List<LogisticaEmbarqueResumenVm> Embarques { get; set; } = new();
}

public sealed class LogisticaDemandaVm
{
    public int ReleaseDetalleID { get; set; }
    public int ReleaseID { get; set; }
    public string FolioRelease { get; set; } = string.Empty;
    public int? ClienteID { get; set; }
    public string Cliente { get; set; } = string.Empty;
    public int? ParteID { get; set; }
    public string NumeroParte { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public int? SecuenciaEntrega { get; set; }
    public DateTime? FechaCarga { get; set; }
    public DateTime FechaEntrega { get; set; }
    public int CantidadRequerida { get; set; }
    public int CantidadProgramada { get; set; }
    public int PendienteProgramar { get; set; }
    public int? SolicitudProduccionID { get; set; }
    public string NumeroOF { get; set; } = string.Empty;
    public long CajasPTDisponibles { get; set; }
    public long PiezasPTDisponibles { get; set; }
}

public sealed class LogisticaEmbarqueResumenVm
{
    public int EmbarqueID { get; set; }
    public string Folio { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public string Destino { get; set; } = string.Empty;
    public DateTime? FechaCargaProgramada { get; set; }
    public TimeSpan? HoraCargaProgramada { get; set; }
    public DateTime? FechaEntregaProgramada { get; set; }
    public TimeSpan? HoraEntregaProgramada { get; set; }
    public string Estatus { get; set; } = string.Empty;
    public bool TieneIncidencia { get; set; }
    public int TotalPartidas { get; set; }
    public int TotalCajas { get; set; }
    public int TotalPiezasSolicitadas { get; set; }
    public int TotalPiezasDespachadas { get; set; }
}

public sealed class LogisticaCrearVm
{
    [Required]
    public int ReleaseDetalleID { get; set; }

    [Range(1, int.MaxValue)]
    [Display(Name = "Cantidad a programar")]
    public int CantidadSolicitada { get; set; }

    [Required, StringLength(300)]
    public string Destino { get; set; } = string.Empty;

    [StringLength(600)]
    [Display(Name = "Direccion de entrega")]
    public string? DireccionEntrega { get; set; }

    [Required, DataType(DataType.Date)]
    [Display(Name = "Fecha de carga")]
    public DateTime FechaCargaProgramada { get; set; }

    [Display(Name = "Hora de carga")]
    public TimeSpan? HoraCargaProgramada { get; set; }

    [Required, DataType(DataType.Date)]
    [Display(Name = "Fecha de entrega")]
    public DateTime FechaEntregaProgramada { get; set; }

    [Display(Name = "Hora de entrega")]
    public TimeSpan? HoraEntregaProgramada { get; set; }

    public int? RutaID { get; set; }
    public int? UnidadID { get; set; }

    [StringLength(200)]
    public string? OperadorTexto { get; set; }

    [StringLength(1200)]
    public string? Observaciones { get; set; }

    public LogisticaDemandaVm? Demanda { get; set; }
    public List<LogisticaDemandaVm> Demandas { get; set; } = new();

    // LOGISTICA_PROGRAMACION_RAPIDA_V1_4
    public List<LogisticaCajaDisponibleVm> CajasDisponibles { get; set; } = new();
    public List<int> CajaIDs { get; set; } = new();

    public List<LogisticaSelectVm> Rutas { get; set; } = new();
    public List<LogisticaSelectVm> Unidades { get; set; } = new();
}

public sealed class LogisticaDetalleVm
{
    public int EmbarqueID { get; set; }
    public string Folio { get; set; } = string.Empty;
    public int ClienteID { get; set; }
    public string Cliente { get; set; } = string.Empty;
    public string Destino { get; set; } = string.Empty;
    public string DireccionEntrega { get; set; } = string.Empty;
    public string Estatus { get; set; } = string.Empty;
    public DateTime? FechaCargaProgramada { get; set; }
    public TimeSpan? HoraCargaProgramada { get; set; }
    public DateTime? FechaEntregaProgramada { get; set; }
    public TimeSpan? HoraEntregaProgramada { get; set; }
    public string Ruta { get; set; } = string.Empty;
    public string Unidad { get; set; } = string.Empty;
    public string Operador { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;
    public string ReferenciaOperacion { get; set; } = string.Empty;
    public DateTime? FechaPreparacion { get; set; }
    public DateTime? FechaCarga { get; set; }
    public DateTime? FechaSalida { get; set; }
    public DateTime? FechaEntrega { get; set; }
    public bool TieneIncidencia { get; set; }
    public List<LogisticaDetallePartidaVm> Partidas { get; set; } = new();
    public List<LogisticaCajaAsignadaVm> CajasAsignadas { get; set; } = new();
    public List<LogisticaCajaDisponibleVm> CajasDisponibles { get; set; } = new();
    public List<LogisticaDemandaVm> DemandasDisponibles { get; set; } = new();
    public List<LogisticaHistorialVm> Historial { get; set; } = new();


    // =====================================================
    // LOGISTICA CONTROL TOWER - FASE 1
    // =====================================================

    // Estado ejecutivo
    public string EstadoGeneral { get; set; } = "En tiempo";
    public string EstadoGeneralClase { get; set; } = "success";
    public string EstadoGeneralIcono { get; set; } = "fa-circle-check";

    // Avance
    public int PorcentajeAvance { get; set; }
    public string ProximaAccion { get; set; } = string.Empty;
    public string ProximaAccionDetalle { get; set; } = string.Empty;

    // Preparacion
    public int TotalPiezasSolicitadas { get; set; }
    public int TotalPiezasPreparadas { get; set; }
    public int TotalPiezasDespachadas { get; set; }

    public int TotalCajasAsignadas { get; set; }
    public int TotalCajasCargadas { get; set; }

    public int PiezasPendientesPreparar =>
        Math.Max(0, TotalPiezasSolicitadas - TotalPiezasPreparadas);

    public decimal PorcentajePreparacion =>
        TotalPiezasSolicitadas <= 0
            ? 0
            : Math.Min(
                100,
                Math.Round(
                    (decimal)TotalPiezasPreparadas
                    / TotalPiezasSolicitadas * 100,
                    1));

    // Tiempo / riesgo
    public DateTime? FechaHoraCargaProgramada { get; set; }
    public DateTime? FechaHoraEntregaProgramada { get; set; }

    public bool CargaAtrasada { get; set; }
    public bool EntregaAtrasada { get; set; }
    public bool EnRiesgo { get; set; }

    public int? MinutosParaCarga { get; set; }
    public int? MinutosParaEntrega { get; set; }

    public string MensajeRiesgo { get; set; } = string.Empty;

    // Validaciones de salida
    public bool TieneRuta => !string.IsNullOrWhiteSpace(Ruta);
    public bool TieneUnidad => !string.IsNullOrWhiteSpace(Unidad);
    public bool TieneOperador => !string.IsNullOrWhiteSpace(Operador);

    public bool PreparacionCompleta =>
        TotalPiezasSolicitadas > 0
        && TotalPiezasPreparadas >= TotalPiezasSolicitadas;

    public bool CargaCompleta =>
        Estatus is "Cargado" or "En ruta" or "Entregado";

    public bool PuedeSalir =>
        TieneRuta
        && TieneUnidad
        && TieneOperador
        && PreparacionCompleta
        && CargaCompleta;

    // Incidencias
    public int IncidenciasAbiertas { get; set; }
    public int IncidenciasCriticas { get; set; }

    public List<LogisticaChecklistVm> Checklist { get; set; } = new();
    public List<LogisticaIncidenciaVm> Incidencias { get; set; } = new();

    
}

public sealed class LogisticaChecklistVm
{
    public string Codigo { get; set; } = string.Empty;

    public string Concepto { get; set; } = string.Empty;

    public string Descripcion { get; set; } = string.Empty;

    public bool Completo { get; set; }

    public bool Obligatorio { get; set; } = true;

    public string Icono =>
        Completo
            ? "fa-circle-check"
            : "fa-circle-xmark";

    public string Clase =>
        Completo
            ? "success"
            : "danger";
}

public sealed class LogisticaIncidenciaVm
{
    public int IncidenciaID { get; set; }
    public int EmbarqueID { get; set; }

    public string Folio { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public string Severidad { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Responsable { get; set; } = string.Empty;
    public string Estatus { get; set; } = string.Empty;

    public DateTime FechaRegistro { get; set; }
    public DateTime? FechaCompromiso { get; set; }
    public DateTime? FechaCierre { get; set; }

    public string Solucion { get; set; } = string.Empty;
    public string UsuarioRegistro { get; set; } = string.Empty;

    public bool EstaAbierta =>
        Estatus is "Abierta" or "En seguimiento";

    public bool EsCritica =>
        EstaAbierta &&
        string.Equals(
            Severidad,
            "Crítica",
            StringComparison.OrdinalIgnoreCase);
}

public sealed class LogisticaIncidenciaCrearVm
{
    [Required]
    public int EmbarqueID { get; set; }

    [Required, StringLength(80)]
    public string Tipo { get; set; } = string.Empty;

    [Required, StringLength(20)]
    public string Severidad { get; set; } = "Media";

    [Required, StringLength(1200)]
    public string Descripcion { get; set; } = string.Empty;

    [StringLength(200)]
    public string? Responsable { get; set; }

    public DateTime? FechaCompromiso { get; set; }
}

public sealed class LogisticaIncidenciaCerrarVm
{
    [Required]
    public int IncidenciaID { get; set; }

    [Required]
    public int EmbarqueID { get; set; }

    [Required, StringLength(1200)]
    public string Solucion { get; set; } = string.Empty;
}





public sealed class LogisticaDetallePartidaVm
{
    public int EmbarqueDetalleID { get; set; }
    public int? ReleaseDetalleID { get; set; }
    public string FolioRelease { get; set; } = string.Empty;
    public string NumeroParte { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public int? SolicitudProduccionID { get; set; }
    public string NumeroOF { get; set; } = string.Empty;
    public DateTime? FechaCargaRelease { get; set; }
    public DateTime? FechaEntregaRelease { get; set; }
    public int CantidadSolicitada { get; set; }
    public int CantidadDespachada { get; set; }
    public int CantidadAsignada { get; set; }
    public int PendienteAsignar => Math.Max(0, CantidadSolicitada - CantidadAsignada);
}

public sealed class LogisticaCajaAsignadaVm
{
    public int EmbarqueCajaID { get; set; }
    public int EmbarqueDetalleID { get; set; }
    public int CajaID { get; set; }
    public string Etiqueta { get; set; } = string.Empty;
    public int NumeroCaja { get; set; }
    public string NumeroParte { get; set; } = string.Empty;
    public string NumeroOF { get; set; } = string.Empty;
    public string Lote { get; set; } = string.Empty;
    public int CantidadAsignada { get; set; }
    public string Estatus { get; set; } = string.Empty;
}

public sealed class LogisticaCajaDisponibleVm
{
    public int EmbarqueDetalleID { get; set; }
    public int CajaID { get; set; }
    public string Etiqueta { get; set; } = string.Empty;
    public int NumeroCaja { get; set; }
    public string NumeroParte { get; set; } = string.Empty;
    public string NumeroOF { get; set; } = string.Empty;
    public string Lote { get; set; } = string.Empty;
    public string Ubicacion { get; set; } = string.Empty;
    public int Disponible { get; set; }
}

public sealed class LogisticaHistorialVm
{
    public DateTime Fecha { get; set; }
    public string Evento { get; set; } = string.Empty;
    public string EstadoAnterior { get; set; } = string.Empty;
    public string EstadoNuevo { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
}

public sealed class LogisticaAgregarDetalleVm
{
    [Required]
    public int EmbarqueID { get; set; }
    [Required]
    public int ReleaseDetalleID { get; set; }
    [Range(1, int.MaxValue)]
    public int CantidadSolicitada { get; set; }
}

public sealed class LogisticaEntregaVm
{
    [Required]
    public int EmbarqueID { get; set; }
    [Required, StringLength(200)]
    public string ReceptorNombre { get; set; } = string.Empty;
    [StringLength(100)]
    public string? FolioRemision { get; set; }
    [StringLength(1200)]
    public string? Observaciones { get; set; }
}

public sealed class LogisticaCatalogosVm
{
    public List<LogisticaRutaVm> Rutas { get; set; } = new();
    public List<LogisticaUnidadVm> Unidades { get; set; } = new();
}

public sealed class LogisticaRutaVm
{
    public int RutaID { get; set; }
    [Required, StringLength(30)]
    public string Codigo { get; set; } = string.Empty;
    [Required, StringLength(150)]
    public string Nombre { get; set; } = string.Empty;
    [StringLength(500)]
    public string? Descripcion { get; set; }
    public bool Activo { get; set; }
}

public sealed class LogisticaUnidadVm
{
    public int UnidadID { get; set; }
    [Required, StringLength(50)]
    public string NumeroEconomico { get; set; } = string.Empty;
    [StringLength(30)]
    public string? Placas { get; set; }
    [StringLength(80)]
    public string? Marca { get; set; }
    [StringLength(80)]
    public string? Modelo { get; set; }
    [Range(1, int.MaxValue)]
    public int? CapacidadPiezas { get; set; }
    public bool Activo { get; set; }
}

public sealed class LogisticaSelectVm
{
    public int Id { get; set; }
    public string Texto { get; set; } = string.Empty;
}
