using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace ERP.NSQuell.Servicios.Releases;

public sealed class VeritasReleasePdfDocument
{
    public string ClienteNombre { get; init; } = "Automotive Veritas de Mexico";
    public string? ScheduleNumber { get; init; }
    public string? SupplierNumber { get; init; }
    public string? ContractNumber { get; init; }
    public DateTime? DocumentDate { get; init; }
    public int? ContractQuantity { get; init; }
    public int? AccumulatedQuantity { get; init; }
    public string VersionText { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public List<VeritasReleasePdfDelivery> Deliveries { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
}

public sealed class VeritasReleasePdfDelivery
{
    public int Sequence { get; init; }
    public int ItemNumber { get; init; }
    public string PartNumber { get; init; } = string.Empty;
    public string PartDescription { get; init; } = string.Empty;
    public string Uom { get; init; } = "pcs";
    public int RequiredQuantity { get; init; }
    public DateTime RequiredDate { get; init; }
    public decimal? UnitPricePer100 { get; init; }
}

// VERITAS_PARSER_SPATIAL_V1_2
public static class VeritasReleasePdfParser
{
    private const double SameLineTolerance = 2.75d;

    private static readonly Regex ItemRowRegex = new(
        @"^(?<item>\d+)\s+(?<part>[A-Z0-9][A-Z0-9._/-]{2,})\s+(?<description>.+?)\s+(?<qty>\d[\d.]*,\d{2})\s+(?<price>\d[\d.]*,\d{2})\s+(?<arrival>\d{2}\.\d{2}\.\d{4})$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static VeritasReleasePdfDocument Parse(byte[] pdfBytes)
    {
        ValidatePdfSignature(pdfBytes);

        var lines = ExtractSpatialLines(pdfBytes);
        var text = string.Join(Environment.NewLine, lines);

        if (!text.Contains("Automotive Veritas", StringComparison.OrdinalIgnoreCase) ||
            !text.Contains("Contract No.", StringComparison.OrdinalIgnoreCase) ||
            !text.Contains("SCHEDULE", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("El PDF no corresponde a la plantilla de programacion VERITAS.");
        }

        var schedule = FindFirstValue(lines, @"^(?<value>\d+)\.\s*SCHEDULE$");
        var supplier = FindFirstValue(lines, @"\bSupplier\s+No\.\s+(?<value>\d+)");
        var contract = FindFirstValue(lines, @"\bContract\s+No\.\s+(?<value>.+)$");
        var documentDate = ParseDate(FindFirstValue(lines, @"^Date\s+(?<value>\d{2}\.\d{2}\.\d{4})$"));
        var contractQuantity = ParseEuropeanInteger(FindFirstValue(lines, @"^Contract\s+quantity\s+(?<value>[\d.]+)$"));
        var accumulatedQuantity = ParseEuropeanInteger(FindFirstValue(lines, @"^Accumulate\s+Qty\s+(?<value>[\d.]+)$"));

        contract = NormalizeContract(contract);

        if (string.IsNullOrWhiteSpace(contract))
            throw new InvalidOperationException("No se pudo leer Contract No. del documento VERITAS.");

        if (!documentDate.HasValue)
            throw new InvalidOperationException("No se pudo leer la fecha del documento VERITAS.");

        var deliveries = new List<VeritasReleasePdfDelivery>();
        var sequence = 1;

        foreach (var line in lines)
        {
            var match = ItemRowRegex.Match(line);
            if (!match.Success)
                continue;

            if (!int.TryParse(match.Groups["item"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemNumber))
                continue;

            var quantityDecimal = ParseEuropeanDecimal(match.Groups["qty"].Value);
            if (!quantityDecimal.HasValue || quantityDecimal.Value <= 0)
                continue;

            if (quantityDecimal.Value != decimal.Truncate(quantityDecimal.Value))
            {
                throw new InvalidOperationException(
                    $"La partida VERITAS {itemNumber} tiene una cantidad fraccionaria ({quantityDecimal.Value}). El Release requiere piezas enteras.");
            }

            var requiredDate = ParseDate(match.Groups["arrival"].Value);
            if (!requiredDate.HasValue)
                continue;

            deliveries.Add(new VeritasReleasePdfDelivery
            {
                Sequence = sequence,
                ItemNumber = itemNumber,
                PartNumber = match.Groups["part"].Value.Trim(),
                PartDescription = Regex.Replace(match.Groups["description"].Value.Trim(), @"\s+", " "),
                Uom = "pcs",
                RequiredQuantity = checked((int)quantityDecimal.Value),
                RequiredDate = requiredDate.Value.Date,
                UnitPricePer100 = ParseEuropeanDecimal(match.Groups["price"].Value)
            });

            sequence++;
        }

        if (deliveries.Count == 0)
        {
            throw new InvalidOperationException(
                "No se encontraron partidas con Part-No., Qty y Arrival date en el PDF VERITAS.");
        }

        var warnings = new List<string>();
        var duplicatedItems = deliveries
            .GroupBy(x => x.ItemNumber)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .ToList();

        if (duplicatedItems.Count > 0)
            warnings.Add($"Se detectaron numeros de partida repetidos: {string.Join(", ", duplicatedItems)}.");

        var distinctParts = deliveries
            .Select(x => x.PartNumber)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (distinctParts > 1)
            warnings.Add($"El documento VERITAS contiene {distinctParts} partes; se creara un renglon de Release por cada parte.");

        var overdue = deliveries.Count(x => x.RequiredDate.Date < documentDate.Value.Date);
        if (overdue > 0)
            warnings.Add($"El documento contiene {overdue} entrega(s) con Arrival date anterior a la fecha del documento; se conservaron como demanda vencida.");

        var version = $"Date {documentDate.Value:dd.MM.yyyy}";

        return new VeritasReleasePdfDocument
        {
            ScheduleNumber = schedule,
            SupplierNumber = supplier,
            ContractNumber = contract,
            DocumentDate = documentDate.Value.Date,
            ContractQuantity = contractQuantity,
            AccumulatedQuantity = accumulatedQuantity,
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

    private static List<string> ExtractSpatialLines(byte[] bytes)
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

        return allLines;
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
            var bucket = buckets.FirstOrDefault(x => Math.Abs(x.CenterY - centerY) <= SameLineTolerance);

            if (bucket == null)
            {
                bucket = new SpatialLine { CenterY = centerY };
                buckets.Add(bucket);
            }

            bucket.Words.Add(word);
            bucket.CenterY = bucket.Words.Average(GetCenterY);
        }

        return buckets
            .OrderByDescending(x => x.CenterY)
            .Select(x => string.Join(" ", x.Words
                .OrderBy(w => w.BoundingBox.Left)
                .Select(w => w.Text.Trim())))
            .Select(NormalizeLine)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
    }

    private static double GetCenterY(Word word) =>
        (word.BoundingBox.Bottom + word.BoundingBox.Top) / 2d;

    private static List<string> SplitAndNormalizeLines(string text) =>
        text
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeLine)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

    private static string NormalizeLine(string line) =>
        Regex.Replace(line.Trim(), @"\s+", " ");

    private static string? FindFirstValue(IEnumerable<string> lines, string pattern)
    {
        var regex = new Regex(pattern, RegexOptions.IgnoreCase);

        foreach (var line in lines)
        {
            var match = regex.Match(line);
            if (match.Success)
                return match.Groups["value"].Value.Trim();
        }

        return null;
    }

    private static string? NormalizeContract(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = Regex.Replace(value.Trim(), @"\s+", string.Empty);
        var match = Regex.Match(normalized, @"(?<value>\d{1,4}/[A-Z0-9]+/\d{3,20})", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["value"].Value.ToUpperInvariant() : normalized.ToUpperInvariant();
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

    private static int? ParseEuropeanInteger(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Replace(".", string.Empty, StringComparison.Ordinal).Trim();
        return int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static decimal? ParseEuropeanDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace(',', '.');

        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private sealed class SpatialLine
    {
        public double CenterY { get; set; }
        public List<Word> Words { get; } = new();
    }
}