using XmlAuditfile.Auditfile.Memory;
using Xunit;

namespace XmlAuditfile.Auditfile.Tests;

public class ReadTests
{
    [Fact]
    public async Task ReadsTheSampleFile()
    {
        var sink = new MemorySink();
        var report = await Auditfile.ReadAsync(TestFiles.Source(TestFiles.Sample), sink);

        Assert.True(report.Loaded);
        Assert.Equal(VersionSource.Official, report.Schema!.Source);
        Assert.Equal(27, report.RowCounts.Count);
        Assert.Equal(327.00m, sink.Rows("tr_line")[0]["amnt"]);
        Assert.Equal(new DateTime(2025, 1, 15), sink.Rows("tr_line")[0]["eff_date"]);
    }

    [Fact]
    public async Task BusinessRulesFindTheUnbalancedTransactionInTheSampleFile()
    {
        var report = await Auditfile.ValidateAsync(TestFiles.Source(TestFiles.Sample));

        Assert.Contains(report.Diagnostics, d => d.Code == "XAF-R001");
        Assert.Contains(report.Diagnostics, d => d.Code == "XAF-R003");
        Assert.False(report.IsValid);
    }

    [Fact]
    public async Task RowsKnowTheirParentAndOrder()
    {
        var sink = new MemorySink();
        await Auditfile.ReadAsync(TestFiles.Source(TestFiles.Sample), sink);

        var transaction = sink.Rows("transaction")[0];
        var line = sink.Rows("tr_line")[0];

        Assert.Equal(transaction.Id, line.ParentId);
        Assert.Equal(1, line.Ordinal);
        Assert.True(line.Id > transaction.Id);
    }

    [Fact]
    public async Task SplitFilesReadAsOneAuditfile()
    {
        var cut = TestFiles.SampleText.IndexOf("<transaction>", StringComparison.Ordinal);
        var first = System.Text.Encoding.UTF8.GetBytes(TestFiles.SampleText[..cut]);
        var second = System.Text.Encoding.UTF8.GetBytes("<!-- Vervolgbestand 2 van 2 -->\n" + TestFiles.SampleText[cut..]);

        var whole = new MemorySink();
        var split = new MemorySink();
        await Auditfile.ReadAsync(TestFiles.Source(TestFiles.Sample), whole);
        var report = await Auditfile.ReadAsync(
            AuditfileSource.FromFactories(("split.xaf", () => new MemoryStream(first)), ("split-2.xaf", () => new MemoryStream(second))),
            split);

        Assert.Equal(2, report.Parts.Count);
        Assert.Equal(whole.Report!.RowCounts, report.RowCounts);
        Assert.Equal(2, split.Rows("tr_line")[0].Part);
    }

    [Fact]
    public async Task SingleUseStreamIsValidatedDuringLoadAndAbortedWhenInvalid()
    {
        var invalid = TestFiles.SampleText.Replace("<header>", "<header><vendorExtra>1</vendorExtra>", StringComparison.Ordinal);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(invalid));
        var sink = new MemorySink();

        var report = await Auditfile.ReadAsync(AuditfileSource.FromStream(stream), sink);

        Assert.True(sink.IsAborted);
        Assert.Contains(report.Diagnostics, d => d.Code == "XAF-S005");
        Assert.Contains(report.Diagnostics, d => d.Code == "XAF-D001");
    }

    [Fact]
    public async Task UnverifiedModeSkipsUnknownElementsWithAWarning()
    {
        var path = TestFiles.Write("extra.xaf", TestFiles.SampleText.Replace("<header>", "<header><vendorExtra>1</vendorExtra>", StringComparison.Ordinal));

        var report = await Auditfile.ReadAsync(TestFiles.Source(path), new MemorySink(), new ReadOptions { Mode = ReadMode.Unverified });

        Assert.True(report.Loaded);
        Assert.Contains(report.Diagnostics, d => d.Code == "XAF-S003" && d.Path == "auditfile/header/vendorExtra");
    }

    [Fact]
    public void ScanCountsRowsAndTheSchemaSurvivesJson()
    {
        var report = Auditfile.Scan(TestFiles.Source(TestFiles.Sample));
        var json = report.Schema!.ToJson();
        var back = AuditfileSchema.FromJson(json);

        Assert.True(report.Schema.HasRowCounts);
        Assert.Equal(7, report.Schema.Table("tr_line").RowCount);
        Assert.Equal(json, back.ToJson());
        Assert.NotNull(back.Table("tr_line").FindColumn("amnt")!.Definition);
    }
}
