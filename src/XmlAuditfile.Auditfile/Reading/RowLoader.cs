using System.Xml;
using XmlAuditfile.Auditfile.Definitions;
using XmlAuditfile.Auditfile.Diagnostics;
using XmlAuditfile.Auditfile.Internal;
using XmlAuditfile.Auditfile.Validation;
using XmlAuditfile.Auditfile.Versioning;

namespace XmlAuditfile.Auditfile.Reading;

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
    private readonly List<string> _paths = new();
    private readonly List<Frame> _frames = new();
    private readonly Queue<AuditfileRow> _ready = new();
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private Dictionary<string, List<DialectCorrection>> _corrections = new(StringComparer.Ordinal);
    private long _nextId;
    private bool _readNext = true;

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
    }

    /// <summary>True when reading stopped on a fatal error.</summary>
    public bool Stopped { get; private set; }

    public IEnumerable<AuditfileRow> Rows()
    {
        while (true)
        {
            while (_ready.Count > 0)
            {
                yield return _ready.Dequeue();
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
    }

    private string? CurrentPath => _paths.Count == 0 ? null : _paths[_paths.Count - 1];

    private Frame? Top => _frames.Count == 0 ? null : _frames[_frames.Count - 1];

    private bool Start()
    {
        if (_reader.Depth >= _options.MaxDepth)
        {
            _diagnostics.Add("XAF-X003", Here(CurrentPath), ("max", _options.MaxDepth));
            Stopped = true;
            return false;
        }

        var path = CurrentPath is null ? _reader.LocalName : $"{CurrentPath}/{_reader.LocalName}";
        var empty = _reader.IsEmptyElement;

        if (_reader.NamespaceURI != _namespace)
        {
            Skip(path);
            return true;
        }

        if (_plan.Tables.TryGetValue(path, out var table))
        {
            var parent = Top;
            var frame = new Frame(table, ++_nextId, parent?.Id, parent?.NextOrdinal(table) ?? 1, Part(LineNumber), path);
            ReadAttributes(path, frame);

            if (table.IsValueTable)
            {
                SetValue(frame, table.Table.Columns[0], _reader.ReadElementContentAsString(), path);
                _readNext = false;
                Emit(frame);
                return true;
            }

            _frames.Add(frame);
            _paths.Add(path);
            if (empty)
            {
                End();
            }

            return true;
        }

        if (_plan.Columns.TryGetValue(path, out var target) && Top is { } owner && owner.Plan == target.Table)
        {
            var raw = _reader.ReadElementContentAsString();
            _readNext = false;
            SetValue(owner, target.Column, raw, path);
            return true;
        }

        if (_plan.Groups.Contains(path))
        {
            if (Top is { } frame)
            {
                ReadAttributes(path, frame);
            }

            _paths.Add(path);
            if (empty)
            {
                _paths.RemoveAt(_paths.Count - 1);
            }

            return true;
        }

        Skip(path);
        return true;
    }

    private void End()
    {
        if (_paths.Count == 0)
        {
            return;
        }

        var path = _paths[_paths.Count - 1];
        _paths.RemoveAt(_paths.Count - 1);

        if (Top is { } frame && frame.Path == path)
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

    private void ReadAttributes(string elementPath, Frame frame)
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

            var path = $"{elementPath}/@{_reader.LocalName}";
            if (_plan.Columns.TryGetValue(path, out var target) && target.Table == frame.Plan)
            {
                SetValue(frame, target.Column, _reader.Value, path);
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

        if (path == _softwarePath && _resolution.Dialect is null && _options.ApplyDialects && value is string software)
        {
            var dialect = _resolution.Version.Dialects.FirstOrDefault(d => d.SoftwareDescription == software);
            if (dialect is not null)
            {
                _resolution.Dialect = dialect;
                UseDialect(dialect);
            }
        }
    }

    private string Correct(string raw, List<DialectCorrection> corrections, SchemaColumn column, string path)
    {
        foreach (var correction in corrections)
        {
            var corrected = correction.Action switch
            {
                "decimalComma" when raw.IndexOf(',') >= 0 && raw.IndexOf('.') < 0 => raw.Replace(',', '.'),
                "dateFromDateTime" when column.Type == ColumnType.Date && raw.Trim().Length > 10 => raw.Trim().Substring(0, 10),
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

        foreach (var correction in dialect.Corrections.Where(c => c.Action != "replaceNamespace"))
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
        var row = new AuditfileRow(frame.Plan.Table, frame.Id, frame.ParentId, frame.Ordinal, frame.Part, frame.Values);
        foreach (var observer in _observers)
        {
            observer.OnRow(row);
        }

        _ready.Enqueue(row);
    }

    private int LineNumber => (_reader as IXmlLineInfo)?.LineNumber ?? 0;

    private int Part(int line) => _knownParts.Count > 0 ? ReadSession.PartOf(line, _knownParts) : ReadSession.PartOf(line, _stream);

    private Location Here(string? path)
    {
        var info = _reader as IXmlLineInfo;
        return new Location(Part(LineNumber), info?.LineNumber, info?.LinePosition, path);
    }

    /// <summary>An open table element.</summary>
    private sealed class Frame(TablePlan plan, long id, long? parentId, int ordinal, int part, string path)
    {
        private Dictionary<TablePlan, int>? _ordinals;

        public TablePlan Plan { get; } = plan;

        public long Id { get; } = id;

        public long? ParentId { get; } = parentId;

        public int Ordinal { get; } = ordinal;

        public int Part { get; } = part;

        public string Path { get; } = path;

        public object?[] Values { get; } = new object?[plan.Table.Columns.Count];

        public int NextOrdinal(TablePlan child)
        {
            _ordinals ??= new Dictionary<TablePlan, int>();
            _ordinals.TryGetValue(child, out var current);
            _ordinals[child] = current + 1;
            return current + 1;
        }
    }
}
