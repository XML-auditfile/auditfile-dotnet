using System.Globalization;
using System.Text;

namespace XmlAuditfile.Auditfile.Benchmarks;

/// <summary>Builds a valid XAF 3.2 file with a given number of balanced transactions (two lines each).</summary>
internal static class SyntheticAuditfile
{
    public static byte[] Create(int transactions)
    {
        var lines = transactions * 2;
        var total = (transactions * 100m).ToString("0.00", CultureInfo.InvariantCulture);
        var xml = new StringBuilder(transactions * 400);
        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<auditfile xmlns=\"http://www.auditfiles.nl/XAF/3.2\">")
           .Append("<header><fiscalYear>2025</fiscalYear><startDate>2025-01-01</startDate><endDate>2025-12-31</endDate><curCode>EUR</curCode><dateCreated>2026-01-15</dateCreated></header>")
           .Append("<company><companyName>Voorbeeld BV</companyName><taxRegistrationCountry>NL</taxRegistrationCountry><taxRegIdent>NL001</taxRegIdent>")
           .Append("<generalLedger><ledgerAccount><accID>1000</accID><accDesc>Kas</accDesc><accTp>B</accTp></ledgerAccount>")
           .Append("<ledgerAccount><accID>8000</accID><accDesc>Omzet</accDesc><accTp>P</accTp></ledgerAccount></generalLedger>")
           .Append("<periods><period><periodNumber>1</periodNumber><startDatePeriod>2025-01-01</startDatePeriod><endDatePeriod>2025-12-31</endDatePeriod></period></periods>")
           .Append(CultureInfo.InvariantCulture, $"<transactions><linesCount>{lines}</linesCount><totalDebit>{total}</totalDebit><totalCredit>{total}</totalCredit>")
           .Append("<journal><jrnID>V</jrnID><desc>Verkoop</desc>\n");

        for (var i = 1; i <= transactions; i++)
        {
            xml.Append(CultureInfo.InvariantCulture, $"<transaction><nr>{i}</nr><periodNumber>1</periodNumber><trDt>2025-03-01</trDt>")
               .Append(CultureInfo.InvariantCulture, $"<trLine><nr>1</nr><accID>1000</accID><docRef>F{i}</docRef><effDate>2025-03-01</effDate><amnt>100.00</amnt><amntTp>D</amntTp></trLine>")
               .Append(CultureInfo.InvariantCulture, $"<trLine><nr>2</nr><accID>8000</accID><docRef>F{i}</docRef><effDate>2025-03-01</effDate><amnt>100.00</amnt><amntTp>C</amntTp></trLine>")
               .Append("</transaction>\n");
        }

        xml.Append("</journal></transactions></company></auditfile>\n");
        return new UTF8Encoding(false).GetBytes(xml.ToString());
    }
}
