using System.Text;
using System.Xml;
using XmlAuditfile.Definitions;
using XmlAuditfile.Diagnostics;
using XmlAuditfile.Internal;
using XmlAuditfile.Validation;
using XmlAuditfile.Versioning;

namespace XmlAuditfile.Reading;

/// <summary>
/// The load phase: walks the XML once and turns elements into rows using the mapping plan.
/// A row is emitted when its element closes, so all its columns are known; ids are assigned when it opens,
/// so they follow document order. Only the open elements are kept in memory.
/// </summary>
internal sealed class RowLoader
{
    private readonly XmlReader _reader;
    private readonly MappingPlan _plan;
    private readonly AuditfileSchema _schema;
    private readonly Resolution _resolution;
    private readonly ReadOptions _options;
    private readonly DiagnosticCollector _diagnostics;
    private readonly IReadOnlyList<IRowObserver> _observers;
    private readonly ConcatenatedStream _stream;
    private readonly IReadOnlyList<PartInfo> _knownParts;
    private readonly string _namespace;
    private readonly string _softwarePath;
    private readonly List<PathNode> _nodes = new();
    private readonly List<Frame> _frames = new();
    private readonly Queue<AuditfileRow> _ready = new();
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private Dictionary<string, List<DialectCorrection>> _corrections = new(StringComparer.Ordinal);
    private long _nextId;
    private bool _readNext = true;
    private readonly bool _checkRequired;

    public RowLoader(
        XmlReader reader, AuditfileSchema schema, Resolution resolution, ReadOptions options, DiagnosticCollector diagnostics,
        IReadOnlyList<IRowObserver> observers, ConcatenatedStream stream, IReadOnlyList<PartInfo> knownParts)
    {
        _reader = reader;
        _schema = schema;
        _plan = new MappingPlan(schema);
        _resolution = resolution;
        _options = options;
        _diagnostics = diagnostics;
        _observers = observers;
        _stream = stream;
        _knownParts = knownParts;
        _namespace = resolution.Version.Namespace;
        _softwarePath = $"{resolution.Version.RootElement}/header/softwareDesc";
        UseDialect(resolution.Dialect);

        // Without the XSD nobody else notices a missing required field; with it, XAF-D001 already does.
        _checkRequired = options.Mode == ReadMode.Unverified;
    }

    /// <summary>True when reading stopped on a fatal error.</summary>
    public bool Stopped { get; private set; }

