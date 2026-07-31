using System.ComponentModel.DataAnnotations;

namespace ERP.NSQuell.Models.ViewModels.Almacen;

public sealed class AlmacenOfResumenVm
{
    public int SolicitudProduccionID { get; set; }
    public string NumeroOF { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public string NumeroParte { get; set; } = string.Empty;
    public string DescripcionParte { get; set; } = string.Empty;
    public string Maquina { get; set; } = string.Empty;
    public DateTime? FechaRequerida { get; set; }
    public string Prioridad { get; set; } = string.Empty;
    public decimal MPRequerida { get; set; }
    public decimal MPEntregada { get; set; }
    public decimal MPPendiente { get; set; }
    public decimal EmbalajeRequerido { get; set; }
    public decimal EmbalajeEntregado { get; set; }
    public decimal EmbalajePendiente { get; set; }
    public decimal PTRequerido { get; set; }
    public bool TieneSolicitudPT { get; set; }
    public string Estado { get; set; } = string.Empty;
    public DateTime? FechaEntregaCompleta { get; set; }
}

public sealed class AlmacenOfIndexVm
{
    public string? Busqueda { get; set; }
    public string? Estado { get; set; }
    public bool EsHistorico { get; set; }
    public List<AlmacenOfResumenVm> Items { get; set; } = new();
}

public sealed class AlmacenOfMaterialVm
{
    public int SolicitudProduccionDetalleID { get; set; }
    public string Modulo { get; set; } = string.Empty;
    public int CatalogoID { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Variante { get; set; } = string.Empty;
    public decimal Requerido { get; set; }
    public decimal Entregado { get; set; }
    public decimal Pendiente => Math.Max(0, Requerido - Entregado);
    public string Unidad { get; set; } = string.Empty;
}

public sealed class AlmacenOfEntregaVm
{
    public long MovimientoID { get; set; }
    public string Modulo { get; set; } = string.Empty;
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Variante { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public decimal Cantidad { get; set; }
    public string Unidad { get; set; } = string.Empty;
    public string EntregadoPor { get; set; } = string.Empty;
    public string RecibidoPor { get; set; } = string.Empty;
    public string PuestoRecibe { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;
}

public sealed class AlmacenUsuarioProduccionVm
{
    public int UsuarioID { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Puesto { get; set; } = string.Empty;
}

public sealed class AlmacenOfDetalleVm
{
    public AlmacenOfResumenVm Resumen { get; set; } = new();
    public List<AlmacenOfMaterialVm> Materiales { get; set; } = new();
    public List<AlmacenOfEntregaVm> Entregas { get; set; } = new();
    public List<AlmacenUsuarioProduccionVm> Receptores { get; set; } = new();
}

public sealed class AlmacenRegistrarEntregaVm
{
    [Required] public int SolicitudProduccionID { get; set; }
    [Required] public int SolicitudProduccionDetalleID { get; set; }
    [Required] public string Modulo { get; set; } = string.Empty;
    [Required] public int CatalogoID { get; set; }
    public string Variante { get; set; } = string.Empty;
    [Range(0.001, 999999999)] public decimal Cantidad { get; set; }
    [Required] public int RecibidoPorUsuarioID { get; set; }
    public string? Observaciones { get; set; }
}

public sealed class AlmacenMaestroVm
{
    public int? AlmacenID { get; set; }
    [Required, StringLength(30)] public string Codigo { get; set; } = string.Empty;
    [Required, StringLength(120)] public string Nombre { get; set; } = string.Empty;
    [Required] public string TipoAlmacen { get; set; } = "OTRO";
    public string? Descripcion { get; set; }
    public bool PermiteNegativos { get; set; }
    public bool Activo { get; set; } = true;
}

public sealed class AlmacenCompraVm
{
    [Required] public string Modulo { get; set; } = string.Empty;
    [Required] public int CatalogoID { get; set; }
    public string Variante { get; set; } = string.Empty;
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public decimal Stock { get; set; }
    public decimal StockMinimo { get; set; }
    public decimal StockAviso { get; set; }
    public string Unidad { get; set; } = string.Empty;
    public string? Motivo { get; set; }
    public string? ReturnUrl { get; set; }
}
