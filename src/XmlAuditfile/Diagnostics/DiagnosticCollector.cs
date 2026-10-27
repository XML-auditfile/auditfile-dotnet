using System.Globalization;
using XmlAuditfile.Definitions;
using XmlAuditfile.Internal;
using XmlAuditfile.Reading;

namespace XmlAuditfile.Diagnostics;

/// <summary>
/// Collects diagnostics, formats messages from diagnostics.json and caps the list.
/// The counts per severity cover every diagnostic, also the ones left out of a full list,
/// and a full list makes room for an error by dropping an info or warning, so errors are never hidden behind them.
/// </summary>
internal sealed class DiagnosticCollector(TextLanguage language, int maximum)
{
    private static readonly Lazy<Dictionary<string, JsonObject>> Catalog = new(LoadCatalog);

    private readonly List<Diagnostic> _items = new();
    private readonly int[] _counts = new int[4];
    private Diagnostic? _capped;

    public IReadOnlyList<Diagnostic> Items => _capped is null ? _items : [.. _items, _capped];

    public bool HasFatal => _counts[(int)DiagnosticSeverity.Fatal] > 0;

    public bool HasErrors => HasFatal || _counts[(int)DiagnosticSeverity.Error] > 0;

    /// <summary>True when diagnostics were left out of <see cref="Items"/>.</summary>
    public bool IsTruncated => _capped is not null;

    /// <summary>Number of diagnostics per severity, including the ones left out.</summary>
    public IReadOnlyList<int> Counts => _counts;

    /// <summary>The codes of layer "dialect", the only ones a dialect correction may report.</summary>
    public static IEnumerable<string> DialectCodes =>
        Catalog.Value.Values.Where(c => c.OptionalString("layer") == "dialect").Select(c => c.String("code"));

    public void Add(string code, Location location, params (string Name, object? Value)[] arguments)
    {
        if (!Catalog.Value.TryGetValue(code, out var entry))
        {
            throw new InvalidOperationException($"Diagnostic code {code} is not defined in diagnostics.json.");
        }

        var severity = ParseSeverity(entry.String("severity"));
        _counts[(int)severity]++;

        if (_items.Count >= maximum && !MakeRoomFor(severity))
        {
            _capped ??= Create("XAF-X900", Location.None, ("max", maximum));
            return;
        }

        _items.Add(Create(code, location, arguments));
    }

    /// <summary>
    /// A fatal diagnostic is always kept. An error replaces the last info or warning in the list;
    /// a dropped diagnostic still counts, and the list is marked as truncated.
    /// </summary>
    private bool MakeRoomFor(DiagnosticSeverity severity)
    {
        if (severity == DiagnosticSeverity.Fatal)
        {
            return true;
        }

        if (severity < DiagnosticSeverity.Error)
        {
            return false;
        }

        var index = _items.FindLastIndex(d => d.Severity == DiagnosticSeverity.Info);
        if (index < 0)
        {
            index = _items.FindLastIndex(d => d.Severity == DiagnosticSeverity.Warning);
        }

        if (index < 0)
        {
            return false;
        }

        _items.RemoveAt(index);
        _capped ??= Create("XAF-X900", Location.None, ("max", maximum));
        return true;
    }

    private Diagnostic Create(string code, Location location, params (string Name, object? Value)[] arguments)
    {
        var entry = Catalog.Value[code];
        var message = LocalizedText.From(entry.Object("message"))?.Get(language) ?? code;
        foreach (var argument in arguments)
        {
            message = message.Replace("{" + argument.Name + "}", ValueConverter.ToText(argument.Value));
        }

        return new Diagnostic(code, entry.String("layer"), ParseSeverity(entry.String("severity")), message,
            location.Part, location.Line, location.Position, location.Path);
    }

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
