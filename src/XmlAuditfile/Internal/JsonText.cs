using System.Globalization;
using System.Text;

namespace XmlAuditfile.Internal;

/// <summary>Minimal JSON writer without reflection. Writes compact JSON.</summary>
internal sealed class JsonText
{
    private readonly StringBuilder _builder = new();
    private bool _needComma;

    public JsonText StartObject() => Open('{');

    public JsonText EndObject() => Close('}');

    public JsonText StartArray() => Open('[');

    public JsonText EndArray() => Close(']');

    public JsonText Name(string name)
    {
        Separator();
        WriteString(name);
        _builder.Append(':');
        _needComma = false;
        return this;
    }

    public JsonText Value(string? value)
    {
        Separator();
        if (value is null)
        {
            _builder.Append("null");
        }
        else
        {
            WriteString(value);
        }

        _needComma = true;
        return this;
    }

    public JsonText Value(long? value) => Raw(value?.ToString(CultureInfo.InvariantCulture) ?? "null");

    public JsonText Value(bool value) => Raw(value ? "true" : "false");

    public JsonText Property(string name, string? value) => Name(name).Value(value);

    public JsonText Property(string name, long? value) => Name(name).Value(value);

    public JsonText Property(string name, bool value) => Name(name).Value(value);

    public JsonText Strings(string name, IEnumerable<string> values)
    {
        Name(name).StartArray();
        foreach (var value in values)
        {
            Value(value);
        }

        return EndArray();
    }

    public override string ToString() => _builder.ToString();

    private JsonText Open(char c)
    {
        Separator();
        _builder.Append(c);
        _needComma = false;
        return this;
    }

    private JsonText Close(char c)
    {
        _builder.Append(c);
        _needComma = true;
        return this;
    }

    private JsonText Raw(string text)
    {
        Separator();
        _builder.Append(text);
        _needComma = true;
        return this;
    }

    private void Separator()
    {
        if (_needComma)
        {
            _builder.Append(',');
        }
    }

    private void WriteString(string value)
    {
        _builder.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': _builder.Append("\\\""); break;
                case '\\': _builder.Append("\\\\"); break;
                case '\n': _builder.Append("\\n"); break;
                case '\r': _builder.Append("\\r"); break;
                case '\t': _builder.Append("\\t"); break;
                default:
                    if (c < ' ')
                    {
                        _builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        _builder.Append(c);
                    }

                    break;
            }
        }

        _builder.Append('"');
    }
}
