using XmlAuditfile.Auditfile.Diagnostics;
using XmlAuditfile.Auditfile.Reading;

namespace XmlAuditfile.Auditfile.Validation;

/// <summary>
/// Checks keys (unique) and references (existing) from the schema.
/// Only key values and unresolved reference values are kept in memory; references are resolved at the end,
/// so a reference to a key that appears later (e.g. in a later part) is still found.
/// </summary>
internal sealed class ReferenceChecker : IRowObserver
{
    private const int MaxPendingPerReference = 100_000;
    private const char Separator = '\u001f';

    private readonly DiagnosticCollector _diagnostics;
    private readonly Dictionary<string, KeyState> _keysByName = new(StringComparer.Ordinal);
    private readonly Dictionary<SchemaTable, List<KeyState>> _keysByTable = new();
    private readonly Dictionary<SchemaTable, List<ReferenceState>> _referencesByTable = new();

    public ReferenceChecker(AuditfileSchema schema, DiagnosticCollector diagnostics)
    {
        _diagnostics = diagnostics;

        foreach (var table in schema.Tables)
        {
            foreach (var key in table.Keys)
            {
                var state = new KeyState(table, Indexes(table, key.Columns));
                _keysByName[key.Name] = state;
                Add(_keysByTable, table, state);
            }
        }

        foreach (var table in schema.Tables)
        {
            foreach (var reference in table.References)
            {
                if (_keysByName.TryGetValue(reference.Key, out var key))
                {
                    Add(_referencesByTable, table, new ReferenceState(table, string.Join(", ", reference.Columns), Indexes(table, reference.Columns), key));
                }
            }
        }
    }

    public void OnRow(AuditfileRow row)
    {
        if (_keysByTable.TryGetValue(row.Table, out var keys))
        {
            foreach (var key in keys)
            {
                var value = Composite(row, key.Columns);
                if (value is not null && !key.Values.Add(value))
                {
                    _diagnostics.Add("XAF-R004", new Location(row.Part, null, null, row.Table.Path), ("value", Display(value)), ("table", row.Table.Name));
                }
            }
        }

        if (_referencesByTable.TryGetValue(row.Table, out var references))
        {
            foreach (var reference in references)
            {
                var value = Composite(row, reference.Columns);
                if (value is null || reference.Key.Values.Contains(value) || reference.Pending.Contains(value))
                {
                    continue;
                }

                if (reference.Pending.Count < MaxPendingPerReference)
                {
                    reference.Pending.Add(value);
                }
                else if (!reference.Overflowed)
                {
                    // From here on new values are not checked; say so once, so a clean result is not mistaken for a complete one.
                    reference.Overflowed = true;
                    _diagnostics.Add("XAF-R017", new Location(row.Part, null, null, row.Table.Path),
                        ("table", row.Table.Name), ("column", reference.ColumnNames), ("target", reference.Key.Table.Name), ("max", MaxPendingPerReference));
                }
            }
        }
    }

    public void OnEnd()
    {
        foreach (var reference in _referencesByTable.Values.SelectMany(r => r))
        {
            foreach (var value in reference.Pending.Where(v => !reference.Key.Values.Contains(v)).OrderBy(v => v, StringComparer.Ordinal))
            {
                _diagnostics.Add("XAF-R003", new Location(null, null, null, reference.Table.Path),
                    ("table", reference.Table.Name), ("column", reference.ColumnNames), ("value", Display(value)), ("target", reference.Key.Table.Name));
            }
        }
    }

    private static string? Composite(AuditfileRow row, int[] columns)
    {
        if (columns.Length == 1)
        {
            var single = row.Values[columns[0]];
            return single is null ? null : ValueConverter.ToText(single);
        }

        var parts = new string[columns.Length];
        for (var i = 0; i < columns.Length; i++)
        {
            var value = row.Values[columns[i]];
            if (value is null)
            {
                return null;
            }

            parts[i] = ValueConverter.ToText(value);
        }

        return string.Join(Separator.ToString(), parts);
    }

    private static string Display(string value) => value.Replace(Separator, '/');

    private static int[] Indexes(SchemaTable table, IReadOnlyList<string> columns) =>
        columns.Select(c => (table.FindColumn(c) ?? throw new InvalidOperationException($"Key column {c} not in {table.Name}.")).Index).ToArray();

    private static void Add<T>(Dictionary<SchemaTable, List<T>> map, SchemaTable table, T item)
    {
        if (!map.TryGetValue(table, out var list))
        {
            list = new List<T>();
            map[table] = list;
        }

        list.Add(item);
    }

    private sealed class KeyState(SchemaTable table, int[] columns)
    {
        public SchemaTable Table { get; } = table;

        public int[] Columns { get; } = columns;

        public HashSet<string> Values { get; } = new(StringComparer.Ordinal);
    }

    private sealed class ReferenceState(SchemaTable table, string columnNames, int[] columns, KeyState key)
    {
        public SchemaTable Table { get; } = table;

        public string ColumnNames { get; } = columnNames;

        public int[] Columns { get; } = columns;

        public KeyState Key { get; } = key;

        public HashSet<string> Pending { get; } = new(StringComparer.Ordinal);

        /// <summary>True once the pending limit was reached and XAF-R017 was reported.</summary>
        public bool Overflowed { get; set; }
    }
}
