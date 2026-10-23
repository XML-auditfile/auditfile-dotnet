using Xunit;

namespace XmlAuditfile.Auditfile.Tests;

public class SecurityTests
{
    [Fact]
    public void DocumentTypeDefinitionsAreRefused()
    {
        var root = TestFiles.OfficialText[TestFiles.OfficialText.IndexOf("<auditfile", StringComparison.Ordinal)..];
        var path = TestFiles.Write("dtd.xaf", "<?xml version=\"1.0\"?><!DOCTYPE a [<!ENTITY x SYSTEM \"file:///etc/passwd\">]>" + root);

        var report = Auditfile.Scan(TestFiles.Source(path));

        Assert.Contains(report.Diagnostics, d => d.Code == "XAF-X001");
        Assert.Null(report.Schema);
    }

    [Fact]
    public void TruncatedFilesStopWithAFatalDiagnostic()
    {
        var path = TestFiles.Write("broken.xaf", TestFiles.OfficialText[..(TestFiles.OfficialText.Length / 2)]);

        var report = Auditfile.Scan(TestFiles.Source(path));

        Assert.Contains(report.Diagnostics, d => d.Code == "XAF-X001" && d.Line > 0);
    }

    [Fact]
    public void ExcessiveNestingIsRefused()
    {
        var deep = string.Concat(Enumerable.Repeat("<x>", 100)) + string.Concat(Enumerable.Repeat("</x>", 100));
        var path = TestFiles.Write("deep.xaf", TestFiles.OfficialText.Replace("<header>", "<header>" + deep, StringComparison.Ordinal));

        var report = Auditfile.Scan(TestFiles.Source(path), new ReadOptions { Mode = ReadMode.Unverified });

        Assert.Contains(report.Diagnostics, d => d.Code == "XAF-X003");
    }
}
