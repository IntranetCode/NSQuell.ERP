using ExcelDataReader;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ERP.NSQuell.Servicios.Releases;

public enum ReleaseExcelTemplate
{
    Unknown = 0,
    GoldeWeeklyMatrix = 1,
    NormaWeeklyMatrix = 2,
    AirThermalMaterialRelease = 3,
    GoldeMexicoWeeklyMatrix = 4
}

public sealed class ReleaseExcelDocument
{
    public string TemplateCode { get; init; } = string.Empty;
    public string ClienteNombre { get; init; } = string.Empty;
    public string? FolioCliente { get; init; }
    public DateTime? DocumentDate { get; init; }
    public string VersionText { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public List<ReleaseExcelRow> Rows { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
}

public sealed class ReleaseExcelRow
{
    public string PartNumber { get; init; } = string.Empty;
    public string? PartDescription { get; init; }
    public string? SourceReference { get; init; }
    public string Uom { get; init; } = "PZA";
    public List<ReleaseExcelDelivery> Deliveries { get; init; } = new();
}

public sealed class ReleaseExcelDelivery
{
    public int Sequence { get; init; }
    public string? PeriodLabel { get; init; }
    public DateTime RequiredDate { get; init; }
    public int RequiredQuantity { get; init; }
    public bool IsBacklog { get; init; }
}

// RELEASE_EXCEL_PARSERS_GOLDE_NORMA_V1_4
public static class ReleaseExcelDocumentDetector
{
    public static ReleaseExcelTemplate Detect(byte[] bytes)
    {
        var workbook = ReadWorkbook(bytes);

        foreach (DataTable table in workbook.Tables)
        {
            if (LooksLikeAirThermal(table))
                return ReleaseExcelTemplate.AirThermalMaterialRelease;

            if (LooksLikeGolde(table))
                return ReleaseExcelTemplate.GoldeWeeklyMatrix;

            if (LooksLikeNorma(table))
                return ReleaseExcelTemplate.NormaWeeklyMatrix;
        }

        return ReleaseExcelTemplate.Unknown;
    }

    public static ReleaseExcelDocument ParseGoldeMexico(
        byte[] bytes,
        string? fileName = null)
    {
        return GoldeMexicoReleaseExcelParser.Parse(bytes, fileName);
    }
    public static ReleaseExcelDocument ParseGolde(
        byte[] bytes,
        string? fileName = null)
    {
        return GoldeReleaseExcelParser.Parse(bytes, fileName);
    }

    public static ReleaseExcelDocument ParseNorma(byte[] bytes)
    {
        var workbook = ReadWorkbook(bytes);
        var table = workbook.Tables.Cast<DataTable>()
            .Where(LooksLikeNorma)
            .Select(x => new
            {
                Table = x,
                HasDate = TryReadDate(Get(x, 1, 2), out var date),
                FirstDate = TryReadDate(Get(x, 1, 2), out var parsedDate)
                    ? parsedDate
                    : DateTime.MinValue
            })
            .Where(x => x.HasDate)
            .OrderByDescending(x => x.FirstDate)
            .Select(x => x.Table)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("No se encontro la matriz semanal NORMA dentro del Excel.");

        if (!TryReadDate(Get(table, 1, 2), out var firstDate))
            throw new InvalidOperationException("No se pudo leer la fecha inicial de la matriz NORMA.");

        var firstWeek = 0;
        TryInteger(Get(table, 0, 2), out firstWeek);
        if (firstWeek <= 0)
            firstWeek = ISOWeek.GetWeekOfYear(firstDate);

        var rows = new List<ReleaseExcelRow>();

        for (var row = 2; row < table.Rows.Count; row++)
        {
            var partNumber = CellText(Get(table, row, 1));
            if (string.IsNullOrWhiteSpace(partNumber))
                continue;

            var deliveries = new List<ReleaseExcelDelivery>();
            var sequence = 1;

            for (var column = 2; column < table.Columns.Count; column++)
            {
                if (!TryPositiveInteger(Get(table, row, column), out var quantity))
                    continue;

                var requiredDate = firstDate.AddDays((column - 2) * 7d);
                if (TryReadDate(Get(table, 1, column), out var explicitDate))
                    requiredDate = explicitDate;

                deliveries.Add(new ReleaseExcelDelivery
                {
                    Sequence = sequence++,
                    PeriodLabel = $"W{ISOWeek.GetWeekOfYear(requiredDate)}",
                    RequiredDate = requiredDate,
                    RequiredQuantity = quantity,
                    IsBacklog = requiredDate.Date < firstDate.Date
                });
            }

            if (deliveries.Count == 0)
                continue;

            var mold = CellText(Get(table, row, 0));
            rows.Add(new ReleaseExcelRow
            {
                PartNumber = partNumber,
                PartDescription = null,
                SourceReference = string.IsNullOrWhiteSpace(mold) ? null : $"Molde {mold}",
                Uom = "PZA",
                Deliveries = deliveries
            });
        }

        if (rows.Count == 0)
            throw new InvalidOperationException("El Excel NORMA no contiene cantidades positivas para importar.");

        return new ReleaseExcelDocument
        {
            TemplateCode = "NORMA_WEEKLY_RELEASE",
            ClienteNombre = "NORMA",
            FolioCliente = $"NORMA-W{firstWeek}-{firstDate:yyyy}",
            DocumentDate = firstDate,
            VersionText = $"W{firstWeek} / {firstDate:dd.MM.yyyy}",
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            Rows = rows,
            Warnings = new List<string>()
        };
    }

    // RELEASE_EXCEL_AIR_THERMAL_V1_8
    public static ReleaseExcelDocument ParseAirThermal(
        byte[] bytes,
        string? fileName)
    {
        var workbook = ReadWorkbook(bytes);
        var table = workbook.Tables.Cast<DataTable>()
            .FirstOrDefault(LooksLikeAirThermal)
            ?? throw new InvalidOperationException(
                "No se encontro la matriz Material Release de AIR THERMAL dentro del Excel.");

        if (!TryReadAirThermalDocumentDate(fileName, out var documentDate))
        {
            throw new InvalidOperationException(
                "No se pudo identificar la fecha del Material Release AIR THERMAL. " +
                "El nombre del archivo debe contener una fecha con formato yyyyMMdd, por ejemplo GM-20260710.xlsx.");
        }

        var customer = CellText(Get(table, 0, 2));
        var supplierNumber = CellText(Get(table, 2, 2));
        var folio = ExtractAirThermalFolio(fileName);

        if (string.IsNullOrWhiteSpace(folio))
            folio = $"AIRTHERMAL-{documentDate:yyyyMMdd}";

        var rows = new List<ReleaseExcelRow>();
        var warnings = new List<string>();

        for (var planRow = 5; planRow < table.Rows.Count; planRow++)
        {
            if (!CellText(Get(table, planRow, 3))
                .Equals("Plan PO", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (planRow + 4 >= table.Rows.Count)
                continue;

            var partNumber = CellText(Get(table, planRow, 1));
            var description = CellText(Get(table, planRow, 2));

            if (string.IsNullOrWhiteSpace(partNumber))
                continue;

            var poRow = planRow + 1;
            var balanceRow = planRow + 4;
            var datedColumns = new List<(int Column, DateTime Date)>();

            for (var column = 4; column < table.Columns.Count; column++)
            {
                if (TryReadDate(Get(table, 4, column), out var columnDate))
                    datedColumns.Add((column, columnDate.Date));
            }

            if (datedColumns.Count == 0)
                continue;

            var deliveries = new List<ReleaseExcelDelivery>();
            var sequence = 1;

            var latestHistorical = datedColumns
                .Where(x => x.Date <= documentDate.Date)
                .OrderByDescending(x => x.Date)
                .FirstOrDefault();

            if (latestHistorical.Column > 0 &&
                TryInteger(Get(table, balanceRow, latestHistorical.Column), out var balance) &&
                balance < 0)
            {
                var backlogQuantity = balance == int.MinValue
                    ? int.MaxValue
                    : Math.Abs(balance);

                deliveries.Add(new ReleaseExcelDelivery
                {
                    Sequence = sequence++,
                    PeriodLabel = "BACKLOG NETO",
                    RequiredDate = documentDate.Date,
                    RequiredQuantity = backlogQuantity,
                    IsBacklog = true
                });

                warnings.Add(
                    $"AIR THERMAL: la parte {partNumber} trae backlog neto de " +
                    $"{backlogQuantity:N0} pieza(s) al {documentDate:dd/MM/yyyy}; " +
                    "se registro como demanda vencida en la fecha del documento.");
            }

            foreach (var datedColumn in datedColumns
                .Where(x => x.Date > documentDate.Date)
                .OrderBy(x => x.Date))
            {
                if (!TryPositiveInteger(
                    Get(table, planRow, datedColumn.Column),
                    out var quantity))
                {
                    continue;
                }

                deliveries.Add(new ReleaseExcelDelivery
                {
                    Sequence = sequence++,
                    PeriodLabel = $"PLAN PO {datedColumn.Date:dd/MM/yyyy}",
                    RequiredDate = datedColumn.Date,
                    RequiredQuantity = quantity,
                    IsBacklog = false
                });
            }

            if (deliveries.Count == 0)
                continue;

            var purchaseOrder = FindAirThermalPurchaseOrder(
                table,
                planRow,
                poRow,
                documentDate,
                datedColumns);

            rows.Add(new ReleaseExcelRow
            {
                PartNumber = partNumber,
                PartDescription = description,
                SourceReference = string.IsNullOrWhiteSpace(purchaseOrder)
                    ? folio
                    : purchaseOrder,
                Uom = "PZA",
                Deliveries = deliveries
            });
        }

        if (rows.Count == 0)
        {
            throw new InvalidOperationException(
                "El Excel AIR THERMAL no contiene demanda futura Plan PO ni backlog neto para importar.");
        }

        warnings.Add(
            "AIR THERMAL: se importo exclusivamente la demanda operativa: backlog neto vigente y cantidades futuras del renglon Plan PO. " +
            "Los renglones PO, Qty ship, Invoice no. y Balance se conservaron en el archivo original como informacion de auditoria y no se duplicaron como demanda.");

        if (!string.IsNullOrWhiteSpace(supplierNumber))
            warnings.Add($"Supplier No. detectado: {supplierNumber}.");

        return new ReleaseExcelDocument
        {
            TemplateCode = "AIR_THERMAL_MATERIAL_RELEASE",
            ClienteNombre = string.IsNullOrWhiteSpace(customer)
                ? "AIR THERMAL SYSTEMS"
                : customer,
            FolioCliente = folio,
            DocumentDate = documentDate.Date,
            VersionText = $"Material Release {documentDate:dd.MM.yyyy}",
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            Rows = rows,
            Warnings = warnings
        };
    }

    private static string? FindAirThermalPurchaseOrder(
        DataTable table,
        int planRow,
        int poRow,
        DateTime documentDate,
        IReadOnlyCollection<(int Column, DateTime Date)> datedColumns)
    {
        foreach (var datedColumn in datedColumns
            .Where(x => x.Date > documentDate.Date)
            .OrderBy(x => x.Date))
        {
            if (!TryPositiveInteger(
                Get(table, planRow, datedColumn.Column),
                out _))
            {
                continue;
            }

            var purchaseOrder = CellText(Get(table, poRow, datedColumn.Column));
            if (!string.IsNullOrWhiteSpace(purchaseOrder))
                return purchaseOrder;
        }

        foreach (var datedColumn in datedColumns
            .Where(x => x.Date <= documentDate.Date)
            .OrderByDescending(x => x.Date))
        {
            var purchaseOrder = CellText(Get(table, poRow, datedColumn.Column));
            if (!string.IsNullOrWhiteSpace(purchaseOrder))
                return purchaseOrder;
        }

        return null;
    }

    private static bool TryReadAirThermalDocumentDate(
        string? fileName,
        out DateTime documentDate)
    {
        documentDate = default;
        var match = Regex.Match(
            fileName ?? string.Empty,
            @"(?<!\d)(?<date>20\d{6})(?!\d)",
            RegexOptions.CultureInvariant);

        return match.Success &&
               DateTime.TryParseExact(
                   match.Groups["date"].Value,
                   "yyyyMMdd",
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.None,
                   out documentDate);
    }

    private static string? ExtractAirThermalFolio(string? fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName ?? string.Empty);
        var match = Regex.Match(
            baseName,
            @"(?<folio>MX[A-Z0-9]{4,})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return match.Success
            ? match.Groups["folio"].Value.ToUpperInvariant()
            : null;
    }
    private static DataSet ReadWorkbook(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
            throw new InvalidOperationException("El archivo Excel esta vacio.");

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        try
        {
            using var stream = new MemoryStream(bytes);
            using var reader = ExcelReaderFactory.CreateReader(stream);
            return reader.AsDataSet(new ExcelDataSetConfiguration
            {
                ConfigureDataTable = _ => new ExcelDataTableConfiguration
                {
                    UseHeaderRow = false,
                    EmptyColumnNamePrefix = "Column"
                }
            });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("No fue posible abrir el archivo Excel: " + ex.Message, ex);
        }
    }

    private static bool LooksLikeAirThermal(DataTable table)
    {
        if (table.Rows.Count < 17 || table.Columns.Count < 8)
            return false;

        var customerLabel = CellText(Get(table, 0, 1));
        var customer = CellText(Get(table, 0, 2));
        var deliveryDateLabel = CellText(Get(table, 4, 3));
        var firstPlanLabel = CellText(Get(table, 5, 3));
        var firstPart = CellText(Get(table, 5, 1));

        return customerLabel.Equals("Customer:", StringComparison.OrdinalIgnoreCase) &&
               customer.Contains("AIR THERMAL", StringComparison.OrdinalIgnoreCase) &&
               deliveryDateLabel.Equals("Delivery date", StringComparison.OrdinalIgnoreCase) &&
               firstPlanLabel.Equals("Plan PO", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(firstPart);
    }
    private static bool LooksLikeGolde(DataTable table)
    {
        return GoldeReleaseExcelParser.LooksLike(table);
    }

    private static bool LooksLikeNorma(DataTable table)
    {
        if (table.Rows.Count < 4 || table.Columns.Count < 4)
            return false;

        var weekLabel = CellText(Get(table, 0, 1));
        var moldLabel = CellText(Get(table, 1, 0));
        var itemLabel = CellText(Get(table, 1, 1));

        return weekLabel.Contains("semana", StringComparison.OrdinalIgnoreCase) &&
               moldLabel.Equals("MOLDE", StringComparison.OrdinalIgnoreCase) &&
               itemLabel.Equals("item", StringComparison.OrdinalIgnoreCase) &&
               TryReadDate(Get(table, 1, 2), out _);
    }

    private static object? Get(DataTable table, int row, int column)
    {
        if (row < 0 || column < 0 || row >= table.Rows.Count || column >= table.Columns.Count)
            return null;

        var value = table.Rows[row][column];
        return value == DBNull.Value ? null : value;
    }

    private static string CellText(object? value)
    {
        if (value == null)
            return string.Empty;

        return value switch
        {
            DateTime date => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
            double number when Math.Abs(number - Math.Round(number)) < 0.0000001d =>
                Math.Round(number).ToString("0", CultureInfo.InvariantCulture),
            float number when Math.Abs(number - MathF.Round(number)) < 0.0001f =>
                MathF.Round(number).ToString("0", CultureInfo.InvariantCulture),
            decimal number when number == decimal.Truncate(number) =>
                decimal.Truncate(number).ToString("0", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty,
            _ => value.ToString()?.Trim() ?? string.Empty
        };
    }

    private static bool TryPositiveInteger(object? value, out int result)
    {
        if (!TryInteger(value, out result))
            return false;

        return result > 0;
    }

    private static bool TryInteger(object? value, out int result)
    {
        return ReleaseQuantityParser.TryParseInteger(value, out result);
    }

    private static bool TryReadDate(object? value, out DateTime date)
    {
        date = default;
        if (value == null)
            return false;

        if (value is DateTime dateTime)
        {
            date = dateTime.Date;
            return true;
        }

        if (value is double oaDate && oaDate > 1d)
        {
            try
            {
                date = DateTime.FromOADate(oaDate).Date;
                return true;
            }
            catch
            {
                // Continua con lectura como texto.
            }
        }

        var text = CellText(value);
        var formats = new[]
        {
            "dd.MM.yyyy",
            "dd/MM/yyyy",
            "MM/dd/yyyy",
            "yyyy-MM-dd"
        };

        return DateTime.TryParseExact(
            text,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }
}