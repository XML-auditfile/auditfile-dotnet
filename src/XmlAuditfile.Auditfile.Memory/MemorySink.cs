using XmlAuditfile.Auditfile.Definitions;

namespace XmlAuditfile.Auditfile.Memory;

/// <summary>Keeps all rows in memory, per table. Useful for tests, small files and the browser.</summary>
public sealed class MemorySink : ISink
{
    private readonly Dictionary<string, List<AuditfileRow>> _tables = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public SinkCapabilities Capabilities => SinkCapabilities.None;

    /// <summary>The import schema, after <see cref="BeginAsync"/>.</summary>
    public AuditfileSchema? Schema { get; private set; }

    /// <summary>The report, after completion or abort.</summary>
    public AuditfileReport? Report { get; private set; }

    /// <summary>True when the import was aborted; no rows are kept then.</summary>
    public bool IsAborted { get; private set; }

    /// <summary>Rows of a table; empty when the table has no rows.</summary>
    public IReadOnlyList<AuditfileRow> Rows(string table) =>
        _tables.TryGetValue(table, out var rows) ? rows : (IReadOnlyList<AuditfileRow>)Array.Empty<AuditfileRow>();

    /// <summary>Runs a view, e.g. <c>sink.Run(sink.Schema!.Version!.FindView("trial_balance")!)</c>, over the rows in memory.</summary>
    /// <exception cref="InvalidOperationException">Nothing has been read yet.</exception>
    public ViewResult Run(ViewDefinition view, IReadOnlyDictionary<string, object?>? parameters = null, TextLanguage language = TextLanguage.Dutch) =>
        view.Run(Schema ?? throw new InvalidOperationException("Read an audit file into this sink first."), Rows, parameters, language);

    /// <inheritdoc />
    public Task BeginAsync(ImportContext context, CancellationToken cancellationToken)
    {
        _tables.Clear();
        Schema = context.Schema;
        IsAborted = false;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task WriteAsync(SchemaTable table, IReadOnlyList<AuditfileRow> rows, CancellationToken cancellationToken)
    {
        if (!_tables.TryGetValue(table.Name, out var list))
        {
            list = new List<AuditfileRow>();
            _tables[table.Name] = list;
        }

        list.AddRange(rows);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CompleteAsync(AuditfileReport report, CancellationToken cancellationToken)
    {
        Report = report;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task AbortAsync(AuditfileReport report, CancellationToken cancellationToken)
    {
        _tables.Clear();
        Report = report;
        IsAborted = true;
        return Task.CompletedTask;
    }
}
