using ExcelDataReader;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ERP.NSQuell.Servicios.Releases;

public enum ReleaseExcelTemplate
{
    Unknown = 0,
    GoldenWeeklyMatrix = 1,
    NormaWeeklyMatrix = 2
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

// RELEASE_EXCEL_PARSERS_GOLDEN_NORMA_V1_4
public static class ReleaseExcelDocumentDetector
{
    public static ReleaseExcelTemplate Detect(byte[] bytes)
    {
        var workbook = ReadWorkbook(bytes);

        foreach (DataTable table in workbook.Tables)
        {
            if (LooksLikeGolden(table))
                return ReleaseExcelTemplate.GoldenWeeklyMatrix;

            if (LooksLikeNorma(table))
                return ReleaseExcelTemplate.NormaWeeklyMatrix;
        }

        return ReleaseExcelTemplate.Unknown;
    }

    public static ReleaseExcelDocument ParseGolden(byte[] bytes)
    {
        var workbook = ReadWorkbook(bytes);
        var table = workbook.Tables.Cast<DataTable>().FirstOrDefault(LooksLikeGolden)
            ?? throw new InvalidOperationException("No se encontro la matriz semanal GOLDEN dentro del Excel.");

        var orderNumber = string.Empty;
        for (var column = 3; column < table.Columns.Count; column++)
        {
            orderNumber = CellText(Get(table, 0, column));
            if (!string.IsNullOrWhiteSpace(orderNumber))
                break;
        }

        var rows = new List<ReleaseExcelRow>();
        var warnings = new List<string>();

        for (var column = 3; column < table.Columns.Count; column++)
        {
            var partNumber = CellText(Get(table, 1, column));
            if (string.IsNullOrWhiteSpace(partNumber))
                continue;

            var description = CellText(Get(table, 2, column));
            var deliveries = new List<ReleaseExcelDelivery>();
            var sequence = 1;

            for (var row = 3; row < table.Rows.Count; row++)
            {
                if (!TryPositiveInteger(Get(table, row, column), out var quantity))
                    continue;

                if (!TryReadDate(Get(table, row, 2), out var requiredDate))
                    continue;

                var period = CellText(Get(table, row, 0));
                var isBacklog = period.Equals("BACKLOG", StringComparison.OrdinalIgnoreCase);

                deliveries.Add(new ReleaseExcelDelivery
                {
                    Sequence = sequence++,
                    PeriodLabel = period,
                    RequiredDate = requiredDate,
                    RequiredQuantity = quantity,
                    IsBacklog = isBacklog
                });
            }

            if (deliveries.Count == 0)
                continue;

            rows.Add(new ReleaseExcelRow
            {
                PartNumber = partNumber,
                PartDescription = description,
                SourceReference = orderNumber,
                Uom = "PZA",
                Deliveries = deliveries
            });
        }

        if (rows.Count == 0)
            throw new InvalidOperationException("El Excel GOLDEN no contiene cantidades positivas para importar.");

        var allDeliveries = rows.SelectMany(x => x.Deliveries).ToList();
        var firstOperational = allDeliveries
            .Where(x => !x.IsBacklog)
            .OrderBy(x => x.RequiredDate)
            .FirstOrDefault()
            ?? allDeliveries.OrderBy(x => x.RequiredDate).First();

        var firstPeriod = firstOperational.PeriodLabel;
        var version = string.IsNullOrWhiteSpace(firstPeriod)
            ? firstOperational.RequiredDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
            : $"{firstPeriod} / {firstOperational.RequiredDate:dd.MM.yyyy}";

        if (allDeliveries.Any(x => x.IsBacklog))
            warnings.Add("El documento GOLDEN contiene demanda BACKLOG; se conservo con la fecha indicada en el archivo.");

        return new ReleaseExcelDocument
        {
            TemplateCode = "GOLDEN_WEEKLY_RELEASE",
            ClienteNombre = "GOLDE AUBURN HILLS, LLC",
            FolioCliente = string.IsNullOrWhiteSpace(orderNumber) ? null : orderNumber,
            DocumentDate = firstOperational.RequiredDate,
            VersionText = version,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            Rows = rows,
            Warnings = warnings
        };
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

    private static bool LooksLikeGolden(DataTable table)
    {
        if (table.Rows.Count < 5 || table.Columns.Count < 5)
            return false;

        var backlog = CellText(Get(table, 3, 0));
        var firstPart = CellText(Get(table, 1, 3));
        var firstDescription = CellText(Get(table, 2, 3));

        return backlog.Equals("BACKLOG", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(firstPart) &&
               !string.IsNullOrWhiteSpace(firstDescription) &&
               TryReadDate(Get(table, 3, 2), out _);
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
        result = 0;
        if (value == null)
            return false;

        try
        {
            switch (value)
            {
                case int integer:
                    result = integer;
                    return true;
                case long longValue when longValue >= int.MinValue && longValue <= int.MaxValue:
                    result = (int)longValue;
                    return true;
                case double doubleValue when doubleValue >= int.MinValue && doubleValue <= int.MaxValue:
                    result = Convert.ToInt32(Math.Round(doubleValue));
                    return true;
                case decimal decimalValue when decimalValue >= int.MinValue && decimalValue <= int.MaxValue:
                    result = Convert.ToInt32(Math.Round(decimalValue));
                    return true;
                default:
                    var text = CellText(value).Replace(",", string.Empty).Trim();
                    return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
            }
        }
        catch
        {
            result = 0;
            return false;
        }
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