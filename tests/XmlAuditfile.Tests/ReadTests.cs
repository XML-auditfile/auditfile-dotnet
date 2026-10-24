using XmlAuditfile.Memory;
using Xunit;

namespace XmlAuditfile.Tests;

public class ReadTests
{
    [Fact]
    public async Task ReadsTheSampleFile()
    {
        var sink = new MemorySink();
        var report = await Auditfile.ReadAsync(TestFiles.Source(TestFiles.Sample), sink, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(report.Loaded);
        Assert.Equal(VersionSource.Official, report.Schema!.Source);
        Assert.Equal(27, report.RowCounts.Count);
        Assert.Equal(327.00m, sink.Rows("tr_line")[0]["amnt"]);
        Assert.Equal(new DateTime(2025, 1, 15), sink.Rows("tr_line")[0]["eff_date"]);
    }

    [Fact]
    public async Task BusinessRulesFindTheUnbalancedTransactionInTheSampleFile()
    {
        var report = await Auditfile.ValidateAsync(TestFiles.Source(TestFiles.Sample), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(report.Diagnostics, d => d.Code == "XAF-R001");
        Assert.Contains(report.Diagnostics, d => d.Code == "XAF-R003");
        Assert.False(report.IsValid);
    }

    [Fact]
    public async Task RowsKnowTheirParentAndOrder()
    {
        var sink = new MemorySink();
        await Auditfile.ReadAsync(TestFiles.Source(TestFiles.Sample), sink, cancellationToken: TestContext.Current.CancellationToken);

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
        await Auditfile.ReadAsync(TestFiles.Source(TestFiles.Sample), whole, cancellationToken: TestContext.Current.CancellationToken);
        var report = await Auditfile.ReadAsync(
            AuditfileSource.FromFactories(("split.xaf", () => new MemoryStream(first)), ("split-2.xaf", () => new MemoryStream(second))),
            split, cancellationToken: TestContext.Current.CancellationToken);

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

        var report = await Auditfile.ReadAsync(AuditfileSource.FromStream(stream), sink, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(sink.IsAborted);
        Assert.Contains(report.Diagnostics, d => d.Code == "XAF-S005");
        Assert.Contains(report.Diagnostics, d => d.Code == "XAF-D001");
    }

    [Fact]
    public async Task UnverifiedModeSkipsUnknownElementsWithAWarning()
    {
        var path = TestFiles.Write("extra.xaf", TestFiles.SampleText.Replace("<header>", "<header><vendorExtra>1</vendorExtra>", StringComparison.Ordinal));

        var report = await Auditfile.ReadAsync(TestFiles.Source(path), new MemorySink(), new ReadOptions { Mode = ReadMode.Unverified }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(report.Loaded);
        Assert.Contains(report.Diagnostics, d => d.Code == "XAF-S003" && d.Path == "auditfile/header/vendorExtra");
    }

    [Fact]
    public void ScanCountsRowsAndTheSchemaSurvivesJson()
    {
        var report = Auditfile.Scan(TestFiles.Source(TestFiles.Sample), null, TestContext.Current.CancellationToken);
        var json = report.Schema!.ToJson();
        var back = AuditfileSchema.FromJson(json);

        Assert.True(report.Schema.HasRowCounts);
        Assert.Equal(7, report.Schema.Table("tr_line").RowCount);
        Assert.Equal(json, back.ToJson());
        Assert.NotNull(back.Table("tr_line").FindColumn("amnt")!.Definition);
    }

    [Theory]
    [InlineData("\n", 4096)]
    [InlineData("\r\n", 4096)]
    [InlineData("\r", 4096)]
    [InlineData("\r\n", 1)]
    public void LinesAreCountedForEveryLineEnding(string newline, int chunk)
    {
        var text = TestFiles.SampleText.Replace("\n", newline, StringComparison.Ordinal);
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        var source = AuditfileSource.FromFactory(() => new ChunkedStream(bytes, chunk), "lines.xaf");

        var report = Auditfile.Scan(source, null, TestContext.Current.CancellationToken);

        Assert.Equal(TestFiles.SampleText.Count(c => c == '\n') + 1, report.Parts[0].Lines);
    }

    /// <summary>Hands out at most a fixed number of bytes per read, so a CRLF can be split over two reads.</summary>
    private sealed class ChunkedStream(byte[] data, int chunk) : MemoryStream(data)
    {
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, chunk));
    }
}
