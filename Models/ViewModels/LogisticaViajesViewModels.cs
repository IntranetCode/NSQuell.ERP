using System.ComponentModel.DataAnnotations;

namespace ERP.NSQuell.Models.ViewModels.Logistica;

public sealed class LogisticaViajesIndexVm
{
    public string? Busqueda { get; set; }
    public string? Estatus { get; set; }
    public string? TipoViaje { get; set; }
    public string? TipoTransporte { get; set; }
    public DateTime? FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }

    public int TotalViajes { get; set; }
    public int Programados { get; set; }
    public int EnCurso { get; set; }
    public int Completados { get; set; }
    public int Cancelados { get; set; }
    public int ViajesHoy { get; set; }
    public int RetornosPendientes { get; set; }

    public List<LogisticaViajeResumenVm> Viajes { get; set; } = new();
}

public sealed class LogisticaViajeResumenVm
{
    public int ViajeID { get; set; }
    public string Folio { get; set; } = string.Empty;
    public string TipoViaje { get; set; } = string.Empty;
    public string TipoTransporte { get; set; } = string.Empty;
    public string Origen { get; set; } = string.Empty;
    public string Destino { get; set; } = string.Empty;
    public string Motivo { get; set; } = string.Empty;
    public DateTime FechaProgramada { get; set; }
    public TimeSpan? HoraSalidaProgramada { get; set; }
    public DateTime? FechaSalidaReal { get; set; }
    public DateTime? FechaRegresoReal { get; set; }
    public string Unidad { get; set; } = string.Empty;
    public string Operador { get; set; } = string.Empty;
    public string Estatus { get; set; } = string.Empty;
    public bool TieneIncidencia { get; set; }

    public bool RetornoPendiente =>
        Estatus == "En curso"
        && !FechaRegresoReal.HasValue;

    public string FechaHoraProgramadaTexto =>
        HoraSalidaProgramada.HasValue
            ? $"{FechaProgramada:dd/MM/yyyy} {HoraSalidaProgramada.Value:hh\\:mm}"
            : FechaProgramada.ToString("dd/MM/yyyy");
}

public sealed class LogisticaViajeCrearVm
{
    [Required]
    [StringLength(50)]
    [Display(Name = "Tipo de viaje")]
    public string TipoViaje { get; set; } = string.Empty;

    [Required]
    [StringLength(30)]
    [Display(Name = "Tipo de transporte")]
    public string TipoTransporte { get; set; } = "Interno";

    [Required]
    [StringLength(300)]
    public string Origen { get; set; } = string.Empty;

    [Required]
    [StringLength(300)]
    public string Destino { get; set; } = string.Empty;

    [Required]
    [StringLength(500)]
    public string Motivo { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Date)]
    [Display(Name = "Fecha programada")]
    public DateTime FechaProgramada { get; set; } = DateTime.Today;

    [Display(Name = "Hora de salida programada")]
    public TimeSpan? HoraSalidaProgramada { get; set; }

    [Display(Name = "Hora estimada de regreso")]
    public TimeSpan? HoraRegresoProgramada { get; set; }

    public int? RutaID { get; set; }

    public int? UnidadID { get; set; }

    [Display(Name = "Operador / chofer")]
    public int? OperadorUsuarioID { get; set; }

    [StringLength(200)]
    public string? OperadorTexto { get; set; }

    [StringLength(200)]
    [Display(Name = "Transportista externo")]
    public string? TransportistaExterno { get; set; }

    [StringLength(100)]
    [Display(Name = "Unidad externa")]
    public string? UnidadExterna { get; set; }

    [StringLength(100)]
    [Display(Name = "Placas externas")]
    public string? PlacasExternas { get; set; }

    [StringLength(200)]
    [Display(Name = "Chofer externo")]
    public string? ChoferExterno { get; set; }

    [StringLength(1200)]
    public string? Observaciones { get; set; }

    public List<LogisticaViajeSelectVm> Rutas { get; set; } = new();
    public List<LogisticaViajeSelectVm> Unidades { get; set; } = new();
    public List<LogisticaViajeSelectVm> Operadores { get; set; } = new();
}

public sealed class LogisticaViajeEditarVm
{
    [Required]
    public int ViajeID { get; set; }

    [Required]
    [StringLength(50)]
    public string TipoViaje { get; set; } = string.Empty;

    [Required]
    [StringLength(30)]
    public string TipoTransporte { get; set; } = "Interno";

    [Required]
    [StringLength(300)]
    public string Origen { get; set; } = string.Empty;

    [Required]
    [StringLength(300)]
    public string Destino { get; set; } = string.Empty;

