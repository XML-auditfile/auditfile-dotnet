namespace XmlAuditfile.Auditfile;

/// <summary>Outcome of a scan, validation or read.</summary>
public sealed class AuditfileReport
{
    internal AuditfileReport(
        AuditfileSchema? schema, IReadOnlyList<PartInfo> parts, IReadOnlyList<Diagnostic> diagnostics,
        IReadOnlyDictionary<string, long> rowCounts, bool loaded, TimeSpan duration)
    {
        Schema = schema;
        Parts = parts;
        Diagnostics = diagnostics;
        RowCounts = rowCounts;
        Loaded = loaded;
        Duration = duration;
    }

    /// <summary>The import schema, or null when the version could not be determined.</summary>
    public AuditfileSchema? Schema { get; }

    /// <summary>Source parts with size and SHA-256 (empty when a part was not read completely).</summary>
    public IReadOnlyList<PartInfo> Parts { get; }

    /// <summary>All diagnostics, in the order they were found.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>Rows per table.</summary>
    public IReadOnlyDictionary<string, long> RowCounts { get; }

    /// <summary>True when rows were handed to the sink and completed.</summary>
    public bool Loaded { get; }

    /// <summary>Time taken.</summary>
    public TimeSpan Duration { get; }

    /// <summary>True when there are no errors and no fatal diagnostics.</summary>
    public bool IsValid => !Diagnostics.Any(d => d.Severity >= DiagnosticSeverity.Error);

    /// <summary>Number of diagnostics with the given severity.</summary>
    public int Count(DiagnosticSeverity severity) => Diagnostics.Count(d => d.Severity == severity);
}
