using XmlAuditfile.Memory;
using Xunit;

namespace XmlAuditfile.Tests;

public class VersionTests
{
    [Fact]
    public void UnknownNamespaceIsFatal()
    {
        var report = Auditfile.Scan(TestFiles.Source(TestFiles.WithNamespace("urn:example:unknown")), null, TestContext.Current.CancellationToken);

        Assert.Contains(report.Diagnostics, d => d.Code == "XAF-V001" && d.Severity == DiagnosticSeverity.Fatal);
        Assert.Null(report.Schema);
    }

    [Fact]
    public async Task ForcedVersionReadsAnUnknownNamespaceIncludingXsdValidation()
    {
        var path = TestFiles.WithNamespace("urn:example:unknown");

        var report = await Auditfile.ReadAsync(TestFiles.Source(path), new MemorySink(), new ReadOptions { Version = AuditfileVersion.Financial.V3_2 }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(report.Loaded);
        Assert.Equal(VersionSource.Forced, report.Schema!.Source);
        Assert.Contains(report.Diagnostics, d => d.Code == "XAF-V003");
        Assert.DoesNotContain(report.Diagnostics, d => d.Code == "XAF-D001");
    }

    [Fact]
    public void ResolverDerivesTheVersionFromTheNamespace()
    {
        var report = Auditfile.Scan(TestFiles.Source(TestFiles.WithNamespace("http://www.auditfiles.nl/XAF/3.2/")), new ReadOptions { UseResolver = true }, TestContext.Current.CancellationToken);

        Assert.Equal("xaf/3.2", report.Schema!.VersionKey);
        Assert.Equal(VersionSource.Resolved, report.Schema.Source);
    }

    [Fact]
    public void ResolverIsOffByDefault()
    {
        var report = Auditfile.Scan(TestFiles.Source(TestFiles.WithNamespace("http://www.auditfiles.nl/XAF/3.2/")), null, TestContext.Current.CancellationToken);

        Assert.Contains(report.Diagnostics, d => d.Code == "XAF-V001");
    }
}
