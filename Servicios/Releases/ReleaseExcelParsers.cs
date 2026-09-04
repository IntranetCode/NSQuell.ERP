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

        var layout = workbook.Tables.Cast<DataTable>()
            .Select(BuildNormaLayout)
            .Where(x => x != null)
            .Cast<NormaLayout>()
            .OrderByDescending(x => x.DocumentDate)
            .ThenByDescending(x => x.DeclaredWeek)
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "No se encontro la matriz semanal NORMA dentro del Excel.");

        var table = layout.Table;
        var firstWeek = layout.DeclaredWeek > 0
            ? layout.DeclaredWeek
            : ISOWeek.GetWeekOfYear(layout.DocumentDate);

        var rows = new List<ReleaseExcelRow>();
        var warnings = new List<string>();
        var currentMold = string.Empty;
        var backlogDetected = false;

        for (var row = 2; row < table.Rows.Count; row++)
        {
            var mold = CellText(Get(table, row, 0));
            if (!string.IsNullOrWhiteSpace(mold))
                currentMold = mold;

            var partNumber = CellText(Get(table, row, 1));
            if (string.IsNullOrWhiteSpace(partNumber))
                continue;

            var deliveries = new List<ReleaseExcelDelivery>();
            var sequence = 1;

            if (layout.BacklogColumn.HasValue &&
                TryPositiveInteger(
                    Get(table, row, layout.BacklogColumn.Value),
                    out var backlogQuantity))
            {
                deliveries.Add(new ReleaseExcelDelivery
                {
                    Sequence = sequence++,
                    PeriodLabel = $"BL W{firstWeek}",
                    RequiredDate = layout.DocumentDate.Date,
                    RequiredQuantity = backlogQuantity,
                    IsBacklog = true
                });

                backlogDetected = true;
            }

            for (var column = layout.FirstDatedColumn;
                 column < table.Columns.Count;
                 column++)
            {
                if (!TryPositiveInteger(Get(table, row, column), out var quantity))
                    continue;

                var requiredDate =
                    layout.FirstDatedDate.AddDays(
                        (column - layout.FirstDatedColumn) * 7d);

                if (TryReadDate(Get(table, 1, column), out var explicitDate))
                    requiredDate = explicitDate.Date;

                deliveries.Add(new ReleaseExcelDelivery
                {
                    Sequence = sequence++,
                    PeriodLabel = $"W{ISOWeek.GetWeekOfYear(requiredDate)}",
                    RequiredDate = requiredDate.Date,
                    RequiredQuantity = quantity,
                    IsBacklog = false
                });
            }

            if (deliveries.Count == 0)
                continue;

            rows.Add(new ReleaseExcelRow
            {
                PartNumber = partNumber,
                PartDescription = null,
                SourceReference = string.IsNullOrWhiteSpace(currentMold)
                    ? null
                    : $"Molde {currentMold}",
                Uom = "PZA",
                Deliveries = deliveries
            });
        }

        if (rows.Count == 0)
        {
            throw new InvalidOperationException(
                "El Excel NORMA no contiene cantidades positivas para importar.");
        }

        if (layout.BacklogColumn.HasValue)
        {
            warnings.Add(
                $"NORMA: se detecto columna BL. " +
                $"La demanda BL se interpreta como backlog de la W{firstWeek} " +
                $"con fecha operativa {layout.DocumentDate:dd/MM/yyyy}.");
        }

        if (backlogDetected)
        {
            warnings.Add(
                "NORMA: las cantidades positivas de BL se importaron como demanda vencida/backlog; " +
                "las columnas fechadas posteriores se conservaron como demanda futura.");
        }

        return new ReleaseExcelDocument
        {
            TemplateCode = "NORMA_WEEKLY_RELEASE",
            ClienteNombre = "NORMA",
            FolioCliente = $"NORMA-W{firstWeek}-{layout.DocumentDate:yyyy}",
            DocumentDate = layout.DocumentDate.Date,
            VersionText = $"W{firstWeek} / {layout.DocumentDate:dd.MM.yyyy}",
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            Rows = rows,
            Warnings = warnings
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
        var releaseDateRow = FindAirThermalReleaseDateRow(table);

        if (releaseDateRow < 0)
        {
            throw new InvalidOperationException(
                "AIR THERMAL: no se encontro la fila de fechas del Material Release.");
        }

        for (var planRow = releaseDateRow + 1; planRow < table.Rows.Count; planRow++)
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
                if (TryReadDate(Get(table, releaseDateRow, column), out var columnDate))
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

    private static int FindAirThermalReleaseDateRow(DataTable table)
    {
        if (table.Rows.Count == 0 || table.Columns.Count == 0)
            return -1;

        var bestRow = -1;
        var bestDateCount = 0;
        var scanRows = Math.Min(table.Rows.Count, 20);

        for (var row = 0; row < scanRows; row++)
        {
            var label = CellText(Get(table, row, 3)).Trim();
            var dateCount = 0;

            for (var column = 4; column < table.Columns.Count; column++)
            {
                if (TryReadDate(Get(table, row, column), out _))
                    dateCount++;
            }

            var isReleaseDate = label.Equals("Release Date", StringComparison.OrdinalIgnoreCase);
            var isDeliveryDate = label.Equals("Delivery Date", StringComparison.OrdinalIgnoreCase);

            if ((isReleaseDate || isDeliveryDate) && dateCount >= 3)
                return row;

            if (dateCount > bestDateCount)
            {
                bestDateCount = dateCount;
                bestRow = row;
            }
        }

        return bestDateCount >= 3 ? bestRow : -1;
    }

    private static bool LooksLikeAirThermal(DataTable table)
    {
        if (table.Rows.Count < 6 || table.Columns.Count < 5)
            return false;

        var hasAirThermal = false;
        var textRows = Math.Min(table.Rows.Count, 12);
        var textCols = Math.Min(table.Columns.Count, 12);

        for (var row = 0; row < textRows && !hasAirThermal; row++)
        {
            for (var column = 0; column < textCols; column++)
            {
                if (CellText(Get(table, row, column))
                    .Contains("AIR THERMAL", StringComparison.OrdinalIgnoreCase))
                {
                    hasAirThermal = true;
                    break;
                }
            }
        }

        if (!hasAirThermal)
            return false;

        var releaseDateRow = FindAirThermalReleaseDateRow(table);
        if (releaseDateRow < 0)
            return false;

        for (var row = releaseDateRow + 1; row < table.Rows.Count; row++)
        {
            if (!CellText(Get(table, row, 3))
                .Equals("Plan PO", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(CellText(Get(table, row, 1))))
                return true;
        }

        return false;
    }
    private static bool LooksLikeGolde(DataTable table)
    {
        return GoldeReleaseExcelParser.LooksLike(table);
    }

    private sealed class NormaLayout
    {
        public required DataTable Table { get; init; }
        public int DeclaredWeek { get; init; }
        public int FirstDatedColumn { get; init; }
        public DateTime FirstDatedDate { get; init; }
        public int? BacklogColumn { get; init; }
        public DateTime DocumentDate { get; init; }
    }

    private static bool LooksLikeNorma(DataTable table)
    {
        return BuildNormaLayout(table) != null;
    }

    private static NormaLayout? BuildNormaLayout(DataTable table)
    {
        if (table.Rows.Count < 4 || table.Columns.Count < 4)
            return null;

        var weekLabel = CellText(Get(table, 0, 1));
        var moldLabel = CellText(Get(table, 1, 0));
        var itemLabel = CellText(Get(table, 1, 1));

        if (!weekLabel.Contains("semana", StringComparison.OrdinalIgnoreCase) ||
            !moldLabel.Equals("MOLDE", StringComparison.OrdinalIgnoreCase) ||
            !itemLabel.Equals("item", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        int? backlogColumn = null;
        var firstDatedColumn = -1;
        var firstDatedDate = DateTime.MinValue;

        for (var column = 2; column < table.Columns.Count; column++)
        {
            var header = CellText(Get(table, 1, column)).Trim();

            if (!backlogColumn.HasValue &&
                (header.Equals("BL", StringComparison.OrdinalIgnoreCase) ||
                 header.Equals("BACKLOG", StringComparison.OrdinalIgnoreCase) ||
                 header.Contains("BACK LOG", StringComparison.OrdinalIgnoreCase)))
            {
                backlogColumn = column;
                continue;
            }

            if (TryReadDate(Get(table, 1, column), out var date))
            {
                firstDatedColumn = column;
                firstDatedDate = date.Date;
                break;
            }
        }

        if (firstDatedColumn < 0)
            return null;

        var declaredWeek = 0;
        TryInteger(Get(table, 0, 2), out declaredWeek);

        var documentDate = backlogColumn.HasValue
            ? firstDatedDate.AddDays(-7).Date
            : firstDatedDate.Date;

        if (declaredWeek <= 0)
            declaredWeek = ISOWeek.GetWeekOfYear(documentDate);

        return new NormaLayout
        {
            Table = table,
            DeclaredWeek = declaredWeek,
            FirstDatedColumn = firstDatedColumn,
            FirstDatedDate = firstDatedDate,
            BacklogColumn = backlogColumn,
            DocumentDate = documentDate
        };
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