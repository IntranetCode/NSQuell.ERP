using System.Globalization;

namespace ERP.NSQuell.Models;

public sealed class ProduccionPersonalSemanalIndexVm
{
    public DateTime SemanaInicio { get; set; }
    public DateTime SemanaFin => SemanaInicio.AddDays(6);
    public int NumeroSemana => ISOWeek.GetWeekOfYear(SemanaInicio);
    public int AnioSemana => ISOWeek.GetYear(SemanaInicio);

    public bool Configurado { get; set; }
    public bool SugerenciasAplicadas { get; set; }

    public int? EscalaID { get; set; }
    public string EscalaFolio { get; set; } = string.Empty;
    public string EscalaEstado { get; set; } = string.Empty;
    public DateTime? EscalaFechaInicio { get; set; }
    public DateTime? EscalaFechaFin { get; set; }

    public List<ProduccionPersonalTurnoApoyoVm> TurnosApoyo { get; set; } = new();
    public List<ProduccionPersonalPersonaOpcionVm> Tecnicos { get; set; } = new();
    public List<ProduccionPersonalPersonaOpcionVm> SmedYTecnicos { get; set; } = new();
    public List<ProduccionPersonalPersonaOpcionVm> Auxiliares { get; set; } = new();
    public List<ProduccionPersonalProgramaOperadorVm> Programas { get; set; } = new();

    public int TotalProgramas => Programas.Count;
    public int TotalAsignados => Programas.Count(x => x.OperadorID.HasValue);
    public int TotalSinOperador => Programas.Count(x => !x.OperadorID.HasValue);
    public int TotalSinMatriz => Programas.Count(x => !x.TieneMatriz);
    public int TotalAlertas => Programas.Count(x => x.TieneAlerta);
    public int TotalConflictos => Programas.Count(x => x.TieneConflictoHorario);
    public int TotalSinTecnico => Programas.Count(x => !TieneCoberturaRol(x, "TECNICO"));
    public int TotalSinSmed => Programas.Count(x => !TieneCoberturaRol(x, "SMED"));
    public int TotalSinAuxiliar => Programas.Count(x => !TieneCoberturaRol(x, "AUXILIAR"));
    public int TotalCoberturaCompleta => Programas.Count(TieneCoberturaCompleta);

    public IEnumerable<ProduccionPersonalTurnoApoyoVm> TurnosOperador =>
        TurnosApoyo
            .Where(x => !EsTurno12x12(x))
            .Where(x => x.HoraInicio.HasValue && x.HoraFin.HasValue)
            .OrderBy(x => x.Orden)
            .ThenBy(x => x.HoraInicio);

    public bool TieneCoberturaCompleta(ProduccionPersonalProgramaOperadorVm programa)
    {
        var operadorOk = programa.OperadorID.HasValue &&
                         !programa.TieneConflictoHorario &&
                         (!programa.TieneMatriz || programa.NivelOperador.HasValue);

        return operadorOk &&
               TieneCoberturaRol(programa, "TECNICO") &&
               TieneCoberturaRol(programa, "SMED") &&
               TieneCoberturaRol(programa, "AUXILIAR");
    }

    public bool TieneCoberturaRol(
        ProduccionPersonalProgramaOperadorVm programa,
        string rol)
    {
        return TurnosApoyo.Any(turno =>
            TurnoCubreMomento(turno, programa.Inicio) &&
            rol switch
            {
                "TECNICO" => turno.TecnicoProduccionID.HasValue,
                "SMED" => turno.SmedID.HasValue,
                "AUXILIAR" => turno.AuxiliarID.HasValue,
                _ => false
            });
    }

    public ProduccionPersonalTurnoApoyoVm? TurnoOperadorPara(DateTime momento)
    {
        return TurnosOperador.FirstOrDefault(x => TurnoCubreMomento(x, momento));
    }

    public string TurnoOperadorNombre(DateTime momento) =>
        TurnoOperadorPara(momento)?.Nombre ?? "Sin turno";

    public int? TurnoOperadorID(DateTime momento) =>
        TurnoOperadorPara(momento)?.TurnoID;

    public static bool EsTurno12x12(ProduccionPersonalTurnoApoyoVm turno)
    {
        var tipo = (turno.TipoTurno ?? string.Empty).ToUpperInvariant();
        var nombre = (turno.Nombre ?? string.Empty).ToUpperInvariant();
        return tipo.Contains("12") || nombre.Contains("12X12") || nombre.Contains("12 X 12");
    }

    public static bool TurnoCubreMomento(
        ProduccionPersonalTurnoApoyoVm turno,
        DateTime momento)
    {
        if (!turno.HoraInicio.HasValue || !turno.HoraFin.HasValue)
            return false;

        var hora = momento.TimeOfDay;
        var inicio = turno.HoraInicio.Value;
        var fin = turno.HoraFin.Value;

        if (turno.CruzaDiaSiguiente || fin <= inicio)
            return hora >= inicio || hora < fin;

        return hora >= inicio && hora < fin;
    }
}

