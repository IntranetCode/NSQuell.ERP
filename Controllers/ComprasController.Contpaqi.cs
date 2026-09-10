using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers
{
    public partial class ComprasController
    {
        private const int ContpaqiCommandTimeoutSeconds = 60;

        [HttpGet]
        public async Task<IActionResult> Contpaqi(
            int? anio,
            int? mes,
            bool rangoPersonalizado = false,
            DateTime? desde = null,
            DateTime? hasta = null,
            string? buscar = null,
            string? proveedor = null,
            string? rfc = null,
            string? estatus = null,
            string? moneda = null,
            string? centroCosto = null,
            string? almacen = null,
            int pagina = 1,
            int tamanoPagina = 25)
        {
            if (!await TieneAccesoDepartamentoAsync("Compras"))
            {
                TempData["Error"] = "Tu usuario no tiene acceso a Compras CONTPAQ.";
                return RedirectToAction(nameof(Index));
            }

            var hoy = DateTime.Today;
            var anioSeleccionado = anio.GetValueOrDefault(hoy.Year);
            var mesSeleccionado = mes.GetValueOrDefault(hoy.Month);

            if (anioSeleccionado < 2000 || anioSeleccionado > 2100)
                anioSeleccionado = hoy.Year;

            if (mesSeleccionado < 1 || mesSeleccionado > 12)
                mesSeleccionado = hoy.Month;

            DateTime desdeReal;
            DateTime hastaReal;

            if (rangoPersonalizado && desde.HasValue && hasta.HasValue)
            {
                desdeReal = desde.Value.Date;
                hastaReal = hasta.Value.Date;

                if (desdeReal > hastaReal)
                {
                    var tmp = desdeReal;
                    desdeReal = hastaReal;
                    hastaReal = tmp;
                }
            }
            else
            {
                desdeReal = new DateTime(anioSeleccionado, mesSeleccionado, 1);
                hastaReal = desdeReal.AddMonths(1).AddDays(-1);
                rangoPersonalizado = false;
            }

            var filtros = new ComprasContpaqiFiltroViewModel
            {
                Anio = anioSeleccionado,
                Mes = mesSeleccionado,
                RangoPersonalizado = rangoPersonalizado,
                Desde = desdeReal,
                Hasta = hastaReal,
                Buscar = LimpiarFiltroContpaqi(buscar),
                Proveedor = LimpiarFiltroContpaqi(proveedor),
                RFC = LimpiarFiltroContpaqi(rfc),
                Estatus = LimpiarFiltroContpaqi(estatus),
                Moneda = LimpiarFiltroContpaqi(moneda),
                CentroCosto = LimpiarFiltroContpaqi(centroCosto),
                Almacen = LimpiarFiltroContpaqi(almacen),
                Pagina = Math.Max(1, pagina),
                TamanoPagina = Math.Min(100, Math.Max(10, tamanoPagina))
            };

            var model = new ComprasContpaqiIndexViewModel
            {
                Filtros = filtros
            };

            try
            {
                using var connection = new SqlConnection(GetConnectionString());
                await connection.OpenAsync();

                var sql = @"
SET NOCOUNT ON;

IF OBJECT_ID('tempdb..#CTQPeriodo') IS NOT NULL DROP TABLE #CTQPeriodo;
IF OBJECT_ID('tempdb..#CTQDocs') IS NOT NULL DROP TABLE #CTQDocs;

;WITH Base AS
(
    SELECT
        EmpresaOrigenID,
        CompraID,
        CompraClave,
        FechaCompra,
        FechaEntregaDocumento,
        CONVERT(nvarchar(100), FolioCompra) AS FolioCompra,
        TituloCompra,
        Comentarios,
        CONVERT(nvarchar(200), CodigoProducto) AS CodigoProductoResumen,
        CONVERT(nvarchar(1000), Producto) AS ProductoResumen,
        COUNT(1) OVER (PARTITION BY EmpresaOrigenID, CompraID) AS TotalPartidas,

        CONVERT(nvarchar(100), ProveedorID) AS ProveedorIDTexto,
        Proveedor,
        RFCProveedor,
        TelefonoProveedor,
        EmailProveedor,
        WebProveedor,
        EstadoProveedor,

        SubtotalCompra,
        DescuentoCompra,
        ImpuestosCompra,
        RetencionesCompra,
        CostoTotalCompra,
        TotalCompra,

        Moneda,
        SimboloMoneda,
        TipoCambio,
        TotalCompraMXN,

        CondicionPago,
        TotalPagado,
        SaldoPendiente,
        FechaUltimoPago,

        UUID,
        CONVERT(nvarchar(30), TieneXML) AS TieneXMLTexto,
        CONVERT(nvarchar(30), TienePDF) AS TienePDFTexto,

        AlmacenDocumento,
        CentroCosto,

        CONVERT(nvarchar(30), Impreso) AS ImpresoTexto,
        CONVERT(nvarchar(30), Validado) AS ValidadoTexto,
        CONVERT(nvarchar(30), Cancelado) AS CanceladoTexto,
        CONVERT(nvarchar(30), Eliminado) AS EliminadoTexto,
        CONVERT(nvarchar(30), EnUso) AS EnUsoTexto,
        EstatusCompra,

        DocumentoCreadoEn,
        CONVERT(nvarchar(200), DocumentoCreadoPor) AS DocumentoCreadoPor,
        DocumentoValidadoEn,
        CONVERT(nvarchar(200), DocumentoValidadoPor) AS DocumentoValidadoPor,
        UltimoCambioConocido,

        PeriodoMes,
        PeriodoSemana,
        PeriodoAnio,
        PeriodoTrimestre,

        CONVERT(nvarchar(200), PolizaDiario) AS PolizaDiario,
        CONVERT(nvarchar(200), PolizaIngreso) AS PolizaIngreso,
        CONVERT(nvarchar(200), PolizaEgreso) AS PolizaEgreso,
        CONVERT(nvarchar(200), PolizaTotal) AS PolizaTotal,
        CONVERT(nvarchar(200), PolizasGeneradas) AS PolizasGeneradas,

        ROW_NUMBER() OVER
        (
            PARTITION BY EmpresaOrigenID, CompraID
            ORDER BY NumeroPartida, PartidaID
        ) AS rn
    FROM [LINK CONTPAQ].[CSPGRUPOQUELL].[dbo].[ERP_Compras]
    WHERE FechaCompra >= @Desde
      AND FechaCompra < @HastaExclusiva
)
SELECT
    EmpresaOrigenID,
    CompraID,
    CompraClave,
    FechaCompra,
    FechaEntregaDocumento,
    FolioCompra,
    TituloCompra,
    Comentarios,
    CodigoProductoResumen,
    ProductoResumen,
    TotalPartidas,

    ProveedorIDTexto,
    Proveedor,
    RFCProveedor,
    TelefonoProveedor,
    EmailProveedor,
    WebProveedor,
    EstadoProveedor,

    SubtotalCompra,
    DescuentoCompra,
    ImpuestosCompra,
    RetencionesCompra,
    CostoTotalCompra,
    TotalCompra,

    Moneda,
    SimboloMoneda,
    TipoCambio,
    TotalCompraMXN,

    CondicionPago,
    TotalPagado,
    SaldoPendiente,
    FechaUltimoPago,

    UUID,
    TieneXMLTexto,
    TienePDFTexto,

    AlmacenDocumento,
    CentroCosto,

    ImpresoTexto,
    ValidadoTexto,
    CanceladoTexto,
    EliminadoTexto,
    EnUsoTexto,
    EstatusCompra,

    DocumentoCreadoEn,
    DocumentoCreadoPor,
    DocumentoValidadoEn,
    DocumentoValidadoPor,
    UltimoCambioConocido,

    PeriodoMes,
    PeriodoSemana,
    PeriodoAnio,
    PeriodoTrimestre,

    PolizaDiario,
    PolizaIngreso,
    PolizaEgreso,
    PolizaTotal,
    PolizasGeneradas,

    FactorEfectivoMXN =
        CASE
            WHEN NULLIF(ABS(CONVERT(decimal(38,10), TotalCompra)), 0) IS NOT NULL
             AND TotalCompraMXN IS NOT NULL
                THEN ABS(CONVERT(decimal(38,10), TotalCompraMXN) /
                         NULLIF(CONVERT(decimal(38,10), TotalCompra), 0))
            WHEN UPPER(LTRIM(RTRIM(ISNULL(Moneda, '')))) IN
                 ('MXN','PESO MEXICANO','PESOS MEXICANOS','PESO','PESOS')
                THEN CONVERT(decimal(38,10), 1)
            WHEN TRY_CONVERT(decimal(38,10), TipoCambio) > 0
                THEN ABS(TRY_CONVERT(decimal(38,10), TipoCambio))
            ELSE NULL
        END
INTO #CTQPeriodo
FROM Base
WHERE rn = 1;

SELECT
    *,
    TotalCalculadoMXN =
        COALESCE(
            TRY_CONVERT(decimal(38,6), TotalCompraMXN),
            TRY_CONVERT(decimal(38,6), TotalCompra) * TRY_CONVERT(decimal(38,10), FactorEfectivoMXN)
        ),
    PagadoCalculadoMXN =
        TRY_CONVERT(decimal(38,6), TotalPagado) * TRY_CONVERT(decimal(38,10), FactorEfectivoMXN),
    SaldoCalculadoMXN =
        TRY_CONVERT(decimal(38,6), SaldoPendiente) * TRY_CONVERT(decimal(38,10), FactorEfectivoMXN)
INTO #CTQDocs
FROM #CTQPeriodo
WHERE
    (
        @Buscar IS NULL
        OR Proveedor LIKE @BuscarLike
        OR FolioCompra LIKE @BuscarLike
        OR CompraClave LIKE @BuscarLike
        OR UUID LIKE @BuscarLike
    )
    AND (@Proveedor IS NULL OR Proveedor = @Proveedor)
    AND (@RFC IS NULL OR RFCProveedor LIKE @RFCLike)
    AND (@Estatus IS NULL OR EstatusCompra = @Estatus)
    AND (@Moneda IS NULL OR Moneda = @Moneda)
    AND (@CentroCosto IS NULL OR CentroCosto = @CentroCosto)
    AND (@Almacen IS NULL OR AlmacenDocumento = @Almacen);

DECLARE @TipoCambioUSD decimal(38,10) = NULL;
DECLARE @FechaTipoCambioUSD datetime2 = NULL;

SELECT TOP (1)
    @TipoCambioUSD = FactorEfectivoMXN,
    @FechaTipoCambioUSD = FechaCompra
FROM #CTQPeriodo
WHERE
    UPPER(LTRIM(RTRIM(ISNULL(Moneda, '')))) IN
    ('USD','US DOLAR','US DÓLAR','DOLAR','DÓLAR','DOLARES','DÓLARES','DOLLAR','US DOLLAR')
    AND FactorEfectivoMXN > 0
ORDER BY FechaCompra DESC, CompraID DESC;

/*
 Si el periodo no contiene USD, busca la ultima compra USD anterior al cierre
 del periodo. Sigue siendo una tasa proveniente de CONTPAQ; no se captura manualmente.
*/
IF @TipoCambioUSD IS NULL
BEGIN
    ;WITH FX AS
    (
        SELECT
            EmpresaOrigenID,
            CompraID,
            FechaCompra,
            TotalCompra,
            TotalCompraMXN,
            TipoCambio,
            Moneda,
            ROW_NUMBER() OVER
            (
                PARTITION BY EmpresaOrigenID, CompraID
                ORDER BY NumeroPartida, PartidaID
            ) AS rn
        FROM [LINK CONTPAQ].[CSPGRUPOQUELL].[dbo].[ERP_Compras]
        WHERE FechaCompra < @HastaExclusiva
          AND FechaCompra >= DATEADD(MONTH, -12, @HastaExclusiva)
          AND UPPER(LTRIM(RTRIM(ISNULL(Moneda, '')))) IN
              ('USD','US DOLAR','US DÓLAR','DOLAR','DÓLAR','DOLARES','DÓLARES','DOLLAR','US DOLLAR')
    )
    SELECT TOP (1)
        @TipoCambioUSD =
            CASE
                WHEN NULLIF(ABS(CONVERT(decimal(38,10), TotalCompra)), 0) IS NOT NULL
                 AND TotalCompraMXN IS NOT NULL
                    THEN ABS(CONVERT(decimal(38,10), TotalCompraMXN) /
                             NULLIF(CONVERT(decimal(38,10), TotalCompra), 0))
                WHEN TRY_CONVERT(decimal(38,10), TipoCambio) > 0
                    THEN ABS(TRY_CONVERT(decimal(38,10), TipoCambio))
                ELSE NULL
            END,
        @FechaTipoCambioUSD = FechaCompra
    FROM FX
    WHERE rn = 1
    ORDER BY FechaCompra DESC, CompraID DESC;
END;

/* 1) KPIs */
SELECT
    TotalDocumentos = COUNT(1),
    ProveedoresUnicos = COUNT(DISTINCT NULLIF(LTRIM(RTRIM(Proveedor)), '')),
    DocumentosConSaldo = COALESCE(SUM(CASE WHEN COALESCE(SaldoCalculadoMXN,0) > 0.009 THEN 1 ELSE 0 END),0),
    DocumentosCancelados = COALESCE(SUM(CASE WHEN UPPER(ISNULL(EstatusCompra,'')) IN ('CANCELADO','ELIMINADO') THEN 1 ELSE 0 END),0),
    DocumentosSinXML = COALESCE(SUM(CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(TieneXMLTexto,'')))) IN ('1','TRUE','SI','SÍ','YES') THEN 0 ELSE 1 END),0),
    DocumentosSinPDF = COALESCE(SUM(CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(TienePDFTexto,'')))) IN ('1','TRUE','SI','SÍ','YES') THEN 0 ELSE 1 END),0),

    TotalCompraMXN = COALESCE(SUM(COALESCE(TotalCalculadoMXN,0)),0),
    TotalCompraUSD =
        CASE WHEN @TipoCambioUSD > 0
             THEN COALESCE(SUM(COALESCE(TotalCalculadoMXN,0)),0) / @TipoCambioUSD
             ELSE NULL END,

    TotalPagadoMXN = COALESCE(SUM(COALESCE(PagadoCalculadoMXN,0)),0),
    TotalPagadoUSD =
        CASE WHEN @TipoCambioUSD > 0
             THEN COALESCE(SUM(COALESCE(PagadoCalculadoMXN,0)),0) / @TipoCambioUSD
             ELSE NULL END,

    SaldoPendienteMXN = COALESCE(SUM(COALESCE(SaldoCalculadoMXN,0)),0),
    SaldoPendienteUSD =
        CASE WHEN @TipoCambioUSD > 0
             THEN COALESCE(SUM(COALESCE(SaldoCalculadoMXN,0)),0) / @TipoCambioUSD
             ELSE NULL END,

    TicketPromedioMXN =
        CASE WHEN COUNT(1) > 0
             THEN COALESCE(SUM(COALESCE(TotalCalculadoMXN,0)),0) / COUNT(1)
             ELSE 0 END,

    TipoCambioUSDReferencia = @TipoCambioUSD,
    FechaTipoCambioUSDReferencia = @FechaTipoCambioUSD
