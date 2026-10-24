using System.Text;
using XmlAuditfile.Memory;
using Xunit;

namespace XmlAuditfile.Tests;

/// <summary>The promises of the public API: a truthful report, checked options, cancellation, and no silent surprises.</summary>
public class ApiContractTests
{
    private static readonly ReadOptions Unverified = new() { Mode = ReadMode.Unverified };

    private static AuditfileSource FromText(string text, string name = "test.xaf") =>
        AuditfileSource.FromFactory(() => new MemoryStream(Encoding.UTF8.GetBytes(text)), name);

    /// <summary>The sample with unknown elements in the header, each reported once as a warning (XAF-S003).</summary>
    private static string WithUnknownElements(int count) =>
        TestFiles.SampleText.Replace(
            "<header>", "<header>" + string.Concat(Enumerable.Range(0, count).Select(i => $"<unknown{i}/>")), StringComparison.Ordinal);

    [Fact]
    public async Task ErrorsAreCountedAndKeptWhenTheListIsFullOfWarnings()
    {
        var options = new ReadOptions { Mode = ReadMode.Unverified, MaxDiagnostics = 5 };

        var report = await Auditfile.ValidateAsync(FromText(WithUnknownElements(20)), options, TestContext.Current.CancellationToken);

        Assert.False(report.IsValid);
        Assert.True(report.HasErrors);
        Assert.True(report.IsTruncated);
        Assert.Equal(2, report.Count(DiagnosticSeverity.Error));
        Assert.Equal(20, report.Count(DiagnosticSeverity.Warning));
        Assert.Contains(report.Diagnostics, d => d.Code == "XAF-R001");
        Assert.Contains(report.Diagnostics, d => d.Code == "XAF-R003");
        Assert.Equal("XAF-X900", report.Diagnostics[^1].Code);
        Assert.Equal(6, report.Diagnostics.Count);
    }

    [Fact]
    public async Task CountsIncludeDiagnosticsLeftOutOfTheList()
    {
        var options = new ReadOptions { Mode = ReadMode.Unverified, MaxDiagnostics = 3 };

        var report = await Auditfile.ValidateAsync(FromText(WithUnknownElements(10)), options, TestContext.Current.CancellationToken);

        Assert.Equal(10, report.Count(DiagnosticSeverity.Warning));
        Assert.Equal(2, report.Count(DiagnosticSeverity.Error));
    }

