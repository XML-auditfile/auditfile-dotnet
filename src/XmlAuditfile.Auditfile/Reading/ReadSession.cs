using System.Diagnostics;
using System.Xml;
using System.Xml.Schema;
using XmlAuditfile.Auditfile.Definitions;
using XmlAuditfile.Auditfile.Diagnostics;
using XmlAuditfile.Auditfile.Internal;
using XmlAuditfile.Auditfile.Validation;
using XmlAuditfile.Auditfile.Versioning;

namespace XmlAuditfile.Auditfile.Reading;

/// <summary>
/// One read of one source: resolve the version, optionally scan (validate and count),
/// then load (stream rows). Holds the diagnostics and the part statistics.
/// </summary>
internal sealed class ReadSession
{
    private readonly AuditfileSource _source;
    private readonly ReadOptions _options;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly Dictionary<string, long> _rowCounts = new(StringComparer.Ordinal);
    private IReadOnlyList<PartInfo> _parts = Array.Empty<PartInfo>();
    private bool _loaded;

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
    public bool Prepare(bool sinkNeedsRowCounts, bool scanOnly = false)
    {
        var verified = _options.Mode == ReadMode.Verified;
        var wantScan = scanOnly || verified || sinkNeedsRowCounts || _source.Parts.Count > 1;

        if (wantScan && _source.CanReadTwice)
        {
            Scan(validate: verified);
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
        return !scanOnly;
    }

    /// <summary>The load phase as a stream of rows. Diagnostics are collected along the way.</summary>
    public IEnumerable<AuditfileRow> Load()
    {
        if (Schema is null)
        {
            yield break;
        }

        var concatenated = _pendingStream ?? new ConcatenatedStream(_source.Parts, collectStatistics: _parts.Count == 0);
        using var reader = CreateReader(concatenated, ValidateDuringLoad, _pendingPeek);
        _pendingStream = null;
        _pendingPeek = null;
        if (reader is null)
        {
            yield break;
        }

        var observers = new List<IRowObserver> { new ReferenceChecker(Schema, Diagnostics) };
        if (Resolution!.Version.Type == "xaf")
        {
            observers.Add(new XafRules(Schema, Diagnostics));
        }

        var loader = new RowLoader(reader, Schema, Resolution, _options, Diagnostics, observers, concatenated, _parts);
        foreach (var row in loader.Rows())
        {
            _rowCounts[row.Table.Name] = _rowCounts.TryGetValue(row.Table.Name, out var count) ? count + 1 : 1;
            yield return row;
        }

        if (!loader.Stopped)
        {
            foreach (var observer in observers)
            {
                observer.OnEnd();
            }

            CheckTables(_rowCounts);
        }

        if (_parts.Count == 0)
        {
            _parts = concatenated.Completed;
        }

        _loaded = true;
    }

    public AuditfileReport CreateReport(bool loaded) =>
        new(Schema, _parts, Diagnostics.Items, _loaded ? _rowCounts : RowCountsFromSchema(), loaded, _stopwatch.Elapsed);

    private Dictionary<string, long> RowCountsFromSchema() =>
        Schema?.Tables.Where(t => t.RowCount > 0).ToDictionary(t => t.Name, t => t.RowCount!.Value, StringComparer.Ordinal)
        ?? new Dictionary<string, long>(StringComparer.Ordinal);

    private void Scan(bool validate)
    {
        var concatenated = new ConcatenatedStream(_source.Parts, collectStatistics: true);
        using (var reader = CreateReader(concatenated, validate))
        {
            if (reader is null)
            {
                return;
            }

            var manifest = Resolution!.Version.Manifest;
            var tablesByPath = manifest.Tables.ToDictionary(t => t.Path, t => t.Name, StringComparer.Ordinal);
            var counts = manifest.Tables.ToDictionary(t => t.Name, _ => 0L, StringComparer.Ordinal);
            var paths = new List<string?>();

            try
            {
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element)
                    {
                        continue;
                    }

                    var depth = reader.Depth;
                    if (depth >= _options.MaxDepth)
                    {
                        Diagnostics.Add("XAF-X003", LocationOf(reader, concatenated), ("max", _options.MaxDepth));
                        return;
                    }

                    var parent = depth == 0 ? null : paths[depth - 1];
                    var path = reader.NamespaceURI == manifest.Namespace && (depth == 0 || parent is not null)
                        ? depth == 0 ? reader.LocalName : $"{parent}/{reader.LocalName}"
                        : null;

                    while (paths.Count <= depth)
                    {
                        paths.Add(null);
                    }

                    paths[depth] = path;
                    if (path is not null && tablesByPath.TryGetValue(path, out var table))
                    {
                        counts[table]++;
                    }
                }
            }
            catch (XmlException ex)
            {
                Diagnostics.Add("XAF-X001", new Location(PartOf(ex.LineNumber, concatenated), ex.LineNumber, ex.LinePosition, null), ("detail", ex.Message));
                return;
            }

            _parts = concatenated.Completed;
            CheckTables(counts);
            Schema = AuditfileSchema.Create(Resolution.Version, Resolution.FileNamespace, Resolution.Source, Resolution.Dialect?.Name, counts);
        }
    }

    /// <summary>Builds the reader stack: parts → peek for the version → namespace mapping → XSD validation.</summary>
    private XmlReader? CreateReader(ConcatenatedStream concatenated, bool validate, PeekableStream? peeked = null)
    {
        var peekable = peeked ?? new PeekableStream(concatenated);
        if (Resolution is null && !ResolveFrom(peekable))
        {
            peekable.Dispose();
            return null;
        }

        peekable.Rewind();
        XmlReader reader = XmlReader.Create(peekable, BaseSettings(closeInput: true));
        if (Resolution!.NeedsMapping)
        {
            reader = new NamespaceMappingReader(reader, Resolution.FileNamespace, Resolution.Version.Namespace);
        }

        if (!validate)
        {
            return reader;
        }

        var settings = BaseSettings(closeInput: true);
        settings.ValidationType = ValidationType.Schema;
        settings.Schemas = LoadSchemaSet();
        settings.ValidationEventHandler += (_, e) =>
        {
            var line = e.Exception?.LineNumber ?? 0;
            Diagnostics.Add("XAF-D001", new Location(PartOf(line, concatenated), line, e.Exception?.LinePosition, null), ("detail", e.Message));
        };

        return XmlReader.Create(reader, settings);
    }

    private bool ResolveFromPeek()
    {
        if (Resolution is not null)
        {
            return true;
        }

        var concatenated = new ConcatenatedStream(_source.Parts, collectStatistics: !_source.CanReadTwice);
        var peekable = new PeekableStream(concatenated);
        if (!ResolveFrom(peekable) || _source.CanReadTwice)
        {
            peekable.Dispose();
            return Resolution is not null;
        }

        _pendingStream = concatenated;
        _pendingPeek = peekable;
        return true;
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
        catch (IOException ex)
        {
            Diagnostics.Add("XAF-X002", Location.None, ("part", _source.Parts[0].Name), ("detail", ex.Message));
            return null;
        }
    }

    private XmlSchemaSet LoadSchemaSet()
    {
        var version = Resolution!.Version;
        if (_options.Xsd is null)
        {
            return version.CreateSchemaSet();
        }

        if (AuditfileVersion.Sha256(_options.Xsd()) != version.Manifest.XsdSha256)
        {
            Diagnostics.Add("XAF-D002", Location.None, ("version", version.Key));
        }

        return AuditfileVersion.CompileSchemaSet(_options.Xsd(), "custom.xsd");
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

    internal static XmlReaderSettings BaseSettings(bool closeInput) => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true,
        CloseInput = closeInput,
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
