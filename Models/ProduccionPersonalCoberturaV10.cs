using System.Globalization;

namespace ERP.NSQuell.Models;

// NSQ_PRODUCCION_PERSONAL_V10_COBERTURA_PERIODOS_HEADER_PEOPLE
public sealed partial class ProduccionPersonalV7IndexVm
{
    public bool CoberturaDiaV10Configurada { get; set; }

    public List<ProduccionPersonalCoberturaPeriodoV10Vm>
        CoberturasSoporteV10 { get; set; } = new();
}

public sealed class ProduccionPersonalCoberturaPeriodoV10Vm
{
    public string Alcance { get; set; } = "SEMANA";

    public DateTime FechaClave { get; set; }

    public DateTime SemanaInicio { get; set; }

    public DateTime FechaInicio { get; set; }

    public DateTime FechaFin { get; set; }

    public int NumeroSemana =>
        ISOWeek.GetWeekOfYear(SemanaInicio);

    public int AnioSemana =>
        ISOWeek.GetYear(SemanaInicio);

    public int? EscalaID { get; set; }

    public string EscalaFolio { get; set; } = string.Empty;

    public string EscalaEstado { get; set; } = string.Empty;

    public int AjustesDiarios { get; set; }

    public bool TieneAjusteDia { get; set; }

    public List<ProduccionPersonalTurnoApoyoVm>
        Turnos { get; set; } = new();
}

public sealed class ProduccionPersonalCoberturaDiaV10PostVm
{
    public DateTime FechaTrabajo { get; set; }

    public List<ProduccionPersonalTurnoGuardarVm>
        Coberturas { get; set; } = new();

    public string Vista { get; set; } = "dia";

    public string Panel { get; set; } = "support";

    public DateTime? FechaDesde { get; set; }

    public DateTime? FechaHasta { get; set; }
}