using CodebaseExplorer.Analysis.Parsing;

namespace CodebaseExplorer.Analysis.Tests.Parsing;

public sealed class SafeJsonTests
{
    [Fact]
    public void Parses_objects_arrays_and_literals_with_positions()
    {
        Assert.True(SafeJson.TryParse("{\n  \"a\": [1, true, null],\n  \"b\": \"x\"\n}", out var value, out var error), error);

        var root = Assert.IsType<JsonObject>(value);
        var a = root.Property("a")!;
        Assert.Equal((2, 3), (a.Line, a.Column));
        Assert.Equal(["1", "true", "null"], Assert.IsType<JsonArray>(a.Value).Items.Cast<JsonLiteral>().Select(l => l.Raw));
        Assert.Equal("x", root.String("b"));
        Assert.Equal((3, 8), (root["b"]!.Line, root["b"]!.Column));
    }

    [Fact]
    public void Accepts_jsonc_comments_and_trailing_commas()
    {
        Assert.True(SafeJson.TryParse("// head\n{ /* c */ \"a\": [1,], }", out var value, out var error), error);

        Assert.Equal((2, 11), (((JsonObject)value!).Property("a")!.Line, ((JsonObject)value).Property("a")!.Column));
    }

    [Fact]
    public void Columns_count_utf16_code_units_after_non_ascii_text()
    {
        Assert.True(SafeJson.TryParse("{\"ä😀\": 1, \"b\": 2}", out var value, out _));

        // {"ä😀": 1, "b" → 'b' property name starts after 11 UTF-16 code units
        Assert.Equal(12, ((JsonObject)value!).Property("b")!.Column);
    }

    [Fact]
    public void Leading_byte_order_mark_is_ignored()
    {
        Assert.True(SafeJson.TryParse("﻿{\"a\": 1}", out var value, out _));

        Assert.Equal(2, ((JsonObject)value!).Property("a")!.Column);
    }

    [Fact]
    public void Duplicate_names_resolve_to_the_last()
    {
        Assert.True(SafeJson.TryParse("{\"a\": \"1\", \"a\": \"2\"}", out var value, out _));

        Assert.Equal("2", ((JsonObject)value!).String("a"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{\"a\": }")]
    [InlineData("{} {}")]
    [InlineData("{'a': 1}")]
    public void Malformed_input_returns_an_error_instead_of_throwing(string content)
    {
        Assert.False(SafeJson.TryParse(content, out var value, out var error));
        Assert.Null(value);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Too_deep_nesting_is_rejected()
    {
        var deep = new string('[', 10_000) + new string(']', 10_000);

        Assert.False(SafeJson.TryParse(deep, out _, out var error));
        Assert.Contains("depth", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Nesting_up_to_the_limit_is_accepted()
    {
        var ok = new string('[', SafeJson.MaxDepth) + new string(']', SafeJson.MaxDepth);

        Assert.True(SafeJson.TryParse(ok, out _, out var error), error);
    }
}
