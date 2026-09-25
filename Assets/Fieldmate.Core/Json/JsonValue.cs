using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Fieldmate.Json;

public enum JsonKind
{
    Null,
    Boolean,
    Number,
    String,
    Array,
    Object,
}

/// <summary>
/// Immutable JSON tree. Core cannot use UnityEngine.JsonUtility (engine type, no dictionaries), so the manual, tool
/// schemas and tool arguments go through this small reader/writer instead of a package dependency.
/// Object members keep their source order.
/// </summary>
public sealed class JsonValue
{
    public static readonly JsonValue Null = new(JsonKind.Null);
    public static readonly JsonValue True = new(JsonKind.Boolean) { boolValue = true };
    public static readonly JsonValue False = new(JsonKind.Boolean) { boolValue = false };

    private static readonly IReadOnlyList<JsonValue> NoItems = System.Array.Empty<JsonValue>();
    private static readonly IReadOnlyList<KeyValuePair<string, JsonValue>> NoMembers = System.Array.Empty<KeyValuePair<string, JsonValue>>();

    private bool boolValue;
    private double numberValue;
    private string stringValue;
    private List<JsonValue> items;
    private List<KeyValuePair<string, JsonValue>> members;
    private Dictionary<string, JsonValue> index;

    private JsonValue(JsonKind kind) => Kind = kind;

    public JsonKind Kind { get; }

    public bool IsNull => Kind == JsonKind.Null;

    /// <summary>Array items, or empty for non-arrays.</summary>
    public IReadOnlyList<JsonValue> Items => items ?? NoItems;

    /// <summary>Object members in source order, or empty for non-objects.</summary>
    public IReadOnlyList<KeyValuePair<string, JsonValue>> Members => members ?? NoMembers;

    /// <summary>Member by name; <see cref="Null"/> when absent or when this is not an object.</summary>
    public JsonValue this[string name] => TryGet(name, out var value) ? value : Null;

    /// <summary>Array item; <see cref="Null"/> when out of range or when this is not an array.</summary>
    public JsonValue this[int i] => items != null && i >= 0 && i < items.Count ? items[i] : Null;

    public static JsonValue From(bool value) => value ? True : False;

    public static JsonValue From(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "JSON numbers must be finite.");
        }

        return new JsonValue(JsonKind.Number) { numberValue = value };
    }

    public static JsonValue From(string value) =>
        value == null ? Null : new JsonValue(JsonKind.String) { stringValue = value };

    public static JsonValue Array(IEnumerable<JsonValue> values)
    {
        var list = new List<JsonValue>();
        foreach (var value in values ?? throw new ArgumentNullException(nameof(values)))
        {
            list.Add(value ?? Null);
        }

        return new JsonValue(JsonKind.Array) { items = list };
    }

    public static JsonValue Array(params JsonValue[] values) => Array((IEnumerable<JsonValue>)values);

    /// <summary>Builds an object. Duplicate names are rejected.</summary>
    public static JsonValue Object(IEnumerable<KeyValuePair<string, JsonValue>> values)
    {
        var value = new JsonValue(JsonKind.Object)
        {
            members = new List<KeyValuePair<string, JsonValue>>(),
            index = new Dictionary<string, JsonValue>(StringComparer.Ordinal),
        };

        foreach (var pair in values ?? throw new ArgumentNullException(nameof(values)))
        {
            if (pair.Key == null)
            {
                throw new ArgumentException("Member names must not be null.", nameof(values));
            }

            if (!value.index.TryAdd(pair.Key, pair.Value ?? Null))
            {
                throw new ArgumentException($"Duplicate member '{pair.Key}'.", nameof(values));
            }

            value.members.Add(new KeyValuePair<string, JsonValue>(pair.Key, pair.Value ?? Null));
        }

        return value;
    }

    public static JsonValue Object(params (string name, JsonValue value)[] values)
    {
        var pairs = new List<KeyValuePair<string, JsonValue>>(values.Length);
        foreach (var (name, value) in values)
        {
            pairs.Add(new KeyValuePair<string, JsonValue>(name, value));
        }

        return Object(pairs);
    }

    public bool TryGet(string name, out JsonValue value)
    {
        value = null;
        return index != null && name != null && index.TryGetValue(name, out value);
    }

    public bool Has(string name) => index != null && name != null && index.ContainsKey(name);

    public bool TryGetBoolean(out bool value)
    {
        value = boolValue;
        return Kind == JsonKind.Boolean;
    }

    public bool TryGetNumber(out double value)
    {
        value = numberValue;
        return Kind == JsonKind.Number;
    }

    public bool TryGetString(out string value)
    {
        value = stringValue;
        return Kind == JsonKind.String;
    }

    /// <summary>True for numbers with no fractional part that fit in an <see cref="int"/>.</summary>
    public bool TryGetInt(out int value)
    {
        value = 0;
        if (Kind != JsonKind.Number || numberValue != Math.Floor(numberValue) ||
            numberValue < int.MinValue || numberValue > int.MaxValue)
        {
            return false;
        }

        value = (int)numberValue;
        return true;
    }

    public string AsString(string fallback = null) => Kind == JsonKind.String ? stringValue : fallback;
    public double AsNumber(double fallback = 0d) => Kind == JsonKind.Number ? numberValue : fallback;
    public bool AsBoolean(bool fallback = false) => Kind == JsonKind.Boolean ? boolValue : fallback;

    /// <summary>String items of an array; non-string items are skipped.</summary>
    public IReadOnlyList<string> AsStringList()
    {
        var result = new List<string>();
        foreach (var item in Items)
        {
            if (item.Kind == JsonKind.String)
            {
                result.Add(item.stringValue);
            }
        }

        return result;
    }

    /// <summary>Compact JSON text.</summary>
    public string ToJson()
    {
        var sb = new StringBuilder();
        Write(sb);
        return sb.ToString();
    }

    public override string ToString() => ToJson();

    internal void Write(StringBuilder sb)
    {
        switch (Kind)
        {
            case JsonKind.Null:
                sb.Append("null");
                break;
            case JsonKind.Boolean:
                sb.Append(boolValue ? "true" : "false");
                break;
            case JsonKind.Number:
                sb.Append(numberValue.ToString("R", CultureInfo.InvariantCulture));
                break;
            case JsonKind.String:
                WriteString(sb, stringValue);
                break;
            case JsonKind.Array:
                sb.Append('[');
                for (var i = 0; i < items.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }

                    items[i].Write(sb);
                }

                sb.Append(']');
                break;
            case JsonKind.Object:
                sb.Append('{');
                for (var i = 0; i < members.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }

                    WriteString(sb, members[i].Key);
                    sb.Append(':');
                    members[i].Value.Write(sb);
                }

                sb.Append('}');
                break;
        }
    }

    internal static void WriteString(StringBuilder sb, string value)
    {
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        sb.Append('"');
    }

    internal static JsonValue FromParsedObject(List<KeyValuePair<string, JsonValue>> members, Dictionary<string, JsonValue> index) =>
        new(JsonKind.Object) { members = members, index = index };

    internal static JsonValue FromParsedArray(List<JsonValue> items) => new(JsonKind.Array) { items = items };
}
