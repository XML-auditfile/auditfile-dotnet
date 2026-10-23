using XmlAuditfile.Auditfile.Internal;

namespace XmlAuditfile.Auditfile.Definitions;

/// <summary>Documentation of a table, column or flattened group, from definition.json.</summary>
public sealed class ElementDefinition
{
    internal ElementDefinition(JsonObject json)
    {
        Key = json.String("key");
        Kind = json.String("kind");
        Label = LocalizedText.From(json.Object("label"));
        Definition = LocalizedText.From(json.Object("definition"));
        Description = LocalizedText.From(json.Object("description"));

        var codes = new Dictionary<string, LocalizedText>(StringComparer.Ordinal);
        if (json.Object("codes") is { } codeList)
        {
            foreach (var pair in codeList)
            {
                codes[pair.Key] = LocalizedText.From(pair.Value as JsonObject) ?? new LocalizedText(null, null);
            }
        }

        Codes = codes;
    }

    /// <summary>"tr_line" (table), "tr_line.amnt" (column) or "tr_line.currency" (group).</summary>
    public string Key { get; }

    /// <summary>"table", "column" or "group".</summary>
    public string Kind { get; }

    /// <summary>Official name, usually English only.</summary>
    public LocalizedText? Label { get; }

    /// <summary>Meaning according to the official documentation.</summary>
    public LocalizedText? Definition { get; }

    /// <summary>Practical explanation.</summary>
    public LocalizedText? Description { get; }

    /// <summary>Meaning of code values, e.g. "D" = Debit.</summary>
    public IReadOnlyDictionary<string, LocalizedText> Codes { get; }
}
