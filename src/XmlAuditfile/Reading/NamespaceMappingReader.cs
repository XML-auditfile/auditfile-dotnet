using System.Xml;

namespace XmlAuditfile.Reading;

/// <summary>
/// Presents one namespace as another, e.g. an alias as the official namespace,
/// so XSD validation and table mapping work as if the file used the official namespace.
/// </summary>
internal sealed class NamespaceMappingReader : XmlReader, IXmlLineInfo
{
    private readonly XmlReader _inner;
    private readonly string _from;
    private readonly string _to;

    public NamespaceMappingReader(XmlReader inner, string from, string to)
    {
        _inner = inner;
        _from = from;
        _to = inner.NameTable.Add(to);
    }

    public override int AttributeCount => _inner.AttributeCount;

    public override string BaseURI => _inner.BaseURI;

    public override int Depth => _inner.Depth;

    public override bool EOF => _inner.EOF;

    public override bool IsEmptyElement => _inner.IsEmptyElement;

    public override string LocalName => _inner.LocalName;

    public override string NamespaceURI => Map(_inner.NamespaceURI);

    public override XmlNameTable NameTable => _inner.NameTable;

    public override XmlNodeType NodeType => _inner.NodeType;

    public override string Prefix => _inner.Prefix;

    public override ReadState ReadState => _inner.ReadState;

    public override string Value => IsNamespaceDeclaration() ? Map(_inner.Value) : _inner.Value;

    public int LineNumber => (_inner as IXmlLineInfo)?.LineNumber ?? 0;

    public int LinePosition => (_inner as IXmlLineInfo)?.LinePosition ?? 0;

    public bool HasLineInfo() => (_inner as IXmlLineInfo)?.HasLineInfo() ?? false;

    public override string GetAttribute(int i) => _inner.GetAttribute(i);

    public override string? GetAttribute(string name) => _inner.GetAttribute(name);

    public override string? GetAttribute(string name, string? namespaceURI) =>
        _inner.GetAttribute(name, namespaceURI == _to ? _from : namespaceURI);

    public override string? LookupNamespace(string prefix)
    {
        var ns = _inner.LookupNamespace(prefix);
        return ns is null ? null : Map(ns);
    }

    public override bool MoveToAttribute(string name) => _inner.MoveToAttribute(name);

    public override bool MoveToAttribute(string name, string? ns) => _inner.MoveToAttribute(name, ns == _to ? _from : ns);

    public override bool MoveToElement() => _inner.MoveToElement();

    public override bool MoveToFirstAttribute() => _inner.MoveToFirstAttribute();

    public override bool MoveToNextAttribute() => _inner.MoveToNextAttribute();

    public override bool Read() => _inner.Read();

    public override bool ReadAttributeValue() => _inner.ReadAttributeValue();

    public override void ResolveEntity() => _inner.ResolveEntity();

    public override void Close() => _inner.Close();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    private string Map(string ns) => string.Equals(ns, _from, StringComparison.Ordinal) ? _to : ns;

    private bool IsNamespaceDeclaration() =>
        _inner.NodeType == XmlNodeType.Attribute && (_inner.Prefix == "xmlns" || (_inner.Prefix.Length == 0 && _inner.LocalName == "xmlns"));
}
