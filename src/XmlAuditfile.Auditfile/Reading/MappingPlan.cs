namespace XmlAuditfile.Auditfile.Reading;

/// <summary>Lookup tables from absolute XML path to table, column or group, built once per import.</summary>
internal sealed class MappingPlan
{
    public MappingPlan(AuditfileSchema schema)
    {
        foreach (var table in schema.Tables)
        {
            var plan = new TablePlan(table);
            Tables[table.Path] = plan;

            foreach (var column in table.Columns)
            {
                if (column.Path == ".")
                {
                    continue;
                }

                var path = $"{table.Path}/{column.Path}";
                Columns[path] = new ColumnTarget(plan, column);
                AddAncestors(path);
            }

            AddAncestors(table.Path);
        }

        foreach (var table in Tables.Keys)
        {
            Groups.Remove(table);
        }
    }

    public Dictionary<string, TablePlan> Tables { get; } = new(StringComparer.Ordinal);

    /// <summary>Element and attribute columns; attributes as ".../@name".</summary>
    public Dictionary<string, ColumnTarget> Columns { get; } = new(StringComparer.Ordinal);

    /// <summary>Flattened elements and pure containers (no table, no column).</summary>
    public HashSet<string> Groups { get; } = new(StringComparer.Ordinal);

    private void AddAncestors(string path)
    {
        for (var slash = path.LastIndexOf('/'); slash > 0; slash = path.LastIndexOf('/', slash - 1))
        {
            Groups.Add(path.Substring(0, slash));
        }
    }
}

internal sealed class TablePlan(SchemaTable table)
{
    public SchemaTable Table { get; } = table;

    /// <summary>A repeating simple element: one "value" column read from the element text.</summary>
    public bool IsValueTable { get; } = table.Columns.Count == 1 && table.Columns[0].Path == ".";
}

internal sealed class ColumnTarget(TablePlan table, SchemaColumn column)
{
    public TablePlan Table { get; } = table;

    public SchemaColumn Column { get; } = column;
}
