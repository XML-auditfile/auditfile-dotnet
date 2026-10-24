using XmlAuditfile.Definitions;
using XmlAuditfile.Reading;
using Xunit;

namespace XmlAuditfile.Tests;

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

    [Theory]
    [InlineData("18:30:00")]
    [InlineData("18:30:00+01:00")]
    [InlineData("18:30:00Z")]
    [InlineData("18:30:00.250")]
    public void TimeIgnoresTheFractionAndTimeZone(string text)
    {
        Assert.True(ValueConverter.TryConvert(text, ColumnType.Time, out var value));
        Assert.Equal(new TimeSpan(18, 30, 0), value);
    }

    [Theory]
    [InlineData("2017-01-03T10:00:00", DateTimeKind.Unspecified)]
    [InlineData("2017-01-03T10:00:00.5", DateTimeKind.Unspecified)]
    [InlineData("2017-01-03T10:00:00Z", DateTimeKind.Utc)]
    public void DateTimesUseXsdNotation(string text, DateTimeKind kind)
    {
        Assert.True(ValueConverter.TryConvert(text, ColumnType.DateTime, out var value));
        var dateTime = Assert.IsType<DateTime>(value);
        Assert.Equal(new DateTime(2017, 1, 3), dateTime.Date);
        Assert.Equal(kind, dateTime.Kind);
    }

    [Theory]
    [InlineData("03/01/2017 10:00:00", ColumnType.DateTime)]
    [InlineData("2017-01-03 10:00:00", ColumnType.DateTime)]
    [InlineData("2017-01-03", ColumnType.DateTime)]
    [InlineData("2017-01-03T10:00", ColumnType.DateTime)]
    [InlineData("18:30", ColumnType.Time)]
    [InlineData("18:30:00 pm", ColumnType.Time)]
    [InlineData("18:30:00.", ColumnType.Time)]
    public void OtherNotationsAreRejected(string text, ColumnType type)
    {
        Assert.False(ValueConverter.TryConvert(text, type, out _));
    }
}
