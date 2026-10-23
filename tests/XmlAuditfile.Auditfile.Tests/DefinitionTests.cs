using System.Reflection;
using Xunit;

namespace XmlAuditfile.Auditfile.Tests;

public class DefinitionTests
{
    private static readonly string[] RequiredTables = { "auditfile", "header", "company" };

    [Fact]
    public void EveryRegistryEntryHasAConstantAndViceVersa()
    {
        var constants = typeof(AuditfileVersion).GetNestedTypes()
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Static))
            .Where(p => p.PropertyType == typeof(AuditfileVersion))
            .Select(p => ((AuditfileVersion)p.GetValue(null)!).Key)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        var registry = AuditfileVersion.All.Select(v => v.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();

        Assert.Equal(registry, constants);
    }

    [Fact]
    public void Xaf32ManifestHasTheExpectedStructure()
    {
        var manifest = AuditfileVersion.Financial.V3_2.Manifest;

        Assert.Equal(31, manifest.Tables.Count);
        Assert.Equal(RequiredTables, manifest.RequiredTables);
        Assert.Equal("transaction", manifest.FindTable("tr_line")!.Parent);
        Assert.Contains(manifest.FindTable("tr_line")!.References, r => r.Table == "ledger_account");
    }

    [Fact]
    public void Xaf32XsdMatchesItsChecksumAndCompiles()
    {
        var version = AuditfileVersion.Financial.V3_2;

        Assert.Equal(version.Manifest.XsdSha256, AuditfileVersion.Sha256(version.OpenXsd()));
        Assert.Equal(1, version.CreateSchemaSet().Count);
    }

    [Fact]
    public void EveryVersionHasAManifestAndACompilingXsd()
    {
        foreach (var version in AuditfileVersion.All)
        {
            Assert.NotEmpty(version.Manifest.Tables);
            Assert.Equal(version.Manifest.XsdSha256, AuditfileVersion.Sha256(version.OpenXsd()));
            Assert.True(version.CreateSchemaSet().Count > 0, version.Key);
        }
    }

    [Fact]
    public void DefinitionsHaveDutchTextAndEnglishLabels()
    {
        var amount = AuditfileVersion.Financial.V3_2.Definitions["tr_line.amnt"];

        Assert.Equal("Amount", amount.Label!.English);
        Assert.Equal("Bedrag in locale valuta.", amount.Definition!.Dutch);
    }

    [Fact]
    public void ParseRejectsUnknownVersions()
    {
        Assert.Throws<ArgumentException>(() => AuditfileVersion.Parse("xaf/9.9"));
    }
}
