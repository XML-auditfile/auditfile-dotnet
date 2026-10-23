using System.Globalization;
using XmlAuditfile.Auditfile.Definitions;
using XmlAuditfile.Auditfile.Internal;

namespace XmlAuditfile.Auditfile.Diagnostics;

/// <summary>Collects diagnostics, formats messages from diagnostics.json and caps the total.</summary>
internal sealed class DiagnosticCollector(TextLanguage language, int maximum)
{
    private static readonly Lazy<Dictionary<string, JsonObject>> Catalog = new(LoadCatalog);

    private readonly List<Diagnostic> _items = new();
    private bool _capped;

    public IReadOnlyList<Diagnostic> Items => _items;

    public bool HasFatal { get; private set; }

    public bool HasErrors { get; private set; }

    public void Add(string code, Location location, params (string Name, object? Value)[] arguments)
    {
        if (!Catalog.Value.TryGetValue(code, out var entry))
        {
            throw new InvalidOperationException($"Diagnostic code {code} is not defined in diagnostics.json.");
        }

        var severity = ParseSeverity(entry.String("severity"));
        HasFatal |= severity == DiagnosticSeverity.Fatal;
        HasErrors |= severity >= DiagnosticSeverity.Error;

        if (_items.Count >= maximum && severity != DiagnosticSeverity.Fatal)
        {
            if (!_capped)
            {
                _capped = true;
                _items.Add(Create("XAF-X900", Location.None, ("max", maximum)));
            }

            return;
        }

        _items.Add(Create(code, location, arguments));
    }

    private Diagnostic Create(string code, Location location, params (string Name, object? Value)[] arguments)
    {
        var entry = Catalog.Value[code];
        var message = LocalizedText.From(entry.Object("message"))?.Get(language) ?? code;
        foreach (var argument in arguments)
        {
            message = message.Replace("{" + argument.Name + "}", Format(argument.Value));
        }

        return new Diagnostic(code, entry.String("layer"), ParseSeverity(entry.String("severity")), message,
            location.Part, location.Line, location.Position, location.Path);
    }

    private static string Format(object? value) => value switch
    {
        null => "",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static DiagnosticSeverity ParseSeverity(string severity) => severity switch
    {
        "info" => DiagnosticSeverity.Info,
        "warning" => DiagnosticSeverity.Warning,
        "error" => DiagnosticSeverity.Error,
        _ => DiagnosticSeverity.Fatal,
    };

    private static Dictionary<string, JsonObject> LoadCatalog()
    {
        var json = MiniJson.Parse(Definitions.EmbeddedResources.ReadText("diagnostics.json")) as JsonObject
            ?? throw new FormatException("diagnostics.json is not a JSON object.");
        return json.Objects("codes").ToDictionary(c => c.String("code"), StringComparer.Ordinal);
    }
}

/// <summary>Where a diagnostic applies.</summary>
internal readonly struct Location(int? part, int? line, int? position, string? path)
{
    public static readonly Location None = new(null, null, null, null);

    public int? Part { get; } = part;

    public int? Line { get; } = line;

    public int? Position { get; } = position;

    public string? Path { get; } = path;
}
