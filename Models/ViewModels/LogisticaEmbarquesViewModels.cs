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
    public int EmbarquesPreparados { get; set; }
    public int EmbarquesCargados { get; set; }
    public int EmbarquesEnRuta { get; set; }
    public int EmbarquesEntregados { get; set; }
    public int EmbarquesConIncidencia { get; set; }
    public long CajasMovilizadas { get; set; }
    public long PiezasMovilizadas { get; set; }
    public List<LogisticaDemandaVm> Demandas { get; set; } = new();
    public List<LogisticaEmbarqueResumenVm> Embarques { get; set; } = new();
    public List<LogisticaResumenClienteVm> ResumenClientes { get; set; } = new();
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
    public string TipoOperacion { get; set; } = string.Empty;
    public bool TieneIncidencia { get; set; }
    public int TotalPartidas { get; set; }
    public int TotalCajas { get; set; }
    public int TotalPiezasSolicitadas { get; set; }
    public int TotalPiezasDespachadas { get; set; }
}

public sealed class LogisticaResumenClienteVm
{
    public int ClienteID { get; set; }
    public string Cliente { get; set; } = string.Empty;
    public int TotalEmbarques { get; set; }
    public int Preparados { get; set; }
    public int Cargados { get; set; }
    public int EnRuta { get; set; }
    public int Entregados { get; set; }
    public int ConIncidencia { get; set; }
    public long TotalCajas { get; set; }
    public long TotalPiezas { get; set; }
    public int EntregasATiempo { get; set; }
    public int EntregasAtrasadas { get; set; }
    public decimal PorcentajeCumplimiento => Entregados <= 0 ? 0 : Math.Round((decimal)EntregasATiempo / Entregados * 100m, 1);
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
    [Display(Name = "Dirección de entrega")]
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

    [Required, StringLength(30)]
    [Display(Name = "Tipo de operación")]
    public string TipoOperacion { get; set; } = "Nacional";

    [Required, StringLength(30)]
    [Display(Name = "Forma de envío")]
    public string FormaEnvio { get; set; } = "Interno";

    [StringLength(30)]
    [Display(Name = "Modalidad de envío")]
    public string? ModalidadEnvio { get; set; }

    [StringLength(200)]
    [Display(Name = "Compañía / transportista")]
    public string? Transportista { get; set; }

    [StringLength(150)]
    [Display(Name = "Guía / referencia")]
    public string? GuiaReferencia { get; set; }

    [Display(Name = "¿Pasa por aduana?")]
    public bool? PasaAduana { get; set; }

    public int? RutaID { get; set; }
    public int? UnidadID { get; set; }

    [StringLength(200)]
    public string? OperadorTexto { get; set; }

    [StringLength(1200)]
    public string? Observaciones { get; set; }
    public int? ClienteID { get; set; }
    public List<LogisticaSelectVm> Clientes { get; set; } = new();
    public List<LogisticaCrearPartidaVm> Partidas { get; set; } = new();

    public LogisticaDemandaVm? Demanda { get; set; }
    public List<LogisticaDemandaVm> Demandas { get; set; } = new();
    public List<LogisticaCajaDisponibleVm> CajasDisponibles { get; set; } = new();
    public List<int> CajaIDs { get; set; } = new();
    public List<LogisticaSelectVm> Rutas { get; set; } = new();
    public List<LogisticaSelectVm> Unidades { get; set; } = new();
}

