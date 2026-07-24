namespace ERP.NSQuell.Models;

public sealed class ReleaseDocumentoPreparadoEditarRequest
{
    public string LoteID { get; set; } = string.Empty;
    public string DocumentoID { get; set; } = string.Empty;
    public int? ClienteID { get; set; }
    public string? FolioCliente { get; set; }
    public string? Version { get; set; }
    public DateTime? FechaDocumento { get; set; }
    public DateTime FechaRecepcion { get; set; } = DateTime.Today;
    public string? Observaciones { get; set; }
    public List<ReleaseDocumentoPreparadoRenglonEditarRequest> Renglones { get; set; } = new();
}

public sealed class ReleaseDocumentoPreparadoRenglonEditarRequest
{
    public string? NumeroParte { get; set; }
    public string? ReferenciaSAP { get; set; }
    public string? DesignacionDescripcionSAP { get; set; }
    public string? UnidadMedidaCliente { get; set; }
    public string? ContratoCliente { get; set; }
    public string? Observaciones { get; set; }
    public List<ReleaseDocumentoPreparadoEntregaEditarRequest> Entregas { get; set; } = new();
}

public sealed class ReleaseDocumentoPreparadoEntregaEditarRequest
{
    public DateTime? FechaRequerida { get; set; }
    public DateTime? FechaCarga { get; set; }
    public int CantidadRequerida { get; set; }
}