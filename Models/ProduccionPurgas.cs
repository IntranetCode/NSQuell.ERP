namespace ERP.NSQuell.Models;

public sealed class ProduccionPurgaIndexVm
{
    public bool Configurado { get; set; } = true;
    public bool PuedeEntregarAlmacen { get; set; }
    public DateTime FechaConsulta { get; set; } = DateTime.Now;
    public List<ProduccionPurgaItemVm> Purgas { get; set; } = new();
    public List<ProduccionPurgaMaquinaVm> Maquinas { get; set; } = new();
    public List<ProduccionPurgaProgramaVm> Programas { get; set; } = new();
    public List<ProduccionPurgaMaterialVm> Materiales { get; set; } = new();
}

public sealed class ProduccionPurgaItemVm
{
    public long PurgaID { get; set; }
    public int MaquinaID { get; set; }
    public string Maquina { get; set; } = string.Empty;
    public int ProgramaProduccionID { get; set; }
    public string OF { get; set; } = string.Empty;
    public string Parte { get; set; } = string.Empty;
    public int MaterialID { get; set; }
    public string Material { get; set; } = string.Empty;
    public string TipoMP { get; set; } = "V";
    public string Lote { get; set; } = string.Empty;
    public decimal CantidadSolicitadaKg { get; set; }
    public decimal? CantidadEntregadaKg { get; set; }
    public string Motivo { get; set; } = string.Empty;
    public string? Observaciones { get; set; }
    public string Estado { get; set; } = string.Empty;
    public DateTime FechaSolicitud { get; set; }
    public DateTime? FechaEntrega { get; set; }
    public DateTime? FechaAplicacion { get; set; }
}

public sealed class ProduccionPurgaMaquinaVm { public int MaquinaID { get; set; } public string Texto { get; set; } = string.Empty; }
public sealed class ProduccionPurgaProgramaVm { public int ProgramaProduccionID { get; set; } public int? MaquinaID { get; set; } public string Texto { get; set; } = string.Empty; }
public sealed class ProduccionPurgaMaterialVm { public int MaterialID { get; set; } public string TipoMP { get; set; } = "V"; public string Texto { get; set; } = string.Empty; public decimal DisponibleKg { get; set; } }
