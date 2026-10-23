using XmlAuditfile.Auditfile;
using XmlAuditfile.Auditfile.Definitions;

// Validates an audit file: XSD, structure, keys and business rules.
// Usage: Validate <file.xaf> [--en] [--unverified] [--force xaf/3.2]
// Exit code: 0 valid, 1 not valid, 2 usage error.

var forceIndex = Array.IndexOf(args, "--force");
var files = args.Where((a, index) => !a.StartsWith("--", StringComparison.Ordinal) && (forceIndex < 0 || index != forceIndex + 1)).ToArray();
if (files.Length == 0)
{
    Console.Error.WriteLine("Usage: Validate <file.xaf> [--en] [--unverified] [--force xaf/3.2]");
    return 2;
}

var force = forceIndex >= 0 && forceIndex + 1 < args.Length ? AuditfileVersion.Parse(args[forceIndex + 1]) : null;
var options = new ReadOptions
{
    Language = args.Contains("--en") ? TextLanguage.English : TextLanguage.Dutch,
    Mode = args.Contains("--unverified") ? ReadMode.Unverified : ReadMode.Verified,
    Version = force,
};

// The reader never opens files itself: hand it a stream factory per part.
var source = AuditfileSource.FromFactories(files.Select(path => (Path.GetFileName(path), (Func<Stream>)(() => File.OpenRead(path)))).ToArray());

AuditfileReport report;
if (options.Mode == ReadMode.Verified)
{
    report = await Auditfile.ValidateAsync(source, options);
}
else
{
    // Unverified: stream through all rows so every check runs, without keeping anything.
    var reader = Auditfile.Open(source, options);
    foreach (var _ in reader.ReadRows())
    {
    }

    report = reader.Report;
}

foreach (var diagnostic in report.Diagnostics)
{
    Console.WriteLine(diagnostic);
}

Console.WriteLine($"{report.Count(DiagnosticSeverity.Error) + report.Count(DiagnosticSeverity.Fatal)} errors, {report.Count(DiagnosticSeverity.Warning)} warnings");
return report.IsValid ? 0 : 1;
