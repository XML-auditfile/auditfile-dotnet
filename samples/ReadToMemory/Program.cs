using System.Globalization;
using XmlAuditfile.Auditfile;
using XmlAuditfile.Auditfile.Definitions;
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
        var label = column.Definition?.Label?.Get(TextLanguage.English) ?? column.Name;
        Console.WriteLine($"  {column.Name,-20} {line.Values[column.Index],-14} {label}");
    }
}

// Views are reports defined by auditfile-spec, e.g. the trial balance; they run over the rows in memory.
foreach (var view in report.Loaded ? report.Schema?.Version?.Views ?? [] : [])
{
    var result = sink.Run(view, language: TextLanguage.English);
    Console.WriteLine();
    Console.WriteLine($"{view.Label?.Get(TextLanguage.English) ?? view.Name} ({result.Rows.Count} rows)");
    foreach (var diagnostic in result.Diagnostics)
    {
        Console.WriteLine($"  {diagnostic}");
    }

    PrintTable(result, maxRows: 10);
}

Console.WriteLine();
foreach (var diagnostic in report.Diagnostics)
{
    Console.WriteLine(diagnostic);
}

Console.WriteLine(report.IsValid ? "Valid." : "Not valid.");
return report.IsValid ? 0 : 1;

static void PrintTable(ViewResult result, int maxRows)
{
    const int maxWidth = 30;
    var header = result.Columns.Select(c => c.Label?.Get(TextLanguage.English) ?? c.Name).ToArray();
    var cells = result.Rows.Take(maxRows)
        .Select(row => row.Select(v => v switch
        {
            null => "",
            DateTime d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => v.ToString() ?? "",
        }).ToArray())
        .ToList();
    var widths = header.Select((h, i) => Math.Min(maxWidth, cells.Select(r => r[i].Length).Prepend(h.Length).Max())).ToArray();
    var numeric = result.Columns.Select((_, i) => result.Rows.Any(r => r[i] is decimal or long)).ToArray();

    string Line(string[] values) => "  " + string.Join("  ", values.Select((v, i) =>
    {
        v = v.Length > widths[i] ? v[..(widths[i] - 1)] + "…" : v;
        return numeric[i] ? v.PadLeft(widths[i]) : v.PadRight(widths[i]);
    }));

    Console.WriteLine(Line(header));
    foreach (var row in cells)
    {
        Console.WriteLine(Line(row));
    }

    if (result.Rows.Count > maxRows)
    {
        Console.WriteLine($"  … {result.Rows.Count - maxRows} more");
    }
}
