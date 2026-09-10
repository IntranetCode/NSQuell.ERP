using ERP.NSQuell.Models;
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
        private static CtqPeriodoFiltroVm CtqV15CrearFiltro(
            DateTime? desde,
            DateTime? hasta,
            string? buscar,
            string? proveedor,
            string? rfc,
            string? estatus,
            string? moneda,
            string? centroCosto,
            string? almacen,
            int pagina,
            int tamanoPagina)
        {
            var hoy = DateTime.Today;
            DateTime d;
            DateTime h;

            if (!desde.HasValue && !hasta.HasValue)
            {
                d = new DateTime(hoy.Year, hoy.Month, 1);
                h = d.AddMonths(1).AddDays(-1);
            }
            else if (desde.HasValue && !hasta.HasValue)
            {
                d = desde.Value.Date;
                h = new DateTime(d.Year, d.Month, 1).AddMonths(1).AddDays(-1);
            }
            else if (!desde.HasValue && hasta.HasValue)
            {
                h = hasta.Value.Date;
                d = new DateTime(h.Year, h.Month, 1);
            }
            else
            {
                d = desde!.Value.Date;
                h = hasta!.Value.Date;
            }

            CtqV15NormalizarRango(ref d, ref h, 36);

            return new CtqPeriodoFiltroVm
            {
                Desde = d,
                Hasta = h,
                Buscar = CtqV15Clean(buscar),
                Proveedor = CtqV15Clean(proveedor),
                RFC = CtqV15Clean(rfc),
                Estatus = CtqV15Clean(estatus),
                Moneda = CtqV15Clean(moneda),
                CentroCosto = CtqV15Clean(centroCosto),
                Almacen = CtqV15Clean(almacen),
                Pagina = Math.Max(1, pagina),
                TamanoPagina = Math.Min(100, Math.Max(10, tamanoPagina))
            };
        }

        private static void CtqV15NormalizarRango(ref DateTime desde, ref DateTime hasta, int maxMeses)
        {
            desde = desde.Date;
            hasta = hasta.Date;
            if (desde > hasta)
            {
                var t = desde;
                desde = hasta;
                hasta = t;
            }

            var limite = desde.AddMonths(maxMeses).AddDays(-1);
            if (hasta > limite)
                hasta = limite;
        }

        private static string? CtqV15Clean(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var x = value.Trim();
            return x.Length <= 300 ? x : x.Substring(0, 300);
        }

        private static string? CtqV15Like(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var x = value.Trim()
                .Replace("[", "[[]")
                .Replace("%", "[%]")
                .Replace("_", "[_]");
            return "%" + x + "%";
        }

        private static void CtqV15AddFilterParameters(SqlCommand cmd, CtqPeriodoFiltroVm f)
        {
            cmd.Parameters.Add("@Desde", SqlDbType.DateTime2).Value = f.Desde.Date;
            cmd.Parameters.Add("@HastaExclusiva", SqlDbType.DateTime2).Value = f.Hasta.Date.AddDays(1);
            CtqV15AddText(cmd, "@Buscar", f.Buscar, 300);
            CtqV15AddText(cmd, "@BuscarLike", CtqV15Like(f.Buscar), 700);
            CtqV15AddText(cmd, "@Proveedor", f.Proveedor, 300);
            CtqV15AddText(cmd, "@RFC", f.RFC, 100);
            CtqV15AddText(cmd, "@RFCLike", CtqV15Like(f.RFC), 250);
            CtqV15AddText(cmd, "@Estatus", f.Estatus, 100);
            CtqV15AddText(cmd, "@Moneda", f.Moneda, 100);
            CtqV15AddText(cmd, "@CentroCosto", f.CentroCosto, 300);
            CtqV15AddText(cmd, "@Almacen", f.Almacen, 300);
        }

        private static void CtqV15AddText(SqlCommand cmd, string name, string? value, int size)
        {
            var p = cmd.Parameters.Add(name, SqlDbType.NVarChar, size);
            p.Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
        }

        private async Task CtqV15CargarCatalogosAsync(SqlConnection cn, CtqPeriodoFiltroVm f)
        {
            var sql = @"
SET NOCOUNT ON;
SELECT DISTINCT TOP (500) Proveedor
FROM [LINK CONTPAQ].[CSPGRUPOQUELL].[dbo].[ERP_Compras]
WHERE FechaCompra >= @Desde AND FechaCompra < @HastaExclusiva
  AND NULLIF(LTRIM(RTRIM(Proveedor)), '') IS NOT NULL
ORDER BY Proveedor;

SELECT DISTINCT TOP (200) EstatusCompra
FROM [LINK CONTPAQ].[CSPGRUPOQUELL].[dbo].[ERP_Compras]
WHERE FechaCompra >= @Desde AND FechaCompra < @HastaExclusiva
  AND NULLIF(LTRIM(RTRIM(EstatusCompra)), '') IS NOT NULL
ORDER BY EstatusCompra;

SELECT DISTINCT TOP (100) Moneda
FROM [LINK CONTPAQ].[CSPGRUPOQUELL].[dbo].[ERP_Compras]
WHERE FechaCompra >= @Desde AND FechaCompra < @HastaExclusiva
  AND NULLIF(LTRIM(RTRIM(Moneda)), '') IS NOT NULL
ORDER BY Moneda;

SELECT DISTINCT TOP (500) CentroCosto
FROM [LINK CONTPAQ].[CSPGRUPOQUELL].[dbo].[ERP_Compras]
WHERE FechaCompra >= @Desde AND FechaCompra < @HastaExclusiva
  AND NULLIF(LTRIM(RTRIM(CentroCosto)), '') IS NOT NULL
ORDER BY CentroCosto;

SELECT DISTINCT TOP (500) AlmacenDocumento
FROM [LINK CONTPAQ].[CSPGRUPOQUELL].[dbo].[ERP_Compras]
WHERE FechaCompra >= @Desde AND FechaCompra < @HastaExclusiva
  AND NULLIF(LTRIM(RTRIM(AlmacenDocumento)), '') IS NOT NULL
ORDER BY AlmacenDocumento;";

            using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 };
            cmd.Parameters.Add("@Desde", SqlDbType.DateTime2).Value = f.Desde.Date;
            cmd.Parameters.Add("@HastaExclusiva", SqlDbType.DateTime2).Value = f.Hasta.Date.AddDays(1);
            using var r = await cmd.ExecuteReaderAsync();

            async Task ReadListAsync(List<string> target, string column)
            {
                while (await r.ReadAsync())
                {
                    var value = CtqV15String(r, column);
                    if (!string.IsNullOrWhiteSpace(value)) target.Add(value);
                }
            }

            await ReadListAsync(f.Proveedores, "Proveedor");
            if (await r.NextResultAsync()) await ReadListAsync(f.EstatusDisponibles, "EstatusCompra");
            if (await r.NextResultAsync()) await ReadListAsync(f.Monedas, "Moneda");
            if (await r.NextResultAsync()) await ReadListAsync(f.CentrosCosto, "CentroCosto");
            if (await r.NextResultAsync()) await ReadListAsync(f.Almacenes, "AlmacenDocumento");
        }

        private const string CtqV15DocumentoTempSql = @"
