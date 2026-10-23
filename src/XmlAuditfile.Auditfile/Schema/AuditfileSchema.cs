using XmlAuditfile.Auditfile.Definitions;
using XmlAuditfile.Auditfile.Internal;

namespace XmlAuditfile.Auditfile;

/// <summary>How the version of an import was determined.</summary>
public enum VersionSource
{
    /// <summary>The file uses the official namespace.</summary>
    Official,

    /// <summary>The file uses a known alias namespace.</summary>
    Alias,

    /// <summary>The caller forced the version.</summary>
    Forced,

    /// <summary>The resolver derived the version from the namespace.</summary>
    Resolved,
}

/// <summary>
/// The schema of one import: tables, columns, types, keys, references, row counts and origin.
/// This is the contract everything after the reader builds on, regardless of where the data is stored.
/// Converts to and from JSON without loss (definitions are re-attached from the version when known).
/// </summary>
public sealed class AuditfileSchema
{
    private readonly Dictionary<string, SchemaTable> _byName;

    internal AuditfileSchema(string versionKey, string fileNamespace, VersionSource source, string? dialect, bool hasRowCounts, IReadOnlyList<SchemaTable> tables)
    {
        VersionKey = versionKey;
        Version = AuditfileVersion.Find(versionKey);
        Namespace = fileNamespace;
        Source = source;
        Dialect = dialect;
        HasRowCounts = hasRowCounts;
        Tables = tables;
        _byName = tables.ToDictionary(t => t.Name, StringComparer.Ordinal);
    }

    /// <summary>Version key, e.g. "xaf/3.2".</summary>
    public string VersionKey { get; }

    /// <summary>The version, or null when this reader does not know it (schema loaded from JSON).</summary>
    public AuditfileVersion? Version { get; }

    /// <summary>Namespace as found in the file.</summary>
    public string Namespace { get; }

    /// <summary>How the version was determined.</summary>
    public VersionSource Source { get; }

    /// <summary>Name of the applied dialect, or null.</summary>
    public string? Dialect { get; }

    /// <summary>True when <see cref="SchemaTable.RowCount"/> is known for every table.</summary>
    public bool HasRowCounts { get; }

    /// <summary>All tables in document order.</summary>
    public IReadOnlyList<SchemaTable> Tables { get; }

    /// <summary>Finds a table by name, or returns null.</summary>
    public SchemaTable? FindTable(string name) => _byName.TryGetValue(name, out var table) ? table : null;

    /// <summary>Returns a table by name.</summary>
    /// <exception cref="KeyNotFoundException">The table does not exist.</exception>
    public SchemaTable Table(string name) => FindTable(name) ?? throw new KeyNotFoundException($"Table '{name}' does not exist.");

    /// <summary>Serializes the schema to compact JSON, e.g. for storage in a database.</summary>
    public string ToJson()
    {
        var json = new JsonText().StartObject()
            .Property("formatVersion", 1)
            .Property("version", VersionKey)
            .Property("namespace", Namespace)
            .Property("source", Source.ToString().ToLowerInvariant())
            .Property("dialect", Dialect)
            .Property("hasRowCounts", HasRowCounts)
            .Name("tables").StartArray();

        foreach (var table in Tables)
        {
            json.StartObject()
                .Property("name", table.Name)
                .Property("xmlName", table.XmlName)
                .Property("path", table.Path)
                .Property("parent", table.Parent)
                .Property("minOccurs", table.MinOccurs)
                .Property("maxOccurs", table.MaxOccurs)
                .Property("required", table.IsRequired)
                .Property("expected", table.IsExpected)
                .Property("rowCount", table.RowCount)
                .Name("keys").StartArray();
            foreach (var key in table.Keys)
            {
                json.StartObject().Property("name", key.Name).Strings("columns", key.Columns).EndObject();
            }

            json.EndArray().Name("references").StartArray();
            foreach (var reference in table.References)
            {
                json.StartObject().Property("name", reference.Name).Strings("columns", reference.Columns)
                    .Property("table", reference.Table).Property("key", reference.Key).EndObject();
            }

            json.EndArray().Name("columns").StartArray();
            foreach (var column in table.Columns)
            {
                json.StartObject()
                    .Property("name", column.Name)
                    .Property("xmlName", column.XmlName)
                    .Property("path", column.Path)
                    .Property("kind", column.Kind)
                    .Property("type", column.Type.ToString().ToLowerInvariant())
                    .Property("xsdType", column.XsdType)
                    .Property("minOccurs", column.MinOccurs)
                    .EndObject();
            }

            json.EndArray().EndObject();
        }

        return json.EndArray().EndObject().ToString();
    }