public sealed class LogisticaCrearPartidaVm
{
    public bool Seleccionada { get; set; }
    public int ReleaseDetalleID { get; set; }
    public int CantidadSolicitada { get; set; }
    public List<int> CajaIDs { get; set; } = new();
    public string FolioRelease { get; set; } = string.Empty;
    public string NumeroParte { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string NumeroOF { get; set; } = string.Empty;
    public DateTime? FechaCarga { get; set; }
    public DateTime FechaEntrega { get; set; }
    public int PendienteProgramar { get; set; }
    public long PiezasPTDisponibles { get; set; }
    public List<LogisticaCajaDisponibleVm> CajasDisponibles { get; set; } = new();
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
    public string TipoOperacion { get; set; } = string.Empty;
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
    public string FormaEnvio { get; set; } = string.Empty;
    public string ModalidadEnvio { get; set; } = string.Empty;
    public string Transportista { get; set; } = string.Empty;
    public string GuiaReferencia { get; set; } = string.Empty;
    public bool? PasaAduana { get; set; }
    public List<LogisticaDetallePartidaVm> Partidas { get; set; } = new();
    public List<LogisticaCajaAsignadaVm> CajasAsignadas { get; set; } = new();
    public List<LogisticaCajaDisponibleVm> CajasDisponibles { get; set; } = new();
    public List<LogisticaDemandaVm> DemandasDisponibles { get; set; } = new();
    public List<LogisticaHistorialVm> Historial { get; set; } = new();
    public List<LogisticaEvidenciaVm> Evidencias { get; set; } = new();
    public List<LogisticaDocumentoVm> Documentos { get; set; } = new();
    public List<LogisticaDocumentoRequeridoVm> DocumentosRequeridos { get; set; } = new();

    public string EstadoGeneral { get; set; } = "En tiempo";
    public string EstadoGeneralClase { get; set; } = "success";
    public string EstadoGeneralIcono { get; set; } = "fa-circle-check";
    public int PorcentajeAvance { get; set; }
    public string ProximaAccion { get; set; } = string.Empty;
    public string ProximaAccionDetalle { get; set; } = string.Empty;

    public int TotalPiezasSolicitadas { get; set; }
    public int TotalPiezasPreparadas { get; set; }
    public int TotalPiezasDespachadas { get; set; }
    public int TotalCajasAsignadas { get; set; }
    public int TotalCajasCargadas { get; set; }

    public int PiezasPendientesPreparar => Math.Max(0, TotalPiezasSolicitadas - TotalPiezasPreparadas);
    public decimal PorcentajePreparacion => TotalPiezasSolicitadas <= 0 ? 0 : Math.Min(100, Math.Round((decimal)TotalPiezasPreparadas / TotalPiezasSolicitadas * 100, 1));

    public DateTime? FechaHoraCargaProgramada { get; set; }
    public DateTime? FechaHoraEntregaProgramada { get; set; }
    public bool CargaAtrasada { get; set; }
    public bool EntregaAtrasada { get; set; }
    public bool EnRiesgo { get; set; }
    public int? MinutosParaCarga { get; set; }
    public int? MinutosParaEntrega { get; set; }
    public string MensajeRiesgo { get; set; } = string.Empty;

    public bool TieneRuta => !string.IsNullOrWhiteSpace(Ruta);
    public bool TieneUnidad => !string.IsNullOrWhiteSpace(Unidad);
    public bool TieneOperador => !string.IsNullOrWhiteSpace(Operador);
    public bool PreparacionCompleta => TotalPiezasSolicitadas > 0 && TotalPiezasPreparadas >= TotalPiezasSolicitadas;
    public bool CargaCompleta => Estatus is "Cargado" or "En ruta" or "Entregado";

    public int DocumentosObligatorios => DocumentosRequeridos.Count(x => x.Obligatorio);
    public int DocumentosObligatoriosCompletos => DocumentosRequeridos.Count(x => x.Obligatorio && x.Cargado && x.Validado);
    public int DocumentosFaltantes => DocumentosRequeridos.Count(x => x.Obligatorio && (!x.Cargado || !x.Validado));
    public bool DocumentacionCompleta => DocumentosObligatorios > 0 && DocumentosFaltantes == 0;
    public decimal PorcentajeDocumentacion => DocumentosObligatorios <= 0 ? 0 : Math.Round((decimal)DocumentosObligatoriosCompletos / DocumentosObligatorios * 100m, 1);

    public bool DatosTransporteCompletos => FormaEnvio == "Paqueteria"
    ? !string.IsNullOrWhiteSpace(ModalidadEnvio) && !string.IsNullOrWhiteSpace(Transportista)
    : TieneRuta && TieneUnidad && TieneOperador;

    public bool PuedeSalir => DatosTransporteCompletos && PreparacionCompleta && CargaCompleta && DocumentacionCompleta && IncidenciasCriticas <= 0;
    public int IncidenciasAbiertas { get; set; }
    public int IncidenciasCriticas { get; set; }
    public List<LogisticaChecklistVm> Checklist { get; set; } = new();
    public List<LogisticaIncidenciaVm> Incidencias { get; set; } = new();

    public bool UnidadRetornada { get; set; }
    public DateTime? FechaRetornoUnidad { get; set; }
    public int? KilometrajeRetorno { get; set; }
    public string ObservacionesRetorno { get; set; } = string.Empty;
    public string UsuarioRetorno { get; set; } = string.Empty;
}

public sealed class LogisticaDocumentoVm
{
    public int EmbarqueDocumentoID { get; set; }
    public int EmbarqueID { get; set; }
    public string TipoDocumento { get; set; } = string.Empty;
    public string NombreOriginal { get; set; } = string.Empty;
    public string NombreFisico { get; set; } = string.Empty;
    public string RutaRelativa { get; set; } = string.Empty;
    public string TipoContenido { get; set; } = string.Empty;
    public long TamanoBytes { get; set; }
    public string AreaResponsable { get; set; } = string.Empty;
    public bool EsObligatorio { get; set; }
    public bool Validado { get; set; }
    public int? UsuarioCargaID { get; set; }
    public string UsuarioCargaNombre { get; set; } = string.Empty;
    public DateTime FechaCarga { get; set; }
    public int? UsuarioValidaID { get; set; }
    public string UsuarioValidaNombre { get; set; } = string.Empty;
    public DateTime? FechaValidacion { get; set; }
    public string Observaciones { get; set; } = string.Empty;
    public bool Activo { get; set; }

