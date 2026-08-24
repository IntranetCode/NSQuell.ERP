using ExcelDataReader;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ERP.NSQuell.Servicios.Releases;

// GOLDE_MEXICO_PARSER_V1_4
// Detecta la matriz por contenido y no por nombre de archivo ni por tipos estrictos de celda.
internal static class GoldeMexicoReleaseExcelParser
{
    private sealed class MatrixLayout
    {
        public required DataTable Table { get; init; }
        public required int TitleRow { get; init; }
        public required int DateRow { get; init; }
        public required int WeekRow { get; init; }
        public required int PartColumn { get; init; }
        public required int DataStartRow { get; init; }
        public required List<(int Column, DateTime Date)> DateColumns { get; init; }
    }

    public static bool LooksLike(DataTable table)
        => TryFindLayout(table, out _);

    public static ReleaseExcelDocument Parse(byte[] bytes, string? fileName)
    {
        var workbook = ReadWorkbook(bytes);

        MatrixLayout? layout = null;
        foreach (DataTable table in workbook.Tables)
        {
            if (TryFindLayout(table, out var candidate))
            {
                layout = candidate;
                break;
            }
        }

        if (layout == null)
        {
            throw new InvalidOperationException(
                "No se encontro una matriz GOLDE MEXICO con 'Supplier shipping plan', fechas semanales, partes y cantidades.");
        }

        var rows = new List<ReleaseExcelRow>();

        for (var row = layout.DataStartRow; row < layout.Table.Rows.Count; row++)
        {
            var partNumber = CellText(Get(layout.Table, row, layout.PartColumn));
            if (!LooksLikePartNumber(partNumber))
                continue;

            var deliveries = new List<ReleaseExcelDelivery>();
            var sequence = 1;

            foreach (var dc in layout.DateColumns.OrderBy(x => x.Column))
            {
                if (!TryPositiveInteger(Get(layout.Table, row, dc.Column), out var quantity))
                    continue;

                var week = GetWeekLabel(layout.Table, layout.WeekRow, dc.Column, dc.Date);

                deliveries.Add(new ReleaseExcelDelivery
                {
                    Sequence = sequence++,
                    PeriodLabel = week,
                    RequiredDate = dc.Date.Date,
                    RequiredQuantity = quantity,
                    IsBacklog = false
                });
            }

            // Una parte con puro cero no genera necesidad y se ignora.
            if (deliveries.Count == 0)
                continue;

            rows.Add(new ReleaseExcelRow
            {
                PartNumber = partNumber,
                PartDescription = null,
                SourceReference = Path.GetFileNameWithoutExtension(fileName ?? string.Empty),
                Uom = "PIEZA",
                Deliveries = deliveries
            });
        }

        if (rows.Count == 0)
        {
            throw new InvalidOperationException(
                "Se reconocio el formato GOLDE MEXICO, pero no se encontraron partes con cantidades positivas.");
        }

        var first = layout.DateColumns.OrderBy(x => x.Column).First();
        var last = layout.DateColumns.OrderBy(x => x.Column).Last();
        var firstWeek = ISOWeek.GetWeekOfYear(first.Date);
        var lastWeek = ISOWeek.GetWeekOfYear(last.Date);

        return new ReleaseExcelDocument
        {
            TemplateCode = "GOLDE_MEXICO_WEEKLY_RELEASE",
            ClienteNombre = "GOLDE MEXICO",
            FolioCliente = Path.GetFileNameWithoutExtension(fileName ?? string.Empty),
            DocumentDate = first.Date.Date,
            VersionText = $"CW{firstWeek}-CW{lastWeek} / {first.Date:dd.MM.yyyy}",
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            Rows = rows,
            Warnings = new List<string>
            {
                "Formato GOLDE MEXICO identificado por contenido Supplier shipping plan; no depende del nombre del archivo.",
                "Fechas, semana, columna de parte y cantidades se detectan dinamicamente.",
                "Las referencias se vinculan contra ERP_Partes del cliente GOLDE MEXICO."
            }
        };
    }

