using System.Globalization;

namespace ERP.NSQuell.Models;

public sealed partial class ProduccionPersonalV7IndexVm
{
    public string Vista { get; set; } = "dia";
    public DateTime InicioPeriodo { get; set; }
    public DateTime FinPeriodo { get; set; }
    public DateTime FechaReferencia { get; set; }
    public DateTime? RangoInicio { get; set; }
    public DateTime? RangoFin { get; set; }
    public DateTime SemanaInicio { get; set; }
    public int NumeroSemana => ISOWeek.GetWeekOfYear(FechaReferencia);
    public int AnioSemana => ISOWeek.GetYear(FechaReferencia);
    public bool Configurado { get; set; }
    public int? EscalaID { get; set; }
    public string EscalaFolio { get; set; } = string.Empty;
    public string EscalaEstado { get; set; } = string.Empty;

    public List<ProduccionPersonalV7SegmentoVm> Segmentos { get; set; } = new();
    public List<ProduccionPersonalTurnoApoyoVm> TurnosApoyo { get; set; } = new();
    public List<ProduccionPersonalPersonaOpcionVm> Operadores { get; set; } = new();
    public List<ProduccionPersonalPersonaOpcionVm> Tecnicos { get; set; } = new();
    public List<ProduccionPersonalPersonaOpcionVm> SmedYTecnicos { get; set; } = new();
    public List<ProduccionPersonalPersonaOpcionVm> Auxiliares { get; set; } = new();

    public DateTime FinPeriodoVisible => FinPeriodo.AddTicks(-1);
    public int TotalSegmentos => Segmentos.Count;
    public int TotalSinOperador => Segmentos.Count(x => !x.OperadorEfectivoID.HasValue);
    public int TotalProduciendo => Segmentos.Count(x => x.ProduccionActiva);
    public int TotalConflictos => Segmentos.Count(x => x.TieneConflicto);
    public int TotalCompletos => Segmentos.Count(x => x.Semaforo == "VERDE");
}

public sealed class ProduccionPersonalV7SegmentoVm
{
    public int? AsignacionPersonalID { get; set; }
    public int ProgramaProduccionID { get; set; }
    public int? SolicitudProduccionID { get; set; }
    public string OF { get; set; } = string.Empty;
    public int? ParteID { get; set; }
    public string NumeroParte { get; set; } = string.Empty;
    public string DescripcionParte { get; set; } = string.Empty;
    public int MaquinaID { get; set; }
    public string MaquinaCodigo { get; set; } = string.Empty;
    public string MaquinaNombre { get; set; } = string.Empty;
    public DateTime InicioPrograma { get; set; }
    public DateTime FinPrograma { get; set; }
    public DateTime FechaTrabajo { get; set; }
    public int TurnoID { get; set; }
    public string TurnoNombre { get; set; } = string.Empty;
    public DateTime Inicio { get; set; }
    public DateTime Fin { get; set; }
    public int? OperadorBaseID { get; set; }
    public string OperadorBaseNombre { get; set; } = string.Empty;
    public int? OperadorAsignadoID { get; set; }
    public string OperadorAsignadoNombre { get; set; } = string.Empty;
    public bool TieneAsignacionEspecifica { get; set; }
    public int? NivelOperador { get; set; }
    public int? EjecucionProduccionID { get; set; }
    public int? EstatusProduccionID { get; set; }
    public bool ProduccionActiva { get; set; }
    public bool TieneConflicto { get; set; }

    public int? OperadorEfectivoID => TieneAsignacionEspecifica ? OperadorAsignadoID : OperadorBaseID;
    public string OperadorEfectivoNombre => TieneAsignacionEspecifica
        ? (string.IsNullOrWhiteSpace(OperadorAsignadoNombre) ? "Sin asignar" : OperadorAsignadoNombre)
        : (string.IsNullOrWhiteSpace(OperadorBaseNombre) ? "Sin asignar" : OperadorBaseNombre);

    public string Semaforo => !OperadorEfectivoID.HasValue
        ? "ROJO"
        : ProduccionActiva
            ? "AMARILLO"
            : "VERDE";
}

public sealed class ProduccionPersonalV7GuardarRequest
{
    public int ProgramaProduccionID { get; set; }
    public int TurnoID { get; set; }
    public DateTime FechaTrabajo { get; set; }
    public int? OperadorID { get; set; }
    public string? Motivo { get; set; }
    public string? Justificacion { get; set; }
    public string Vista { get; set; } = "dia";
    public DateTime? FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }
}

public sealed class ProduccionPersonalV7CoberturaPostVm
{
    public DateTime SemanaInicio { get; set; }
    public List<ProduccionPersonalTurnoGuardarVm> Coberturas { get; set; } = new();
    public string Vista { get; set; } = "dia";
    public DateTime? FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }
}
