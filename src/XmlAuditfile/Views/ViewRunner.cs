using System.Globalization;
using XmlAuditfile.Definitions;
using XmlAuditfile.Diagnostics;
using XmlAuditfile.Internal;
using XmlAuditfile.Reading;

namespace XmlAuditfile;

/// <summary>
/// Runs a view over rows from any store. An input row is the source row plus one slot per join;
/// joins are left joins, so a missing ancestor or lookup leaves its columns null.
/// </summary>
internal static class ViewRunner
{
    private const int MaximumDiagnostics = 100;

    public static ViewResult Run(
        ViewDefinition view,
        AuditfileSchema schema,
        Func<string, IEnumerable<AuditfileRow>> rows,
        IReadOnlyDictionary<string, object?>? parameters,
        TextLanguage language)
    {
        var diagnostics = new DiagnosticCollector(language, MaximumDiagnostics);
        var slots = new List<string> { view.Source };
        slots.AddRange(view.Joins.Select(j => j.Table));

        if (FindMissing(view, schema, slots) is { } missing)
        {
            diagnostics.Add("XAF-Q001", Location.None, ("view", view.Name), ("standard", schema.VersionKey), ("missing", missing));
            return new ViewResult(view, false, Array.Empty<IReadOnlyList<object?>>(), diagnostics.Items);
        }

        Ref Resolve(string reference)
        {
            var (table, column) = Split(reference);
            return new Ref(slots.IndexOf(table), schema.Table(table).FindColumn(column)!);
        }

        var filters = BuildFilters(view, parameters, Resolve);
        var columns = view.Columns.Select(c => new Output(c, c.Value is null ? null : new Expression(c.Value, Resolve))).ToList();
        var joins = BuildJoins(view, schema, rows, Resolve, diagnostics);

        var inputs = new List<AuditfileRow?[]>();
        foreach (var source in rows(view.Source))
        {
            var input = new AuditfileRow?[slots.Count];
            input[0] = source;
            for (var i = 0; i < joins.Count; i++)
            {
                input[i + 1] = joins[i](input);
            }

            if (filters.All(f => f(input)))
            {
                inputs.Add(input);
            }
        }

        var result = view.IsAggregated ? Aggregate(columns, inputs) : inputs.Select(i => columns.Select(c => c.Expression!.Evaluate(i)).ToArray()).ToList();
        var order = view.OrderBy.Select(name => columns.FindIndex(c => c.Column.Name == name)).Where(i => i >= 0).ToList();
        IReadOnlyList<IReadOnlyList<object?>> sorted = order.Count == 0
            ? result
            : result.OrderBy(r => r, Comparer<object?[]>.Create((a, b) =>
            {
                foreach (var index in order)
                {
                    var compared = Compare(a[index], b[index]);
                    if (compared != 0)
                    {
                        return compared;
                    }
                }

                return 0;
            })).ToList();

        return new ViewResult(view, true, sorted, diagnostics.Items);
    }

    /// <summary>Returns the first table or "table.column" the view needs but the schema lacks, or null.</summary>
    private static string? FindMissing(ViewDefinition view, AuditfileSchema schema, List<string> slots)
    {
        foreach (var table in slots)
        {
            if (schema.FindTable(table) is null)
            {
                return table;
            }
        }

        foreach (var join in view.Joins.Where(j => j.On.Count == 0))
        {
            if (AncestorPath(schema, view.Source, join.Table) is null)
            {
                return join.Table;
            }
        }

        var references = view.Joins.SelectMany(j => j.On).SelectMany(c => new[] { c.Left, c.Right })
            .Concat(view.Parameters.Select(p => p.Column))
            .Concat(view.Columns.Where(c => c.Value is not null).SelectMany(c => new[] { c.Value!.Column, c.Value.By }))
            .OfType<string>();
        foreach (var reference in references)
        {
            var (table, column) = Split(reference);
            if (!slots.Contains(table) || schema.FindTable(table)?.FindColumn(column) is null)
            {
                return reference;
            }
        }

        return null;
    }

    /// <summary>Tables from the parent of <paramref name="source"/> up to <paramref name="ancestor"/>, or null when it is no ancestor.</summary>
    private static List<string>? AncestorPath(AuditfileSchema schema, string source, string ancestor)
    {
        var path = new List<string>();
        for (var table = schema.FindTable(source)?.Parent; table is not null; table = schema.FindTable(table)?.Parent)
        {
            path.Add(table);
            if (table == ancestor)
            {
                return path;
            }
        }

        return null;
    }

