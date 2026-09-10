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
        public async Task<IActionResult> ContpaqiResumen(
            DateTime? desde = null, DateTime? hasta = null,
            string? buscar = null, string? proveedor = null, string? rfc = null,
            string? estatus = null, string? moneda = null, string? centroCosto = null,
            string? almacen = null, int pagina = 1, int tamanoPagina = 25)
        {
            if (!await TieneAccesoDepartamentoAsync("Compras"))
            {
                TempData["Error"] = "Tu usuario no tiene acceso a Compras CONTPAQ.";
                return RedirectToAction(nameof(Index));
            }

            var f = CtqV15CrearFiltro(desde, hasta, buscar, proveedor, rfc, estatus, moneda, centroCosto, almacen, pagina, tamanoPagina);
            var model = new CtqResumenVm { Filtro = f };

            try
            {
                using var cn = new SqlConnection(GetConnectionString());
                await cn.OpenAsync();
                await CtqV15CargarCatalogosAsync(cn, f);

                var sql = CtqV15DocumentoTempSql + CtqV15TipoCambioUsdSql + @"
SELECT
    Documentos = COUNT(1),
    Proveedores = COUNT(DISTINCT NULLIF(LTRIM(RTRIM(Proveedor)),'')),
    ConSaldo = COALESCE(SUM(CASE WHEN SaldoMxn > .009 THEN 1 ELSE 0 END),0),
    SinXml = COALESCE(SUM(CASE WHEN TieneXmlBit = 0 THEN 1 ELSE 0 END),0),
    SinPdf = COALESCE(SUM(CASE WHEN TienePdfBit = 0 THEN 1 ELSE 0 END),0),
    TotalMxn = COALESCE(SUM(TotalMxn),0),
    PagadoMxn = COALESCE(SUM(PagadoMxn),0),
    SaldoMxn = COALESCE(SUM(SaldoMxn),0),
    TicketPromedioMxn = CASE WHEN COUNT(1)>0 THEN COALESCE(SUM(TotalMxn),0)/COUNT(1) ELSE 0 END,
    TipoCambioUsd = @TipoCambioUsd
FROM #CTQDoc;

SELECT TOP (@Top)
    Nombre = COALESCE(NULLIF(LTRIM(RTRIM(Proveedor)),''),'Sin proveedor'),
    Documentos = COUNT(1), TotalMxn = SUM(TotalMxn), PagadoMxn = SUM(PagadoMxn), SaldoMxn = SUM(SaldoMxn)
FROM #CTQDoc
GROUP BY COALESCE(NULLIF(LTRIM(RTRIM(Proveedor)),''),'Sin proveedor')
ORDER BY TotalMxn DESC;

SELECT TOP (@Top)
    Nombre = COALESCE(NULLIF(LTRIM(RTRIM(CentroCosto)),''),'Sin centro de costo'),
    Documentos = COUNT(1), TotalMxn = SUM(TotalMxn), PagadoMxn = SUM(PagadoMxn), SaldoMxn = SUM(SaldoMxn)
FROM #CTQDoc
GROUP BY COALESCE(NULLIF(LTRIM(RTRIM(CentroCosto)),''),'Sin centro de costo')
ORDER BY TotalMxn DESC;

SELECT TOP (@Top)
    Nombre = COALESCE(NULLIF(LTRIM(RTRIM(AlmacenDocumento)),''),'Sin almacén'),
    Documentos = COUNT(1), TotalMxn = SUM(TotalMxn), PagadoMxn = SUM(PagadoMxn), SaldoMxn = SUM(SaldoMxn)
FROM #CTQDoc
GROUP BY COALESCE(NULLIF(LTRIM(RTRIM(AlmacenDocumento)),''),'Sin almacén')
ORDER BY TotalMxn DESC;

SELECT YEAR(FechaCompra) Anio, MONTH(FechaCompra) Mes, COUNT(1) Documentos,
       SUM(TotalMxn) TotalMxn, SUM(PagadoMxn) PagadoMxn, SUM(SaldoMxn) SaldoMxn
FROM #CTQDoc
GROUP BY YEAR(FechaCompra), MONTH(FechaCompra)
ORDER BY Anio, Mes;";

                using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 };
                CtqV15AddFilterParameters(cmd, f);
                cmd.Parameters.Add("@Top", SqlDbType.Int).Value = f.TamanoPagina;

                using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    model.Documentos = CtqV15Int(r,"Documentos");
                    model.Proveedores = CtqV15Int(r,"Proveedores");
                    model.ConSaldo = CtqV15Int(r,"ConSaldo");
                    model.SinXml = CtqV15Int(r,"SinXml");
                    model.SinPdf = CtqV15Int(r,"SinPdf");
                    model.TotalMxn = CtqV15Decimal(r,"TotalMxn");
                    model.PagadoMxn = CtqV15Decimal(r,"PagadoMxn");
                    model.SaldoMxn = CtqV15Decimal(r,"SaldoMxn");
                    model.TicketPromedioMxn = CtqV15Decimal(r,"TicketPromedioMxn");
                    model.TipoCambioUsd = CtqV15NullableDecimal(r,"TipoCambioUsd");
                    f.TotalResultados = model.Documentos;
                }

                async Task ReadGroup(System.Collections.Generic.List<CtqAgrupadoVm> target)
                {
                    if (!await r.NextResultAsync()) return;
                    while (await r.ReadAsync()) target.Add(new CtqAgrupadoVm
                    {
                        Nombre = CtqV15String(r,"Nombre") ?? "Sin dato",
                        Documentos = CtqV15Int(r,"Documentos"),
                        TotalMxn = CtqV15Decimal(r,"TotalMxn"),
                        PagadoMxn = CtqV15Decimal(r,"PagadoMxn"),
                        SaldoMxn = CtqV15Decimal(r,"SaldoMxn")
                    });
                }

                await ReadGroup(model.TopProveedores);
                await ReadGroup(model.CentrosCosto);
                await ReadGroup(model.Almacenes);

                if (await r.NextResultAsync())
                    while (await r.ReadAsync()) model.Tendencia.Add(new CtqTendenciaVm
                    {
                        Anio = CtqV15Int(r,"Anio"), Mes = CtqV15Int(r,"Mes"), Documentos = CtqV15Int(r,"Documentos"),
                        TotalMxn = CtqV15Decimal(r,"TotalMxn"), PagadoMxn = CtqV15Decimal(r,"PagadoMxn"), SaldoMxn = CtqV15Decimal(r,"SaldoMxn")
                    });
            }
            catch (Exception ex)
            {
                model.ErrorMessage = "No fue posible consultar el resumen de CONTPAQ." +
                    (string.Equals(_environment.EnvironmentName,"Development",StringComparison.OrdinalIgnoreCase) ? " Detalle técnico: " + ex.Message : "");
            }

            return View(model);
        }
    }
}
