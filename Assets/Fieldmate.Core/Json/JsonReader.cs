using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Fieldmate.Json;

/// <summary>Malformed JSON, with a 1-based position that model- and human-readable errors can point at.</summary>
public sealed class JsonException : Exception
{
    public JsonException(string message, int line, int column)
        : base($"{message} (line {line}, column {column})")
    {
        Line = line;
        Column = column;
    }

    public int Line { get; }
    public int Column { get; }
}

/// <summary>Strict RFC 8259 parser: no comments, no trailing commas, no duplicate object members.</summary>
public static class JsonReader
{
    public const int MaxDepth = 64;

    public static JsonValue Parse(string text)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        var parser = new Parser(text);
        parser.SkipWhitespace();
        var value = parser.ParseValue(0);
        parser.SkipWhitespace();
        if (!parser.AtEnd)
        {
            throw parser.Error("Unexpected content after the JSON value");
        }

        return value;
    }

    public static bool TryParse(string text, out JsonValue value, out JsonException error)
    {
        try
        {
            value = Parse(text);
            error = null;
            return true;
        }
        catch (JsonException e)
        {
            value = null;
            error = e;
            return false;
        }
        catch (ArgumentNullException)
        {
            value = null;
            error = new JsonException("No JSON text", 1, 1);
            return false;
        }
    }

    private sealed class Parser
    {
        private readonly string text;
        private int position;

        public Parser(string text) => this.text = text;

        public bool AtEnd => position >= text.Length;

        public JsonValue ParseValue(int depth)
        {
            if (depth > MaxDepth)
            {
                throw Error($"Nesting deeper than {MaxDepth}");
            }

            if (AtEnd)
            {
                throw Error("Unexpected end of input");
            }

            var c = text[position];
            switch (c)
            {
                case '{': return ParseObject(depth);
                case '[': return ParseArray(depth);
                case '"': return JsonValue.From(ParseString());
                case 't': ExpectLiteral("true"); return JsonValue.True;
                case 'f': ExpectLiteral("false"); return JsonValue.False;
                case 'n': ExpectLiteral("null"); return JsonValue.Null;
                default:
                    if (c == '-' || (c >= '0' && c <= '9'))
                    {
                        return ParseNumber();
                    }

                    throw Error($"Unexpected character '{c}'");
            }
        }

        public void SkipWhitespace()
        {
            while (!AtEnd && text[position] is ' ' or '\t' or '\n' or '\r')
            {
                position++;
            }
        }

        public JsonException Error(string message)
        {
            int line = 1, column = 1;
            for (var i = 0; i < position && i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                    column = 1;
                }
                else
                {
                    column++;
                }
            }

            return new JsonException(message, line, column);
        }

        private JsonValue ParseObject(int depth)
        {
            position++; // {
            var members = new List<KeyValuePair<string, JsonValue>>();
            var index = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
            SkipWhitespace();
            if (TryConsume('}'))
            {
                return JsonValue.FromParsedObject(members, index);
            }

            while (true)
            {
                SkipWhitespace();
                if (AtEnd || text[position] != '"')
                {
                    throw Error("Expected a member name in double quotes");
                }

                var namePosition = position;
                var name = ParseString();
                SkipWhitespace();
                Expect(':');
                SkipWhitespace();
                var value = ParseValue(depth + 1);
                if (!index.TryAdd(name, value))
                {
                    position = namePosition;
                    throw Error($"Duplicate member '{name}'");
                }

                members.Add(new KeyValuePair<string, JsonValue>(name, value));
                SkipWhitespace();
                if (TryConsume('}'))
                {
                    return JsonValue.FromParsedObject(members, index);
                }

                Expect(',');
            }
        }

        private JsonValue ParseArray(int depth)
        {
            position++; // [
            var items = new List<JsonValue>();
            SkipWhitespace();
            if (TryConsume(']'))
            {
                return JsonValue.FromParsedArray(items);
            }

            while (true)
            {
                SkipWhitespace();
                items.Add(ParseValue(depth + 1));
                SkipWhitespace();
                if (TryConsume(']'))
                {
                    return JsonValue.FromParsedArray(items);
                }

                Expect(',');
            }
        }

        private string ParseString()
        {
            position++; // opening quote
            var sb = new StringBuilder();
            while (true)
            {
                if (AtEnd)
                {
                    throw Error("Unterminated string");
                }

                var c = text[position++];
                if (c == '"')
                {
                    return sb.ToString();
                }

                if (c < 0x20)
                {
                    position--;
                    throw Error("Control characters must be escaped in strings");
                }

                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }

                if (AtEnd)
                {
                    throw Error("Unterminated escape sequence");
                }

                var escape = text[position++];
                switch (escape)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (position + 4 > text.Length ||
                            !int.TryParse(text.Substring(position, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                        {
                            throw Error("Invalid \\u escape");
                        }

                        sb.Append((char)code);
                        position += 4;
                        break;
                    default:
                        position--;
                        throw Error($"Invalid escape '\\{escape}'");
                }
            }
        }

        private JsonValue ParseNumber()
        {
            var start = position;
            TryConsume('-');
            if (TryConsume('0'))
            {
                // no leading zeros
            }
            else if (!ConsumeDigits())
            {
                throw Error("Invalid number");
            }

            if (TryConsume('.') && !ConsumeDigits())
            {
                throw Error("Expected digits after the decimal point");
            }

            if (!AtEnd && text[position] is 'e' or 'E')
            {
                position++;
                if (!TryConsume('+'))
                {
                    TryConsume('-');
                }

                if (!ConsumeDigits())
                {
                    throw Error("Expected digits in the exponent");
                }
            }

            var span = text.Substring(start, position - start);
            if (!double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
                double.IsInfinity(number))
            {
                position = start;
                throw Error($"Number out of range: {span}");
            }

            return JsonValue.From(number);
        }

        private bool ConsumeDigits()
        {
            var start = position;
            while (!AtEnd && text[position] >= '0' && text[position] <= '9')
            {
                position++;
            }

            return position > start;
        }

        private void ExpectLiteral(string literal)
        {
            if (string.CompareOrdinal(text, position, literal, 0, literal.Length) != 0)
            {
                throw Error($"Expected '{literal}'");
            }

            position += literal.Length;
        }

        private void Expect(char c)
        {
            if (!TryConsume(c))
            {
                throw Error(AtEnd ? $"Expected '{c}' but reached the end" : $"Expected '{c}' but found '{text[position]}'");
            }
        }

        private bool TryConsume(char c)
        {
            if (AtEnd || text[position] != c)
            {
                return false;
            }

            position++;
            return true;
        }
    }
}