SET NOCOUNT ON;
IF OBJECT_ID('tempdb..#CTQDoc') IS NOT NULL DROP TABLE #CTQDoc;
IF OBJECT_ID('tempdb..#CTQDocBase') IS NOT NULL DROP TABLE #CTQDocBase;

;WITH X AS
(
    SELECT
        EmpresaOrigenID,
        CompraID,
        CompraClave,
        FechaCompra,
        FechaEntregaDocumento,
        CONVERT(nvarchar(100), FolioCompra) AS FolioCompra,
        Proveedor,
        RFCProveedor,
        TotalCompra,
        TotalCompraMXN,
        TotalPagado,
        SaldoPendiente,
        Moneda,
        TipoCambio,
        CondicionPago,
        FechaUltimoPago,
        UUID,
        CONVERT(nvarchar(30), TieneXML) AS TieneXMLTexto,
        CONVERT(nvarchar(30), TienePDF) AS TienePDFTexto,
        AlmacenDocumento,
        CentroCosto,
        EstatusCompra,
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
    *,
    FactorMXN =
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
INTO #CTQDocBase
FROM X
WHERE rn = 1
  AND (@Buscar IS NULL
       OR Proveedor LIKE @BuscarLike
       OR FolioCompra LIKE @BuscarLike
       OR RFCProveedor LIKE @BuscarLike
       OR UUID LIKE @BuscarLike
       OR CompraClave LIKE @BuscarLike)
  AND (@Proveedor IS NULL OR Proveedor = @Proveedor)
  AND (@RFC IS NULL OR RFCProveedor LIKE @RFCLike)
  AND (@Estatus IS NULL OR EstatusCompra = @Estatus)
  AND (@Moneda IS NULL OR Moneda = @Moneda)
  AND (@CentroCosto IS NULL OR CentroCosto = @CentroCosto)
  AND (@Almacen IS NULL OR AlmacenDocumento = @Almacen);

SELECT
    *,
    TotalMxn = COALESCE(
        TRY_CONVERT(decimal(38,6), TotalCompraMXN),
        TRY_CONVERT(decimal(38,6), TotalCompra) * TRY_CONVERT(decimal(38,10), FactorMXN),
        CONVERT(decimal(38,6), 0)),
    PagadoMxn = COALESCE(
        TRY_CONVERT(decimal(38,6), TotalPagado) * TRY_CONVERT(decimal(38,10), FactorMXN),
        CONVERT(decimal(38,6), 0)),
    SaldoMxn = COALESCE(
        TRY_CONVERT(decimal(38,6), SaldoPendiente) * TRY_CONVERT(decimal(38,10), FactorMXN),
        CONVERT(decimal(38,6), 0)),
    TieneXmlBit = CASE
        WHEN UPPER(LTRIM(RTRIM(ISNULL(TieneXMLTexto,'')))) IN ('1','TRUE','SI','SÍ','YES') THEN 1 ELSE 0 END,
    TienePdfBit = CASE
        WHEN UPPER(LTRIM(RTRIM(ISNULL(TienePDFTexto,'')))) IN ('1','TRUE','SI','SÍ','YES') THEN 1 ELSE 0 END
INTO #CTQDoc
FROM #CTQDocBase;
";

        private const string CtqV15TipoCambioUsdSql = @"
DECLARE @TipoCambioUsd decimal(38,10) = NULL;
DECLARE @FechaTipoCambioUsd datetime2 = NULL;

SELECT TOP (1)
    @TipoCambioUsd = FactorMXN,
    @FechaTipoCambioUsd = FechaCompra
FROM #CTQDoc
WHERE UPPER(LTRIM(RTRIM(ISNULL(Moneda,'')))) IN
      ('USD','US DOLAR','US DÓLAR','DOLAR','DÓLAR','DOLARES','DÓLARES','DOLLAR','US DOLLAR')
  AND FactorMXN > 0
ORDER BY FechaCompra DESC, CompraID DESC;

IF @TipoCambioUsd IS NULL
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
          AND UPPER(LTRIM(RTRIM(ISNULL(Moneda,'')))) IN
              ('USD','US DOLAR','US DÓLAR','DOLAR','DÓLAR','DOLARES','DÓLARES','DOLLAR','US DOLLAR')
    )
    SELECT TOP (1)
        @TipoCambioUsd = CASE
            WHEN NULLIF(ABS(CONVERT(decimal(38,10), TotalCompra)),0) IS NOT NULL
             AND TotalCompraMXN IS NOT NULL
                THEN ABS(CONVERT(decimal(38,10), TotalCompraMXN) /
                         NULLIF(CONVERT(decimal(38,10), TotalCompra),0))
            WHEN TRY_CONVERT(decimal(38,10), TipoCambio) > 0
                THEN ABS(TRY_CONVERT(decimal(38,10), TipoCambio))
            ELSE NULL END,
        @FechaTipoCambioUsd = FechaCompra
    FROM FX
    WHERE rn = 1
    ORDER BY FechaCompra DESC, CompraID DESC;
