using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers
{
    public partial class ComprasController
    {
        [HttpGet]
        public async Task<IActionResult> ContpaqiCuentasPagar(
            DateTime? desde = null, DateTime? hasta = null,
            string? buscar = null, string? proveedor = null, string? rfc = null,
            string? estatus = null, string? moneda = null, string? centroCosto = null,
            string? almacen = null, int pagina = 1, int tamanoPagina = 25)
        {
            if (!await TieneAccesoDepartamentoAsync("Compras")) return RedirectToAction(nameof(Index));
            var f = CtqV15CrearFiltro(desde, hasta, buscar, proveedor, rfc, estatus, moneda, centroCosto, almacen, pagina, tamanoPagina);
            var model = new CtqCuentaPagarVm { Filtro = f };

            try
            {
                using var cn = new SqlConnection(GetConnectionString());
                await cn.OpenAsync();
                await CtqV15CargarCatalogosAsync(cn, f);

                var sql = CtqV15DocumentoTempSql + CtqV15TipoCambioUsdSql + @"
SELECT
    TotalDocumentos = COUNT(1),
    TotalSaldoMxn = COALESCE(SUM(SaldoMxn),0),
    TotalPagadoMxn = COALESCE(SUM(PagadoMxn),0),
    TipoCambioUsd = @TipoCambioUsd
FROM #CTQDoc
WHERE SaldoMxn > .009;

SELECT
    EmpresaOrigenID, CompraID, FechaCompra, FolioCompra, Proveedor, RFCProveedor, Moneda,
    CondicionPago, FechaUltimoPago, EstatusCompra,
    TotalOriginal = COALESCE(TotalCompra,0),
    PagadoOriginal = COALESCE(TotalPagado,0),
    SaldoOriginal = COALESCE(SaldoPendiente,0),
    SaldoMxn,
    AntiguedadDias = DATEDIFF(DAY, FechaCompra, GETDATE())
FROM #CTQDoc
WHERE SaldoMxn > .009
ORDER BY SaldoMxn DESC, FechaCompra
OFFSET @Offset ROWS FETCH NEXT @TamanoPagina ROWS ONLY;";

                using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 };
                CtqV15AddFilterParameters(cmd, f);
                cmd.Parameters.Add("@Offset", SqlDbType.Int).Value = (f.Pagina - 1) * f.TamanoPagina;
                cmd.Parameters.Add("@TamanoPagina", SqlDbType.Int).Value = f.TamanoPagina;

                using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    model.TotalDocumentos = CtqV15Int(r,"TotalDocumentos");
                    model.TotalSaldoMxn = CtqV15Decimal(r,"TotalSaldoMxn");
                    model.TotalPagadoMxn = CtqV15Decimal(r,"TotalPagadoMxn");
                    model.TipoCambioUsd = CtqV15NullableDecimal(r,"TipoCambioUsd");
                    f.TotalResultados = model.TotalDocumentos;
                }

                if (await r.NextResultAsync())
                    while (await r.ReadAsync()) model.Filas.Add(new CtqCuentaPagarFilaVm
                    {
                        EmpresaOrigenID = CtqV15Int(r,"EmpresaOrigenID"), CompraID = CtqV15Int(r,"CompraID"),
                        FechaCompra = CtqV15Date(r,"FechaCompra") ?? DateTime.MinValue,
                        Folio = CtqV15String(r,"FolioCompra"), Proveedor = CtqV15String(r,"Proveedor"), RFC = CtqV15String(r,"RFCProveedor"),
                        Moneda = CtqV15String(r,"Moneda"), CondicionPago = CtqV15String(r,"CondicionPago"), FechaUltimoPago = CtqV15Date(r,"FechaUltimoPago"),
                        TotalOriginal = CtqV15Decimal(r,"TotalOriginal"), PagadoOriginal = CtqV15Decimal(r,"PagadoOriginal"), SaldoOriginal = CtqV15Decimal(r,"SaldoOriginal"),
                        SaldoMxn = CtqV15Decimal(r,"SaldoMxn"), AntiguedadDias = CtqV15Int(r,"AntiguedadDias"), Estatus = CtqV15String(r,"EstatusCompra")
                    });
            }
            catch (Exception ex)
            {
                model.ErrorMessage = "No fue posible consultar cuentas por pagar." +
                    (string.Equals(_environment.EnvironmentName,"Development",StringComparison.OrdinalIgnoreCase) ? " Detalle técnico: " + ex.Message : "");
            }
            return View(model);
        }
    }
}
