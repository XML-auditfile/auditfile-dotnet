using System.Security.Cryptography;

namespace XmlAuditfile.Internal;

/// <summary>Base for the read-only, forward-only streams below.</summary>
internal abstract class ForwardOnlyStream : Stream
{
    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>Wraps a caller-owned stream so the reader never disposes it.</summary>
internal sealed class NonClosingStream(Stream inner) : ForwardOnlyStream
{
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
}

/// <summary>
/// Reads the parts of a source one after another as one document.
/// For continuation parts a leading byte order mark and XML declaration are skipped.
/// Collects size, SHA-256 and line count per part when <see cref="CollectStatistics"/> is set.
/// </summary>
internal sealed class ConcatenatedStream : ForwardOnlyStream
{
    private const int PrefixSize = 1024;

    private readonly IReadOnlyList<SourcePart> _parts;
    private readonly List<PartInfo> _completed = new();
    private int _index = -1;
    private Stream? _current;
    private byte[] _prefix = Array.Empty<byte>();
    private int _prefixOffset;
    private IncrementalHash? _hash;
    private long _size;
    private long _lines;
    private bool _lastWasCarriageReturn;
    private PartEncoding? _encoding;

    public ConcatenatedStream(IReadOnlyList<SourcePart> parts, bool collectStatistics)
    {
        _parts = parts;
        CollectStatistics = collectStatistics;
    }

    public bool CollectStatistics { get; }

    /// <summary>Statistics of the parts read completely so far.</summary>
    public IReadOnlyList<PartInfo> Completed => _completed;

    /// <summary>Name of the part being read, or null before the first part.</summary>
    public string? CurrentPartName => _index >= 0 && _index < _parts.Count ? _parts[_index].Name : null;

    public override int Read(byte[] buffer, int offset, int count)
    {
        while (true)
        {
            if (_current is null && !OpenNext())
            {
                return 0;
            }

            if (_prefixOffset < _prefix.Length)
            {
                var fromPrefix = Math.Min(count, _prefix.Length - _prefixOffset);
                Buffer.BlockCopy(_prefix, _prefixOffset, buffer, offset, fromPrefix);
                _prefixOffset += fromPrefix;
                return fromPrefix;
            }

            var read = _current!.Read(buffer, offset, count);
            if (read > 0)
            {
                Track(buffer, offset, read);
                return read;
            }

            ClosePart();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _current?.Dispose();
            _hash?.Dispose();
        }

        base.Dispose(disposing);
    }

    private bool OpenNext()
    {
        if (_index + 1 >= _parts.Count)
        {
            return false;
        }

        _index++;
        _current = _parts[_index].Open();
        _size = 0;
        _lines = 1;
        _lastWasCarriageReturn = false;
        _hash = CollectStatistics ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;

        var prefix = new byte[PrefixSize];
        var length = 0;
        int read;
        while (length < PrefixSize && (read = _current.Read(prefix, length, PrefixSize - length)) > 0)
        {
            length += read;
        }

        Track(prefix, 0, length);

        // The parts are decoded as one document, with the encoding of the first part. A part in another encoding would be
        // read as garbled text without any error, so it is refused (XAF-X004).
        var encoding = PartEncoding.Detect(prefix, length);
        if (_index == 0)
        {
            _encoding = encoding;
        }
        else if (!encoding.SameAs(_encoding!))
        {
            throw new PartEncodingException(_parts[_index].Number, encoding.Name, _encoding!.Name);
        }

        var skip = _index == 0 ? 0 : encoding.SkipDeclaration(prefix, length);
        _prefix = new byte[length - skip];
        Buffer.BlockCopy(prefix, skip, _prefix, 0, _prefix.Length);
        _prefixOffset = 0;
        return true;
    }

    private void ClosePart()
    {
        if (_hash is not null)
        {
            var part = _parts[_index];
            _completed.Add(new PartInfo(part.Number, part.Name, _size, AuditfileVersion.Hex(_hash.GetHashAndReset()), _lines));
            _hash.Dispose();
            _hash = null;
        }

        _current!.Dispose();
        _current = null;
        _prefix = Array.Empty<byte>();
    }

    private void Track(byte[] buffer, int offset, int count)
    {
        _size += count;
        _hash?.AppendData(buffer, offset, count);
        if (count == 0)
        {
            return;
        }

#if NET
        // CR, LF and CRLF each end a line; the counts are vectorized. A CRLF split over two reads counts once.
        var span = buffer.AsSpan(offset, count);
        var pairs = 0;
        for (var rest = span; rest.IndexOf("\r\n"u8) is var at and >= 0; rest = rest.Slice(at + 2))
        {
            pairs++;
        }

        _lines += span.Count((byte)'\r') + span.Count((byte)'\n') - pairs - (_lastWasCarriageReturn && span[0] == (byte)'\n' ? 1 : 0);
        _lastWasCarriageReturn = span[span.Length - 1] == (byte)'\r';
#else
        for (var i = offset; i < offset + count; i++)
        {
            var b = buffer[i];
            if (b == (byte)'\n')
            {
                if (!_lastWasCarriageReturn)
                {
                    _lines++;
                }

                _lastWasCarriageReturn = false;
            }
            else if (b == (byte)'\r')
            {
                _lines++;
                _lastWasCarriageReturn = true;
            }
            else
            {
                _lastWasCarriageReturn = false;
            }
        }
#endif
    }
}

/// <summary>A continuation part whose encoding differs from the first part.</summary>
internal sealed class PartEncodingException(int part, string encoding, string firstEncoding)
    : IOException($"Part {part} is encoded as {encoding}, but part 1 as {firstEncoding}.")
{
    public int Part { get; } = part;

    /// <summary>The encoding for the diagnostic, e.g. "UTF-8 (part 1: ISO-8859-1)".</summary>
    public string Description { get; } = $"{encoding} (part 1: {firstEncoding})";
}

/// <summary>
/// The encoding of one part, from its byte order mark or XML declaration (UTF-8 when neither says otherwise),
/// following the detection rules of XML 1.0 appendix F.
/// </summary>
internal sealed class PartEncoding
{
    private PartEncoding(string name, int bomLength, int unitSize, bool bigEndian)
    {
        Name = name;
        BomLength = bomLength;
        UnitSize = unitSize;
        BigEndian = bigEndian;
    }

