using XmlAuditfile.Auditfile.Definitions;

namespace XmlAuditfile.Auditfile;

/// <summary>How the reader treats the file.</summary>
public enum ReadMode
{
    /// <summary>Validate against the XSD first; load only a valid file.</summary>
    Verified,

    /// <summary>Load directly with the manifest; elements outside the manifest are reported and skipped.</summary>
    Unverified,
}

/// <summary>Options for reading. The defaults suit most uses.</summary>
public sealed class ReadOptions
{
    /// <summary>Default options.</summary>
    public static readonly ReadOptions Default = new();

    /// <summary>Force a version, e.g. <c>AuditfileVersion.Financial.V3_2</c>. Null: determine from the namespace.</summary>
    public AuditfileVersion? Version { get; init; }

    /// <summary>Verified (default) or unverified.</summary>
    public ReadMode Mode { get; init; } = ReadMode.Verified;

    /// <summary>Derive the version from an unknown namespace, e.g. ".../XAF/3.2/". Off by default.</summary>
    public bool UseResolver { get; init; }

    /// <summary>Own XSD instead of the built-in one. Null: use the built-in XSD.</summary>
    public Func<Stream>? Xsd { get; init; }

    /// <summary>Apply known dialect corrections. On by default.</summary>
    public bool ApplyDialects { get; init; } = true;

    /// <summary>Rows per table handed to the sink at once.</summary>
    public int BatchSize { get; init; } = 1000;

    /// <summary>Language of diagnostic messages.</summary>
    public TextLanguage Language { get; init; } = TextLanguage.Dutch;

    /// <summary>Maximum number of diagnostics; the rest is summarized.</summary>
    public int MaxDiagnostics { get; init; } = 1000;

    /// <summary>Maximum element nesting; deeper files are refused.</summary>
    public int MaxDepth { get; init; } = 64;

    /// <summary>
    /// Maximum number of characters in the whole document, all parts together; a larger file stops with XAF-X001.
    /// Default 4 Gi characters. 0 means no limit.
    /// </summary>
    public long MaxCharactersInDocument { get; init; } = 4L * 1024 * 1024 * 1024;

    /// <summary>
    /// Maximum number of characters of one value. A longer value is skipped with XAF-S008 and never held in memory as a whole,
    /// so one huge element cannot exhaust memory. The longest value an XSD of auditfile-spec allows is 999 characters.
    /// </summary>
    public int MaxValueLength { get; init; } = 65_536;

    /// <summary>Receives progress after every batch.</summary>
    public IProgress<ReadProgress>? Progress { get; init; }

    internal ReadOptions WithMode(ReadMode mode) => new()
    {
        Version = Version,
        Mode = mode,
        UseResolver = UseResolver,
        Xsd = Xsd,
        ApplyDialects = ApplyDialects,
        BatchSize = BatchSize,
        Language = Language,
        MaxDiagnostics = MaxDiagnostics,
        MaxDepth = MaxDepth,
        MaxCharactersInDocument = MaxCharactersInDocument,
        MaxValueLength = MaxValueLength,
        Progress = Progress,
    };
}

/// <summary>Progress of a read.</summary>
public sealed class ReadProgress
{
    internal ReadProgress(long rowsRead, long? totalRows)
    {
        RowsRead = rowsRead;
        TotalRows = totalRows;
    }

    /// <summary>Rows read so far.</summary>
    public long RowsRead { get; }

    /// <summary>Total rows, when known from the scan phase.</summary>
    public long? TotalRows { get; }
}
