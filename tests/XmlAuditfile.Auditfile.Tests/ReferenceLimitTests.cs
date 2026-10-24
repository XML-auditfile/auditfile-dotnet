using System.Text;
using Xunit;

namespace XmlAuditfile.Auditfile.Tests;

/// <summary>When the reference check stops keeping unresolved values, it says so once (XAF-R017).</summary>
public class ReferenceLimitTests
{
    [Fact]
    public async Task ReachingThePendingLimitIsReportedOnce()
    {
        var report = await Read(WithUnknownCustomers(100_001));

        var warnings = report.Diagnostics.Where(d => d.Code == "XAF-R017").ToList();
        var warning = Assert.Single(warnings);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("tr_line.cust_sup_id", warning.Message, StringComparison.Ordinal);
        Assert.Contains("100000", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BelowTheLimitThereIsNoWarning()
    {
        var report = await Read(WithUnknownCustomers(10));

        Assert.DoesNotContain(report.Diagnostics, d => d.Code == "XAF-R017");
        Assert.Contains(report.Diagnostics, d => d.Code == "XAF-R003");
    }

    /// <summary>The sample with extra lines in its first transaction, each referring to a customer that does not exist.</summary>
    private static string WithUnknownCustomers(int count)
    {
        var lines = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            lines.Append("<trLine><nr>x</nr><accID>1300</accID><docRef>d</docRef><effDate>2025-01-15</effDate><amnt>1.00</amnt><amntTp>D</amntTp><custSupID>U")
                .Append(i).Append("</custSupID></trLine>");
        }

        var text = TestFiles.SampleText;
        var end = text.IndexOf("</transaction>", StringComparison.Ordinal);
        return text[..end] + lines + text[end..];
    }

    private static Task<AuditfileReport> Read(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var source = AuditfileSource.FromFactory(() => new MemoryStream(bytes), "references.xaf");
        return Auditfile.ReadAsync(source, new Memory.MemorySink(), new ReadOptions { Mode = ReadMode.Unverified }, TestContext.Current.CancellationToken);
    }
}