    /// <summary>Normalized name, e.g. "UTF-8", "UTF-16LE", "ISO-8859-1".</summary>
    public string Name { get; }

    private int BomLength { get; }

    /// <summary>Bytes per code unit: 1 for UTF-8 and single-byte encodings, 2 for UTF-16.</summary>
    private int UnitSize { get; }

    private bool BigEndian { get; }

    public bool SameAs(PartEncoding other) => Name == other.Name;

    public static PartEncoding Detect(byte[] bytes, int length)
    {
        bool StartsWith(params byte[] start) => length >= start.Length && start.Select((b, i) => bytes[i] == b).All(x => x);

        if (StartsWith(0xEF, 0xBB, 0xBF))
        {
            return new PartEncoding("UTF-8", 3, 1, false);
        }

        if (StartsWith(0xFF, 0xFE) || StartsWith((byte)'<', 0, (byte)'?', 0))
        {
            return new PartEncoding("UTF-16LE", StartsWith(0xFF, 0xFE) ? 2 : 0, 2, false);
        }

        if (StartsWith(0xFE, 0xFF) || StartsWith(0, (byte)'<', 0, (byte)'?'))
        {
            return new PartEncoding("UTF-16BE", StartsWith(0xFE, 0xFF) ? 2 : 0, 2, true);
        }

        return new PartEncoding(Normalize(DeclaredEncoding(bytes, length) ?? "UTF-8"), 0, 1, false);
    }

    /// <summary>Number of bytes of a leading byte order mark and XML declaration.</summary>
    public int SkipDeclaration(byte[] bytes, int length)
    {
        var position = BomLength;
        if (!Matches(bytes, length, position, "<?xml"))
        {
            return position;
        }

        for (var i = position + (5 * UnitSize); i + (2 * UnitSize) <= length; i += UnitSize)
        {
            if (Matches(bytes, length, i, "?>"))
            {
                return i + (2 * UnitSize);
            }
        }

        return position;
    }

    private bool Matches(byte[] bytes, int length, int position, string text)
    {
        for (var c = 0; c < text.Length; c++)
        {
            var at = position + (c * UnitSize);
            if (at + UnitSize > length)
            {
                return false;
            }

            // A UTF-16 code unit of an ASCII character: the character in one byte, zero in the other.
            var character = UnitSize == 2 && BigEndian ? bytes[at + 1] : bytes[at];
            var zero = UnitSize == 2 && BigEndian ? bytes[at] : bytes[at + UnitSize - 1];
            if (character != text[c] || (UnitSize == 2 && zero != 0))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The encoding pseudo-attribute of an ASCII-compatible XML declaration, or null.</summary>
    private static string? DeclaredEncoding(byte[] bytes, int length)
    {
        if (length < 5 || bytes[0] != '<' || bytes[1] != '?' || bytes[2] != 'x' || bytes[3] != 'm' || bytes[4] != 'l')
        {
            return null;
        }

        var end = 5;
        while (end < length - 1 && !(bytes[end] == '?' && bytes[end + 1] == '>'))
        {
            end++;
        }

        var declaration = System.Text.Encoding.ASCII.GetString(bytes, 0, end);
        var match = System.Text.RegularExpressions.Regex.Match(declaration, @"encoding\s*=\s*(?:""([^""]*)""|'([^']*)')");
        if (!match.Success)
        {
            return null;
        }

        return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
    }

    private static string Normalize(string name) => name.Trim().ToUpperInvariant() switch
    {
        "UTF8" => "UTF-8",
        "LATIN1" or "LATIN-1" or "ISO8859-1" or "ISO_8859-1" => "ISO-8859-1",
        "CP1252" => "WINDOWS-1252",
        "ASCII" => "US-ASCII",
        var upper => upper,
    };
}

/// <summary>
/// Records the first bytes read so the stream can be rewound once.
/// Used to read the root element (version detection) before the real reader starts.
/// </summary>
internal sealed class PeekableStream(Stream inner) : ForwardOnlyStream
{
    private const int Limit = 1024 * 1024;

    private readonly MemoryStream _recorded = new();
    private bool _recording = true;
    private byte[]? _replay;
    private int _replayOffset;

    public void Rewind()
    {
        if (!_recording)
        {
            throw new InvalidOperationException("The stream can only be rewound once.");
        }

        _recording = false;
        _replay = _recorded.ToArray();
        _replayOffset = 0;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_replay is not null && _replayOffset < _replay.Length)
        {
            var fromReplay = Math.Min(count, _replay.Length - _replayOffset);
            Buffer.BlockCopy(_replay, _replayOffset, buffer, offset, fromReplay);
            _replayOffset += fromReplay;
            return fromReplay;
        }

        var read = inner.Read(buffer, offset, count);
        if (_recording)
        {
            if (_recorded.Length + read > Limit)
            {
                throw new InvalidDataException("The root element was not found within the first megabyte.");
            }

            _recorded.Write(buffer, offset, read);
        }

        return read;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
