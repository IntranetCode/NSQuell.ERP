namespace ERP.NSQuell.Models.ViewModels.Almacen;

public sealed class AlmacenMPDetalleStockVm
{
    public int MaterialID { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Unidad { get; set; } = "KG";

    public decimal FisicoV { get; set; }
    public decimal SolicitadoV { get; set; }
    public decimal StockV { get; set; }

    public decimal FisicoM { get; set; }
    public decimal SolicitadoM { get; set; }
    public decimal StockM { get; set; }

    public DateTime? UltimoMovimientoV { get; set; }
    public DateTime? UltimoMovimientoM { get; set; }

    public decimal FisicoTotal => FisicoV + FisicoM;
    public decimal SolicitadoTotal => SolicitadoV + SolicitadoM;
    public decimal StockTotal => StockV + StockM;

    public List<AlmacenMPDetalleMovimientoVm> Movimientos { get; set; } = new();
}

public sealed class AlmacenMPDetalleMovimientoVm
{
    public long MovimientoID { get; set; }
    public DateTime FechaMovimiento { get; set; }
    public string TipoExistencia { get; set; } = string.Empty;
    public string TipoMovimiento { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public string Unidad { get; set; } = "KG";
    public string NumeroOF { get; set; } = string.Empty;
    public string Ubicacion { get; set; } = string.Empty;
    public string Responsable { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;
}

public sealed class AlmacenMPStockSelectorVm
{
    public decimal StockV { get; set; }
    public decimal StockM { get; set; }
}
