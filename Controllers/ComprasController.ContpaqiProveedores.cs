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
        public async Task<IActionResult> ContpaqiProveedores(
            DateTime? desde = null, DateTime? hasta = null,
            string? buscar = null, string? proveedor = null, string? rfc = null,
            string? estatus = null, string? moneda = null, string? centroCosto = null,
            string? almacen = null, int pagina = 1, int tamanoPagina = 25)
        {
            if (!await TieneAccesoDepartamentoAsync("Compras")) return RedirectToAction(nameof(Index));
            var f = CtqV15CrearFiltro(desde, hasta, buscar, proveedor, rfc, estatus, moneda, centroCosto, almacen, pagina, tamanoPagina);
            var model = new CtqProveedoresVm { Filtro = f };

            try
            {
                using var cn = new SqlConnection(GetConnectionString());
                await cn.OpenAsync();
                await CtqV15CargarCatalogosAsync(cn, f);

                var sql = CtqV15DocumentoTempSql + @"
IF OBJECT_ID('tempdb..#P') IS NOT NULL DROP TABLE #P;
SELECT
    Proveedor = COALESCE(NULLIF(LTRIM(RTRIM(Proveedor)),''),'Sin proveedor'),
    RFC = MAX(RFCProveedor),
    Compras = COUNT(1),
    TotalMxn = SUM(TotalMxn),
    PagadoMxn = SUM(PagadoMxn),
    SaldoMxn = SUM(SaldoMxn),
    TicketPromedioMxn = CASE WHEN COUNT(1)>0 THEN SUM(TotalMxn)/COUNT(1) ELSE 0 END,
    PrimeraCompra = MIN(FechaCompra),
    UltimaCompra = MAX(FechaCompra),
    FrecuenciaDias = CASE WHEN COUNT(1)>1
        THEN CONVERT(decimal(18,2),DATEDIFF(DAY,MIN(FechaCompra),MAX(FechaCompra))) / NULLIF(COUNT(1)-1,0)
        ELSE 0 END
INTO #P
FROM #CTQDoc
GROUP BY COALESCE(NULLIF(LTRIM(RTRIM(Proveedor)),''),'Sin proveedor');

SELECT TotalProveedores = COUNT(1) FROM #P;
SELECT * FROM #P
ORDER BY TotalMxn DESC, Proveedor
OFFSET @Offset ROWS FETCH NEXT @TamanoPagina ROWS ONLY;";

                using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 };
                CtqV15AddFilterParameters(cmd, f);
                cmd.Parameters.Add("@Offset", SqlDbType.Int).Value = (f.Pagina - 1) * f.TamanoPagina;
                cmd.Parameters.Add("@TamanoPagina", SqlDbType.Int).Value = f.TamanoPagina;

                using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    model.TotalProveedores = CtqV15Int(r,"TotalProveedores");
                    f.TotalResultados = model.TotalProveedores;
                }
                if (await r.NextResultAsync())
                    while (await r.ReadAsync()) model.Filas.Add(new CtqProveedorFilaVm
                    {
                        Proveedor = CtqV15String(r,"Proveedor") ?? "Sin proveedor", RFC = CtqV15String(r,"RFC"), Compras = CtqV15Int(r,"Compras"),
                        TotalMxn = CtqV15Decimal(r,"TotalMxn"), PagadoMxn = CtqV15Decimal(r,"PagadoMxn"), SaldoMxn = CtqV15Decimal(r,"SaldoMxn"),
                        TicketPromedioMxn = CtqV15Decimal(r,"TicketPromedioMxn"), PrimeraCompra = CtqV15Date(r,"PrimeraCompra"), UltimaCompra = CtqV15Date(r,"UltimaCompra"),
                        FrecuenciaDias = CtqV15Decimal(r,"FrecuenciaDias")
                    });
            }
            catch (Exception ex)
            {
                model.ErrorMessage = "No fue posible consultar proveedores." +
                    (string.Equals(_environment.EnvironmentName,"Development",StringComparison.OrdinalIgnoreCase) ? " Detalle técnico: " + ex.Message : "");
            }
            return View(model);
        }
    }
}
