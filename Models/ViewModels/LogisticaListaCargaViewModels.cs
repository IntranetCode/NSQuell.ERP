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
    public long TotalPorAtender => Filas.Sum(x => (long)x.TotalPorAtender);
    public long TotalProgramado => Filas.Sum(x => (long)x.TotalProgramado);
    public long TotalSinProgramar => Filas.Sum(x => (long)x.TotalSinProgramar);
    public long TotalGeneradoEmbarques => Filas.Sum(x => (long)x.TotalGeneradoEmbarques);
    public long TotalPendienteGenerar => Filas.Sum(x => (long)x.TotalPendienteGenerar);
    public long TotalEnviado => Filas.Sum(x => (long)x.TotalEnviado);
    public long TotalPendienteEnviar => Filas.Sum(x => (long)x.TotalPendienteEnviar);

    public string SemanaTexto => $"W{NumeroSemana:00} {Anio}";
    public string RangoTexto => $"{FechaInicio:dd/MM/yyyy} - {FechaFin:dd/MM/yyyy}";
    public bool SemanaAbierta => string.Equals(EstatusSemana, "Abierta", StringComparison.OrdinalIgnoreCase);
    public bool TieneProgramacion => Filas.Any(x => x.TotalProgramado > 0);
    public bool TieneProgramacionSinGenerar => Filas.Any(x => x.TotalPendienteGenerar > 0);
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

    // Demanda original de la semana.
    public int TotalSemana { get; set; }

    // Pendiente anterior a la semana actual.
    public int Atraso { get; set; }

    // Demanda original según FechaRequerida del Release.
    public int RequeridoLunes { get; set; }
    public int RequeridoMartes { get; set; }
    public int RequeridoMiercoles { get; set; }
    public int RequeridoJueves { get; set; }
    public int RequeridoViernes { get; set; }
    public int RequeridoSabado { get; set; }

    // Programación real de Lista de carga.
    public int Lunes { get; set; }
    public int Martes { get; set; }
    public int Miercoles { get; set; }
    public int Jueves { get; set; }
    public int Viernes { get; set; }
    public int Sabado { get; set; }

    // Campos provenientes de demanda/Planeación.
    public int CantidadProgramadaLogistica { get; set; }
    public int PendienteProgramar { get; set; }

    // Inventario/localización.
    public long CajasPTDisponibles { get; set; }
    public long PiezasPTDisponibles { get; set; }
    public decimal PiezasAlmacen { get; set; }
    public decimal PiezasGP12 { get; set; }
    public decimal PiezasProduccion { get; set; }
    public decimal PiezasLocalizadas { get; set; }

    public string Ubicacion { get; set; } = string.Empty;
    public string? Observaciones { get; set; }

    // Programación y Releases que forman esta fila consolidada.
    public List<LogisticaListaCargaProgramacionVm> Programaciones { get; set; } = new();
    public List<LogisticaListaCargaReleasePendienteVm> Releases { get; set; } = new();
    public List<LogisticaListaCargaDiaHabitualVm> DiasHabituales { get; set; } = new();

    public bool TieneMaterialAlmacen => PiezasAlmacen > 0;
    public bool TieneMaterialGP12 => PiezasGP12 > 0;
    public bool TieneMaterialProduccion => PiezasProduccion > 0;
    public bool TieneMaterialLocalizado => PiezasLocalizadas > 0;
    public bool TieneDisponiblePT => PiezasPTDisponibles > 0 || CajasPTDisponibles > 0;

    public int TotalRequeridoDias =>
        RequeridoLunes +
        RequeridoMartes +
        RequeridoMiercoles +
        RequeridoJueves +
        RequeridoViernes +
        RequeridoSabado;

    public int TotalDias =>
        Lunes +
        Martes +
        Miercoles +
        Jueves +
        Viernes +
        Sabado;

    // Total de demanda pendiente que queremos atender.
    public int TotalPorAtender => Math.Max(0, TotalSemana + Atraso);

    // Todo lo programado, incluso si la fecha cae en otra semana.
    public int TotalProgramado =>
        Programaciones
            .Where(x => x.Activo && !string.Equals(x.Estatus, "Cancelada", StringComparison.OrdinalIgnoreCase))
            .Sum(x => x.CantidadProgramada);

    public int TotalSinProgramar =>
        Math.Max(0, TotalPorAtender - TotalProgramado);

    public int TotalGeneradoEmbarques =>
        Programaciones
            .Where(x => x.Activo && !string.Equals(x.Estatus, "Cancelada", StringComparison.OrdinalIgnoreCase))
            .Sum(x => x.CantidadGeneradaEmbarque);

    public int TotalPendienteGenerar =>
        Math.Max(0, TotalProgramado - TotalGeneradoEmbarques);

    // "Enviado" significa salida física de PT/planta.
    public int TotalEnviado =>
        Programaciones
            .Where(x => x.Activo && !string.Equals(x.Estatus, "Cancelada", StringComparison.OrdinalIgnoreCase))
            .Sum(x => x.CantidadEnviada);

    public int TotalPendienteEnviar =>
        Math.Max(0, TotalProgramado - TotalEnviado);

    // Se conserva para no romper vistas/controlador actuales mientras migramos.
    public int Enviado
    {
        get => TotalEnviado;
    }

    public int Pendiente => TotalPorAtender;

    public bool EsExpeditado => Atraso > 0;
    public string Criticidad => EsExpeditado ? "Expeditado" : "Programado";
    public string CriticidadClase => EsExpeditado ? "danger" : "primary";

    public decimal PorcentajeProgramado =>
        TotalPorAtender <= 0
            ? 0
            : Math.Round(Math.Min(100m, (decimal)TotalProgramado * 100m / TotalPorAtender), 2);

    public decimal PorcentajeEnviado =>
        TotalPorAtender <= 0
            ? 0
            : Math.Round(Math.Min(100m, (decimal)TotalEnviado * 100m / TotalPorAtender), 2);

    public string UbicacionTexto =>
        string.IsNullOrWhiteSpace(Ubicacion)
            ? "Sin identificar"
            : Ubicacion;

    public string DiasHabitualesTexto =>
        DiasHabituales.Count == 0
            ? "Sin día habitual"
            : string.Join(" · ", DiasHabituales.OrderBy(x => x.DiaSemana).Select(x => x.DiaTexto));

    public bool EsDiaHabitual(DateTime fecha) =>
        DiasHabituales.Any(x => x.DiaSemana == ConvertirDiaSemana(fecha.DayOfWeek));

    private static int ConvertirDiaSemana(DayOfWeek dia) => dia switch
    {
        DayOfWeek.Monday => 1,
        DayOfWeek.Tuesday => 2,
        DayOfWeek.Wednesday => 3,
        DayOfWeek.Thursday => 4,
        DayOfWeek.Friday => 5,
        DayOfWeek.Saturday => 6,
        DayOfWeek.Sunday => 7,
        _ => 0
    };
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
    public List<LogisticaListaCargaDiaHabitualVm> DiasHabituales { get; set; } = new();

    public string DiasHabitualesTexto =>
        DiasHabituales.Count == 0
            ? "Sin día habitual"
            : string.Join(" · ", DiasHabituales.OrderBy(x => x.DiaSemana).Select(x => x.DiaTexto));
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

    public int Programado { get; set; }
    public int GeneradoEmbarques { get; set; }
    public int Enviado { get; set; }

    public int TotalPorAtender =>
        Math.Max(0, TotalSemana + Atraso - Enviado);

    public int SinProgramar =>
        Math.Max(0, TotalPorAtender - Programado);

    public int PendienteGenerar =>
        Math.Max(0, Programado - GeneradoEmbarques);

    public int PendienteEnviar =>
        Math.Max(0, Programado - Enviado);

    public bool TieneExpeditado => Atraso > 0;
}