    public IEnumerable<AuditfileRow> Rows(CancellationToken cancellationToken)
    {
        long steps = 0;
        while (true)
        {
            while (_ready.Count > 0)
            {
                yield return _ready.Dequeue();
            }

            if (steps++ % ReadSession.CancellationInterval == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (!Step())
            {
                break;
            }
        }

        while (_ready.Count > 0)
        {
            yield return _ready.Dequeue();
        }
    }

    /// <summary>Processes one node. Returns false at the end of the document or after a fatal error.</summary>
    private bool Step()
    {
        try
        {
            if (_readNext && !_reader.Read())
            {
                return false;
            }

            if (_reader.EOF)
            {
                return false;
            }

            _readNext = true;
            switch (_reader.NodeType)
            {
                case XmlNodeType.Element:
                    return Start();
                case XmlNodeType.EndElement:
                    End();
                    break;
            }

            return true;
        }
        catch (XmlException ex)
        {
            _diagnostics.Add("XAF-X001", new Location(Part(ex.LineNumber), ex.LineNumber, ex.LinePosition, CurrentPath), ("detail", ex.Message));
            Stopped = true;
            return false;
        }
        catch (Exception ex) when (ReadSession.IsSourceError(ex))
        {
            ReadSession.ReportSourceError(_diagnostics, ex, _stream.CurrentPartName ?? "", CurrentPath);
            Stopped = true;
            return false;
        }
    }

    /// <summary>The open element, or the node above the root element before it opens.</summary>
    private PathNode Current => _nodes.Count == 0 ? _plan.Root : _nodes[_nodes.Count - 1];

    private string? CurrentPath => _nodes.Count == 0 ? null : _nodes[_nodes.Count - 1].Path;

    private Frame? Top => _frames.Count == 0 ? null : _frames[_frames.Count - 1];

    private bool Start()
    {
        if (_reader.Depth >= _options.MaxDepth)
        {
            _diagnostics.Add("XAF-X003", Here(CurrentPath), ("max", _options.MaxDepth));
            Stopped = true;
            return false;
        }

        var node = _reader.NamespaceURI == _namespace ? Current.Child(_reader.LocalName) : null;
        if (node is null)
        {
            Skip(Current.ChildPath(_reader.LocalName));
            return true;
        }

        if (node.Table is { } table)
        {
            StartTable(node, table);
        }
        else if (node.Column is { } target && Top is { } owner && owner.Plan == target.Table)
        {
            ReadValue(owner, target.Column, node.Path);
        }
        else if (node.IsGroup)
        {
            StartGroup(node);
        }
        else
        {
            Skip(node.Path);
        }

        return true;
    }

    private void StartTable(PathNode node, TablePlan table)
    {
        var parent = Top;
        var frame = new Frame(table, ++_nextId, parent?.Id, parent?.NextOrdinal(table) ?? 1, Part(LineNumber), node);
        ReadAttributes(node, frame);

        if (table.IsValueTable)
        {
            ReadValue(frame, table.Table.Columns[0], node.Path);
            Emit(frame);
            return;
        }

        var empty = _reader.IsEmptyElement;
        _frames.Add(frame);
        _nodes.Add(node);
        if (empty)
        {
            End();
        }
    }

    private void StartGroup(PathNode node)
    {
        if (Top is { } frame)
        {
            frame.Groups.Add(node);
            ReadAttributes(node, frame);
        }

        if (!_reader.IsEmptyElement)
        {
            _nodes.Add(node);
        }
    }

    private void End()
    {
        if (_nodes.Count == 0)
        {
            return;
        }

        var node = _nodes[_nodes.Count - 1];
        _nodes.RemoveAt(_nodes.Count - 1);

        if (Top is { } frame && frame.Node == node)
        {
            _frames.RemoveAt(_frames.Count - 1);
            Emit(frame);
        }
    }

    private void Skip(string path)
    {
        if (_reported.Add("S003:" + path))
        {
            _diagnostics.Add("XAF-S003", Here(path), ("path", path));
        }

        _reader.Skip();
        _readNext = false;
    }

    /// <summary>
    /// Reads the text of the current element into a column, like ReadElementContentAsString, but keeps at most
    /// <see cref="ReadOptions.MaxValueLength"/> characters: the rest of a longer value is only counted, and the value is
    /// reported (XAF-S008) and left empty. Leaves the reader on the node after the end tag.
    /// </summary>
    private void ReadValue(Frame frame, SchemaColumn column, string path)
    {
        _readNext = false;
        if (_reader.IsEmptyElement)
        {
            _reader.Read();
            SetValue(frame, column, "", path);
            return;
        }

        var max = _options.MaxValueLength;
        var depth = _reader.Depth;
        var text = new StringBuilder();
        char[]? buffer = null;
        long length = 0;
        _reader.Read();
        while (_reader.NodeType != XmlNodeType.EndElement || _reader.Depth != depth)
        {
            switch (_reader.NodeType)
            {
                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.Whitespace:
                case XmlNodeType.SignificantWhitespace:
                    if (_reader.CanReadValueChunk)
                    {
                        buffer ??= new char[4096];
                        int read;
                        while ((read = _reader.ReadValueChunk(buffer, 0, buffer.Length)) > 0)
                        {
                            length += read;
                            if (length <= max)
                            {
                                text.Append(buffer, 0, read);
                            }
                        }
                    }
                    else
                    {
                        var value = _reader.Value;
                        length += value.Length;
                        if (length <= max)
                        {
                            text.Append(value);
                        }
                    }

                    break;
                case XmlNodeType.Element:
                    throw new XmlException($"Element '{_reader.LocalName}' is not allowed inside '{path}', which holds a value.", null, LineNumber, (_reader as IXmlLineInfo)?.LinePosition ?? 0);
            }

            _reader.Read();
        }

        _reader.Read();
        if (length > max)
        {
            frame.Present[column.Index] = true;
            _diagnostics.Add("XAF-S008", Here(path),
                ("table", frame.Plan.Table.Name), ("column", column.Name), ("length", length), ("max", max));
            return;
        }

        SetValue(frame, column, text.ToString(), path);
    }

    private void ReadAttributes(PathNode element, Frame frame)
    {
        if (!_reader.HasAttributes)
        {
            return;
        }

        while (_reader.MoveToNextAttribute())
        {
            if (_reader.Prefix == "xmlns" || _reader.LocalName == "xmlns" || _reader.NamespaceURI == "http://www.w3.org/2001/XMLSchema-instance")
            {
                continue;
            }

            if (element.Attribute(_reader.LocalName) is { Column: { } target } attribute && target.Table == frame.Plan)
            {
                SetValue(frame, target.Column, _reader.Value, attribute.Path);
            }
        }

        _reader.MoveToElement();
    }

    private void SetValue(Frame frame, SchemaColumn column, string raw, string path)
    {
        if (_corrections.TryGetValue(path, out var corrections))
        {
            raw = Correct(raw, corrections, column, path);
        }

        if (ValueConverter.TryConvert(raw, column.Type, out var value))
        {
            frame.Values[column.Index] = value;
        }
        else
        {
            _diagnostics.Add("XAF-S004", Here(path), ("value", raw), ("column", $"{frame.Plan.Table.Name}.{column.Name}"), ("type", column.Type.ToString().ToLowerInvariant()));
        }

        frame.Present[column.Index] = true;
        if (path == _softwarePath && value is string software && _resolution.UseSoftware(software, _options.ApplyDialects))
        {
            UseDialect(_resolution.Dialect);
        }
    }

    private string Correct(string raw, List<DialectCorrection> corrections, SchemaColumn column, string path)
    {
        foreach (var correction in corrections)
        {
            var corrected = correction.Action switch
            {
                DialectAction.DecimalComma when raw.IndexOf(',') >= 0 && raw.IndexOf('.') < 0 => raw.Replace(',', '.'),
                DialectAction.DateFromDateTime when column.Type == ColumnType.Date && raw.Trim().Length > 10 => raw.Trim().Substring(0, 10),
                _ => raw,
            };

            if (!ReferenceEquals(corrected, raw) && corrected != raw && _reported.Add(correction.Code + ":" + path))
            {
                _diagnostics.Add(correction.Code, Here(path), ("path", path));
            }

            raw = corrected;
        }

        return raw;
    }

    private void UseDialect(Dialect? dialect)
    {
        _corrections = new Dictionary<string, List<DialectCorrection>>(StringComparer.Ordinal);
        if (dialect is null)
        {
            return;
        }

        foreach (var correction in dialect.Corrections.Where(c => c.Action != DialectAction.ReplaceNamespace))
        {
            foreach (var path in correction.Paths)
            {
                if (!_corrections.TryGetValue(path, out var list))
                {
                    list = new List<DialectCorrection>();
                    _corrections[path] = list;
                }

                list.Add(correction);
            }
        }
    }

    private void Emit(Frame frame)
    {
        if (_checkRequired)
        {
            CheckRequired(frame);
        }

        var row = new AuditfileRow(frame.Plan.Table, frame.Id, frame.ParentId, frame.Ordinal, frame.Part, frame.Values);
        foreach (var observer in _observers)
        {
            observer.OnRow(row);
        }

        _ready.Enqueue(row);
    }

    /// <summary>
    /// XAF-S006 for each required column the row lacks. MinOccurs is relative to the column's XML parent:
    /// a column inside an optional group is only required when that group occurs.
    /// </summary>
    private void CheckRequired(Frame frame)
    {
        foreach (var required in frame.Plan.Required)
        {
            if (!frame.Present[required.Column.Index] && (required.Group is null || frame.Groups.Contains(required.Group)))
            {
                _diagnostics.Add("XAF-S006", Here(frame.Node.Path), ("column", required.Column.Name), ("table", frame.Plan.Table.Name));
            }
        }
    }

    private int LineNumber => (_reader as IXmlLineInfo)?.LineNumber ?? 0;

    private int Part(int line) => _knownParts.Count > 0 ? ReadSession.PartOf(line, _knownParts) : ReadSession.PartOf(line, _stream);

    private Location Here(string? path)
    {
        var info = _reader as IXmlLineInfo;
        return new Location(Part(LineNumber), info?.LineNumber, info?.LinePosition, path);
    }

    /// <summary>An open table element.</summary>
    private sealed class Frame(TablePlan plan, long id, long? parentId, int ordinal, int part, PathNode node)
    {
        private Dictionary<TablePlan, int>? _ordinals;
        private HashSet<PathNode>? _groups;

        public TablePlan Plan { get; } = plan;

        public long Id { get; } = id;

        public long? ParentId { get; } = parentId;

        public int Ordinal { get; } = ordinal;

        public int Part { get; } = part;

        public PathNode Node { get; } = node;

        public object?[] Values { get; } = new object?[plan.Table.Columns.Count];

        /// <summary>Columns that occurred, also when their value was invalid.</summary>
        public bool[] Present { get; } = new bool[plan.Table.Columns.Count];

        /// <summary>Groups that occurred within this row.</summary>
        public HashSet<PathNode> Groups => _groups ??= new HashSet<PathNode>();

        public int NextOrdinal(TablePlan child)
        {
            _ordinals ??= new Dictionary<TablePlan, int>();
            _ordinals.TryGetValue(child, out var current);
            _ordinals[child] = current + 1;
            return current + 1;
        }
    }
}
