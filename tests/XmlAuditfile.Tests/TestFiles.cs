namespace XmlAuditfile.Tests;

/// <summary>
/// The XAF 3.2 sample file of this repository (Data/xaf-3.2-sample.xaf) and variations of it, written to a temporary folder.
/// The sample covers the same tables as the official test file and holds two intended findings: XAF-R001 and XAF-R003.
/// </summary>
internal static class TestFiles
{
    public const string OfficialNamespace = "http://www.auditfiles.nl/XAF/3.2";

    public static string Sample { get; } = Path.Combine(AppContext.BaseDirectory, "Data", "xaf-3.2-sample.xaf");

    public static string SampleText { get; } = File.ReadAllText(Sample).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    /// <summary>One folder per test run, removed when the run ends.</summary>
    private static readonly Lazy<string> RunFolder = new(() =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "xmlauditfile-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // A file still open by a failed test; the system cleans the temp folder eventually.
            }
        };

        return folder;
    });

    public static string Write(string name, string content)
    {
        var folder = Path.Combine(RunFolder.Value, Guid.NewGuid().ToString("N"));
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