    /// <summary>Reads a schema written by <see cref="ToJson"/>.</summary>
    public static AuditfileSchema FromJson(string json)
    {
        var root = MiniJson.Parse(Guard.NotNull(json, nameof(json))) as JsonObject ?? throw new FormatException("Schema JSON is not an object.");
        var versionKey = root.String("version");
        var definitions = AuditfileVersion.Find(versionKey)?.Definitions;

        var tables = root.Objects("tables").Select((t, index) =>
        {
            var name = t.String("name");
            var columns = t.Objects("columns").Select((c, i) => new SchemaColumn(
                c.String("name"), c.String("xmlName"), c.String("path"), c.String("kind"),
                ParseType(c.String("type")), c.String("xsdType"), c.Int("minOccurs"), i,
                Lookup(definitions, $"{name}.{c.String("name")}"))).ToList();

            return new SchemaTable(
                name, t.String("xmlName"), t.String("path"), t.OptionalString("parent"),
                t.Int("minOccurs"), t.OptionalInt("maxOccurs"),
                t.TryGetValue("required", out var r) && r is true,
                t.TryGetValue("expected", out var e) && e is true,
                t.TryGetValue("rowCount", out var count) && count is decimal d ? (long)d : null,
                index, columns,
                t.Objects("keys").Select(k => new ManifestKey(k.String("name"), k.Strings("columns").ToList())).ToList(),
                t.Objects("references").Select(x => new ManifestReference(x.String("name"), x.Strings("columns").ToList(), x.String("table"), x.String("key"))).ToList(),
                Lookup(definitions, name));
        }).ToList();

        return new AuditfileSchema(
            versionKey,
            root.String("namespace"),
            ParseSource(root.String("source")),
            root.OptionalString("dialect"),
            root.TryGetValue("hasRowCounts", out var has) && has is true,
            tables);
    }

    internal static AuditfileSchema Create(AuditfileVersion version, string fileNamespace, VersionSource source, string? dialect, IReadOnlyDictionary<string, long>? rowCounts)
    {
        var manifest = version.Manifest;
        var definitions = version.Definitions;
        var required = new HashSet<string>(manifest.RequiredTables, StringComparer.Ordinal);
        var expected = new HashSet<string>(manifest.ExpectedTables, StringComparer.Ordinal);

        var tables = manifest.Tables.Select(t => new SchemaTable(
            t.Name, t.XmlName, t.Path, t.Parent, t.MinOccurs, t.MaxOccurs,
            required.Contains(t.Name), expected.Contains(t.Name),
            rowCounts is null ? null : rowCounts.TryGetValue(t.Name, out var count) ? count : 0,
            t.Index,
            t.Columns.Select(c => new SchemaColumn(c.Name, c.XmlName, c.Path, c.Kind, c.Type, c.XsdType, c.MinOccurs, c.Index,
                Lookup(definitions, $"{t.Name}.{c.Name}"))).ToList(),
            t.Keys, t.References, Lookup(definitions, t.Name))).ToList();

        return new AuditfileSchema(version.Key, fileNamespace, source, dialect, rowCounts is not null, tables);
    }

    private static ElementDefinition? Lookup(IReadOnlyDictionary<string, ElementDefinition>? definitions, string key) =>
        definitions is not null && definitions.TryGetValue(key, out var definition) ? definition : null;

    private static ColumnType ParseType(string type) => type switch
    {
        "decimal" => ColumnType.Decimal,
        "integer" => ColumnType.Integer,
        "date" => ColumnType.Date,
        "datetime" => ColumnType.DateTime,
        "time" => ColumnType.Time,
        "boolean" => ColumnType.Boolean,
        _ => ColumnType.String,
    };