    private static bool TryFindLayout(DataTable table, out MatrixLayout? layout)
    {
        layout = null;

        if (table.Rows.Count < 3 || table.Columns.Count < 3)
            return false;

        var titleRow = -1;
        var titleCol = -1;
        var scanRows = Math.Min(table.Rows.Count, 12);
        var scanCols = Math.Min(table.Columns.Count, 12);

        for (var row = 0; row < scanRows && titleRow < 0; row++)
        {
            for (var col = 0; col < scanCols; col++)
            {
                var text = NormalizeText(CellText(Get(table, row, col)));
                if (text.Contains("SUPPLIER SHIPPING PLAN", StringComparison.OrdinalIgnoreCase))
                {
                    titleRow = row;
                    titleCol = col;
                    break;
                }
            }
        }

        if (titleRow < 0)
            return false;

        // Busca la fila con mayor cantidad de fechas cerca del titulo.
        var dateRow = -1;
        var bestDateColumns = new List<(int Column, DateTime Date)>();
        var dateSearchEnd = Math.Min(table.Rows.Count - 1, titleRow + 5);

        for (var row = Math.Max(0, titleRow); row <= dateSearchEnd; row++)
        {
            var dates = new List<(int Column, DateTime Date)>();

            for (var col = Math.Max(0, titleCol + 1); col < table.Columns.Count; col++)
            {
                if (TryReadDate(Get(table, row, col), out var date))
                    dates.Add((col, date.Date));
            }

            if (dates.Count > bestDateColumns.Count)
            {
                dateRow = row;
                bestDateColumns = dates;
            }
        }

        if (dateRow < 0 || bestDateColumns.Count < 2)
            return false;

        var firstDateColumn = bestDateColumns.Min(x => x.Column);

        // Normalmente la parte esta inmediatamente antes de la primera fecha.
        // Si hay mas columnas previas, elige la que contenga mas referencias tipo parte.
        var candidatePartColumns = Enumerable.Range(0, Math.Max(1, firstDateColumn)).ToList();
        var preliminaryDataStart = Math.Min(table.Rows.Count, dateRow + 1);

        var partColumn = candidatePartColumns
            .Select(col => new
            {
                Column = col,
                Score = Enumerable.Range(preliminaryDataStart, table.Rows.Count - preliminaryDataStart)
                    .Count(row => LooksLikePartNumber(CellText(Get(table, row, col))))
            })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Column == firstDateColumn - 1)
            .ThenBy(x => x.Column)
            .First();

        if (partColumn.Score <= 0)
            return false;

        // Si la fila siguiente contiene numeros de semana, se considera encabezado de semanas.
        var weekRow = -1;
        if (dateRow + 1 < table.Rows.Count)
        {
            var weekHits = 0;
            foreach (var dc in bestDateColumns)
            {
                if (LooksLikeWeek(Get(table, dateRow + 1, dc.Column)))
                    weekHits++;
            }

            if (weekHits >= Math.Min(2, bestDateColumns.Count))
                weekRow = dateRow + 1;
        }

        var dataStart = Math.Max(dateRow, weekRow) + 1;
        if (dataStart >= table.Rows.Count)
            return false;

        // Confirmacion final: debe existir al menos una parte con una cantidad positiva
        // bajo alguna columna de fecha.
        var hasData = false;
        for (var row = dataStart; row < table.Rows.Count && !hasData; row++)
        {
            var part = CellText(Get(table, row, partColumn.Column));
            if (!LooksLikePartNumber(part))
                continue;

            foreach (var dc in bestDateColumns)
            {
                if (TryPositiveInteger(Get(table, row, dc.Column), out _))
                {
                    hasData = true;
                    break;
                }
            }
        }

        if (!hasData)
            return false;

        layout = new MatrixLayout
        {
            Table = table,
            TitleRow = titleRow,
            DateRow = dateRow,
            WeekRow = weekRow,
            PartColumn = partColumn.Column,
            DataStartRow = dataStart,
            DateColumns = bestDateColumns
        };

