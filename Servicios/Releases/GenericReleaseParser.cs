using ExcelDataReader;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace ERP.NSQuell.Servicios.Releases;

// GENERIC_RELEASE_READER_V1_0
// Fallback para documentos que no coinciden con una plantilla especializada.
// La prioridad sigue siendo: parser conocido -> parser generico.
//
// Principio de seguridad:
// - No inventar ParteID.
// - Las partes conocidas se identifican contra ERP_Partes.
// - Si no existe una relacion fecha/cantidad suficientemente clara, no se crea
//   una entrega falsa.
public sealed class GenericReleaseKnownPart
{
    public int ParteID { get; init; }
    public int ClienteID { get; init; }
    public string ClienteNombre { get; init; } = string.Empty;
    public string NumeroParte { get; init; } = string.Empty;
    public string? ReferenciaSAP { get; init; }
    public string? Descripcion { get; init; }
    public string? Designacion { get; init; }
}

public sealed class GenericReleaseDocument
{
    public string TemplateCode { get; init; } = "GENERIC_RELEASE";
    public int ClienteID { get; init; }
    public string ClienteNombre { get; init; } = string.Empty;
    public string? FolioCliente { get; init; }
    public DateTime? DocumentDate { get; init; }
    public string VersionText { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public List<GenericReleaseRow> Rows { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
}

public sealed class GenericReleaseRow
{
    public required GenericReleaseKnownPart Part { get; init; }
    public string SourceToken { get; init; } = string.Empty;
    public string Uom { get; init; } = "PIEZA";
    public string? SourceReference { get; init; }
    public List<GenericReleaseDelivery> Deliveries { get; init; } = new();
}

public sealed class GenericReleaseDelivery
{
    public int Sequence { get; init; }
    public DateTime RequiredDate { get; init; }
    public int RequiredQuantity { get; init; }
    public string? PeriodLabel { get; init; }
}

public static class GenericReleaseParser
{
    private sealed class PartIndex
    {
        public required Dictionary<string, List<GenericReleaseKnownPart>> ByAlias { get; init; }
        public required List<(string Normalized, string Raw, GenericReleaseKnownPart Part)> SearchAliases { get; init; }
    }

    public static GenericReleaseDocument Parse(
        byte[] bytes,
        string extension,
        string? fileName,
        IReadOnlyCollection<GenericReleaseKnownPart> catalog)
    {
        if (bytes == null || bytes.Length == 0)
            throw new InvalidOperationException("El documento esta vacio.");

        if (catalog == null || catalog.Count == 0)
            throw new InvalidOperationException("ERP_Partes no contiene partes activas para realizar el enlace generico.");

        var ext = (extension ?? string.Empty).Trim().ToLowerInvariant();
        var index = BuildIndex(catalog);

        return ext switch
        {
            ".xlsx" or ".xls" or ".xlsm" => ParseWorkbook(bytes, fileName, index),
            ".csv" => ParseCsv(bytes, fileName, index),
            ".pdf" => ParsePdf(bytes, fileName, index),
            _ => throw new InvalidOperationException("El lector generico admite PDF, XLSX, XLS, XLSM y CSV.")
        };
    }

    private static GenericReleaseDocument ParseWorkbook(
        byte[] bytes,
        string? fileName,
        PartIndex index)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        DataSet workbook;
        try
        {
            using var stream = new MemoryStream(bytes);
            using var reader = ExcelReaderFactory.CreateReader(stream);
            workbook = reader.AsDataSet(new ExcelDataSetConfiguration
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
            throw new InvalidOperationException("No fue posible abrir el Excel con el lector generico: " + ex.Message, ex);
        }

        var rows = new List<GenericReleaseRow>();
        var warnings = new List<string>();

        foreach (DataTable table in workbook.Tables)
            ExtractFromTable(table, index, rows, warnings);

        return BuildDocument(bytes, fileName, rows, warnings, "Excel");
    }

    private static GenericReleaseDocument ParseCsv(
        byte[] bytes,
        string? fileName,
        PartIndex index)
    {
        string text;
        try
        {
            text = Encoding.UTF8.GetString(bytes);
            if (text.Contains('\uFFFD'))
                text = Encoding.GetEncoding(1252).GetString(bytes);
        }
        catch
        {
            text = Encoding.GetEncoding(1252).GetString(bytes);
        }

        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.None);

        var delimiter = DetectDelimiter(lines);
        var table = new DataTable("CSV");

        var splitRows = lines
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => SplitDelimited(x, delimiter))
            .ToList();

        var maxColumns = splitRows.Count == 0 ? 0 : splitRows.Max(x => x.Count);
        if (maxColumns == 0)
            throw new InvalidOperationException("El CSV no contiene datos.");

        for (var i = 0; i < maxColumns; i++)
            table.Columns.Add("Column" + i, typeof(string));

        foreach (var values in splitRows)
        {
            var row = table.NewRow();
            for (var i = 0; i < values.Count; i++)
                row[i] = values[i];
            table.Rows.Add(row);
        }

        var rows = new List<GenericReleaseRow>();
        var warnings = new List<string>();
        ExtractFromTable(table, index, rows, warnings);

        return BuildDocument(bytes, fileName, rows, warnings, "CSV");
    }

