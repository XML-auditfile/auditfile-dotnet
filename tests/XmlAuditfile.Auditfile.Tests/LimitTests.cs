using System.Text;
using XmlAuditfile.Auditfile.Memory;
using Xunit;

namespace XmlAuditfile.Auditfile.Tests;

/// <summary>Size limits: a huge value or document is reported, never read into memory as a whole.</summary>
public class LimitTests
{
    private const string CompanyName = "<companyName>Bakkerij De Korenschoof B.V.</companyName>";

    [Fact]
    public async Task ValueLongerThanTheMaximumIsSkippedWithAWarning()
    {
        var text = TestFiles.SampleText.Replace(CompanyName, $"<companyName>{new string('x', 70_000)}</companyName>", StringComparison.Ordinal);
        var sink = new MemorySink();

        var report = await Read(text, sink, new ReadOptions { Mode = ReadMode.Unverified });

        Assert.True(report.Loaded);
        Assert.Contains(report.Diagnostics, d => d.Code == "XAF-S008" && d.Message.Contains("company.company_name", StringComparison.Ordinal)
            && d.Message.Contains("70000", StringComparison.Ordinal) && d.Message.Contains("65536", StringComparison.Ordinal));
        Assert.Null(sink.Rows("company")[0]["company_name"]);
    }

    [Fact]
    public async Task MaximumValueLengthIsAnOption()
    {
        var sink = new MemorySink();

        var report = await Read(TestFiles.SampleText, sink, new ReadOptions { MaxValueLength = 10 });

        Assert.Contains(report.Diagnostics, d => d.Code == "XAF-S008" && d.Message.Contains("company.company_name", StringComparison.Ordinal));
        Assert.Equal("EUR", sink.Rows("header")[0]["cur_code"]);
    }

    [Fact]
    public async Task ValueOfExactlyTheMaximumIsKept()
    {
        var sink = new MemorySink();

        await Read(TestFiles.SampleText, sink, new ReadOptions { MaxValueLength = "Bakkerij De Korenschoof B.V.".Length });

        Assert.Equal("Bakkerij De Korenschoof B.V.", sink.Rows("company")[0]["company_name"]);
    }

    [Fact]
    public async Task DocumentLargerThanTheMaximumStops()
    {
        var report = await Read(TestFiles.SampleText, new MemorySink(), new ReadOptions { MaxCharactersInDocument = 5000 });

        Assert.Contains(report.Diagnostics, d => d.Code == "XAF-X001" && d.Severity == DiagnosticSeverity.Fatal);
        Assert.False(report.Loaded);
    }

    private static Task<AuditfileReport> Read(string text, MemorySink sink, ReadOptions options)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return Auditfile.ReadAsync(AuditfileSource.FromFactory(() => new MemoryStream(bytes), "limits.xaf"), sink, options, TestContext.Current.CancellationToken);
    }
}