    [Required]
    [StringLength(500)]
    public string Motivo { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Date)]
    public DateTime FechaProgramada { get; set; }

    public TimeSpan? HoraSalidaProgramada { get; set; }
    public TimeSpan? HoraRegresoProgramada { get; set; }

    public int? RutaID { get; set; }
    public int? UnidadID { get; set; }

    public int? OperadorUsuarioID { get; set; }

    [StringLength(200)]
    public string? OperadorTexto { get; set; }

    [StringLength(200)]
    public string? TransportistaExterno { get; set; }

    [StringLength(100)]
    public string? UnidadExterna { get; set; }

    [StringLength(100)]
    public string? PlacasExternas { get; set; }

    [StringLength(200)]
    public string? ChoferExterno { get; set; }

    [StringLength(1200)]
    public string? Observaciones { get; set; }

    public List<LogisticaViajeSelectVm> Rutas { get; set; } = new();
    public List<LogisticaViajeSelectVm> Unidades { get; set; } = new();
    public List<LogisticaViajeSelectVm> Operadores { get; set; } = new();
}

public sealed class LogisticaViajeDetalleVm
{
    public int ViajeID { get; set; }
    public string Folio { get; set; } = string.Empty;
    public string TipoViaje { get; set; } = string.Empty;
    public string TipoTransporte { get; set; } = string.Empty;

    public string Origen { get; set; } = string.Empty;
    public string Destino { get; set; } = string.Empty;
    public string Motivo { get; set; } = string.Empty;

    public DateTime FechaProgramada { get; set; }
    public TimeSpan? HoraSalidaProgramada { get; set; }
    public TimeSpan? HoraRegresoProgramada { get; set; }

    public DateTime? FechaSalidaReal { get; set; }
    public DateTime? FechaRegresoReal { get; set; }

    public int? RutaID { get; set; }
    public string Ruta { get; set; } = string.Empty;

    public int? UnidadID { get; set; }
    public string Unidad { get; set; } = string.Empty;

    public int? OperadorUsuarioID { get; set; }
    public string Operador { get; set; } = string.Empty;

    public string TransportistaExterno { get; set; } = string.Empty;
    public string UnidadExterna { get; set; } = string.Empty;
    public string PlacasExternas { get; set; } = string.Empty;
    public string ChoferExterno { get; set; } = string.Empty;

    public string Estatus { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;

    public bool TieneIncidencia { get; set; }

    public int? KilometrajeSalida { get; set; }
    public int? KilometrajeRegreso { get; set; }

    public int? UsuarioResponsableID { get; set; }
    public string UsuarioResponsable { get; set; } = string.Empty;

    public DateTime FechaCreacion { get; set; }
    public string CreadoPor { get; set; } = string.Empty;

    public decimal? PagoGasolina { get; set; }
    public int? KilometrosUtilizados => KilometrajeSalida.HasValue && KilometrajeRegreso.HasValue && KilometrajeRegreso.Value >= KilometrajeSalida.Value ? KilometrajeRegreso.Value - KilometrajeSalida.Value : null;
    public List<LogisticaViajeEvidenciaVm> Evidencias { get; set; } = new();

    public List<LogisticaViajeHistorialVm> Historial { get; set; } = new();
    public List<LogisticaViajeIncidenciaVm> Incidencias { get; set; } = new();

    public bool EsTransporteInterno =>
        string.Equals(
            TipoTransporte,
            "Interno",
            StringComparison.OrdinalIgnoreCase);

    public bool EsTransporteExterno =>
        string.Equals(
            TipoTransporte,
            "Externo",
            StringComparison.OrdinalIgnoreCase);

    public bool PuedeEditar =>
        Estatus is "Programado";

    public bool PuedeRegistrarSalida =>
        Estatus == "Programado";

    public bool PuedeRegistrarRegreso =>
        Estatus == "En curso"
        && FechaSalidaReal.HasValue
        && !FechaRegresoReal.HasValue;

    public bool PuedeCancelar =>
        Estatus == "Programado";

    public bool EstaCerrado =>
        Estatus is "Completado" or "Cancelado";

    public bool RetornoPendiente =>
        Estatus == "En curso"
        && !FechaRegresoReal.HasValue;

    public string RecursoTransporte
    {
        get
        {
            if (EsTransporteInterno)
                return string.IsNullOrWhiteSpace(Unidad)
                    ? "Sin unidad"
                    : Unidad;

            if (!string.IsNullOrWhiteSpace(UnidadExterna))
                return UnidadExterna;

            return "Transporte externo";
        }
    }

    public string OperadorMostrar
    {
        get
        {
            if (EsTransporteInterno)
                return string.IsNullOrWhiteSpace(Operador)
                    ? "Sin operador"
                    : Operador;

            return string.IsNullOrWhiteSpace(ChoferExterno)
                ? "Sin chofer"
                : ChoferExterno;
        }
    }
}

// LOGISTICA_VIAJES_CHOFERES_V5
public sealed class LogisticaChoferesVm
{
    public List<LogisticaChoferEstadoVm> Choferes { get; set; } = new();
    public int Disponibles => Choferes.Count(x => x.Estado == "Disponible");
    public int EnViaje => Choferes.Count(x => x.Estado == "En viaje");
    public int Programados => Choferes.Count(x => x.Estado == "Programado");
}

public sealed class LogisticaChoferEstadoVm
{
    public int UsuarioID { get; set; }
    public string Chofer { get; set; } = string.Empty;
    public string Estado { get; set; } = "Disponible";
    public string FuenteActual { get; set; } = string.Empty;
    public string FolioActual { get; set; } = string.Empty;
    public string DestinoActual { get; set; } = string.Empty;
    public DateTime? SalidaActual { get; set; }
    public string FuenteProxima { get; set; } = string.Empty;
    public string FolioProximo { get; set; } = string.Empty;
    public string DestinoProximo { get; set; } = string.Empty;
    public DateTime? ProximaSalida { get; set; }
}

public sealed class LogisticaViajeEvidenciaVm
{
    public int ViajeEvidenciaID { get; set; }
    public int ViajeID { get; set; }
    public string TipoEvidencia { get; set; } = string.Empty;
    public string NombreOriginal { get; set; } = string.Empty;
    public string NombreFisico { get; set; } = string.Empty;
    public string RutaRelativa { get; set; } = string.Empty;
    public string TipoContenido { get; set; } = string.Empty;
    public long TamanoBytes { get; set; }
    public string Observaciones { get; set; } = string.Empty;
    public int? UsuarioCargaID { get; set; }
    public string UsuarioCargaNombre { get; set; } = string.Empty;
    public DateTime FechaCarga { get; set; }
}

public sealed class LogisticaViajeSalidaVm
{
    [Required]
    public int ViajeID { get; set; }