    private static GenericReleaseDocument ParsePdf(
        byte[] bytes,
        string? fileName,
        PartIndex index)
    {
        string text;
        try
        {
            using var stream = new MemoryStream(bytes);
            using var pdf = PdfDocument.Open(stream);
            text = string.Join(
                Environment.NewLine,
                pdf.GetPages().Select(p => p.Text));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("No fue posible extraer texto del PDF: " + ex.Message, ex);
        }

        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("El PDF no contiene texto util para el lector generico.");

        var normalizedDocument = Normalize(text);
        var candidateParts = new Dictionary<int, (GenericReleaseKnownPart Part, string RawAlias)>();

        foreach (var alias in index.SearchAliases)
        {
            if (alias.Normalized.Length < 6)
                continue;

            if (!normalizedDocument.Contains(alias.Normalized, StringComparison.Ordinal))
                continue;

            if (!candidateParts.ContainsKey(alias.Part.ParteID))
                candidateParts[alias.Part.ParteID] = (alias.Part, alias.Raw);
        }

        var rows = new List<GenericReleaseRow>();
        var warnings = new List<string>();

        foreach (var candidate in candidateParts.Values)
        {
            var window = FindTextWindow(text, candidate.RawAlias, candidate.Part.NumeroParte);
            var deliveries = ExtractTextDeliveries(window);

            if (deliveries.Count == 0)
            {
                warnings.Add(
                    $"Se detecto la parte {candidate.Part.NumeroParte} en el PDF, pero no se pudo asociar con seguridad una fecha y cantidad.");
                continue;
            }

            rows.Add(new GenericReleaseRow
            {
                Part = candidate.Part,
                SourceToken = candidate.RawAlias,
                Uom = DetectUom(window),
                SourceReference = Path.GetFileNameWithoutExtension(fileName ?? string.Empty),
                Deliveries = deliveries
            });
        }

        return BuildDocument(bytes, fileName, rows, warnings, "PDF");
    }

    private static void ExtractFromTable(
        DataTable table,
        PartIndex index,
        List<GenericReleaseRow> output,
        List<string> warnings)
    {
        if (table.Rows.Count == 0 || table.Columns.Count == 0)
            return;

        var collected = new Dictionary<int, GenericReleaseRow>();

        for (var row = 0; row < table.Rows.Count; row++)
        {
            for (var col = 0; col < table.Columns.Count; col++)
            {
                var raw = CellText(Get(table, row, col));
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                var matches = ResolveCellParts(raw, index);
                if (matches.Count == 0)
                    continue;

                var distinctParts = matches
                    .Select(x => x.Part)
                    .GroupBy(x => x.ParteID)
                    .Select(x => x.First())
                    .ToList();

                if (distinctParts.Count != 1)
                {
                    warnings.Add(
                        $"La celda '{Truncate(raw, 80)}' coincide con mas de una parte activa; se omitio por ambiguedad.");
                    continue;
                }

                var part = distinctParts[0];
                var deliveries = ExtractTableDeliveries(table, row, col);

                if (deliveries.Count == 0)
                    continue;

                if (!collected.TryGetValue(part.ParteID, out var target))
                {
                    target = new GenericReleaseRow
                    {
                        Part = part,
                        SourceToken = raw,
                        Uom = DetectUomInTable(table, row),
                        SourceReference = null,
                        Deliveries = new List<GenericReleaseDelivery>()
                    };
                    collected[part.ParteID] = target;
                }

                foreach (var delivery in deliveries)
                {
                    if (target.Deliveries.Any(x =>
                        x.RequiredDate.Date == delivery.RequiredDate.Date &&
                        x.RequiredQuantity == delivery.RequiredQuantity))
                    {
                        continue;
                    }

                    target.Deliveries.Add(delivery);
                }
            }
        }

        foreach (var row in collected.Values)
        {
            var ordered = row.Deliveries
                .OrderBy(x => x.RequiredDate)
                .ThenBy(x => x.RequiredQuantity)
                .ToList();

            var seq = 1;
            row.Deliveries.Clear();
            foreach (var d in ordered)
            {
                row.Deliveries.Add(new GenericReleaseDelivery
                {
                    Sequence = seq++,
                    RequiredDate = d.RequiredDate.Date,
                    RequiredQuantity = d.RequiredQuantity,
                    PeriodLabel = d.PeriodLabel
                });
            }

            if (row.Deliveries.Count > 0)
                output.Add(row);
        }
    }

