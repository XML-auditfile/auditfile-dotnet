using System.Globalization;
using XmlAuditfile.Auditfile.Diagnostics;

namespace XmlAuditfile.Auditfile.Validation;

/// <summary>
/// Business rules for Auditfile Financieel (XAF):
/// every transaction balances, and the counts and totals in the headers match the lines.
/// Rules whose tables or columns do not exist in a version are skipped.
/// </summary>
internal sealed class XafRules : IRowObserver
{
    private readonly DiagnosticCollector _diagnostics;
    private readonly Dictionary<long, Totals> _transactions = new();
    private readonly List<TotalsRule> _rules = new();
    private readonly SchemaTable? _line;
    private readonly SchemaTable? _transaction;

    public XafRules(AuditfileSchema schema, DiagnosticCollector diagnostics)
    {
        _diagnostics = diagnostics;
        _line = schema.FindTable("tr_line");
        _transaction = schema.FindTable("transaction");

        AddRule(schema, "tr_line", "company", perParent: false, "transactions_lines_count", "transactions_total_debit", "transactions_total_credit");
        AddRule(schema, "ob_line", "company", perParent: false, "opening_balance_lines_count", "opening_balance_total_debit", "opening_balance_total_credit");
        AddRule(schema, "sb_line", "subledger", perParent: true, "lines_count", "total_debit", "total_credit");
        AddRule(schema, "ob_sb_line", "ob_subledger", perParent: true, "lines_count", "total_debit", "total_credit");
    }

    public void OnRow(AuditfileRow row)
    {
        if (row.Table == _line && row.ParentId is { } parent)
        {
            var totals = Get(_transactions, parent);
            totals.Add(row["amnt"] as decimal?, row["amnt_tp"] as string);
        }
        else if (row.Table == _transaction && _transactions.TryGetValue(row.Id, out var lines))
        {
            _transactions.Remove(row.Id);
            if (lines.Debit != lines.Credit)
            {
                _diagnostics.Add("XAF-R001", new Location(row.Part, null, null, row.Table.Path),
                    ("transaction", row["nr"]), ("debit", lines.Debit), ("credit", lines.Credit));
            }
        }

        foreach (var rule in _rules)
        {
            rule.OnRow(row, _diagnostics);
        }
    }

    public void OnEnd()
    {
    }

    private void AddRule(AuditfileSchema schema, string child, string owner, bool perParent, string count, string debit, string credit)
    {
        var childTable = schema.FindTable(child);
        var ownerTable = schema.FindTable(owner);
        if (childTable?.FindColumn("amnt") is null || childTable.FindColumn("amnt_tp") is null || ownerTable?.FindColumn(count) is null)
        {
            return;
        }

        _rules.Add(new TotalsRule(childTable, ownerTable, perParent, count, debit, credit));
    }

    private static Totals Get(Dictionary<long, Totals> map, long key)
    {
        if (!map.TryGetValue(key, out var totals))
        {
            totals = new Totals();
            map[key] = totals;
        }

        return totals;
    }

    private sealed class Totals
    {
        public long Count { get; private set; }

        public decimal Debit { get; private set; }

        public decimal Credit { get; private set; }

        public void Add(decimal? amount, string? type)
        {
            Count++;
            if (amount is not { } value)
            {
                return;
            }

            if (type == "D")
            {
                Debit += value;
            }
            else if (type == "C")
            {
                Credit += value;
            }
        }
    }

    private sealed class TotalsRule(SchemaTable child, SchemaTable owner, bool perParent, string count, string debit, string credit)
    {
        private readonly Dictionary<long, Totals> _byParent = new();
        private readonly Totals _all = new();

        public void OnRow(AuditfileRow row, DiagnosticCollector diagnostics)
        {
            if (row.Table == child)
            {
                var totals = perParent && row.ParentId is { } parent ? Get(_byParent, parent) : _all;
                totals.Add(row["amnt"] as decimal?, row["amnt_tp"] as string);
                return;
            }

            if (row.Table != owner)
            {
                return;
            }

            Totals actual;
            if (perParent)
            {
                _byParent.TryGetValue(row.Id, out var found);
                _byParent.Remove(row.Id);
                actual = found ?? new Totals();
            }
            else
            {
                actual = _all;
            }

            Compare(row, count, row[count] as long?, actual.Count, diagnostics);
            Compare(row, debit, row[debit] as decimal?, actual.Debit, diagnostics);
            Compare(row, credit, row[credit] as decimal?, actual.Credit, diagnostics);
        }

        private static void Compare<T>(AuditfileRow row, string field, T? expected, T actual, DiagnosticCollector diagnostics)
            where T : struct, IFormattable
        {
            if (expected is { } value && !value.Equals(actual))
            {
                diagnostics.Add("XAF-R002", new Location(row.Part, null, null, row.Table.Path),
                    ("field", field), ("table", row.Table.Name),
                    ("expected", value.ToString(null, CultureInfo.InvariantCulture)), ("actual", actual.ToString(null, CultureInfo.InvariantCulture)));
            }
        }
    }
}
