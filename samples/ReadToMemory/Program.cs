using XmlAuditfile.Auditfile;
using XmlAuditfile.Auditfile.Memory;

// Reads an audit file into memory and prints what was found.
// Usage: ReadToMemory <file.xaf> [more parts...]

if (args.Length == 0)
{
    Console.WriteLine("Usage: ReadToMemory <file.xaf> [continuation files...]");
    return 2;
}

var sink = new MemorySink();
// The reader never opens files itself: hand it a stream factory per part.
var source = AuditfileSource.FromFactories(args.Select(path => (Path.GetFileName(path), (Func<Stream>)(() => File.OpenRead(path)))).ToArray());
var report = await Auditfile.ReadAsync(source, sink);

Console.WriteLine($"Version  : {report.Schema?.VersionKey ?? "-"} ({report.Schema?.Source})");
foreach (var part in report.Parts)
{
    Console.WriteLine($"Part {part.Number}   : {part.Name}, {part.Size} bytes, sha256 {part.Sha256[..12]}…");
}

Console.WriteLine($"Loaded   : {report.Loaded} in {report.Duration.TotalMilliseconds:0} ms");
Console.WriteLine();
foreach (var table in report.Schema?.Tables ?? [])
{
    if (report.RowCounts.TryGetValue(table.Name, out var count))
    {
        Console.WriteLine($"  {table.Name,-40} {count,8}");
    }
}

if (sink.Rows("tr_line") is { Count: > 0 } lines && lines[0] is var line)
{
    Console.WriteLine();
    Console.WriteLine("First transaction line:");
    foreach (var column in line.Table.Columns.Where(c => line.Values[c.Index] is not null))
    {
        var label = column.Definition?.Label?.Get(XmlAuditfile.Auditfile.Definitions.TextLanguage.English) ?? column.Name;
        Console.WriteLine($"  {column.Name,-20} {line.Values[column.Index],-14} {label}");
    }
}

Console.WriteLine();
foreach (var diagnostic in report.Diagnostics)
{
    Console.WriteLine(diagnostic);
}

Console.WriteLine(report.IsValid ? "Valid." : "Not valid.");
return report.IsValid ? 0 : 1;