    private static List<Func<AuditfileRow?[], bool>> BuildFilters(ViewDefinition view, IReadOnlyDictionary<string, object?>? parameters, Func<string, Ref> resolve)
    {
        var filters = new List<Func<AuditfileRow?[], bool>>();
        foreach (var pair in parameters ?? new Dictionary<string, object?>())
        {
            var parameter = view.Parameters.FirstOrDefault(p => p.Name == pair.Key)
                ?? throw new ArgumentException($"View '{view.Name}' has no parameter '{pair.Key}'.", nameof(parameters));
            if (pair.Value is null)
            {
                continue;
            }

            var reference = resolve(parameter.Column);
            var value = Convert(pair.Value, reference.Column, parameter.Name);
            Func<int, bool> test = parameter.Operator switch
            {
                "=" => c => c == 0,
                "<" => c => c < 0,
                "<=" => c => c <= 0,
                ">" => c => c > 0,
                ">=" => c => c >= 0,
                _ => throw new NotSupportedException($"View '{view.Name}': operator '{parameter.Operator}' is not supported."),
            };
            filters.Add(input => reference.Get(input) is { } cell && test(Compare(cell, value)));
        }

        return filters;
    }

    private static List<Func<AuditfileRow?[], AuditfileRow?>> BuildJoins(
        ViewDefinition view, AuditfileSchema schema, Func<string, IEnumerable<AuditfileRow>> rows, Func<string, Ref> resolve, DiagnosticCollector diagnostics)
    {
        // One index by id over every table between the source and its joined ancestors.
        var ancestorTables = view.Joins.Where(j => j.On.Count == 0).SelectMany(j => AncestorPath(schema, view.Source, j.Table)!).Distinct().ToList();
        var byId = new Dictionary<long, AuditfileRow>();
        foreach (var row in ancestorTables.SelectMany(rows))
        {
            byId[row.Id] = row;
        }

        var joins = new List<Func<AuditfileRow?[], AuditfileRow?>>();
        foreach (var join in view.Joins)
        {
            if (join.On.Count == 0)
            {
                joins.Add(input =>
                {
                    for (var id = input[0]!.ParentId; id is not null && byId.TryGetValue(id.Value, out var row); id = row.ParentId)
                    {
                        if (row.Table.Name == join.Table)
                        {
                            return row;
                        }
                    }

                    return null;
                });
                continue;
            }

            var left = join.On.Select(c => resolve(c.Left)).ToList();
            var right = join.On.Select(c => resolve(c.Right)).ToList();
            var index = new Dictionary<string, AuditfileRow>(StringComparer.Ordinal);
            foreach (var row in rows(join.Table))
            {
                var cells = new AuditfileRow?[right.Max(r => r.Slot) + 1];
                cells[right[0].Slot] = row;
                if (Key(right, cells) is not { } key)
                {
                    continue;
                }

                if (index.ContainsKey(key))
                {
                    diagnostics.Add("XAF-Q002", Location.None, ("view", view.Name), ("table", join.Table), ("key", CompositeKey.Display(key)));
                }
                else
                {
                    index[key] = row;
                }
            }

            joins.Add(input => Key(left, input) is { } key && index.TryGetValue(key, out var match) ? match : null);
        }

        return joins;
    }

    private static string? Key(List<Ref> references, AuditfileRow?[] input)
    {
        var parts = new string[references.Count];
        for (var i = 0; i < parts.Length; i++)
        {
            if (references[i].Get(input) is not { } value)
            {
                return null;
            }

            parts[i] = ValueConverter.ToText(value);
        }

        return CompositeKey.Join(parts);
    }

    /// <summary>The values of the grouping columns; "=" marks a value, so null and an empty string differ.</summary>
    private static string GroupKey(List<Output> columns, object?[] values)
    {
        var parts = new List<string>();
        for (var i = 0; i < columns.Count; i++)
        {
            if (columns[i].Column.Aggregate is null)
            {
                parts.Add(values[i] is { } value ? "=" + ValueConverter.ToText(value) : "");
            }
        }

        return CompositeKey.Join(parts);
    }

    private static List<object?[]> Aggregate(List<Output> columns, List<AuditfileRow?[]> inputs)
    {
        var groups = new Dictionary<string, (object?[] Values, Aggregator?[] Aggregators)>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var input in inputs)
        {
            var values = columns.Select(c => c.Expression?.Evaluate(input)).ToArray();
            var key = GroupKey(columns, values);

            if (!groups.TryGetValue(key, out var group))
            {
                group = (values, columns.Select(c => c.Column.Aggregate is null ? null : new Aggregator(c)).ToArray());
                groups[key] = group;
                order.Add(key);
            }

            for (var i = 0; i < columns.Count; i++)
            {
                group.Aggregators[i]?.Add(values[i]);
            }
        }