    private static List<GenericReleaseDelivery> ExtractTableDeliveries(
        DataTable table,
        int partRow,
        int partCol)
    {
        var result = new List<GenericReleaseDelivery>();

        // Caso 1: matriz horizontal.
        // Busca la fila de fechas mas cercana arriba de la parte.
        var bestDates = new List<(int Column, DateTime Date)>();

        for (var headerRow = Math.Max(0, partRow - 8); headerRow <= partRow; headerRow++)
        {
            var dates = new List<(int Column, DateTime Date)>();

            for (var col = 0; col < table.Columns.Count; col++)
            {
                if (TryReadDate(Get(table, headerRow, col), out var date))
                    dates.Add((col, date.Date));
            }

            if (dates.Count > bestDates.Count)
                bestDates = dates;
        }

        if (bestDates.Count > 0)
        {
            foreach (var dc in bestDates)
            {
                if (dc.Column == partCol)
                    continue;

                if (!TryPositiveInteger(Get(table, partRow, dc.Column), out var quantity))
                    continue;

                result.Add(new GenericReleaseDelivery
                {
                    RequiredDate = dc.Date,
                    RequiredQuantity = quantity,
                    PeriodLabel = $"GEN {dc.Date:dd/MM/yyyy}"
                });
            }

            if (result.Count > 0)
                return result;
        }

        // Caso 2: una fecha y una cantidad en la misma fila.
        var rowDates = new List<DateTime>();
        var rowQuantities = new List<int>();

        for (var col = 0; col < table.Columns.Count; col++)
        {
            if (col == partCol)
                continue;

            var value = Get(table, partRow, col);
            if (TryReadDate(value, out var date))
                rowDates.Add(date.Date);
            else if (TryPositiveInteger(value, out var quantity))
                rowQuantities.Add(quantity);
        }

        if (rowDates.Count == 1 && rowQuantities.Count == 1)
        {
            result.Add(new GenericReleaseDelivery
            {
                RequiredDate = rowDates[0],
                RequiredQuantity = rowQuantities[0],
                PeriodLabel = $"GEN {rowDates[0]:dd/MM/yyyy}"
            });

            return result;
        }

        // Caso 3: bloque vertical debajo de la parte.
        for (var row = partRow; row <= Math.Min(table.Rows.Count - 1, partRow + 10); row++)
        {
            var dates = new List<DateTime>();
            var quantities = new List<int>();

            for (var col = 0; col < table.Columns.Count; col++)
            {
                var value = Get(table, row, col);

                if (TryReadDate(value, out var date))
                    dates.Add(date.Date);
                else if (TryPositiveInteger(value, out var quantity))
                    quantities.Add(quantity);
            }

            if (dates.Count == 1 && quantities.Count == 1)
            {
                result.Add(new GenericReleaseDelivery
                {
                    RequiredDate = dates[0],
                    RequiredQuantity = quantities[0],
                    PeriodLabel = $"GEN {dates[0]:dd/MM/yyyy}"
                });
            }
        }

        return result
            .GroupBy(x => new { Date = x.RequiredDate.Date, x.RequiredQuantity })
            .Select(x => x.First())
            .OrderBy(x => x.RequiredDate)
            .ToList();
    }

