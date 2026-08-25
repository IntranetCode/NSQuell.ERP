using ExcelDataReader;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ERP.NSQuell.Servicios.Releases;

/// <summary>
/// Lector tolerante para las matrices semanales de GOLDE.
///
/// Los archivos recibidos contienen una hoja horizontal estable (normalmente
/// "Hoja2") y, en algunas versiones, una hoja transpuesta auxiliar. La hoja
/// horizontal puede cambiar en la columna previa a la semana vigente:
/// BACKLOG, PENDIENTE o incluso un encabezado histórico heredado.
/// </summary>
internal static class GoldeReleaseExcelParser
{
    private const int HeaderPeriodRow = 0;
    private const int HeaderDateRow = 2;
    private const int DataStartRow = 3;
    private const int OrderColumn = 0;
    private const int PartColumn = 1;
    private const int DescriptionColumn = 2;
    private const int FirstVariableColumn = 3;

    private sealed class WeekColumn
    {
        public int Column { get; init; }
        public int Week { get; init; }
        public string Label { get; init; } = string.Empty;
        public DateTime Date { get; init; }
    }

    private sealed class DemandColumn
    {
        public int Column { get; init; }
        public string Label { get; init; } = string.Empty;
        public DateTime RequiredDate { get; init; }
        public bool IsBacklog { get; init; }
    }

    private sealed class RowLayout
    {
        public required DataTable Table { get; init; }
        public required WeekColumn FirstWeek { get; init; }
        public required List<DemandColumn> DemandColumns { get; init; }
        public required List<string> Warnings { get; init; }
    }

    private sealed class PeriodRow
    {
        public int Row { get; init; }
        public int? Week { get; init; }
        public string Label { get; init; } = string.Empty;
        public DateTime? Date { get; init; }
        public bool IsExplicitBacklog { get; init; }
    }

    public static bool LooksLike(DataTable table)
    {
        return TryBuildRowLayout(table, null, out _) ||
               LooksLikeLegacyTransposed(table);
    }

    public static ReleaseExcelDocument Parse(byte[] bytes, string? fileName)
    {
        var workbook = ReadWorkbook(bytes);

        foreach (DataTable table in workbook.Tables)
        {
            if (TryBuildRowLayout(table, fileName, out var layout))
                return ParseRowLayout(layout, bytes);
        }

        var legacyTable = workbook.Tables
            .Cast<DataTable>()
            .FirstOrDefault(LooksLikeLegacyTransposed);

        if (legacyTable != null)
            return ParseLegacyTransposed(legacyTable, bytes, fileName);

        throw new InvalidOperationException(
            "No se encontro una matriz semanal GOLDE compatible dentro del Excel.");
    }

    private static ReleaseExcelDocument ParseRowLayout(
        RowLayout layout,
        byte[] bytes)
    {
        var rows = new List<ReleaseExcelRow>();
        var warnings = new List<string>(layout.Warnings);
        var orderNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasBacklog = false;

        for (var row = DataStartRow; row < layout.Table.Rows.Count; row++)
        {
            var partNumber = CellText(Get(layout.Table, row, PartColumn));
            if (string.IsNullOrWhiteSpace(partNumber))
                continue;

            var description = CellText(Get(layout.Table, row, DescriptionColumn));
            var orderNumber = CellText(Get(layout.Table, row, OrderColumn));

            if (!string.IsNullOrWhiteSpace(orderNumber))
                orderNumbers.Add(orderNumber);

            var deliveries = new List<ReleaseExcelDelivery>();
            var sequence = 1;

            foreach (var demandColumn in layout.DemandColumns.OrderBy(x => x.Column))
            {
                if (!TryPositiveInteger(
                    Get(layout.Table, row, demandColumn.Column),
                    out var quantity))
                {
                    continue;
                }

                deliveries.Add(new ReleaseExcelDelivery
                {
                    Sequence = sequence++,
                    PeriodLabel = demandColumn.Label,
                    RequiredDate = demandColumn.RequiredDate.Date,
                    RequiredQuantity = quantity,
                    IsBacklog = demandColumn.IsBacklog
                });

                hasBacklog |= demandColumn.IsBacklog;
            }

            if (deliveries.Count == 0)
                continue;

            rows.Add(new ReleaseExcelRow
            {
                PartNumber = partNumber,
                PartDescription = string.IsNullOrWhiteSpace(description)
                    ? null
                    : description,
                SourceReference = string.IsNullOrWhiteSpace(orderNumber)
                    ? null
                    : orderNumber,
                Uom = "PZA",
                Deliveries = deliveries
            });
        }

        if (rows.Count == 0)
        {
            throw new InvalidOperationException(
                "El Excel GOLDE no contiene cantidades positivas para importar.");
        }

        if (hasBacklog)
        {
            warnings.Add(
                "El documento GOLDE contiene demanda BACKLOG/PENDIENTE; " +
                "se conservo como demanda vencida sin mezclarla con las semanas futuras.");
        }

        if (orderNumbers.Count > 1)
        {
            warnings.Add(
                "El documento GOLDE contiene mas de un numero de orden. " +
                "Cada renglon conserva su referencia original.");
        }

        var folio = orderNumbers.FirstOrDefault();

        return new ReleaseExcelDocument
        {
            // Se conserva el codigo historico para no separar versiones ya importadas.
            TemplateCode = "GOLDEN_WEEKLY_RELEASE",
            ClienteNombre = "GOLDE AUBURN HILLS, LLC",
            FolioCliente = string.IsNullOrWhiteSpace(folio) ? null : folio,
            DocumentDate = layout.FirstWeek.Date.Date,
            VersionText =
                $"{layout.FirstWeek.Label} / {layout.FirstWeek.Date:dd.MM.yyyy}",
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            Rows = rows,
            Warnings = warnings
        };
    }

