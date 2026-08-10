using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;


//HOLA
namespace ERP.NSQuell.Models
{
    // ============================================================
    // ENTIDADES DE BASE DE DATOS
    // ============================================================

    [Table("GP12_Solicitudes")]
    public class GP12Solicitud
    {
        [Key]
        public int SolicitudGP12ID { get; set; }

        [Required]
        [StringLength(20)]
        public string Origen { get; set; } = GP12Origen.Manual;

        public int? ProgramaProduccionID { get; set; }
        public int? EjecucionProduccionID { get; set; }
        public int? CalidadInspeccionID { get; set; }

        public int? SolicitudProduccionID { get; set; }
        public int? SolicitudProduccionDetalleID { get; set; }

        [StringLength(100)]
        public string? OrdenFabricacion { get; set; }

        public int? ClienteID { get; set; }

        [StringLength(250)]
        public string? ClienteNombre { get; set; }

        public int? ParteID { get; set; }

        [StringLength(150)]
        public string? NumeroParte { get; set; }

        [StringLength(500)]
        public string? DescripcionParte { get; set; }

        public int? MaterialID { get; set; }

        [StringLength(150)]
        public string? MaterialCodigo { get; set; }

        [StringLength(500)]
        public string? MaterialDescripcion { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal CantidadSolicitada { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal CantidadRecibida { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal CantidadProcesada { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal CantidadPendiente { get; set; }

        [StringLength(1000)]
        public string? Motivo { get; set; }

        [StringLength(250)]
        public string? InstruccionTrabajo { get; set; }

        [StringLength(100)]
        public string? CodigoHIP { get; set; }

        [StringLength(100)]
        public string? CodigoHOE { get; set; }

        [StringLength(2000)]
        public string? Observaciones { get; set; }

        public int EstatusID { get; set; } = GP12Estatus.Recibido;

        public DateTime FechaSolicitud { get; set; } = DateTime.Now;
        public DateTime? FechaRecepcion { get; set; }
        public DateTime? FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }
        public DateTime? FechaCierre { get; set; }

        public int? UsuarioSolicitudID { get; set; }
        public int? UsuarioRecepcionID { get; set; }

        public int? UsuarioCreacionID { get; set; }
        public DateTime FechaCreacion { get; set; } = DateTime.Now;

        public int? UsuarioModificacionID { get; set; }
        public DateTime? FechaModificacion { get; set; }

        public bool Activo { get; set; } = true;
    }


    [Table("GP12_SolicitudEtiquetas")]
    public class GP12SolicitudEtiqueta
    {
        [Key]
        public int SolicitudEtiquetaID { get; set; }

        public int SolicitudGP12ID { get; set; }

        [Required]
        [StringLength(20)]
        public string TipoEtiqueta { get; set; } =
            GP12TipoEtiqueta.SinClasificar;

        [Column(TypeName = "decimal(18,4)")]
        public decimal CantidadSolicitada { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal CantidadRecibida { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal CantidadProcesada { get; set; }

        public int? UsuarioCreacionID { get; set; }
        public DateTime FechaCreacion { get; set; } = DateTime.Now;

        public int? UsuarioModificacionID { get; set; }
        public DateTime? FechaModificacion { get; set; }

        public bool Activo { get; set; } = true;
    }


    [Table("GP12_InventarioMovimientos")]
    public class GP12InventarioMovimiento
    {
        [Key]
        public int MovimientoID { get; set; }

        public int SolicitudGP12ID { get; set; }
        public int? SolicitudEtiquetaID { get; set; }

        [Required]
        [StringLength(20)]
        public string TipoMovimiento { get; set; } =
            GP12TipoMovimiento.Entrada;

        [Column(TypeName = "decimal(18,4)")]
        public decimal Cantidad { get; set; }

        public int? CajaID { get; set; }
        public int? TarimaID { get; set; }

        [StringLength(250)]
        public string? Referencia { get; set; }

        [StringLength(1000)]
        public string? Observaciones { get; set; }

        public DateTime FechaMovimiento { get; set; } = DateTime.Now;

        public int? UsuarioID { get; set; }

        public bool Activo { get; set; } = true;
    }


    [Table("GP12_Programacion")]
    public class GP12Programacion
    {
        [Key]
        public int ProgramacionGP12ID { get; set; }

        public int SolicitudGP12ID { get; set; }
        public int? SolicitudEtiquetaID { get; set; }

        public DateTime FechaProgramada { get; set; }

        public TimeSpan? HoraInicioProgramada { get; set; }
        public TimeSpan? HoraFinProgramada { get; set; }

        public int Prioridad { get; set; } = 3;

        [Column(TypeName = "decimal(18,4)")]
        public decimal CantidadProgramada { get; set; }

        [StringLength(1000)]
        public string? Observaciones { get; set; }

        public int? UsuarioProgramacionID { get; set; }

        public DateTime FechaCreacion { get; set; } = DateTime.Now;

        public int? UsuarioModificacionID { get; set; }
        public DateTime? FechaModificacion { get; set; }

        public bool Activo { get; set; } = true;
    }


    [Table("GP12_Asignaciones")]
    public class GP12Asignacion
    {
        [Key]
        public int AsignacionGP12ID { get; set; }

        public int ProgramacionGP12ID { get; set; }
        public int SolicitudGP12ID { get; set; }

        public int PersonaID { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal CantidadAsignada { get; set; }

        public DateTime FechaAsignacion { get; set; } = DateTime.Now;

        public DateTime? FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }

        public bool Cumplida { get; set; }

        [StringLength(1000)]
        public string? Observaciones { get; set; }

        public int? UsuarioAsignacionID { get; set; }

        public int? UsuarioCreacionID { get; set; }
        public DateTime FechaCreacion { get; set; } = DateTime.Now;

        public int? UsuarioModificacionID { get; set; }
        public DateTime? FechaModificacion { get; set; }

        public bool Activo { get; set; } = true;
    }


    [Table("GP12_Inspecciones")]
    public class GP12Inspeccion
    {
        [Key]
        public int InspeccionGP12ID { get; set; }

        public int SolicitudGP12ID { get; set; }
        public int AsignacionGP12ID { get; set; }

        public int PersonaInspectorID { get; set; }

        public DateTime? FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal CantidadRevisada { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal CantidadOK { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal CantidadNOK { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal CantidadRetrabajada { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal CantidadScrap { get; set; }

        public bool ValidacionEtiqueta { get; set; }
        public bool DocumentacionColocada { get; set; }
        public bool RutaInspeccionValidada { get; set; }
        public bool CantidadBasculaValidada { get; set; }
        public bool EtiquetaInspeccionColocada { get; set; }

        [StringLength(2000)]
        public string? Observaciones { get; set; }

        public int? UsuarioCreacionID { get; set; }
        public DateTime FechaCreacion { get; set; } = DateTime.Now;

        public int? UsuarioModificacionID { get; set; }
        public DateTime? FechaModificacion { get; set; }

        public bool Activo { get; set; } = true;
    }


    [Table("GP12_CatalogoDefectos")]
    public class GP12CatalogoDefecto
    {
        [Key]
        public int DefectoID { get; set; }

        [Required]
        [StringLength(20)]
        public string Codigo { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        public string Nombre { get; set; } = string.Empty;

        public int Orden { get; set; }
        public bool Activo { get; set; } = true;

        [NotMapped]
        public string Texto => $"{Codigo} - {Nombre}";
    }


    [Table("GP12_InspeccionDefectos")]
    public class GP12InspeccionDefecto
    {
        [Key]
        public int InspeccionDefectoID { get; set; }

        public int InspeccionGP12ID { get; set; }
        public int DefectoID { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal Cantidad { get; set; }

        [StringLength(500)]
        public string? Observaciones { get; set; }

        public int? UsuarioCreacionID { get; set; }
        public DateTime FechaCreacion { get; set; } = DateTime.Now;

        public int? UsuarioModificacionID { get; set; }
        public DateTime? FechaModificacion { get; set; }

        public bool Activo { get; set; } = true;
    }


    [Table("GP12_Cajas")]
    public class GP12Caja
    {
        [Key]
        public int CajaID { get; set; }

        public int SolicitudGP12ID { get; set; }
        public int? InspeccionGP12ID { get; set; }

        [Required]
        [StringLength(100)]
        public string FolioCaja { get; set; } = string.Empty;

        [StringLength(100)]
        public string? NumeroEtiqueta { get; set; }

        [StringLength(50)]
        public string? NumeroOperador { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal CantidadPiezas { get; set; }

        public bool CantidadBasculaValidada { get; set; }
        public DateTime? FechaValidacionBascula { get; set; }

        public bool EtiquetaInspeccionColocada { get; set; }
        public DateTime? FechaEtiquetado { get; set; }
        public int? UsuarioEtiquetadoID { get; set; }

        [Required]
        [StringLength(30)]
        public string Estado { get; set; } = GP12EstadoCaja.Recibida;

        public DateTime? FechaSalida { get; set; }

        [StringLength(1000)]
        public string? Observaciones { get; set; }

        public int? UsuarioCreacionID { get; set; }
        public DateTime FechaCreacion { get; set; } = DateTime.Now;

        public int? UsuarioModificacionID { get; set; }
        public DateTime? FechaModificacion { get; set; }

        public bool Activo { get; set; } = true;
    }


    [Table("GP12_Tarimas")]
    public class GP12Tarima
    {
        [Key]
        public int TarimaID { get; set; }

        [Required]
        [StringLength(100)]
        public string FolioTarima { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        public string Estado { get; set; } = GP12EstadoTarima.Abierta;

        public DateTime FechaApertura { get; set; } = DateTime.Now;
        public DateTime? FechaCierre { get; set; }
        public DateTime? FechaSalida { get; set; }

        public int? UsuarioAperturaID { get; set; }
        public int? UsuarioCierreID { get; set; }
        public int? UsuarioSalidaID { get; set; }

        [StringLength(1000)]
        public string? Observaciones { get; set; }

        public DateTime FechaCreacion { get; set; } = DateTime.Now;

        public int? UsuarioModificacionID { get; set; }
        public DateTime? FechaModificacion { get; set; }

        public bool Activo { get; set; } = true;
    }


    [Table("GP12_TarimaCajas")]
    public class GP12TarimaCaja
    {
        [Key]
        public int TarimaCajaID { get; set; }

        public int TarimaID { get; set; }
        public int CajaID { get; set; }

        public DateTime FechaColocacion { get; set; } = DateTime.Now;
        public int? UsuarioColocacionID { get; set; }

        public DateTime? FechaRetiro { get; set; }
        public int? UsuarioRetiroID { get; set; }

        public bool Activo { get; set; } = true;
    }


    [Table("GP12_Historial")]
    public class GP12Historial
    {
        [Key]
        public int HistorialGP12ID { get; set; }

        public int SolicitudGP12ID { get; set; }

        [Required]
        [StringLength(100)]
        public string Movimiento { get; set; } = string.Empty;

        public int? EstatusAnteriorID { get; set; }
        public int? EstatusNuevoID { get; set; }

        [StringLength(30)]
        public string? Entidad { get; set; }

        public int? EntidadID { get; set; }

        [StringLength(2000)]
        public string? Comentario { get; set; }

        public int? UsuarioID { get; set; }

        public DateTime FechaMovimiento { get; set; } = DateTime.Now;
    }


    [Table("GP12_CierresTurno")]
    public class GP12CierreTurno
    {
        [Key]
        public int CierreTurnoID { get; set; }

        public DateTime FechaOperacion { get; set; }

        [Required]
        [StringLength(50)]
        public string Turno { get; set; } = string.Empty;

        public int ResponsablePersonaID { get; set; }

        public int TotalAsignaciones { get; set; }
        public int AsignacionesCumplidas { get; set; }
        public int AsignacionesPendientes { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal CantidadRevisada { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal CantidadOK { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal CantidadNOK { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal CantidadRetrabajada { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal CantidadScrap { get; set; }

        public bool EnviadoCalidad { get; set; }
        public DateTime? FechaEnvioCalidad { get; set; }
        public int? UsuarioEnvioCalidadID { get; set; }

        public bool ReporteContencionRegistrado { get; set; }

        [StringLength(250)]
        public string? ReferenciaReporteContencion { get; set; }

        [StringLength(2000)]
        public string? Observaciones { get; set; }

        public DateTime FechaCierre { get; set; } = DateTime.Now;

        public int? UsuarioCierreID { get; set; }

        public DateTime FechaCreacion { get; set; } = DateTime.Now;

        public int? UsuarioModificacionID { get; set; }
        public DateTime? FechaModificacion { get; set; }

        public bool Activo { get; set; } = true;
    }


    // ============================================================
    // VIEWMODELS - BANDEJA
    // ============================================================

    public class GP12IndexViewModel
    {
        public string? Busqueda { get; set; }
        public int? EstatusID { get; set; }
        public string? Origen { get; set; }

        public int TotalMostrados { get; set; }

        public int TotalRecibidos { get; set; }
        public int TotalPendientesProgramar { get; set; }
        public int TotalProgramados { get; set; }
        public int TotalAsignados { get; set; }
        public int TotalEnInspeccion { get; set; }
        public int TotalInspeccionPausada { get; set; }
        public int TotalInspeccionTerminada { get; set; }
        public int TotalEnTarima { get; set; }
        public int TotalSalidaRegistrada { get; set; }
        public int TotalCerrados { get; set; }

        public List<GP12ListadoItemViewModel> Solicitudes { get; set; } =
            new();
    }


    public class GP12ListadoItemViewModel
    {
        public int SolicitudGP12ID { get; set; }

        public string Origen { get; set; } = string.Empty;

        public string? OrdenFabricacion { get; set; }
        public string? ClienteNombre { get; set; }
        public string? NumeroParte { get; set; }
        public string? DescripcionParte { get; set; }

        public decimal CantidadSolicitada { get; set; }
        public decimal CantidadRecibida { get; set; }
        public decimal CantidadProcesada { get; set; }
        public decimal CantidadPendiente { get; set; }

        public int EstatusID { get; set; }
        public string EstatusCodigo { get; set; } = string.Empty;
        public string EstatusNombre { get; set; } = string.Empty;

        public DateTime FechaSolicitud { get; set; }
        public DateTime? FechaRecepcion { get; set; }

        public string? Motivo { get; set; }

        public decimal PorcentajeProcesado =>
            CantidadRecibida <= 0
                ? 0
                : Math.Round(
                    Math.Min(
                        100m,
                        CantidadProcesada * 100m / CantidadRecibida),
                    1);
    }


    // ============================================================
    // VIEWMODEL - DETALLE
    // ============================================================

    public class GP12DetalleViewModel
    {
        public int SolicitudGP12ID { get; set; }

        public string Origen { get; set; } = string.Empty;

        public int? ProgramaProduccionID { get; set; }
        public int? EjecucionProduccionID { get; set; }
        public int? CalidadInspeccionID { get; set; }

        public int? SolicitudProduccionID { get; set; }
        public int? SolicitudProduccionDetalleID { get; set; }

        public string? OrdenFabricacion { get; set; }

        public int? ClienteID { get; set; }
        public string? ClienteNombre { get; set; }

        public int? ParteID { get; set; }
        public string? NumeroParte { get; set; }
        public string? DescripcionParte { get; set; }

        public int? MaterialID { get; set; }
        public string? MaterialCodigo { get; set; }
        public string? MaterialDescripcion { get; set; }

        public decimal CantidadSolicitada { get; set; }
        public decimal CantidadRecibida { get; set; }
        public decimal CantidadProcesada { get; set; }
        public decimal CantidadPendiente { get; set; }

        public string? Motivo { get; set; }
        public string? InstruccionTrabajo { get; set; }
        public string? CodigoHIP { get; set; }
        public string? CodigoHOE { get; set; }
        public string? Observaciones { get; set; }

        public int EstatusID { get; set; }
        public string EstatusCodigo { get; set; } = string.Empty;
        public string EstatusNombre { get; set; } = string.Empty;

        public DateTime FechaSolicitud { get; set; }
        public DateTime? FechaRecepcion { get; set; }
        public DateTime? FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }
        public DateTime? FechaCierre { get; set; }

        public List<GP12SolicitudEtiquetaItemViewModel>
            Etiquetas { get; set; } = new();

        public List<GP12InventarioMovimientoItemViewModel>
            InventarioMovimientos { get; set; } = new();

        public List<GP12ProgramacionItemViewModel>
            Programaciones { get; set; } = new();

        public List<GP12AsignacionItemViewModel>
            Asignaciones { get; set; } = new();

        public List<GP12InspeccionItemViewModel>
            Inspecciones { get; set; } = new();

        public List<GP12CajaItemViewModel>
            Cajas { get; set; } = new();

        public List<GP12HistorialItemViewModel>
            Historial { get; set; } = new();

        public List<GP12PersonaItemViewModel>
            PersonalDisponible { get; set; } = new();

        public List<GP12CatalogoDefectoItemViewModel>
            CatalogoDefectos { get; set; } = new();

        public bool TieneRecepcion =>
            FechaRecepcion.HasValue &&
            CantidadRecibida > 0;

        public bool TieneProgramacionActiva =>
            Programaciones.Any(x => x.Activo);

        public bool TieneAsignacionesActivas =>
            Asignaciones.Any(x => x.Activo);

        // Permite recepciones parciales incluso si la solicitud
        // ya avanzó a Programado o Asignado. Para las solicitudes
        // nuevas se valida por clasificación de etiqueta.
        public bool PuedeRecibir =>
            !GP12Estatus.EsFinal(EstatusID) &&
            (
                Etiquetas.Any(x =>
                    x.Activo &&
                    x.CantidadRecibida < x.CantidadSolicitada)
                ||
                (!Etiquetas.Any() &&
                 CantidadRecibida < CantidadSolicitada)
            );

        public bool PuedeProgramar =>
            !GP12Estatus.EsFinal(EstatusID) &&
            Etiquetas.Any(e =>
                e.Activo &&
                e.CantidadRecibida >
                Programaciones
                    .Where(p =>
                        p.Activo &&
                        p.SolicitudEtiquetaID == e.SolicitudEtiquetaID)
                    .Sum(p => p.CantidadProgramada));

        public bool PuedeAsignar =>
            Programaciones.Any(x => x.Activo) &&
            EstatusID != GP12Estatus.Cerrado &&
            EstatusID != GP12Estatus.Cancelado;

        public decimal SaldoInventario =>
            InventarioMovimientos
                .Where(x => x.Activo)
                .Sum(x => x.EsEntrada
                    ? x.Cantidad
                    : -x.Cantidad);
    }


    // ============================================================
    // CREAR GP12 DESDE UNA OF EXISTENTE
    // ============================================================

    public class GP12CrearViewModel
    {
        [Required(ErrorMessage = "Selecciona una Orden de Fabricación.")]
        [Range(
            1,
            int.MaxValue,
            ErrorMessage = "Selecciona una Orden de Fabricación válida.")]
        public int? SolicitudProduccionID { get; set; }

        [Required(ErrorMessage = "Selecciona el renglón o producto de la OF.")]
        [Range(
            1,
            int.MaxValue,
            ErrorMessage = "Selecciona un renglón válido.")]
        public int? SolicitudProduccionDetalleID { get; set; }

        [Range(
            typeof(decimal),
            "0",
            "999999999",
            ErrorMessage = "La cantidad amarilla no puede ser negativa.")]
        public decimal CantidadAmarilla { get; set; }

        [Range(
            typeof(decimal),
            "0",
            "999999999",
            ErrorMessage = "La cantidad roja no puede ser negativa.")]
        public decimal CantidadRoja { get; set; }

        public decimal CantidadSolicitada =>
            CantidadAmarilla + CantidadRoja;

        [Required(ErrorMessage = "Captura el motivo de ingreso a GP12.")]
        [StringLength(2000)]
        public string Motivo { get; set; } = string.Empty;

        [StringLength(500)]
        public string? InstruccionTrabajo { get; set; }

        [StringLength(200)]
        public string? CodigoHIP { get; set; }

        [StringLength(200)]
        public string? CodigoHOE { get; set; }

        [StringLength(4000)]
        public string? Observaciones { get; set; }

        public List<GP12OFSelectorItemViewModel>
            OrdenesFabricacion { get; set; } = new();
    }


    public class GP12OFSelectorItemViewModel
    {
        public int SolicitudProduccionID { get; set; }

        public string? FolioSolicitud { get; set; }
        public string? NumeroOFRecibida { get; set; }

        public string? ClienteNombre { get; set; }

        public DateTime FechaSolicitud { get; set; }
        public DateTime? FechaRequerida { get; set; }

        public int TotalRenglones { get; set; }
        public decimal TotalPiezas { get; set; }

        public string NumeroOF =>
            !string.IsNullOrWhiteSpace(NumeroOFRecibida)
                ? NumeroOFRecibida!
                : !string.IsNullOrWhiteSpace(FolioSolicitud)
                    ? FolioSolicitud!
                    : $"OF #{SolicitudProduccionID}";

        public string Texto
        {
            get
            {
                var cliente =
                    string.IsNullOrWhiteSpace(ClienteNombre)
                        ? "Sin cliente"
                        : ClienteNombre;

                return
                    $"{NumeroOF} · {cliente} · {TotalRenglones} renglón(es)";
            }
        }
    }


    // ============================================================
    // FORMULARIO MANUAL LEGACY
    // Se conserva por compatibilidad con helpers del controller
    // actual. Ya no es la pantalla principal de Crear.
    // ============================================================

    public class GP12SolicitudManualViewModel
    {
        [StringLength(100)]
        public string? OrdenFabricacion { get; set; }

        public int? ClienteID { get; set; }

        [StringLength(250)]
        public string? ClienteNombre { get; set; }

        public int? ParteID { get; set; }

        [Required]
        [StringLength(150)]
        public string NumeroParte { get; set; } = string.Empty;

        [StringLength(500)]
        public string? DescripcionParte { get; set; }

        public int? MaterialID { get; set; }

        [StringLength(150)]
        public string? MaterialCodigo { get; set; }

        [StringLength(500)]
        public string? MaterialDescripcion { get; set; }

        [Range(
            typeof(decimal),
            "0.0001",
            "999999999",
            ErrorMessage = "La cantidad solicitada debe ser mayor a cero.")]
        public decimal CantidadSolicitada { get; set; }

        [Required]
        [StringLength(1000)]
        public string Motivo { get; set; } = string.Empty;

        [StringLength(250)]
        public string? InstruccionTrabajo { get; set; }

        [StringLength(100)]
        public string? CodigoHIP { get; set; }

        [StringLength(100)]
        public string? CodigoHOE { get; set; }

        [StringLength(2000)]
        public string? Observaciones { get; set; }
    }


    // ============================================================
    // FORMULARIOS - FLUJO OPERATIVO
    // ============================================================

    public class GP12RecepcionViewModel
    {
        [Range(1, int.MaxValue)]
        public int SolicitudGP12ID { get; set; }

        [Range(
            typeof(decimal),
            "0",
            "999999999",
            ErrorMessage = "La cantidad amarilla no puede ser negativa.")]
        public decimal CantidadAmarilla { get; set; }

        [Range(
            typeof(decimal),
            "0",
            "999999999",
            ErrorMessage = "La cantidad roja no puede ser negativa.")]
        public decimal CantidadRoja { get; set; }

        // Solo se usa para registros migrados que fueron creados
        // antes de distinguir AMARILLA / ROJA.
        [Range(
            typeof(decimal),
            "0",
            "999999999",
            ErrorMessage = "La cantidad sin clasificar no puede ser negativa.")]
        public decimal CantidadSinClasificar { get; set; }

        [StringLength(250)]
        public string? Referencia { get; set; }

        [StringLength(1000)]
        public string? Observaciones { get; set; }

        public decimal CantidadTotal =>
            CantidadAmarilla +
            CantidadRoja +
            CantidadSinClasificar;
    }


    public class GP12ProgramacionGuardarViewModel
    {
        [Range(1, int.MaxValue)]
        public int SolicitudGP12ID { get; set; }

        [Range(
            1,
            int.MaxValue,
            ErrorMessage = "Selecciona la clasificación del material.")]
        public int SolicitudEtiquetaID { get; set; }

        [Required]
        public DateTime FechaProgramada { get; set; }

        public TimeSpan? HoraInicioProgramada { get; set; }
        public TimeSpan? HoraFinProgramada { get; set; }

        [Range(1, 5)]
        public int Prioridad { get; set; } = 3;

        [Range(
            typeof(decimal),
            "0.0001",
            "999999999",
            ErrorMessage = "La cantidad programada debe ser mayor a cero.")]
        public decimal CantidadProgramada { get; set; }

        [StringLength(1000)]
        public string? Observaciones { get; set; }
    }


    public class GP12AsignacionGuardarViewModel
    {
        [Range(1, int.MaxValue)]
        public int SolicitudGP12ID { get; set; }

        [Range(1, int.MaxValue)]
        public int ProgramacionGP12ID { get; set; }

        [Range(1, int.MaxValue)]
        public int PersonaID { get; set; }

        [Range(
            typeof(decimal),
            "0.0001",
            "999999999",
            ErrorMessage = "La cantidad asignada debe ser mayor a cero.")]
        public decimal CantidadAsignada { get; set; }

        [StringLength(1000)]
        public string? Observaciones { get; set; }
    }


    // ============================================================
    // ITEMS PARA DETALLE
    // ============================================================

    public class GP12SolicitudEtiquetaItemViewModel
    {
        public int SolicitudEtiquetaID { get; set; }
        public int SolicitudGP12ID { get; set; }

        public string TipoEtiqueta { get; set; } = string.Empty;

        public decimal CantidadSolicitada { get; set; }
        public decimal CantidadRecibida { get; set; }
        public decimal CantidadProcesada { get; set; }

        public bool Activo { get; set; }

        public decimal FaltaRecibir =>
            Math.Max(0, CantidadSolicitada - CantidadRecibida);

        public decimal PendienteProcesar =>
            Math.Max(0, CantidadRecibida - CantidadProcesada);

        public string TipoEtiquetaNombre =>
            GP12TipoEtiqueta.Nombre(TipoEtiqueta);

        public bool EsAmarilla =>
            TipoEtiqueta == GP12TipoEtiqueta.Amarilla;

        public bool EsRoja =>
            TipoEtiqueta == GP12TipoEtiqueta.Roja;

        public bool EsSinClasificar =>
            TipoEtiqueta == GP12TipoEtiqueta.SinClasificar;
    }


    public class GP12InventarioMovimientoItemViewModel
    {
        public int MovimientoID { get; set; }
        public int SolicitudGP12ID { get; set; }

        public int? SolicitudEtiquetaID { get; set; }
        public string TipoEtiqueta { get; set; } =
            GP12TipoEtiqueta.SinClasificar;

        public string TipoMovimiento { get; set; } = string.Empty;
        public decimal Cantidad { get; set; }

        public int? CajaID { get; set; }
        public int? TarimaID { get; set; }

        public string? Referencia { get; set; }
        public string? Observaciones { get; set; }

        public DateTime FechaMovimiento { get; set; }
        public int? UsuarioID { get; set; }

        public bool Activo { get; set; }

        public bool EsEntrada =>
            TipoMovimiento == GP12TipoMovimiento.Entrada ||
            TipoMovimiento == GP12TipoMovimiento.AjusteEntrada;

        public bool EsSalida =>
            TipoMovimiento == GP12TipoMovimiento.Salida ||
            TipoMovimiento == GP12TipoMovimiento.AjusteSalida;

        public string TipoTexto => TipoMovimiento switch
        {
            GP12TipoMovimiento.Entrada => "Entrada",
            GP12TipoMovimiento.Salida => "Salida",
            GP12TipoMovimiento.AjusteEntrada => "Ajuste de entrada",
            GP12TipoMovimiento.AjusteSalida => "Ajuste de salida",
            _ => TipoMovimiento
        };
    }


    public class GP12ProgramacionItemViewModel
    {
        public int ProgramacionGP12ID { get; set; }
        public int SolicitudGP12ID { get; set; }

        public int? SolicitudEtiquetaID { get; set; }
        public string TipoEtiqueta { get; set; } =
            GP12TipoEtiqueta.SinClasificar;

        public DateTime FechaProgramada { get; set; }

        public TimeSpan? HoraInicioProgramada { get; set; }
        public TimeSpan? HoraFinProgramada { get; set; }

        public int Prioridad { get; set; }

        public decimal CantidadProgramada { get; set; }

        public string? Observaciones { get; set; }

        public int? UsuarioProgramacionID { get; set; }

        public DateTime FechaCreacion { get; set; }

        public bool Activo { get; set; }

        public string HorarioTexto =>
            HoraInicioProgramada.HasValue &&
            HoraFinProgramada.HasValue
                ? $"{HoraInicioProgramada.Value:hh\\:mm} - " +
                  $"{HoraFinProgramada.Value:hh\\:mm}"
                : HoraInicioProgramada.HasValue
                    ? $"Desde {HoraInicioProgramada.Value:hh\\:mm}"
                    : "Sin horario definido";
    }


    public class GP12AsignacionItemViewModel
    {
        public int AsignacionGP12ID { get; set; }

        public int ProgramacionGP12ID { get; set; }
        public int SolicitudGP12ID { get; set; }

        public string TipoEtiqueta { get; set; } =
            GP12TipoEtiqueta.SinClasificar;

        public int PersonaID { get; set; }
        public string PersonaNombre { get; set; } = string.Empty;

        public decimal CantidadAsignada { get; set; }

        public DateTime FechaAsignacion { get; set; }

        public DateTime? FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }

        public bool Cumplida { get; set; }

        public string? Observaciones { get; set; }

        public bool Activo { get; set; }

        public string EstadoTexto =>
            Cumplida
                ? "Cumplida"
                : FechaInicio.HasValue
                    ? "En proceso"
                    : "Pendiente";
    }


    public class GP12InspeccionItemViewModel
    {
        public int InspeccionGP12ID { get; set; }

        public int SolicitudGP12ID { get; set; }
        public int AsignacionGP12ID { get; set; }

        public int PersonaInspectorID { get; set; }
        public string InspectorNombre { get; set; } = string.Empty;

        public DateTime? FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }

        public decimal CantidadRevisada { get; set; }
        public decimal CantidadOK { get; set; }
        public decimal CantidadNOK { get; set; }
        public decimal CantidadRetrabajada { get; set; }
        public decimal CantidadScrap { get; set; }

        public bool ValidacionEtiqueta { get; set; }
        public bool DocumentacionColocada { get; set; }
        public bool RutaInspeccionValidada { get; set; }
        public bool CantidadBasculaValidada { get; set; }
        public bool EtiquetaInspeccionColocada { get; set; }

        public bool Activo { get; set; }

        public bool Terminada => FechaFin.HasValue;
    }


    public class GP12CajaItemViewModel
    {
        public int CajaID { get; set; }
        public int SolicitudGP12ID { get; set; }

        public int? InspeccionGP12ID { get; set; }

        public string FolioCaja { get; set; } = string.Empty;

        public string? NumeroEtiqueta { get; set; }
        public string? NumeroOperador { get; set; }

        public decimal CantidadPiezas { get; set; }

        public bool CantidadBasculaValidada { get; set; }
        public bool EtiquetaInspeccionColocada { get; set; }

        public string Estado { get; set; } = GP12EstadoCaja.Recibida;

        public DateTime? FechaSalida { get; set; }

        public bool Activo { get; set; }
    }


    public class GP12HistorialItemViewModel
    {
        public int HistorialGP12ID { get; set; }
        public int SolicitudGP12ID { get; set; }

        public string Movimiento { get; set; } = string.Empty;

        public int? EstatusAnteriorID { get; set; }
        public string? EstatusAnteriorNombre { get; set; }

        public int? EstatusNuevoID { get; set; }
        public string? EstatusNuevoNombre { get; set; }

        public string? Entidad { get; set; }
        public int? EntidadID { get; set; }

        public string? Comentario { get; set; }

        public int? UsuarioID { get; set; }

        public DateTime FechaMovimiento { get; set; }
    }


    public class GP12PersonaItemViewModel
    {
        public int PersonaID { get; set; }

        public string Nombre { get; set; } = string.Empty;

        public string? Puesto { get; set; }

        public string Texto =>
            string.IsNullOrWhiteSpace(Puesto)
                ? Nombre
                : $"{Nombre} - {Puesto}";
    }


    public class GP12CatalogoDefectoItemViewModel
    {
        public int DefectoID { get; set; }

        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;

        public int Orden { get; set; }

        public string Texto => $"{Codigo} - {Nombre}";
    }


    // ============================================================
    // CATÁLOGOS Y CONSTANTES
    // ============================================================

    public static class GP12Estatus
    {
        public const int Recibido = 1;
        public const int PendienteProgramar = 2;
        public const int Programado = 3;
        public const int Asignado = 4;
        public const int EnInspeccion = 5;
        public const int InspeccionPausada = 6;
        public const int InspeccionTerminada = 7;
        public const int EnTarima = 8;
        public const int SalidaRegistrada = 9;
        public const int Cerrado = 10;
        public const int Cancelado = 90;

        public static bool EsFinal(int estatusID)
        {
            return estatusID == Cerrado ||
                   estatusID == Cancelado;
        }

        public static string Codigo(int estatusID)
        {
            return estatusID switch
            {
                Recibido => "RECIBIDO",
                PendienteProgramar => "PENDIENTE_PROGRAMAR",
                Programado => "PROGRAMADO",
                Asignado => "ASIGNADO",
                EnInspeccion => "EN_INSPECCION",
                InspeccionPausada => "INSPECCION_PAUSADA",
                InspeccionTerminada => "INSPECCION_TERMINADA",
                EnTarima => "EN_TARIMA",
                SalidaRegistrada => "SALIDA_REGISTRADA",
                Cerrado => "CERRADO",
                Cancelado => "CANCELADO",
                _ => "DESCONOCIDO"
            };
        }

        public static string Nombre(int estatusID)
        {
            return estatusID switch
            {
                Recibido => "Recibido",
                PendienteProgramar => "Pendiente de programar",
                Programado => "Programado",
                Asignado => "Asignado",
                EnInspeccion => "En inspección",
                InspeccionPausada => "Inspección pausada",
                InspeccionTerminada => "Inspección terminada",
                EnTarima => "En tarima",
                SalidaRegistrada => "Salida registrada",
                Cerrado => "Cerrado",
                Cancelado => "Cancelado",
                _ => "Desconocido"
            };
        }
    }


    public static class GP12Origen
    {
        public const string Planeacion = "PLANEACION";
        public const string Produccion = "PRODUCCION";
        public const string Calidad = "CALIDAD";
        public const string Manual = "MANUAL";

        public static bool EsValido(string? origen)
        {
            return origen == Planeacion ||
                   origen == Produccion ||
                   origen == Calidad ||
                   origen == Manual;
        }

        public static string Nombre(string? origen)
        {
            return origen switch
            {
                Planeacion => "Planeación",
                Produccion => "Producción",
                Calidad => "Calidad",
                Manual => "Manual",
                _ => "Sin origen"
            };
        }
    }


    public static class GP12TipoEtiqueta
    {
        public const string Amarilla = "AMARILLA";
        public const string Roja = "ROJA";
        public const string SinClasificar = "SIN_CLASIFICAR";

        public static bool EsValida(string? tipo)
        {
            return tipo == Amarilla ||
                   tipo == Roja ||
                   tipo == SinClasificar;
        }

        public static string Nombre(string? tipo)
        {
            return tipo switch
            {
                Amarilla => "Amarilla",
                Roja => "Roja",
                SinClasificar => "Sin clasificar",
                _ => "Sin clasificar"
            };
        }
    }


    public static class GP12TipoMovimiento
    {
        public const string Entrada = "ENTRADA";
        public const string Salida = "SALIDA";
        public const string AjusteEntrada = "AJUSTE_ENTRADA";
        public const string AjusteSalida = "AJUSTE_SALIDA";
    }


    public static class GP12EstadoCaja
    {
        public const string Recibida = "RECIBIDA";
        public const string EnInspeccion = "EN_INSPECCION";
        public const string Revisada = "REVISADA";
        public const string EnTarima = "EN_TARIMA";
        public const string Salida = "SALIDA";
        public const string Cancelada = "CANCELADA";
    }


    public static class GP12EstadoTarima
    {
        public const string Abierta = "ABIERTA";
        public const string Cerrada = "CERRADA";
        public const string Salida = "SALIDA";
        public const string Cancelada = "CANCELADA";
    }


    public static class GP12EntidadHistorial
    {
        public const string Solicitud = "SOLICITUD";
        public const string Programacion = "PROGRAMACION";
        public const string Asignacion = "ASIGNACION";
        public const string Inspeccion = "INSPECCION";
        public const string Caja = "CAJA";
        public const string Tarima = "TARIMA";
        public const string CierreTurno = "CIERRE_TURNO";
    }


    public static class GP12Movimientos
    {
        public const string SolicitudCreada =
            "SOLICITUD_CREADA";

        public const string MaterialRecibido =
            "MATERIAL_RECIBIDO";

        public const string ProgramacionCreada =
            "PROGRAMACION_CREADA";

        public const string TrabajoAsignado =
            "TRABAJO_ASIGNADO";

        public const string InspeccionIniciada =
            "INSPECCION_INICIADA";

        public const string InspeccionPausada =
            "INSPECCION_PAUSADA";

        public const string InspeccionReanudada =
            "INSPECCION_REANUDADA";

        public const string InspeccionTerminada =
            "INSPECCION_TERMINADA";

        public const string CajaRegistrada =
            "CAJA_REGISTRADA";

        public const string CajaEtiquetada =
            "CAJA_ETIQUETADA";

        public const string CajaEnTarima =
            "CAJA_EN_TARIMA";

        public const string TarimaCerrada =
            "TARIMA_CERRADA";

        public const string SalidaRegistrada =
            "SALIDA_REGISTRADA";

        public const string AsignacionCumplida =
            "ASIGNACION_CUMPLIDA";

        public const string CierreTurno =
            "CIERRE_TURNO";

        public const string SolicitudCerrada =
            "SOLICITUD_CERRADA";

        public const string SolicitudCancelada =
            "SOLICITUD_CANCELADA";
    }
}
