using System.Security.Cryptography;

namespace XmlAuditfile.Auditfile.Internal;

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

    public ConcatenatedStream(IReadOnlyList<SourcePart> parts, bool collectStatistics)
    {
        _parts = parts;
        CollectStatistics = collectStatistics;
    }

    public bool CollectStatistics { get; }

    /// <summary>Statistics of the parts read completely so far.</summary>
    public IReadOnlyList<PartInfo> Completed => _completed;

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
        var skip = _index == 0 ? 0 : SkipDeclaration(prefix, length);
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
    }

    /// <summary>Number of bytes of a leading UTF-8 BOM and XML declaration.</summary>
    private static int SkipDeclaration(byte[] bytes, int length)
    {
        var position = length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        if (length - position >= 5 && bytes[position] == '<' && bytes[position + 1] == '?' && bytes[position + 2] == 'x'
            && bytes[position + 3] == 'm' && bytes[position + 4] == 'l')
        {
            for (var i = position + 5; i < length - 1; i++)
            {
                if (bytes[i] == '?' && bytes[i + 1] == '>')
                {
                    return i + 2;
                }
            }
        }

        return position;
    }
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
