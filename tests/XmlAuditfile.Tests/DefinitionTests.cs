using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace XmlAuditfile.Tests;

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

    /// <summary>
    /// The XSD's identity constraints are not evaluated (see ReadSession); the ReferenceChecker works from the manifest instead,
    /// so the manifest must hold every key and reference of every XSD.
    /// </summary>
    [Fact]
    public void EveryManifestHoldsAllIdentityConstraintsOfItsXsd()
    {
        XNamespace xsd = "http://www.w3.org/2001/XMLSchema";
        foreach (var version in AuditfileVersion.All)
        {
            using var stream = version.OpenXsd();
            var schema = XDocument.Load(stream);
            var keys = schema.Descendants().Where(e => e.Name == xsd + "key" || e.Name == xsd + "unique").Count();
            var references = schema.Descendants(xsd + "keyref").Count();

            Assert.Equal((version.Key, keys), (version.Key, version.Manifest.Tables.Sum(t => t.Keys.Count)));
            Assert.Equal((version.Key, references), (version.Key, version.Manifest.Tables.Sum(t => t.References.Count)));
        }
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
