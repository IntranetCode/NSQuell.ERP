using System;
using System.Collections.Generic;

namespace ERP.NSQuell.Models;

// NSQ_PRODUCCION_RELEVO_OPERADOR_V9_1_TRAMOS
public sealed class ProduccionRelevoV91PostVm
{
    public int EjecucionProduccionID { get; set; }
    public int ProgramaProduccionID { get; set; }
    public int OperadorEntranteID { get; set; }

    public int TurnoID { get; set; }
    public string? TurnoNombre { get; set; }
    public DateTime FechaTrabajo { get; set; }

    public DateTime SegmentoInicio { get; set; }
    public DateTime SegmentoFin { get; set; }

    public long? ContadorMaquinaActual { get; set; }

    public int CantidadSospechosa { get; set; }
    public int CantidadScrap { get; set; }

    public List<ProduccionRegistroDefectoPostVm> DefectosScrap { get; set; }
        = new();

    public string? Motivo { get; set; }
    public string? Justificacion { get; set; }

    public string Vista { get; set; } = "dia";
    public string Panel { get; set; } = "planner";

    public DateTime? FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }
}