        return order.Select(key =>
        {
            var (values, aggregators) = groups[key];
            return values.Select((v, i) => aggregators[i] is { } aggregator ? aggregator.Result : v).ToArray();
        }).ToList();
    }

    private static (string Table, string Column) Split(string reference)
    {
        var dot = reference.IndexOf('.');
        return dot < 0 ? (reference, "") : (reference.Substring(0, dot), reference.Substring(dot + 1));
    }

    private static object Convert(object value, SchemaColumn column, string parameter)
    {
        try
        {
            return column.Type switch
            {
                ColumnType.Decimal => System.Convert.ToDecimal(value, CultureInfo.InvariantCulture),
                ColumnType.Integer => System.Convert.ToInt64(value, CultureInfo.InvariantCulture),
                ColumnType.Date or ColumnType.DateTime => value as DateTime? ?? DateTime.Parse(ValueConverter.ToText(value), CultureInfo.InvariantCulture),
                ColumnType.Time => value as TimeSpan? ?? TimeSpan.Parse(ValueConverter.ToText(value), CultureInfo.InvariantCulture),
                ColumnType.Boolean => System.Convert.ToBoolean(value, CultureInfo.InvariantCulture),
                _ => ValueConverter.ToText(value),
            };
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or OverflowException)
        {
            throw new ArgumentException($"Parameter '{parameter}': '{value}' is not a valid {column.Type.ToString().ToLowerInvariant()}.", parameter, e);
        }
    }

    /// <summary>Orders null first, numbers by value, strings ordinal.</summary>
    private static int Compare(object? a, object? b)
    {
        if (a is null || b is null)
        {
            if (a is null)
            {
                return b is null ? 0 : -1;
            }

            return 1;
        }

        if (a is string sa && b is string sb)
        {
            return string.CompareOrdinal(sa, sb);
        }

        if (a.GetType() == b.GetType() && a is IComparable comparable)
        {
            return comparable.CompareTo(b);
        }

        return a is decimal or long && b is decimal or long
            ? System.Convert.ToDecimal(a, CultureInfo.InvariantCulture).CompareTo(System.Convert.ToDecimal(b, CultureInfo.InvariantCulture))
            : string.CompareOrdinal(ValueConverter.ToText(a), ValueConverter.ToText(b));
    }

    /// <summary>A resolved "table.column": the input slot and the column.</summary>
    private readonly struct Ref(int slot, SchemaColumn column)
    {
        public int Slot { get; } = slot;

        public SchemaColumn Column { get; } = column;

        public object? Get(AuditfileRow?[] input) => input[Slot]?.Values[Column.Index];
    }

    private sealed class Output(ViewColumn column, Expression? expression)
    {
        public ViewColumn Column { get; } = column;

        public Expression? Expression { get; } = expression;
    }

    private sealed class Expression
    {
        private readonly ViewValue _value;
        private readonly Ref _column;
        private readonly Ref? _by;

        public Expression(ViewValue value, Func<string, Ref> resolve)
        {
            _value = value;
            _column = resolve(value.Column);
            _by = value.By is null ? null : resolve(value.By);
        }

        public ColumnType Type => _column.Column.Type;

        public object? Evaluate(AuditfileRow?[] input)
        {
            var cell = _column.Get(input);
            if (_value.Operator == ViewOperator.Column || cell is null)
            {
                return cell;
            }

            var code = _by?.Get(input) is { } by ? ValueConverter.ToText(by) : null;
            var listed = code is not null && _value.Codes.Contains(code);
            if (_value.Operator == ViewOperator.Only)
            {
                return listed ? cell : null;
            }

            return listed ? Negate(cell) : cell;
        }

        private static object Negate(object value) => value switch
        {
            decimal d => -d,
            long l => -l,
            _ => value,
        };
    }

    private sealed class Aggregator
    {
        private readonly ViewAggregate _kind;
        private readonly bool _countRows;
        private long _count;
        private decimal _sum;
        private bool _decimal;
        private object? _extreme;

        public Aggregator(Output output)
        {
            _kind = output.Column.Aggregate!.Value;
            _countRows = output.Expression is null;
            _decimal = output.Expression?.Type == ColumnType.Decimal;
        }

        public object? Result => _kind switch
        {
            ViewAggregate.Count => _count,
            ViewAggregate.Sum => _decimal ? _sum : (long)_sum,
            _ => _extreme,
        };

        public void Add(object? value)
        {
            if (value is null && !_countRows)
            {
                return;
            }

            _count++;
            switch (_kind)
            {
                case ViewAggregate.Sum:
                    _decimal |= value is decimal;
                    _sum += System.Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                    break;
                case ViewAggregate.Min when _extreme is null || Compare(value, _extreme) < 0:
                case ViewAggregate.Max when _extreme is null || Compare(value, _extreme) > 0:
                    _extreme = value;
                    break;
            }
        }
    }
}
