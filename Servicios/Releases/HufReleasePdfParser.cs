using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace ERP.NSQuell.Servicios.Releases;

public sealed class HufReleasePdfDocument
{
    public string ClienteNombre { get; init; } = "Huf Mexico";
    public string? ScheduleNumber { get; init; }
    public string? PreviousScheduleNumber { get; init; }
    public string? OrderNumber { get; init; }
    public string? PartNumber { get; init; }
    public string? PartDescription { get; init; }
    public string? Uom { get; init; }
    public DateTime? DocumentDate { get; init; }
    public string VersionText { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public List<HufReleasePdfDelivery> Deliveries { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
}

public sealed class HufReleasePdfDelivery
{
    public int Sequence { get; init; }
    public string Category { get; init; } = string.Empty;
    public string SourcePeriod { get; init; } = string.Empty;
    public DateTime? LoadingDate { get; init; }
    public DateTime RequiredDate { get; init; }
    public int RequiredQuantity { get; init; }
    public int? CumulativeQuantity { get; init; }
    public bool IsArrear { get; init; }
}

// HUF_PARSER_SPATIAL_V1_1
public static class HufReleasePdfParser
{
    private const double SameLineTolerance = 2.75d;

    private static readonly Regex StandardRowRegex = new(
        @"^(?:(?<loading>\d{2}\.\d{2}\.\d{4})\s+)?(?<category>D|W|M)\s+(?<due>\d{2}\.\d{2}\.\d{4}|\d{2}\.\d{4})\s+(?<qty>\d[\d,]*)\s+(?<cum>\d[\d,]*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ArrearRowRegex = new(
        @"^(?:\*\s*)?ARREAR\s+(?<qty>\d[\d,]*)\s+(?<cum>\d[\d,]*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> InvalidPartTokens = new(
        new[]
        {
            "OUR", "CUSTOMER", "SUPPLIER", "SCHEDULE", "ORDER", "NUMBER",
            "PART", "NO", "CUM", "DATE", "APPROVED", "UOM"
        },
        StringComparer.OrdinalIgnoreCase);

    public static HufReleasePdfDocument Parse(byte[] pdfBytes, DateTime receptionDate)
    {
        ValidatePdfSignature(pdfBytes);

        var spatial = ExtractSpatialText(pdfBytes);
        var text = spatial.Text;
        var lines = spatial.Lines;

        if (!text.Contains("Supplier Schedule Report", StringComparison.OrdinalIgnoreCase) ||
            !text.Contains("Huf", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("El PDF no corresponde a la plantilla HUF Supplier Schedule Report.");
        }

        var order = FindValueAfterLabel(
            lines,
            "Order Number:",
            @"(?<value>\d{6,20})");

        var partNumber = FindValueAfterLabel(
            lines.Where(x => !x.Contains("Supplier Part No", StringComparison.OrdinalIgnoreCase)),
            "Part No:",
            @"(?<value>[A-Z0-9][A-Z0-9._/-]{4,})");

        var description = FindValueAfterLabel(
            lines,
            "Part Description:",
            @"(?<value>.+)$");

        var uom = FindValueAfterLabel(
            lines,
            "UOM:",
            @"(?<value>[A-Z0-9]{1,10})");

        var schedule = FindValueAfterLabel(
            lines.Where(x =>
                !x.Contains("Pervious Schedule No:", StringComparison.OrdinalIgnoreCase) &&
                !x.Contains("Previous Schedule No:", StringComparison.OrdinalIgnoreCase)),
            "Schedule No:",
            @"(?<value>\d{6,20})");

        var previousSchedule = FindValueAfterLabel(
            lines,
            "Pervious Schedule No:",
            @"(?<value>\d{6,20})")
            ?? FindValueAfterLabel(
                lines,
                "Previous Schedule No:",
                @"(?<value>\d{6,20})");

        var documentDate = ParseDate(FindFirstValue(
            lines,
            @"\bApproved\s+D\s+(?<value>\d{2}\.\d{2}\.\d{4})"));

        ValidateHeader(order, partNumber, schedule);

        description = CleanDescription(description);
        uom = CleanToken(uom);

        var deliveries = ExtractDeliveries(lines, receptionDate.Date);
        if (deliveries.Count == 0)
        {
            throw new InvalidOperationException(
                "No se encontraron renglones de demanda D, W, M o ARREAR en el PDF HUF.");
        }

        var warnings = new List<string>();
        if (deliveries.Any(x => x.Category == "W"))
            warnings.Add("Las semanas HUF se registraron con el lunes de la semana ISO como fecha operativa.");
        if (deliveries.Any(x => x.Category == "M"))
            warnings.Add("Los meses HUF se registraron con el primer dia del mes como fecha operativa.");
        if (deliveries.Any(x => x.IsArrear))
            warnings.Add("La demanda ARREAR se registro con la fecha de recepcion del archivo para tratarla como vencida/inmediata.");
        if (deliveries.Any(x => !x.LoadingDate.HasValue && !x.IsArrear))
            warnings.Add("Existen entregas sin Loading date; se conservaron con fecha de carga vacia.");
        if (string.IsNullOrWhiteSpace(description))
            warnings.Add("El PDF no proporciono una descripcion de parte legible; se utilizara la informacion del catalogo ERP al calcular.");
        if (string.IsNullOrWhiteSpace(uom))
            warnings.Add("El PDF no proporciono una unidad de medida legible.");

        var version = documentDate.HasValue
            ? $"Print {documentDate.Value:dd.MM.yyyy}"
            : $"Schedule {schedule}";

        return new HufReleasePdfDocument
        {
            ScheduleNumber = schedule,
            PreviousScheduleNumber = previousSchedule,
            OrderNumber = order,
            PartNumber = partNumber,
            PartDescription = description,
            Uom = uom,
            DocumentDate = documentDate,
            VersionText = version,
            Sha256 = Convert.ToHexString(SHA256.HashData(pdfBytes)),
            Deliveries = deliveries,
            Warnings = warnings
        };
    }

    private static void ValidatePdfSignature(byte[] pdfBytes)
    {
        if (pdfBytes == null || pdfBytes.Length == 0)
            throw new InvalidOperationException("El PDF esta vacio.");

        if (pdfBytes.Length < 5 ||
            pdfBytes[0] != (byte)'%' ||
            pdfBytes[1] != (byte)'P' ||
            pdfBytes[2] != (byte)'D' ||
            pdfBytes[3] != (byte)'F' ||
            pdfBytes[4] != (byte)'-')
        {
            throw new InvalidOperationException("El archivo no contiene una firma PDF valida.");
        }
    }

    private static SpatialText ExtractSpatialText(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var document = PdfDocument.Open(stream);

        var allLines = new List<string>();

        foreach (var page in document.GetPages())
        {
            var pageLines = BuildSpatialLines(page);

            if (pageLines.Count == 0)
            {
                var fallback = ContentOrderTextExtractor.GetText(page);
                pageLines = SplitAndNormalizeLines(
                    string.IsNullOrWhiteSpace(fallback)
                        ? page.Text ?? string.Empty
                        : fallback);
            }

            allLines.AddRange(pageLines);
        }

        return new SpatialText
        {
            Lines = allLines,
            Text = string.Join(Environment.NewLine, allLines)
        };
    }

    private static List<string> BuildSpatialLines(Page page)
    {
        var words = page
            .GetWords(NearestNeighbourWordExtractor.Instance)
            .Where(x => !string.IsNullOrWhiteSpace(x.Text))
            .OrderByDescending(GetCenterY)
            .ThenBy(x => x.BoundingBox.Left)
            .ToList();

        var buckets = new List<SpatialLine>();

        foreach (var word in words)
        {
            var centerY = GetCenterY(word);

            var bucket = buckets
                .Select(x => new
                {
                    Line = x,
                    Distance = Math.Abs(x.CenterY - centerY)
                })
                .Where(x => x.Distance <= SameLineTolerance)
                .OrderBy(x => x.Distance)
                .Select(x => x.Line)
                .FirstOrDefault();

            if (bucket == null)
            {
                bucket = new SpatialLine(centerY);
                buckets.Add(bucket);
            }

            bucket.Add(word, centerY);
        }

        return buckets
            .OrderByDescending(x => x.CenterY)
            .Select(x => NormalizeLine(string.Join(
                " ",
                x.Words
                    .OrderBy(w => w.BoundingBox.Left)
                    .Select(w => w.Text))))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
    }

    private static double GetCenterY(Word word)
    {
        return (word.BoundingBox.Bottom + word.BoundingBox.Top) / 2d;
    }

    private static List<string> SplitAndNormalizeLines(string text)
    {
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeLine)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
    }

    private static string NormalizeLine(string value)
    {
        var normalized = value
            .Replace('\u00A0', ' ')
            .Trim();

        normalized = Regex.Replace(normalized, @"\s+", " ");
        normalized = Regex.Replace(normalized, @"\s+([:;,])", "$1");

        return normalized.Trim();
    }

    private static string? FindValueAfterLabel(
        IEnumerable<string> lines,
        string label,
        string valuePattern)
    {
        foreach (var line in lines)
        {
            var index = line.IndexOf(label, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                continue;

            var tail = line[(index + label.Length)..].Trim();
            var match = Regex.Match(tail, valuePattern, RegexOptions.IgnoreCase);

            if (match.Success)
                return CleanToken(match.Groups["value"].Value);
        }

        return null;
    }

    private static string? FindFirstValue(
        IEnumerable<string> lines,
        string pattern)
    {
        foreach (var line in lines)
        {
            var match = Regex.Match(line, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
                return CleanToken(match.Groups["value"].Value);
        }

        return null;
    }

    private static void ValidateHeader(
        string? order,
        string? partNumber,
        string? schedule)
    {
        if (string.IsNullOrWhiteSpace(order) ||
            !Regex.IsMatch(order, @"^\d{6,20}$"))
        {
            throw new InvalidOperationException(
                "No se pudo identificar correctamente el Order Number del encabezado HUF.");
        }

        if (string.IsNullOrWhiteSpace(schedule) ||
            !Regex.IsMatch(schedule, @"^\d{6,20}$"))
        {
            throw new InvalidOperationException(
                "No se pudo identificar correctamente el Schedule No. del encabezado HUF.");
        }

        if (string.IsNullOrWhiteSpace(partNumber) ||
            partNumber.Length > 120 ||
            InvalidPartTokens.Contains(partNumber) ||
            !partNumber.Any(char.IsDigit) ||
            !Regex.IsMatch(partNumber, @"^[A-Z0-9][A-Z0-9._/-]{4,}$", RegexOptions.IgnoreCase))
        {
            throw new InvalidOperationException(
                "No se pudo identificar correctamente el Part No. del encabezado HUF. No se consulto ERP_Partes para evitar relacionar una referencia incorrecta.");
        }
    }

    private static string? CleanDescription(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var result = NormalizeLine(value);

        var labels = new[]
        {
            " UOM:",
            " Schedule No:",
            " Pervious Schedule No:",
            " Previous Schedule No:",
            " Approved D"
        };

        foreach (var label in labels)
        {
            var index = result.IndexOf(label, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
                result = result[..index].Trim();
        }

        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private static string? CleanToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim().Trim(',', ';', ':');
    }

    private static List<HufReleasePdfDelivery> ExtractDeliveries(
        IEnumerable<string> lines,
        DateTime receptionDate)
    {
        var result = new List<HufReleasePdfDelivery>();
        var sequence = 1;

        foreach (var sourceLine in lines)
        {
            var line = NormalizeLine(sourceLine);

            var arrearMatch = ArrearRowRegex.Match(line);
            if (arrearMatch.Success)
            {
                var quantity = ParseInteger(arrearMatch.Groups["qty"].Value);
                if (quantity <= 0)
                    continue;

                result.Add(new HufReleasePdfDelivery
                {
                    Sequence = sequence++,
                    Category = "ARREAR",
                    SourcePeriod = "ARREAR",
                    LoadingDate = null,
                    RequiredDate = receptionDate,
                    RequiredQuantity = quantity,
                    CumulativeQuantity = ParseNullableInteger(arrearMatch.Groups["cum"].Value),
                    IsArrear = true
                });
                continue;
            }

            var match = StandardRowRegex.Match(line);
            if (!match.Success)
                continue;

            var category = match.Groups["category"].Value.ToUpperInvariant();
            var sourcePeriod = match.Groups["due"].Value;
            var requiredDate = ConvertSourcePeriod(category, sourcePeriod, receptionDate);
            var loadingDate = ParseDate(match.Groups["loading"].Value);
            var quantityRequired = ParseInteger(match.Groups["qty"].Value);

            if (quantityRequired <= 0)
                continue;

            result.Add(new HufReleasePdfDelivery
            {
                Sequence = sequence++,
                Category = category,
                SourcePeriod = sourcePeriod,
                LoadingDate = loadingDate,
                RequiredDate = requiredDate,
                RequiredQuantity = quantityRequired,
                CumulativeQuantity = ParseNullableInteger(match.Groups["cum"].Value),
                IsArrear = false
            });
        }

        return result;
    }

    private static DateTime ConvertSourcePeriod(
        string category,
        string sourcePeriod,
        DateTime receptionDate)
    {
        if (category == "D")
        {
            return ParseDate(sourcePeriod)
                ?? throw new InvalidOperationException(
                    $"Fecha diaria HUF invalida: {sourcePeriod}.");
        }

        var parts = sourcePeriod.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out var first) ||
            !int.TryParse(parts[1], out var year))
        {
            throw new InvalidOperationException(
                $"Periodo HUF invalido: {category} {sourcePeriod}.");
        }

        if (category == "W")
        {
            try
            {
                return ISOWeek.ToDateTime(year, first, DayOfWeek.Monday);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new InvalidOperationException(
                    $"Semana ISO HUF invalida: {sourcePeriod}.");
            }
        }

        if (category == "M")
        {
            if (first < 1 || first > 12)
            {
                throw new InvalidOperationException(
                    $"Mes HUF invalido: {sourcePeriod}.");
            }

            return new DateTime(year, first, 1);
        }

        return receptionDate;
    }

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return DateTime.TryParseExact(
            value.Trim(),
            "dd.MM.yyyy",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
                ? date.Date
                : null;
    }

    private static int ParseInteger(string value)
    {
        var normalized = value
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Trim();

        if (!int.TryParse(
                normalized,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var number))
        {
            throw new InvalidOperationException(
                $"Cantidad HUF invalida: {value}.");
        }

        return number;
    }

    private static int? ParseNullableInteger(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Trim();

        return int.TryParse(
            normalized,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var number)
                ? number
                : null;
    }

    private sealed class SpatialText
    {
        public List<string> Lines { get; init; } = new();
        public string Text { get; init; } = string.Empty;
    }

    private sealed class SpatialLine
    {
        public SpatialLine(double centerY)
        {
            CenterY = centerY;
        }

        public double CenterY { get; private set; }
        public int Count { get; private set; }
        public List<Word> Words { get; } = new();

        public void Add(Word word, double centerY)
        {
            Words.Add(word);
            Count++;
            CenterY += (centerY - CenterY) / Count;
        }
    }
}