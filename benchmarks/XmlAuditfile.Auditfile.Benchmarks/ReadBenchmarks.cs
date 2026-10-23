using BenchmarkDotNet.Attributes;

namespace XmlAuditfile.Auditfile.Benchmarks;

/// <summary>Throughput and allocations of reading into a sink that only counts.</summary>
[MemoryDiagnoser]
public class ReadBenchmarks
{
    private byte[] _file = Array.Empty<byte>();

    [Params(10_000, 100_000)]
    public int Transactions { get; set; }

    [GlobalSetup]
    public void Setup() => _file = SyntheticAuditfile.Create(Transactions);

    [Benchmark(Baseline = true)]
    public Task<AuditfileReport> Unverified() => Read(ReadMode.Unverified);

    [Benchmark]
    public Task<AuditfileReport> Verified() => Read(ReadMode.Verified);

    private Task<AuditfileReport> Read(ReadMode mode) =>
        Auditfile.ReadAsync(AuditfileSource.FromFactory(() => new MemoryStream(_file)), new CountingSink(), new ReadOptions { Mode = mode });

    private sealed class CountingSink : ISink
    {
        public long Rows { get; private set; }

        public SinkCapabilities Capabilities => SinkCapabilities.None;

        public Task BeginAsync(ImportContext context, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WriteAsync(SchemaTable table, IReadOnlyList<AuditfileRow> rows, CancellationToken cancellationToken)
        {
            Rows += rows.Count;
            return Task.CompletedTask;
        }

        public Task CompleteAsync(AuditfileReport report, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AbortAsync(AuditfileReport report, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
