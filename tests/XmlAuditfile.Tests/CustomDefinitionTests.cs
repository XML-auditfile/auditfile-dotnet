using System.Text;
using System.Text.RegularExpressions;
using XmlAuditfile.Memory;
using Xunit;

namespace XmlAuditfile.Tests;

/// <summary>The registries are shared by the process, so these tests never run next to tests that read files.</summary>
[CollectionDefinition(nameof(CustomDefinitionTests), DisableParallelization = true)]
public sealed class CustomDefinitionsRunAlone;

/// <summary>Dialects and views added at runtime, in the format of auditfile-spec, next to the built-in ones.</summary>
[Collection(nameof(CustomDefinitionTests))]
public sealed class CustomDefinitionTests : IDisposable
{
    private const string DeviatingNamespace = "http://www.auditfiles.nl/XAF/3.2/";

    private const string DialectsJson = $$"""
        {
          "formatVersion": 1,
          "dialects": [
            {
              "name": "test-package",
              "description": { "nl": "Testpakket met afwijkende namespace en decimale komma.", "en": "Test package with a deviating namespace and decimal comma." },
              "match": { "namespaces": [ "{{DeviatingNamespace}}" ] },
              "corrections": [
                { "code": "XAF-C001", "action": "replaceNamespace", "from": "{{DeviatingNamespace}}", "to": "{{TestFiles.OfficialNamespace}}" },
                { "code": "XAF-C002", "action": "decimalComma", "paths": [ "auditfile/company/transactions/journal/transaction/trLine/amnt" ] }
              ]
            }
          ]
        }
        """;

    private const string ViewsJson = """
        {
          "formatVersion": 1,
          "type": "xaf",
          "views": [
            {
              "name": "lines_per_account",
              "label": { "nl": "Regels per rekening", "en": "Lines per account" },
              "source": "tr_line",
              "columns": [
                { "name": "account", "value": "tr_line.acc_id" },
                { "name": "lines", "aggregate": "count" }
              ],
              "orderBy": [ "account" ]
            },
            {
              "name": "needs_unknown_table",
              "source": "no_such_table",
              "columns": [ { "name": "value", "value": "no_such_table.value" } ]
            }
          ]
        }
        """;

    private static readonly ReadOptions Unverified = new() { Mode = ReadMode.Unverified };

    /// <summary>The sample in the deviating namespace, with decimal commas in the amounts of the transaction lines.</summary>
    private static readonly string Deviating = Regex.Replace(
        TestFiles.SampleText.Replace($"xmlns=\"{TestFiles.OfficialNamespace}\"", $"xmlns=\"{DeviatingNamespace}\"", StringComparison.Ordinal),
        @"<trLine>[\s\S]*?</trLine>",
        line => Regex.Replace(line.Value, @"<amnt>(\d+)\.(\d+)</amnt>", "<amnt>$1,$2</amnt>"));

    public void Dispose()
    {
        Auditfile.Dialects.Clear();
        Auditfile.Views.Clear();
    }

