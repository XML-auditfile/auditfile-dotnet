using XmlAuditfile.Auditfile.Internal;

namespace XmlAuditfile.Auditfile;

/// <summary>
/// Where an audit file comes from: one stream, or several streams for the parts of a split audit file.
/// The reader never opens files itself; the caller supplies the streams, so the same code runs on
/// a server, under Native AOT and in the browser. A factory can be read twice (scan, then load); a plain stream only once.
/// </summary>
public sealed class AuditfileSource
{
    private AuditfileSource(IReadOnlyList<SourcePart> parts)
    {
        if (parts.Count == 0)
        {
            throw new ArgumentException("A source needs at least one part.", nameof(parts));
        }

        Parts = parts;
    }

    /// <summary>The parts in reading order.</summary>
    public IReadOnlyList<SourcePart> Parts { get; }

    /// <summary>True when every part can be opened again, so the reader can scan before loading.</summary>
    public bool CanReadTwice => Parts.All(p => p.Reopenable);

    /// <summary>A stream that can be read only once. The stream is not disposed by the reader.</summary>
    public static AuditfileSource FromStream(Stream stream, string name = "stream") =>
        FromStreams((name, stream));

    /// <summary>
    /// A split audit file as streams that can be read only once: the first part followed by its continuation parts, in order.
    /// The streams are not disposed by the reader.
    /// </summary>
    public static AuditfileSource FromStreams(params (string Name, Stream Stream)[] parts) =>
        new(Guard.NotNull(parts, nameof(parts)).Select((p, i) => SingleUse(i + 1, p.Name, Guard.NotNull(p.Stream, nameof(parts)))).ToList());

    /// <summary>
    /// A stream factory that returns a fresh stream on every call, so the source can be read twice.
    /// The reader disposes the streams it gets.
    /// </summary>
    /// <example>
    /// <code>
    /// AuditfileSource.FromFactory(() => File.OpenRead("admin.xaf"), "admin.xaf");
    /// AuditfileSource.FromFactory(() => new MemoryStream(bytes));
    /// </code>
    /// </example>
    public static AuditfileSource FromFactory(Func<Stream> open, string name = "stream") =>
        FromFactories((name, open));

    /// <summary>
    /// A split audit file as stream factories: the first part followed by its continuation parts, in order.
    /// Every factory returns a fresh stream on every call, so the source can be read twice. The reader disposes the streams it gets.
    /// </summary>
    public static AuditfileSource FromFactories(params (string Name, Func<Stream> Open)[] parts) =>
        new(Guard.NotNull(parts, nameof(parts)).Select((p, i) => new SourcePart(i + 1, p.Name, Guard.NotNull(p.Open, nameof(parts)), reopenable: true)).ToList());

    private static SourcePart SingleUse(int number, string name, Stream stream)
    {
        var used = false;
        return new SourcePart(number, name, () =>
        {
            if (used)
            {
                throw new InvalidOperationException("This stream source can only be read once.");
            }

            used = true;
            return new NonClosingStream(stream);
        }, reopenable: false);
    }
}

/// <summary>One part of a source.</summary>
public sealed class SourcePart
{
    internal SourcePart(int number, string name, Func<Stream> open, bool reopenable)
    {
        Number = number;
        Name = name;
        Open = open;
        Reopenable = reopenable;
    }

    /// <summary>Part number (1-based).</summary>
    public int Number { get; }

    /// <summary>File or stream name.</summary>
    public string Name { get; }

    /// <summary>True when the part can be opened more than once.</summary>
    public bool Reopenable { get; }

    internal Func<Stream> Open { get; }
}

/// <summary>Size, checksum and line count of a source part, known after it has been read once.</summary>
public sealed class PartInfo
{
    internal PartInfo(int number, string name, long size, string sha256, long lines)
    {
        Number = number;
        Name = name;
        Size = size;
        Sha256 = sha256;
        Lines = lines;
    }

    /// <summary>Part number (1-based).</summary>
    public int Number { get; }

    /// <summary>File or stream name.</summary>
    public string Name { get; }

    /// <summary>Size in bytes.</summary>
    public long Size { get; }

    /// <summary>SHA-256 of the bytes as read.</summary>
    public string Sha256 { get; }

    /// <summary>Number of lines.</summary>
    public long Lines { get; }
}
