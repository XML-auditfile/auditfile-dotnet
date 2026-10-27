using System.Text;
using XmlAuditfile.Definitions;
using XmlAuditfile.Diagnostics;
using XmlAuditfile.Internal;

namespace XmlAuditfile;

/// <summary>
/// Dialects added at runtime, available as <see cref="Auditfile.Dialects"/>. They are used like the built-in dialects
/// of the version: recognized by namespace or header/softwareDesc, unless <see cref="ReadOptions.ApplyDialects"/> is false.
/// Thread-safe; a read that has started keeps the dialects it found.
/// </summary>
public sealed class DialectRegistry
{
    private readonly CustomList<Dialect> _dialects = new(d => d.Name);

    internal DialectRegistry()
    {
    }

    /// <summary>
    /// Adds the dialects of a file in the format of dialects.json of auditfile-spec: <c>{ "formatVersion": 1, "dialects": [...] }</c>.
    /// The whole file is refused when any dialect is invalid or has a name the version already knows.
    /// </summary>
    /// <param name="version">The version the dialects are for, e.g. <c>AuditfileVersion.Financial.V3_2</c>.</param>
    /// <param name="json">The JSON text.</param>
    /// <returns>The added dialects.</returns>
    /// <exception cref="FormatException">The JSON is invalid.</exception>
    /// <exception cref="ArgumentException">A dialect name is taken.</exception>
    public IReadOnlyList<Dialect> Add(AuditfileVersion version, string json)
    {
        Guard.NotNull(version, nameof(version));
        var added = CustomDefinitions.Parse(json, "Dialects").Objects("dialects").Select(d => new Dialect(d)).ToList();
        var codes = new HashSet<string>(DiagnosticCollector.DialectCodes, StringComparer.Ordinal);
        foreach (var correction in added.SelectMany(d => d.Corrections.Select(c => (Dialect: d, Correction: c))))
        {
            if (!codes.Contains(correction.Correction.Code))
            {
                throw new FormatException(
                    $"Dialect '{correction.Dialect.Name}': {correction.Correction.Code} is not a dialect diagnostic ({string.Join(", ", codes)}).");
            }
        }

        _dialects.Add(version.Key, added, () => version.Dialects.Select(d => d.Name), "Dialect");
        return added;
    }

    /// <summary>Adds the dialects of a UTF-8 JSON stream; see <see cref="Add(AuditfileVersion, string)"/>. The stream is not closed.</summary>
    public IReadOnlyList<Dialect> Add(AuditfileVersion version, Stream json) => Add(version, CustomDefinitions.ReadText(json));

    /// <summary>Removes an added dialect. Built-in dialects cannot be removed.</summary>
    /// <returns>True when the dialect was found and removed.</returns>
    public bool Remove(AuditfileVersion version, string name) =>
        _dialects.Remove(Guard.NotNull(version, nameof(version)).Key, Guard.NotNull(name, nameof(name)));

    /// <summary>Removes all added dialects, of every version.</summary>
    public void Clear() => _dialects.Clear();

    internal IReadOnlyList<Dialect> For(AuditfileVersion version) => _dialects.For(version.Key);
}

/// <summary>
/// Views added at runtime, available as <see cref="Auditfile.Views"/>. They appear in <see cref="AuditfileVersion.Views"/>
/// and <see cref="AuditfileVersion.FindView"/> of every version of their type, after the built-in views. Thread-safe.
/// </summary>
public sealed class ViewRegistry
{
    private readonly CustomList<ViewDefinition> _views = new(v => v.Name);

    internal ViewRegistry()
    {
    }

    /// <summary>
    /// Adds the views of a file in the format of views/&lt;type&gt;.json of auditfile-spec:
    /// <c>{ "formatVersion": 1, "type": "xaf", "views": [...] }</c>. The whole file is refused when any view is invalid
    /// or has a name the type already knows. Whether a view fits a version is decided when it runs (XAF-Q001).
    /// </summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The added views.</returns>
    /// <exception cref="FormatException">The JSON is invalid.</exception>
    /// <exception cref="ArgumentException">The type is unknown or a view name is taken.</exception>
    public IReadOnlyList<ViewDefinition> Add(string json)
    {
        var parsed = CustomDefinitions.Parse(json, "Views");
        var type = parsed.String("type");
        var version = AuditfileVersion.All.FirstOrDefault(v => v.Type == type)
            ?? throw new ArgumentException($"Unknown audit file type '{type}'.", nameof(json));
        var added = parsed.Objects("views").Select(v => new ViewDefinition(v)).ToList();
        _views.Add(type, added, () => version.Views.Select(v => v.Name), "View");
        return added;
    }

    /// <summary>Adds the views of a UTF-8 JSON stream; see <see cref="Add(string)"/>. The stream is not closed.</summary>
    public IReadOnlyList<ViewDefinition> Add(Stream json) => Add(CustomDefinitions.ReadText(json));

    /// <summary>Removes an added view. Built-in views cannot be removed.</summary>
    /// <param name="type">Audit file type, e.g. "xaf".</param>
    /// <param name="name">View name.</param>
    /// <returns>True when the view was found and removed.</returns>
    public bool Remove(string type, string name) => _views.Remove(Guard.NotNull(type, nameof(type)), Guard.NotNull(name, nameof(name)));

    /// <summary>Removes all added views, of every type.</summary>
    public void Clear() => _views.Clear();

    internal IReadOnlyList<ViewDefinition> For(string type) => _views.For(type);
}

internal static class CustomDefinitions
{
    public static JsonObject Parse(string json, string what)
    {
        var parsed = MiniJson.Parse(Guard.NotNull(json, nameof(json))) as JsonObject
            ?? throw new FormatException($"{what} must be a JSON object.");
        var format = parsed.OptionalInt("formatVersion");
        if (format is not null and not 1)
        {
            throw new FormatException($"{what}: formatVersion {format} is not supported; this reader reads format 1.");
        }

        return parsed;
    }

    public static string ReadText(Stream json)
    {
        using var reader = new StreamReader(Guard.NotNull(json, nameof(json)), Encoding.UTF8, detectEncodingFromByteOrderMarks: true, 4096, leaveOpen: true);
        return reader.ReadToEnd();
    }
}

/// <summary>
/// Added definitions by version key or type. The lists are replaced on every change, never modified, so a caller that
/// already took a list keeps a consistent one.
/// </summary>
internal sealed class CustomList<T>(Func<T, string> nameOf)
{
    private readonly object _lock = new();
    private readonly Dictionary<string, IReadOnlyList<T>> _lists = new(StringComparer.Ordinal);

    public IReadOnlyList<T> For(string key)
    {
        lock (_lock)
        {
            return _lists.TryGetValue(key, out var list) ? list : Array.Empty<T>();
        }
    }

    public void Add(string key, IReadOnlyList<T> added, Func<IEnumerable<string>> existing, string what)
    {
        lock (_lock)
        {
            // Under the lock, so two files with the same name cannot both pass.
            var names = new HashSet<string>(existing(), StringComparer.Ordinal);
            foreach (var name in added.Select(nameOf))
            {
                if (!names.Add(name))
                {
                    throw new ArgumentException($"{what} '{name}' already exists for {key}.");
                }
            }

            _lists[key] = [.. For(key), .. added];
        }
    }

    public bool Remove(string key, string name)
    {
        lock (_lock)
        {
            var list = For(key);
            var kept = list.Where(d => nameOf(d) != name).ToList();
            if (kept.Count == list.Count)
            {
                return false;
            }

            _lists[key] = kept;
            return true;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _lists.Clear();
        }
    }
}
