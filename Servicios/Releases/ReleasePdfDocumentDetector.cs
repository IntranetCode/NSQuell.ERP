using System.Globalization;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace ERP.NSQuell.Servicios.Releases;

public enum ReleasePdfTemplate
{
    Unknown = 0,
    HufSupplierSchedule = 1,
    VeritasSchedule = 2
}

// RELEASE_PDF_DETECTOR_V1_2
public static class ReleasePdfDocumentDetector
{
    public static ReleasePdfTemplate Detect(byte[] pdfBytes)
    {
        ValidatePdfSignature(pdfBytes);

        using var stream = new MemoryStream(pdfBytes);
        using var document = PdfDocument.Open(stream);

        var pages = new List<string>();
        foreach (var page in document.GetPages().Take(3))
        {
            var text = ContentOrderTextExtractor.GetText(page);
            if (string.IsNullOrWhiteSpace(text))
                text = page.Text ?? string.Empty;

            pages.Add(text);
        }

        var normalized = RemoveDiacritics(string.Join(Environment.NewLine, pages)).ToUpperInvariant();

        if (normalized.Contains("SUPPLIER SCHEDULE REPORT", StringComparison.Ordinal) &&
            normalized.Contains("HUF", StringComparison.Ordinal))
        {
            return ReleasePdfTemplate.HufSupplierSchedule;
        }

        if (normalized.Contains("AUTOMOTIVE VERITAS DE MEXICO", StringComparison.Ordinal) &&
            normalized.Contains("CONTRACT NO.", StringComparison.Ordinal) &&
            normalized.Contains("SCHEDULE", StringComparison.Ordinal))
        {
            return ReleasePdfTemplate.VeritasSchedule;
        }

        return ReleasePdfTemplate.Unknown;
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

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(character);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}