using System.ComponentModel.DataAnnotations;

namespace ERP.NSQuell.Models.ViewModels.Logistica;

public sealed class LogisticaListaCargaIndexVm
{
    public int? ListaCargaSemanaID { get; set; }
    public int Anio { get; set; }
    public int NumeroSemana { get; set; }
    public DateTime FechaInicio { get; set; }
    public DateTime FechaFin { get; set; }
    public string EstatusSemana { get; set; } = "Abierta";
    public string? ObservacionesSemana { get; set; }
    public string? Busqueda { get; set; }
    public int? ClienteID { get; set; }
    public string? Criticidad { get; set; }
    public List<LogisticaListaCargaFilaVm> Filas { get; set; } = new();
    public List<LogisticaListaCargaSalidaVm> Salidas { get; set; } = new();
    public List<LogisticaListaCargaClienteVm> Clientes { get; set; } = new();
    public int TotalClientes => Filas.Select(x => x.ClienteID).Where(x => x.HasValue).Distinct().Count();
    public int TotalPartidas => Filas.Count;
    public int TotalExpeditados => Filas.Count(x => x.EsExpeditado);
    public int TotalSalidas => Salidas.Count;
    public int TotalSalidasExpeditadas => Salidas.Count(x => x.EsExpeditada);
    public long TotalSemana => Filas.Sum(x => (long)x.TotalSemana);
    public long TotalAtraso => Filas.Sum(x => (long)x.Atraso);
    public long TotalEnviado => Filas.Sum(x => (long)x.Enviado);
    public long TotalPendiente => Filas.Sum(x => (long)x.Pendiente);
    public string SemanaTexto => $"W{NumeroSemana:00} {Anio}";
    public string RangoTexto => $"{FechaInicio:dd/MM/yyyy} - {FechaFin:dd/MM/yyyy}";
    public bool SemanaAbierta => string.Equals(EstatusSemana, "Abierta", StringComparison.OrdinalIgnoreCase);
}

public sealed class LogisticaListaCargaFilaVm
{
    public int? ClienteID { get; set; }
    public string Cliente { get; set; } = string.Empty;
    public int? ParteID { get; set; }
    public string Referencia { get; set; } = string.Empty;
    public string Designacion { get; set; } = string.Empty;
    public DateTime InicioSemana { get; set; }
    public DateTime FinSemana { get; set; }
    public int TotalSemana { get; set; }
    public int Atraso { get; set; }
    public int Lunes { get; set; }
    public int Martes { get; set; }
    public int Miercoles { get; set; }
    public int Jueves { get; set; }
    public int Viernes { get; set; }
    public int Sabado { get; set; }
    public int CantidadProgramadaLogistica { get; set; }
    public int PendienteProgramar { get; set; }
    public long CajasPTDisponibles { get; set; }
    public long PiezasPTDisponibles { get; set; }
    public int Enviado { get; set; }
    public string Ubicacion { get; set; } = string.Empty;
    public string? Observaciones { get; set; }

    public decimal PiezasAlmacen { get; set; }
    public decimal PiezasGP12 { get; set; }
    public decimal PiezasProduccion { get; set; }
    public decimal PiezasLocalizadas { get; set; }

    public bool TieneMaterialAlmacen => PiezasAlmacen > 0;
    public bool TieneMaterialGP12 => PiezasGP12 > 0;
    public bool TieneMaterialProduccion => PiezasProduccion > 0;
    public bool TieneMaterialLocalizado => PiezasLocalizadas > 0;
    public int TotalDias => Lunes + Martes + Miercoles + Jueves + Viernes + Sabado;
    public int Pendiente => Math.Max(0, TotalSemana + Atraso - Enviado);
    public bool EsExpeditado => Atraso > 0;
    public bool TieneDisponiblePT => PiezasPTDisponibles > 0 || CajasPTDisponibles > 0;
    public decimal PorcentajeEnviado
    {
        get
        {
            var total = TotalSemana + Atraso;
            if (total <= 0) return 0;
            return Math.Round(Math.Min(100m, (decimal)Enviado * 100m / total), 2);
        }
    }
    public string Criticidad => EsExpeditado ? "Expeditado" : "Programado";
    public string CriticidadClase => EsExpeditado ? "danger" : "primary";
    public string UbicacionTexto => string.IsNullOrWhiteSpace(Ubicacion) ? "Sin identificar" : Ubicacion;
}