    private static bool TryBuildRowLayout(
        DataTable table,
        string? fileName,
        out RowLayout layout)
    {
        layout = null!;

        if (table.Rows.Count < 4 || table.Columns.Count < 6)
            return false;

        var weekColumns = new List<WeekColumn>();

        for (var column = FirstVariableColumn;
             column < table.Columns.Count;
             column++)
        {
            var label = CellText(Get(table, HeaderPeriodRow, column));

            if (!TryReadWeek(label, out var week) ||
                !TryReadDate(Get(table, HeaderDateRow, column), out var date))
            {
                continue;
            }

            weekColumns.Add(new WeekColumn
            {
                Column = column,
                Week = week,
                Label = string.IsNullOrWhiteSpace(label)
                    ? $"CW{week}"
                    : label.ToUpperInvariant(),
                Date = date.Date
            });
        }

        weekColumns = weekColumns
            .OrderBy(x => x.Column)
            .ToList();

        if (weekColumns.Count < 2)
            return false;

        var firstWeek = ResolveFirstWeek(weekColumns, fileName);
        if (firstWeek == null)
            return false;

        var demandColumns = new List<DemandColumn>();
        var warnings = new List<string>();
        var occupiedColumns = new HashSet<int>();

        for (var column = FirstVariableColumn;
             column < firstWeek.Column;
             column++)
        {
            var periodText = CellText(Get(table, HeaderPeriodRow, column));
            var dateText = CellText(Get(table, HeaderDateRow, column));
            var combined = $"{periodText} {dateText}";

            if (!ContainsBacklogKeyword(combined))
                continue;

            var label = ContainsWord(combined, "BACKLOG")
                ? "BACKLOG"
                : "PENDIENTE";

            var requiredDate = TryReadDate(
                Get(table, HeaderDateRow, column),
                out var explicitDate)
                    ? explicitDate.Date
                    : firstWeek.Date.Date;

            demandColumns.Add(new DemandColumn
            {
                Column = column,
                Label = label,
                RequiredDate = requiredDate,
                IsBacklog = true
            });

            occupiedColumns.Add(column);
        }

        // Algunas matrices GOLDE traen una columna inmediatamente anterior a
        // la semana vigente con un encabezado heredado (por ejemplo, una CW de
        // otro ano). Si contiene cantidades y rompe la secuencia semanal, se
        // interpreta como PENDIENTE, no como una entrega historica literal.
        var previousWeek = weekColumns
            .Where(x => x.Column < firstWeek.Column)
            .OrderByDescending(x => x.Column)
            .FirstOrDefault();

        if (previousWeek != null &&
            !occupiedColumns.Contains(previousWeek.Column) &&
            HasPositiveDemand(table, previousWeek.Column) &&
            !IsDirectPredecessor(previousWeek, firstWeek))
        {
            demandColumns.Add(new DemandColumn
            {
                Column = previousWeek.Column,
                Label = "PENDIENTE",
                RequiredDate = firstWeek.Date.Date,
                IsBacklog = true
            });

            occupiedColumns.Add(previousWeek.Column);

            warnings.Add(
                $"GOLDE: la columna previa {previousWeek.Label} / " +
                $"{previousWeek.Date:dd.MM.yyyy} no pertenece a la secuencia " +
                $"de {firstWeek.Label}; se normalizo como PENDIENTE con fecha " +
                $"{firstWeek.Date:dd.MM.yyyy}.");
        }

        foreach (var weekColumn in weekColumns
            .Where(x => x.Column >= firstWeek.Column)
            .OrderBy(x => x.Column))
        {
            if (weekColumn.Date.Date < firstWeek.Date.Date)
                continue;

            demandColumns.Add(new DemandColumn
            {
                Column = weekColumn.Column,
                Label = weekColumn.Label,
                RequiredDate = weekColumn.Date.Date,
                IsBacklog = false
            });
        }

        demandColumns = demandColumns
            .GroupBy(x => x.Column)
            .Select(x => x.First())
            .OrderBy(x => x.Column)
            .ToList();

        if (demandColumns.Count == 0 ||
            !HasCompatibleDataRow(table, demandColumns))
        {
            return false;
        }

        layout = new RowLayout
        {
            Table = table,
            FirstWeek = firstWeek,
            DemandColumns = demandColumns,
            Warnings = warnings
        };

        return true;
    }

