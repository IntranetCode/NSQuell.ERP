using System;
using System.Collections.Generic;

namespace ERP.NSQuell.Models
{
    public sealed class CtqPeriodoFiltroVm
    {
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
        public int TotalResultados { get; set; }

        public List<string> Proveedores { get; set; } = new();
        public List<string> EstatusDisponibles { get; set; } = new();
        public List<string> Monedas { get; set; } = new();
        public List<string> CentrosCosto { get; set; } = new();
        public List<string> Almacenes { get; set; } = new();
    }

    public sealed class CtqAgrupadoVm
    {
        public string Nombre { get; set; } = "Sin dato";
        public int Documentos { get; set; }
        public decimal TotalMxn { get; set; }
        public decimal PagadoMxn { get; set; }
        public decimal SaldoMxn { get; set; }
    }

    public sealed class CtqTendenciaVm
    {
        public int Anio { get; set; }
        public int Mes { get; set; }
        public int Documentos { get; set; }
        public decimal TotalMxn { get; set; }
        public decimal PagadoMxn { get; set; }
        public decimal SaldoMxn { get; set; }
    }

    public sealed class CtqResumenVm
    {
        public CtqPeriodoFiltroVm Filtro { get; set; } = new();
        public int Documentos { get; set; }
        public int Proveedores { get; set; }
        public int ConSaldo { get; set; }
        public int SinXml { get; set; }
        public int SinPdf { get; set; }
        public decimal TotalMxn { get; set; }
        public decimal PagadoMxn { get; set; }
        public decimal SaldoMxn { get; set; }
        public decimal TicketPromedioMxn { get; set; }
        public decimal? TipoCambioUsd { get; set; }
        public decimal? TotalUsd => TipoCambioUsd.GetValueOrDefault() > 0 ? TotalMxn / TipoCambioUsd.Value : null;
        public decimal? PagadoUsd => TipoCambioUsd.GetValueOrDefault() > 0 ? PagadoMxn / TipoCambioUsd.Value : null;
        public decimal? SaldoUsd => TipoCambioUsd.GetValueOrDefault() > 0 ? SaldoMxn / TipoCambioUsd.Value : null;
        public List<CtqAgrupadoVm> TopProveedores { get; set; } = new();
        public List<CtqAgrupadoVm> CentrosCosto { get; set; } = new();
        public List<CtqAgrupadoVm> Almacenes { get; set; } = new();
        public List<CtqTendenciaVm> Tendencia { get; set; } = new();
        public string? ErrorMessage { get; set; }
    }

    public sealed class CtqCuentaPagarVm
    {
        public CtqPeriodoFiltroVm Filtro { get; set; } = new();
        public int TotalDocumentos { get; set; }
        public decimal TotalSaldoMxn { get; set; }
        public decimal TotalPagadoMxn { get; set; }
        public decimal? TipoCambioUsd { get; set; }
        public decimal? TotalSaldoUsd => TipoCambioUsd.GetValueOrDefault() > 0 ? TotalSaldoMxn / TipoCambioUsd.Value : null;
        public List<CtqCuentaPagarFilaVm> Filas { get; set; } = new();
        public int TotalPaginas => Math.Max(1, (int)Math.Ceiling(TotalDocumentos / (double)Math.Max(1, Filtro.TamanoPagina)));
        public string? ErrorMessage { get; set; }
    }

    public sealed class CtqCuentaPagarFilaVm
    {
        public int EmpresaOrigenID { get; set; }
        public int CompraID { get; set; }
        public DateTime FechaCompra { get; set; }
        public string? Folio { get; set; }
        public string? Proveedor { get; set; }
        public string? RFC { get; set; }
        public string? Moneda { get; set; }
        public string? CondicionPago { get; set; }
        public DateTime? FechaUltimoPago { get; set; }
        public decimal TotalOriginal { get; set; }
        public decimal PagadoOriginal { get; set; }
        public decimal SaldoOriginal { get; set; }
        public decimal SaldoMxn { get; set; }
        public int AntiguedadDias { get; set; }
        public string? Estatus { get; set; }
    }

