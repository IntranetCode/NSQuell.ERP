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
        public async Task<IActionResult> ContpaqiProductos(
            DateTime? desde = null, DateTime? hasta = null,
            string? buscar = null, string? proveedor = null, string? rfc = null,
            string? estatus = null, string? moneda = null, string? centroCosto = null,
            string? almacen = null, int pagina = 1, int tamanoPagina = 25)
        {
            if (!await TieneAccesoDepartamentoAsync("Compras")) return RedirectToAction(nameof(Index));
            var f = CtqV15CrearFiltro(desde, hasta, buscar, proveedor, rfc, estatus, moneda, centroCosto, almacen, pagina, tamanoPagina);
            var model = new CtqProductosVm { Filtro = f };

            try
            {
                using var cn = new SqlConnection(GetConnectionString());
                await cn.OpenAsync();
                await CtqV15CargarCatalogosAsync(cn, f);

                var sql = @"
SET NOCOUNT ON;
IF OBJECT_ID('tempdb..#I') IS NOT NULL DROP TABLE #I;
IF OBJECT_ID('tempdb..#P') IS NOT NULL DROP TABLE #P;

SELECT
    EmpresaOrigenID, CompraID, PartidaID, FechaCompra,
    CodigoProducto, Producto, Unidad, Cantidad, PrecioUnitario, ImportePartida,
    Proveedor, RFCProveedor, EstatusCompra, Moneda, CentroCosto, AlmacenDocumento,
    CONVERT(nvarchar(100), FolioCompra) AS FolioCompra, UUID,
    FactorMXN = CASE
        WHEN NULLIF(ABS(CONVERT(decimal(38,10),TotalCompra)),0) IS NOT NULL AND TotalCompraMXN IS NOT NULL
            THEN ABS(CONVERT(decimal(38,10),TotalCompraMXN)/NULLIF(CONVERT(decimal(38,10),TotalCompra),0))
        WHEN UPPER(LTRIM(RTRIM(ISNULL(Moneda,'')))) IN ('MXN','PESO MEXICANO','PESOS MEXICANOS','PESO','PESOS')
            THEN CONVERT(decimal(38,10),1)
        WHEN TRY_CONVERT(decimal(38,10),TipoCambio)>0 THEN ABS(TRY_CONVERT(decimal(38,10),TipoCambio))
        ELSE NULL END
INTO #I
FROM [LINK CONTPAQ].[CSPGRUPOQUELL].[dbo].[ERP_Compras]
WHERE FechaCompra >= @Desde AND FechaCompra < @HastaExclusiva
  AND (@Buscar IS NULL OR CodigoProducto LIKE @BuscarLike OR Producto LIKE @BuscarLike OR Proveedor LIKE @BuscarLike OR FolioCompra LIKE @BuscarLike OR RFCProveedor LIKE @BuscarLike OR UUID LIKE @BuscarLike)
  AND (@Proveedor IS NULL OR Proveedor = @Proveedor)
  AND (@RFC IS NULL OR RFCProveedor LIKE @RFCLike)
  AND (@Estatus IS NULL OR EstatusCompra = @Estatus)
  AND (@Moneda IS NULL OR Moneda = @Moneda)
  AND (@CentroCosto IS NULL OR CentroCosto = @CentroCosto)
  AND (@Almacen IS NULL OR AlmacenDocumento = @Almacen);

SELECT
    Codigo = COALESCE(NULLIF(LTRIM(RTRIM(CodigoProducto)),''),'Sin código'),
    Producto = COALESCE(NULLIF(LTRIM(RTRIM(Producto)),''),'Sin descripción'),
    Unidad = MAX(Unidad),
    Cantidad = SUM(COALESCE(Cantidad,0)),
    Compras = COUNT(DISTINCT CONVERT(varchar(20),EmpresaOrigenID)+'|'+CONVERT(varchar(20),CompraID)),
    Proveedores = COUNT(DISTINCT NULLIF(LTRIM(RTRIM(Proveedor)),'')),
    ImporteMxn = SUM(COALESCE(ImportePartida,0) * COALESCE(FactorMXN,0)),
    PrecioPromedioMxn = AVG(COALESCE(PrecioUnitario,0) * COALESCE(FactorMXN,0)),
    PrecioMinMxn = MIN(COALESCE(PrecioUnitario,0) * COALESCE(FactorMXN,0)),
    PrecioMaxMxn = MAX(COALESCE(PrecioUnitario,0) * COALESCE(FactorMXN,0)),
    UltimaCompra = MAX(FechaCompra)
INTO #P
FROM #I
GROUP BY COALESCE(NULLIF(LTRIM(RTRIM(CodigoProducto)),''),'Sin código'), COALESCE(NULLIF(LTRIM(RTRIM(Producto)),''),'Sin descripción');

SELECT TotalProductos = COUNT(1) FROM #P;
SELECT * FROM #P
ORDER BY ImporteMxn DESC, Producto
OFFSET @Offset ROWS FETCH NEXT @TamanoPagina ROWS ONLY;";

                using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 };
                CtqV15AddFilterParameters(cmd, f);
                cmd.Parameters.Add("@Offset", SqlDbType.Int).Value = (f.Pagina - 1) * f.TamanoPagina;
                cmd.Parameters.Add("@TamanoPagina", SqlDbType.Int).Value = f.TamanoPagina;

                using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    model.TotalProductos = CtqV15Int(r,"TotalProductos");
                    f.TotalResultados = model.TotalProductos;
                }
                if (await r.NextResultAsync())
                    while (await r.ReadAsync()) model.Filas.Add(new CtqProductoFilaVm
                    {
                        Codigo = CtqV15String(r,"Codigo") ?? "Sin código", Producto = CtqV15String(r,"Producto") ?? "Sin descripción", Unidad = CtqV15String(r,"Unidad"),
                        Cantidad = CtqV15Decimal(r,"Cantidad"), Compras = CtqV15Int(r,"Compras"), Proveedores = CtqV15Int(r,"Proveedores"), ImporteMxn = CtqV15Decimal(r,"ImporteMxn"),
                        PrecioPromedioMxn = CtqV15Decimal(r,"PrecioPromedioMxn"), PrecioMinMxn = CtqV15Decimal(r,"PrecioMinMxn"), PrecioMaxMxn = CtqV15Decimal(r,"PrecioMaxMxn"),
                        UltimaCompra = CtqV15Date(r,"UltimaCompra")
                    });
            }
            catch (Exception ex)
            {
                model.ErrorMessage = "No fue posible consultar productos comprados." +
                    (string.Equals(_environment.EnvironmentName,"Development",StringComparison.OrdinalIgnoreCase) ? " Detalle técnico: " + ex.Message : "");
            }
            return View(model);
        }
    }
}