    public bool EsImagen =>
        TipoContenido.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase)
        || TipoContenido.Equals("image/pjpeg", StringComparison.OrdinalIgnoreCase)
        || TipoContenido.Equals("image/png", StringComparison.OrdinalIgnoreCase);

    public bool EsPdf =>
        TipoContenido.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);

    public bool EsXml =>
        TipoContenido.Equals("application/xml", StringComparison.OrdinalIgnoreCase)
        || TipoContenido.Equals("text/xml", StringComparison.OrdinalIgnoreCase)
        || Extension == ".xml";

    public string Extension =>
        Path.GetExtension(NombreOriginal)?.ToLowerInvariant() ?? string.Empty;

    public string Icono => Extension switch
    {
        ".pdf" => "fa-file-pdf",
        ".xml" => "fa-file-code",
        ".xlsx" or ".xls" => "fa-file-excel",
        ".docx" or ".doc" => "fa-file-word",
        ".jpg" or ".jpeg" or ".png" => "fa-image",
        _ => "fa-file"
    };

    public string EstadoTexto =>
        Validado ? "Validado" : "Pendiente de validación";

    public string EstadoClase =>
        Validado ? "success" : "warning";

    public string TamanoTexto
    {
        get
        {
            if (TamanoBytes <= 0) return "0 KB";
            if (TamanoBytes < 1024) return $"{TamanoBytes} B";
            if (TamanoBytes < 1024 * 1024) return $"{TamanoBytes / 1024d:N1} KB";
            return $"{TamanoBytes / 1024d / 1024d:N1} MB";
        }
    }
}

public sealed class LogisticaDocumentoRequeridoVm
{
    public string TipoDocumento { get; set; } = string.Empty;
    public string AreaResponsable { get; set; } = string.Empty;
    public bool Obligatorio { get; set; } = true;
    public bool Cargado { get; set; }
    public bool Validado { get; set; }
    public LogisticaDocumentoVm? Documento { get; set; }

