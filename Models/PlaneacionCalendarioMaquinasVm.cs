using System.Globalization;

namespace ERP.NSQuell.Models;

public sealed class PlaneacionCalendarioMaquinasVm
{
    /*
     * Nuevas propiedades para zoom del calendario:
     * dia / semana / mes / rango
     */
    public string Vista { get; set; } = "semana";

    public DateTime InicioPeriodo { get; set; }
    public DateTime FinPeriodo { get; set; }

    public DateTime? FechaReferencia { get; set; }
    public DateTime? RangoInicio { get; set; }
    public DateTime? RangoFin { get; set; }

    /*
     * Compatibilidad con la vista/controlador anterior.
     * No las quitamos todavía para no romper nada.
     */
    public DateTime InicioSemana
    {
        get => InicioPeriodo;
        set => InicioPeriodo = value;
    }

    public DateTime FinSemana
    {
        get => FinPeriodo;
        set => FinPeriodo = value;
    }

    public DateTime Ahora { get; set; } = DateTime.Now;

    public List<PlaneacionCalendarioMaquinaVm> Maquinas { get; set; } = new();

    public int TotalMaquinas => Maquinas.Count;

    public int TotalBloques => Maquinas.Sum(x => x.Bloques.Count);

    public int TrabajandoAhora => Maquinas.Count(x =>
        x.Bloques.Any(b => b.EstaEnLinea));

    public string VistaNormalizada =>
        string.IsNullOrWhiteSpace(Vista)
            ? "semana"
            : Vista.Trim().ToLowerInvariant();

    public bool EsVistaDia => VistaNormalizada == "dia";

    public bool EsVistaSemana => VistaNormalizada == "semana";

    public bool EsVistaMes => VistaNormalizada == "mes";

    public bool EsVistaRango => VistaNormalizada == "rango";

    public int TotalDiasPeriodo
    {
        get
        {
            if (FinPeriodo <= InicioPeriodo)
                return 1;

            return Math.Max(
                1,
                (int)Math.Ceiling((FinPeriodo.Date - InicioPeriodo.Date).TotalDays)
            );
        }
    }

    public DateTime PeriodoAnterior
    {
        get
        {
            if (EsVistaDia)
                return InicioPeriodo.AddDays(-1);

            if (EsVistaMes)
                return InicioPeriodo.AddMonths(-1);

            if (EsVistaRango)
                return InicioPeriodo.AddDays(-TotalDiasPeriodo);

            return InicioPeriodo.AddDays(-7);
        }
    }

    public DateTime PeriodoSiguiente
    {
        get
        {
            if (EsVistaDia)
                return InicioPeriodo.AddDays(1);

            if (EsVistaMes)
                return InicioPeriodo.AddMonths(1);

            if (EsVistaRango)
                return InicioPeriodo.AddDays(TotalDiasPeriodo);

            return InicioPeriodo.AddDays(7);
        }
    }

    public string TituloPeriodo
    {
        get
        {
            var cultura = new CultureInfo("es-MX");

            if (EsVistaDia)
                return $"Día {InicioPeriodo:dd/MM/yyyy}";

            if (EsVistaMes)
                return $"Mes {InicioPeriodo.ToString("MMMM yyyy", cultura)}";

            if (EsVistaRango)
                return $"Rango {InicioPeriodo:dd/MM/yyyy} al {FinPeriodo.AddDays(-1):dd/MM/yyyy}";

            return $"Semana {InicioPeriodo:dd/MM/yyyy} al {FinPeriodo.AddDays(-1):dd/MM/yyyy}";
        }
    }

    public string TextoVista
    {
        get
        {
            if (EsVistaDia)
                return "Día";

            if (EsVistaMes)
                return "Mes";

            if (EsVistaRango)
                return "Rango";

            return "Semana";
        }
    }
}

public sealed class PlaneacionCalendarioMaquinaVm
{
    public int MaquinaID { get; set; }

    public string Codigo { get; set; } = string.Empty;

    public string Nombre { get; set; } = string.Empty;

    public int Carriles { get; set; } = 1;

    public List<PlaneacionCalendarioBloqueVm> Bloques { get; set; } = new();
}

public sealed class PlaneacionCalendarioBloqueVm
{
    public int ProgramaProduccionID { get; set; }

    public int? SolicitudProduccionID { get; set; }

    public int MaquinaID { get; set; }

    public string MaquinaCodigo { get; set; } = string.Empty;

    public string? ClienteNombre { get; set; }

    public string? NumeroParte { get; set; }

    public string? ReferenciaSAP { get; set; }

    public string? Descripcion { get; set; }

    public string? MoldeCodigo { get; set; }

    public int CantidadProgramada { get; set; }

