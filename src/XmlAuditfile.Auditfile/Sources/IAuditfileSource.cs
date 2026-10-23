namespace XmlAuditfile.Auditfile;

/// <summary>
/// A stored import, e.g. in a database. Gives back the schema and the rows per table,
/// so code can work with an import regardless of where it is stored. Implemented by connectors.
/// </summary>
public interface IAuditfileSource
{
    /// <summary>The schema of the stored import.</summary>
    Task<AuditfileSchema> GetSchemaAsync(CancellationToken cancellationToken);

    /// <summary>Reads the rows of one table in batches, in id order.</summary>
    Task ReadRowsAsync(string table, Func<IReadOnlyList<AuditfileRow>, Task> onBatch, CancellationToken cancellationToken);
}
