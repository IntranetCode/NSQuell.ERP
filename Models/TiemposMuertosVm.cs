using System.Globalization;
namespace ERP.NSQuell.Models;

// NSQ_TIEMPOS_DIRECCION_V1_1. Un intervalo físico no se suma por cada OF o tarea que coincide.
public sealed class TiemposMuertosVm
{
    public DateTime Desde { get; set; }
    public DateTime Hasta { get; set; }
    public DateTime Actualizado { get; set; }
    public string? Departamento { get; set; }
    public string? Categoria { get; set; } = "DEMORA";
    public string? OF { get; set; }
    public bool SoloActivos { get; set; }
    public int DatosSinCierre { get; set; }
    public List<string> Departamentos { get; set; } = new();
    public List<TiempoDepartamentoFila> Filas { get; set; } = new();
    public List<TiempoEpisodioVm> Episodios { get; private set; } = new();
    public List<TiempoFrenteVm> Frentes { get; private set; } = new();
    public List<TiempoAreaVm> Areas { get; private set; } = new();
    public List<TiempoDiaVm> Dias { get; private set; } = new();
    public double HorasEspera { get; private set; }
    public double HorasParo { get; private set; }
    public double HorasNetas => HorasEspera + HorasParo;
    public int FrentesAbiertos => Frentes.Count(x => x.Episodios.Any(e => e.Abierto && e.Categoria != "TRABAJO"));
    public int EpisodiosAbiertos => Episodios.Count(x => x.Abierto && x.Categoria != "TRABAJO");
    public int RegistrosConsolidados => Math.Max(0, Filas.Count - Episodios.Count);
    public bool EsPeriodoActual => Actualizado >= Desde && Actualizado < Hasta.AddDays(1);
    public static string HorasTexto(double valor) => valor.ToString("N1", CultureInfo.GetCultureInfo("es-MX"));
    public static string Duracion(double horas)
    {
        var minutos = Math.Max(0, (long)Math.Round(horas * 60, MidpointRounding.AwayFromZero));
        return $"{minutos / 60:N0} h {minutos % 60:00} min";
    }

