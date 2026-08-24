using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ERP.NSQuell.Models;

// NSQ_PRODUCCION_PERSONAL_MODELS_V31
public sealed class ProduccionPersonalIndexVm
{
    public DateTime FechaDesde { get; set; }
    public DateTime FechaHasta { get; set; }
    public bool TablaConfigurada { get; set; }
    public List<ProduccionPersonalTurnoVm> Turnos { get; set; } = new();
    public List<ProduccionPersonalProgramaVm> Programas { get; set; } = new();

    public int TotalProgramas => Programas.Count;
    public int TotalAsignaciones => Programas.Sum(x => x.Asignaciones.Count);
    public int AsignacionesCompletas => Programas.Sum(x => x.Asignaciones.Count(a => a.EstaCompleta));
    public int AsignacionesPendientes => Math.Max(0, TotalAsignaciones - AsignacionesCompletas);
}

public sealed class ProduccionPersonalProgramaVm
{
    public int ProgramaProduccionID { get; set; }
    public int? SolicitudProduccionID { get; set; }
    public string FolioOF { get; set; } = string.Empty;
    public int? MaquinaID { get; set; }
    public string MaquinaCodigo { get; set; } = string.Empty;
    public string MaquinaNombre { get; set; } = string.Empty;
    public int? ParteID { get; set; }
    public string NumeroParte { get; set; } = string.Empty;
    public string ReferenciaSAP { get; set; } = string.Empty;
    public string DescripcionParte { get; set; } = string.Empty;
    public string MoldeCodigo { get; set; } = string.Empty;
    public DateTime Inicio { get; set; }
    public DateTime Fin { get; set; }
    public int CantidadProgramada { get; set; }
    public List<ProduccionPersonalAsignacionVm> Asignaciones { get; set; } = new();

    public string ParteVisible =>
        !string.IsNullOrWhiteSpace(ReferenciaSAP)
            ? ReferenciaSAP
            : !string.IsNullOrWhiteSpace(NumeroParte)
                ? NumeroParte
                : $"Parte #{ParteID}";
}

public sealed class ProduccionPersonalAsignacionVm
{
    public int AsignacionPersonalID { get; set; }
    public int ProgramaProduccionID { get; set; }
    public DateTime FechaTrabajo { get; set; }
    public int? TurnoID { get; set; }
    public string TurnoNombre { get; set; } = string.Empty;
    public DateTime Inicio { get; set; }
    public DateTime Fin { get; set; }
    public int? OperadorID { get; set; }
    public string OperadorNombre { get; set; } = string.Empty;
    public int? AuxiliarID { get; set; }
    public string AuxiliarNombre { get; set; } = string.Empty;
    public int? TecnicoProduccionID { get; set; }
    public string TecnicoProduccionNombre { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;

    public bool EstaCompleta =>
        OperadorID.HasValue &&
        AuxiliarID.HasValue &&
        TecnicoProduccionID.HasValue;
}

public sealed class ProduccionPersonalTurnoVm
{
    public int TurnoID { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public TimeSpan? HoraInicio { get; set; }
    public TimeSpan? HoraFin { get; set; }
    public bool CruzaDiaSiguiente { get; set; }
    public bool EsFlexible { get; set; }
    public int Orden { get; set; }

    private string NombreNormalizado =>
        (Nombre ?? string.Empty).Trim().ToUpperInvariant();

    public bool EsMixto =>
        NombreNormalizado.Contains("MIXTO");

    // NSQ_TURNOS_CLAROS_V31
    // RRHH tiene Mixto como flexible/sin horario fijo. Para programación
    // de Producción se utiliza el horario operativo definido: 08:30-18:00.
    public TimeSpan? HoraInicioEfectiva =>
        HoraInicio ?? (EsMixto ? new TimeSpan(8, 30, 0) : null);

    public TimeSpan? HoraFinEfectiva =>
        HoraFin ?? (EsMixto ? new TimeSpan(18, 0, 0) : null);

    public bool CruzaDiaSiguienteEfectivo =>
        CruzaDiaSiguiente ||
        (HoraInicioEfectiva.HasValue &&
         HoraFinEfectiva.HasValue &&
         HoraFinEfectiva.Value <= HoraInicioEfectiva.Value);

    public string HorarioTexto =>
        HoraInicioEfectiva.HasValue && HoraFinEfectiva.HasValue
            ? $"{HoraInicioEfectiva.Value:hh\\:mm} - {HoraFinEfectiva.Value:hh\\:mm}" +
              (CruzaDiaSiguienteEfectivo ? " (+1 día)" : string.Empty)
            : "Horario flexible";

    public string NombreVisible
    {
        get
        {
            var inicio = HoraInicioEfectiva;
            var fin = HoraFinEfectiva;

            if (EsMixto)
                return "Mixto";

            if (inicio == new TimeSpan(7, 0, 0) && fin == new TimeSpan(15, 0, 0))
                return "1er turno";

            if (inicio == new TimeSpan(15, 0, 0) && fin == new TimeSpan(22, 30, 0))
                return "2do turno";

            if (inicio == new TimeSpan(22, 30, 0) && fin == new TimeSpan(7, 0, 0))
                return "3er turno";

            if (inicio == new TimeSpan(7, 0, 0) && fin == new TimeSpan(19, 0, 0))
                return "Turno 12 h día";

            if (inicio == new TimeSpan(19, 0, 0) && fin == new TimeSpan(7, 0, 0))
                return "Turno 12 h noche";

            // Si RRHH nombró el turno únicamente con el rango horario,
            // evitamos repetir "07:00-15:00 · 07:00-15:00".
            var nombre = (Nombre ?? string.Empty).Trim();
            if (Regex.IsMatch(nombre, @"^\\d{1,2}:\\d{2}\\s*-\\s*\\d{1,2}:\\d{2}$"))
                return "Turno";

            return string.IsNullOrWhiteSpace(nombre) ? "Turno" : nombre;
        }
    }

    public string EtiquetaDropdown =>
        $"{NombreVisible} · {HorarioTexto}";

    public bool EsTurnoPrincipal =>
        NombreVisible is "1er turno" or "2do turno" or "3er turno" or "Mixto";
}

public sealed class ProduccionPersonalGuardarVm
{
    public int? AsignacionPersonalID { get; set; }
    public int ProgramaProduccionID { get; set; }
    public DateTime FechaTrabajo { get; set; }
    public int TurnoID { get; set; }
    public int? OperadorID { get; set; }
    public int? AuxiliarID { get; set; }
    public int? TecnicoProduccionID { get; set; }
    public string? Observaciones { get; set; }
}