public sealed class LogisticaListaCargaDiaHabitualVm
{
    public int ClienteDiaCargaID { get; set; }
    public int ClienteID { get; set; }
    public int DiaSemana { get; set; }
    public int Prioridad { get; set; } = 1;
    public DateTime? VigenciaDesde { get; set; }
    public DateTime? VigenciaHasta { get; set; }
    public string? Observaciones { get; set; }
    public bool Activo { get; set; }

    public string DiaTexto => DiaSemana switch
    {
        1 => "Lunes",
        2 => "Martes",
        3 => "Miércoles",
        4 => "Jueves",
        5 => "Viernes",
        6 => "Sábado",
        7 => "Domingo",
        _ => "Sin definir"
    };
}

public sealed class LogisticaListaCargaProgramacionVm
{
    public int ListaCargaProgramacionID { get; set; }
    public int? ListaCargaSemanaOrigenID { get; set; }

    public int ReleaseDetalleID { get; set; }
    public int ClienteID { get; set; }
    public int ParteID { get; set; }

    public DateTime FechaRequeridaOriginal { get; set; }
    public DateTime FechaProgramadaCarga { get; set; }

    public int CantidadProgramada { get; set; }
    public int CantidadGeneradaEmbarque { get; set; }
    public int CantidadEnviada { get; set; }

    public string Estatus { get; set; } = "Programada";
    public string? Observaciones { get; set; }

    public bool EsReprogramacion { get; set; }
    public bool Activo { get; set; }

