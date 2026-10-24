using System.Diagnostics;
using System.Text;
using System.Xml;
using System.Xml.Schema;
using XmlAuditfile.Definitions;
using XmlAuditfile.Diagnostics;
using XmlAuditfile.Internal;
using XmlAuditfile.Validation;
using XmlAuditfile.Versioning;

namespace XmlAuditfile.Reading;

/// <summary>
/// One read of one source: resolve the version, optionally scan (validate and count),
/// then load (stream rows). Holds the diagnostics and the part statistics.
/// </summary>
internal sealed class ReadSession
{
    /// <summary>Nodes read between two checks of the cancellation token.</summary>
    internal const int CancellationInterval = 10_000;

    private readonly AuditfileSource _source;
    private readonly ReadOptions _options;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private long[] _rowCounts = Array.Empty<long>();
    private IReadOnlyList<PartInfo> _parts = Array.Empty<PartInfo>();
    private bool _loaded;

    /// <summary>True when the load phase has read the whole file.</summary>
    public bool Loaded => _loaded;
    private bool _loadStarted;

    // A single-use source is opened once: the stream used for version detection is kept and rewound for loading.
    private ConcatenatedStream? _pendingStream;
    private PeekableStream? _pendingPeek;

    public ReadSession(AuditfileSource source, ReadOptions options)
    {
        _source = source;
        _options = options;
        Diagnostics = new DiagnosticCollector(options.Language, options.MaxDiagnostics);
    }

    public DiagnosticCollector Diagnostics { get; }

    public Resolution? Resolution { get; private set; }

    public AuditfileSchema? Schema { get; private set; }

    /// <summary>True when the load phase must validate against the XSD itself (single-use source).</summary>
    public bool ValidateDuringLoad { get; private set; }

    public IReadOnlyList<SourcePart> Parts => _source.Parts;

    /// <summary>Resolves the version and runs the scan phase when useful. Returns false when loading must not start.</summary>
    public bool Prepare(bool sinkNeedsRowCounts, bool scanOnly = false, CancellationToken cancellationToken = default)
    {
        var verified = _options.Mode == ReadMode.Verified;
        var wantScan = scanOnly || verified || sinkNeedsRowCounts || _source.Parts.Count > 1;

        if (wantScan && _source.CanReadTwice)
        {
            Scan(validate: verified, cancellationToken);
            if (Diagnostics.HasFatal || (verified && Diagnostics.HasErrors))
            {
                return false;
            }

            return !scanOnly;
        }

        if (wantScan)
        {
            Diagnostics.Add("XAF-S005", Location.None);
        }

        ValidateDuringLoad = verified;
        if (!ResolveFromPeek())
        {
            return false;
        }

        Schema = AuditfileSchema.Create(Resolution!.Version, Resolution.FileNamespace, Resolution.Source, Resolution.Dialect?.Name, rowCounts: null);
        if (scanOnly)
        {
            ReleasePending();
        }

        return !scanOnly;
    }

    /// <summary>Releases the streams kept for a load that will not happen (scan only, or the import failed before loading).</summary>
    public void ReleasePending()
    {
        _pendingPeek?.Dispose();
        _pendingPeek = null;
        _pendingStream = null;
    }

    /// <summary>The load phase as a stream of rows. Diagnostics are collected along the way. Can be enumerated once.</summary>
    public IEnumerable<AuditfileRow> Load(CancellationToken cancellationToken = default)
    {
        // A second enumeration would read the file again and add to the same counts and diagnostics.
        if (_loadStarted)
        {
            throw new InvalidOperationException("The rows of an audit file can be enumerated only once; materialize them (e.g. with ToList) to use them twice.");
        }

        _loadStarted = true;
        if (Schema is null)
        {
            yield break;
        }

        _rowCounts = new long[Schema.Tables.Count];

        var concatenated = _pendingStream ?? new ConcatenatedStream(_source.Parts, collectStatistics: _parts.Count == 0);
        using var reader = CreateReader(concatenated, ValidateDuringLoad, _pendingPeek);
        _pendingStream = null;
        _pendingPeek = null;
        if (reader is null)
        {
            yield break;
        }

        var observers = new List<IRowObserver> { new ReferenceChecker(Schema, Diagnostics) };
        if (Resolution!.Version.Type == XafRules.Type)
        {
            observers.Add(new XafRules(Schema, Diagnostics));
        }

        var loader = new RowLoader(reader, Schema, Resolution, _options, Diagnostics, observers, concatenated, _parts);
        foreach (var row in loader.Rows(cancellationToken))
        {
            _rowCounts[row.Table.Index]++;
            yield return row;
        }

        if (!loader.Stopped)
        {
            foreach (var observer in observers)
            {
                observer.OnEnd();
            }

            CheckTables(LoadedRowCounts());
        }

        if (_parts.Count == 0)
        {
            _parts = concatenated.Completed;
        }

        // Without a scan phase a dialect recognized by the software description is only known now; the report names it.
        if (Resolution.Dialect?.Name != Schema.Dialect)
        {
            Schema = AuditfileSchema.Create(Resolution.Version, Resolution.FileNamespace, Resolution.Source, Resolution.Dialect?.Name,
                Schema.HasRowCounts ? RowCountsFromSchema() : null);
        }

        _loaded = true;
    }

