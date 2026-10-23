using XmlAuditfile.Auditfile.Definitions;
using XmlAuditfile.Auditfile.Reading;
using Xunit;

namespace XmlAuditfile.Auditfile.Tests;

public class ValueConverterTests
{
    [Theory]
    [InlineData("123.45", 123.45)]
    [InlineData("+123.4567", 123.4567)]
    [InlineData("-123", -123)]
    public void DecimalsUseXsdNotation(string text, double expected)
    {
        Assert.True(ValueConverter.TryConvert(text, ColumnType.Decimal, out var value));
        Assert.Equal((decimal)expected, value);
    }

    [Theory]
    [InlineData("123,45")]
    [InlineData("1.234,56")]
    [InlineData("+ 123")]
    public void LocalNotationIsRejected(string text)
    {
        Assert.False(ValueConverter.TryConvert(text, ColumnType.Decimal, out _));
    }

    [Theory]
    [InlineData("2007-12-25")]
    [InlineData("2007-12-25+01:00")]
    [InlineData("2007-12-25Z")]
    public void DatesMayHaveATimeZone(string text)
    {
        Assert.True(ValueConverter.TryConvert(text, ColumnType.Date, out var value));
        Assert.Equal(new DateTime(2007, 12, 25), value);
    }

    [Fact]
    public void EmptyTextIsNullForTypedColumns()
    {
        Assert.True(ValueConverter.TryConvert("", ColumnType.Decimal, out var value));
        Assert.Null(value);
    }

    [Fact]
    public void TimeIgnoresTheTimeZone()
    {
        Assert.True(ValueConverter.TryConvert("18:30:00+01:00", ColumnType.Time, out var value));
        Assert.Equal(new TimeSpan(18, 30, 0), value);
    }
}
