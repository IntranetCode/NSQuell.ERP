using System.ComponentModel.DataAnnotations;

namespace ERP.NSQuell.Models.ViewModels.Almacen;

public sealed class AlmacenMPIndexVm
{
    public bool Configurado { get; set; } = true;
    public string? MensajeConfiguracion { get; set; }
    public string? Busqueda { get; set; }
    public string? Estado { get; set; }

    public int TotalMateriales { get; set; }
    public int Criticos { get; set; }
    public int Advertencias { get; set; }
    public int Disponibles { get; set; }
    public int PendientesConfiguracion { get; set; }
    public decimal SaldoTotal { get; set; }

    // INVENTARIO_SOLICITADO_MP_EMB_V1_1
    public decimal SolicitadoPendiente { get; set; }
    public int OFPendientes { get; set; }
    public int RecepcionesHoy { get; set; }
    public decimal CantidadRecibidaHoy { get; set; }
    public decimal SalidasHoy { get; set; }

    public List<AlmacenMPExistenciaVm> Existencias { get; set; } = new();
    public List<AlmacenMPMovimientoListaVm> Movimientos { get; set; } = new();
}
public sealed class AlmacenMPExistenciaVm
{
    public string TipoMP { get; set; } = "VIRGEN";
    public int MaterialID { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Unidad { get; set; } = string.Empty;

    public decimal Entradas { get; set; }
    public decimal Salidas { get; set; }
    public decimal Saldo { get; set; }

    // Cantidad requerida por OF que todavía no ha sido entregada.
    public decimal Solicitado { get; set; }

    public decimal StockMinimo { get; set; }
    public decimal StockAviso { get; set; }
    public string Semaforo { get; set; } = "SIN_CONFIGURAR";
    public bool StockConfigurado { get; set; }

    public bool TieneCosto { get; set; }
    public decimal CostoUnitario { get; set; }
    public string MonedaCosto { get; set; } = string.Empty;
    public string UnidadCosto { get; set; } = string.Empty;
    public string FuenteCosto { get; set; } = string.Empty;
    public DateTime? FechaCosto { get; set; }

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

    [Required(ErrorMessage = "El cÃ³digo es obligatorio.")]
    [StringLength(80)]
    [Display(Name = "CÃ³digo")]
    public string Codigo { get; set; } = string.Empty;

    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(250)]
    [Display(Name = "Material")]
    public string Nombre { get; set; } = string.Empty;

    [Required(ErrorMessage = "La unidad es obligatoria.")]
    [StringLength(20)]
    [Display(Name = "Unidad")]
    public string UnidadDefault { get; set; } = "KG";

    [StringLength(200)]
    public string? Proveedor { get; set; }

    [Display(Name = "Requiere lote")]
    public bool RequiereLote { get; set; } = true;

    [Range(0, 999999999, ErrorMessage = "El stock mÃ­nimo no puede ser negativo.")]
    [Display(Name = "Stock mÃ­nimo")]
    public decimal StockMinimo { get; set; }

    [Range(0, 999999999, ErrorMessage = "El stock de aviso no puede ser negativo.")]
    [Display(Name = "Stock de aviso")]
    public decimal StockAviso { get; set; }

    public bool Activo { get; set; } = true;
}

public sealed class AlmacenMPMovimientoFormVm
{
    public string TipoMP { get; set; } = "VIRGEN";
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

    [Display(Name = "UbicaciÃ³n")]
    public int? UbicacionID { get; set; }

    [StringLength(80)]
    [Display(Name = "Orden de fabricaciÃ³n")]
    public string? NumeroOF { get; set; }

    [StringLength(800)]
    public string? Observaciones { get; set; }

    [Display(Name = "Fecha y hora del movimiento")]
    public DateTime FechaMovimiento { get; set; } = DateTime.Now;

    public bool EsEntregaOF { get; set; }

    [Display(Name = "Solicitud de producciÃ³n")]
    public int? SolicitudProduccionID { get; set; }

    [Display(Name = "Cantidad pendiente de la OF")]
    public decimal CantidadPendienteOF { get; set; }