    [Fact]
    public void OptionsOutOfRangeAreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReadOptions { MaxDiagnostics = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReadOptions { MaxDiagnostics = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReadOptions { BatchSize = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReadOptions { BatchSize = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReadOptions { MaxDepth = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReadOptions { MaxValueLength = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReadOptions { MaxCharactersInDocument = -1 });
        _ = new ReadOptions { MaxCharactersInDocument = 0, BatchSize = 1, MaxDiagnostics = 1, MaxDepth = 1, MaxValueLength = 1 };
    }

    [Fact]
    public async Task ValidateRespectsUnverifiedMode()
    {
        var invalid = TestFiles.SampleText.Replace("<amnt>327.00</amnt>", "<amnt>abc</amnt>", StringComparison.Ordinal);

        var verified = await Auditfile.ValidateAsync(FromText(invalid), null, TestContext.Current.CancellationToken);
        var unverified = await Auditfile.ValidateAsync(FromText(invalid), Unverified, TestContext.Current.CancellationToken);

        Assert.Contains(verified.Diagnostics, d => d.Code == "XAF-D001");
        Assert.DoesNotContain(unverified.Diagnostics, d => d.Code == "XAF-D001");
        Assert.Contains(unverified.Diagnostics, d => d.Code == "XAF-S004");
    }

    [Fact]
    public void RowsCanBeEnumeratedOnlyOnce()
    {
        var reader = Auditfile.Open(TestFiles.Source(TestFiles.Sample), Unverified, TestContext.Current.CancellationToken);
        var rows = reader.ReadRows(TestContext.Current.CancellationToken);

        var count = rows.Count();

        Assert.True(count > 0);
        Assert.Throws<InvalidOperationException>(() => rows.ToList());
        Assert.Throws<InvalidOperationException>(() => reader.ReadRows(TestContext.Current.CancellationToken).ToList());
        Assert.Equal(1, reader.Report.RowCounts["header"]);
    }

    [Fact]
    public async Task CancellationStopsTheScanAndTheLoad()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var source = TestFiles.Source(TestFiles.Sample);

        Assert.ThrowsAny<OperationCanceledException>(() => Auditfile.Scan(source, null, cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Auditfile.ValidateAsync(source, null, cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Auditfile.ValidateAsync(source, Unverified, cancelled.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => Auditfile.Open(source, null, cancelled.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => Auditfile.Open(source, Unverified, TestContext.Current.CancellationToken).ReadRows(cancelled.Token).ToList());
    }

    [Fact]
    public async Task AFailingAbortDoesNotHideTheOriginalError()
    {
        var sink = new FailingSink();

        var error = await Assert.ThrowsAsync<AggregateException>(() =>
            Auditfile.ReadAsync(TestFiles.Source(TestFiles.Sample), sink, new ReadOptions { BatchSize = 1 }, TestContext.Current.CancellationToken));

        Assert.Collection(
            error.InnerExceptions,
            e => Assert.Equal("database gone", e.Message),
            e => Assert.Equal("rollback failed", e.Message));
    }

    [Fact]
    public async Task AnAbortThatSucceedsRethrowsTheOriginalError()
    {
        var sink = new FailingSink { AbortFails = false };

        var error = await Assert.ThrowsAsync<TimeoutException>(() =>
            Auditfile.ReadAsync(TestFiles.Source(TestFiles.Sample), sink, new ReadOptions { BatchSize = 1 }, TestContext.Current.CancellationToken));

        Assert.Equal("database gone", error.Message);
    }

    [Fact]
    public async Task ACustomXsdFactoryIsCalledOnce()
    {
        var calls = 0;
        var options = new ReadOptions { Xsd = () => { calls++; return AuditfileVersion.Parse("xaf/3.2").OpenXsd(); } };

        var report = await Auditfile.ValidateAsync(TestFiles.Source(TestFiles.Sample), options, TestContext.Current.CancellationToken);

        Assert.Equal(1, calls);
        Assert.DoesNotContain(report.Diagnostics, d => d.Code is "XAF-D001" or "XAF-D002" or "XAF-X002");
    }

    [Fact]
    public async Task AMissingRequiredFieldIsReportedWithoutTheXsd()
    {
        var text = TestFiles.SampleText;
        var at = text.IndexOf("<amnt>327.00</amnt>", text.IndexOf("<trLine>", StringComparison.Ordinal), StringComparison.Ordinal);
        var missing = text.Remove(at, "<amnt>327.00</amnt>".Length);

        var report = await Auditfile.ValidateAsync(FromText(missing), Unverified, TestContext.Current.CancellationToken);

        var diagnostic = Assert.Single(report.Diagnostics, d => d.Code == "XAF-S006");
        Assert.Contains("amnt", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("tr_line", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFileWithAllRequiredFieldsHasNoMissingFields()
    {
        var report = await Auditfile.ValidateAsync(TestFiles.Source(TestFiles.Sample), Unverified, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(report.Diagnostics, d => d.Code == "XAF-S006");
    }

    [Fact]
    public async Task PartsInDifferentEncodingsAreRefused()
    {
        var (first, second) = Split(TestFiles.SampleText.Replace("Factuur 2025-001", "Factuur café", StringComparison.Ordinal));
        var latin1 = Encoding.Latin1.GetBytes(first.Replace("encoding=\"UTF-8\"", "encoding=\"ISO-8859-1\"", StringComparison.Ordinal));
        var utf8 = Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + second);

        var report = await Auditfile.ReadAsync(Parts(latin1, utf8), new MemorySink(), Unverified, TestContext.Current.CancellationToken);

        var diagnostic = Assert.Single(report.Diagnostics, d => d.Code == "XAF-X004");
        Assert.Equal(DiagnosticSeverity.Fatal, diagnostic.Severity);
        Assert.Equal(2, diagnostic.Part);
        Assert.Contains("UTF-8 (part 1: ISO-8859-1)", diagnostic.Message, StringComparison.Ordinal);
        Assert.False(report.Loaded);
    }

    [Theory]
    [InlineData("ISO-8859-1")]
    [InlineData("UTF-16")]
    public async Task PartsInTheSameEncodingAreReadCorrectly(string name)
    {
        var encoding = name == "UTF-16" ? new UnicodeEncoding(bigEndian: false, byteOrderMark: true) : Encoding.Latin1;
        var (first, second) = Split(TestFiles.SampleText
            .Replace("Factuur 2025-001", "Factuur café", StringComparison.Ordinal)
            .Replace("encoding=\"UTF-8\"", $"encoding=\"{name}\"", StringComparison.Ordinal));
        var sink = new MemorySink();

        var report = await Auditfile.ReadAsync(
            Parts([.. encoding.GetPreamble(), .. encoding.GetBytes(first)], [.. encoding.GetPreamble(), .. encoding.GetBytes($"<?xml version=\"1.0\" encoding=\"{name}\"?>" + second)]),
            sink, Unverified, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(report.Diagnostics, d => d.Severity == DiagnosticSeverity.Fatal);
        Assert.Contains(sink.Rows("tr_line"), r => r["desc"] is "Factuur café");
    }

    [Fact]
    public void ASchemaInAnUnknownFormatIsRefused()
    {
        var json = Auditfile.Scan(TestFiles.Source(TestFiles.Sample), null, TestContext.Current.CancellationToken).Schema!.ToJson();

        Assert.Equal(json, AuditfileSchema.FromJson(json).ToJson());
        Assert.Throws<FormatException>(() => AuditfileSchema.FromJson(json.Replace("\"formatVersion\":1", "\"formatVersion\":2", StringComparison.Ordinal)));
        Assert.Throws<FormatException>(() => AuditfileSchema.FromJson(json.Replace("\"formatVersion\":1,", "", StringComparison.Ordinal)));
        Assert.Throws<FormatException>(() => AuditfileSchema.FromJson(json.Replace("\"type\":\"decimal\"", "\"type\":\"money\"", StringComparison.Ordinal)));
        Assert.Throws<FormatException>(() => AuditfileSchema.FromJson(json.Replace("\"source\":\"official\"", "\"source\":\"guessed\"", StringComparison.Ordinal)));
    }

    /// <summary>The sample cut before the first transaction, without the XML declaration of the first half.</summary>
    private static (string First, string Second) Split(string text)
    {
        var cut = text.IndexOf("<transaction>", StringComparison.Ordinal);
        return (text[..cut], text[cut..]);
    }

    private static AuditfileSource Parts(byte[] first, byte[] second) =>
        AuditfileSource.FromFactories(("part-1.xaf", () => new MemoryStream(first)), ("part-2.xaf", () => new MemoryStream(second)));

    private sealed class FailingSink : ISink
    {
        public bool AbortFails { get; init; } = true;

        public SinkCapabilities Capabilities => SinkCapabilities.None;

        public Task BeginAsync(ImportContext context, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WriteAsync(SchemaTable table, IReadOnlyList<AuditfileRow> rows, CancellationToken cancellationToken) =>
            throw new TimeoutException("database gone");

        public Task CompleteAsync(AuditfileReport report, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AbortAsync(AuditfileReport report, CancellationToken cancellationToken) =>
            AbortFails ? throw new InvalidOperationException("rollback failed") : Task.CompletedTask;
    }
}