    private static GenericReleaseDocument BuildDocument(
        byte[] bytes,
        string? fileName,
        List<GenericReleaseRow> rows,
        List<string> warnings,
        string sourceType)
    {
        rows = rows
            .Where(x => x.Deliveries.Count > 0)
            .GroupBy(x => x.Part.ParteID)
            .Select(group =>
            {
                var first = group.First();
                var merged = group
                    .SelectMany(x => x.Deliveries)
                    .GroupBy(x => new { Date = x.RequiredDate.Date, x.RequiredQuantity })
                    .Select(x => x.First())
                    .OrderBy(x => x.RequiredDate)
                    .ToList();

                var seq = 1;
                return new GenericReleaseRow
                {
                    Part = first.Part,
                    SourceToken = first.SourceToken,
                    Uom = first.Uom,
                    SourceReference = first.SourceReference,
                    Deliveries = merged.Select(x => new GenericReleaseDelivery
                    {
                        Sequence = seq++,
                        RequiredDate = x.RequiredDate.Date,
                        RequiredQuantity = x.RequiredQuantity,
                        PeriodLabel = x.PeriodLabel
                    }).ToList()
                };
            })
            .ToList();

        if (rows.Count == 0)
        {
            var extra = warnings.Count > 0
                ? " " + string.Join(" ", warnings.Take(3))
                : string.Empty;

            throw new InvalidOperationException(
                $"El lector generico {sourceType} no encontro una combinacion segura de Parte ERP + fecha de entrega + cantidad.{extra}");
        }

        var clients = rows
            .Select(x => new { x.Part.ClienteID, x.Part.ClienteNombre })
            .Distinct()
            .ToList();

        if (clients.Count != 1)
        {
            throw new InvalidOperationException(
                "El documento contiene partes activas de mas de un cliente ERP. No se puede determinar automaticamente un solo cliente.");
        }

        var client = clients[0];

        warnings.Insert(
            0,
            $"Lector generico {sourceType}: las partes se reconocieron contra ERP_Partes; el archivo solo aporta demanda/fechas/cantidades.");

        return new GenericReleaseDocument
        {
            ClienteID = client.ClienteID,
            ClienteNombre = client.ClienteNombre,
            FolioCliente = Path.GetFileNameWithoutExtension(fileName ?? string.Empty),
            DocumentDate = null,
            VersionText = $"Lectura generica {DateTime.Today:dd.MM.yyyy}",
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            Rows = rows,
            Warnings = warnings
        };
    }

    private static PartIndex BuildIndex(IReadOnlyCollection<GenericReleaseKnownPart> catalog)
    {
        var byAlias = new Dictionary<string, List<GenericReleaseKnownPart>>(StringComparer.Ordinal);
        var search = new List<(string Normalized, string Raw, GenericReleaseKnownPart Part)>();

        foreach (var part in catalog)
        {
            AddAlias(part.NumeroParte, part, byAlias, search);
            AddAlias(part.ReferenciaSAP, part, byAlias, search);
        }

        return new PartIndex
        {
            ByAlias = byAlias,
            SearchAliases = search
                .GroupBy(x => new { x.Normalized, x.Part.ParteID })
                .Select(x => x.First())
                .OrderByDescending(x => x.Normalized.Length)
                .ToList()
        };
    }

    private static void AddAlias(
        string? raw,
        GenericReleaseKnownPart part,
        Dictionary<string, List<GenericReleaseKnownPart>> byAlias,
        List<(string Normalized, string Raw, GenericReleaseKnownPart Part)> search)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return;

        var normalized = Normalize(raw);
        if (normalized.Length < 4 || !normalized.Any(char.IsDigit))
            return;

        if (!byAlias.TryGetValue(normalized, out var list))
        {
            list = new List<GenericReleaseKnownPart>();
            byAlias[normalized] = list;
        }

        if (!list.Any(x => x.ParteID == part.ParteID))
            list.Add(part);

