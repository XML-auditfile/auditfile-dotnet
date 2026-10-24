namespace XmlAuditfile.Reading;

/// <summary>
/// The paths of an import as a tree, built once per import: every node knows its table, column or group.
/// Reading an element is then one lookup of its local name under the current node, instead of building the
/// full path as a string and looking that up. Only known paths have a node, so a file cannot make the tree grow.
/// </summary>
internal sealed class MappingPlan
{
    private readonly Dictionary<string, PathNode> _nodes = new(StringComparer.Ordinal);

    public MappingPlan(AuditfileSchema schema)
    {
        Root = new PathNode(null, "");
        var plans = new List<TablePlan>();
        foreach (var table in schema.Tables)
        {
            var plan = new TablePlan(table);
            plans.Add(plan);
            Node(table.Path).Table = plan;

            foreach (var column in table.Columns)
            {
                if (column.Path != ".")
                {
                    Node($"{table.Path}/{column.Path}").Column = new ColumnTarget(plan, column);
                }
            }
        }

        foreach (var node in _nodes.Values)
        {
            node.IsGroup = node.Table is null && node.Column is null;
        }

        foreach (var plan in plans)
        {
            plan.Required = plan.Table.Columns
                .Where(c => c.MinOccurs >= 1 && c.Path != ".")
                .Select(c => new RequiredColumn(c, c.Path.LastIndexOf('/') is var slash and > 0 ? _nodes[$"{plan.Table.Path}/{c.Path.Substring(0, slash)}"] : null))
                .ToList();
        }
    }

    /// <summary>Above the root element: its only child is the root element of the version.</summary>
    public PathNode Root { get; }

    private PathNode Node(string path)
    {
        if (_nodes.TryGetValue(path, out var node))
        {
            return node;
        }

        var slash = path.LastIndexOf('/');
        var parent = slash < 0 ? Root : Node(path.Substring(0, slash));
        var name = slash < 0 ? path : path.Substring(slash + 1);
        node = name.StartsWith("@", StringComparison.Ordinal) ? parent.AddAttribute(name.Substring(1), path) : parent.AddChild(name, path);
        _nodes[path] = node;
        return node;
    }
}

/// <summary>A known element or attribute path.</summary>
internal sealed class PathNode(PathNode? parent, string path)
{
    private Dictionary<string, PathNode>? _children;
    private Dictionary<string, PathNode>? _attributes;

    /// <summary>The full path, e.g. "auditfile/company/transactions"; attributes as ".../@name".</summary>
    public string Path { get; } = path;

    public PathNode? Parent { get; } = parent;

    public TablePlan? Table { get; set; }

    public ColumnTarget? Column { get; set; }

    /// <summary>A flattened element or pure container: no table and no column.</summary>
    public bool IsGroup { get; set; }

    /// <summary>The child element with this local name, or null when the path is not known.</summary>
    public PathNode? Child(string localName) => _children is not null && _children.TryGetValue(localName, out var node) ? node : null;

    /// <summary>The attribute with this local name, or null when the path is not known.</summary>
    public PathNode? Attribute(string localName) => _attributes is not null && _attributes.TryGetValue(localName, out var node) ? node : null;

    /// <summary>The path of an unknown child, for a diagnostic.</summary>
    public string ChildPath(string localName) => Path.Length == 0 ? localName : $"{Path}/{localName}";

    public PathNode AddChild(string localName, string path) =>
        Add(_children ??= new Dictionary<string, PathNode>(StringComparer.Ordinal), localName, path);

    public PathNode AddAttribute(string localName, string path) =>
        Add(_attributes ??= new Dictionary<string, PathNode>(StringComparer.Ordinal), localName, path);

    private PathNode Add(Dictionary<string, PathNode> nodes, string localName, string path)
    {
        var node = new PathNode(this, path);
        nodes[localName] = node;
        return node;
    }
}

internal sealed class TablePlan(SchemaTable table)
{
    public SchemaTable Table { get; } = table;

    /// <summary>A repeating simple element: one "value" column read from the element text.</summary>
    public bool IsValueTable { get; } = table.Columns.Count == 1 && table.Columns[0].Path == ".";

    /// <summary>Columns with minOccurs ≥ 1, each with the group it is in (null for a direct child or attribute).</summary>
    public IReadOnlyList<RequiredColumn> Required { get; set; } = Array.Empty<RequiredColumn>();
}

/// <summary>A required column; inside an optional group it is only required when the group occurs.</summary>
internal sealed class RequiredColumn(SchemaColumn column, PathNode? group)
{
    public SchemaColumn Column { get; } = column;

    public PathNode? Group { get; } = group;
}

internal sealed class ColumnTarget(TablePlan table, SchemaColumn column)
{
    public TablePlan Table { get; } = table;

    public SchemaColumn Column { get; } = column;
}
