using System.Globalization;
using System.Text.RegularExpressions;

namespace ERP.NSQuell.Servicios.Releases;

// NSQ_RELEASE_CANTIDAD_CULTURA_V3
// Release quantities are pieces. Text such as "80,00" must mean 80,
// while "8,000" / "8.000" may represent 8000 depending on grouping.
internal static class ReleaseQuantityParser
{
    public static bool TryParsePositiveInteger(object? value, out int result)
        => TryParseInteger(value, out result) && result > 0;

    public static bool TryParseInteger(object? value, out int result)
    {
        result = 0;
        if (value == null || value == DBNull.Value || value is DateTime)
            return false;

        try
        {
            if (value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
            {
                var number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                if (number < int.MinValue || number > int.MaxValue)
                    return false;

                result = Convert.ToInt32(Math.Round(number, 0, MidpointRounding.AwayFromZero));
                return true;
            }
        }
        catch
        {
            return false;
        }

        return TryParseText(Convert.ToString(value, CultureInfo.InvariantCulture), out result);
    }

    public static int ParseInteger(string? value, string label)
    {
        if (!TryParseText(value, out var result))
            throw new InvalidOperationException($"{label} invalida: {value}.");

        return result;
    }

    private static bool TryParseText(string? text, out int result)
    {
        result = 0;
        var value = (text ?? string.Empty)
            .Trim()
            .Replace("\u00A0", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        if (string.IsNullOrWhiteSpace(value))
            return false;

        value = Regex.Replace(value, @"[^\d,\.\-+]", string.Empty);
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var sign = 1m;
        if (value.StartsWith("-", StringComparison.Ordinal))
        {
            sign = -1m;
            value = value[1..];
        }
        else if (value.StartsWith("+", StringComparison.Ordinal))
        {
            value = value[1..];
        }

        if (string.IsNullOrWhiteSpace(value) || value.Contains('-') || value.Contains('+'))
            return false;

        string normalized;
        var dotCount = value.Count(c => c == '.');
        var commaCount = value.Count(c => c == ',');

        if (dotCount > 0 && commaCount > 0)
        {
            // The last separator is decimal; earlier separators are grouping.
            var lastDot = value.LastIndexOf('.');
            var lastComma = value.LastIndexOf(',');
            var decimalSeparator = lastDot > lastComma ? '.' : ',';
            var groupingSeparator = decimalSeparator == '.' ? ',' : '.';

            normalized = value.Replace(groupingSeparator.ToString(), string.Empty, StringComparison.Ordinal);
            if (decimalSeparator == ',')
                normalized = normalized.Replace(',', '.');
        }
        else if (commaCount > 0 || dotCount > 0)
        {
            var separator = commaCount > 0 ? ',' : '.';
            var parts = value.Split(separator);

            if (parts.Length > 2 && parts.Skip(1).All(x => x.Length == 3))
            {
                normalized = string.Concat(parts);
            }
            else if (parts.Length == 2)
            {
                var decimals = parts[1].Length;

                // Exactly three trailing digits with 1-3 leading digits is the
                // common thousands form: 8,000 / 8.000 / 80,000.
                if (decimals == 3 && parts[0].Length <= 3)
                    normalized = parts[0] + parts[1];
                else
                    normalized = parts[0] + "." + parts[1];
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
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            return false;
        }

        parsed *= sign;
        if (parsed < int.MinValue || parsed > int.MaxValue)
            return false;

        result = Convert.ToInt32(Math.Round(parsed, 0, MidpointRounding.AwayFromZero));
        return true;
    }
}