        search.Add((normalized, raw.Trim(), part));
    }

    private static List<(GenericReleaseKnownPart Part, string Alias)> ResolveCellParts(
        string raw,
        PartIndex index)
    {
        var result = new List<(GenericReleaseKnownPart Part, string Alias)>();
        var normalized = Normalize(raw);

        if (index.ByAlias.TryGetValue(normalized, out var exact))
        {
            result.AddRange(exact.Select(x => (x, raw)));
            return result;
        }

        if (normalized.Length < 6)
            return result;

        foreach (var alias in index.SearchAliases)
        {
            if (alias.Normalized.Length < 6)
                continue;

            if (normalized.Contains(alias.Normalized, StringComparison.Ordinal))
                result.Add((alias.Part, alias.Raw));
        }

        return result
            .GroupBy(x => x.Part.ParteID)
            .Select(x => x.First())
            .ToList();
    }

    private static string FindTextWindow(string text, params string?[] aliases)
    {
        foreach (var alias in aliases)
        {
            if (string.IsNullOrWhiteSpace(alias))
                continue;

            var idx = text.IndexOf(alias, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                continue;

            var start = Math.Max(0, idx - 500);
            var length = Math.Min(text.Length - start, 1800);
            return text.Substring(start, length);
        }

        return text.Length <= 3000 ? text : text.Substring(0, 3000);
    }

    private static List<GenericReleaseDelivery> ExtractTextDeliveries(string text)
    {
        var result = new List<GenericReleaseDelivery>();

        const string datePattern =
            @"(?<date>(?:0?[1-9]|[12]\d|3[01])[./-](?:0?[1-9]|1[0-2])[./-](?:20)?\d{2}|20\d{2}[./-](?:0?[1-9]|1[0-2])[./-](?:0?[1-9]|[12]\d|3[01]))";

        var qtyBefore = new Regex(
            @"(?<qty>\d[\d., ]{0,16})\s*(?:pcs|pzas?|piezas?|pieces?)?\s*(?:arrival|due|delivery|date|fecha)?\s*" + datePattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        foreach (Match match in qtyBefore.Matches(text))
        {
            if (!TryParseDateText(match.Groups["date"].Value, out var date))
                continue;

            if (!TryParseQuantityText(match.Groups["qty"].Value, out var qty))
                continue;

            result.Add(new GenericReleaseDelivery
            {
                RequiredDate = date.Date,
                RequiredQuantity = qty,
                PeriodLabel = $"GEN {date:dd/MM/yyyy}"
            });
        }

        var dateBefore = new Regex(
            datePattern +
            @"\s*(?:qty|quantity|cantidad|pcs|pzas?|piezas?|pieces?)?\s*(?<qty>\d[\d., ]{0,16})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        foreach (Match match in dateBefore.Matches(text))
        {
            if (!TryParseDateText(match.Groups["date"].Value, out var date))
                continue;

            if (!TryParseQuantityText(match.Groups["qty"].Value, out var qty))
                continue;

            result.Add(new GenericReleaseDelivery
            {
                RequiredDate = date.Date,
                RequiredQuantity = qty,
                PeriodLabel = $"GEN {date:dd/MM/yyyy}"
            });
        }

        return result
            .GroupBy(x => new { Date = x.RequiredDate.Date, x.RequiredQuantity })
            .Select(x => x.First())
            .OrderBy(x => x.RequiredDate)
            .ToList();
    }

    private static string DetectUom(string text)
    {
        if (Regex.IsMatch(text, @"\bpcs\b", RegexOptions.IgnoreCase))
            return "PIEZA";

        if (Regex.IsMatch(text, @"\b(pza|pzas|pieza|piezas)\b", RegexOptions.IgnoreCase))
            return "PIEZA";

        return "PIEZA";
    }

    private static string DetectUomInTable(DataTable table, int row)
    {
        var start = Math.Max(0, row - 1);
        var end = Math.Min(table.Rows.Count - 1, row + 1);

        for (var r = start; r <= end; r++)
        {
            for (var c = 0; c < table.Columns.Count; c++)
            {
                var text = CellText(Get(table, r, c));
                if (Regex.IsMatch(text, @"\b(pcs|pza|pzas|pieza|piezas)\b", RegexOptions.IgnoreCase))
                    return "PIEZA";
            }
        }

        return "PIEZA";
    }

    private static char DetectDelimiter(IEnumerable<string> lines)
    {
        var first = lines.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
        var candidates = new[] { ';', ',', '\t', '|' };

        return candidates
            .Select(x => new { Delimiter = x, Count = first.Count(c => c == x) })
            .OrderByDescending(x => x.Count)
            .First().Delimiter;
    }

    private static List<string> SplitDelimited(string line, char delimiter)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (ch == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }

                continue;
            }

            if (ch == delimiter && !quoted)
            {
                result.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        result.Add(current.ToString().Trim());
        return result;
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

        if (value is DateTime dt)
            return dt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        if (value is IFormattable f)
            return f.ToString(null, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;

        return Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
    }

    private static bool TryReadDate(object? value, out DateTime date)
    {
        date = default;

        if (value is DateTime dt)
        {
            date = dt.Date;
            return true;
        }

        if (value is double or float or decimal or int or long)
        {
            try
            {
                var serial = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (serial >= 20000 && serial <= 100000)
                {
                    date = DateTime.FromOADate(serial).Date;
                    return true;
                }
            }
            catch
            {
                // Sigue a texto.
            }
        }

        return TryParseDateText(CellText(value), out date);
    }

    private static bool TryParseDateText(string text, out DateTime date)
    {
        date = default;
        text = (text ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var formats = new[]
        {
            "dd/MM/yyyy", "d/M/yyyy",
            "dd.MM.yyyy", "d.M.yyyy",
            "dd-MM-yyyy", "d-M-yyyy",
            "yyyy-MM-dd", "yyyy/MM/dd", "yyyy.MM.dd",
            "dd/MM/yy", "d/M/yy",
            "dd.MM.yy", "d.M.yy",
            "dd-MM-yy", "d-M-yy"
        };

        return DateTime.TryParseExact(
                   text,
                   formats,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.AllowWhiteSpaces,
                   out date)
               || DateTime.TryParse(
                   text,
                   CultureInfo.GetCultureInfo("es-MX"),
                   DateTimeStyles.AllowWhiteSpaces,
                   out date);
    }

    private static bool TryPositiveInteger(object? value, out int quantity)
    {
        quantity = 0;
        if (value == null)
            return false;

        if (value is DateTime)
            return false;

        if (value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
        {
            try
            {
                var number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                if (number <= 0 || number > int.MaxValue)
                    return false;

                // Evitar seriales de fecha de Excel cuando se usan como cantidades.
                if (number >= 20000 && number <= 100000 && decimal.Truncate(number) == number)
                {
                    try
                    {
                        var possibleDate = DateTime.FromOADate((double)number);
                        if (possibleDate.Year is >= 1950 and <= 2200)
                            return false;
                    }
                    catch
                    {
                        // No era fecha.
                    }
                }

                quantity = Convert.ToInt32(Math.Round(number, 0, MidpointRounding.AwayFromZero));
                return quantity > 0;
            }
            catch
            {
                return false;
            }
        }

        return TryParseQuantityText(CellText(value), out quantity);
    }

    private static bool TryParseQuantityText(string text, out int quantity)
    {
        quantity = 0;
        var value = (text ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(value))
            return false;

        value = Regex.Replace(value, @"[^\d.,-]", string.Empty);
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("-", StringComparison.Ordinal))
            return false;

        string normalized;

        if (value.Contains('.') && value.Contains(','))
        {
            var lastDot = value.LastIndexOf('.');
            var lastComma = value.LastIndexOf(',');

            if (lastComma > lastDot)
            {
                normalized = value.Replace(".", string.Empty, StringComparison.Ordinal)
                                  .Replace(',', '.');
            }
            else
            {
                normalized = value.Replace(",", string.Empty, StringComparison.Ordinal);
            }
        }
        else if (Regex.IsMatch(value, @"^\d{1,3}(?:\.\d{3})+(?:,\d+)?$"))
        {
            normalized = value.Replace(".", string.Empty, StringComparison.Ordinal)
                              .Replace(',', '.');
        }
        else if (Regex.IsMatch(value, @"^\d{1,3}(?:,\d{3})+(?:\.\d+)?$"))
        {
            normalized = value.Replace(",", string.Empty, StringComparison.Ordinal);
        }
        else
        {
            normalized = value.Replace(',', '.');
        }

        if (!decimal.TryParse(
                normalized,
                NumberStyles.Number | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            return false;
        }

        if (parsed <= 0 || parsed > int.MaxValue)
            return false;

        quantity = Convert.ToInt32(Math.Round(parsed, 0, MidpointRounding.AwayFromZero));

        // Evitar que anios aislados terminen como cantidades.
        if (quantity is >= 1950 and <= 2200)
            return false;

        return quantity > 0;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value.ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
        }

        return sb.ToString();
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value.Substring(0, max) + "...";
}