    public bool Completo => !Obligatorio || (Cargado && Validado);

    public string EstadoTexto
    {
        get
        {
            if (!Cargado) return "Pendiente";
            if (!Validado) return "Pendiente de validación";
            return "Validado";
        }
    }

    public string EstadoClase
    {
        get
        {
            if (!Cargado) return "danger";
            if (!Validado) return "warning";
            return "success";
        }
    }

    public string Icono
    {
        get
        {
            if (!Cargado) return "fa-circle-xmark";
            if (!Validado) return "fa-clock";
            return "fa-circle-check";
        }
    }
}

public sealed class LogisticaEvidenciaVm
{
    public int EvidenciaID { get; set; }
    public int EmbarqueID { get; set; }
    public string TipoEvidencia { get; set; } = string.Empty;
    public string NombreOriginal { get; set; } = string.Empty;
    public string TipoContenido { get; set; } = string.Empty;
    public long TamanoBytes { get; set; }
    public string Observaciones { get; set; } = string.Empty;
    public int? UsuarioID { get; set; }
    public string UsuarioNombre { get; set; } = string.Empty;
    public DateTime FechaCarga { get; set; }

    public bool EsImagen =>
        TipoContenido.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase)
        || TipoContenido.Equals("image/pjpeg", StringComparison.OrdinalIgnoreCase)
        || TipoContenido.Equals("image/png", StringComparison.OrdinalIgnoreCase);

    public bool EsPdf =>
        TipoContenido.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);

    public string Extension =>
        Path.GetExtension(NombreOriginal)?.ToLowerInvariant() ?? string.Empty;

    public string Icono => Extension switch
    {
        ".pdf" => "fa-file-pdf",
        ".jpg" or ".jpeg" or ".png" => "fa-image",
        _ => "fa-file"
    };

    public string TamanoTexto
    {
        get
        {
            if (TamanoBytes <= 0) return "0 KB";
            if (TamanoBytes < 1024) return $"{TamanoBytes} B";
            if (TamanoBytes < 1024 * 1024) return $"{TamanoBytes / 1024d:N1} KB";
            return $"{TamanoBytes / 1024d / 1024d:N1} MB";
        }
    }
}

public sealed class LogisticaReprogramarVm
{
    [Required]
    public int EmbarqueID { get; set; }

    [Required, DataType(DataType.Date)]
    [Display(Name = "Nueva fecha de carga")]
    public DateTime FechaCargaProgramada { get; set; }

    [Display(Name = "Nueva hora de carga")]
    public TimeSpan? HoraCargaProgramada { get; set; }

    [Required, DataType(DataType.Date)]
    [Display(Name = "Nueva fecha de entrega")]
    public DateTime FechaEntregaProgramada { get; set; }

    [Display(Name = "Nueva hora de entrega")]
    public TimeSpan? HoraEntregaProgramada { get; set; }

    [Required, StringLength(80)]
    public string Motivo { get; set; } = string.Empty;

    [StringLength(1200)]
    public string? Observaciones { get; set; }
}

public sealed class LogisticaChecklistVm
{
    public string Codigo { get; set; } = string.Empty;
    public string Concepto { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public bool Completo { get; set; }
    public bool Obligatorio { get; set; } = true;
    public string Icono => Completo ? "fa-circle-check" : "fa-circle-xmark";
    public string Clase => Completo ? "success" : "danger";
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
        EstaAbierta
        && string.Equals(Severidad, "Crítica", StringComparison.OrdinalIgnoreCase);
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

public sealed class LogisticaRetornoVm
{
    public int EmbarqueID { get; set; }
    public DateTime FechaRetorno { get; set; } = DateTime.Now;
    public int? KilometrajeRetorno { get; set; }
    public string Observaciones { get; set; } = string.Empty;
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

public sealed class LogisticaSelectVm
{
    public int Id { get; set; }
    public string Texto { get; set; } = string.Empty;
}