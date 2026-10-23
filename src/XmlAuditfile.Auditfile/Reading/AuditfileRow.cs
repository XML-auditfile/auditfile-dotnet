namespace XmlAuditfile.Auditfile;

/// <summary>
/// One row of a table. <see cref="Values"/> follows the column order of <see cref="Table"/>;
/// the .NET type of a value follows <see cref="SchemaColumn.Type"/> (string, decimal, long, DateTime, TimeSpan, bool) or is null.
/// </summary>
public sealed class AuditfileRow
{
    private readonly object?[] _values;

    internal AuditfileRow(SchemaTable table, long id, long? parentId, int ordinal, int part, object?[] values)
    {
        Table = table;
        Id = id;
        ParentId = parentId;
        Ordinal = ordinal;
        Part = part;
        _values = values;
    }

    /// <summary>The table this row belongs to.</summary>
    public SchemaTable Table { get; }

    /// <summary>Unique within the import; assigned in document order.</summary>
    public long Id { get; }

    /// <summary>Id of the parent row, or null for the root.</summary>
    public long? ParentId { get; }

    /// <summary>Position under the parent row (1-based), per table.</summary>
    public int Ordinal { get; }

    /// <summary>Source part the row came from (1-based).</summary>
    public int Part { get; }

    /// <summary>Column values in column order.</summary>
    public IReadOnlyList<object?> Values => _values;

    /// <summary>Value of a column by name.</summary>
    /// <exception cref="KeyNotFoundException">The column does not exist.</exception>
    public object? this[string column] =>
        _values[(Table.FindColumn(column) ?? throw new KeyNotFoundException($"Column '{column}' does not exist in {Table.Name}.")).Index];
}
