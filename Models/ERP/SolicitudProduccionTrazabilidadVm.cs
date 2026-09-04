namespace ERP.NSQuell.Models.ERP;

// NSQ_OF_TRAZABILIDAD_V3E
public sealed class SolicitudProduccionTrazabilidadItemVm
{
    public int SolicitudProduccionID { get; set; }
    public DateTime FechaEvento { get; set; }
    public int OrdenEtapa { get; set; }
    public string Etapa { get; set; } = string.Empty;
    public string Evento { get; set; } = string.Empty;
    public string? EstadoAnterior { get; set; }
    public string? EstadoNuevo { get; set; }
    public string? Descripcion { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public string TipoOrigen { get; set; } = string.Empty;
    public long OrigenID { get; set; }
    public bool EsAlerta { get; set; }
    public string? Severidad { get; set; }
    public string? EvidenciaUrl { get; set; }
}

public sealed class SolicitudProduccionAlertaVm
{
    public int SolicitudProduccionID { get; set; }
    public DateTime FechaAlerta { get; set; }
    public string Departamento { get; set; } = string.Empty;
    public string TipoAlerta { get; set; } = string.Empty;
    public string Severidad { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public string OrigenTabla { get; set; } = string.Empty;
    public long OrigenID { get; set; }
    public string? EvidenciaUrl { get; set; }
}

public sealed class SolicitudProduccionEstadoActualVm
{
    public int SolicitudProduccionID { get; set; }
    public int EtapaActualOrden { get; set; }
    public string EtapaActual { get; set; } = "OF creada";
    public string ResumenActual { get; set; } = string.Empty;
    public DateTime? FechaUltimoAvance { get; set; }
}