    public int CantidadProducida { get; set; }

    public int CantidadPendiente =>
        Math.Max(0, CantidadProgramada - CantidadProducida);

    public DateTime Inicio { get; set; }

    public DateTime Fin { get; set; }

    public decimal HorasProgramadas { get; set; }

    public TimeSpan? Cambio { get; set; }

    public TimeSpan? Arranque { get; set; }

    public int EstatusID { get; set; }

    public int Carril { get; set; }

    public bool EstaEnLinea { get; set; }

    public int? MaquinaPrincipalID { get; set; }

    public string? MaquinaPrincipalCodigo { get; set; }

    public string? MaquinaPrincipalNombre { get; set; }

    public int? MaquinaSustitutaID { get; set; }

    public string? MaquinaSustitutaCodigo { get; set; }

    public string? MaquinaSustitutaNombre { get; set; }

    public bool YaProducido =>
        CantidadProgramada > 0 &&
        CantidadProducida >= CantidadProgramada;

    public bool EstaTerminadoOCerrado =>
        EstatusID == PlaneacionProgramaEstatus.Terminado ||
        EstatusID == PlaneacionProgramaEstatus.Cerrado ||
        EstatusID == 99;

    public bool EstaProduciendo =>
        EstaEnLinea ||
        EstatusID == PlaneacionProgramaEstatus.EnProduccion;

    public bool PuedeMoverCalendario =>
        !EstaProduciendo &&
        !YaProducido &&
        !EstaTerminadoOCerrado;

    public string ClaseSemaforoCalendario
    {
        get
        {
            if (EstatusID == 99 || EstatusID == PlaneacionProgramaEstatus.Cerrado)
                return "bloque-cerrado";

            if (YaProducido || EstatusID == PlaneacionProgramaEstatus.Terminado)
                return "bloque-producido";

            if (EstaProduciendo)
                return "bloque-produciendo";

            return "bloque-timeline";
        }
    }

    public string MaquinaSugeridaTexto
    {
        get
        {
            var principal = string.IsNullOrWhiteSpace(MaquinaPrincipalCodigo)
                ? "Sin máquina principal"
                : MaquinaPrincipalCodigo;

            var sustituta = string.IsNullOrWhiteSpace(MaquinaSustitutaCodigo)
                ? "Sin máquina sustituta"
                : MaquinaSustitutaCodigo;

            return $"Principal: {principal} | Sustituta: {sustituta}";
        }
    }

    public string OFTexto =>
        SolicitudProduccionID.HasValue
            ? $"OF {SolicitudProduccionID.Value}"
            : $"Programa {ProgramaProduccionID}";

    public string ParteTexto =>
        !string.IsNullOrWhiteSpace(ReferenciaSAP)
            ? ReferenciaSAP!
            : !string.IsNullOrWhiteSpace(NumeroParte)
                ? NumeroParte!
                : "Sin parte";

    public string EstatusTexto
    {
        get
        {
            if (YaProducido)
                return "Ya producido";

            if (EstaEnLinea)
                return "En línea";

            return EstatusID switch
            {
                1 => "Programado",
                2 => "En preparación",
                3 => "En producción",
                4 => "Pausado",
                5 => "Terminado",
                9 => "Cerrado",
                99 => "Cancelado",
                _ => $"Estatus {EstatusID}"
            };
        }
    }

    public string EstadoSemaforoTexto
    {
        get
        {
            if (EstatusID == 99 || EstatusID == PlaneacionProgramaEstatus.Cerrado)
                return "Cerrado / cancelado";

            if (YaProducido || EstatusID == PlaneacionProgramaEstatus.Terminado)
                return "Ya producido";

            if (EstaProduciendo)
                return "Produciendo";

            return "Timeline";
        }
    }

    public string MotivoBloqueoMovimiento
    {
        get
        {
            if (EstaProduciendo)
                return "Este programa está en línea o producción. No puede moverse hasta que esté listo el módulo de Producción.";

            if (YaProducido)
                return "Este programa ya fue producido. No puede moverse desde el calendario.";

            if (EstaTerminadoOCerrado)
                return "Este programa está terminado, cerrado o cancelado. No puede moverse.";

            return string.Empty;
        }
    }

    /*
     * Se deja ClaseEstado por compatibilidad con vistas anteriores.
     * La vista nueva debe usar ClaseSemaforoCalendario.
     */
    public string ClaseEstado => EstatusID switch
    {
        2 => "bloque-preparacion",
        3 => "bloque-produccion",
        4 => "bloque-pausado",
        5 => "bloque-terminado",
        9 => "bloque-terminado",
        99 => "bloque-terminado",
        _ => "bloque-programado"
    };
}