    private static WeekColumn? ResolveFirstWeek(
        IReadOnlyList<WeekColumn> weekColumns,
        string? fileName)
    {
        var requestedWeek = ExtractWeekFromFileName(fileName);

        if (requestedWeek.HasValue)
        {
            var exact = weekColumns.FirstOrDefault(
                x => x.Week == requestedWeek.Value);

            if (exact != null)
                return exact;
        }

        for (var index = 0; index < weekColumns.Count - 1; index++)
        {
            if (IsSequentialPair(weekColumns[index], weekColumns[index + 1]))
                return weekColumns[index];
        }

        return weekColumns.FirstOrDefault();
    }

    private static bool IsSequentialPair(
        WeekColumn current,
        WeekColumn next)
    {
        var dayDifference = (next.Date.Date - current.Date.Date).TotalDays;

        if (dayDifference < 5 || dayDifference > 9)
            return false;

        return next.Week == current.Week + 1 ||
               (current.Week >= 52 && next.Week == 1);
    }

    private static bool IsDirectPredecessor(
        WeekColumn previous,
        WeekColumn current)
    {
        var dayDifference = (current.Date.Date - previous.Date.Date).TotalDays;

        if (dayDifference < 5 || dayDifference > 9)
            return false;

        return current.Week == previous.Week + 1 ||
               (previous.Week >= 52 && current.Week == 1);
    }