    public int PendienteGenerar =>
        Math.Max(0, CantidadProgramada - CantidadGeneradaEmbarque);

    public int PendienteEnviar =>
        Math.Max(0, CantidadProgramada - CantidadEnviada);

    public bool Generada =>
        CantidadGeneradaEmbarque >= CantidadProgramada;

    public bool Cumplida =>
        CantidadEnviada >= CantidadProgramada;

    public string FechaProgramadaTexto =>
        FechaProgramadaCarga.ToString("dd/MM/yyyy");
}

public sealed class LogisticaListaCargaReleasePendienteVm
{
    public int ReleaseDetalleID { get; set; }
    public int ReleaseID { get; set; }
    public string FolioRelease { get; set; } = string.Empty;

    public int ClienteID { get; set; }
    public int ParteID { get; set; }

    public string NumeroParte { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string NumeroOF { get; set; } = string.Empty;

    public DateTime FechaRequerida { get; set; }

    public int CantidadRequerida { get; set; }
    public int CantidadPendiente { get; set; }
    public int CantidadYaProgramada { get; set; }

    public int DisponibleProgramar =>
        Math.Max(0, CantidadPendiente - CantidadYaProgramada);

    public bool EsExpeditado { get; set; }

    public string Criticidad =>
        EsExpeditado
            ? "Expeditado"
            : "Programado";
}

public sealed class LogisticaListaCargaProgramarVm
{
    [Required]
    public int ListaCargaSemanaID { get; set; }

    [Required]
    public int ClienteID { get; set; }

    [Required]
    public int ParteID { get; set; }

    public int? Anio { get; set; }
    public int? NumeroSemana { get; set; }

    public List<LogisticaListaCargaProgramarDetalleVm> Programaciones { get; set; } = new();
}

public sealed class LogisticaListaCargaProgramarDetalleVm
{
    [Required(ErrorMessage = "La fecha de carga es obligatoria.")]
    [DataType(DataType.Date)]
    [Display(Name = "Fecha de carga")]
    public DateTime FechaProgramadaCarga { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "La cantidad debe ser mayor a cero.")]
    [Display(Name = "Cantidad")]
    public int Cantidad { get; set; }

    [StringLength(1000, ErrorMessage = "Las observaciones no pueden exceder 1000 caracteres.")]
    public string? Observaciones { get; set; }
}

public sealed class LogisticaListaCargaGenerarEmbarquesVm
{
    [Required]
    public int ListaCargaSemanaID { get; set; }

    public int? Anio { get; set; }
    public int? NumeroSemana { get; set; }

    public List<int> ProgramacionIDs { get; set; } = new();
}

public sealed class LogisticaListaCargaProgramacionEmbarqueVm
{
    public int ListaCargaProgramacionEmbarqueID { get; set; }
    public int ListaCargaProgramacionID { get; set; }

    public int EmbarqueID { get; set; }
    public int EmbarqueDetalleID { get; set; }

    public string FolioEmbarque { get; set; } = string.Empty;

    public int CantidadAsignada { get; set; }
    public int CantidadEnviada { get; set; }

    public string Criticidad { get; set; } = string.Empty;
    public bool Activo { get; set; }

    public int PendienteEnviar =>
        Math.Max(0, CantidadAsignada - CantidadEnviada);
}

public sealed class LogisticaDiasCargaClientesVm
{
    public string? Busqueda { get; set; }
    public List<LogisticaDiaCargaClienteVm> Clientes { get; set; } = new();
    public int TotalClientes => Clientes.Count;
    public int ClientesConfigurados => Clientes.Count(x => x.DiasSeleccionados.Count > 0);
    public int ClientesSinConfigurar => Clientes.Count(x => x.DiasSeleccionados.Count == 0);
}

public sealed class LogisticaDiaCargaClienteVm
{
    public int ClienteID { get; set; }
    public string Cliente { get; set; } = string.Empty;
    public List<int> DiasSeleccionados { get; set; } = new();
    public string DiasTexto
    {
        get
        {
            if (DiasSeleccionados.Count == 0) return "Sin días definidos";
            return string.Join(", ", DiasSeleccionados.OrderBy(x => x).Select(x => x switch
            {
                1 => "Lunes",
                2 => "Martes",
                3 => "Miércoles",
                4 => "Jueves",
                5 => "Viernes",
                6 => "Sábado",
                _ => string.Empty
            }).Where(x => !string.IsNullOrWhiteSpace(x)));
        }
    }
}

public sealed class LogisticaGuardarDiasCargaClienteVm
{
    public int ClienteID { get; set; }
    public List<int> DiasSeleccionados { get; set; } = new();
    public string? Busqueda { get; set; }
}