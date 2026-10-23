namespace XmlAuditfile.Auditfile.Definitions;

/// <summary>Language for definitions and diagnostic messages.</summary>
public enum TextLanguage
{
    /// <summary>Dutch: the official language of the Belastingdienst publications.</summary>
    Dutch,

    /// <summary>English; falls back to Dutch where no translation exists.</summary>
    English,
}

/// <summary>A text in Dutch and English. English falls back to Dutch when empty.</summary>
public sealed class LocalizedText
{
    internal LocalizedText(string? dutch, string? english)
    {
        Dutch = string.IsNullOrEmpty(dutch) ? null : dutch;
        English = string.IsNullOrEmpty(english) ? null : english;
    }

    /// <summary>Dutch text, or null.</summary>
    public string? Dutch { get; }

    /// <summary>English text, or null.</summary>
    public string? English { get; }

    /// <summary>The text in the requested language, falling back to the other language.</summary>
    public string? Get(TextLanguage language) => language == TextLanguage.English ? English ?? Dutch : Dutch ?? English;

    /// <inheritdoc />
    public override string ToString() => Get(TextLanguage.Dutch) ?? "";

    internal static LocalizedText? From(Internal.JsonObject? json) =>
        json is null ? null : new LocalizedText(json.OptionalString("nl"), json.OptionalString("en"));
}