    public void PrepararAnalisis()
    {
        Filas = Filas.GroupBy(x => (x.Origen, x.RegistroID, x.ProgramaID, x.Categoria)).Select(x => x.First()).ToList();
        Episodios.Clear(); Frentes.Clear(); Areas.Clear(); Dias.Clear(); HorasEspera = 0; HorasParo = 0;
        foreach (var grupo in Filas.GroupBy(x => (x.Recurso, x.Departamento, x.Fase, x.Categoria)))
        {
            TiempoEpisodioVm? actual = null;
            foreach (var fila in grupo.OrderBy(x => x.InicioPeriodo).ThenBy(x => x.FinPeriodo))
            {
                if (actual == null || fila.InicioPeriodo > actual.FinPeriodo)
                {
                    actual = new TiempoEpisodioVm { Recurso = fila.Recurso, Maquina = fila.Maquina, Departamento = fila.Departamento, Fase = fila.Fase, Categoria = fila.Categoria, InicioPeriodo = fila.InicioPeriodo, FinPeriodo = fila.FinPeriodo };
                    Episodios.Add(actual);
                }
                else if (fila.FinPeriodo > actual.FinPeriodo) actual.FinPeriodo = fila.FinPeriodo;
                actual.Registros.Add(fila);
            }
        }
        foreach (var grupo in Episodios.GroupBy(x => x.Recurso))
        {
            var frente = new TiempoFrenteVm { Recurso = grupo.Key, Maquina = grupo.First().Maquina, Episodios = grupo.OrderByDescending(x => x.Abierto).ThenByDescending(x => x.Horas).ToList() };
            Frentes.Add(frente);
        }
        // Dividir en segmentos: si existe paro real, tiene prioridad sobre espera concurrente.
        // Cuando coinciden departamentos, asignar al grupo compartido y no sumar la misma hora a ambos.
        var areas = new Dictionary<string, TiempoAreaVm>(StringComparer.OrdinalIgnoreCase);
        foreach (var recurso in Filas.Where(x => x.Categoria is "ESPERA" or "PARO").GroupBy(x => x.Recurso))
        {
            var filas = recurso.ToList();
            var limites = filas.SelectMany(x => new[] { x.InicioPeriodo, x.FinPeriodo }).Distinct().OrderBy(x => x).ToList();
            for (var i = 0; i + 1 < limites.Count; i++)
            {
                var inicio = limites[i]; var fin = limites[i + 1];
                var activas = filas.Where(x => x.InicioPeriodo < fin && x.FinPeriodo > inicio).ToList();
                if (activas.Count == 0) continue;
                var esParo = activas.Any(x => x.Categoria == "PARO");
                var responsables = activas.Where(x => x.Categoria == (esParo ? "PARO" : "ESPERA")).Select(x => x.Departamento).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var nombre = responsables.Count == 1 ? responsables[0] : "Áreas simultáneas";
                if (!areas.TryGetValue(nombre, out var area)) { area = new TiempoAreaVm { Nombre = nombre }; areas[nombre] = area; }
                var horas = (fin - inicio).TotalHours;
                if (esParo) { HorasParo += horas; area.HorasParo += horas; } else { HorasEspera += horas; area.HorasEspera += horas; }
                area.Recursos.Add(recurso.Key);
                foreach (var responsable in responsables) area.Responsables.Add(responsable);
            }
        }
        Areas = areas.Values.OrderByDescending(x => x.Horas).ToList();
        for (var dia = Desde.Date; dia <= Hasta.Date; dia = dia.AddDays(1))
        {
            var datos = new TiempoDiaVm { Fecha = dia };
            foreach (var grupo in Filas.Where(x => x.Categoria is "ESPERA" or "PARO").GroupBy(x => x.Recurso))
            {
                datos.Horas += HorasUnidas(grupo.Select(x => (x.InicioPeriodo > dia ? x.InicioPeriodo : dia, x.FinPeriodo < dia.AddDays(1) ? x.FinPeriodo : dia.AddDays(1))));
            }
            Dias.Add(datos);
        }
        Frentes = Frentes.OrderByDescending(x => x.Episodios.Any(e => e.Abierto && e.Categoria != "TRABAJO")).ThenByDescending(x => x.HorasNetas).ThenBy(x => x.Maquina).ToList();
    }
    public static double HorasUnidas(IEnumerable<(DateTime Inicio, DateTime Fin)> intervalos)
    {
        DateTime? inicio = null; DateTime fin = default; double horas = 0;
        foreach (var intervalo in intervalos.Where(x => x.Fin > x.Inicio).OrderBy(x => x.Inicio))
        {
            if (!inicio.HasValue) { inicio = intervalo.Inicio; fin = intervalo.Fin; }
            else if (intervalo.Inicio <= fin) { if (intervalo.Fin > fin) fin = intervalo.Fin; }
            else { horas += (fin - inicio.Value).TotalHours; inicio = intervalo.Inicio; fin = intervalo.Fin; }
        }
        return horas + (inicio.HasValue ? (fin - inicio.Value).TotalHours : 0);
    }
}
public sealed class TiempoDepartamentoFila
{
    public string Origen { get; set; } = "";
    public long RegistroID { get; set; }
    public int ProgramaID { get; set; }
    public int? MaquinaID { get; set; }
    public string Maquina { get; set; } = "";
    public string NumeroOF { get; set; } = "";
    public string NumeroParte { get; set; } = "";
    public string Designacion { get; set; } = "";
    public string Departamento { get; set; } = "";
    public string Categoria { get; set; } = "";
    public string Fase { get; set; } = "";
    public string Tarea { get; set; } = "";
    public string Motivo { get; set; } = "";
    public DateTime Inicio { get; set; }
    public DateTime? Fin { get; set; }
    public DateTime InicioPeriodo { get; set; }
    public DateTime FinPeriodo { get; set; }
    public string Recurso => MaquinaID.HasValue ? "M:" + MaquinaID : "P:" + ProgramaID;
}
public sealed class TiempoEpisodioVm
{
    public string Recurso { get; set; } = "";
    public string Maquina { get; set; } = "";
    public string Departamento { get; set; } = "";
    public string Categoria { get; set; } = "";
    public string Fase { get; set; } = "";
    public DateTime InicioPeriodo { get; set; }
    public DateTime FinPeriodo { get; set; }
    public List<TiempoDepartamentoFila> Registros { get; set; } = new();
    public bool Abierto => Registros.Any(x => !x.Fin.HasValue);
    public double Horas => Math.Max(0, (FinPeriodo - InicioPeriodo).TotalHours);
    public DateTime InicioReal => Registros.Min(x => x.Inicio);
    public DateTime? FinReal => Abierto ? null : Registros.Max(x => x.Fin);
    public IEnumerable<string> Ordenes => Registros.Select(x => x.NumeroOF).Distinct().OrderBy(x => x);
}
public sealed class TiempoFrenteVm
{
    public string Recurso { get; set; } = "";
    public string Maquina { get; set; } = "";
    public List<TiempoEpisodioVm> Episodios { get; set; } = new();
    public IEnumerable<string> Ordenes => Episodios.SelectMany(x => x.Ordenes).Distinct().OrderBy(x => x);
    public double HorasNetas => TiemposMuertosVm.HorasUnidas(Episodios.Where(x => x.Categoria != "TRABAJO").Select(x => (x.InicioPeriodo, x.FinPeriodo)));
    public double HorasParo => TiemposMuertosVm.HorasUnidas(Episodios.Where(x => x.Categoria == "PARO").Select(x => (x.InicioPeriodo, x.FinPeriodo)));
    public double HorasEspera => Math.Max(0, HorasNetas - HorasParo);
    public DateTime? DesdeAbierto => Episodios.Where(x => x.Abierto && x.Categoria != "TRABAJO").Select(x => (DateTime?)x.InicioReal).Min();
}
public sealed class TiempoAreaVm
{
    public string Nombre { get; set; } = "";
    public double HorasEspera { get; set; }
    public double HorasParo { get; set; }
    public double Horas => HorasEspera + HorasParo;
    public HashSet<string> Recursos { get; set; } = new();
    public HashSet<string> Responsables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
public sealed class TiempoDiaVm
{
    public DateTime Fecha { get; set; }
    public double Horas { get; set; }
}