public sealed class ProduccionPersonalTurnoApoyoVm
{
    public int TurnoID { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string TipoTurno { get; set; } = string.Empty;
    public string Color { get; set; } = "#64748B";
    public TimeSpan? HoraInicio { get; set; }
    public TimeSpan? HoraFin { get; set; }
    public bool CruzaDiaSiguiente { get; set; }
    public int Orden { get; set; }

    public int? TecnicoProduccionID { get; set; }
    public int? SmedID { get; set; }
    public int? AuxiliarID { get; set; }
    public string Fuente { get; set; } = "SIN_CONFIGURAR";

    public string HorarioTexto =>
        HoraInicio.HasValue && HoraFin.HasValue
            ? $"{HoraInicio.Value:hh\\:mm} - {HoraFin.Value:hh\\:mm}" +
              (CruzaDiaSiguiente ? " (+1)" : string.Empty)
            : "Flexible";

    public bool TienePersonal =>
        TecnicoProduccionID.HasValue || SmedID.HasValue || AuxiliarID.HasValue;
}

public sealed class ProduccionPersonalPersonaOpcionVm
{
    public int PersonaID { get; set; }
    public string NumeroControl { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Puesto { get; set; } = string.Empty;
    public string TipoOpcion { get; set; } = string.Empty;
}

public sealed class ProduccionPersonalProgramaOperadorVm
{
    public int ProgramaProduccionID { get; set; }
    public int? SolicitudProduccionID { get; set; }
    public string OF { get; set; } = string.Empty;

    public int? ParteID { get; set; }
    public string NumeroParte { get; set; } = string.Empty;
    public string DescripcionParte { get; set; } = string.Empty;

    public int MaquinaID { get; set; }
    public string MaquinaCodigo { get; set; } = string.Empty;
    public string MaquinaNombre { get; set; } = string.Empty;

    public DateTime Inicio { get; set; }
    public DateTime Fin { get; set; }
    public decimal DuracionHoras =>
        Convert.ToDecimal(Math.Max(0, (Fin - Inicio).TotalHours));

    public int? OperadorID { get; set; }
    public string OperadorNombre { get; set; } = string.Empty;
    public int? NivelOperador { get; set; }
    public bool OperadorEnEscala { get; set; }
    public bool OperadorMismaMaquinaEscala { get; set; }
    public bool FueSugerido { get; set; }
    public bool ExcepcionActiva { get; set; }

    public bool TieneMatriz { get; set; }
    public bool TieneConflictoHorario { get; set; }
    public List<ProduccionPersonalOperadorCandidatoVm> Candidatos { get; set; } = new();

    public bool TieneAlerta =>
        !OperadorID.HasValue ||
        TieneConflictoHorario ||
        (TieneMatriz && !NivelOperador.HasValue);
}

public sealed class ProduccionPersonalOperadorCandidatoVm
{
    public int PersonaID { get; set; }
    public string NumeroControl { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public int? Nivel { get; set; }
    public bool EnEscala { get; set; }
    public bool MismaMaquinaEscala { get; set; }
    public string TurnoEscala { get; set; } = string.Empty;

    public string Etiqueta
    {
        get
        {
            var nivel = Nivel.HasValue ? $"N{Nivel.Value} · " : string.Empty;
            var escala = EnEscala
                ? (MismaMaquinaEscala ? " · en turno / misma máquina" : " · en turno")
                : " · fuera de turno";
            return $"{nivel}{Nombre}{escala}";
        }
    }
}

public sealed class ProduccionPersonalSemanaGuardarVm
{
    public DateTime SemanaInicio { get; set; }
    public List<ProduccionPersonalTurnoGuardarVm> Coberturas { get; set; } = new();
    public List<ProduccionPersonalOperadorProgramaGuardarVm> Operadores { get; set; } = new();
}

public sealed class ProduccionPersonalTurnoGuardarVm
{
    public int TurnoID { get; set; }
    public int? TecnicoProduccionID { get; set; }
    public int? SmedID { get; set; }
    public int? AuxiliarID { get; set; }
}

public sealed class ProduccionPersonalOperadorProgramaGuardarVm
{
    public int ProgramaProduccionID { get; set; }
    public int? OperadorID { get; set; }
}

public sealed class ProduccionPersonalCambioOperadorRequest
{
    public int ProgramaProduccionID { get; set; }
    public int OperadorSustitutoID { get; set; }
    public string Alcance { get; set; } = "SOLO_OF";
    public string Motivo { get; set; } = string.Empty;
    public string Justificacion { get; set; } = string.Empty;
}
