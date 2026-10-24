using System.Text;
using XmlAuditfile.Auditfile.Memory;
using Xunit;

namespace XmlAuditfile.Auditfile.Tests;

/// <summary>Bad input never throws: every case ends in a report with a fatal diagnostic.</summary>
public class InputErrorTests
{
    [Fact]
    public async Task RootElementAfterTheFirstMegabyteIsReported()
    {
        var root = TestFiles.SampleText[TestFiles.SampleText.IndexOf("<auditfile", StringComparison.Ordinal)..];
        var bytes = Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><!--" + new string('x', 1_100_000) + "-->" + root);
        var source = AuditfileSource.FromFactory(() => new MemoryStream(bytes), "padded.xaf");

        var scan = Auditfile.Scan(source);
        var read = await Auditfile.ReadAsync(source, new MemorySink(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(scan.Diagnostics, d => d.Code == "XAF-X002" && d.Severity == DiagnosticSeverity.Fatal);
        Assert.Contains(read.Diagnostics, d => d.Code == "XAF-X002");
        Assert.False(read.Loaded);
    }

    [Theory]
    [InlineData("<xsd:schema")]
    [InlineData("<xsd:schema xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"><xsd:element name=\"a\" type=\"xsd:unknown\"/></xsd:schema>")]
    public async Task CustomXsdThatDoesNotCompileIsReported(string xsd)
    {
        var sink = new MemorySink();
        var options = new ReadOptions { Xsd = () => new MemoryStream(Encoding.UTF8.GetBytes(xsd)) };

        var report = await Auditfile.ReadAsync(TestFiles.Source(TestFiles.Sample), sink, options, TestContext.Current.CancellationToken);

        Assert.Contains(report.Diagnostics, d => d.Code == "XAF-X002" && d.Message.Contains("XSD", StringComparison.Ordinal));
        Assert.False(report.Loaded);
    }

    [Fact]
    public void FailureWhileScanningIsReported()
    {
        var report = Auditfile.Scan(Failing(afterBytes: 2000));

        Assert.Contains(report.Diagnostics, d => d.Code == "XAF-X002" && d.Message.Contains("failing.xaf", StringComparison.Ordinal));
        Assert.Null(report.Schema);
    }

    [Fact]
    public async Task FailureWhileLoadingIsReportedAndTheSinkAborts()
    {
        var sink = new MemorySink();

        var report = await Auditfile.ReadAsync(Failing(afterBytes: 2000), sink, new ReadOptions { Mode = ReadMode.Unverified }, TestContext.Current.CancellationToken);

        Assert.Contains(report.Diagnostics, d => d.Code == "XAF-X002");
        Assert.False(report.Loaded);
        Assert.True(sink.IsAborted);
    }

    [Fact]
    public void FactoryThatIsRefusedAccessIsReported()
    {
        var report = Auditfile.Scan(AuditfileSource.FromFactory(() => throw new UnauthorizedAccessException("Access denied."), "locked.xaf"));

        Assert.Contains(report.Diagnostics, d => d.Code == "XAF-X002" && d.Message.Contains("Access denied.", StringComparison.Ordinal));
    }

    private static AuditfileSource Failing(int afterBytes)
    {
        var bytes = Encoding.UTF8.GetBytes(TestFiles.SampleText);
        return AuditfileSource.FromFactory(() => new FailingStream(bytes, afterBytes), "failing.xaf");
    }

    /// <summary>Hands out the first bytes of a file, then fails like a broken network share.</summary>
    private sealed class FailingStream(byte[] bytes, int failAfter) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= failAfter)
            {
                throw new IOException("The network path is no longer available.");
            }

            var read = Math.Min(Math.Min(count, failAfter - _position), bytes.Length - _position);
            Array.Copy(bytes, _position, buffer, offset, read);
            _position += read;
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
