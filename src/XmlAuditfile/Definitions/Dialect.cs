using XmlAuditfile.Internal;

namespace XmlAuditfile.Definitions;

/// <summary>Corrections for a package that deviates from the standard, from dialects.json.</summary>
public sealed class Dialect
{
    internal Dialect(JsonObject json)
    {
        Name = json.String("name");
        Description = LocalizedText.From(json.Object("description"));
        var match = json.Object("match");
        Namespaces = match?.Strings("namespaces").ToList() ?? new List<string>();
        SoftwareDescription = match?.OptionalString("softwareDesc");
        Corrections = json.Objects("corrections").Select(c => new DialectCorrection(c)).ToList();
    }

    /// <summary>Dialect name.</summary>
    public string Name { get; }

    /// <summary>What the dialect is for.</summary>
    public LocalizedText? Description { get; }

    /// <summary>Namespaces that select this dialect.</summary>
    public IReadOnlyList<string> Namespaces { get; }

    /// <summary>Value of header/softwareDesc that selects this dialect.</summary>
    public string? SoftwareDescription { get; }

    /// <summary>Corrections to apply.</summary>
    public IReadOnlyList<DialectCorrection> Corrections { get; }
}

/// <summary>One correction of a dialect.</summary>
public sealed class DialectCorrection
{
    internal DialectCorrection(JsonObject json)
    {
        Code = json.String("code");
        Action = DefinitionNames.ParseDialectAction(json.String("action"));
        Paths = json.Strings("paths").ToList();
        From = json.OptionalString("from");
        To = json.OptionalString("to");
    }

    /// <summary>Diagnostic code reported when the correction is applied.</summary>
    public string Code { get; }

    /// <summary>What the correction does.</summary>
    public DialectAction Action { get; }

    /// <summary>XML paths the correction applies to.</summary>
    public IReadOnlyList<string> Paths { get; }

    /// <summary>Original value (replaceNamespace).</summary>
    public string? From { get; }

    /// <summary>Replacement value (replaceNamespace).</summary>
    public string? To { get; }
}
