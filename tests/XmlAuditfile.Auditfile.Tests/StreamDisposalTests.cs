using System.Text;
using XmlAuditfile.Auditfile.Memory;
using Xunit;

namespace XmlAuditfile.Auditfile.Tests;

/// <summary>Every stream the reader gets from a factory is closed again, also when reading fails.</summary>
public class StreamDisposalTests
{
    public static TheoryData<string> Cases() => new()
    {
        "valid", "unknown namespace", "dtd", "truncated", "root after first megabyte", "invalid custom xsd", "io failure",
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ReadClosesEveryStream(string @case)
    {
        var (source, tracker, options) = Create(@case);

        await Auditfile.ReadAsync(source, new MemorySink(), options, TestContext.Current.CancellationToken);

        Assert.True(tracker.Opened > 0);
        Assert.Equal(tracker.Opened, tracker.Closed);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ReadUnverifiedClosesEveryStream(string @case)
    {
        var (source, tracker, options) = Create(@case);

        await Auditfile.ReadAsync(source, new MemorySink(), options.WithMode(ReadMode.Unverified), TestContext.Current.CancellationToken);

        Assert.Equal(tracker.Opened, tracker.Closed);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ScanClosesEveryStream(string @case)
    {
        var (source, tracker, options) = Create(@case);

        Auditfile.Scan(source, options);

        Assert.Equal(tracker.Opened, tracker.Closed);
    }

    [Fact]
    public async Task AFailingSinkStillClosesTheStream()
    {
        var (source, tracker, options) = Create("valid");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Auditfile.ReadAsync(source, new FailingSink(), options, TestContext.Current.CancellationToken));

        Assert.Equal(tracker.Opened, tracker.Closed);
    }

    [Fact]
    public void StoppingEarlyWithReadRowsClosesTheStream()
    {
        var (source, tracker, _) = Create("valid");
        var reader = Auditfile.Open(source, new ReadOptions { Mode = ReadMode.Unverified });

        foreach (var row in reader.ReadRows())
        {
            break;
        }

        Assert.Equal(tracker.Opened, tracker.Closed);
    }

    private static (AuditfileSource Source, Tracker Tracker, ReadOptions Options) Create(string @case)
    {
        var text = TestFiles.SampleText;
        var root = text[text.IndexOf("<auditfile", StringComparison.Ordinal)..];
        var options = new ReadOptions();
        var failAfter = int.MaxValue;
        var content = @case switch
        {
            "unknown namespace" => text.Replace($"xmlns=\"{TestFiles.OfficialNamespace}\"", "xmlns=\"urn:unknown\"", StringComparison.Ordinal),
            "dtd" => "<?xml version=\"1.0\"?><!DOCTYPE a [<!ENTITY x \"y\">]>" + root,
            "truncated" => text[..(text.Length / 2)],
            "root after first megabyte" => "<?xml version=\"1.0\"?><!--" + new string('x', 1_100_000) + "-->" + root,
            _ => text,
        };

        if (@case == "invalid custom xsd")
        {
            options = new ReadOptions { Xsd = () => new MemoryStream(Encoding.UTF8.GetBytes("<xsd:schema")) };
        }

        if (@case == "io failure")
        {
            failAfter = content.Length / 2;
        }

        var tracker = new Tracker();
        var bytes = Encoding.UTF8.GetBytes(content);
        return (AuditfileSource.FromFactory(() => tracker.Open(bytes, failAfter), "tracked.xaf"), tracker, options);
    }

    private sealed class Tracker
    {
        public int Opened { get; private set; }

        public int Closed { get; set; }

        public TrackedStream Open(byte[] bytes, int failAfter)
        {
            Opened++;
            return new TrackedStream(bytes, failAfter, this);
        }
    }

    private sealed class TrackedStream(byte[] bytes, int failAfter, Tracker tracker) : MemoryStream(bytes, writable: false)
    {
        private bool _closed;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (Position >= failAfter)
            {
                throw new IOException("The network path is no longer available.");
            }

            return base.Read(buffer, offset, (int)Math.Min(count, failAfter - Position));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_closed)
            {
                _closed = true;
                tracker.Closed++;
            }

            base.Dispose(disposing);
        }
    }

    private sealed class FailingSink : ISink
    {
        public SinkCapabilities Capabilities => SinkCapabilities.None;

        public Task BeginAsync(ImportContext context, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WriteAsync(SchemaTable table, IReadOnlyList<AuditfileRow> rows, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The database is down.");

        public Task CompleteAsync(AuditfileReport report, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AbortAsync(AuditfileReport report, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