    [Fact]
    public async Task AnAddedViewIsFoundAndRunsLikeABuiltInView()
    {
        var added = Auditfile.Views.Add(ViewsJson);

        var version = AuditfileVersion.Financial.V3_2;
        Assert.Equal(["lines_per_account", "needs_unknown_table"], added.Select(v => v.Name));
        Assert.Same(added[0], version.FindView("lines_per_account"));
        Assert.NotNull(version.FindView("trial_balance"));
        Assert.Same(added[0], AuditfileVersion.Financial.V4_0_3.FindView("lines_per_account"));
        Assert.Null(AuditfileVersion.Payroll.V2027_1_0.FindView("lines_per_account"));

        var sink = new MemorySink();
        await Auditfile.ReadAsync(FromText(TestFiles.SampleText), sink, Unverified, TestContext.Current.CancellationToken);
        var result = sink.Run(version.FindView("lines_per_account")!);

        Assert.True(result.IsAvailable);
        Assert.Empty(result.Diagnostics);
        var lines = sink.Rows("tr_line");
        Assert.Equal(lines.Count, result.Rows.Sum(r => Convert.ToInt64(r[1], System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Equal(lines.Select(l => l.GetString("acc_id")).Distinct().Order(StringComparer.Ordinal), result.Rows.Select(r => (string?)r[0]));

        var unavailable = sink.Run(version.FindView("needs_unknown_table")!);
        Assert.False(unavailable.IsAvailable);
        Assert.Equal("XAF-Q001", unavailable.Diagnostics[0].Code);
    }

    [Fact]
    public void ViewsCanBeAddedFromAStreamAndRemovedAgain()
    {
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(ViewsJson)))
        {
            Auditfile.Views.Add(stream);
        }

        Assert.True(Auditfile.Views.Remove("xaf", "lines_per_account"));
        Assert.False(Auditfile.Views.Remove("xaf", "lines_per_account"));
        Assert.False(Auditfile.Views.Remove("xaf", "trial_balance"));
        Assert.Null(AuditfileVersion.Financial.V3_2.FindView("lines_per_account"));
        Assert.NotNull(AuditfileVersion.Financial.V3_2.FindView("needs_unknown_table"));
    }

    [Fact]
    public async Task AnAddedDialectIsRecognizedByNamespaceAndCorrectsValues()
    {
        var token = TestContext.Current.CancellationToken;
        var before = await Auditfile.ValidateAsync(FromText(Deviating), Unverified, token);
        Assert.Equal("XAF-V001", before.Diagnostics[0].Code);

        var dialect = Assert.Single(Auditfile.Dialects.Add(AuditfileVersion.Financial.V3_2, DialectsJson));
        Assert.Contains(dialect, AuditfileVersion.Financial.V3_2.Dialects);

        var sink = new MemorySink();
        var report = await Auditfile.ReadAsync(FromText(Deviating), sink, Unverified, token);

        Assert.True(report.Loaded, string.Join("\n", report.Diagnostics));
        Assert.Equal("test-package", report.Schema!.Dialect);
        var codes = report.Diagnostics.Select(d => d.Code).ToList();
        Assert.Contains("XAF-V002", codes);
        Assert.Contains("XAF-C001", codes);
        Assert.Contains("XAF-C002", codes);
        Assert.DoesNotContain("XAF-S004", codes);
        var first = Regex.Match(TestFiles.SampleText, @"<trLine>[\s\S]*?<amnt>([^<]+)</amnt>").Groups[1].Value;
        Assert.Equal(decimal.Parse(first, System.Globalization.CultureInfo.InvariantCulture), sink.Rows("tr_line")[0].GetDecimal("amnt"));

        var ignored = await Auditfile.ValidateAsync(FromText(Deviating), new ReadOptions { Mode = ReadMode.Unverified, ApplyDialects = false }, token);
        Assert.Equal("XAF-V001", ignored.Diagnostics[0].Code);
    }

    [Fact]
    public async Task ARemovedOrClearedDialectNoLongerApplies()
    {
        var version = AuditfileVersion.Financial.V3_2;
        Auditfile.Dialects.Add(version, DialectsJson);
        Assert.True(Auditfile.Dialects.Remove(version, "test-package"));
        Assert.False(Auditfile.Dialects.Remove(version, "test-package"));

        Auditfile.Dialects.Add(version, DialectsJson);
        Auditfile.Dialects.Clear();

        var report = await Auditfile.ValidateAsync(FromText(Deviating), Unverified, TestContext.Current.CancellationToken);
        Assert.Equal("XAF-V001", report.Diagnostics[0].Code);
    }

    [Fact]
    public void InvalidFilesAreRefusedAsAWhole()
    {
        var version = AuditfileVersion.Financial.V3_2;
        var secondView = ViewsJson.Replace("needs_unknown_table", "{0}", StringComparison.Ordinal);

        Assert.Throws<FormatException>(() => Auditfile.Views.Add("{ not json"));
        Assert.Throws<FormatException>(() => Auditfile.Views.Add("[]"));
        Assert.Throws<ArgumentNullException>(() => Auditfile.Views.Add((string)null!));
        Assert.Throws<FormatException>(() => Auditfile.Views.Add(ViewsJson.Replace("\"formatVersion\": 1", "\"formatVersion\": 2", StringComparison.Ordinal)));
        Assert.Throws<FormatException>(() => Auditfile.Views.Add(ViewsJson.Replace("\"type\": \"xaf\",", "", StringComparison.Ordinal)));
        Assert.Throws<ArgumentException>(() => Auditfile.Views.Add(ViewsJson.Replace("\"type\": \"xaf\"", "\"type\": \"xyz\"", StringComparison.Ordinal)));
        Assert.Throws<ArgumentException>(() => Auditfile.Views.Add(secondView.Replace("{0}", "trial_balance", StringComparison.Ordinal)));
        Assert.Throws<ArgumentException>(() => Auditfile.Views.Add(secondView.Replace("{0}", "lines_per_account", StringComparison.Ordinal)));
        Assert.Throws<FormatException>(() => Auditfile.Views.Add(ViewsJson.Replace("\"count\"", "\"median\"", StringComparison.Ordinal)));
        Assert.Null(version.FindView("lines_per_account"));

        Assert.Throws<ArgumentNullException>(() => Auditfile.Dialects.Add(null!, DialectsJson));
        Assert.Throws<FormatException>(() => Auditfile.Dialects.Add(version, DialectsJson.Replace("\"decimalComma\"", "\"guess\"", StringComparison.Ordinal)));
        Assert.Throws<FormatException>(() => Auditfile.Dialects.Add(version, DialectsJson.Replace("XAF-C002", "XAF-R001", StringComparison.Ordinal)));
        Auditfile.Dialects.Add(version, DialectsJson);
        Assert.Throws<ArgumentException>(() => Auditfile.Dialects.Add(version, DialectsJson));
        Assert.Single(version.Dialects);
    }

    private static AuditfileSource FromText(string text) =>
        AuditfileSource.FromFactory(() => new MemoryStream(Encoding.UTF8.GetBytes(text)), "custom.xaf");
}
