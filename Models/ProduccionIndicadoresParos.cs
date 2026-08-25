using Microsoft.AspNetCore.Mvc.Rendering;

namespace ERP.NSQuell.Models;

public sealed class ProduccionIndicadoresParosIndexVm
{
    public DateTime Fecha { get; set; } = DateTime.Today;
    public string Periodo { get; set; } = "diario";

    public DateTime FechaDesde { get; set; }
    public DateTime FechaHastaExclusiva { get; set; }

    public int? MaquinaID { get; set; }
    public int? OperadorID { get; set; }
    public string? Motivo { get; set; }
    public string? OF { get; set; }

    public List<SelectListItem> Maquinas { get; set; } = new();
    public List<SelectListItem> Operadores { get; set; } = new();
    public List<SelectListItem> Motivos { get; set; } = new();

    public List<ProduccionIndicadorParoFilaVm> Paros { get; set; } = new();

    public DateTime FechaActualizacion { get; set; } = DateTime.Now;

    public int TotalParos => Paros.Count;

    public int MinutosTotalesDetenidos =>
        Paros.Sum(x => Math.Max(0, x.DuracionMinutos));

    public decimal DuracionPromedioMinutos =>
        Paros.Count == 0
            ? 0m
            : Math.Round(
                Paros.Average(x => (decimal)Math.Max(0, x.DuracionMinutos)),
                1);

    public int MayoresA15Minutos =>
        Paros.Count(x => x.EsMayorA15Minutos);

    public int EnCurso =>
        Paros.Count(x => x.EnCurso);

    public bool TieneParosEnCurso =>
        EnCurso > 0;

    public string PeriodoNormalizado =>
        (Periodo ?? "diario").Trim().ToLowerInvariant();

    public string PeriodoTitulo =>
        PeriodoNormalizado switch
        {
            "semanal" => "Semanal",
            "mensual" => "Mensual",
            _ => "Diario"
        };

    public string RangoTexto
    {
        get
        {
            var ultimoDia = FechaHastaExclusiva.AddDays(-1).Date;

            return PeriodoNormalizado switch
            {
                "semanal" =>
                    $"{FechaDesde:dd/MM/yyyy} al {ultimoDia:dd/MM/yyyy}",

                "mensual" =>
                    FechaDesde.ToString("MMMM yyyy"),

                _ =>
                    FechaDesde.ToString("dd/MM/yyyy")
            };
        }
    }
}

public sealed class ProduccionIndicadorParoFilaVm
{
    public int ParoID { get; set; }
    public int EjecucionProduccionID { get; set; }
    public int ProgramaProduccionID { get; set; }
    public int? SolicitudProduccionID { get; set; }

    public int? MaquinaID { get; set; }
    public string MaquinaCodigo { get; set; } = string.Empty;
    public string MaquinaNombre { get; set; } = string.Empty;

    public int? OperadorID { get; set; }
    public string OperadorNombre { get; set; } = string.Empty;

    public string FolioOF { get; set; } = string.Empty;

    public DateTime FechaInicioParo { get; set; }
    public DateTime? FechaFinParo { get; set; }

    public int DuracionMinutos { get; set; }
    public bool EsMayorA15Minutos { get; set; }
    public bool EnCurso { get; set; }

    public string MotivoParoTexto { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;

    public string EstadoTexto =>
        EnCurso ? "EN CURSO" : "CERRADO";

    public string DuracionTexto
    {
        get
        {
            var total = Math.Max(0, DuracionMinutos);
            var horas = total / 60;
            var minutos = total % 60;

            if (horas <= 0)
                return $"{minutos} min";

            return $"{horas} h {minutos:00} min";
        }
    }

    public string MaquinaVisible =>
        string.IsNullOrWhiteSpace(MaquinaCodigo)
            ? (string.IsNullOrWhiteSpace(MaquinaNombre)
                ? "Sin máquina"
                : MaquinaNombre)
            : MaquinaCodigo;

    public string OperadorVisible =>
        string.IsNullOrWhiteSpace(OperadorNombre)
            ? "Sin operador"
            : OperadorNombre;

    public string MotivoVisible =>
        string.IsNullOrWhiteSpace(MotivoParoTexto)
            ? "Sin motivo"
            : MotivoParoTexto;

    public string OFVisible =>
        string.IsNullOrWhiteSpace(FolioOF)
            ? "Sin OF"
            : FolioOF;
}