    public AuditfileReport CreateReport(bool loaded) =>
        new(Schema, _parts, Diagnostics.Items, [.. Diagnostics.Counts], Diagnostics.IsTruncated, _loaded ? LoadedRowCounts() : RowCountsFromSchema(), loaded, _stopwatch.Elapsed);

    /// <summary>Rows per table read so far, for the tables that have rows.</summary>
    private Dictionary<string, long> LoadedRowCounts() =>
        Schema!.Tables.Where(t => _rowCounts[t.Index] > 0).ToDictionary(t => t.Name, t => _rowCounts[t.Index], StringComparer.Ordinal);

    private Dictionary<string, long> RowCountsFromSchema() =>
        Schema?.Tables.Where(t => t.RowCount > 0).ToDictionary(t => t.Name, t => t.RowCount!.Value, StringComparer.Ordinal)
        ?? new Dictionary<string, long>(StringComparer.Ordinal);

    private void Scan(bool validate, CancellationToken cancellationToken)
    {
        var concatenated = new ConcatenatedStream(_source.Parts, collectStatistics: true);
        using (var reader = CreateReader(concatenated, validate))
        {
            if (reader is null)
            {
                return;
            }

            long[] counts;
            try
            {
                counts = CountRows(reader, concatenated, cancellationToken);
                if (counts.Length == 0)
                {
                    return;
                }
            }
            catch (XmlException ex)
            {
                Diagnostics.Add("XAF-X001", new Location(PartOf(ex.LineNumber, concatenated), ex.LineNumber, ex.LinePosition, null), ("detail", ex.Message));
                return;
            }
            catch (Exception ex) when (IsSourceError(ex))
            {
                ReportSourceError(Diagnostics, ex, concatenated.CurrentPartName ?? _source.Parts[0].Name, null);
                return;
            }

            _parts = concatenated.Completed;
            var byName = Resolution!.Version.Manifest.Tables.ToDictionary(t => t.Name, t => counts[t.Index], StringComparer.Ordinal);
            CheckTables(byName);
            Schema = AuditfileSchema.Create(Resolution.Version, Resolution.FileNamespace, Resolution.Source, Resolution.Dialect?.Name, byName);
        }
    }