    private static bool HasCompatibleDataRow(
        DataTable table,
        IReadOnlyCollection<DemandColumn> demandColumns)
    {
        for (var row = DataStartRow; row < table.Rows.Count; row++)
        {
            var orderNumber = CellText(Get(table, row, OrderColumn));
            var partNumber = CellText(Get(table, row, PartColumn));
            var description = CellText(Get(table, row, DescriptionColumn));

            if (string.IsNullOrWhiteSpace(orderNumber) ||
                string.IsNullOrWhiteSpace(partNumber) ||
                string.IsNullOrWhiteSpace(description))
            {
                continue;
            }

            foreach (var demandColumn in demandColumns)
            {
                if (TryPositiveInteger(
                    Get(table, row, demandColumn.Column),
                    out _))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasPositiveDemand(DataTable table, int column)
    {
        for (var row = DataStartRow; row < table.Rows.Count; row++)
        {
            if (TryPositiveInteger(Get(table, row, column), out _))
                return true;
        }

        return false;
    }

    private static bool LooksLikeLegacyTransposed(DataTable table)
    {
        if (table.Rows.Count < 5 || table.Columns.Count < 5)
            return false;

        var validParts = 0;

        for (var column = FirstVariableColumn;
             column < table.Columns.Count;
             column++)
        {
            var part = CellText(Get(table, 1, column));
            var description = CellText(Get(table, 2, column));

            if (!string.IsNullOrWhiteSpace(part) &&
                !string.IsNullOrWhiteSpace(description))
            {
                validParts++;
            }
        }

        if (validParts == 0)
            return false;

        var validPeriods = 0;

        for (var row = 3; row < table.Rows.Count; row++)
        {
            var label = CellText(Get(table, row, 0));
            var dateText = CellText(Get(table, row, 2));

            if ((TryReadWeek(label, out _) ||
                 ContainsBacklogKeyword($"{label} {dateText}")) &&
                (TryReadDate(Get(table, row, 2), out _) ||
                 ContainsBacklogKeyword($"{label} {dateText}")))
            {
                validPeriods++;
            }
        }

        return validPeriods >= 2;
    }

    private static ReleaseExcelDocument ParseLegacyTransposed(
        DataTable table,
        byte[] bytes,
        string? fileName)
    {
        var periodRows = new List<PeriodRow>();

        for (var row = 3; row < table.Rows.Count; row++)
        {
            var label = CellText(Get(table, row, 0));
            var dateText = CellText(Get(table, row, 2));
            var combined = $"{label} {dateText}";

            int? week = null;
            if (TryReadWeek(label, out var parsedWeek))
                week = parsedWeek;

            DateTime? date = null;
            if (TryReadDate(Get(table, row, 2), out var parsedDate))
                date = parsedDate.Date;

            if (!week.HasValue &&
                !ContainsBacklogKeyword(combined))
            {
                continue;
            }

            periodRows.Add(new PeriodRow
            {
                Row = row,
                Week = week,
                Label = string.IsNullOrWhiteSpace(label)
                    ? "PENDIENTE"
                    : label.ToUpperInvariant(),
                Date = date,
                IsExplicitBacklog = ContainsBacklogKeyword(combined)
            });
        }

        var requestedWeek = ExtractWeekFromFileName(fileName);
        var firstOperational = requestedWeek.HasValue
            ? periodRows.FirstOrDefault(x => x.Week == requestedWeek.Value)
            : null;

        firstOperational ??= periodRows
            .Where(x => x.Week.HasValue && x.Date.HasValue)
            .OrderBy(x => x.Row)
            .FirstOrDefault();

        if (firstOperational?.Date == null)
        {
            throw new InvalidOperationException(
                "No se pudo identificar la primera semana operativa del Excel GOLDE.");
        }

        var demandRows = new List<PeriodRow>();

        foreach (var periodRow in periodRows.OrderBy(x => x.Row))
        {
            if (periodRow.Row < firstOperational.Row &&
                !periodRow.IsExplicitBacklog)
            {
                continue;
            }

            demandRows.Add(new PeriodRow
            {
                Row = periodRow.Row,
                Week = periodRow.Week,
                Label = periodRow.IsExplicitBacklog
                    ? (ContainsWord(periodRow.Label, "BACKLOG")
                        ? "BACKLOG"
                        : "PENDIENTE")
                    : periodRow.Label,
                Date = periodRow.Date ?? firstOperational.Date,
                IsExplicitBacklog = periodRow.IsExplicitBacklog
            });
        }

        var orderNumber = string.Empty;

        for (var column = FirstVariableColumn;
             column < table.Columns.Count;
             column++)
        {
            orderNumber = CellText(Get(table, 0, column));
            if (!string.IsNullOrWhiteSpace(orderNumber))
                break;
        }

        var rows = new List<ReleaseExcelRow>();
        var warnings = new List<string>();

        for (var column = FirstVariableColumn;
             column < table.Columns.Count;
             column++)
        {
            var partNumber = CellText(Get(table, 1, column));
            if (string.IsNullOrWhiteSpace(partNumber))
                continue;

            var description = CellText(Get(table, 2, column));
            var deliveries = new List<ReleaseExcelDelivery>();
            var sequence = 1;

            foreach (var periodRow in demandRows)
            {
                if (!TryPositiveInteger(Get(table, periodRow.Row, column), out var quantity) ||
                    !periodRow.Date.HasValue)
                {
                    continue;
                }

                deliveries.Add(new ReleaseExcelDelivery
                {
                    Sequence = sequence++,
                    PeriodLabel = periodRow.Label,
                    RequiredDate = periodRow.Date.Value.Date,
                    RequiredQuantity = quantity,
                    IsBacklog = periodRow.IsExplicitBacklog
                });
            }

            if (deliveries.Count == 0)
                continue;

            rows.Add(new ReleaseExcelRow
            {
                PartNumber = partNumber,
                PartDescription = string.IsNullOrWhiteSpace(description)
                    ? null
                    : description,
                SourceReference = string.IsNullOrWhiteSpace(orderNumber)
                    ? null
                    : orderNumber,
                Uom = "PZA",
                Deliveries = deliveries
            });
        }

        if (rows.Count == 0)
        {
            throw new InvalidOperationException(
                "El Excel GOLDE no contiene cantidades positivas para importar.");
        }

        if (rows.SelectMany(x => x.Deliveries).Any(x => x.IsBacklog))
        {
            warnings.Add(
                "El documento GOLDE contiene demanda BACKLOG/PENDIENTE; " +
                "se conservo con la fecha indicada en el archivo.");
        }

        return new ReleaseExcelDocument
        {
            TemplateCode = "GOLDEN_WEEKLY_RELEASE",
            ClienteNombre = "GOLDE AUBURN HILLS, LLC",
            FolioCliente = string.IsNullOrWhiteSpace(orderNumber)
                ? null
                : orderNumber,
            DocumentDate = firstOperational.Date.Value.Date,
            VersionText =
                $"{firstOperational.Label} / {firstOperational.Date.Value:dd.MM.yyyy}",
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            Rows = rows,
            Warnings = warnings
        };
    }

    private static int? ExtractWeekFromFileName(string? fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName ?? string.Empty);
        var match = Regex.Match(
            baseName,
            @"(?<![A-Z0-9])CW\s*[-_ ]?(?<week>\d{1,2})(?!\d)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!match.Success ||
            !int.TryParse(
                match.Groups["week"].Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var week) ||
            week < 1 ||
            week > 53)
        {
            return null;
        }

        return week;
    }

    private static bool TryReadWeek(string? value, out int week)
    {
        week = 0;

        var match = Regex.Match(
            value ?? string.Empty,
            @"^\s*CW\s*[-_ ]?(?<week>\d{1,2})\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return match.Success &&
               int.TryParse(
                   match.Groups["week"].Value,
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out week) &&
               week >= 1 &&
               week <= 53;
    }

    private static bool ContainsBacklogKeyword(string? value)
    {
        var normalized = NormalizeText(value);

        return normalized.Contains("BACKLOG", StringComparison.Ordinal) ||
               normalized.Contains("PENDIENTE", StringComparison.Ordinal) ||
               normalized.Contains("PENDING", StringComparison.Ordinal) ||
               normalized.Contains("PAST DUE", StringComparison.Ordinal) ||
               normalized.Contains("ATRASO", StringComparison.Ordinal) ||
               normalized.Contains("VENCIDO", StringComparison.Ordinal);
    }

    private static bool ContainsWord(string? value, string word)
    {
        return NormalizeText(value).Contains(
            NormalizeText(word),
            StringComparison.Ordinal);
    }

    private static string NormalizeText(string? value)
    {
        return (value ?? string.Empty)
            .Trim()
            .ToUpperInvariant();
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
            throw new InvalidOperationException(
                "No fue posible abrir el archivo Excel GOLDE: " + ex.Message,
                ex);
        }
    }

    private static object? Get(DataTable table, int row, int column)
    {
        if (row < 0 ||
            column < 0 ||
            row >= table.Rows.Count ||
            column >= table.Columns.Count)
        {
            return null;
        }

        var value = table.Rows[row][column];
        return value == DBNull.Value ? null : value;
    }

    private static string CellText(object? value)
    {
        if (value == null)
            return string.Empty;

        return value switch
        {
            DateTime date =>
                date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),

            double number
                when Math.Abs(number - Math.Round(number)) < 0.0000001d =>
                Math.Round(number).ToString("0", CultureInfo.InvariantCulture),

            float number
                when Math.Abs(number - MathF.Round(number)) < 0.0001f =>
                MathF.Round(number).ToString("0", CultureInfo.InvariantCulture),

            decimal number
                when number == decimal.Truncate(number) =>
                decimal.Truncate(number).ToString(
                    "0",
                    CultureInfo.InvariantCulture),

            IFormattable formattable =>
                formattable.ToString(
                    null,
                    CultureInfo.InvariantCulture)?.Trim() ?? string.Empty,

            _ => value.ToString()?.Trim() ?? string.Empty
        };
    }

    private static bool TryPositiveInteger(object? value, out int result)
    {
        return TryInteger(value, out result) && result > 0;
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
            "d.M.yyyy",
            "dd/MM/yyyy",
            "d/M/yyyy",
            "MM/dd/yyyy",
            "M/d/yyyy",
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
