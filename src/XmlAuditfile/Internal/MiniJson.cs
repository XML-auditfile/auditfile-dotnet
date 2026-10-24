using System.Globalization;
using System.Text;

namespace XmlAuditfile.Internal;

/// <summary>
/// Minimal JSON reader for the embedded definitions. No reflection and no dependencies,
/// so it works on netstandard2.0, in Native AOT and in WebAssembly.
/// Objects become <see cref="JsonObject"/>, arrays <see cref="List{T}"/> of object,
/// numbers <see cref="decimal"/>, and true/false/null their .NET equivalents.
/// </summary>
internal static class MiniJson
{
    public static object? Parse(string text)
    {
        var parser = new Parser(text);
        var value = parser.ReadValue();
        parser.SkipWhitespace();
        if (!parser.AtEnd)
        {
            throw parser.Error("unexpected content after the JSON value");
        }

        return value;
    }

    private sealed class Parser(string text)
    {
        private int _position;

        public bool AtEnd => _position >= text.Length;

        public object? ReadValue()
        {
            SkipWhitespace();
            if (AtEnd)
            {
                throw Error("unexpected end of input");
            }

            switch (text[_position])
            {
                case '{': return ReadObject();
                case '[': return ReadArray();
                case '"': return ReadString();
                case 't': Expect("true"); return true;
                case 'f': Expect("false"); return false;
                case 'n': Expect("null"); return null;
                default: return ReadNumber();
            }
        }

        private JsonObject ReadObject()
        {
            var result = new JsonObject();
            _position++;
            SkipWhitespace();
            if (Peek() == '}')
            {
                _position++;
                return result;
            }

            while (true)
            {
                SkipWhitespace();
                var key = ReadString();
                SkipWhitespace();
                Consume(':');
                result[key] = ReadValue();
                SkipWhitespace();
                if (Peek() == ',')
                {
                    _position++;
                    continue;
                }

                Consume('}');
                return result;
            }
        }

        private List<object?> ReadArray()
        {
            var result = new List<object?>();
            _position++;
            SkipWhitespace();
            if (Peek() == ']')
            {
                _position++;
                return result;
            }

            while (true)
            {
                result.Add(ReadValue());
                SkipWhitespace();
                if (Peek() == ',')
                {
                    _position++;
                    continue;
                }

                Consume(']');
                return result;
            }
        }

        private string ReadString()
        {
            Consume('"');
            var builder = new StringBuilder();
            while (true)
            {
                if (AtEnd)
                {
                    throw Error("unterminated string");
                }

                var c = text[_position++];
                if (c == '"')
                {
                    return builder.ToString();
                }

                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }

                var escape = text[_position++];
                switch (escape)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        builder.Append((char)int.Parse(text.Substring(_position, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        _position += 4;
                        break;
                    default: throw Error($"invalid escape '\\{escape}'");
                }
            }
        }

        private decimal ReadNumber()
        {
            var start = _position;
            while (!AtEnd && "+-0123456789.eE".IndexOf(text[_position]) >= 0)
            {
                _position++;
            }

            var token = text.Substring(start, _position - start);
            return decimal.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                ? number
                : throw Error($"invalid number '{token}'");
        }

        public void SkipWhitespace()
        {
            while (!AtEnd && char.IsWhiteSpace(text[_position]))
            {
                _position++;
            }
        }

        private char Peek() => AtEnd ? '\0' : text[_position];

        private void Consume(char expected)
        {
            if (Peek() != expected)
            {
                throw Error($"expected '{expected}'");
            }

            _position++;
        }

        private void Expect(string literal)
        {
            if (string.CompareOrdinal(text, _position, literal, 0, literal.Length) != 0)
            {
                throw Error($"expected '{literal}'");
            }

            _position += literal.Length;
        }

        public FormatException Error(string message) => new($"Invalid embedded JSON at position {_position}: {message}");
    }
}

/// <summary>A JSON object with typed accessors that fail loudly on unexpected content.</summary>
internal sealed class JsonObject : Dictionary<string, object?>
{
    public JsonObject()
        : base(StringComparer.Ordinal)
    {
    }

    public string String(string name) => Get(name) as string ?? throw Missing(name);

    public string? OptionalString(string name) => Get(name) as string;

    public int Int(string name) => Get(name) is decimal d ? (int)d : throw Missing(name);

    public int? OptionalInt(string name) => Get(name) is decimal d ? (int)d : null;

    public JsonObject? Object(string name) => Get(name) as JsonObject;

    public IEnumerable<JsonObject> Objects(string name) =>
        Get(name) is List<object?> list ? list.OfType<JsonObject>() : Enumerable.Empty<JsonObject>();

    public IEnumerable<string> Strings(string name) =>
        Get(name) is List<object?> list ? list.OfType<string>() : Enumerable.Empty<string>();

    private object? Get(string name) => TryGetValue(name, out var value) ? value : null;

    private static FormatException Missing(string name) => new($"Embedded JSON: missing or invalid property '{name}'.");
}