    [Required(ErrorMessage = "La operaciÃ³n no tiene un identificador vÃ¡lido.")]
    [StringLength(32, MinimumLength = 32)]
    public string OperacionToken { get; set; } = System.Guid.NewGuid().ToString("N");

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

    // ALMACEN_PT_SOLICITADO_V1_0
    public long PiezasSolicitadasPendientes { get; set; }
    public int OFPendientesRecepcion { get; set; }
    public int CajasRecibidasHoy { get; set; }
    public long PiezasRecibidasHoy { get; set; }
    public long PiezasSalidasHoy { get; set; }
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
    public bool TienePrecioVenta { get; set; }
    public decimal PrecioVentaUnitario { get; set; }
    public string MonedaPrecioVenta { get; set; } = string.Empty;
    public string UnidadPrecioVenta { get; set; } = string.Empty;
    public string FuentePrecioVenta { get; set; } = string.Empty;
    public DateTime? FechaPrecioVenta { get; set; }
    public DateTime? UltimoMovimiento { get; set; }

    // ALMACEN_PT_SOLICITADO_DETALLE_V1_0
    public long Solicitado { get; set; }
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
    [Display(Name = "NÃºmero de parte")]
    public int ParteID { get; set; }

    [Required]
    [StringLength(120)]
    public string Etiqueta { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    [Display(Name = "NÃºmero de caja")]
    public int NumeroCaja { get; set; } = 1;

    [Range(1, int.MaxValue)]
    public int Cantidad { get; set; }

    [StringLength(120)]
    [Display(Name = "Lote")]
    public string? LoteEtiqueta { get; set; }

    [StringLength(80)]
    [Display(Name = "Orden de fabricaciÃ³n")]
    public string? NumeroOF { get; set; }

    [Required]
    [StringLength(30)]
    [Display(Name = "Estado de calidad")]
    public string EstadoCalidad { get; set; } = "Liberado";

    [Display(Name = "UbicaciÃ³n")]
    public int? UbicacionID { get; set; }

    [StringLength(800)]
    public string? Observaciones { get; set; }

    [Display(Name = "Tipo de movimiento")]
    public string TipoMovimiento { get; set; } = "Entrada";

    [Display(Name = "Unidad")]
    public string Unidad { get; set; } = "PZS";

    [Display(Name = "Fecha y hora del movimiento")]
    public DateTime FechaMovimiento { get; set; } = DateTime.Now;

    public bool EsEntregaOF { get; set; }

    [Display(Name = "Solicitud de producciÃ³n")]
    public int? SolicitudProduccionID { get; set; }

    [Display(Name = "Cantidad pendiente de la OF")]
    public decimal CantidadPendienteOF { get; set; }

    [Required(ErrorMessage = "La operaciÃ³n no tiene un identificador vÃ¡lido.")]
    [StringLength(32, MinimumLength = 32)]
    public string OperacionToken { get; set; } = System.Guid.NewGuid().ToString("N");

    public List<AlmacenSelectVm> Partes { get; set; } = new();
    public List<AlmacenSelectVm> Ubicaciones { get; set; } = new();
    public List<AlmacenSelectVm> EstadosCalidad { get; set; } = new();
}


public sealed class AlmacenPTEntradaLoteVm
{
    [StringLength(50000)]
    [Display(Name = "CÃ³digos escaneados")]
    public string CodigosEscaneados { get; set; } = string.Empty;

    [Required]
    [StringLength(30)]
    [Display(Name = "Estado de calidad")]
    public string EstadoCalidad { get; set; } = "Liberado";

    [Display(Name = "UbicaciÃ³n")]
    public int? UbicacionID { get; set; }

    [StringLength(800)]
    public string? Observaciones { get; set; }

    public bool EsEntregaOF { get; set; }

    [Display(Name = "Solicitud de producciÃ³n")]
    public int? SolicitudProduccionID { get; set; }

    public int? ParteIDEsperada { get; set; }