FROM #CTQDocs;

/* 2) Proveedores disponibles del periodo */
SELECT DISTINCT Proveedor
FROM #CTQPeriodo
WHERE NULLIF(LTRIM(RTRIM(Proveedor)), '') IS NOT NULL
ORDER BY Proveedor;

/* 3) Monedas */
SELECT DISTINCT Moneda
FROM #CTQPeriodo
WHERE NULLIF(LTRIM(RTRIM(Moneda)), '') IS NOT NULL
ORDER BY Moneda;

/* 4) Centros */
SELECT DISTINCT CentroCosto
FROM #CTQPeriodo
WHERE NULLIF(LTRIM(RTRIM(CentroCosto)), '') IS NOT NULL
ORDER BY CentroCosto;

/* 5) Almacenes */
SELECT DISTINCT AlmacenDocumento
FROM #CTQPeriodo
WHERE NULLIF(LTRIM(RTRIM(AlmacenDocumento)), '') IS NOT NULL
ORDER BY AlmacenDocumento;

/* 6) Estatus */
SELECT DISTINCT EstatusCompra
FROM #CTQPeriodo
WHERE NULLIF(LTRIM(RTRIM(EstatusCompra)), '') IS NOT NULL
ORDER BY EstatusCompra;

/* 7) Top proveedores */
SELECT TOP (10)
    Nombre = COALESCE(NULLIF(LTRIM(RTRIM(Proveedor)), ''), 'Sin proveedor'),
    Documentos = COUNT(1),
    TotalMXN = COALESCE(SUM(COALESCE(TotalCalculadoMXN,0)),0),
    TotalUSD = CASE WHEN @TipoCambioUSD > 0 THEN COALESCE(SUM(COALESCE(TotalCalculadoMXN,0)),0) / @TipoCambioUSD ELSE 0 END,
    Porcentaje = CASE
        WHEN (SELECT SUM(COALESCE(TotalCalculadoMXN,0)) FROM #CTQDocs) > 0
        THEN COALESCE(SUM(COALESCE(TotalCalculadoMXN,0)),0) * 100.0 /
             NULLIF((SELECT SUM(COALESCE(TotalCalculadoMXN,0)) FROM #CTQDocs),0)
        ELSE 0 END
FROM #CTQDocs
GROUP BY COALESCE(NULLIF(LTRIM(RTRIM(Proveedor)), ''), 'Sin proveedor')
ORDER BY TotalMXN DESC;

/* 8) Resumen estatus */
SELECT
    Nombre = COALESCE(NULLIF(LTRIM(RTRIM(EstatusCompra)), ''), 'Sin estatus'),
    Documentos = COUNT(1),
    TotalMXN = COALESCE(SUM(COALESCE(TotalCalculadoMXN,0)),0),
    TotalUSD = CASE WHEN @TipoCambioUSD > 0 THEN COALESCE(SUM(COALESCE(TotalCalculadoMXN,0)),0) / @TipoCambioUSD ELSE 0 END,
    Porcentaje = CASE WHEN COUNT(*) OVER() > 0 THEN COUNT(1) * 100.0 / SUM(COUNT(1)) OVER() ELSE 0 END
FROM #CTQDocs
GROUP BY COALESCE(NULLIF(LTRIM(RTRIM(EstatusCompra)), ''), 'Sin estatus')
ORDER BY Documentos DESC, Nombre;

/* 9) Resumen moneda */
SELECT
    Nombre = COALESCE(NULLIF(LTRIM(RTRIM(Moneda)), ''), 'Sin moneda'),
    Documentos = COUNT(1),
    TotalMXN = COALESCE(SUM(COALESCE(TotalCalculadoMXN,0)),0),
    TotalUSD = CASE WHEN @TipoCambioUSD > 0 THEN COALESCE(SUM(COALESCE(TotalCalculadoMXN,0)),0) / @TipoCambioUSD ELSE 0 END,
    Porcentaje = CASE
        WHEN (SELECT SUM(COALESCE(TotalCalculadoMXN,0)) FROM #CTQDocs) > 0
        THEN COALESCE(SUM(COALESCE(TotalCalculadoMXN,0)),0) * 100.0 /
             NULLIF((SELECT SUM(COALESCE(TotalCalculadoMXN,0)) FROM #CTQDocs),0)
        ELSE 0 END
FROM #CTQDocs
GROUP BY COALESCE(NULLIF(LTRIM(RTRIM(Moneda)), ''), 'Sin moneda')
ORDER BY TotalMXN DESC;

/* 10) Centros */
SELECT TOP (10)
    Nombre = COALESCE(NULLIF(LTRIM(RTRIM(CentroCosto)), ''), 'Sin centro de costo'),
    Documentos = COUNT(1),
    TotalMXN = COALESCE(SUM(COALESCE(TotalCalculadoMXN,0)),0),
    TotalUSD = CASE WHEN @TipoCambioUSD > 0 THEN COALESCE(SUM(COALESCE(TotalCalculadoMXN,0)),0) / @TipoCambioUSD ELSE 0 END,
    Porcentaje = CASE
        WHEN (SELECT SUM(COALESCE(TotalCalculadoMXN,0)) FROM #CTQDocs) > 0
        THEN COALESCE(SUM(COALESCE(TotalCalculadoMXN,0)),0) * 100.0 /
             NULLIF((SELECT SUM(COALESCE(TotalCalculadoMXN,0)) FROM #CTQDocs),0)
        ELSE 0 END
FROM #CTQDocs
GROUP BY COALESCE(NULLIF(LTRIM(RTRIM(CentroCosto)), ''), 'Sin centro de costo')
ORDER BY TotalMXN DESC;

/* 11) Almacenes */
SELECT TOP (10)
    Nombre = COALESCE(NULLIF(LTRIM(RTRIM(AlmacenDocumento)), ''), 'Sin almacen'),
    Documentos = COUNT(1),
    TotalMXN = COALESCE(SUM(COALESCE(TotalCalculadoMXN,0)),0),
    TotalUSD = CASE WHEN @TipoCambioUSD > 0 THEN COALESCE(SUM(COALESCE(TotalCalculadoMXN,0)),0) / @TipoCambioUSD ELSE 0 END,
    Porcentaje = CASE
        WHEN (SELECT SUM(COALESCE(TotalCalculadoMXN,0)) FROM #CTQDocs) > 0
        THEN COALESCE(SUM(COALESCE(TotalCalculadoMXN,0)),0) * 100.0 /
             NULLIF((SELECT SUM(COALESCE(TotalCalculadoMXN,0)) FROM #CTQDocs),0)
        ELSE 0 END
FROM #CTQDocs
GROUP BY COALESCE(NULLIF(LTRIM(RTRIM(AlmacenDocumento)), ''), 'Sin almacen')
ORDER BY TotalMXN DESC;

/* 12) Listado paginado */
SELECT
    EmpresaOrigenID,
    CompraID,
    CompraClave,
    FechaCompra,
    FechaEntregaDocumento,
    FolioCompra,
    TituloCompra,
    Comentarios,
    CodigoProductoResumen,
    ProductoResumen,
    TotalPartidas,

    ProveedorIDTexto,
    Proveedor,
    RFCProveedor,

    TotalCompra,
    Moneda,
    SimboloMoneda,
    TipoCambio,
    FactorEfectivoMXN,
    TotalCompraMXN = TotalCalculadoMXN,
    TotalCompraUSD = CASE WHEN @TipoCambioUSD > 0 THEN TotalCalculadoMXN / @TipoCambioUSD ELSE NULL END,

    CondicionPago,
    TotalPagado,
    TotalPagadoMXN = PagadoCalculadoMXN,
    TotalPagadoUSD = CASE WHEN @TipoCambioUSD > 0 THEN PagadoCalculadoMXN / @TipoCambioUSD ELSE NULL END,
    SaldoPendiente,
    SaldoPendienteMXN = SaldoCalculadoMXN,
    SaldoPendienteUSD = CASE WHEN @TipoCambioUSD > 0 THEN SaldoCalculadoMXN / @TipoCambioUSD ELSE NULL END,
    FechaUltimoPago,

    UUID,
    TieneXMLTexto,
    TienePDFTexto,
    AlmacenDocumento,
    CentroCosto,
    EstatusCompra,
    UltimoCambioConocido
FROM #CTQDocs
ORDER BY FechaCompra DESC, CompraID DESC
OFFSET @Offset ROWS FETCH NEXT @TamanoPagina ROWS ONLY;
";

                using var command = new SqlCommand(sql, connection)
                {
                    CommandTimeout = ContpaqiCommandTimeoutSeconds
                };

                AgregarParametrosContpaqi(command, filtros);
                command.Parameters.Add("@Offset", SqlDbType.Int).Value =
                    (filtros.Pagina - 1) * filtros.TamanoPagina;
                command.Parameters.Add("@TamanoPagina", SqlDbType.Int).Value =
                    filtros.TamanoPagina;

                using var reader = await command.ExecuteReaderAsync();

                // 1) KPIs
                if (await reader.ReadAsync())
                {
                    model.TotalDocumentos = CtqInt(reader, "TotalDocumentos");
                    model.ProveedoresUnicos = CtqInt(reader, "ProveedoresUnicos");
                    model.DocumentosConSaldo = CtqInt(reader, "DocumentosConSaldo");
                    model.DocumentosCancelados = CtqInt(reader, "DocumentosCancelados");
                    model.DocumentosSinXML = CtqInt(reader, "DocumentosSinXML");
                    model.DocumentosSinPDF = CtqInt(reader, "DocumentosSinPDF");

                    model.TotalCompraMXN = CtqDecimal(reader, "TotalCompraMXN");
                    model.TotalCompraUSD = CtqNullableDecimal(reader, "TotalCompraUSD");
                    model.TotalPagadoMXN = CtqDecimal(reader, "TotalPagadoMXN");
                    model.TotalPagadoUSD = CtqNullableDecimal(reader, "TotalPagadoUSD");
                    model.SaldoPendienteMXN = CtqDecimal(reader, "SaldoPendienteMXN");
                    model.SaldoPendienteUSD = CtqNullableDecimal(reader, "SaldoPendienteUSD");
                    model.TicketPromedioMXN = CtqDecimal(reader, "TicketPromedioMXN");

                    model.TipoCambioUSDReferencia = CtqNullableDecimal(reader, "TipoCambioUSDReferencia");
                    model.FechaTipoCambioUSDReferencia = CtqNullableDateTime(reader, "FechaTipoCambioUSDReferencia");
                    model.FuenteTipoCambio = model.TipoCambioUSDReferencia.HasValue
                        ? "Referencia automatica derivada del ultimo documento USD disponible en CONTPAQ para el periodo o anterior."
                        : "CONTPAQ no proporciono una referencia USD utilizable para este periodo.";
                }

                // 2-6 Catalogos
                await LeerCatalogoAsync(reader, model.Proveedores, "Proveedor");
                await LeerCatalogoAsync(reader, model.Monedas, "Moneda");
                await LeerCatalogoAsync(reader, model.CentrosCosto, "CentroCosto");
                await LeerCatalogoAsync(reader, model.Almacenes, "AlmacenDocumento");
                await LeerCatalogoAsync(reader, model.EstatusDisponibles, "EstatusCompra");

                // 7-11 Resumenes
                await LeerResumenAsync(reader, model.TopProveedores);
                await LeerResumenAsync(reader, model.ResumenEstatus);
                await LeerResumenAsync(reader, model.ResumenMoneda);
                await LeerResumenAsync(reader, model.ResumenCentroCosto);
                await LeerResumenAsync(reader, model.ResumenAlmacen);

                // 12 Listado
                if (await reader.NextResultAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        model.Documentos.Add(MapearDocumentoContpaqi(reader, false));
                    }
                }
            }
            catch (SqlException ex)
            {
                model.ErrorMessage =
                    "No fue posible consultar CONTPAQ en este momento. " +
                    (string.Equals(_environment.EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase)
                        ? "Detalle tecnico: " + ex.Message
                        : "Verifica la disponibilidad del origen y vuelve a intentar.");
            }
            catch (TimeoutException)
            {
                model.ErrorMessage = "La consulta a CONTPAQ excedio el tiempo permitido. Reduce el periodo o aplica filtros.";
            }
            catch (Exception ex)
            {
                model.ErrorMessage =
                    "CONTPAQ respondio, pero el ERP no pudo procesar correctamente la respuesta." +
                    (string.Equals(_environment.EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase)
                        ? " Detalle tecnico: " + ex.GetType().Name + " - " + ex.Message
                        : "");
            }

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> ContpaqiDetalle(
            int empresaOrigenId,
            int compraId,
            string? returnQueryString = null)
        {
            if (!await TieneAccesoDepartamentoAsync("Compras"))
            {
                TempData["Error"] = "Tu usuario no tiene acceso a Compras CONTPAQ.";
                return RedirectToAction(nameof(Index));
            }

            if (empresaOrigenId <= 0 || compraId <= 0)
            {
                TempData["Error"] = "La compra solicitada no es valida.";
                return RedirectToAction(nameof(Contpaqi));
            }

            var model = new ComprasContpaqiDetalleViewModel
            {
                ReturnQueryString = NormalizarReturnQueryString(returnQueryString)
            };

            try
            {
                using var connection = new SqlConnection(GetConnectionString());
                await connection.OpenAsync();

                var sql = @"
SET NOCOUNT ON;

SELECT
    EmpresaOrigenID,
    CompraID,
    PartidaID,
    CompraClave,
    PartidaClave,

    FechaCompra,
    FechaEntregaDocumento,
    CONVERT(nvarchar(100), FolioCompra) AS FolioCompra,
    TituloCompra,
    Comentarios,

    CONVERT(nvarchar(100), ProveedorID) AS ProveedorIDTexto,
    Proveedor,
    RFCProveedor,
    TelefonoProveedor,
    EmailProveedor,
    WebProveedor,
    EstadoProveedor,

    NumeroPartida,
    FechaPartida,
    FechaItem,

    CONVERT(nvarchar(100), ProductoID) AS ProductoIDTexto,
    CodigoProducto,
    CodigoProductoProveedor,
    CodigoEAN13,
    Producto,
    Unidad,
    ClaveUnidadSAT,

    Cantidad,
    CantidadInventario,
    PrecioUnitario,
    CostoUnitario,
    ImportePartida,
    PorcentajeDescuento,

    PorcentajeIVA,
    PorcentajeRetencion,
    PorcentajeIEPS,
    ImporteIEPS,
    BaseIVA,
    IVAImportacion,
    PorcentajeImpuestoPais,
    ObjetoImpuestoSAT,

    SubtotalCompra,
    DescuentoCompra,
    ImpuestosCompra,
    RetencionesCompra,
    CostoTotalCompra,
    TotalCompra,

    Moneda,
    SimboloMoneda,
    TipoCambio,
    TotalCompraMXN,

    CondicionPago,
    TotalPagado,
    SaldoPendiente,
    FechaUltimoPago,

    UUID,
    CONVERT(nvarchar(30), TieneXML) AS TieneXMLTexto,
    CONVERT(nvarchar(30), TienePDF) AS TienePDFTexto,

    AlmacenDocumento,
    CONVERT(nvarchar(100), AlmacenID) AS AlmacenIDTexto,
    CentroCosto,
    CONVERT(nvarchar(100), CentroCostoID) AS CentroCostoIDTexto,

    Lote,
    NumeroSerie,
    NumeroSerieHasta,
    FechaCaducidad,
    Rack,
    Posicion,
    CONVERT(nvarchar(100), Nivel) AS Nivel,

    CONVERT(nvarchar(100), DocumentoOrigenID) AS DocumentoOrigenID,
    CONVERT(nvarchar(100), PartidaOrigenID) AS PartidaOrigenID,
    CONVERT(nvarchar(100), PartidaEntregaID) AS PartidaEntregaID,
    CONVERT(nvarchar(100), PartidaDestinoID) AS PartidaDestinoID,
    CONVERT(nvarchar(100), ProveedorPartidaID) AS ProveedorPartidaID,
    OrdenCompraExterna,
    CONVERT(nvarchar(200), Orden) AS Orden,
    CONVERT(nvarchar(200), Pedido) AS Pedido,

    CONVERT(nvarchar(30), Impreso) AS ImpresoTexto,
    CONVERT(nvarchar(30), Validado) AS ValidadoTexto,
    CONVERT(nvarchar(30), Cancelado) AS CanceladoTexto,
    CONVERT(nvarchar(30), Eliminado) AS EliminadoTexto,
    CONVERT(nvarchar(30), EnUso) AS EnUsoTexto,
    EstatusCompra,

    CONVERT(nvarchar(30), PartidaCancelada) AS PartidaCanceladaTexto,
    CONVERT(nvarchar(30), PartidaEliminada) AS PartidaEliminadaTexto,
    PartidaEliminadaEn,

    DocumentoCreadoEn,
    CONVERT(nvarchar(200), DocumentoCreadoPor) AS DocumentoCreadoPor,
    DocumentoValidadoEn,
    CONVERT(nvarchar(200), DocumentoValidadoPor) AS DocumentoValidadoPor,
    PartidaValidadaEn,
    CONVERT(nvarchar(200), PartidaValidadaPor) AS PartidaValidadaPor,
    PartidaSincronizadaEn,
    UltimoCambioConocido,

    PeriodoMes,
    PeriodoSemana,
    PeriodoAnio,
    PeriodoTrimestre,

    CONVERT(nvarchar(200), PolizaDiario) AS PolizaDiario,
    CONVERT(nvarchar(200), PolizaIngreso) AS PolizaIngreso,
    CONVERT(nvarchar(200), PolizaEgreso) AS PolizaEgreso,
    CONVERT(nvarchar(200), PolizaTotal) AS PolizaTotal,
    CONVERT(nvarchar(200), PolizasGeneradas) AS PolizasGeneradas

FROM [LINK CONTPAQ].[CSPGRUPOQUELL].[dbo].[ERP_Compras]
WHERE EmpresaOrigenID = @EmpresaOrigenID
  AND CompraID = @CompraID
ORDER BY NumeroPartida, PartidaID;
";

                using var command = new SqlCommand(sql, connection)
                {
                    CommandTimeout = ContpaqiCommandTimeoutSeconds
                };

                command.Parameters.Add("@EmpresaOrigenID", SqlDbType.Int).Value = empresaOrigenId;
                command.Parameters.Add("@CompraID", SqlDbType.Int).Value = compraId;

                using var reader = await command.ExecuteReaderAsync();

                var first = true;

                while (await reader.ReadAsync())
                {
                    if (first)
                    {
                        model.Documento = MapearDocumentoContpaqi(reader, true);

                        var factor = CalcularFactorEfectivoMXN(
                            model.Documento.Moneda,
                            model.Documento.TotalCompra,
                            model.Documento.TotalCompraMXN,
                            model.Documento.TipoCambio);

                        model.Documento.FactorEfectivoMXN = factor;

                        if (factor.HasValue)
                        {
                            model.Documento.TotalPagadoMXN =
                                model.Documento.TotalPagado.HasValue
                                    ? model.Documento.TotalPagado.Value * factor.Value
                                    : null;

                            model.Documento.SaldoPendienteMXN =
                                model.Documento.SaldoPendiente.HasValue
                                    ? model.Documento.SaldoPendiente.Value * factor.Value
                                    : null;
                        }

                        first = false;
                    }

                    model.Partidas.Add(new ComprasContpaqiPartidaViewModel
                    {
                        PartidaID = CtqInt(reader, "PartidaID"),
                        PartidaClave = CtqString(reader, "PartidaClave"),
                        NumeroPartida = CtqNullableInt(reader, "NumeroPartida"),
                        FechaPartida = CtqNullableDateTime(reader, "FechaPartida"),
                        FechaItem = CtqNullableDateTime(reader, "FechaItem"),

                        ProductoIDTexto = CtqString(reader, "ProductoIDTexto"),
                        CodigoProducto = CtqString(reader, "CodigoProducto"),
                        CodigoProductoProveedor = CtqString(reader, "CodigoProductoProveedor"),
                        CodigoEAN13 = CtqString(reader, "CodigoEAN13"),
                        Producto = CtqString(reader, "Producto"),
                        Unidad = CtqString(reader, "Unidad"),
                        ClaveUnidadSAT = CtqString(reader, "ClaveUnidadSAT"),

                        Cantidad = CtqNullableDecimal(reader, "Cantidad"),
                        CantidadInventario = CtqNullableDecimal(reader, "CantidadInventario"),
                        PrecioUnitario = CtqNullableDecimal(reader, "PrecioUnitario"),
                        CostoUnitario = CtqNullableDecimal(reader, "CostoUnitario"),
                        ImportePartida = CtqNullableDecimal(reader, "ImportePartida"),
                        PorcentajeDescuento = CtqNullableDecimal(reader, "PorcentajeDescuento"),

                        PorcentajeIVA = CtqNullableDecimal(reader, "PorcentajeIVA"),
                        PorcentajeRetencion = CtqNullableDecimal(reader, "PorcentajeRetencion"),
                        PorcentajeIEPS = CtqNullableDecimal(reader, "PorcentajeIEPS"),
                        ImporteIEPS = CtqNullableDecimal(reader, "ImporteIEPS"),
                        BaseIVA = CtqNullableDecimal(reader, "BaseIVA"),
                        IVAImportacion = CtqNullableDecimal(reader, "IVAImportacion"),
                        PorcentajeImpuestoPais = CtqNullableDecimal(reader, "PorcentajeImpuestoPais"),
                        ObjetoImpuestoSAT = CtqString(reader, "ObjetoImpuestoSAT"),

                        AlmacenIDTexto = CtqString(reader, "AlmacenIDTexto"),
                        CentroCostoIDTexto = CtqString(reader, "CentroCostoIDTexto"),
                        Lote = CtqString(reader, "Lote"),
                        NumeroSerie = CtqString(reader, "NumeroSerie"),
                        NumeroSerieHasta = CtqString(reader, "NumeroSerieHasta"),
                        FechaCaducidad = CtqNullableDateTime(reader, "FechaCaducidad"),
                        Rack = CtqString(reader, "Rack"),
                        Posicion = CtqString(reader, "Posicion"),
                        Nivel = CtqString(reader, "Nivel"),

                        DocumentoOrigenID = CtqString(reader, "DocumentoOrigenID"),
                        PartidaOrigenID = CtqString(reader, "PartidaOrigenID"),
                        PartidaEntregaID = CtqString(reader, "PartidaEntregaID"),
                        PartidaDestinoID = CtqString(reader, "PartidaDestinoID"),
                        ProveedorPartidaID = CtqString(reader, "ProveedorPartidaID"),
                        OrdenCompraExterna = CtqString(reader, "OrdenCompraExterna"),
                        Orden = CtqString(reader, "Orden"),
                        Pedido = CtqString(reader, "Pedido"),

                        PartidaCancelada = CtqBool(reader, "PartidaCanceladaTexto"),
                        PartidaEliminada = CtqBool(reader, "PartidaEliminadaTexto"),
                        PartidaEliminadaEn = CtqNullableDateTime(reader, "PartidaEliminadaEn"),
                        PartidaValidadaEn = CtqNullableDateTime(reader, "PartidaValidadaEn"),
                        PartidaValidadaPor = CtqString(reader, "PartidaValidadaPor"),
                        PartidaSincronizadaEn = CtqNullableDateTime(reader, "PartidaSincronizadaEn")
                    });
                }

                if (first)
                {
                    TempData["Error"] = "No se encontro la compra solicitada en CONTPAQ.";
                    return RedirectToAction(nameof(Contpaqi));
                }
            }
            catch (SqlException ex)
            {
                model.ErrorMessage =
                    "No fue posible consultar el detalle de CONTPAQ." +
                    (string.Equals(_environment.EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase)
                        ? " Detalle tecnico: " + ex.Message
                        : "");
            }
            catch (TimeoutException)
            {
                model.ErrorMessage = "La consulta del detalle de CONTPAQ excedio el tiempo permitido.";
            }
            catch (Exception ex)
            {
                model.ErrorMessage =
                    "CONTPAQ respondio, pero el ERP no pudo procesar correctamente el detalle." +
                    (string.Equals(_environment.EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase)
                        ? " Detalle tecnico: " + ex.GetType().Name + " - " + ex.Message
                        : "");
            }

            return View(model);
        }

        private static async Task LeerCatalogoAsync(
            SqlDataReader reader,
            List<string> destino,
            string columna)
        {
            if (!await reader.NextResultAsync())
                return;

            while (await reader.ReadAsync())
            {
                var valor = CtqString(reader, columna);
                if (!string.IsNullOrWhiteSpace(valor))
                    destino.Add(valor);
            }
        }

        private static async Task LeerResumenAsync(
            SqlDataReader reader,
            List<ComprasContpaqiResumenItemViewModel> destino)
        {
            if (!await reader.NextResultAsync())
                return;

            while (await reader.ReadAsync())
            {
                destino.Add(new ComprasContpaqiResumenItemViewModel
                {
                    Nombre = CtqString(reader, "Nombre") ?? "Sin dato",
                    Documentos = CtqInt(reader, "Documentos"),
                    TotalMXN = CtqDecimal(reader, "TotalMXN"),
                    TotalUSD = CtqDecimal(reader, "TotalUSD"),
                    Porcentaje = CtqDecimal(reader, "Porcentaje")
                });
            }
        }

        private static void AgregarParametrosContpaqi(
            SqlCommand command,
            ComprasContpaqiFiltroViewModel filtros)
        {
            command.Parameters.Add("@Desde", SqlDbType.DateTime2).Value = filtros.Desde.Date;
            command.Parameters.Add("@HastaExclusiva", SqlDbType.DateTime2).Value = filtros.Hasta.Date.AddDays(1);

            AgregarParametroNullable(command, "@Buscar", SqlDbType.NVarChar, 200, filtros.Buscar);
            AgregarParametroNullable(command, "@Proveedor", SqlDbType.NVarChar, 300, filtros.Proveedor);
            AgregarParametroNullable(command, "@RFC", SqlDbType.NVarChar, 100, filtros.RFC);
            AgregarParametroNullable(command, "@Estatus", SqlDbType.NVarChar, 50, filtros.Estatus);
            AgregarParametroNullable(command, "@Moneda", SqlDbType.NVarChar, 100, filtros.Moneda);
            AgregarParametroNullable(command, "@CentroCosto", SqlDbType.NVarChar, 300, filtros.CentroCosto);
            AgregarParametroNullable(command, "@Almacen", SqlDbType.NVarChar, 300, filtros.Almacen);

            AgregarParametroNullable(command, "@BuscarLike", SqlDbType.NVarChar, 404, LikeContpaqi(filtros.Buscar));
            AgregarParametroNullable(command, "@RFCLike", SqlDbType.NVarChar, 204, LikeContpaqi(filtros.RFC));
        }

        private static void AgregarParametroNullable(
            SqlCommand command,
            string nombre,
            SqlDbType tipo,
            int tamano,
            string? valor)
        {
            var parameter = command.Parameters.Add(nombre, tipo, tamano);
            parameter.Value = string.IsNullOrWhiteSpace(valor) ? DBNull.Value : valor.Trim();
        }

        private static string? LimpiarFiltroContpaqi(string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor))
                return null;

            var limpio = valor.Trim();
            return limpio.Length <= 300 ? limpio : limpio.Substring(0, 300);
        }

        private static string? LikeContpaqi(string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor))
                return null;

            var escaped = valor
                .Trim()
                .Replace("[", "[[]")
                .Replace("%", "[%]")
                .Replace("_", "[_]");

            return "%" + escaped + "%";
        }

        private static string? NormalizarReturnQueryString(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var trimmed = value.Trim();
            return trimmed.StartsWith("?", StringComparison.Ordinal) && trimmed.Length <= 1800
                ? trimmed
                : null;
        }

        private static decimal? CalcularFactorEfectivoMXN(
            string? moneda,
            decimal? totalOriginal,
            decimal? totalMxn,
            decimal? tipoCambio)
        {
            if (totalOriginal.HasValue &&
                totalMxn.HasValue &&
                Math.Abs(totalOriginal.Value) > 0.000001m)
            {
                return Math.Abs(totalMxn.Value / totalOriginal.Value);
            }

            if (EsMonedaMXN(moneda))
                return 1m;

            if (tipoCambio.HasValue && tipoCambio.Value > 0m)
                return Math.Abs(tipoCambio.Value);

            return null;
        }

        private static bool EsMonedaMXN(string? moneda)
        {
            var value = (moneda ?? string.Empty).Trim().ToUpperInvariant();

            return value == "MXN" ||
                   value == "PESO MEXICANO" ||
                   value == "PESOS MEXICANOS" ||
                   value == "PESO" ||
                   value == "PESOS";
        }

        // Lectores tolerantes a los tipos expuestos por el Linked Server.
        private static object? CtqValue(SqlDataReader reader, string column)
        {
            try
            {
                var ordinal = reader.GetOrdinal(column);
                return reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal);
            }
            catch (IndexOutOfRangeException)
            {
                return null;
            }
        }

        private static string? CtqString(SqlDataReader reader, string column)
        {
            var value = CtqValue(reader, column);
            return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static int CtqInt(SqlDataReader reader, string column)
        {
            var value = CtqValue(reader, column);
            if (value == null) return 0;

            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                var text = Convert.ToString(value, CultureInfo.InvariantCulture);
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) &&
                    n >= int.MinValue && n <= int.MaxValue)
                    return (int)n;

                return 0;
            }
        }

        private static int? CtqNullableInt(SqlDataReader reader, string column)
        {
            var value = CtqValue(reader, column);
            if (value == null) return null;

            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                var text = Convert.ToString(value, CultureInfo.InvariantCulture);
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) &&
                    n >= int.MinValue && n <= int.MaxValue)
                    return (int)n;

                return null;
            }
        }

        private static decimal CtqDecimal(SqlDataReader reader, string column)
        {
            return CtqNullableDecimal(reader, column) ?? 0m;
        }

        private static decimal? CtqNullableDecimal(SqlDataReader reader, string column)
        {
            var value = CtqValue(reader, column);
            if (value == null) return null;

            try
            {
                return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                var text = Convert.ToString(value, CultureInfo.InvariantCulture);
                return decimal.TryParse(
                    text,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out var n)
                    ? n
                    : null;
            }
        }

        private static DateTime CtqDateTime(SqlDataReader reader, string column)
        {
            return CtqNullableDateTime(reader, column) ?? DateTime.MinValue;
        }

        private static DateTime? CtqNullableDateTime(SqlDataReader reader, string column)
        {
            var value = CtqValue(reader, column);
            if (value == null) return null;

            if (value is DateTime dt) return dt;
            if (value is DateTimeOffset dto) return dto.DateTime;

            try
            {
                return Convert.ToDateTime(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                var text = Convert.ToString(value, CultureInfo.InvariantCulture);

                return DateTime.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out var parsed)
                    ? parsed
                    : null;
            }
        }

        private static bool CtqBool(SqlDataReader reader, string column)
        {
            var value = CtqValue(reader, column);
            if (value == null) return false;

            if (value is bool b) return b;

            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture) != 0;
            }
            catch
            {
                var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
                if (string.IsNullOrWhiteSpace(text)) return false;

                return text.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                       text.Equals("si", StringComparison.OrdinalIgnoreCase) ||
                       text.Equals("sí", StringComparison.OrdinalIgnoreCase) ||
                       text.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                       text.Equals("1", StringComparison.OrdinalIgnoreCase);
            }
        }

        private static ComprasContpaqiDocumentoViewModel MapearDocumentoContpaqi(
            SqlDataReader reader,
            bool incluirDatosExtendidos)
        {
            var item = new ComprasContpaqiDocumentoViewModel
            {
                EmpresaOrigenID = CtqInt(reader, "EmpresaOrigenID"),
                CompraID = CtqInt(reader, "CompraID"),
                CompraClave = CtqString(reader, "CompraClave"),
                FechaCompra = CtqDateTime(reader, "FechaCompra"),
                FechaEntregaDocumento = CtqNullableDateTime(reader, "FechaEntregaDocumento"),
                FolioCompra = CtqString(reader, "FolioCompra"),
                TituloCompra = CtqString(reader, "TituloCompra"),
                Comentarios = CtqString(reader, "Comentarios"),
                CodigoProductoResumen = CtqString(reader, "CodigoProductoResumen"),
                ProductoResumen = CtqString(reader, "ProductoResumen"),
                TotalPartidas = CtqInt(reader, "TotalPartidas"),

                ProveedorIDTexto = CtqString(reader, "ProveedorIDTexto"),
                Proveedor = CtqString(reader, "Proveedor"),
                RFCProveedor = CtqString(reader, "RFCProveedor"),

                TotalCompra = CtqNullableDecimal(reader, "TotalCompra"),
                Moneda = CtqString(reader, "Moneda"),
                SimboloMoneda = CtqString(reader, "SimboloMoneda"),
                TipoCambio = CtqNullableDecimal(reader, "TipoCambio"),
                FactorEfectivoMXN = CtqNullableDecimal(reader, "FactorEfectivoMXN"),
                TotalCompraMXN = CtqNullableDecimal(reader, "TotalCompraMXN"),
                TotalCompraUSD = CtqNullableDecimal(reader, "TotalCompraUSD"),

                CondicionPago = CtqString(reader, "CondicionPago"),
                TotalPagado = CtqNullableDecimal(reader, "TotalPagado"),
                TotalPagadoMXN = CtqNullableDecimal(reader, "TotalPagadoMXN"),
                TotalPagadoUSD = CtqNullableDecimal(reader, "TotalPagadoUSD"),
                SaldoPendiente = CtqNullableDecimal(reader, "SaldoPendiente"),
                SaldoPendienteMXN = CtqNullableDecimal(reader, "SaldoPendienteMXN"),
                SaldoPendienteUSD = CtqNullableDecimal(reader, "SaldoPendienteUSD"),
                FechaUltimoPago = CtqNullableDateTime(reader, "FechaUltimoPago"),

                UUID = CtqString(reader, "UUID"),
                TieneXML = CtqBool(reader, "TieneXMLTexto"),
                TienePDF = CtqBool(reader, "TienePDFTexto"),

                AlmacenDocumento = CtqString(reader, "AlmacenDocumento"),
                CentroCosto = CtqString(reader, "CentroCosto"),
                EstatusCompra = CtqString(reader, "EstatusCompra"),
                UltimoCambioConocido = CtqNullableDateTime(reader, "UltimoCambioConocido")
            };

            if (incluirDatosExtendidos)
            {
                item.TelefonoProveedor = CtqString(reader, "TelefonoProveedor");
                item.EmailProveedor = CtqString(reader, "EmailProveedor");
                item.WebProveedor = CtqString(reader, "WebProveedor");
                item.EstadoProveedor = CtqString(reader, "EstadoProveedor");

                item.SubtotalCompra = CtqNullableDecimal(reader, "SubtotalCompra");
                item.DescuentoCompra = CtqNullableDecimal(reader, "DescuentoCompra");
                item.ImpuestosCompra = CtqNullableDecimal(reader, "ImpuestosCompra");
                item.RetencionesCompra = CtqNullableDecimal(reader, "RetencionesCompra");
                item.CostoTotalCompra = CtqNullableDecimal(reader, "CostoTotalCompra");

                item.Impreso = CtqBool(reader, "ImpresoTexto");
                item.Validado = CtqBool(reader, "ValidadoTexto");
                item.Cancelado = CtqBool(reader, "CanceladoTexto");
                item.Eliminado = CtqBool(reader, "EliminadoTexto");
                item.EnUso = CtqBool(reader, "EnUsoTexto");

                item.DocumentoCreadoEn = CtqNullableDateTime(reader, "DocumentoCreadoEn");
                item.DocumentoCreadoPor = CtqString(reader, "DocumentoCreadoPor");
                item.DocumentoValidadoEn = CtqNullableDateTime(reader, "DocumentoValidadoEn");
                item.DocumentoValidadoPor = CtqString(reader, "DocumentoValidadoPor");

                item.PeriodoMes = CtqNullableInt(reader, "PeriodoMes");
                item.PeriodoSemana = CtqNullableInt(reader, "PeriodoSemana");
                item.PeriodoAnio = CtqNullableInt(reader, "PeriodoAnio");
                item.PeriodoTrimestre = CtqNullableInt(reader, "PeriodoTrimestre");

                item.PolizaDiario = CtqString(reader, "PolizaDiario");
                item.PolizaIngreso = CtqString(reader, "PolizaIngreso");
                item.PolizaEgreso = CtqString(reader, "PolizaEgreso");
                item.PolizaTotal = CtqString(reader, "PolizaTotal");
                item.PolizasGeneradas = CtqString(reader, "PolizasGeneradas");
            }

            return item;
        }
    }
}