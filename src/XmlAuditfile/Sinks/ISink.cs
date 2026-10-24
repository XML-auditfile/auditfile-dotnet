namespace XmlAuditfile;

/// <summary>What a sink needs from the reader.</summary>
[Flags]
public enum SinkCapabilities
{
    /// <summary>Nothing special.</summary>
    None = 0,

    /// <summary>The sink wants row counts before the first row (e.g. to pre-allocate); the reader scans first.</summary>
    NeedsRowCounts = 1,
}

/// <summary>
/// Receives an import: first the schema, then rows per table in batches, then completion or abort.
/// All validation, merging and reporting happens before the sink, so a custom sink gets the same result as a built-in one.
/// </summary>
public interface ISink
{
    /// <summary>What the sink needs.</summary>
    SinkCapabilities Capabilities { get; }

    /// <summary>Start of an import. The schema contains row counts when the reader scanned first.</summary>
    Task BeginAsync(ImportContext context, CancellationToken cancellationToken);

    /// <summary>A batch of rows of one table. Child rows can arrive before their parent row; ids are already assigned.</summary>
    Task WriteAsync(SchemaTable table, IReadOnlyList<AuditfileRow> rows, CancellationToken cancellationToken);

    /// <summary>The import is complete; commit.</summary>
    Task CompleteAsync(AuditfileReport report, CancellationToken cancellationToken);

    /// <summary>The import failed; discard everything received since <see cref="BeginAsync"/>.</summary>
    Task AbortAsync(AuditfileReport report, CancellationToken cancellationToken);
}

/// <summary>Information at the start of an import.</summary>
public sealed class ImportContext
{
    internal ImportContext(AuditfileSchema schema, IReadOnlyList<SourcePart> parts)
    {
        Schema = schema;
        Parts = parts;
    }

    /// <summary>The import schema.</summary>
    public AuditfileSchema Schema { get; }

    /// <summary>The source parts.</summary>
    public IReadOnlyList<SourcePart> Parts { get; }
}
