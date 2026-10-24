using XmlAuditfile.Internal;

namespace XmlAuditfile.Definitions;

/// <summary>The structure of one audit file version, from manifest.json.</summary>
public sealed class AuditfileManifest
{
    private readonly Dictionary<string, ManifestTable> _byName;

    internal AuditfileManifest(JsonObject json)
    {
        FormatVersion = json.Int("formatVersion");
        if (FormatVersion != 1)
        {
            throw new NotSupportedException($"Manifest format version {FormatVersion} is not supported by this reader.");
        }

        Standard = json.String("standard");
        Version = json.String("version");
        Namespace = json.String("namespace");
        RootElement = json.String("rootElement");
        XsdSha256 = json.Object("xsd")?.String("sha256") ?? "";
        RequiredTables = json.Strings("requiredTables").ToList();
        ExpectedTables = json.Strings("expectedTables").ToList();
        Tables = json.Objects("tables").Select((t, i) => new ManifestTable(t, i)).ToList();
        _byName = Tables.ToDictionary(t => t.Name, StringComparer.Ordinal);
    }

    /// <summary>Format version of manifest.json.</summary>
    public int FormatVersion { get; }

    /// <summary>Audit file type, e.g. "xaf".</summary>
    public string Standard { get; }

    /// <summary>Version, e.g. "3.2".</summary>
    public string Version { get; }

    /// <summary>Official namespace.</summary>
    public string Namespace { get; }

    /// <summary>Name of the root element.</summary>
    public string RootElement { get; }

    /// <summary>SHA-256 of the official XSD.</summary>
    public string XsdSha256 { get; }

    /// <summary>Tables required by the XSD.</summary>
    public IReadOnlyList<string> RequiredTables { get; }

    /// <summary>Tables optional in the XSD but expected in practice.</summary>
    public IReadOnlyList<string> ExpectedTables { get; }

    /// <summary>All tables in document order.</summary>
    public IReadOnlyList<ManifestTable> Tables { get; }

    /// <summary>Finds a table by name, or returns null.</summary>
    public ManifestTable? FindTable(string name) => _byName.TryGetValue(name, out var table) ? table : null;
}

/// <summary>A table of the manifest.</summary>
public sealed class ManifestTable
{
    internal ManifestTable(JsonObject json, int index)
    {
        Index = index;
        Name = json.String("name");
        XmlName = json.String("xmlName");
        Path = json.String("path");
        Parent = json.OptionalString("parent");
        MinOccurs = json.Int("minOccurs");
        MaxOccurs = json.OptionalInt("maxOccurs");
        Keys = json.Objects("keys").Select(k => new ManifestKey(k.String("name"), k.Strings("columns").ToList())).ToList();
        References = json.Objects("references").Select(r => new ManifestReference(
            r.String("name"), r.Strings("columns").ToList(), r.String("table"), r.String("key"))).ToList();
        Columns = json.Objects("columns").Select((c, i) => new ManifestColumn(c, i)).ToList();
    }

    /// <summary>Position in the manifest.</summary>
    public int Index { get; }

    /// <summary>Table name, e.g. "tr_line".</summary>
    public string Name { get; }

    /// <summary>Element name, e.g. "trLine".</summary>
    public string XmlName { get; }

    /// <summary>Full XML path, e.g. "auditfile/company/transactions/journal/transaction/trLine".</summary>
    public string Path { get; }

    /// <summary>Name of the parent table, or null for the root.</summary>
    public string? Parent { get; }

    /// <summary>Minimum occurrences relative to the XML parent element.</summary>
    public int MinOccurs { get; }

    /// <summary>Maximum occurrences relative to the XML parent element; null means unbounded.</summary>
    public int? MaxOccurs { get; }

    /// <summary>Natural keys.</summary>
    public IReadOnlyList<ManifestKey> Keys { get; }

    /// <summary>References to keys of other tables.</summary>
    public IReadOnlyList<ManifestReference> References { get; }

    /// <summary>Columns in XSD order.</summary>
    public IReadOnlyList<ManifestColumn> Columns { get; }
}

/// <summary>A column of a manifest table.</summary>
public sealed class ManifestColumn
{
    internal ManifestColumn(JsonObject json, int index)
    {
        Index = index;
        Name = json.String("name");
        XmlName = json.String("xmlName");
        Path = json.String("path");
        Kind = json.String("kind");
        Type = ParseType(json.String("type"));
        XsdType = json.String("xsdType");
        MinOccurs = json.Int("minOccurs");
    }

    /// <summary>Position within the table.</summary>
    public int Index { get; }

    /// <summary>Column name, e.g. "amnt".</summary>
    public string Name { get; }

    /// <summary>Element or attribute name.</summary>
    public string XmlName { get; }

    /// <summary>Path relative to the table element: "amnt", "currency/curCode", "@attr" or ".".</summary>
    public string Path { get; }

    /// <summary>"element" or "attribute".</summary>
    public string Kind { get; }

    /// <summary>Logical type.</summary>
    public ColumnType Type { get; }

    /// <summary>Name of the XSD type.</summary>
    public string XsdType { get; }

    /// <summary>Minimum occurrences relative to the XML parent element.</summary>
    public int MinOccurs { get; }

    private static ColumnType ParseType(string type) => type switch
    {
        "string" => ColumnType.String,
        "decimal" => ColumnType.Decimal,
        "integer" => ColumnType.Integer,
        "date" => ColumnType.Date,
        "datetime" => ColumnType.DateTime,
        "time" => ColumnType.Time,
        "boolean" => ColumnType.Boolean,
        _ => throw new NotSupportedException($"Unknown column type '{type}'."),
    };
}

/// <summary>A natural key: the column values are unique within an import.</summary>
public sealed class ManifestKey
{
    internal ManifestKey(string name, IReadOnlyList<string> columns)
    {
        Name = name;
        Columns = columns;
    }

    /// <summary>Key name from the XSD.</summary>
    public string Name { get; }

    /// <summary>Key columns.</summary>
    public IReadOnlyList<string> Columns { get; }
}

/// <summary>A reference from columns of one table to a key of another table.</summary>
public sealed class ManifestReference
{
    internal ManifestReference(string name, IReadOnlyList<string> columns, string table, string key)
    {
        Name = name;
        Columns = columns;
        Table = table;
        Key = key;
    }

    /// <summary>Reference name from the XSD.</summary>
    public string Name { get; }

    /// <summary>Referencing columns.</summary>
    public IReadOnlyList<string> Columns { get; }

    /// <summary>Referenced table.</summary>
    public string Table { get; }

    /// <summary>Referenced key.</summary>
    public string Key { get; }
}
