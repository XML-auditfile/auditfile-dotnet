using System.Reflection;
using System.Text;

namespace XmlAuditfile.Auditfile.Definitions;

/// <summary>Reads the files from auditfile-spec/generated/ that are embedded in this assembly.</summary>
internal static class EmbeddedResources
{
    private static readonly Assembly Assembly = typeof(EmbeddedResources).Assembly;

    public static bool Exists(string name) => Assembly.GetManifestResourceInfo(name) is not null;

    public static Stream Open(string name) =>
        Assembly.GetManifestResourceStream(name)
        ?? throw new InvalidOperationException($"Embedded definition '{name}' is missing. Was auditfile-spec/generated/ present at build time?");

    public static string ReadText(string name)
    {
        using var reader = new StreamReader(Open(name), new UTF8Encoding(false));
        return reader.ReadToEnd();
    }
}