    public sealed class CtqProveedoresVm
    {
        public CtqPeriodoFiltroVm Filtro { get; set; } = new();
        public List<CtqProveedorFilaVm> Filas { get; set; } = new();
        public int TotalProveedores { get; set; }
        public int TotalPaginas => Math.Max(1, (int)Math.Ceiling(TotalProveedores / (double)Math.Max(1, Filtro.TamanoPagina)));
        public string? ErrorMessage { get; set; }
    }

    public sealed class CtqProveedorFilaVm
    {
        public string Proveedor { get; set; } = "Sin proveedor";
        public string? RFC { get; set; }
        public int Compras { get; set; }
        public decimal TotalMxn { get; set; }
        public decimal PagadoMxn { get; set; }
        public decimal SaldoMxn { get; set; }
        public decimal TicketPromedioMxn { get; set; }
        public DateTime? PrimeraCompra { get; set; }
        public DateTime? UltimaCompra { get; set; }
        public decimal FrecuenciaDias { get; set; }
    }

    public sealed class CtqProductosVm
    {
        public CtqPeriodoFiltroVm Filtro { get; set; } = new();
        public List<CtqProductoFilaVm> Filas { get; set; } = new();
        public int TotalProductos { get; set; }
        public int TotalPaginas => Math.Max(1, (int)Math.Ceiling(TotalProductos / (double)Math.Max(1, Filtro.TamanoPagina)));
        public string? ErrorMessage { get; set; }
    }

    public sealed class CtqProductoFilaVm
    {
        public string Codigo { get; set; } = "Sin código";
        public string Producto { get; set; } = "Sin descripción";
        public string? Unidad { get; set; }
        public decimal Cantidad { get; set; }
        public int Compras { get; set; }
        public int Proveedores { get; set; }
        public decimal ImporteMxn { get; set; }
        public decimal PrecioPromedioMxn { get; set; }
        public decimal PrecioMinMxn { get; set; }
        public decimal PrecioMaxMxn { get; set; }
        public DateTime? UltimaCompra { get; set; }
    }

    public sealed class CtqFiscalVm
    {
        public CtqPeriodoFiltroVm Filtro { get; set; } = new();
        public string EstadoDocumento { get; set; } = "Todos";
        public int TotalDocumentos { get; set; }
        public int TotalFiltrados { get; set; }
        public int Completos { get; set; }
        public int SinXml { get; set; }
        public int SinPdf { get; set; }
        public int SinAmbos { get; set; }
        public List<CtqFiscalFilaVm> Filas { get; set; } = new();
        public int TotalPaginas => Math.Max(1, (int)Math.Ceiling(TotalFiltrados / (double)Math.Max(1, Filtro.TamanoPagina)));
        public string? ErrorMessage { get; set; }
    }

    public sealed class CtqFiscalFilaVm
    {
        public int EmpresaOrigenID { get; set; }
        public int CompraID { get; set; }
        public DateTime FechaCompra { get; set; }
        public string? Folio { get; set; }
        public string? Proveedor { get; set; }
        public string? RFC { get; set; }
        public string? UUID { get; set; }
        public bool TieneXml { get; set; }
        public bool TienePdf { get; set; }
        public string? Estatus { get; set; }
        public decimal TotalMxn { get; set; }
    }

    public sealed class CtqAnaliticaVm
    {
        public CtqPeriodoFiltroVm Filtro { get; set; } = new();
        public List<CtqTendenciaVm> Tendencia { get; set; } = new();
        public List<CtqAgrupadoVm> Proveedores { get; set; } = new();
        public List<CtqAgrupadoVm> CentrosCosto { get; set; } = new();
        public List<CtqAgrupadoVm> Almacenes { get; set; } = new();
        public List<CtqAgrupadoVm> Monedas { get; set; } = new();
        public string? ErrorMessage { get; set; }
    }
}
