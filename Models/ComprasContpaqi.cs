using System;
using System.Collections.Generic;

namespace ERP.NSQuell.Models
{
    public sealed class ComprasContpaqiFiltroViewModel
    {
        public int Anio { get; set; }
        public int Mes { get; set; }
        public bool RangoPersonalizado { get; set; }
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }

        public string? Buscar { get; set; }
        public string? Proveedor { get; set; }
        public string? RFC { get; set; }
        public string? Estatus { get; set; }
        public string? Moneda { get; set; }
        public string? CentroCosto { get; set; }
        public string? Almacen { get; set; }

        public int Pagina { get; set; } = 1;
        public int TamanoPagina { get; set; } = 25;
    }

    public sealed class ComprasContpaqiResumenItemViewModel
    {
        public string Nombre { get; set; } = "Sin dato";
        public int Documentos { get; set; }
        public decimal TotalMXN { get; set; }
        public decimal TotalUSD { get; set; }
        public decimal Porcentaje { get; set; }
    }

    public sealed class ComprasContpaqiIndexViewModel
    {
        public ComprasContpaqiFiltroViewModel Filtros { get; set; } = new();
        public List<ComprasContpaqiDocumentoViewModel> Documentos { get; set; } = new();

        public List<string> Proveedores { get; set; } = new();
        public List<string> Monedas { get; set; } = new();
        public List<string> CentrosCosto { get; set; } = new();
        public List<string> Almacenes { get; set; } = new();
        public List<string> EstatusDisponibles { get; set; } = new();

        public List<ComprasContpaqiResumenItemViewModel> TopProveedores { get; set; } = new();
        public List<ComprasContpaqiResumenItemViewModel> ResumenEstatus { get; set; } = new();
        public List<ComprasContpaqiResumenItemViewModel> ResumenMoneda { get; set; } = new();
        public List<ComprasContpaqiResumenItemViewModel> ResumenCentroCosto { get; set; } = new();
        public List<ComprasContpaqiResumenItemViewModel> ResumenAlmacen { get; set; } = new();

        public int TotalDocumentos { get; set; }
        public int ProveedoresUnicos { get; set; }
        public int DocumentosConSaldo { get; set; }
        public int DocumentosCancelados { get; set; }
        public int DocumentosSinXML { get; set; }
        public int DocumentosSinPDF { get; set; }

        public decimal TotalCompraMXN { get; set; }
        public decimal? TotalCompraUSD { get; set; }
        public decimal TotalPagadoMXN { get; set; }
        public decimal? TotalPagadoUSD { get; set; }
        public decimal SaldoPendienteMXN { get; set; }
        public decimal? SaldoPendienteUSD { get; set; }
        public decimal TicketPromedioMXN { get; set; }

        public decimal? TipoCambioUSDReferencia { get; set; }
        public DateTime? FechaTipoCambioUSDReferencia { get; set; }
        public string? FuenteTipoCambio { get; set; }

        public string? ErrorMessage { get; set; }

        public int TotalPaginas =>
            Math.Max(1, (int)Math.Ceiling(TotalDocumentos / (double)Math.Max(1, Filtros.TamanoPagina)));
    }

    public sealed class ComprasContpaqiDocumentoViewModel
    {
        public int EmpresaOrigenID { get; set; }
        public int CompraID { get; set; }
        public string? CompraClave { get; set; }

        public DateTime FechaCompra { get; set; }
        public DateTime? FechaEntregaDocumento { get; set; }
        public string? FolioCompra { get; set; }
        public string? TituloCompra { get; set; }
        public string? Comentarios { get; set; }

        // Resumen de la primera partida para identificar rapidamente que se compro.
        // TotalPartidas indica si el documento contiene mas conceptos.
        public string? CodigoProductoResumen { get; set; }
        public string? ProductoResumen { get; set; }
        public int TotalPartidas { get; set; }

        public string? ProveedorIDTexto { get; set; }
        public string? Proveedor { get; set; }
        public string? RFCProveedor { get; set; }
        public string? TelefonoProveedor { get; set; }
        public string? EmailProveedor { get; set; }
        public string? WebProveedor { get; set; }
        public string? EstadoProveedor { get; set; }

        public decimal? SubtotalCompra { get; set; }
        public decimal? DescuentoCompra { get; set; }
        public decimal? ImpuestosCompra { get; set; }
        public decimal? RetencionesCompra { get; set; }
        public decimal? CostoTotalCompra { get; set; }
        public decimal? TotalCompra { get; set; }

        public string? Moneda { get; set; }
        public string? SimboloMoneda { get; set; }
        public decimal? TipoCambio { get; set; }
        public decimal? FactorEfectivoMXN { get; set; }
        public decimal? TotalCompraMXN { get; set; }
        public decimal? TotalCompraUSD { get; set; }

        public string? CondicionPago { get; set; }
        public decimal? TotalPagado { get; set; }
        public decimal? TotalPagadoMXN { get; set; }
        public decimal? TotalPagadoUSD { get; set; }
        public decimal? SaldoPendiente { get; set; }
        public decimal? SaldoPendienteMXN { get; set; }
        public decimal? SaldoPendienteUSD { get; set; }
        public DateTime? FechaUltimoPago { get; set; }

        public string? UUID { get; set; }
        public bool TieneXML { get; set; }
        public bool TienePDF { get; set; }

        public string? AlmacenDocumento { get; set; }
        public string? CentroCosto { get; set; }

        public bool Impreso { get; set; }
        public bool Validado { get; set; }
        public bool Cancelado { get; set; }
        public bool Eliminado { get; set; }
        public bool EnUso { get; set; }
        public string? EstatusCompra { get; set; }

        public DateTime? DocumentoCreadoEn { get; set; }
        public string? DocumentoCreadoPor { get; set; }
        public DateTime? DocumentoValidadoEn { get; set; }
        public string? DocumentoValidadoPor { get; set; }
        public DateTime? UltimoCambioConocido { get; set; }

        public int? PeriodoMes { get; set; }
        public int? PeriodoSemana { get; set; }
        public int? PeriodoAnio { get; set; }
        public int? PeriodoTrimestre { get; set; }

        public string? PolizaDiario { get; set; }
        public string? PolizaIngreso { get; set; }
        public string? PolizaEgreso { get; set; }
        public string? PolizaTotal { get; set; }
        public string? PolizasGeneradas { get; set; }
    }

    public sealed class ComprasContpaqiDetalleViewModel
    {
        public ComprasContpaqiDocumentoViewModel Documento { get; set; } = new();
        public List<ComprasContpaqiPartidaViewModel> Partidas { get; set; } = new();
        public string? ReturnQueryString { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public sealed class ComprasContpaqiPartidaViewModel
    {
        public int PartidaID { get; set; }
        public string? PartidaClave { get; set; }

        public int? NumeroPartida { get; set; }
        public DateTime? FechaPartida { get; set; }
        public DateTime? FechaItem { get; set; }

        public string? ProductoIDTexto { get; set; }
        public string? CodigoProducto { get; set; }
        public string? CodigoProductoProveedor { get; set; }
        public string? CodigoEAN13 { get; set; }
        public string? Producto { get; set; }
        public string? Unidad { get; set; }
        public string? ClaveUnidadSAT { get; set; }

        public decimal? Cantidad { get; set; }
        public decimal? CantidadInventario { get; set; }
        public decimal? PrecioUnitario { get; set; }
        public decimal? CostoUnitario { get; set; }
        public decimal? ImportePartida { get; set; }
        public decimal? PorcentajeDescuento { get; set; }

        public decimal? PorcentajeIVA { get; set; }
        public decimal? PorcentajeRetencion { get; set; }
        public decimal? PorcentajeIEPS { get; set; }
        public decimal? ImporteIEPS { get; set; }
        public decimal? BaseIVA { get; set; }
        public decimal? IVAImportacion { get; set; }
        public decimal? PorcentajeImpuestoPais { get; set; }
        public string? ObjetoImpuestoSAT { get; set; }

        public string? AlmacenIDTexto { get; set; }
        public string? CentroCostoIDTexto { get; set; }
        public string? Lote { get; set; }
        public string? NumeroSerie { get; set; }
        public string? NumeroSerieHasta { get; set; }
        public DateTime? FechaCaducidad { get; set; }
        public string? Rack { get; set; }
        public string? Posicion { get; set; }
        public string? Nivel { get; set; }

        public string? DocumentoOrigenID { get; set; }
        public string? PartidaOrigenID { get; set; }
        public string? PartidaEntregaID { get; set; }
        public string? PartidaDestinoID { get; set; }
        public string? ProveedorPartidaID { get; set; }
        public string? OrdenCompraExterna { get; set; }
        public string? Orden { get; set; }
        public string? Pedido { get; set; }

        public bool PartidaCancelada { get; set; }
        public bool PartidaEliminada { get; set; }
        public DateTime? PartidaEliminadaEn { get; set; }
        public DateTime? PartidaValidadaEn { get; set; }
        public string? PartidaValidadaPor { get; set; }
        public DateTime? PartidaSincronizadaEn { get; set; }
    }
}