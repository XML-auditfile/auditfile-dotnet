using XmlAuditfile.Internal;
using XmlAuditfile.Reading;

namespace XmlAuditfile;

/// <summary>
/// Entry point. Read an audit file into a sink, validate it, scan it, or stream its rows yourself.
/// </summary>
/// <example>
/// <code>
/// // MemorySink comes from the XmlAuditfile.Memory package; any ISink works.
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
        if (!session.Prepare(sink.Capabilities.HasFlag(SinkCapabilities.NeedsRowCounts), cancellationToken: cancellationToken))
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
            foreach (var row in session.Load(cancellationToken))
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
        catch (Exception ex)
        {
            session.ReleasePending();
            try
            {
                await sink.AbortAsync(session.CreateReport(loaded: false), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception abortError)
            {
                // A failing rollback must not hide why the import failed: the caller gets both.
                throw new AggregateException("The import failed, and aborting the sink failed too.", ex, abortError);
            }

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

    /// <summary>
    /// Validates an audit file (structure, keys, business rules) without storing anything.
    /// In <see cref="ReadMode.Verified"/> (the default) the XSD is checked first and the rest only for a valid file;
    /// <see cref="ReadMode.Unverified"/> skips the XSD and checks everything else, which is about three times faster
    /// but reports fewer deviations (e.g. XAF-S004 instead of XAF-D001 for an invalid value).
    /// </summary>
    public static Task<AuditfileReport> ValidateAsync(AuditfileSource source, ReadOptions? options = null, CancellationToken cancellationToken = default) =>
        ReadAsync(source, NullSink.Instance, options, cancellationToken);

    /// <summary>
    /// The scan phase only: determines the version, validates against the XSD in verified mode,
    /// and counts rows per table. The report's schema contains the row counts.
    /// </summary>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static AuditfileReport Scan(AuditfileSource source, ReadOptions? options = null, CancellationToken cancellationToken = default)
    {
        var session = new ReadSession(Guard.NotNull(source, nameof(source)), options ?? ReadOptions.Default);
        session.Prepare(sinkNeedsRowCounts: true, scanOnly: true, cancellationToken);
        return session.CreateReport(loaded: false);
    }

    /// <summary>
    /// Opens an audit file for reading rows yourself, without a sink. In verified mode this runs the scan phase,
    /// which the token can cancel.
    /// </summary>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static AuditfileReader Open(AuditfileSource source, ReadOptions? options = null, CancellationToken cancellationToken = default) =>
        new(new ReadSession(Guard.NotNull(source, nameof(source)), options ?? ReadOptions.Default), cancellationToken);
}

/// <summary>Reads rows one by one. Check <see cref="Schema"/> for null before reading: null means the file cannot be read.</summary>
public sealed class AuditfileReader
{
    private readonly ReadSession _session;
    private readonly bool _ready;
    private AuditfileReport? _report;

    internal AuditfileReader(ReadSession session, CancellationToken cancellationToken)
    {
        _session = session;
        _ready = session.Prepare(sinkNeedsRowCounts: false, cancellationToken: cancellationToken);
    }

    /// <summary>The import schema, or null when the file cannot be read (see <see cref="Report"/>).</summary>
    public AuditfileSchema? Schema => _ready ? _session.Schema : null;

    /// <summary>
    /// Rows in document order; a row is returned when its element closes. The rows are read while you enumerate,
    /// and can be enumerated only once: materialize them (e.g. with ToList) when you need them twice.
    /// </summary>
    /// <exception cref="InvalidOperationException">The rows are enumerated a second time.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public IEnumerable<AuditfileRow> ReadRows(CancellationToken cancellationToken = default) =>
        _ready ? _session.Load(cancellationToken) : Enumerable.Empty<AuditfileRow>();

    /// <summary>
    /// The report; complete after <see cref="ReadRows"/> has been enumerated. Until then every call gives the report so far;
    /// the complete report is made once and kept.
    /// </summary>
    public AuditfileReport Report
    {
        get
        {
            if (_report is not null)
            {
                return _report;
            }

            var report = _session.CreateReport(loaded: false);
            if (!_ready || _session.Loaded)
            {
                _report = report;
            }

            return report;
        }
    }
}