    [Required]
    [Display(Name = "Fecha y hora de salida")]
    public DateTime FechaSalida { get; set; } = DateTime.Now;

    [Range(0, int.MaxValue)]
    [Display(Name = "Kilometraje de salida")]
    public int? KilometrajeSalida { get; set; }

    [StringLength(1000)]
    public string? Observaciones { get; set; }
}

public sealed class LogisticaViajeRetornoVm
{
    [Required]
    public int ViajeID { get; set; }

    [Required]
    [Display(Name = "Fecha y hora de regreso")]
    public DateTime FechaRegreso { get; set; } = DateTime.Now;

    [Range(0, int.MaxValue)]
    [Display(Name = "Kilometraje de regreso")]
    public int? KilometrajeRegreso { get; set; }

    [StringLength(1000)]
    public string? Observaciones { get; set; }

    [Range(0, 999999999.99)]
    [Display(Name = "Pago de gasolina")]
    public decimal? PagoGasolina { get; set; }
}

public sealed class LogisticaViajeCancelarVm
{
    [Required]
    public int ViajeID { get; set; }

    [Required]
    [StringLength(1000)]
    public string Motivo { get; set; } = string.Empty;
}

public sealed class LogisticaViajeHistorialVm
{
    public int HistorialID { get; set; }
    public int ViajeID { get; set; }
    public string Evento { get; set; } = string.Empty;
    public string EstadoAnterior { get; set; } = string.Empty;
    public string EstadoNuevo { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;
    public int? UsuarioID { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public DateTime FechaEvento { get; set; }
}

public sealed class LogisticaViajeIncidenciaVm
{
    public int ViajeIncidenciaID { get; set; }
    public int ViajeID { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public string Severidad { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Estatus { get; set; } = string.Empty;
    public string Responsable { get; set; } = string.Empty;
    public DateTime FechaRegistro { get; set; }
    public DateTime? FechaCierre { get; set; }

    public bool EstaAbierta =>
        Estatus is "Abierta" or "En seguimiento";

    public bool EsCritica =>
        EstaAbierta
        && string.Equals(
            Severidad,
            "Crítica",
            StringComparison.OrdinalIgnoreCase);
}

public sealed class LogisticaViajeIncidenciaCrearVm
{
    [Required]
    public int ViajeID { get; set; }

    [Required]
    [StringLength(80)]
    public string Tipo { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string Severidad { get; set; } = "Media";

    [Required]
    [StringLength(1200)]
    public string Descripcion { get; set; } = string.Empty;

    [StringLength(200)]
    public string? Responsable { get; set; }
}

public sealed class LogisticaViajeIncidenciaCerrarVm
{
    [Required]
    public int ViajeIncidenciaID { get; set; }

    [Required]
    public int ViajeID { get; set; }

    [Required]
    [StringLength(1200)]
    public string Solucion { get; set; } = string.Empty;
}

public sealed class LogisticaViajeSelectVm
{
    public int Id { get; set; }
    public string Texto { get; set; } = string.Empty;
}