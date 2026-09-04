using ExcelDataReader;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace ERP.NSQuell.Servicios.Releases;

// GENERIC_RELEASE_READER_V2_0
// Fallback universal para documentos que no coinciden con una plantilla especializada.
// La prioridad sigue siendo: parser conocido -> parser generico.
//
// Reglas:
// - La identidad y el cliente se obtienen de ERP_Partes.
// - Se reconocen NumeroParte, ReferenciaSAP, Designacion y descripciones unicas.
// - Se soportan matrices con fechas reales y matrices con semanas (Sem/CW/WK/WEEK).
// - Los PDF solo generan entregas cuando existe una relacion segura fecha/cantidad.
// - Nunca se inventa ParteID ni se crea demanda si falta fecha o cantidad positiva.
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

    private sealed class PeriodColumn
    {
        public int Column { get; init; }
        public DateTime Date { get; init; }
        public string Label { get; init; } = string.Empty;
        public bool InferredFromWeek { get; init; }
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
            ExtractFromTable(table, index, rows, warnings, fileName);

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
        ExtractFromTable(table, index, rows, warnings, fileName);

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
        {
            throw new InvalidOperationException(
                "El PDF no contiene una capa de texto util. Si es un escaneo/imagen no se generara demanda automaticamente sin OCR.");
        }

        var normalizedDocument = Normalize(text);
        var preferredClientId = InferPreferredClientFromText(text, index);
        var candidateParts = new Dictionary<int, (GenericReleaseKnownPart Part, string RawAlias)>();

        foreach (var alias in index.SearchAliases)
        {
            if (alias.Normalized.Length < 6)
                continue;

            if (preferredClientId.HasValue && alias.Part.ClienteID != preferredClientId.Value)
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
            var window = FindTextWindow(
                text,
                candidate.RawAlias,
                candidate.Part.NumeroParte,
                candidate.Part.ReferenciaSAP,
                candidate.Part.Designacion,
                candidate.Part.Descripcion);

            var deliveries = ExtractTextDeliveries(window);

            if (deliveries.Count == 0)
            {
                AddWarningOnce(
                    warnings,
                    $"Se detecto la parte {candidate.Part.NumeroParte} en el PDF, pero no se pudo asociar con seguridad una fecha de entrega y cantidad.");
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

        // Si el PDF contiene una demanda legible pero la parte/cliente aun no
        // existe o no se pudo resolver contra ERP_Partes, se conserva como
        // PENDIENTE para validacion humana. Nunca se inventa una ParteID.
        if (rows.Count == 0)
        {
            rows.AddRange(ExtractUnresolvedPdfDemandRows(
                text,
                fileName,
                warnings));
        }

        return BuildDocument(bytes, fileName, rows, warnings, "PDF");
    }

    private static void ExtractFromTable(
        DataTable table,
        PartIndex index,
        List<GenericReleaseRow> output,
        List<string> warnings,
        string? fileName)
    {
        if (table.Rows.Count == 0 || table.Columns.Count == 0)
            return;

        var collected = new Dictionary<int, GenericReleaseRow>();
        var preferredClientId = InferPreferredClientFromTable(table, index);

        for (var row = 0; row < table.Rows.Count; row++)
        {
            for (var col = 0; col < table.Columns.Count; col++)
            {
                var raw = CellText(Get(table, row, col));
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                var matches = ResolveCellParts(raw, index, preferredClientId);
                if (matches.Count == 0)
                    continue;

                var distinctParts = matches
                    .Select(x => x.Part)
                    .GroupBy(x => x.ParteID)
                    .Select(x => x.First())
                    .ToList();

                if (distinctParts.Count != 1)
                {
                    AddWarningOnce(
                        warnings,
                        $"La celda '{Truncate(raw, 80)}' coincide con mas de una parte activa; se omitio por ambiguedad.");
                    continue;
                }

                var part = distinctParts[0];
                var deliveries = ExtractTableDeliveries(table, row, col, fileName, warnings);

                if (deliveries.Count == 0)
                    continue;

                if (!collected.TryGetValue(part.ParteID, out var target))
                {
                    target = new GenericReleaseRow
                    {
                        Part = part,
                        SourceToken = raw,
                        Uom = DetectUomInTable(table, row),
                        SourceReference = Path.GetFileNameWithoutExtension(fileName ?? string.Empty),
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
        int partCol,
        string? fileName,
        List<string> warnings)
    {
        var result = new List<GenericReleaseDelivery>();

        // Caso 0: tabla clasica con encabezados semanticos (fecha entrega / cantidad).
        var mapped = ExtractHeaderMappedDelivery(table, partRow, partCol);
        if (mapped.Count > 0)
            return mapped;

        // Evita tomar historicos de embarque/recibo/precio como nueva demanda.
        if (IsExplicitNonDemandRow(table, partRow))
            return result;

        // Caso 1: matriz horizontal con fechas reales o semanas.
        var periods = FindBestPeriodColumns(table, partRow, fileName, warnings);

        if (periods.Count > 0)
        {
            foreach (var period in periods)
            {
                if (period.Column == partCol)
                    continue;

                if (!TryPositiveInteger(Get(table, partRow, period.Column), out var quantity))
                    continue;

                result.Add(new GenericReleaseDelivery
                {
                    RequiredDate = period.Date.Date,
                    RequiredQuantity = quantity,
                    PeriodLabel = period.Label
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
        for (var row = partRow; row <= Math.Min(table.Rows.Count - 1, partRow + 12); row++)
        {
            if (IsExplicitNonDemandRow(table, row))
                continue;

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

    private static List<GenericReleaseDelivery> ExtractHeaderMappedDelivery(
        DataTable table,
        int partRow,
        int partCol)
    {
        if (partRow <= 0)
            return new List<GenericReleaseDelivery>();

        foreach (var headerRow in CandidateHeaderRows(partRow, includeCurrent: false).OrderByDescending(x => x))
        {
            int? dateColumn = null;
            int? quantityColumn = null;

            for (var col = 0; col < table.Columns.Count; col++)
            {
                var header = NormalizeWords(CellText(Get(table, headerRow, col)));
                if (string.IsNullOrWhiteSpace(header))
                    continue;

                if (!dateColumn.HasValue && IsDeliveryDateHeader(header))
                    dateColumn = col;

                if (!quantityColumn.HasValue && IsQuantityHeader(header))
                    quantityColumn = col;
            }

            if (!dateColumn.HasValue || !quantityColumn.HasValue)
                continue;

            if (dateColumn.Value == partCol || quantityColumn.Value == partCol)
                continue;

            if (!TryReadDate(Get(table, partRow, dateColumn.Value), out var date))
                continue;

            if (!TryPositiveInteger(Get(table, partRow, quantityColumn.Value), out var quantity))
                continue;

            return new List<GenericReleaseDelivery>
            {
                new()
                {
                    RequiredDate = date.Date,
                    RequiredQuantity = quantity,
                    PeriodLabel = $"GEN {date:dd/MM/yyyy}"
                }
            };
        }

        return new List<GenericReleaseDelivery>();
    }

    private static List<PeriodColumn> FindBestPeriodColumns(
        DataTable table,
        int partRow,
        string? fileName,
        List<string> warnings)
    {
        var best = new List<PeriodColumn>();
        var bestScore = 0;

        foreach (var headerRow in CandidateHeaderRows(partRow, includeCurrent: true))
        {
            var directDates = new List<PeriodColumn>();
            var weeks = new List<(int Column, int Week, string Label)>();

            for (var col = 0; col < table.Columns.Count; col++)
            {
                var value = Get(table, headerRow, col);

                if (TryReadDate(value, out var date))
                {
                    directDates.Add(new PeriodColumn
                    {
                        Column = col,
                        Date = date.Date,
                        Label = $"GEN {date:dd/MM/yyyy}",
                        InferredFromWeek = false
                    });
                    continue;
                }

                if (TryReadWeekLabel(value, out var week, out var label))
                    weeks.Add((col, week, label));
            }

            if (directDates.Count >= 2)
            {
                var score = 1000 + directDates.Count;
                if (score > bestScore)
                {
                    best = directDates;
                    bestScore = score;
                }
            }

            if (weeks.Count >= 2)
            {
                var inferred = ResolveWeekColumns(weeks, fileName);
                var score = 500 + inferred.Count;

                if (score > bestScore)
                {
                    best = inferred;
                    bestScore = score;
                }
            }
        }

        if (best.Any(x => x.InferredFromWeek))
        {
            AddWarningOnce(
                warnings,
                "El documento usa semanas sin fecha completa. El lector generico convirtio Sem/CW/WK a lunes de semana ISO y resolvio el cambio de anio automaticamente.");
        }

        return best;
    }

    private static List<PeriodColumn> ResolveWeekColumns(
        List<(int Column, int Week, string Label)> weeks,
        string? fileName)
    {
        var ordered = weeks.OrderBy(x => x.Column).ToList();
        if (ordered.Count == 0)
            return new List<PeriodColumn>();

        // Regla NS Quell: W43 / CW43 / Sem 43 pertenece inicialmente al
        // anio calendario actual. Solo cambia de anio cuando la secuencia
        // cruza de W52/W53 hacia W1.
        var anchor = DateTime.Today;
        var year = anchor.Year;
        var previousWeek = ordered[0].Week;
        var result = new List<PeriodColumn>();

        foreach (var item in ordered)
        {
            if (result.Count > 0)
            {
                if (previousWeek >= 40 && item.Week <= 15)
                    year++;
                else if (previousWeek <= 15 && item.Week >= 40)
                    year--;
            }

            year = AdjustYearForIsoWeek(year, item.Week, anchor);

            result.Add(new PeriodColumn
            {
                Column = item.Column,
                Date = ISOWeek.ToDateTime(year, item.Week, DayOfWeek.Monday).Date,
                Label = string.IsNullOrWhiteSpace(item.Label)
                    ? $"CW{item.Week}"
                    : item.Label.Trim().ToUpperInvariant(),
                InferredFromWeek = true
            });

            previousWeek = item.Week;
        }

        return result;
    }

    private static DateTime InferReferenceDate(string? fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName ?? string.Empty);

        var ymd = Regex.Match(name, @"(?<!\d)(?<y>20\d{2})[-_]?((?<m>0[1-9]|1[0-2]))[-_]?(?<d>0[1-9]|[12]\d|3[01])(?!\d)");
        if (ymd.Success &&
            int.TryParse(ymd.Groups["y"].Value, out var y) &&
            int.TryParse(ymd.Groups["m"].Value, out var m) &&
            int.TryParse(ymd.Groups["d"].Value, out var d))
        {
            try
            {
                return new DateTime(y, m, d);
            }
            catch
            {
                // Usa la fecha actual.
            }
        }

        return DateTime.Today;
    }

    private static int FindClosestIsoYear(DateTime anchor, int week)
    {
        var candidates = new List<(int Year, int Distance)>();

        for (var year = anchor.Year - 1; year <= anchor.Year + 1; year++)
        {
            if (week < 1 || week > ISOWeek.GetWeeksInYear(year))
                continue;

            var date = ISOWeek.ToDateTime(year, week, DayOfWeek.Monday).Date;
            candidates.Add((year, Math.Abs((date - anchor.Date).Days)));
        }

        return candidates.Count == 0
            ? anchor.Year
            : candidates.OrderBy(x => x.Distance).ThenBy(x => Math.Abs(x.Year - anchor.Year)).First().Year;
    }

    private static int AdjustYearForIsoWeek(int preferredYear, int week, DateTime anchor)
    {
        if (week >= 1 && week <= ISOWeek.GetWeeksInYear(preferredYear))
            return preferredYear;

        var candidates = new[] { preferredYear - 1, preferredYear + 1 }
            .Where(year => week >= 1 && week <= ISOWeek.GetWeeksInYear(year))
            .Select(year => new
            {
                Year = year,
                Distance = Math.Abs((ISOWeek.ToDateTime(year, week, DayOfWeek.Monday).Date - anchor.Date).Days)
            })
            .OrderBy(x => x.Distance)
            .ToList();

        return candidates.Count > 0 ? candidates[0].Year : preferredYear;
    }

    private static bool TryReadWeekLabel(object? value, out int week, out string label)
    {
        week = 0;
        label = string.Empty;

        var text = CellText(value).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var match = Regex.Match(
            text,
            @"^(?:SEM(?:ANA)?|CW|WK|WEEK|W|KW)\s*[.\-#:]*\s*(?<week>\d{1,2})$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!match.Success ||
            !int.TryParse(match.Groups["week"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out week) ||
            week is < 1 or > 53)
        {
            week = 0;
            return false;
        }

        label = text;
        return true;
    }

    private static bool IsExplicitNonDemandRow(DataTable table, int row)
    {
        if (row < 0 || row >= table.Rows.Count)
            return false;

        var parts = new List<string>();
        for (var col = 0; col < table.Columns.Count; col++)
        {
            var text = CellText(Get(table, row, col));
            if (!string.IsNullOrWhiteSpace(text))
                parts.Add(text);
        }

        if (parts.Count == 0)
            return false;

        var normalized = NormalizeWords(string.Join(" ", parts));

        var positive = ContainsAny(
            normalized,
            "PLAN", "FORECAST", "DEMAND", "REQUIREMENT", "REQUIRED",
            "RELEASE", "CALL OFF", "CALL-OFF", "CANTIDAD REQUERIDA", "PEDIDO");

        var negative = ContainsAny(
            normalized,
            "QTY SHIP", "QTY SHIPPED", "SHIPPED", "RECEIPT", "RECEIVED",
            "INVENTORY", "STOCK", "UNIT PRICE", "PRECIO UNITARIO", "AMOUNT", "MONTO TOTAL",
            "BALANCE", "BACKLOG", "INVOICE", "INVOICE NO", "SALDO");

        if (!positive && Regex.IsMatch(normalized, @"(^|\s)PO($|\s)", RegexOptions.IgnoreCase))
            negative = true;

        return negative && !positive;
    }

    private static bool IsDeliveryDateHeader(string normalized)
    {
        return ContainsAny(
            normalized,
            "FECHA ENTREGA", "FEC ENTREGA", "FECHA REQUERIDA", "FECHA REQUERIMIENTO",
            "DELIVERY DATE", "REQUIRED DATE", "DUE DATE", "SHIP DATE", "ARRIVAL DATE");
    }

    private static bool IsQuantityHeader(string normalized)
    {
        if (ContainsAny(normalized, "PRICE", "PRECIO", "AMOUNT", "MONTO", "RECEIPT", "RECEIVED", "SHIP"))
            return false;

        return normalized == "QTY" ||
               normalized == "PCS" ||
               normalized == "PIECES" ||
               ContainsAny(normalized, "CANTIDAD", "QUANTITY", "QTY REQUIRED", "REQUIRED QTY", "PIEZAS");
    }

    private static bool ContainsAny(string text, params string[] values)
    {
        foreach (var value in values)
        {
            if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static IEnumerable<int> CandidateHeaderRows(int partRow, bool includeCurrent)
    {
        if (partRow < 0)
            yield break;

        var seen = new HashSet<int>();
        var last = includeCurrent ? partRow : partRow - 1;
        if (last < 0)
            yield break;

        // Los encabezados globales suelen vivir al inicio de la hoja.
        var topEnd = Math.Min(last, 39);
        for (var row = 0; row <= topEnd; row++)
        {
            if (seen.Add(row))
                yield return row;
        }

        // Tambien cubre bloques repetidos con encabezado cercano a la parte.
        var nearStart = Math.Max(0, last - 40);
        for (var row = nearStart; row <= last; row++)
        {
            if (seen.Add(row))
                yield return row;
        }
    }

    private static int? InferPreferredClientFromTable(DataTable table, PartIndex index)
    {
        var scores = new Dictionary<int, int>();

        for (var row = 0; row < table.Rows.Count; row++)
        {
            for (var col = 0; col < table.Columns.Count; col++)
            {
                var raw = CellText(Get(table, row, col));
                if (string.IsNullOrWhiteSpace(raw) || raw.Length > 300)
                    continue;

                ScoreClientEvidence(raw, index, scores);
            }
        }

        return SelectPreferredClient(scores);
    }

    private static int? InferPreferredClientFromText(string text, PartIndex index)
    {
        var scores = new Dictionary<int, int>();
        var normalized = Normalize(text);

        // En PDF no se recorre cada token porque PdfPig puede perder columnas.
        foreach (var alias in index.SearchAliases)
        {
            if (alias.Normalized.Length < 6 ||
                !normalized.Contains(alias.Normalized, StringComparison.Ordinal))
            {
                continue;
            }

            AddClientScore(scores, alias.Part.ClienteID, alias.Normalized.Any(char.IsDigit) ? 5 : 3);
        }

        return SelectPreferredClient(scores);
    }

    private static void ScoreClientEvidence(
        string raw,
        PartIndex index,
        Dictionary<int, int> scores)
    {
        var normalized = Normalize(raw);
        if (normalized.Length < 4)
            return;

        if (index.ByAlias.TryGetValue(normalized, out var exact))
        {
            var exactClients = exact.Select(x => x.ClienteID).Distinct().ToList();
            if (exactClients.Count == 1)
                AddClientScore(scores, exactClients[0], 12);
            return;
        }

        var matches = ResolveCellParts(raw, index)
            .Select(x => x.Part.ClienteID)
            .Distinct()
            .ToList();

        if (matches.Count == 1)
            AddClientScore(scores, matches[0], 3);
    }

    private static void AddClientScore(Dictionary<int, int> scores, int clientId, int points)
    {
        scores.TryGetValue(clientId, out var current);
        scores[clientId] = current + points;
    }

    private static int? SelectPreferredClient(Dictionary<int, int> scores)
    {
        if (scores.Count == 0)
            return null;

        var ordered = scores
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key)
            .ToList();

        var winner = ordered[0];
        var runnerUp = ordered.Count > 1 ? ordered[1].Value : 0;

        // Un identificador exacto unico basta. Para coincidencias parciales se
        // exige evidencia repetida y ventaja clara sobre otro cliente.
        if (winner.Value >= 10 ||
            (winner.Value >= 6 && winner.Value >= runnerUp * 2))
        {
            return winner.Key;
        }

        return null;
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
            .GroupBy(x => x.Part.ParteID > 0
                ? $"ERP:{x.Part.ParteID}"
                : $"RAW:{Normalize(x.Part.NumeroParte)}")
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
                $"El lector generico {sourceType} no encontro una combinacion segura de parte/designacion ERP + fecha de entrega + cantidad.{extra}");
        }

        var clients = rows
            .Where(x => x.Part.ClienteID > 0)
            .Select(x => new { x.Part.ClienteID, x.Part.ClienteNombre })
            .Distinct()
            .ToList();

        if (clients.Count > 1)
        {
            throw new InvalidOperationException(
                "El documento contiene partes activas de mas de un cliente ERP. No se puede determinar automaticamente un solo cliente.");
        }

        var clientId = clients.Count == 1 ? clients[0].ClienteID : 0;
        var clientName = clients.Count == 1 ? clients[0].ClienteNombre : string.Empty;

        warnings.Insert(
            0,
            clients.Count == 1
                ? $"Lector generico V3 {sourceType}: parte/designacion y cliente se resolvieron contra ERP_Partes; el archivo aporta demanda, fechas y cantidades."
                : $"Lector generico V3 {sourceType}: se extrajo demanda, pero cliente/parte requieren validacion antes de guardar.");

        return new GenericReleaseDocument
        {
            ClienteID = clientId,
            ClienteNombre = clientName,
            FolioCliente = Path.GetFileNameWithoutExtension(fileName ?? string.Empty),
            DocumentDate = null,
            VersionText = $"Lectura generica V2 {DateTime.Today:dd.MM.yyyy}",
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
            AddAlias(part.NumeroParte, part, byAlias, search, allowTextOnly: false);
            AddAlias(part.ReferenciaSAP, part, byAlias, search, allowTextOnly: false);
            AddAlias(part.Designacion, part, byAlias, search, allowTextOnly: true);
            AddAlias(part.Descripcion, part, byAlias, search, allowTextOnly: true);
        }

        var searchable = search
            .Where(x =>
            {
                if (!byAlias.TryGetValue(x.Normalized, out var owners))
                    return false;

                var uniqueOwners = owners.Select(p => p.ParteID).Distinct().Count();
                return uniqueOwners == 1 &&
                       (x.Normalized.Any(char.IsDigit) || x.Normalized.Length >= 10);
            })
            .GroupBy(x => new { x.Normalized, x.Part.ParteID })
            .Select(x => x.First())
            .OrderByDescending(x => x.Normalized.Length)
            .ToList();

        return new PartIndex
        {
            ByAlias = byAlias,
            SearchAliases = searchable
        };
    }

    private static void AddAlias(
        string? raw,
        GenericReleaseKnownPart part,
        Dictionary<string, List<GenericReleaseKnownPart>> byAlias,
        List<(string Normalized, string Raw, GenericReleaseKnownPart Part)> search,
        bool allowTextOnly)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return;

        var normalized = Normalize(raw);
        if (normalized.Length < 4)
            return;

        if (!normalized.Any(char.IsDigit) && (!allowTextOnly || normalized.Length < 8))
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
        PartIndex index,
        int? preferredClientId = null)
    {
        var result = new List<(GenericReleaseKnownPart Part, string Alias)>();
        var normalized = Normalize(raw);

        if (index.ByAlias.TryGetValue(normalized, out var exact))
        {
            var exactCandidates = preferredClientId.HasValue
                ? exact.Where(x => x.ClienteID == preferredClientId.Value)
                : exact;

            result.AddRange(exactCandidates.Select(x => (x, raw)));
            return result;
        }

        if (normalized.Length < 6)
            return result;

        foreach (var alias in index.SearchAliases)
        {
            if (alias.Normalized.Length < 6)
                continue;

            if (preferredClientId.HasValue && alias.Part.ClienteID != preferredClientId.Value)
                continue;

            if (normalized.Contains(alias.Normalized, StringComparison.Ordinal) ||
                (normalized.Length >= 8 && alias.Normalized.Contains(normalized, StringComparison.Ordinal)))
            {
                result.Add((alias.Part, alias.Raw));
            }
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

            var start = Math.Max(0, idx - 700);
            var length = Math.Min(text.Length - start, 2600);
            return text.Substring(start, length);
        }

        return text.Length <= 3500 ? text : text.Substring(0, 3500);
    }

    private static List<GenericReleaseRow> ExtractUnresolvedPdfDemandRows(
        string text,
        string? fileName,
        List<string> warnings)
    {
        var result = new List<GenericReleaseRow>();
        if (string.IsNullOrWhiteSpace(text))
            return result;

        const string datePattern =
            @"(?<date>(?:0?[1-9]|[12]\d|3[01])[./-](?:0?[1-9]|1[0-2])[./-](?:20)?\d{2}|20\d{2}[./-](?:0?[1-9]|1[0-2])[./-](?:0?[1-9]|[12]\d|3[01]))";

        var demand = new Regex(
            datePattern +
            @"\s+(?<qty>\d[\d., ]{0,16})\s*(?<uom>PZA|PZAS|PIEZA|PIEZAS|PCS|PC|PIECE|PIECES)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => Regex.Replace(x, @"\s+", " ").Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        foreach (var line in lines)
        {
            foreach (Match match in demand.Matches(line))
            {
                if (!TryParseDateText(match.Groups["date"].Value, out var date))
                    continue;

                if (!TryParsePdfDemandQuantity(
                        match.Groups["qty"].Value,
                        line,
                        match.Index + match.Length,
                        out var quantity))
                {
                    continue;
                }

                var prefix = line.Substring(0, match.Index).Trim();
                if (string.IsNullOrWhiteSpace(prefix))
                    continue;

                var tokens = Regex.Matches(
                        prefix.ToUpperInvariant(),
                        @"(?<![A-Z0-9])(?<part>[A-Z0-9][A-Z0-9._/-]{4,})(?![A-Z0-9])",
                        RegexOptions.CultureInvariant)
                    .Cast<Match>()
                    .Where(x =>
                    {
                        var value = x.Groups["part"].Value;
                        if (!value.Any(char.IsDigit))
                            return false;

                        var digitsOnly = value.All(char.IsDigit);
                        if (digitsOnly && value.Length <= 5)
                            return false; // posicion 00010, 00020, etc.

                        if (digitsOnly && int.TryParse(value, out var numeric) &&
                            numeric is >= 1950 and <= 2200)
                        {
                            return false;
                        }

                        return value.Length <= 80;
                    })
                    .ToList();

                if (tokens.Count == 0)
                    continue;

                var token = tokens.Last();
                var partNumber = token.Groups["part"].Value.Trim();
                var descriptionStart = token.Index + token.Length;
                var description = descriptionStart < prefix.Length
                    ? Regex.Replace(prefix.Substring(descriptionStart), @"\s+", " ").Trim(' ', '-', ':', ';', '|')
                    : string.Empty;

                if (description.Length > 300)
                    description = description.Substring(0, 300);

                var existing = result.FirstOrDefault(x =>
                    Normalize(x.Part.NumeroParte) == Normalize(partNumber));

                if (existing == null)
                {
                    existing = new GenericReleaseRow
                    {
                        Part = new GenericReleaseKnownPart
                        {
                            ParteID = 0,
                            ClienteID = 0,
                            ClienteNombre = string.Empty,
                            NumeroParte = partNumber,
                            ReferenciaSAP = partNumber,
                            Descripcion = string.IsNullOrWhiteSpace(description) ? null : description,
                            Designacion = string.IsNullOrWhiteSpace(description) ? null : description
                        },
                        SourceToken = partNumber,
                        Uom = "PIEZA",
                        SourceReference = Path.GetFileNameWithoutExtension(fileName ?? string.Empty),
                        Deliveries = new List<GenericReleaseDelivery>()
                    };
                    result.Add(existing);
                }

                if (!existing.Deliveries.Any(x =>
                    x.RequiredDate.Date == date.Date &&
                    x.RequiredQuantity == quantity))
                {
                    existing.Deliveries.Add(new GenericReleaseDelivery
                    {
                        Sequence = existing.Deliveries.Count + 1,
                        RequiredDate = date.Date,
                        RequiredQuantity = quantity,
                        PeriodLabel = $"GEN {date:dd/MM/yyyy}"
                    });
                }
            }
        }

        if (result.Count > 0)
        {
            AddWarningOnce(
                warnings,
                "El PDF contiene demanda legible, pero cliente/parte no pudieron vincularse automaticamente con ERP_Partes. Se conservo como pendiente para validacion; no se invento ninguna ParteID.");
        }

        return result;
    }

    private static bool TryParsePdfDemandQuantity(
        string raw,
        string line,
        int tailStart,
        out int quantity)
    {
        quantity = 0;
        var value = (raw ?? string.Empty).Trim().Replace(" ", string.Empty, StringComparison.Ordinal);

        // Ejemplo real VITALMEX:
        // 28.09.2026  200.000 PZA  2.78  556.00
        // Aqui 200.000 es precision decimal de la cantidad (200 piezas), no
        // doscientos mil. La pareja precio/importe de dos decimales lo confirma.
        var decimalZero = Regex.Match(value, @"^(?<whole>\d{1,9})[.,]0{2,3}$");
        var tail = tailStart < line.Length ? line.Substring(tailStart) : string.Empty;
        var hasPriceAndAmount = Regex.Matches(
            tail,
            @"(?<!\d)\d{1,12}[.,]\d{2}(?!\d)",
            RegexOptions.CultureInvariant).Count >= 2;

        if (decimalZero.Success && hasPriceAndAmount &&
            int.TryParse(decimalZero.Groups["whole"].Value, out quantity))
        {
            return quantity > 0;
        }

        return TryParseQuantityText(value, out quantity, preferDecimalWhenAmbiguous: true);
    }
    private static List<GenericReleaseDelivery> ExtractTextDeliveries(string text)
    {
        var result = new List<GenericReleaseDelivery>();

        const string datePattern =
            @"(?<date>(?:0?[1-9]|[12]\d|3[01])[./-](?:0?[1-9]|1[0-2])[./-](?:20)?\d{2}|20\d{2}[./-](?:0?[1-9]|1[0-2])[./-](?:0?[1-9]|[12]\d|3[01]))";

        const string uomPattern = @"(?:pcs|pza|pzas|pieza|piezas|piece|pieces)";

        // Patron fuerte: fecha + cantidad + unidad. Evita confundir ordenes, telefonos o precios.
        var dateQtyUom = new Regex(
            datePattern + @"\s*(?<qty>\d[\d., ]{0,16})\s*(?<uom>" + uomPattern + @")\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        foreach (Match match in dateQtyUom.Matches(text))
        {
            if (!TryParseDateText(match.Groups["date"].Value, out var date))
                continue;

            if (!TryParseQuantityText(match.Groups["qty"].Value, out var qty, preferDecimalWhenAmbiguous: true))
                continue;

            result.Add(new GenericReleaseDelivery
            {
                RequiredDate = date.Date,
                RequiredQuantity = qty,
                PeriodLabel = $"GEN {date:dd/MM/yyyy}"
            });
        }

        // Variante cantidad + unidad + fecha.
        var qtyUomDate = new Regex(
            @"(?<qty>\d[\d., ]{0,16})\s*(?<uom>" + uomPattern + @")\b\s*" + datePattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        foreach (Match match in qtyUomDate.Matches(text))
        {
            if (!TryParseDateText(match.Groups["date"].Value, out var date))
                continue;

            if (!TryParseQuantityText(match.Groups["qty"].Value, out var qty, preferDecimalWhenAmbiguous: true))
                continue;

            result.Add(new GenericReleaseDelivery
            {
                RequiredDate = date.Date,
                RequiredQuantity = qty,
                PeriodLabel = $"GEN {date:dd/MM/yyyy}"
            });
        }

        // Patron por etiquetas cuando el documento no imprime unidad junto a la cantidad.
        var labeledDateQty = new Regex(
            @"(?:delivery\s*date|required\s*date|due\s*date|fecha\s*(?:de\s*)?entrega|fec\s*entrega)\s*[:#-]?\s*" +
            datePattern +
            @"[\s\S]{0,180}?(?:qty|required\s*qty|quantity|cantidad(?:\s*requerida)?)\s*[:#-]?\s*(?<qty>\d[\d., ]{0,16})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        foreach (Match match in labeledDateQty.Matches(text))
        {
            if (!TryParseDateText(match.Groups["date"].Value, out var date))
                continue;

            if (!TryParseQuantityText(match.Groups["qty"].Value, out var qty, preferDecimalWhenAmbiguous: false))
                continue;

            result.Add(new GenericReleaseDelivery
            {
                RequiredDate = date.Date,
                RequiredQuantity = qty,
                PeriodLabel = $"GEN {date:dd/MM/yyyy}"
            });
        }

        return result
            .Where(x => x.RequiredQuantity > 0)
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
            "MM/dd/yyyy", "M/d/yyyy",
            "dd.MM.yyyy", "d.M.yyyy",
            "dd-MM-yyyy", "d-M-yyyy",
            "yyyy-MM-dd", "yyyy/MM/dd", "yyyy.MM.dd",
            "dd/MM/yy", "d/M/yy",
            "MM/dd/yy", "M/d/yy",
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
        if (value == null || value is DateTime)
            return false;

        if (value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
        {
            try
            {
                var number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                if (number <= 0 || number > int.MaxValue)
                    return false;

                quantity = Convert.ToInt32(Math.Round(number, 0, MidpointRounding.AwayFromZero));
                return quantity > 0;
            }
            catch
            {
                return false;
            }
        }

        return TryParseQuantityText(CellText(value), out quantity, preferDecimalWhenAmbiguous: false);
    }

    private static bool TryParseQuantityText(
        string text,
        out int quantity,
        bool preferDecimalWhenAmbiguous)
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
        else if (value.Contains('.') || value.Contains(','))
        {
            var separator = value.Contains('.') ? '.' : ',';
            var pieces = value.Split(separator);

            if (pieces.Length > 2 && pieces.Skip(1).All(x => x.Length == 3))
            {
                normalized = string.Concat(pieces);
            }
            else if (pieces.Length == 2)
            {
                var trailing = pieces[1].Length;

                if (trailing == 3 &&
                    !(preferDecimalWhenAmbiguous && pieces[0].Length >= 3))
                {
                    normalized = pieces[0] + pieces[1];
                }
                else
                {
                    normalized = pieces[0] + "." + pieces[1];
                }
            }
            else
            {
                normalized = value.Replace(separator.ToString(), string.Empty, StringComparison.Ordinal);
            }
        }
        else
        {
            normalized = value;
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
        return quantity > 0;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);

        foreach (var ch in normalized.ToUpperInvariant())
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
        }

        return sb.ToString();
    }

    private static string NormalizeWords(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        var lastWasSpace = false;

        foreach (var ch in normalized.ToUpperInvariant())
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                sb.Append(' ');
                lastWasSpace = true;
            }
        }

        return Regex.Replace(sb.ToString().Trim(), @"\s+", " ");
    }

    private static void AddWarningOnce(List<string> warnings, string warning)
    {
        if (!warnings.Any(x => string.Equals(x, warning, StringComparison.OrdinalIgnoreCase)))
            warnings.Add(warning);
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value.Substring(0, max) + "...";
}
