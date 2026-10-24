namespace XmlAuditfile;

// The member names are the logical type names of auditfile-spec; matching .NET type names is intended.
#pragma warning disable CA1720

/// <summary>Logical type of a column.</summary>
public enum ColumnType
{
    /// <summary>Text.</summary>
    String,

    /// <summary>Exact decimal number (<see cref="decimal"/>).</summary>
    Decimal,

    /// <summary>Whole number (<see cref="long"/>).</summary>
    Integer,

    /// <summary>Date without time (<see cref="DateTime"/>).</summary>
    Date,

    /// <summary>Date and time (<see cref="DateTime"/>).</summary>
    DateTime,

    /// <summary>Time of day (<see cref="TimeSpan"/>).</summary>
    Time,

    /// <summary>True or false (<see cref="bool"/>).</summary>
    Boolean,
}

#pragma warning restore CA1720
