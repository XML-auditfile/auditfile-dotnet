using XmlAuditfile.Definitions;
using XmlAuditfile.Internal;

namespace XmlAuditfile;

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

    private readonly int _batchSize = 1000;
    private readonly int _maxDiagnostics = 1000;
    private readonly int _maxDepth = 64;
    private readonly long _maxCharacters = 4L * 1024 * 1024 * 1024;
    private readonly int _maxValueLength = 65_536;

    /// <summary>Force a version, e.g. <c>AuditfileVersion.Financial.V3_2</c>. Null: determine from the namespace.</summary>
    public AuditfileVersion? Version { get; init; }

    /// <summary>Verified (default) or unverified.</summary>
    public ReadMode Mode { get; init; } = ReadMode.Verified;

    /// <summary>Derive the version from an unknown namespace, e.g. ".../XAF/3.2/". Off by default.</summary>
    public bool UseResolver { get; init; }

    /// <summary>
    /// Own XSD instead of the built-in one. Null: use the built-in XSD.
    /// Called once per read that validates; the reader closes the stream it returns.
    /// </summary>
    public Func<Stream>? Xsd { get; init; }

    /// <summary>Apply known dialect corrections. On by default.</summary>
    public bool ApplyDialects { get; init; } = true;

    /// <summary>Rows per table handed to the sink at once. At least 1.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int BatchSize { get => _batchSize; init => _batchSize = Guard.AtLeast(value, 1, nameof(BatchSize)); }

    /// <summary>Language of diagnostic messages.</summary>
    public TextLanguage Language { get; init; } = TextLanguage.Dutch;

    /// <summary>
    /// Maximum number of diagnostics in the report; the rest is counted but left out (XAF-X900). At least 1.
    /// Errors take the place of infos and warnings, and fatal diagnostics are always included.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int MaxDiagnostics { get => _maxDiagnostics; init => _maxDiagnostics = Guard.AtLeast(value, 1, nameof(MaxDiagnostics)); }

    /// <summary>Maximum element nesting; deeper files are refused. At least 1.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int MaxDepth { get => _maxDepth; init => _maxDepth = Guard.AtLeast(value, 1, nameof(MaxDepth)); }

    /// <summary>
    /// Maximum number of characters in the whole document, all parts together; a larger file stops with XAF-X001.
    /// Default 4 Gi characters. 0 means no limit.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public long MaxCharactersInDocument { get => _maxCharacters; init => _maxCharacters = Guard.AtLeast(value, 0L, nameof(MaxCharactersInDocument)); }

    /// <summary>
    /// Maximum number of characters of one value. A longer value is skipped with XAF-S008 and never held in memory as a whole,
    /// so one huge element cannot exhaust memory. The longest value an XSD of auditfile-spec allows is 999 characters. At least 1.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int MaxValueLength { get => _maxValueLength; init => _maxValueLength = Guard.AtLeast(value, 1, nameof(MaxValueLength)); }

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
