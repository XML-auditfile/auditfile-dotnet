using XmlAuditfile.Internal;

namespace XmlAuditfile;

/// <summary>
/// One row of a table. <see cref="Values"/> follows the column order of <see cref="Table"/>;
/// the .NET type of a value follows <see cref="SchemaColumn.Type"/> (string, decimal, long, DateTime, TimeSpan, bool) or is null.
/// </summary>
/// <remarks>
/// Prefer the typed accessors (<see cref="GetDecimal(string)"/>, <see cref="GetString(string)"/>, ...) over casting <see cref="this[string]"/>:
/// they check the column type and say what is wrong instead of returning null or throwing an InvalidCastException.
/// In a loop over many rows, look the <see cref="SchemaColumn"/> up once (<see cref="SchemaTable.FindColumn"/>) and use the overloads
/// that take it: no lookup by name per row.
/// </remarks>
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
    public object? this[string column] => _values[Column(column).Index];

    /// <summary>Value of a column of this table.</summary>
    /// <exception cref="ArgumentException">The column belongs to another table.</exception>
    public object? this[SchemaColumn column] => _values[Own(column).Index];

    /// <summary>The value of a column, or false when this table has no such column (e.g. in another version).</summary>
    public bool TryGetValue(string column, out object? value)
    {
        var found = Table.FindColumn(Guard.NotNull(column, nameof(column)));
        value = found is null ? null : _values[found.Index];
        return found is not null;
    }

    /// <summary>A string column.</summary>
    /// <exception cref="KeyNotFoundException">The column does not exist.</exception>
    /// <exception cref="InvalidOperationException">The column is not a string column.</exception>
    public string? GetString(string column) => GetString(Column(column));

    /// <inheritdoc cref="GetString(string)"/>
    public string? GetString(SchemaColumn column) => (string?)Typed(column, ColumnType.String);

    /// <summary>A decimal column, or an integer column as a decimal (a column can be integer in one version and decimal in another).</summary>
    /// <exception cref="KeyNotFoundException">The column does not exist.</exception>
    /// <exception cref="InvalidOperationException">The column is not a decimal or integer column.</exception>
    public decimal? GetDecimal(string column) => GetDecimal(Column(column));

    /// <inheritdoc cref="GetDecimal(string)"/>
    public decimal? GetDecimal(SchemaColumn column) => Typed(column, ColumnType.Decimal, ColumnType.Integer) switch
    {
        long integer => integer,
        var value => (decimal?)value,
    };

    /// <summary>An integer column.</summary>
    /// <exception cref="KeyNotFoundException">The column does not exist.</exception>
    /// <exception cref="InvalidOperationException">The column is not an integer column.</exception>
    public long? GetInteger(string column) => GetInteger(Column(column));

    /// <inheritdoc cref="GetInteger(string)"/>
    public long? GetInteger(SchemaColumn column) => (long?)Typed(column, ColumnType.Integer);

    /// <summary>A date column (the time is midnight).</summary>
    /// <exception cref="KeyNotFoundException">The column does not exist.</exception>
    /// <exception cref="InvalidOperationException">The column is not a date column.</exception>
    public DateTime? GetDate(string column) => GetDate(Column(column));

    /// <inheritdoc cref="GetDate(string)"/>
    public DateTime? GetDate(SchemaColumn column) => (DateTime?)Typed(column, ColumnType.Date);

    /// <summary>A date-time column, or a date column at midnight.</summary>
    /// <exception cref="KeyNotFoundException">The column does not exist.</exception>
    /// <exception cref="InvalidOperationException">The column is not a date-time or date column.</exception>
    public DateTime? GetDateTime(string column) => GetDateTime(Column(column));

    /// <inheritdoc cref="GetDateTime(string)"/>
    public DateTime? GetDateTime(SchemaColumn column) => (DateTime?)Typed(column, ColumnType.DateTime, ColumnType.Date);

    /// <summary>A time column.</summary>
    /// <exception cref="KeyNotFoundException">The column does not exist.</exception>
    /// <exception cref="InvalidOperationException">The column is not a time column.</exception>
    public TimeSpan? GetTime(string column) => GetTime(Column(column));

    /// <inheritdoc cref="GetTime(string)"/>
    public TimeSpan? GetTime(SchemaColumn column) => (TimeSpan?)Typed(column, ColumnType.Time);

    /// <summary>A boolean column.</summary>
    /// <exception cref="KeyNotFoundException">The column does not exist.</exception>
    /// <exception cref="InvalidOperationException">The column is not a boolean column.</exception>
    public bool? GetBoolean(string column) => GetBoolean(Column(column));

    /// <inheritdoc cref="GetBoolean(string)"/>
    public bool? GetBoolean(SchemaColumn column) => (bool?)Typed(column, ColumnType.Boolean);

    private SchemaColumn Column(string name) =>
        Table.FindColumn(Guard.NotNull(name, nameof(name))) ?? throw new KeyNotFoundException($"Column '{name}' does not exist in {Table.Name}.");

    private SchemaColumn Own(SchemaColumn column)
    {
        Guard.NotNull(column, nameof(column));
        return column.Index < Table.Columns.Count && ReferenceEquals(Table.Columns[column.Index], column)
            ? column
            : throw new ArgumentException($"Column '{column.Name}' does not belong to {Table.Name}.", nameof(column));
    }

    private object? Typed(SchemaColumn column, ColumnType type, ColumnType? alternative = null)
    {
        Own(column);
        if (column.Type != type && column.Type != alternative)
        {
            throw new InvalidOperationException(
                $"{Table.Name}.{column.Name} is a {Name(column.Type)} column, not {Name(type)}; use Get{column.Type}.");
        }

        return _values[column.Index];
    }

    private static string Name(ColumnType type) => type.ToString().ToLowerInvariant();
}
