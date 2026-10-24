namespace XmlAuditfile;

/// <summary>Severity of a diagnostic.</summary>
public enum DiagnosticSeverity
{
    /// <summary>Information only.</summary>
    Info,

    /// <summary>Something to look at; the result is still usable.</summary>
    Warning,

    /// <summary>The file does not meet the standard.</summary>
    Error,

    /// <summary>Reading stopped.</summary>
    Fatal,
}

/// <summary>A finding about an audit file, with a stable code from auditfile-spec.</summary>
public sealed class Diagnostic
{
    internal Diagnostic(string code, string layer, DiagnosticSeverity severity, string message, int? part, int? line, int? position, string? path)
    {
        Code = code;
        Layer = layer;
        Severity = severity;
        Message = message;
        Part = part;
        Line = line;
        Position = position;
        Path = path;
    }

    /// <summary>Code, e.g. "XAF-R001".</summary>
    public string Code { get; }

    /// <summary>"xml", "version", "structure", "xsd", "rules", "dialect" or "view".</summary>
    public string Layer { get; }

    /// <summary>Severity.</summary>
    public DiagnosticSeverity Severity { get; }

    /// <summary>Message in the requested language.</summary>
    public string Message { get; }

    /// <summary>Source part (1-based), when known.</summary>
    public int? Part { get; }

    /// <summary>Line number, when known.</summary>
    public int? Line { get; }

    /// <summary>Position on the line, when known.</summary>
    public int? Position { get; }

    /// <summary>XML path, when known.</summary>
    public string? Path { get; }

    /// <inheritdoc />
    public override string ToString() =>
        Line is null ? $"{Code} {Severity}: {Message}" : $"{Code} {Severity} (line {Line}): {Message}";
}