        return true;
    }

    private static string GetWeekLabel(DataTable table, int weekRow, int column, DateTime date)
    {
        if (weekRow >= 0)
        {
            var raw = CellText(Get(table, weekRow, column));
            if (!string.IsNullOrWhiteSpace(raw))
            {
                raw = raw.Trim();
                if (raw.StartsWith("CW", StringComparison.OrdinalIgnoreCase) ||
                    raw.StartsWith("W", StringComparison.OrdinalIgnoreCase))
                    return raw.ToUpperInvariant();

                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) &&
                    number is >= 1 and <= 53)
                    return $"CW{number}";
            }
        }

        return $"CW{ISOWeek.GetWeekOfYear(date)}";
    }

    private static bool LooksLikeWeek(object? value)
    {
        var text = CellText(value).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (text.StartsWith("CW", StringComparison.OrdinalIgnoreCase))
            text = text[2..];
        else if (text.StartsWith("W", StringComparison.OrdinalIgnoreCase))
            text = text[1..];

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var week) &&
               week is >= 1 and <= 53;
    }

    private static bool LooksLikePartNumber(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 4 || text.Length > 150)
            return false;

        var normalized = NormalizeText(text);
        if (normalized.Contains("SUPPLIER SHIPPING PLAN", StringComparison.OrdinalIgnoreCase))
            return false;

        var hasDigit = text.Any(char.IsDigit);
        var hasSeparatorOrLetter = text.Any(ch => char.IsLetter(ch) || ch is '.' or '-' or '_' or '/');
        return hasDigit && hasSeparatorOrLetter;
    }

    private static DataSet ReadWorkbook(byte[] bytes)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
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

    private static object? Get(DataTable table, int row, int col)
    {
        if (row < 0 || col < 0 || row >= table.Rows.Count || col >= table.Columns.Count)
            return null;

        var value = table.Rows[row][col];
        return value == DBNull.Value ? null : value;
    }

    private static string CellText(object? value)
    {
        if (value == null)
            return string.Empty;

        if (value is DateTime date)
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (value is IFormattable formattable)
            return formattable.ToString(null, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;

        return Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
    }

    private static string NormalizeText(string value)
        => string.Join(" ", value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Trim();

    private static bool TryPositiveInteger(object? value, out int quantity)
    {
        quantity = 0;
        if (value == null)
            return false;

        try
        {
            if (value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
            {
                var number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                if (number <= 0 || number > int.MaxValue)
                    return false;

                quantity = Convert.ToInt32(Math.Round(number, 0, MidpointRounding.AwayFromZero));
                return quantity > 0;
            }
        }
        catch
        {
            return false;
        }

        var text = CellText(value)
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) &&
            parsed > 0 && parsed <= int.MaxValue)
        {
            quantity = Convert.ToInt32(Math.Round(parsed, 0, MidpointRounding.AwayFromZero));
            return quantity > 0;
        }

        return false;
    }

    private static bool TryReadDate(object? value, out DateTime date)
    {
        date = default;
        if (value == null)
            return false;

        if (value is DateTime dt)
        {
            date = dt.Date;
            return true;
        }

        try
        {
            if (value is double or float or decimal or int or long)
            {
                var serial = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (serial > 20000 && serial < 100000)
                {
                    date = DateTime.FromOADate(serial).Date;
                    return true;
                }
            }
        }
        catch
        {
            // Sigue con lectura textual.
        }

        var text = CellText(value);
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var formats = new[]
        {
            "dd.MM.yyyy", "dd/MM/yyyy", "yyyy-MM-dd", "MM/dd/yyyy", "dd-MM-yyyy",
            "d.M.yyyy", "d/M/yyyy", "M/d/yyyy", "yyyy/MM/dd"
        };

        if (DateTime.TryParseExact(
            text,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var parsed))
        {
            date = parsed.Date;
            return true;
        }

        if (DateTime.TryParse(
            text,
            CultureInfo.GetCultureInfo("es-MX"),
            DateTimeStyles.AllowWhiteSpaces,
            out parsed))
        {
            date = parsed.Date;
            return true;
        }

        return false;
    }
}