public sealed class LogisticaListaCargaSalidaVm
{
    public int ViajeID { get; set; }
    public string Folio { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public string LugarEnvio { get; set; } = string.Empty;
    public string Chofer { get; set; } = string.Empty;
    public TimeSpan? HoraSalida { get; set; }
    public TimeSpan? HoraRegreso { get; set; }
    public string TipoSalida { get; set; } = string.Empty;
    public string Criticidad { get; set; } = string.Empty;
    public string TipoUnidad { get; set; } = string.Empty;
    public string Unidad { get; set; } = string.Empty;
    public string Estatus { get; set; } = string.Empty;
    public bool EsExpeditada => string.Equals(Criticidad, "Expeditado", StringComparison.OrdinalIgnoreCase);
    public bool EnCurso => string.Equals(Estatus, "En curso", StringComparison.OrdinalIgnoreCase);
    public bool Completada => string.Equals(Estatus, "Completado", StringComparison.OrdinalIgnoreCase);
    public string HoraSalidaTexto => HoraSalida.HasValue ? HoraSalida.Value.ToString(@"hh\:mm") : "-";
    public string HoraRegresoTexto => HoraRegreso.HasValue ? HoraRegreso.Value.ToString(@"hh\:mm") : "-";
}

public sealed class LogisticaListaCargaClienteVm
{
    public int ClienteID { get; set; }
    public string Cliente { get; set; } = string.Empty;
}

public sealed class LogisticaListaCargaSemanaVm
{
    public int ListaCargaSemanaID { get; set; }

    [Range(2020, 2100, ErrorMessage = "El año no es válido.")]
    public int Anio { get; set; }

    [Range(1, 53, ErrorMessage = "La semana debe estar entre 1 y 53.")]
    [Display(Name = "Semana")]
    public int NumeroSemana { get; set; }

    [Required(ErrorMessage = "La fecha inicial es obligatoria.")]
    [DataType(DataType.Date)]
    [Display(Name = "Fecha inicial")]
    public DateTime FechaInicio { get; set; }

    [Required(ErrorMessage = "La fecha final es obligatoria.")]
    [DataType(DataType.Date)]
    [Display(Name = "Fecha final")]
    public DateTime FechaFin { get; set; }

    [StringLength(30)]
    public string Estatus { get; set; } = "Abierta";

    [StringLength(1000, ErrorMessage = "Las observaciones no pueden exceder 1000 caracteres.")]
    public string? Observaciones { get; set; }

    public bool Activo { get; set; } = true;
    public string SemanaTexto => $"W{NumeroSemana:00} {Anio}";
    public string RangoTexto => $"{FechaInicio:dd/MM/yyyy} - {FechaFin:dd/MM/yyyy}";
}

public sealed class LogisticaListaCargaAjusteVm
{
    public int ListaCargaAjusteID { get; set; }

    [Required]
    public int ListaCargaSemanaID { get; set; }

    public int? ReleaseDetalleID { get; set; }
    public int? ClienteID { get; set; }
    public int? ParteID { get; set; }

    [StringLength(100, ErrorMessage = "La ubicación no puede exceder 100 caracteres.")]
    [Display(Name = "Ubicación")]
    public string? UbicacionManual { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "La cantidad de atraso no puede ser negativa.")]
    [Display(Name = "Atraso")]
    public int? CantidadAtrasoManual { get; set; }

    [StringLength(1000, ErrorMessage = "Las observaciones no pueden exceder 1000 caracteres.")]
    public string? Observaciones { get; set; }
}

public sealed class LogisticaListaCargaFiltroVm
{
    public int? Anio { get; set; }
    public int? NumeroSemana { get; set; }
    public int? ClienteID { get; set; }
    public string? Busqueda { get; set; }
    public string? Criticidad { get; set; }
}

public sealed class LogisticaListaCargaResumenClienteVm
{
    public int? ClienteID { get; set; }
    public string Cliente { get; set; } = string.Empty;
    public int Partidas { get; set; }
    public int TotalSemana { get; set; }
    public int Atraso { get; set; }
    public int Enviado { get; set; }
    public int Pendiente => Math.Max(0, TotalSemana + Atraso - Enviado);
    public bool TieneExpeditado => Atraso > 0;
}