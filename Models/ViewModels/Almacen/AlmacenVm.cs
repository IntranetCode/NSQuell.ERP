using System.ComponentModel.DataAnnotations;

namespace ERP.NSQuell.Models.ViewModels.Almacen;

public sealed class AlmacenMPIndexVm
{
    public bool Configurado { get; set; } = true;
    public string? MensajeConfiguracion { get; set; }
    public string? Busqueda { get; set; }
    public string? Estado { get; set; }
    public string? Tipo { get; set; }
    public int TotalMateriales { get; set; }
    public int Criticos { get; set; }
    public int Advertencias { get; set; }
    public int Disponibles { get; set; }
    public int PendientesConfiguracion { get; set; }
    public decimal SaldoTotal { get; set; }
    public List<AlmacenMPExistenciaVm> Existencias { get; set; } = new();
    public List<AlmacenMPMovimientoListaVm> Movimientos { get; set; } = new();
}

public sealed class AlmacenMPExistenciaVm
{
    public int MaterialID { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string TipoMaterial { get; set; } = string.Empty;
    public string Unidad { get; set; } = string.Empty;
    public decimal Entradas { get; set; }
    public decimal Salidas { get; set; }
    public decimal Saldo { get; set; }
    public decimal StockMinimo { get; set; }
    public decimal StockAviso { get; set; }
    public string Semaforo { get; set; } = "SIN_CONFIGURAR";
    public bool StockConfigurado { get; set; }
    public DateTime? UltimoMovimiento { get; set; }
}

public sealed class AlmacenMPMovimientoListaVm
{
    public long MovimientoID { get; set; }
    public DateTime FechaMovimiento { get; set; }
    public int MaterialID { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Material { get; set; } = string.Empty;
    public string TipoMovimiento { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public string Unidad { get; set; } = string.Empty;
    public string Lote { get; set; } = string.Empty;
    public string Ubicacion { get; set; } = string.Empty;
    public string NumeroOF { get; set; } = string.Empty;
    public string Responsable { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;
    public string ReferenciaOperacion { get; set; } = string.Empty;
}

public sealed class AlmacenMaterialFormVm
{
    public int? MaterialID { get; set; }

    [Required(ErrorMessage = "El código es obligatorio.")]
    [StringLength(80)]
    [Display(Name = "Código")]
    public string Codigo { get; set; } = string.Empty;

    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(250)]
    [Display(Name = "Material")]
    public string Nombre { get; set; } = string.Empty;

    [StringLength(80)]
    [Display(Name = "Tipo de material")]
    public string? TipoMaterial { get; set; }

    [Required(ErrorMessage = "La unidad es obligatoria.")]
    [StringLength(20)]
    [Display(Name = "Unidad")]
    public string UnidadDefault { get; set; } = "KG";

    [StringLength(200)]
    public string? Proveedor { get; set; }

    [Display(Name = "Requiere lote")]
    public bool RequiereLote { get; set; } = true;

    [Range(0, 999999999, ErrorMessage = "El stock mínimo no puede ser negativo.")]
    [Display(Name = "Stock mínimo")]
    public decimal StockMinimo { get; set; }

    [Range(0, 999999999, ErrorMessage = "El stock de aviso no puede ser negativo.")]
    [Display(Name = "Stock de aviso")]
    public decimal StockAviso { get; set; }

    public bool Activo { get; set; } = true;
}

public sealed class AlmacenMPMovimientoFormVm
{
    [Required]
    [Display(Name = "Material")]
    public int MaterialID { get; set; }

    [Required]
    [Display(Name = "Tipo de movimiento")]
    public string TipoMovimiento { get; set; } = "Entrada";

    [Range(typeof(decimal), "0.001", "999999999", ErrorMessage = "La cantidad debe ser mayor que cero.")]
    public decimal Cantidad { get; set; }

    [Required]
    [StringLength(20)]
    public string Unidad { get; set; } = "KG";

    [StringLength(120)]
    public string Lote { get; set; } = "S/L";

    [Display(Name = "Ubicación")]
    public int? UbicacionID { get; set; }

    [StringLength(80)]
    [Display(Name = "Orden de fabricación")]
    public string? NumeroOF { get; set; }

    [StringLength(800)]
    public string? Observaciones { get; set; }

    public List<AlmacenSelectVm> Materiales { get; set; } = new();
    public List<AlmacenSelectVm> Ubicaciones { get; set; } = new();
    public List<AlmacenSelectVm> TiposMovimiento { get; set; } = new();
}

public sealed class AlmacenPTIndexVm
{
    public bool Configurado { get; set; } = true;
    public string? MensajeConfiguracion { get; set; }
    public string? Busqueda { get; set; }
    public string? Estado { get; set; }
    public int TotalPartes { get; set; }
    public int Criticos { get; set; }
    public int Advertencias { get; set; }
    public int Disponibles { get; set; }
    public int PendientesConfiguracion { get; set; }
    public int PiezasFisicas { get; set; }
    public int PiezasRetenidas { get; set; }
    public int PiezasDisponibles { get; set; }
    public List<AlmacenPTExistenciaVm> Existencias { get; set; } = new();
    public List<AlmacenPTMovimientoListaVm> Movimientos { get; set; } = new();
}

public sealed class AlmacenPTExistenciaVm
{
    public int ParteID { get; set; }
    public string NumeroParte { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public int Cajas { get; set; }
    public int Entradas { get; set; }
    public int Salidas { get; set; }
    public int SaldoFisico { get; set; }
    public int Retenido { get; set; }
    public int Disponible { get; set; }
    public int StockMinimo { get; set; }
    public int StockAviso { get; set; }
    public string Semaforo { get; set; } = "SIN_CONFIGURAR";
    public bool StockConfigurado { get; set; }
    public DateTime? UltimoMovimiento { get; set; }
}

public sealed class AlmacenPTMovimientoListaVm
{
    public long MovimientoID { get; set; }
    public DateTime FechaMovimiento { get; set; }
    public int ParteID { get; set; }
    public string NumeroParte { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Etiqueta { get; set; } = string.Empty;
    public string TipoMovimiento { get; set; } = string.Empty;
    public int Cantidad { get; set; }
    public string Ubicacion { get; set; } = string.Empty;
    public string EstadoCalidad { get; set; } = string.Empty;
    public string NumeroOF { get; set; } = string.Empty;
    public string Responsable { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;
    public string ReferenciaOperacion { get; set; } = string.Empty;
    public string LoteEtiqueta { get; set; } = string.Empty;
    public int NumeroCaja { get; set; }
}

public sealed class AlmacenPTEntradaFormVm
{
    [Required]
    [Display(Name = "Número de parte")]
    public int ParteID { get; set; }

    [Required]
    [StringLength(120)]
    public string Etiqueta { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    [Display(Name = "Número de caja")]
    public int NumeroCaja { get; set; } = 1;

    [Range(1, int.MaxValue)]
    public int Cantidad { get; set; }

    [StringLength(120)]
    [Display(Name = "Lote")]
    public string? LoteEtiqueta { get; set; }

    [StringLength(80)]
    [Display(Name = "Orden de fabricación")]
    public string? NumeroOF { get; set; }

    [Required]
    [StringLength(30)]
    [Display(Name = "Estado de calidad")]
    public string EstadoCalidad { get; set; } = "Liberado";

    [Display(Name = "Ubicación")]
    public int? UbicacionID { get; set; }

    [StringLength(800)]
    public string? Observaciones { get; set; }

    public List<AlmacenSelectVm> Partes { get; set; } = new();
    public List<AlmacenSelectVm> Ubicaciones { get; set; } = new();
    public List<AlmacenSelectVm> EstadosCalidad { get; set; } = new();
}

public sealed class AlmacenPTMovimientoFormVm
{
    [Required]
    [Display(Name = "Número de parte")]
    public int ParteID { get; set; }

    [Required(ErrorMessage = "Selecciona la caja física que se utilizará.")]
    [Range(1, int.MaxValue, ErrorMessage = "Selecciona una caja válida.")]
    [Display(Name = "Caja")]
    public int? CajaID { get; set; }

    [Required]
    [Display(Name = "Tipo de movimiento")]
    public string TipoMovimiento { get; set; } = "Salida";

    [Range(1, int.MaxValue)]
    public int Cantidad { get; set; }

    [Display(Name = "Ubicación")]
    public int? UbicacionID { get; set; }

    [StringLength(80)]
    [Display(Name = "Orden de fabricación")]
    public string? NumeroOF { get; set; }

    [StringLength(30)]
    [Display(Name = "Estado de calidad")]
    public string? EstadoCalidad { get; set; }

    [StringLength(800)]
    public string? Observaciones { get; set; }

    public List<AlmacenSelectVm> Partes { get; set; } = new();
    public List<AlmacenSelectVm> Cajas { get; set; } = new();
    public List<AlmacenSelectVm> Ubicaciones { get; set; } = new();
    public List<AlmacenSelectVm> TiposMovimiento { get; set; } = new();
    public List<AlmacenSelectVm> EstadosCalidad { get; set; } = new();
}

public sealed class AlmacenPTStockFormVm
{
    public int ParteID { get; set; }
    public string NumeroParte { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;

    [Range(0, int.MaxValue)]
    [Display(Name = "Stock mínimo")]
    public int StockMinimo { get; set; }

    [Range(0, int.MaxValue)]
    [Display(Name = "Stock de aviso")]
    public int StockAviso { get; set; }
}

public sealed class AlmacenUbicacionesIndexVm
{
    public List<AlmacenUbicacionVm> Ubicaciones { get; set; } = new();
}

public sealed class AlmacenUbicacionVm
{
    public int? UbicacionID { get; set; }

    [Required]
    [StringLength(60)]
    public string Almacen { get; set; } = "MP";

    [Required]
    [StringLength(120)]
    public string Rack { get; set; } = string.Empty;

    [StringLength(40)]
    public string? Nivel { get; set; }

    [StringLength(40)]
    public string? Posicion { get; set; }

    public bool Activo { get; set; } = true;

    public string Display => string.Join(" · ", new[] { Almacen, Rack, Nivel, Posicion }.Where(x => !string.IsNullOrWhiteSpace(x)));
}

public sealed class AlmacenSelectVm
{
    public int Id { get; set; }
    public string Texto { get; set; } = string.Empty;
    public string? Extra { get; set; }
}

public sealed class AlmacenStockNivelesVm
{
    public string Modulo { get; set; } = string.Empty;
    public string? Busqueda { get; set; }
    public bool SoloSinConfigurar { get; set; }
    public int Total { get; set; }
    public int Configurados { get; set; }
    public int Pendientes { get; set; }
    public List<AlmacenStockNivelItemVm> Items { get; set; } = new();
}

public sealed class AlmacenStockNivelItemVm
{
    public int CatalogoID { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Unidad { get; set; } = string.Empty;
    public decimal Disponible { get; set; }

    [Range(typeof(decimal), "0", "999999999", ErrorMessage = "El stock mínimo no puede ser negativo.")]
    [Display(Name = "Stock mínimo")]
    public decimal StockMinimo { get; set; }

    [Range(typeof(decimal), "0", "999999999", ErrorMessage = "El stock de aviso no puede ser negativo.")]
    [Display(Name = "Stock de aviso")]
    public decimal StockAviso { get; set; }

    public bool Configurado { get; set; }
}

public sealed class AlmacenDescuentoMPRequestVm
{
    [Required]
    [StringLength(80)]
    public string Codigo { get; set; } = string.Empty;

    [Range(typeof(decimal), "0.001", "999999999")]
    public decimal Cantidad { get; set; }

    [Required]
    [StringLength(80)]
    public string NumeroOF { get; set; } = string.Empty;

    [Required]
    [StringLength(120)]
    public string ReferenciaOperacion { get; set; } = string.Empty;

    [StringLength(120)]
    public string? Lote { get; set; }

    [StringLength(20)]
    public string? Unidad { get; set; }

    public int? UbicacionID { get; set; }

    [StringLength(800)]
    public string? Observaciones { get; set; }
}

public sealed class AlmacenDescuentoPTRequestVm
{
    [Required]
    [StringLength(120)]
    public string NumeroParte { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int Cantidad { get; set; }

    [Required(ErrorMessage = "La caja física es obligatoria para descontar PT.")]
    [Range(1, int.MaxValue, ErrorMessage = "La caja física no es válida.")]
    public int? CajaID { get; set; }

    [Required]
    [StringLength(80)]
    public string NumeroOF { get; set; } = string.Empty;

    [Required]
    [StringLength(120)]
    public string ReferenciaOperacion { get; set; } = string.Empty;

    public int? UbicacionID { get; set; }

    [StringLength(800)]
    public string? Observaciones { get; set; }
}

public sealed class AlmacenMPHistorialVm
{
    public string? FiltroMaterial { get; set; }
    public string? Busqueda { get; set; }
    public string? TipoMovimiento { get; set; }
    public string? NumeroOF { get; set; }
    public string? Responsable { get; set; }
    public string? Lote { get; set; }
    public DateTime? Desde { get; set; }
    public DateTime? Hasta { get; set; }
    public int Pagina { get; set; } = 1;
    public int TamanoPagina { get; set; } = 50;
    public int TotalRegistros { get; set; }
    public int TotalPaginas => Math.Max(1, (int)Math.Ceiling(TotalRegistros / (double)Math.Max(1, TamanoPagina)));
    public List<string> TiposMovimiento { get; set; } = new();
    public List<AlmacenSelectVm> MaterialesFiltro { get; set; } = new();
    public List<string> OrdenesFiltro { get; set; } = new();
    public List<string> ResponsablesFiltro { get; set; } = new();
    public List<string> LotesFiltro { get; set; } = new();
    public List<AlmacenMPMovimientoListaVm> Movimientos { get; set; } = new();
}

public sealed class AlmacenPTHistorialVm
{
    public string? FiltroParte { get; set; }
    public string? Busqueda { get; set; }
    public string? TipoMovimiento { get; set; }
    public string? NumeroOF { get; set; }
    public string? Responsable { get; set; }
    public string? EtiquetaLote { get; set; }
    public DateTime? Desde { get; set; }
    public DateTime? Hasta { get; set; }
    public int Pagina { get; set; } = 1;
    public int TamanoPagina { get; set; } = 50;
    public int TotalRegistros { get; set; }
    public int TotalPaginas => Math.Max(1, (int)Math.Ceiling(TotalRegistros / (double)Math.Max(1, TamanoPagina)));
    public List<string> TiposMovimiento { get; set; } = new();
    public List<AlmacenSelectVm> PartesFiltro { get; set; } = new();
    public List<string> OrdenesFiltro { get; set; } = new();
    public List<string> ResponsablesFiltro { get; set; } = new();
    public List<string> EtiquetasLotesFiltro { get; set; } = new();
    public List<AlmacenPTMovimientoListaVm> Movimientos { get; set; } = new();
}