    private static VersionSource ParseSource(string source) => source switch
    {
        "alias" => VersionSource.Alias,
        "forced" => VersionSource.Forced,
        "resolved" => VersionSource.Resolved,
        _ => VersionSource.Official,
    };
}

/// <summary>A table of an import.</summary>
public sealed class SchemaTable
{
    private readonly Dictionary<string, SchemaColumn> _byName;

    internal SchemaTable(
        string name, string xmlName, string path, string? parent, int minOccurs, int? maxOccurs,
        bool isRequired, bool isExpected, long? rowCount, int index,
        IReadOnlyList<SchemaColumn> columns, IReadOnlyList<ManifestKey> keys, IReadOnlyList<ManifestReference> references,
        ElementDefinition? definition)
    {
        Name = name;
        XmlName = xmlName;
        Path = path;
        Parent = parent;
        MinOccurs = minOccurs;
        MaxOccurs = maxOccurs;
        IsRequired = isRequired;
        IsExpected = isExpected;
        RowCount = rowCount;
        Index = index;
        Columns = columns;
        Keys = keys;
        References = references;
        Definition = definition;
        _byName = columns.ToDictionary(c => c.Name, StringComparer.Ordinal);
    }

    /// <summary>Table name, e.g. "tr_line".</summary>
    public string Name { get; }

    /// <summary>Element name, e.g. "trLine".</summary>
    public string XmlName { get; }

    /// <summary>Full XML path.</summary>
    public string Path { get; }

    /// <summary>Parent table name, or null for the root.</summary>
    public string? Parent { get; }

    /// <summary>Minimum occurrences relative to the XML parent element.</summary>
    public int MinOccurs { get; }

    /// <summary>Maximum occurrences relative to the XML parent element; null means unbounded.</summary>
    public int? MaxOccurs { get; }

    /// <summary>Required by the standard.</summary>
    public bool IsRequired { get; }

    /// <summary>Expected in practice.</summary>
    public bool IsExpected { get; }

    /// <summary>Number of rows, when known from the scan phase.</summary>
    public long? RowCount { get; }

    /// <summary>Position in the schema.</summary>
    public int Index { get; }

    /// <summary>Columns; <see cref="AuditfileRow.Values"/> uses the same order.</summary>
    public IReadOnlyList<SchemaColumn> Columns { get; }

    /// <summary>Natural keys.</summary>
    public IReadOnlyList<ManifestKey> Keys { get; }

    /// <summary>References to keys of other tables.</summary>
    public IReadOnlyList<ManifestReference> References { get; }

    /// <summary>Documentation, when available.</summary>
    public ElementDefinition? Definition { get; }

    /// <summary>Finds a column by name, or returns null.</summary>
    public SchemaColumn? FindColumn(string name) => _byName.TryGetValue(name, out var column) ? column : null;

    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>A column of an import table.</summary>
public sealed class SchemaColumn
{
    internal SchemaColumn(string name, string xmlName, string path, string kind, ColumnType type, string xsdType, int minOccurs, int index, ElementDefinition? definition)
    {
        Name = name;
        XmlName = xmlName;
        Path = path;
        Kind = kind;
        Type = type;
        XsdType = xsdType;
        MinOccurs = minOccurs;
        Index = index;
        Definition = definition;
    }

    /// <summary>Column name.</summary>
    public string Name { get; }

    /// <summary>Element or attribute name.</summary>
    public string XmlName { get; }

    /// <summary>Path relative to the table element.</summary>
    public string Path { get; }

    /// <summary>"element" or "attribute".</summary>
    public string Kind { get; }

    /// <summary>Logical type; determines the .NET type of the value.</summary>
    public ColumnType Type { get; }

    /// <summary>XSD type name.</summary>
    public string XsdType { get; }

    /// <summary>Minimum occurrences relative to the XML parent element.</summary>
    public int MinOccurs { get; }

    /// <summary>Position in <see cref="AuditfileRow.Values"/>.</summary>
    public int Index { get; }

    /// <summary>Documentation, when available.</summary>
    public ElementDefinition? Definition { get; }

    /// <inheritdoc />
    public override string ToString() => Name;
}
