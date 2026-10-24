using XmlAuditfile.Definitions;

namespace XmlAuditfile;

/// <summary>The outcome of running a <see cref="ViewDefinition"/>.</summary>
public sealed class ViewResult
{
    internal ViewResult(ViewDefinition view, bool isAvailable, IReadOnlyList<IReadOnlyList<object?>> rows, IReadOnlyList<Diagnostic> diagnostics)
    {
        View = view;
        IsAvailable = isAvailable;
        Rows = rows;
        Diagnostics = diagnostics;
    }

    /// <summary>The view that was run.</summary>
    public ViewDefinition View { get; }

    /// <summary>Output columns; each row has one value per column, in this order.</summary>
    public IReadOnlyList<ViewColumn> Columns => View.Columns;

    /// <summary>False when the version lacks a table or column the view needs (XAF-Q001); there are no rows then.</summary>
    public bool IsAvailable { get; }

    /// <summary>Result rows, sorted by <see cref="ViewDefinition.OrderBy"/>.</summary>
    public IReadOnlyList<IReadOnlyList<object?>> Rows { get; }

    /// <summary>Findings while running the view.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
}
