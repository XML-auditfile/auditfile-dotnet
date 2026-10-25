using XmlAuditfile.Internal;

namespace XmlAuditfile.Definitions;

/// <summary>
/// A view from views/&lt;type&gt;.json: a report over the rows of an import, such as a trial balance.
/// Run it with <see cref="Run"/> on any store that can hand out the rows of a table.
/// </summary>
public sealed class ViewDefinition
{
    internal ViewDefinition(JsonObject json)
    {
        Name = json.String("name");
        Label = LocalizedText.From(json.Object("label"));
        Description = LocalizedText.From(json.Object("description"));
        Source = json.String("source");
        Joins = json.Objects("joins").Select(j => new ViewJoin(j)).ToList();
        Parameters = json.Objects("parameters").Select(p => new ViewParameter(p)).ToList();
        Columns = json.Objects("columns").Select(c => new ViewColumn(c)).ToList();
        OrderBy = json.Strings("orderBy").ToList();
    }

    /// <summary>View name, e.g. "trial_balance".</summary>
    public string Name { get; }

    /// <summary>Display name.</summary>
    public LocalizedText? Label { get; }

    /// <summary>What the view shows.</summary>
    public LocalizedText? Description { get; }

    /// <summary>The table with one input row per row of the view (before grouping).</summary>
    public string Source { get; }

    /// <summary>Tables joined to the source: an ancestor of the source, or a lookup on key columns.</summary>
    public IReadOnlyList<ViewJoin> Joins { get; }

    /// <summary>Optional filters.</summary>
    public IReadOnlyList<ViewParameter> Parameters { get; }

    /// <summary>Output columns. When any column aggregates, the other columns group.</summary>
    public IReadOnlyList<ViewColumn> Columns { get; }

    /// <summary>Output column names to sort by, ascending.</summary>
    public IReadOnlyList<string> OrderBy { get; }

    /// <summary>True when at least one column aggregates.</summary>
    public bool IsAggregated => Columns.Any(c => c.Aggregate is not null);

    /// <summary>
    /// Runs the view. <paramref name="rows"/> returns the rows of a table by name.
    /// <paramref name="parameters"/> holds filter values by parameter name; null values are ignored.
    /// </summary>
    /// <exception cref="ArgumentException">A parameter is unknown or its value cannot be converted.</exception>
    public ViewResult Run(
        AuditfileSchema schema,
        Func<string, IEnumerable<AuditfileRow>> rows,
        IReadOnlyDictionary<string, object?>? parameters = null,
        TextLanguage language = TextLanguage.Dutch) =>
        ViewRunner.Run(this, Guard.NotNull(schema, nameof(schema)), Guard.NotNull(rows, nameof(rows)), parameters, language);

    /// <inheritdoc />
    public override string ToString() => Name;

    internal static IReadOnlyList<ViewDefinition> Load(string type)
    {
        var name = $"views/{type}.json";
        if (!EmbeddedResources.Exists(name))
        {
            return Array.Empty<ViewDefinition>();
        }

        var json = MiniJson.Parse(EmbeddedResources.ReadText(name)) as JsonObject
            ?? throw new FormatException($"{name} is not a JSON object.");
        return json.Objects("views").Select(v => new ViewDefinition(v)).ToList();
    }
}

/// <summary>A table joined to the source of a view.</summary>
public sealed class ViewJoin
{
    internal ViewJoin(JsonObject json)
    {
        Table = json.String("table");
        On = json.TryGetValue("on", out var on) && on is List<object?> pairs
            ? pairs.OfType<List<object?>>().Select(p => new ViewJoinCondition((string)p[0]!, (string)p[1]!)).ToList()
            : new List<ViewJoinCondition>();
        Alternatives = json.Strings("alternatives").ToList();
    }

    /// <summary>Table name, as the view refers to it.</summary>
    public string Table { get; }

    /// <summary>
    /// Names of the same table in versions that call it differently: when a version lacks <see cref="Table"/>,
    /// the first alternative it has is joined.
    /// </summary>
    public IReadOnlyList<string> Alternatives { get; }

    /// <summary>Equality conditions; empty means <see cref="Table"/> is an ancestor of the source.</summary>
    public IReadOnlyList<ViewJoinCondition> On { get; }
}

/// <summary>One equality condition of a join, as "table.column" references.</summary>
public sealed class ViewJoinCondition
{
    internal ViewJoinCondition(string left, string right)
    {
        Left = left;
        Right = right;
    }

    /// <summary>Column of the source or an earlier join.</summary>
    public string Left { get; }

    /// <summary>Column of the joined table.</summary>
    public string Right { get; }
}

/// <summary>An optional filter of a view.</summary>
public sealed class ViewParameter
{
    internal ViewParameter(JsonObject json)
    {
        Name = json.String("name");
        Label = LocalizedText.From(json.Object("label"));
        Column = json.String("column");
        Operator = json.String("operator");
    }

    /// <summary>Parameter name, e.g. "period_from".</summary>
    public string Name { get; }

    /// <summary>Display name.</summary>
    public LocalizedText? Label { get; }

    /// <summary>The filtered column, as "table.column".</summary>
    public string Column { get; }

    /// <summary>"=", "&lt;", "&lt;=", "&gt;" or "&gt;=".</summary>
    public string Operator { get; }
}

/// <summary>An output column of a view.</summary>
public sealed class ViewColumn
{
    internal ViewColumn(JsonObject json)
    {
        Name = json.String("name");
        Label = LocalizedText.From(json.Object("label"));
        Aggregate = DefinitionNames.ParseAggregate(json.OptionalString("aggregate"));
        Value = json.TryGetValue("value", out var value) ? ViewValue.From(value) : null;
    }

    /// <summary>Column name.</summary>
    public string Name { get; }

    /// <summary>Display name.</summary>
    public LocalizedText? Label { get; }

    /// <summary>The aggregate, or null for a grouping column.</summary>
    public ViewAggregate? Aggregate { get; }

    /// <summary>The value per input row; null for a row count.</summary>
    public ViewValue? Value { get; }
}

/// <summary>How a view column gets its value from an input row.</summary>
public sealed class ViewValue
{
    private ViewValue(ViewOperator op, string column, string? by, IReadOnlyList<string> codes, string? prefix)
    {
        Operator = op;
        Column = column;
        By = by;
        Codes = codes;
        Prefix = prefix;
    }

    /// <summary>How the value is taken from <see cref="Column"/>.</summary>
    public ViewOperator Operator { get; }

    /// <summary>The value column, as "table.column".</summary>
    public string Column { get; }

    /// <summary>The column holding the code, as "table.column".</summary>
    public string? By { get; }

    /// <summary>Codes for "only" (in) or "signed" (negative).</summary>
    public IReadOnlyList<string> Codes { get; }

    /// <summary>Placeholder prefix for "pseudonym", e.g. "REL" for REL-1, REL-2, ...</summary>
    public string? Prefix { get; }

    internal static ViewValue From(object? json) => json switch
    {
        string column => new ViewValue(ViewOperator.Column, column, null, Array.Empty<string>(), null),
        JsonObject op => new ViewValue(
            DefinitionNames.ParseOperator(op.String("op")), op.String("value"), op.OptionalString("by"),
            op.Strings("in").Concat(op.Strings("negative")).ToList(), op.OptionalString("prefix")),
        _ => throw new FormatException("Embedded JSON: invalid view column value."),
    };
}
