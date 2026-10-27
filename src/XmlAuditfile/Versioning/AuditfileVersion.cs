using System.Security.Cryptography;
using System.Xml;
using System.Xml.Schema;
using XmlAuditfile.Definitions;
using XmlAuditfile.Internal;

namespace XmlAuditfile;

/// <summary>
/// One audit file type and version, with its manifest, XSD, definitions and dialects.
/// Use the constants, e.g. <c>AuditfileVersion.Financial.V3_2</c>, or <see cref="Parse"/> with "xaf/3.2".
/// </summary>
public sealed partial class AuditfileVersion
{
    private static readonly Lazy<List<AuditfileVersion>> Registry = new(LoadRegistry);

    // Constants such as Financial.V3_2 look a version up on every use: by key, not by searching the list.
    private static readonly Lazy<Dictionary<string, AuditfileVersion>> ByKey =
        new(() => Registry.Value.ToDictionary(v => v.Key, StringComparer.OrdinalIgnoreCase));

    private readonly Lazy<AuditfileManifest> _manifest;
    private readonly Lazy<IReadOnlyDictionary<string, ElementDefinition>> _definitions;
    private readonly Lazy<IReadOnlyList<Dialect>> _dialects;
    private readonly Lazy<IReadOnlyList<ViewDefinition>> _views;

    private AuditfileVersion(JsonObject json)
    {
        Key = json.String("key");
        Type = json.String("type");
        TypeName = json.String("typeName");
        Version = json.String("version");
        Folder = json.String("folder");
        RootElement = json.String("rootElement");
        Provenance = json.String("provenance");

        var namespaces = json.Objects("namespaces").ToList();
        Namespace = namespaces.First(n => n.String("kind") == "official").String("uri");
        Aliases = namespaces.Where(n => n.String("kind") == "alias").Select(n => n.String("uri")).ToList();

        _manifest = new(() => new AuditfileManifest(ReadJson("manifest.json")));
        _definitions = new(() => LoadDefinitions());
        _dialects = new(() => ReadJson("dialects.json").Objects("dialects").Select(d => new Dialect(d)).ToList());
        _views = new(() => ViewDefinition.Load(Type));
    }

    /// <summary>All versions this reader knows, in registry order.</summary>
    public static IReadOnlyList<AuditfileVersion> All => Registry.Value;

    /// <summary>Registry key, e.g. "xaf/3.2".</summary>
    public string Key { get; }

    /// <summary>Audit file type, e.g. "xaf".</summary>
    public string Type { get; }

    /// <summary>Type name used for version objects, e.g. "financial".</summary>
    public string TypeName { get; }

    /// <summary>Version, e.g. "3.2".</summary>
    public string Version { get; }

    /// <summary>Folder of the definitions, e.g. "standards/xaf/3_2".</summary>
    public string Folder { get; }

    /// <summary>Name of the root element.</summary>
    public string RootElement { get; }

    /// <summary>"official" or "reconstructed".</summary>
    public string Provenance { get; }

    /// <summary>True when the XSD is the official one published by the Belastingdienst.</summary>
    public bool HasOfficialXsd => Provenance == "official";

    /// <summary>The official namespace.</summary>
    public string Namespace { get; }

    /// <summary>Other namespaces known to be used for this version.</summary>
    public IReadOnlyList<string> Aliases { get; }

    /// <summary>Structure: tables and columns.</summary>
    public AuditfileManifest Manifest => _manifest.Value;

    /// <summary>Definitions by key ("tr_line", "tr_line.amnt").</summary>
    public IReadOnlyDictionary<string, ElementDefinition> Definitions => _definitions.Value;

    /// <summary>Known dialects of this version: the built-in ones, then those added with <see cref="Auditfile.Dialects"/>.</summary>
    public IReadOnlyList<Dialect> Dialects => Combine(_dialects.Value, Auditfile.Dialects.For(this));

