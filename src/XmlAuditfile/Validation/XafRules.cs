using System.Globalization;
using XmlAuditfile.Diagnostics;

namespace XmlAuditfile.Validation;

/// <summary>
/// Business rules for Auditfile Financieel (XAF):
/// every transaction balances, and the counts and totals in the headers match the lines.
/// Rules whose tables or columns do not exist in a version are skipped. Columns are looked up once, not per row.
/// </summary>
internal sealed class XafRules : IRowObserver
{
    /// <summary>Audit file type these rules apply to.</summary>
    public const string Type = "xaf";

    private const string Amount = "amnt";
    private const string AmountType = "amnt_tp";
    private const string Debit = "D";
    private const string Credit = "C";

    private readonly DiagnosticCollector _diagnostics;
    private readonly Dictionary<long, Totals> _transactions = new();
    private readonly List<TotalsRule> _rules = new();
    private readonly Lines? _lines;
    private readonly SchemaTable? _transaction;
    private readonly SchemaColumn? _transactionNumber;

    public XafRules(AuditfileSchema schema, DiagnosticCollector diagnostics)
    {
        _diagnostics = diagnostics;
        _lines = Lines.Find(schema.FindTable("tr_line"));
        _transaction = schema.FindTable("transaction");
        _transactionNumber = _transaction?.FindColumn("nr");

        AddRule(schema, "tr_line", "company", perParent: false, "transactions_lines_count", "transactions_total_debit", "transactions_total_credit");
        AddRule(schema, "ob_line", "company", perParent: false, "opening_balance_lines_count", "opening_balance_total_debit", "opening_balance_total_credit");
        AddRule(schema, "sb_line", "subledger", perParent: true, "lines_count", "total_debit", "total_credit");
        AddRule(schema, "ob_sb_line", "ob_subledger", perParent: true, "lines_count", "total_debit", "total_credit");
    }

    public void OnRow(AuditfileRow row)
    {
        if (_lines is not null && row.Table == _lines.Table && row.ParentId is { } parent)
        {
            Get(_transactions, parent).Add(row, _lines);
        }
        else if (row.Table == _transaction && _transactions.TryGetValue(row.Id, out var lines))
        {
            _transactions.Remove(row.Id);
            if (lines.Debit != lines.Credit)
            {
                _diagnostics.Add("XAF-R001", new Location(row.Part, null, null, row.Table.Path),
                    ("transaction", _transactionNumber is null ? null : row[_transactionNumber]), ("debit", lines.Debit), ("credit", lines.Credit));
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
        var lines = Lines.Find(schema.FindTable(child));
        var ownerTable = schema.FindTable(owner);
        if (lines is null || ownerTable?.FindColumn(count) is not { Type: ColumnType.Integer } countColumn
            || ownerTable.FindColumn(debit) is not { Type: ColumnType.Decimal } debitColumn
            || ownerTable.FindColumn(credit) is not { Type: ColumnType.Decimal } creditColumn)
        {
            return;
        }

        _rules.Add(new TotalsRule(lines, ownerTable, perParent, countColumn, debitColumn, creditColumn));
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

    /// <summary>A table of lines with an amount and a debit/credit indicator.</summary>
    private sealed class Lines(SchemaTable table, SchemaColumn amount, SchemaColumn amountType)
    {
        public SchemaTable Table { get; } = table;

        public SchemaColumn Amount { get; } = amount;

        public SchemaColumn AmountType { get; } = amountType;

        public static Lines? Find(SchemaTable? table) =>
            table?.FindColumn(XafRules.Amount) is { Type: ColumnType.Decimal or ColumnType.Integer } amount
            && table.FindColumn(XafRules.AmountType) is { Type: ColumnType.String } amountType
                ? new Lines(table, amount, amountType)
                : null;
    }

    private sealed class Totals
    {
        public long Count { get; private set; }

        public decimal Debit { get; private set; }

        public decimal Credit { get; private set; }

        public void Add(AuditfileRow row, Lines lines)
        {
            Count++;
            if (row.GetDecimal(lines.Amount) is not { } value)
            {
                return;
            }

            switch (row.GetString(lines.AmountType))
            {
                case XafRules.Debit:
                    Debit += value;
                    break;
                case XafRules.Credit:
                    Credit += value;
                    break;
            }
        }
    }

    private sealed class TotalsRule(Lines lines, SchemaTable owner, bool perParent, SchemaColumn count, SchemaColumn debit, SchemaColumn credit)
    {
        private readonly Dictionary<long, Totals> _byParent = new();
        private readonly Totals _all = new();

        public void OnRow(AuditfileRow row, DiagnosticCollector diagnostics)
        {
            if (row.Table == lines.Table)
            {
                var totals = perParent && row.ParentId is { } parent ? Get(_byParent, parent) : _all;
                totals.Add(row, lines);
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

            Compare(row, count.Name, row.GetInteger(count), actual.Count, diagnostics);
            Compare(row, debit.Name, row.GetDecimal(debit), actual.Debit, diagnostics);
            Compare(row, credit.Name, row.GetDecimal(credit), actual.Credit, diagnostics);
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
