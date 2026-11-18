using System.Text;
using System.Text.Json;

namespace CodebaseExplorer.Analysis.Parsing;

/// <summary>A parsed JSON value with the 1-based position (UTF-16 columns) of its first character.</summary>
internal abstract record JsonValue(int Line, int Column);

/// <summary>A JSON string.</summary>
internal sealed record JsonString(string Value, int Line, int Column) : JsonValue(Line, Column);

/// <summary>A number, <c>true</c>, <c>false</c> or <c>null</c>, kept as written.</summary>
internal sealed record JsonLiteral(string Raw, int Line, int Column) : JsonValue(Line, Column);

/// <summary>A JSON array.</summary>
internal sealed record JsonArray(IReadOnlyList<JsonValue> Items, int Line, int Column) : JsonValue(Line, Column);

/// <summary>A property with the position of its (quoted) name.</summary>
internal sealed record JsonProperty(string Name, JsonValue Value, int Line, int Column);

/// <summary>A JSON object; properties keep their order, duplicate names resolve to the last one (as in <c>JSON.parse</c>).</summary>
internal sealed record JsonObject(IReadOnlyList<JsonProperty> Properties, int Line, int Column) : JsonValue(Line, Column)
{
    public JsonProperty? Property(string name) => Properties.LastOrDefault(p => p.Name == name);

    public JsonValue? this[string name] => Property(name)?.Value;

    public string? String(string name) => this[name] is JsonString s ? s.Value : null;
}

/// <summary>
/// Hardened JSON/JSONC parsing for untrusted repository files (SECURITY.md §3): bounded nesting depth, size
/// limited by the caller's read, no exceptions for malformed input, positions for evidence locations. JSONC
/// (comments, trailing commas) is accepted because <c>tsconfig.json</c> uses it.
/// </summary>
internal static class SafeJson
{
    /// <summary>Maximum nesting depth; deeper documents are rejected.</summary>
    public const int MaxDepth = 64;

    /// <summary>Parses <paramref name="content"/>; returns the error message instead of throwing.</summary>
    public static bool TryParse(string content, out JsonValue? value, out string? error)
    {
        ArgumentNullException.ThrowIfNull(content);

        // A leading BOM is not JSON; the file reader already decoded the bytes.
        var text = content.Length > 0 && content[0] == '﻿' ? content[1..] : content;
        var offset = text.Length == content.Length ? 0 : 1;
        var bytes = Encoding.UTF8.GetBytes(text);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            MaxDepth = MaxDepth,
        });
        var positions = new PositionMap(content, offset);

        try
        {
            if (!reader.Read())
            {
                value = null;
                error = "The file contains no JSON value.";
                return false;
            }

            value = ReadValue(ref reader, positions);
            if (reader.Read())
            {
                value = null;
                error = "Unexpected content after the JSON value.";
                return false;
            }

            error = null;
            return true;
        }
        catch (JsonException exception)
        {
            value = null;
            error = $"Invalid JSON at line {exception.LineNumber + 1}: {exception.Message}";
            return false;
        }
    }

    private static JsonValue ReadValue(ref Utf8JsonReader reader, PositionMap positions)
    {
        var (line, column) = positions.At(reader.TokenStartIndex);
        switch (reader.TokenType)
        {
            case JsonTokenType.StartObject:
                var properties = new List<JsonProperty>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    var (nameLine, nameColumn) = positions.At(reader.TokenStartIndex);
                    var name = reader.GetString()!;
                    reader.Read();
                    properties.Add(new JsonProperty(name, ReadValue(ref reader, positions), nameLine, nameColumn));
                }

                return new JsonObject(properties, line, column);
            case JsonTokenType.StartArray:
                var items = new List<JsonValue>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    items.Add(ReadValue(ref reader, positions));
                }

                return new JsonArray(items, line, column);
            case JsonTokenType.String:
                return new JsonString(reader.GetString()!, line, column);
            default:
                return new JsonLiteral(Encoding.UTF8.GetString(reader.ValueSpan), line, column);
        }
    }

    /// <summary>
    /// Maps UTF-8 byte offsets of increasing tokens to 1-based line and UTF-16 column, walking the text once.
    /// </summary>
    private sealed class PositionMap(string text, int startIndex)
    {
        private int _charIndex = startIndex;
        private long _byteIndex;
        private int _line = 1;
        private int _column = 1;

        public (int Line, int Column) At(long byteOffset)
        {
            while (_byteIndex < byteOffset && _charIndex < text.Length)
            {
                var c = text[_charIndex];
                int byteCount;
                int charCount;
                if (char.IsHighSurrogate(c) && _charIndex + 1 < text.Length && char.IsLowSurrogate(text[_charIndex + 1]))
                {
                    byteCount = 4;
                    charCount = 2;
                }
                else
                {
                    byteCount = c < 0x80 ? 1 : c < 0x800 ? 2 : 3;
                    charCount = 1;
                }

                if (c == '\n')
                {
                    _line++;
                    _column = 1;
                }
                else
                {
                    _column += charCount;
                }

                _byteIndex += byteCount;
                _charIndex += charCount;
            }

            return (_line, _column);
        }
    }
}
