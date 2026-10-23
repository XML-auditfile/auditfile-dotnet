using XmlAuditfile.Auditfile.Internal;
using XmlAuditfile.Auditfile.Reading;

namespace XmlAuditfile.Auditfile;

/// <summary>
/// Entry point. Read an audit file into a sink, validate it, scan it, or stream its rows yourself.
/// </summary>
/// <example>
/// <code>
/// // MemorySink comes from the XmlAuditfile.Auditfile.Memory package; any ISink works.
/// var sink = new MemorySink();
/// var report = await Auditfile.ReadAsync(AuditfileSource.FromFactory(() => File.OpenRead("admin.xaf"), "admin.xaf"), sink);
/// </code>
/// </example>
public static class Auditfile
{
    /// <summary>
    /// Reads an audit file into a sink. Verified mode (default) validates against the XSD first and loads only a valid file.
    /// </summary>
    public static async Task<AuditfileReport> ReadAsync(AuditfileSource source, ISink sink, ReadOptions? options = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(source, nameof(source));
        Guard.NotNull(sink, nameof(sink));
        options ??= ReadOptions.Default;

        var session = new ReadSession(source, options);
        if (!session.Prepare(sink.Capabilities.HasFlag(SinkCapabilities.NeedsRowCounts)))
        {
            return session.CreateReport(loaded: false);
        }

        var schema = session.Schema!;
        var batches = new Dictionary<SchemaTable, List<AuditfileRow>>();
        var total = schema.HasRowCounts ? schema.Tables.Sum(t => t.RowCount ?? 0) : (long?)null;
        long rows = 0;

        await sink.BeginAsync(new ImportContext(schema, session.Parts), cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var row in session.Load())
            {
                if (!batches.TryGetValue(row.Table, out var batch))
                {
                    batch = new List<AuditfileRow>(options.BatchSize);
                    batches[row.Table] = batch;
                }

                batch.Add(row);
                rows++;
                if (batch.Count >= options.BatchSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await sink.WriteAsync(row.Table, batch, cancellationToken).ConfigureAwait(false);
                    batches[row.Table] = new List<AuditfileRow>(options.BatchSize);
                    options.Progress?.Report(new ReadProgress(rows, total));
                }
            }

            foreach (var pair in batches.Where(p => p.Value.Count > 0).OrderBy(p => p.Key.Index))
            {
                await sink.WriteAsync(pair.Key, pair.Value, cancellationToken).ConfigureAwait(false);
            }

            options.Progress?.Report(new ReadProgress(rows, total));
        }
        catch
        {
            await sink.AbortAsync(session.CreateReport(loaded: false), CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        var failed = session.Diagnostics.HasFatal || (session.ValidateDuringLoad && session.Diagnostics.HasErrors);
        var report = session.CreateReport(loaded: !failed);
        if (failed)
        {
            await sink.AbortAsync(report, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await sink.CompleteAsync(report, cancellationToken).ConfigureAwait(false);
        }

        return report;
    }

    /// <summary>Validates an audit file completely (XSD, structure, keys, business rules) without storing anything.</summary>
    public static Task<AuditfileReport> ValidateAsync(AuditfileSource source, ReadOptions? options = null, CancellationToken cancellationToken = default) =>
        ReadAsync(source, NullSink.Instance, (options ?? ReadOptions.Default).WithMode(ReadMode.Verified), cancellationToken);

    /// <summary>
    /// The scan phase only: determines the version, validates against the XSD in verified mode,
    /// and counts rows per table. The report's schema contains the row counts.
    /// </summary>
    public static AuditfileReport Scan(AuditfileSource source, ReadOptions? options = null)
    {
        var session = new ReadSession(Guard.NotNull(source, nameof(source)), options ?? ReadOptions.Default);
        session.Prepare(sinkNeedsRowCounts: true, scanOnly: true);
        return session.CreateReport(loaded: false);
    }

    /// <summary>Opens an audit file for reading rows yourself, without a sink.</summary>
    public static AuditfileReader Open(AuditfileSource source, ReadOptions? options = null) =>
        new(new ReadSession(Guard.NotNull(source, nameof(source)), options ?? ReadOptions.Default));
}

/// <summary>Reads rows one by one. Check <see cref="Schema"/> for null before reading: null means the file cannot be read.</summary>
public sealed class AuditfileReader
{
    private readonly ReadSession _session;
    private readonly bool _ready;

    internal AuditfileReader(ReadSession session)
    {
        _session = session;
        _ready = session.Prepare(sinkNeedsRowCounts: false);
    }

    /// <summary>The import schema, or null when the file cannot be read (see <see cref="Report"/>).</summary>
    public AuditfileSchema? Schema => _ready ? _session.Schema : null;

    /// <summary>Rows in document order; a row is returned when its element closes.</summary>
    public IEnumerable<AuditfileRow> ReadRows() => _ready ? _session.Load() : Enumerable.Empty<AuditfileRow>();

    /// <summary>The report; complete after <see cref="ReadRows"/> has been enumerated.</summary>
    public AuditfileReport Report => _session.CreateReport(loaded: false);
}
