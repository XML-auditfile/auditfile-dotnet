using XmlAuditfile.Definitions;
using XmlAuditfile.Memory;
using Xunit;

namespace XmlAuditfile.Tests;

public class ViewTests
{
    // Two journals, two periods; account 1000 is debited 350 and credited 100.
    private const string Xaf = """
        <?xml version="1.0" encoding="UTF-8"?>
        <auditfile xmlns="http://www.auditfiles.nl/XAF/3.2"><header><fiscalYear>2025</fiscalYear><startDate>2025-01-01</startDate><endDate>2025-12-31</endDate><curCode>EUR</curCode><dateCreated>2026-01-15</dateCreated></header>
        <company><companyName>Voorbeeld BV</companyName><taxRegistrationCountry>NL</taxRegistrationCountry><taxRegIdent>NL001</taxRegIdent>
        <generalLedger><ledgerAccount><accID>1000</accID><accDesc>Kas</accDesc><accTp>B</accTp></ledgerAccount><ledgerAccount><accID>4000</accID><accDesc>Huur</accDesc><accTp>P</accTp></ledgerAccount><ledgerAccount><accID>8000</accID><accDesc>Omzet</accDesc><accTp>P</accTp></ledgerAccount></generalLedger>
        <periods><period><periodNumber>1</periodNumber><startDatePeriod>2025-01-01</startDatePeriod><endDatePeriod>2025-06-30</endDatePeriod></period><period><periodNumber>2</periodNumber><startDatePeriod>2025-07-01</startDatePeriod><endDatePeriod>2025-12-31</endDatePeriod></period></periods>
        <transactions><linesCount>6</linesCount><totalDebit>450.00</totalDebit><totalCredit>450.00</totalCredit>
        <journal><jrnID>V</jrnID><desc>Verkoop</desc>
        <transaction><nr>1</nr><periodNumber>1</periodNumber><trDt>2025-03-01</trDt><trLine><nr>1</nr><accID>1000</accID><docRef>F1</docRef><effDate>2025-03-01</effDate><amnt>100.00</amnt><amntTp>D</amntTp></trLine><trLine><nr>2</nr><accID>8000</accID><docRef>F1</docRef><effDate>2025-03-01</effDate><amnt>100.00</amnt><amntTp>C</amntTp></trLine></transaction>
        <transaction><nr>2</nr><periodNumber>2</periodNumber><trDt>2025-09-01</trDt><trLine><nr>1</nr><accID>1000</accID><docRef>F2</docRef><effDate>2025-09-01</effDate><amnt>250.00</amnt><amntTp>D</amntTp></trLine><trLine><nr>2</nr><accID>8000</accID><docRef>F2</docRef><effDate>2025-09-01</effDate><amnt>250.00</amnt><amntTp>C</amntTp></trLine></transaction>
        </journal>
        <journal><jrnID>M</jrnID><desc>Memoriaal</desc>
        <transaction><nr>3</nr><periodNumber>2</periodNumber><trDt>2025-10-01</trDt><trLine><nr>1</nr><accID>4000</accID><docRef>H1</docRef><effDate>2025-10-01</effDate><amnt>100.00</amnt><amntTp>D</amntTp></trLine><trLine><nr>2</nr><accID>1000</accID><docRef>H1</docRef><effDate>2025-10-01</effDate><amnt>100.00</amnt><amntTp>C</amntTp></trLine></transaction>
        </journal></transactions></company></auditfile>
        """;

    [Fact]
    public async Task TrialBalanceSumsDebitCreditAndBalancePerAccount()
    {
        var sink = await Load(Xaf);

        var result = sink.Run(View("trial_balance"));

        Assert.True(result.IsAvailable);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(["1000", "4000", "8000"], result.Rows.Select(r => r[0]));
        Assert.Equal(new object?[] { "1000", "Kas", "B", 350.00m, 100.00m, 250.00m }, result.Rows[0]);
        Assert.Equal(-350.00m, result.Rows[2][5]);
    }

    [Fact]
    public async Task ParametersFilterTheInputRows()
    {
        var sink = await Load(Xaf);

        var result = sink.Run(View("trial_balance"), new Dictionary<string, object?> { ["period_from"] = 2, ["date_to"] = "2025-09-30", ["period_to"] = null });

        Assert.Equal(["1000", "8000"], result.Rows.Select(r => r[0]));
        Assert.Equal(250.00m, result.Rows[0][5]);
    }

    [Fact]
    public async Task AncestorJoinsReachThroughTheParentChain()
    {
        var sink = await Load(Xaf);

        var result = sink.Run(View("journal_totals"));

        var memoriaal = result.Rows[0];
        Assert.Equal("M", memoriaal[0]);
        Assert.Equal(2L, memoriaal[3]);
        Assert.Equal(0.00m, memoriaal[6]);
    }

    [Fact]
    public async Task UnknownParameterIsRejected()
    {
        var sink = await Load(Xaf);

        Assert.Throws<ArgumentException>(() => sink.Run(View("trial_balance"), new Dictionary<string, object?> { ["year"] = 2025 }));
    }

    [Fact]
    public async Task ViewOfAnotherTypeIsNotAvailable()
    {
        var sink = await Load(Xaf);
        var payroll = AuditfileVersion.All.First(v => v.Type == "xas").FindView("hours")!;

        var result = sink.Run(payroll);

        Assert.False(result.IsAvailable);
        Assert.Empty(result.Rows);
        Assert.Equal("XAF-Q001", Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task DuplicateLookupKeyUsesTheFirstRowWithAWarning()
    {
        // Loading rejects duplicate keys, so the store hands out a ledger account twice: the first copy last.
        var sink = await Load(Xaf);
        var huur = sink.Rows("ledger_account")[1];
        IEnumerable<AuditfileRow> Rows(string table) => table == "ledger_account" ? sink.Rows(table).Append(huur) : sink.Rows(table);

        var result = View("trial_balance").Run(sink.Schema!, Rows);

        Assert.Equal("Huur", result.Rows[1][1]);
        Assert.Equal("XAF-Q002", Assert.Single(result.Diagnostics).Code);
    }

    private static ViewDefinition View(string name) => AuditfileVersion.Financial.V3_2.FindView(name)!;

    private static async Task<MemorySink> Load(string xml)
    {
        var sink = new MemorySink();
        var bytes = System.Text.Encoding.UTF8.GetBytes(xml);
        var report = await Auditfile.ReadAsync(AuditfileSource.FromFactory(() => new MemoryStream(bytes), "views.xaf"), sink, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(report.Loaded);
        return sink;
    }
}
