using System;
using System.Collections.Generic;
using Fieldmate.Json;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.Json;

public class JsonTests
{
    [Test]
    public void Parse_ReadsAllValueKinds()
    {
        var value = JsonReader.Parse("{\"s\":\"hi\",\"n\":-1.5e2,\"i\":42,\"t\":true,\"f\":false,\"z\":null,\"a\":[1,\"x\"],\"o\":{\"k\":0}}");

        Assert.That(value.Kind, Is.EqualTo(JsonKind.Object));
        Assert.That(value["s"].AsString(), Is.EqualTo("hi"));
        Assert.That(value["n"].AsNumber(), Is.EqualTo(-150d));
        Assert.That(value["i"].TryGetInt(out var i) && i == 42, Is.True);
        Assert.That(value["t"].AsBoolean(), Is.True);
        Assert.That(value["f"].TryGetBoolean(out var f) && !f, Is.True);
        Assert.That(value["z"].IsNull, Is.True);
        Assert.That(value["a"].Items, Has.Count.EqualTo(2));
        Assert.That(value["a"][1].AsString(), Is.EqualTo("x"));
        Assert.That(value["o"]["k"].AsNumber(), Is.Zero);
    }

    [Test]
    public void Parse_DecodesEscapes()
    {
        var value = JsonReader.Parse("\"a\\\"b\\\\c\\/d\\n\\t\\u00e9\\b\\f\\r\"");

        Assert.That(value.AsString(), Is.EqualTo("a\"b\\c/d\n\t\u00e9\b\f\r"));
    }

    [Test]
    public void Members_KeepSourceOrder()
    {
        var value = JsonReader.Parse("{\"b\":1,\"a\":2,\"c\":3}");

        Assert.That(System.Linq.Enumerable.Select(value.Members, m => m.Key), Is.EqualTo(new[] { "b", "a", "c" }));
    }

    [TestCase("", "Unexpected end")]
    [TestCase("{\"a\":1,}", "member name")]
    [TestCase("[1,]", "Unexpected character")]
    [TestCase("{\"a\" 1}", "Expected ':'")]
    [TestCase("{\"a\":1 \"b\":2}", "Expected ','")]
    [TestCase("\"open", "Unterminated string")]
    [TestCase("\"bad\\q\"", "Invalid escape")]
    [TestCase("\"\\u12\"", "Invalid \\u escape")]
    [TestCase("01", "Unexpected content")]
    [TestCase("1.", "decimal point")]
    [TestCase("1e", "exponent")]
    [TestCase("-", "Invalid number")]
    [TestCase("tru", "Expected 'true'")]
    [TestCase("{\"a\":1,\"a\":2}", "Duplicate member 'a'")]
    [TestCase("[1] 2", "Unexpected content")]
    [TestCase("1e999", "out of range")]
    public void Parse_RejectsMalformedInputWithPosition(string text, string expected)
    {
        var e = Assert.Throws<JsonException>(() => JsonReader.Parse(text));

        Assert.That(e.Message, Does.Contain(expected));
        Assert.That(e.Line, Is.GreaterThanOrEqualTo(1));
        Assert.That(e.Column, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void Parse_ReportsLineAndColumn()
    {
        var e = Assert.Throws<JsonException>(() => JsonReader.Parse("{\n  \"a\": x\n}"));

        Assert.That((e.Line, e.Column), Is.EqualTo((2, 8)));
    }

    [Test]
    public void Parse_RejectsControlCharactersAndDeepNesting()
    {
        Assert.Throws<JsonException>(() => JsonReader.Parse("\"a\nb\""));
        Assert.Throws<JsonException>(() => JsonReader.Parse(new string('[', JsonReader.MaxDepth + 2)));
        Assert.Throws<ArgumentNullException>(() => JsonReader.Parse(null));
    }

    [Test]
    public void TryParse_ReturnsErrorsInsteadOfThrowing()
    {
        Assert.That(JsonReader.TryParse("[1]", out var ok, out var none), Is.True);
        Assert.That(ok.Items, Has.Count.EqualTo(1));
        Assert.That(none, Is.Null);

        Assert.That(JsonReader.TryParse("{", out var bad, out var error), Is.False);
        Assert.That(bad, Is.Null);
        Assert.That(error, Is.Not.Null);

        Assert.That(JsonReader.TryParse(null, out _, out var nullError), Is.False);
        Assert.That(nullError.Message, Does.Contain("No JSON"));
    }

    [Test]
    public void ToJson_RoundTrips()
    {
        const string text = "{\"name\":\"Relief \\\"RV\\\"\\n\",\"n\":0.25,\"list\":[true,false,null,{}],\"empty\":[]}";

        Assert.That(JsonReader.Parse(text).ToJson(), Is.EqualTo(text));
    }

    [Test]
    public void Builders_CreateValues()
    {
        var value = JsonValue.Object(("a", JsonValue.From(1)), ("b", JsonValue.Array(JsonValue.From("x"), null)),
            ("c", JsonValue.From((string)null)), ("d", JsonValue.From(true)));

        Assert.That(value.ToJson(), Is.EqualTo("{\"a\":1,\"b\":[\"x\",null],\"c\":null,\"d\":true}"));
        Assert.That(JsonValue.From("\u0001").ToJson(), Is.EqualTo("\"\\u0001\""));
        Assert.That(value.ToString(), Is.EqualTo(value.ToJson()));
    }

    [Test]
    public void Builders_RejectInvalidInput()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => JsonValue.From(double.NaN));
        Assert.Throws<ArgumentException>(() => JsonValue.Object(("a", JsonValue.Null), ("a", JsonValue.Null)));
        Assert.Throws<ArgumentException>(() => JsonValue.Object(new[] { new KeyValuePair<string, JsonValue>(null, JsonValue.Null) }));
        Assert.Throws<ArgumentNullException>(() => JsonValue.Array((IEnumerable<JsonValue>)null));
        Assert.Throws<ArgumentNullException>(() => JsonValue.Object((IEnumerable<KeyValuePair<string, JsonValue>>)null));
    }

    [Test]
    public void Accessors_AreSafeOnWrongKinds()
    {
        var number = JsonValue.From(2.5);

        Assert.That(number["x"].IsNull, Is.True);
        Assert.That(number[0].IsNull, Is.True);
        Assert.That(number.Has("x"), Is.False);
        Assert.That(number.Items, Is.Empty);
        Assert.That(number.Members, Is.Empty);
        Assert.That(number.AsString("fallback"), Is.EqualTo("fallback"));
        Assert.That(number.TryGetString(out _), Is.False);
        Assert.That(number.TryGetInt(out _), Is.False, "not an integer");
        Assert.That(JsonValue.From(3e10).TryGetInt(out _), Is.False, "out of int range");
        Assert.That(JsonValue.From("s").AsNumber(7), Is.EqualTo(7));
        Assert.That(JsonValue.From("s").AsBoolean(true), Is.True);
        Assert.That(JsonValue.From("s").TryGetNumber(out _), Is.False);
        Assert.That(JsonReader.Parse("[\"a\",1,\"b\"]").AsStringList(), Is.EqualTo(new[] { "a", "b" }));
        Assert.That(JsonReader.Parse("{\"a\":1}").Has("a"), Is.True);
    }
}
