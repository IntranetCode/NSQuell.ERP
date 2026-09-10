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
        public async Task<IActionResult> ContpaqiFiscal(
            DateTime? desde = null, DateTime? hasta = null,
            string? buscar = null, string? proveedor = null, string? rfc = null,
            string? estatus = null, string? moneda = null, string? centroCosto = null,
            string? almacen = null, string estadoDocumento = "Todos",
            int pagina = 1, int tamanoPagina = 25)
        {
            if (!await TieneAccesoDepartamentoAsync("Compras")) return RedirectToAction(nameof(Index));
            var f = CtqV15CrearFiltro(desde, hasta, buscar, proveedor, rfc, estatus, moneda, centroCosto, almacen, pagina, tamanoPagina);
            var estado = string.IsNullOrWhiteSpace(estadoDocumento) ? "Todos" : estadoDocumento.Trim();
            var model = new CtqFiscalVm { Filtro = f, EstadoDocumento = estado };

            try
            {
                using var cn = new SqlConnection(GetConnectionString());
                await cn.OpenAsync();
                await CtqV15CargarCatalogosAsync(cn, f);

                var sql = CtqV15DocumentoTempSql + @"
SELECT
    TotalDocumentos = COUNT(1),
    Completos = COALESCE(SUM(CASE WHEN TieneXmlBit=1 AND TienePdfBit=1 THEN 1 ELSE 0 END),0),
    SinXml = COALESCE(SUM(CASE WHEN TieneXmlBit=0 THEN 1 ELSE 0 END),0),
    SinPdf = COALESCE(SUM(CASE WHEN TienePdfBit=0 THEN 1 ELSE 0 END),0),
    SinAmbos = COALESCE(SUM(CASE WHEN TieneXmlBit=0 AND TienePdfBit=0 THEN 1 ELSE 0 END),0)
FROM #CTQDoc;

SELECT TotalFiltrados = COUNT(1)
FROM #CTQDoc
WHERE @Estado='Todos'
   OR (@Estado='Completos' AND TieneXmlBit=1 AND TienePdfBit=1)
   OR (@Estado='Sin XML' AND TieneXmlBit=0)
   OR (@Estado='Sin PDF' AND TienePdfBit=0)
   OR (@Estado='Sin ambos' AND TieneXmlBit=0 AND TienePdfBit=0);

SELECT EmpresaOrigenID,CompraID,FechaCompra,FolioCompra,Proveedor,RFCProveedor,UUID,TieneXmlBit,TienePdfBit,EstatusCompra,TotalMxn
FROM #CTQDoc
WHERE @Estado='Todos'
   OR (@Estado='Completos' AND TieneXmlBit=1 AND TienePdfBit=1)
   OR (@Estado='Sin XML' AND TieneXmlBit=0)
   OR (@Estado='Sin PDF' AND TienePdfBit=0)
   OR (@Estado='Sin ambos' AND TieneXmlBit=0 AND TienePdfBit=0)
ORDER BY FechaCompra DESC,CompraID DESC
OFFSET @Offset ROWS FETCH NEXT @TamanoPagina ROWS ONLY;";

                using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 };
                CtqV15AddFilterParameters(cmd, f);
                CtqV15AddText(cmd,"@Estado",estado,50);
                cmd.Parameters.Add("@Offset", SqlDbType.Int).Value = (f.Pagina - 1) * f.TamanoPagina;
                cmd.Parameters.Add("@TamanoPagina", SqlDbType.Int).Value = f.TamanoPagina;

                using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    model.TotalDocumentos=CtqV15Int(r,"TotalDocumentos"); model.Completos=CtqV15Int(r,"Completos"); model.SinXml=CtqV15Int(r,"SinXml");
                    model.SinPdf=CtqV15Int(r,"SinPdf"); model.SinAmbos=CtqV15Int(r,"SinAmbos");
                }
                if (await r.NextResultAsync() && await r.ReadAsync())
                {
                    model.TotalFiltrados=CtqV15Int(r,"TotalFiltrados"); f.TotalResultados=model.TotalFiltrados;
                }
                if (await r.NextResultAsync())
                    while (await r.ReadAsync()) model.Filas.Add(new CtqFiscalFilaVm
                    {
                        EmpresaOrigenID=CtqV15Int(r,"EmpresaOrigenID"),CompraID=CtqV15Int(r,"CompraID"),FechaCompra=CtqV15Date(r,"FechaCompra")??DateTime.MinValue,
                        Folio=CtqV15String(r,"FolioCompra"),Proveedor=CtqV15String(r,"Proveedor"),RFC=CtqV15String(r,"RFCProveedor"),UUID=CtqV15String(r,"UUID"),
                        TieneXml=CtqV15Bool(r,"TieneXmlBit"),TienePdf=CtqV15Bool(r,"TienePdfBit"),Estatus=CtqV15String(r,"EstatusCompra"),TotalMxn=CtqV15Decimal(r,"TotalMxn")
                    });
            }
            catch (Exception ex)
            {
                model.ErrorMessage = "No fue posible consultar información fiscal." +
                    (string.Equals(_environment.EnvironmentName,"Development",StringComparison.OrdinalIgnoreCase) ? " Detalle técnico: " + ex.Message : "");
            }
            return View(model);
        }
    }
}
