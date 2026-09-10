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
        public async Task<IActionResult> ContpaqiAnalitica(
            DateTime? desde = null, DateTime? hasta = null,
            string? buscar = null, string? proveedor = null, string? rfc = null,
            string? estatus = null, string? moneda = null, string? centroCosto = null,
            string? almacen = null, int pagina = 1, int tamanoPagina = 25)
        {
            if (!await TieneAccesoDepartamentoAsync("Compras")) return RedirectToAction(nameof(Index));
            var f = CtqV15CrearFiltro(desde, hasta, buscar, proveedor, rfc, estatus, moneda, centroCosto, almacen, pagina, tamanoPagina);
            var model = new CtqAnaliticaVm { Filtro = f };

            try
            {
                using var cn = new SqlConnection(GetConnectionString());
                await cn.OpenAsync();
                await CtqV15CargarCatalogosAsync(cn, f);

                var sql = CtqV15DocumentoTempSql + @"
SELECT YEAR(FechaCompra) Anio, MONTH(FechaCompra) Mes, COUNT(1) Documentos,
       SUM(TotalMxn) TotalMxn, SUM(PagadoMxn) PagadoMxn, SUM(SaldoMxn) SaldoMxn
FROM #CTQDoc GROUP BY YEAR(FechaCompra),MONTH(FechaCompra) ORDER BY Anio,Mes;

SELECT TOP (@Top) Nombre=COALESCE(NULLIF(LTRIM(RTRIM(Proveedor)),''),'Sin proveedor'),COUNT(1) Documentos,
       SUM(TotalMxn) TotalMxn,SUM(PagadoMxn) PagadoMxn,SUM(SaldoMxn) SaldoMxn
FROM #CTQDoc GROUP BY COALESCE(NULLIF(LTRIM(RTRIM(Proveedor)),''),'Sin proveedor') ORDER BY TotalMxn DESC;

SELECT TOP (@Top) Nombre=COALESCE(NULLIF(LTRIM(RTRIM(CentroCosto)),''),'Sin centro de costo'),COUNT(1) Documentos,
       SUM(TotalMxn) TotalMxn,SUM(PagadoMxn) PagadoMxn,SUM(SaldoMxn) SaldoMxn
FROM #CTQDoc GROUP BY COALESCE(NULLIF(LTRIM(RTRIM(CentroCosto)),''),'Sin centro de costo') ORDER BY TotalMxn DESC;

SELECT TOP (@Top) Nombre=COALESCE(NULLIF(LTRIM(RTRIM(AlmacenDocumento)),''),'Sin almacén'),COUNT(1) Documentos,
       SUM(TotalMxn) TotalMxn,SUM(PagadoMxn) PagadoMxn,SUM(SaldoMxn) SaldoMxn
FROM #CTQDoc GROUP BY COALESCE(NULLIF(LTRIM(RTRIM(AlmacenDocumento)),''),'Sin almacén') ORDER BY TotalMxn DESC;

SELECT Nombre=COALESCE(NULLIF(LTRIM(RTRIM(Moneda)),''),'Sin moneda'),COUNT(1) Documentos,
       SUM(TotalMxn) TotalMxn,SUM(PagadoMxn) PagadoMxn,SUM(SaldoMxn) SaldoMxn
FROM #CTQDoc GROUP BY COALESCE(NULLIF(LTRIM(RTRIM(Moneda)),''),'Sin moneda') ORDER BY TotalMxn DESC;

SELECT TotalResultados=COUNT(1) FROM #CTQDoc;";

                using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 };
                CtqV15AddFilterParameters(cmd, f);
                cmd.Parameters.Add("@Top", SqlDbType.Int).Value = f.TamanoPagina;
                using var r = await cmd.ExecuteReaderAsync();

                while (await r.ReadAsync()) model.Tendencia.Add(new CtqTendenciaVm
                {
                    Anio=CtqV15Int(r,"Anio"),Mes=CtqV15Int(r,"Mes"),Documentos=CtqV15Int(r,"Documentos"),TotalMxn=CtqV15Decimal(r,"TotalMxn"),PagadoMxn=CtqV15Decimal(r,"PagadoMxn"),SaldoMxn=CtqV15Decimal(r,"SaldoMxn")
                });

                async Task ReadGroup(System.Collections.Generic.List<CtqAgrupadoVm> target)
                {
                    if (!await r.NextResultAsync()) return;
                    while (await r.ReadAsync()) target.Add(new CtqAgrupadoVm
                    {
                        Nombre=CtqV15String(r,"Nombre")??"Sin dato",Documentos=CtqV15Int(r,"Documentos"),TotalMxn=CtqV15Decimal(r,"TotalMxn"),PagadoMxn=CtqV15Decimal(r,"PagadoMxn"),SaldoMxn=CtqV15Decimal(r,"SaldoMxn")
                    });
                }

                await ReadGroup(model.Proveedores); await ReadGroup(model.CentrosCosto); await ReadGroup(model.Almacenes); await ReadGroup(model.Monedas);
                if (await r.NextResultAsync() && await r.ReadAsync()) f.TotalResultados=CtqV15Int(r,"TotalResultados");
            }
            catch (Exception ex)
            {
                model.ErrorMessage = "No fue posible consultar analítica." +
                    (string.Equals(_environment.EnvironmentName,"Development",StringComparison.OrdinalIgnoreCase) ? " Detalle técnico: " + ex.Message : "");
            }
            return View(model);
        }
    }
}
