using System.Globalization;
using XmlAuditfile.Definitions;

namespace XmlAuditfile.Reading;

/// <summary>Converts XML text to typed values, always with the invariant culture and XSD notation.</summary>
internal static class ValueConverter
{
    /// <summary>xsd:dateTime: no time zone, Z, or an offset (K); with or without a fraction of up to seven digits.</summary>
    private static readonly string[] DateTimeFormats = ["yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"];

    /// <summary>Empty text becomes null. Returns false when the text is not valid for the type.</summary>
    public static bool TryConvert(string raw, ColumnType type, out object? value)
    {
        value = null;
        if (type == ColumnType.String)
        {
            value = raw;
            return true;
        }

        var text = raw.Trim();
        if (text.Length == 0)
        {
            return true;
        }

        switch (type)
        {
            case ColumnType.Decimal:
                if (decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d))
                {
                    value = d;
                    return true;
                }

                return false;

            case ColumnType.Integer:
                if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l))
                {
                    value = l;
                    return true;
                }

                return false;

            case ColumnType.Date:
                // The date as written; an optional time zone (Z, +hh:mm, -hh:mm) is ignored, never converted.
                if (text.Length >= 10 && IsTimeZone(text.Substring(10))
                    && DateTime.TryParseExact(text.Substring(0, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    value = date;
                    return true;
                }

                return false;

            case ColumnType.DateTime:
                // Only the XSD notation: 2017-01-03T10:00:00 with an optional fraction and time zone. A culture-dependent parse
                // would also take 03/01/2017 10:00:00, and read it as 1 March or 3 January depending on the order.
                if (DateTime.TryParseExact(text, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTime))
                {
                    value = dateTime;
                    return true;
                }

                return false;

            case ColumnType.Time:
                // hh:mm:ss with an optional fraction (dropped) and time zone (ignored), as for a date.
                var end = 8;
                if (text.Length > 9 && text[8] == '.' && char.IsDigit(text[9]))
                {
                    end = 10;
                    while (end < text.Length && char.IsDigit(text[end]))
                    {
                        end++;
                    }
                }

                if (text.Length >= 8 && IsTimeZone(text.Substring(end))
                    && TimeSpan.TryParseExact(text.Substring(0, 8), "hh\\:mm\\:ss", CultureInfo.InvariantCulture, out var time))
                {
                    value = time;
                    return true;
                }

                return false;

            case ColumnType.Boolean:
                if (text is "true" or "1")
                {
                    value = true;
                    return true;
                }

                if (text is "false" or "0")
                {
                    value = false;
                    return true;
                }

                return false;

            default:
                value = raw;
                return true;
        }
    }

    private static bool IsTimeZone(string suffix) =>
        suffix.Length == 0 || suffix == "Z"
        || (suffix.Length == 6 && (suffix[0] == '+' || suffix[0] == '-') && char.IsDigit(suffix[1]) && char.IsDigit(suffix[2])
            && suffix[3] == ':' && char.IsDigit(suffix[4]) && char.IsDigit(suffix[5]));

    /// <summary>Text form of a value for keys and messages.</summary>
    public static string ToText(object? value) => value switch
    {
        null => "",
        DateTime date => date.ToString(date.TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : "o", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
}