    [StringLength(80)]
    public string? NumeroOFEsperada { get; set; }

    public string NumeroParteEsperada { get; set; } = string.Empty;

    public string DescripcionParteEsperada { get; set; } = string.Empty;

    public decimal CantidadPendienteOF { get; set; }

    [Required(ErrorMessage = "La operaciÃ³n no tiene un identificador vÃ¡lido.")]
    [StringLength(32, MinimumLength = 32)]
    public string OperacionToken { get; set; } =
        System.Guid.NewGuid().ToString("N");

    public List<AlmacenPTCodigoBarrasVm> Resultados { get; set; } = new();

    public List<AlmacenSelectVm> Ubicaciones { get; set; } = new();

    public List<AlmacenSelectVm> EstadosCalidad { get; set; } = new();

    public int TotalCodigos =>
        Resultados.Count > 0
            ? Resultados.Count
            : CodigosEscaneados
                .Split(
                    new[] { "\r\n", "\n", "\r" },
                    StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries)
                .Length;

    public int TotalPiezas =>
        Resultados
            .Where(x => x.Parseado)
            .Sum(x => x.Cantidad);

    public bool PuedeRegistrar =>
        Resultados.Count > 0
        && Resultados.All(x => x.Valido);
}

public sealed class AlmacenPTCodigoBarrasVm
{
    public int Renglon { get; set; }

    public string CodigoOriginal { get; set; } = string.Empty;

    public string NumeroOF { get; set; } = string.Empty;

    public string NumeroParte { get; set; } = string.Empty;

    public string Designacion { get; set; } = string.Empty;

    public int Cantidad { get; set; }

    public string Lote { get; set; } = string.Empty;

    public int ParteID { get; set; }

    public string DescripcionCatalogo { get; set; } = string.Empty;

    public string NumeroParteCatalogo { get; set; } = string.Empty;

    public bool CoincidenciaNormalizada { get; set; }

    public bool Parseado { get; set; }

    public bool ExisteEnCatalogo { get; set; }

    public bool YaRegistrado { get; set; }

    public bool RepetidoEnLote { get; set; }

    public bool CoincideConOF { get; set; } = true;

    public bool CoincideConParte { get; set; } = true;

    public string Mensaje { get; set; } = string.Empty;

    public bool Valido =>
        Parseado
        && ExisteEnCatalogo
        && !YaRegistrado
        && !RepetidoEnLote
        && CoincideConOF
        && CoincideConParte;

    public string ClaseEstado =>
        Valido
            ? "ok"
            : "error";
}

public sealed class AlmacenPTMovimientoFormVm
{
    [Required]
    [Display(Name = "NÃºmero de parte")]
    public int ParteID { get; set; }

    [Required(ErrorMessage = "Selecciona la caja fÃ­sica que se utilizarÃ¡.")]
    [Range(1, int.MaxValue, ErrorMessage = "Selecciona una caja vÃ¡lida.")]
    [Display(Name = "Caja")]
    public int? CajaID { get; set; }

    [Required]
    [Display(Name = "Tipo de movimiento")]
    public string TipoMovimiento { get; set; } = "Salida";

    [Range(1, int.MaxValue)]
    public int Cantidad { get; set; }

    [Display(Name = "UbicaciÃ³n")]
    public int? UbicacionID { get; set; }

    [StringLength(80)]
    [Display(Name = "Orden de fabricaciÃ³n")]
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
    [Display(Name = "Stock mÃ­nimo")]
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

    public string Display => string.Join(" Â· ", new[] { Almacen, Rack, Nivel, Posicion }.Where(x => !string.IsNullOrWhiteSpace(x)));
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

    [Range(typeof(decimal), "0", "999999999", ErrorMessage = "El stock mÃ­nimo no puede ser negativo.")]
    [Display(Name = "Stock mÃ­nimo")]
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

    [Required(ErrorMessage = "La caja fÃ­sica es obligatoria para descontar PT.")]
    [Range(1, int.MaxValue, ErrorMessage = "La caja fÃ­sica no es vÃ¡lida.")]
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












