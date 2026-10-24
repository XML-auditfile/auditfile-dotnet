using XmlAuditfile.Memory;
using Xunit;

namespace XmlAuditfile.Tests;

/// <summary>Typed accessors check the column type and name the problem, instead of a null or an InvalidCastException.</summary>
public class RowAccessorTests
{
    private static async Task<MemorySink> Read()
    {
        var sink = new MemorySink();
        await Auditfile.ReadAsync(TestFiles.Source(TestFiles.Sample), sink, cancellationToken: TestContext.Current.CancellationToken);
        return sink;
    }

    [Fact]
    public async Task TypedAccessorsReturnTheValue()
    {
        var line = (await Read()).Rows("tr_line")[0];

        Assert.Equal(327.00m, line.GetDecimal("amnt"));
        Assert.Equal("D", line.GetString("amnt_tp"));
        Assert.Equal(new DateTime(2025, 1, 15), line.GetDate("eff_date"));
        Assert.Equal(new DateTime(2025, 1, 15), line.GetDateTime("eff_date"));
    }

    [Fact]
    public async Task ColumnsLookedUpOnceGiveTheSameValues()
    {
        var sink = await Read();
        var amount = sink.Rows("tr_line")[0].Table.FindColumn("amnt")!;

        Assert.Equal(sink.Rows("tr_line").Select(r => r.GetDecimal("amnt")), sink.Rows("tr_line").Select(r => r.GetDecimal(amount)));
        Assert.Equal(sink.Rows("tr_line")[0]["amnt"], sink.Rows("tr_line")[0][amount]);
    }

    [Fact]
    public async Task AWrongTypeIsNamed()
    {
        var line = (await Read()).Rows("tr_line")[0];

        var error = Assert.Throws<InvalidOperationException>(() => line.GetInteger("amnt"));
        Assert.Contains("tr_line.amnt is a decimal column", error.Message, StringComparison.Ordinal);
        Assert.Contains("GetDecimal", error.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => line.GetDecimal("amnt_tp"));
    }

    [Fact]
    public async Task AnUnknownColumnIsNamedAndTryGetValueSaysSo()
    {
        var line = (await Read()).Rows("tr_line")[0];

        var error = Assert.Throws<KeyNotFoundException>(() => line.GetDecimal("amount"));
        Assert.Contains("'amount'", error.Message, StringComparison.Ordinal);
        Assert.False(line.TryGetValue("amount", out var missing));
        Assert.Null(missing);
        Assert.True(line.TryGetValue("amnt", out var amount));
        Assert.Equal(327.00m, amount);
    }

    [Fact]
    public async Task AColumnOfAnotherTableIsRefused()
    {
        var sink = await Read();
        var header = sink.Rows("header")[0];
        var amount = sink.Rows("tr_line")[0].Table.FindColumn("amnt")!;

        Assert.Throws<ArgumentException>(() => header.GetDecimal(amount));
    }
}
