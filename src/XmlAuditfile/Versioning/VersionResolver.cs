using XmlAuditfile.Definitions;
using XmlAuditfile.Diagnostics;

namespace XmlAuditfile.Versioning;

/// <summary>The outcome of version resolution.</summary>
internal sealed class Resolution(AuditfileVersion version, VersionSource source, string fileNamespace, Dialect? dialect)
{
    public AuditfileVersion Version { get; } = version;

    public VersionSource Source { get; } = source;

    public string FileNamespace { get; } = fileNamespace;

    public Dialect? Dialect { get; private set; } = dialect;

    /// <summary>
    /// Recognizes a dialect by the software description in the header, when none was recognized by namespace.
    /// Returns true when a dialect was found.
    /// </summary>
    public bool UseSoftware(string software, bool applyDialects)
    {
        if (Dialect is not null || !applyDialects)
        {
            return false;
        }

        Dialect = Version.Dialects.FirstOrDefault(d => d.SoftwareDescription == software);
        return Dialect is not null;
    }

    public bool NeedsMapping => !string.Equals(FileNamespace, Version.Namespace, StringComparison.Ordinal);
}

/// <summary>
/// Determines the version: forced by the caller, then the registry (official namespace, alias or dialect),
/// then optionally the resolver. Anything else is fatal.
/// </summary>
internal static class VersionResolver
{
    private static readonly char[] Separators = { '/', ':', '#' };

    public static Resolution? Resolve(string rootName, string fileNamespace, ReadOptions options, DiagnosticCollector diagnostics)
    {
        var resolution = Find(fileNamespace, options, diagnostics);
        if (resolution is null)
        {
            diagnostics.Add("XAF-V001", Location.None, ("namespace", fileNamespace));
            return null;
        }

        var version = resolution.Version;
        if (!string.Equals(rootName, version.RootElement, StringComparison.Ordinal))
        {
            diagnostics.Add("XAF-V005", Location.None, ("element", rootName), ("expected", version.RootElement));
            return null;
        }

        if (!version.HasOfficialXsd)
        {
            diagnostics.Add("XAF-V006", Location.None, ("version", version.Key));
        }

        if (resolution.Dialect?.Corrections.FirstOrDefault(c => c.Action == DialectAction.ReplaceNamespace && c.From == fileNamespace) is { } replace)
        {
            diagnostics.Add(replace.Code, Location.None, ("from", fileNamespace), ("to", version.Namespace));
        }

        return resolution;
    }

    private static Resolution? Find(string fileNamespace, ReadOptions options, DiagnosticCollector diagnostics)
    {
        if (options.Version is { } forced)
        {
            if (!string.Equals(fileNamespace, forced.Namespace, StringComparison.Ordinal))
            {
                diagnostics.Add("XAF-V003", Location.None, ("version", forced.Key), ("namespace", fileNamespace));
            }

            return new(forced, VersionSource.Forced, fileNamespace, FindDialect(forced, fileNamespace, options));
        }

        foreach (var version in AuditfileVersion.All)
        {
            if (string.Equals(version.Namespace, fileNamespace, StringComparison.Ordinal))
            {
                return new(version, VersionSource.Official, fileNamespace, null);
            }
        }

        foreach (var version in AuditfileVersion.All)
        {
            var dialect = FindDialect(version, fileNamespace, options);
            if (version.Aliases.Contains(fileNamespace) || dialect is not null)
            {
                diagnostics.Add("XAF-V002", Location.None, ("namespace", fileNamespace), ("version", version.Key));
                return new(version, VersionSource.Alias, fileNamespace, dialect);
            }
        }

        if (options.UseResolver && Derive(fileNamespace) is { } derived)
        {
            diagnostics.Add("XAF-V004", Location.None, ("version", derived.Key), ("namespace", fileNamespace));
            return new(derived, VersionSource.Resolved, fileNamespace, null);
        }

        return null;
    }

    private static Dialect? FindDialect(AuditfileVersion version, string fileNamespace, ReadOptions options) =>
        options.ApplyDialects ? version.Dialects.FirstOrDefault(d => d.Namespaces.Contains(fileNamespace)) : null;

    /// <summary>
    /// Derives type and version from the namespace segments, e.g. ".../XAF/3.2/" or ".../xaf/3_2".
    /// Returns a version only when exactly one matches.
    /// </summary>
    private static AuditfileVersion? Derive(string fileNamespace)
    {
        var segments = fileNamespace.ToLowerInvariant().Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        var candidates = AuditfileVersion.All.Where(v =>
            segments.Contains(v.Type) &&
            segments.Any(s => Digits(s).Length > 0 && Digits(s) == Digits(v.Version) && s.All(c => char.IsDigit(c) || c == '.' || c == '_')))
            .ToList();

        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static string Digits(string value) => new(value.Where(char.IsDigit).ToArray());
}
