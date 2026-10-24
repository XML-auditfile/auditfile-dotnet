namespace XmlAuditfile;

/// <summary>Outcome of a scan, validation or read.</summary>
public sealed class AuditfileReport
{
    private readonly IReadOnlyList<int> _counts;

    internal AuditfileReport(
        AuditfileSchema? schema, IReadOnlyList<PartInfo> parts, IReadOnlyList<Diagnostic> diagnostics, IReadOnlyList<int> counts, bool truncated,
        IReadOnlyDictionary<string, long> rowCounts, bool loaded, TimeSpan duration)
    {
        Schema = schema;
        Parts = parts;
        Diagnostics = diagnostics;
        _counts = counts;
        IsTruncated = truncated;
        RowCounts = rowCounts;
        Loaded = loaded;
        Duration = duration;
    }

    /// <summary>The import schema, or null when the version could not be determined.</summary>
    public AuditfileSchema? Schema { get; }

    /// <summary>Source parts with size and SHA-256 (empty when a part was not read completely).</summary>
    public IReadOnlyList<PartInfo> Parts { get; }

    /// <summary>
    /// The diagnostics, in the order they were found, at most <see cref="ReadOptions.MaxDiagnostics"/> plus XAF-X900 when more were found.
    /// A full list makes room for errors by leaving out infos and warnings; fatal diagnostics are always included.
    /// </summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>True when diagnostics were left out of <see cref="Diagnostics"/>; <see cref="Count"/> still counts them.</summary>
    public bool IsTruncated { get; }

    /// <summary>Rows per table.</summary>
    public IReadOnlyDictionary<string, long> RowCounts { get; }

    /// <summary>True when rows were handed to the sink and completed.</summary>
    public bool Loaded { get; }

    /// <summary>Time taken.</summary>
    public TimeSpan Duration { get; }

    /// <summary>True when a fatal diagnostic was found: reading stopped.</summary>
    public bool HasFatal => Count(DiagnosticSeverity.Fatal) > 0;

    /// <summary>True when an error or fatal diagnostic was found, also when it is not in a truncated <see cref="Diagnostics"/>.</summary>
    public bool HasErrors => HasFatal || Count(DiagnosticSeverity.Error) > 0;

    /// <summary>True when there are no errors and no fatal diagnostics.</summary>
    public bool IsValid => !HasErrors;

    /// <summary>Number of diagnostics with the given severity, including the ones left out of a truncated <see cref="Diagnostics"/>.</summary>
    public int Count(DiagnosticSeverity severity) => _counts[(int)severity];
}
