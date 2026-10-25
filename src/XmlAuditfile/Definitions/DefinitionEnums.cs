namespace XmlAuditfile.Definitions;

/// <summary>What a dialect correction does. Unknown actions are refused when the definitions are loaded.</summary>
public enum DialectAction
{
    /// <summary>"replaceNamespace": read a file in <see cref="DialectCorrection.From"/> as the official namespace.</summary>
    ReplaceNamespace,

    /// <summary>"decimalComma": a decimal comma becomes a point.</summary>
    DecimalComma,

    /// <summary>"dateFromDateTime": the time is removed from a date.</summary>
    DateFromDateTime,
}

/// <summary>How a view column aggregates its values. Unknown aggregates are refused when the definitions are loaded.</summary>
public enum ViewAggregate
{
    /// <summary>"sum": the total.</summary>
    Sum,

    /// <summary>"count": the number of input rows with a value (or all rows for a column without a value).</summary>
    Count,

    /// <summary>"min": the smallest value.</summary>
    Min,

    /// <summary>"max": the largest value.</summary>
    Max,
}

// The member names are the operator names of auditfile-spec; "Signed" is not meant as a type name.
#pragma warning disable CA1720

/// <summary>How a view column gets its value from an input row. Unknown operators are refused when the definitions are loaded.</summary>
public enum ViewOperator
{
    /// <summary>"column": the value of <see cref="ViewValue.Column"/>.</summary>
    Column,

    /// <summary>"only": the value when <see cref="ViewValue.By"/> is one of <see cref="ViewValue.Codes"/>, otherwise null.</summary>
    Only,

    /// <summary>"signed": the value, negated when <see cref="ViewValue.By"/> is one of <see cref="ViewValue.Codes"/>.</summary>
    Signed,

    /// <summary>
    /// "pseudonym": <see cref="ViewValue.Prefix"/>-n instead of the value, where n numbers the distinct values of the
    /// column in the order they first occur in its table. Null and empty values stay as they are.
    /// </summary>
    Pseudonym,
}

#pragma warning restore CA1720

/// <summary>Parses the names used in the JSON definitions.</summary>
internal static class DefinitionNames
{
    public static DialectAction ParseDialectAction(string value) => value switch
    {
        "replaceNamespace" => DialectAction.ReplaceNamespace,
        "decimalComma" => DialectAction.DecimalComma,
        "dateFromDateTime" => DialectAction.DateFromDateTime,
        _ => throw new FormatException($"Embedded JSON: unknown dialect action '{value}'."),
    };

    public static ViewAggregate? ParseAggregate(string? value) => value switch
    {
        null => null,
        "sum" => ViewAggregate.Sum,
        "count" => ViewAggregate.Count,
        "min" => ViewAggregate.Min,
        "max" => ViewAggregate.Max,
        _ => throw new FormatException($"Embedded JSON: unknown view aggregate '{value}'."),
    };

    public static ViewOperator ParseOperator(string value) => value switch
    {
        "column" => ViewOperator.Column,
        "only" => ViewOperator.Only,
        "signed" => ViewOperator.Signed,
        "pseudonym" => ViewOperator.Pseudonym,
        _ => throw new FormatException($"Embedded JSON: unknown view value operator '{value}'."),
    };
}