END;
";

        private static object? CtqV15Value(SqlDataReader r, string name)
        {
            try
            {
                var i = r.GetOrdinal(name);
                return r.IsDBNull(i) ? null : r.GetValue(i);
            }
            catch (IndexOutOfRangeException)
            {
                return null;
            }
        }

        private static string? CtqV15String(SqlDataReader r, string name)
        {
            var v = CtqV15Value(r, name);
            return v == null ? null : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        private static int CtqV15Int(SqlDataReader r, string name)
        {
            var v = CtqV15Value(r, name);
            if (v == null) return 0;
            try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        private static decimal CtqV15Decimal(SqlDataReader r, string name)
        {
            var v = CtqV15Value(r, name);
            if (v == null) return 0m;
            try { return Convert.ToDecimal(v, CultureInfo.InvariantCulture); }
            catch { return 0m; }
        }

        private static decimal? CtqV15NullableDecimal(SqlDataReader r, string name)
        {
            var v = CtqV15Value(r, name);
            if (v == null) return null;
            try { return Convert.ToDecimal(v, CultureInfo.InvariantCulture); }
            catch { return null; }
        }

        private static DateTime? CtqV15Date(SqlDataReader r, string name)
        {
            var v = CtqV15Value(r, name);
            if (v == null) return null;
            if (v is DateTime dt) return dt;
            try { return Convert.ToDateTime(v, CultureInfo.InvariantCulture); }
            catch { return null; }
        }

        private static bool CtqV15Bool(SqlDataReader r, string name)
        {
            var v = CtqV15Value(r, name);
            if (v == null) return false;
            if (v is bool b) return b;
            try { return Convert.ToInt32(v, CultureInfo.InvariantCulture) != 0; }
            catch
            {
                var s = Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim();
                return string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s, "si", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s, "sí", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s, "yes", StringComparison.OrdinalIgnoreCase)
                    || s == "1";
            }
        }
    }
}
