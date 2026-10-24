namespace XmlAuditfile.Internal;

/// <summary>Keys of several values (composite keys, view groups and joins), joined with a character that cannot occur in XML text.</summary>
internal static class CompositeKey
{
    public const char Separator = '\u001f';

    public static string Join(IEnumerable<string> parts) => string.Join(Separator.ToString(), parts);

    /// <summary>The key for a message: the parts separated by "/".</summary>
    public static string Display(string key) => key.Replace(Separator, '/');
}
