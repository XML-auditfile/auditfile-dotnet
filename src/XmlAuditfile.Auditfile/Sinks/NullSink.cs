namespace XmlAuditfile.Auditfile;

/// <summary>Discards everything; used for validation.</summary>
internal sealed class NullSink : ISink
{
    public static readonly NullSink Instance = new();

    public SinkCapabilities Capabilities => SinkCapabilities.None;

    public Task BeginAsync(ImportContext context, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task WriteAsync(SchemaTable table, IReadOnlyList<AuditfileRow> rows, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task CompleteAsync(AuditfileReport report, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task AbortAsync(AuditfileReport report, CancellationToken cancellationToken) => Task.CompletedTask;
}