    /// <summary>
    /// Views of this audit file type, e.g. the trial balance: the built-in ones, then those added with <see cref="Auditfile.Views"/>.
    /// Shared by all versions of the type; see <see cref="ViewResult.IsAvailable"/>.
    /// </summary>
    public IReadOnlyList<ViewDefinition> Views => Combine(_views.Value, Auditfile.Views.For(Type));

    /// <summary>Finds a view by name, or returns null.</summary>
    public ViewDefinition? FindView(string name) => Views.FirstOrDefault(v => v.Name == name);

    /// <summary>Finds a version by key, e.g. "xaf/3.2". Returns null when unknown.</summary>
    public static AuditfileVersion? Find(string key) =>
        ByKey.Value.TryGetValue(Guard.NotNull(key, nameof(key)), out var version) ? version : null;

    /// <summary>Returns the version for a key such as "xaf/3.2".</summary>
    /// <exception cref="ArgumentException">The key is unknown.</exception>
    public static AuditfileVersion Parse(string key) =>
        Find(Guard.NotNull(key, nameof(key))) ?? throw new ArgumentException($"Unknown audit file version '{key}'.", nameof(key));

    /// <summary>Opens the XSD of this version.</summary>
    public Stream OpenXsd() => EmbeddedResources.Open($"{Folder}/auditfile.xsd");

    /// <summary>Compiles the XSD of this version into a new schema set, ready for validation.</summary>
    public XmlSchemaSet CreateSchemaSet() => CompileSchemaSet(OpenXsd(), $"{Folder}/auditfile.xsd");

    /// <inheritdoc />
    public override string ToString() => Key;

    internal static XmlSchemaSet CompileSchemaSet(Stream xsd, string name)
    {
        var resolver = new EmbeddedResolver();
        var set = new XmlSchemaSet { XmlResolver = resolver };
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = resolver };
        using (xsd)
        using (var reader = XmlReader.Create(xsd, settings, $"{EmbeddedResolver.Scheme}:///{name}"))
        {
            set.Add(null, reader);
        }

        set.Compile();
        return set;
    }

    internal static string Sha256(Stream stream)
    {
        using (stream)
        using (var sha = SHA256.Create())
        {
            return Hex(sha.ComputeHash(stream));
        }
    }

    internal static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();

    private static IReadOnlyList<T> Combine<T>(IReadOnlyList<T> builtIn, IReadOnlyList<T> custom) =>
        custom.Count == 0 ? builtIn : [.. builtIn, .. custom];

    private JsonObject ReadJson(string file) =>
        MiniJson.Parse(EmbeddedResources.ReadText($"{Folder}/{file}")) as JsonObject
        ?? throw new FormatException($"{Folder}/{file} is not a JSON object.");

    private Dictionary<string, ElementDefinition> LoadDefinitions() =>
        ReadJson("definition.json").Objects("entries")
            .Select(e => new ElementDefinition(e))
            .ToDictionary(d => d.Key, StringComparer.Ordinal);

    private static List<AuditfileVersion> LoadRegistry()
    {
        var registry = MiniJson.Parse(EmbeddedResources.ReadText("registry.json")) as JsonObject
            ?? throw new FormatException("registry.json is not a JSON object.");
        return registry.Objects("standards").Select(s => new AuditfileVersion(s)).ToList();
    }

    /// <summary>Resolves XSD includes only from the embedded definitions; nothing from disk or network.</summary>
    private sealed class EmbeddedResolver : XmlUrlResolver
    {
        public const string Scheme = "embedded";

        public override object GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            var name = absoluteUri.Scheme == Scheme ? absoluteUri.AbsolutePath.TrimStart('/') : null;
            return name is not null && EmbeddedResources.Exists(name)
                ? EmbeddedResources.Open(name)
                : throw new XmlException($"Refused to resolve '{absoluteUri}': only embedded definitions are allowed.");
        }
    }
}
