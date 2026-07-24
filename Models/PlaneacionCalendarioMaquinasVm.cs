namespace ERP.NSQuell.Models;

public sealed class PlaneacionCalendarioMaquinasVm
{
    public DateTime InicioSemana { get; set; }
    public DateTime FinSemana { get; set; }
    public DateTime Ahora { get; set; } = DateTime.Now;
    public List<PlaneacionCalendarioMaquinaVm> Maquinas { get; set; } = new();

    public int TotalMaquinas => Maquinas.Count;
    public int TotalBloques => Maquinas.Sum(x => x.Bloques.Count);
    public int TrabajandoAhora => Maquinas.Count(x =>
        x.Bloques.Any(b => b.Inicio <= Ahora && b.Fin > Ahora));
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
    public int CantidadPendiente => Math.Max(0, CantidadProgramada - CantidadProducida);

    public DateTime Inicio { get; set; }
    public DateTime Fin { get; set; }
    public decimal HorasProgramadas { get; set; }
    public TimeSpan? Cambio { get; set; }
    public TimeSpan? Arranque { get; set; }

    public int EstatusID { get; set; }
    public int Carril { get; set; }

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

    public string EstatusTexto => EstatusID switch
    {
        1 => "Programado",
        2 => "En preparacion",
        3 => "En produccion",
        4 => "Pausado",
        5 => "Terminado",
        9 => "Cerrado",
        99 => "Cancelado",
        _ => $"Estatus {EstatusID}"
    };

    public string ClaseEstado => EstatusID switch
    {
        2 => "bloque-preparacion",
        3 => "bloque-produccion",
        4 => "bloque-pausado",
        5 => "bloque-terminado",
        9 => "bloque-terminado",
        _ => "bloque-programado"
    };
}