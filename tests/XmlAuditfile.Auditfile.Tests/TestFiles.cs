namespace XmlAuditfile.Auditfile.Tests;

/// <summary>
/// The XAF 3.2 sample file of this repository (Data/xaf-3.2-sample.xaf) and variations of it, written to a temporary folder.
/// The sample covers the same tables as the official test file and holds two intended findings: XAF-R001 and XAF-R003.
/// </summary>
internal static class TestFiles
{
    public const string OfficialNamespace = "http://www.auditfiles.nl/XAF/3.2";

    public static string Sample { get; } = Path.Combine(AppContext.BaseDirectory, "Data", "xaf-3.2-sample.xaf");

    public static string SampleText { get; } = File.ReadAllText(Sample).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

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
        Write("namespace.xaf", SampleText.Replace($"xmlns=\"{OfficialNamespace}\"", $"xmlns=\"{ns}\"", StringComparison.Ordinal));
}
