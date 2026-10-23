namespace XmlAuditfile.Auditfile.Tests;

/// <summary>The official XAF 3.2 test file and variations of it, written to a temporary folder.</summary>
internal static class TestFiles
{
    public const string OfficialNamespace = "http://www.auditfiles.nl/XAF/3.2";

    public static string Official { get; } = Path.Combine(AppContext.BaseDirectory, "Data", "xaf-3.2-official-test.xml");

    public static string OfficialText { get; } = File.ReadAllText(Official).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    public static string Write(string name, string content)
    {
        var folder = Path.Combine(Path.GetTempPath(), "xaftoolkit-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>A file on disk as a source; the reader itself never opens files.</summary>
    public static AuditfileSource Source(string path) =>
        AuditfileSource.FromFactory(() => File.OpenRead(path), Path.GetFileName(path));

    public static string WithNamespace(string ns) =>
        Write("namespace.xaf", OfficialText.Replace($"xmlns=\"{OfficialNamespace}\"", $"xmlns=\"{ns}\"", StringComparison.Ordinal));
}