    /// <summary>
    /// Walks the document (validating it when the reader validates) and counts the rows per table index.
    /// Recognizes a dialect by the software description on the way. Returns an empty array after XAF-X003.
    /// </summary>
    private long[] CountRows(XmlReader reader, ConcatenatedStream concatenated, CancellationToken cancellationToken)
    {
        var resolution = Resolution!;
        var plan = new MappingPlan(AuditfileSchema.Create(resolution.Version, resolution.FileNamespace, resolution.Source, null, rowCounts: null));
        var softwareNode = plan.Root.Child(resolution.Version.RootElement)?.Child("header")?.Child("softwareDesc");
        var counts = new long[resolution.Version.Manifest.Tables.Count];
        var namespaceUri = resolution.Version.Namespace;
        var nodes = new List<PathNode?>();
        StringBuilder? software = null;
        var softwareDepth = -1;
        long read = 0;

        while (reader.Read())
        {
            if (read++ % CancellationInterval == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            // The dialect is recognized here already, so the schema handed to the sink names it.
            if (software is not null && ReadSoftware(reader, software, softwareDepth))
            {
                resolution.UseSoftware(software.ToString(), _options.ApplyDialects);
                software = null;
            }

            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            var depth = reader.Depth;
            if (depth >= _options.MaxDepth)
            {
                Diagnostics.Add("XAF-X003", LocationOf(reader, concatenated), ("max", _options.MaxDepth));
                return Array.Empty<long>();
            }

            var parent = depth == 0 ? plan.Root : nodes[depth - 1];
            var node = parent is not null && reader.NamespaceURI == namespaceUri ? parent.Child(reader.LocalName) : null;
            if (nodes.Count <= depth)
            {
                nodes.Add(node);
            }
            else
            {
                nodes[depth] = node;
            }

            if (node is null)
            {
                continue;
            }

            if (node.Table is { } table)
            {
                counts[table.Table.Index]++;
            }
            else if (node == softwareNode && !reader.IsEmptyElement && resolution.Dialect is null)
            {
                software = new StringBuilder();
                softwareDepth = depth;
            }
        }

        return counts;
    }

    /// <summary>Adds text of the software description; true at its end tag.</summary>
    private bool ReadSoftware(XmlReader reader, StringBuilder software, int depth)
    {
        if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
        {
            if (software.Length <= _options.MaxValueLength)
            {
                software.Append(reader.Value);
            }

            return false;
        }

        return reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth;
    }

    /// <summary>
    /// Builds the reader stack: parts → peek for the version → namespace mapping → XSD validation.
    /// Takes ownership of the streams: the returned reader closes them, and on null or an exception they are closed here,
    /// so a file opened by a factory is never left locked.
    /// </summary>
    private XmlReader? CreateReader(ConcatenatedStream concatenated, bool validate, PeekableStream? peeked = null)
    {
        Stream? owned = peeked ?? (Stream)concatenated;
        XmlReader? reader = null;
        try
        {
            var peekable = peeked ?? new PeekableStream(concatenated);
            owned = peekable;
            if (Resolution is null && !ResolveFrom(peekable))
            {
                return null;
            }

            peekable.Rewind();
            reader = XmlReader.Create(peekable, BaseSettings(closeInput: true));
            owned = null;
            if (Resolution!.NeedsMapping)
            {
                reader = new NamespaceMappingReader(reader, Resolution.FileNamespace, Resolution.Version.Namespace);
            }

            if (validate)
            {
                XmlSchemaSet schemas;
                try
                {
                    schemas = LoadSchemaSet();
                }
                catch (Exception ex) when (ex is XmlSchemaException or XmlException || IsSourceError(ex))
                {
                    // Only a custom XSD can fail here; the embedded ones are checked by the tests.
                    Diagnostics.Add("XAF-X002", Location.None, ("part", "XSD"), ("detail", ex.Message));
                    return null;
                }

                var settings = BaseSettings(closeInput: true);
                settings.ValidationType = ValidationType.Schema;
                settings.Schemas = schemas;

                // Keys and references are checked by the ReferenceChecker (XAF-R003, XAF-R004) from the manifest, which holds
                // every constraint of the XSD (a test checks this). The XSD's own identity constraints use XPaths without a
                // namespace prefix, so in a namespaced document they never match; evaluating them only costs time (about 40%).
                settings.ValidationFlags &= ~XmlSchemaValidationFlags.ProcessIdentityConstraints;
                settings.ValidationEventHandler += (_, e) =>
                {
                    var line = e.Exception?.LineNumber ?? 0;
                    Diagnostics.Add("XAF-D001", new Location(PartOf(line, concatenated), line, e.Exception?.LinePosition, null), ("detail", e.Message));
                };

                reader = XmlReader.Create(reader, settings);
            }

            var result = reader;
            reader = null;
            return result;
        }
        finally
        {
            reader?.Dispose();
            owned?.Dispose();
        }
    }

    private bool ResolveFromPeek()
    {
        if (Resolution is not null)
        {
            return true;
        }

        ConcatenatedStream? concatenated = new(_source.Parts, collectStatistics: !_source.CanReadTwice);
        PeekableStream? peekable = null;
        try
        {
            peekable = new PeekableStream(concatenated);
            var stream = concatenated;
            concatenated = null;
            if (!ResolveFrom(peekable) || _source.CanReadTwice)
            {
                return Resolution is not null;
            }

            // Kept for loading: a single-use source is opened only once. The session now owns the streams.
            _pendingStream = stream;
            _pendingPeek = peekable;
            peekable = null;
            return true;
        }
        finally
        {
            peekable?.Dispose();
            concatenated?.Dispose();
        }
    }

    private bool ResolveFrom(PeekableStream peekable)
    {
        var root = PeekRoot(peekable);
        if (root is null)
        {
            return false;
        }

        Resolution = VersionResolver.Resolve(root.Value.Name, root.Value.Namespace, _options, Diagnostics);
        return Resolution is not null;
    }

    private (string Name, string Namespace)? PeekRoot(Stream stream)
    {
        try
        {
            using var reader = XmlReader.Create(stream, BaseSettings(closeInput: false));
            reader.MoveToContent();
            return reader.NodeType == XmlNodeType.Element ? (reader.LocalName, reader.NamespaceURI) : null;
        }
        catch (XmlException ex)
        {
            Diagnostics.Add("XAF-X001", new Location(1, ex.LineNumber, ex.LinePosition, null), ("detail", ex.Message));
            return null;
        }
        catch (Exception ex) when (IsSourceError(ex))
        {
            ReportSourceError(Diagnostics, ex, _source.Parts[0].Name, null);
            return null;
        }
    }

    /// <summary>
    /// A source that cannot be read: an I/O failure, a stream that refuses access, or a root element that never comes.
    /// These become XAF-X002, so a read always ends in a report. Cancellation and argument errors still throw.
    /// </summary>
    internal static bool IsSourceError(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException;

    /// <summary>XAF-X004 for a part in another encoding than the first part, XAF-X002 for any other source error.</summary>
    internal static void ReportSourceError(DiagnosticCollector diagnostics, Exception ex, string part, string? path)
    {
        if (ex is PartEncodingException encoding)
        {
            diagnostics.Add("XAF-X004", new Location(encoding.Part, null, null, path), ("encoding", encoding.Description));
            return;
        }

        diagnostics.Add("XAF-X002", new Location(null, null, null, path), ("part", part), ("detail", ex.Message));
    }

    private XmlSchemaSet LoadSchemaSet()
    {
        var version = Resolution!.Version;
        if (_options.Xsd is null)
        {
            return version.CreateSchemaSet();
        }

        // Read once, for the hash and for compiling: the factory is called only once per read.
        byte[] xsd;
        using (var stream = _options.Xsd() ?? throw new InvalidOperationException("ReadOptions.Xsd returned null."))
        using (var copy = new MemoryStream())
        {
            stream.CopyTo(copy);
            xsd = copy.ToArray();
        }

        if (AuditfileVersion.Sha256(new MemoryStream(xsd, writable: false)) != version.Manifest.XsdSha256)
        {
            Diagnostics.Add("XAF-D002", Location.None, ("version", version.Key));
        }

        return AuditfileVersion.CompileSchemaSet(new MemoryStream(xsd, writable: false), "custom.xsd");
    }

    private void CheckTables(Dictionary<string, long> counts)
    {
        var manifest = Resolution!.Version.Manifest;
        foreach (var table in manifest.RequiredTables.Where(t => !counts.TryGetValue(t, out var c) || c == 0))
        {
            Diagnostics.Add("XAF-S001", Location.None, ("table", table));
        }

        foreach (var table in manifest.ExpectedTables.Where(t => !counts.TryGetValue(t, out var c) || c == 0))
        {
            Diagnostics.Add("XAF-S002", Location.None, ("table", table));
        }
    }

    private XmlReaderSettings BaseSettings(bool closeInput) => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true,
        CloseInput = closeInput,
        MaxCharactersInDocument = _options.MaxCharactersInDocument,
    };

    internal static Location LocationOf(XmlReader reader, ConcatenatedStream concatenated)
    {
        var line = (reader as IXmlLineInfo)?.LineNumber;
        return new Location(line is null ? null : PartOf(line.Value, concatenated), line, (reader as IXmlLineInfo)?.LinePosition, null);
    }

    /// <summary>
    /// Part of a line number. The parser lags behind the stream, so a line beyond the completed parts
    /// belongs to the part currently being read.
    /// </summary>
    internal static int PartOf(int line, ConcatenatedStream concatenated) => PartOf(line, concatenated.Completed);

    internal static int PartOf(int line, IReadOnlyList<PartInfo> parts)
    {
        long end = 0;
        for (var i = 0; i < parts.Count; i++)
        {
            end += parts[i].Lines;
            if (line <= end)
            {
                return i + 1;
            }
        }

        return parts.Count + 1;
    }
}
