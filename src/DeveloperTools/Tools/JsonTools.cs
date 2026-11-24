using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DeveloperTools.Tools;

/// <summary>Result of a strict syntax check. Line/Column are 1-based; Offset is a character offset.</summary>
public sealed record JsonCheck(
    bool IsEmpty,
    bool IsValid,
    string Message,
    int Line = 0,
    int Column = 0,
    int Offset = -1,
    string? RootKind = null,
    int Keys = 0,
    int Depth = 0)
{
    public static readonly JsonCheck Empty = new(true, false, "Paste or type JSON");
}

public sealed record JsonRepairResult(string Text, IReadOnlyList<string> Fixes);

/// <summary>
/// Strict validation (System.Text.Json), a lenient parser that repairs common mistakes,
/// and a writer that formats or minifies while keeping numbers and string escapes exactly as written.
/// </summary>
public static partial class JsonTools
{
    public const int MaxDepth = 1000;

    // ------------------------------------------------------------------ validation

    public static JsonCheck Validate(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return JsonCheck.Empty;

        var skip = text.Length > 0 && text[0] == '﻿' ? 1 : 0;
        var bytes = Encoding.UTF8.GetBytes(text.AsSpan(skip).ToArray());
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = MaxDepth });
        try
        {
            string? root = null;
            int keys = 0, depth = 0;
            while (reader.Read())
            {
                root ??= reader.TokenType switch
                {
                    JsonTokenType.StartObject => "object",
                    JsonTokenType.StartArray => "array",
                    JsonTokenType.String => "string",
                    JsonTokenType.Number => "number",
                    JsonTokenType.True or JsonTokenType.False => "boolean",
                    _ => "null"
                };
                if (reader.TokenType == JsonTokenType.PropertyName)
                    keys++;
                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                    depth = Math.Max(depth, reader.CurrentDepth + 1);
            }

            return new JsonCheck(false, true, "Valid JSON", RootKind: root, Keys: keys, Depth: depth);
        }
        catch (JsonException ex)
        {
            var line = (int)(ex.LineNumber ?? 0);
            var bytePos = (int)(ex.BytePositionInLine ?? 0);
            var offset = CharOffset(text, skip, line, bytePos);
            var lineStart = LineStart(text, offset);
            return new JsonCheck(false, false, Explain(text, offset, CleanMessage(ex.Message)), line + 1, offset - lineStart + 1, offset);
        }
    }

    private static int CharOffset(string text, int skip, int line, int bytePos)
    {
        var i = skip;
        for (var l = 0; l < line && i < text.Length; i++)
            if (text[i] == '\n')
                l++;

        var bytes = 0;
        while (i < text.Length && bytes < bytePos && text[i] != '\n')
        {
            var c = text[i];
            bytes += c < 0x80 ? 1 : c < 0x800 ? 2 : char.IsSurrogate(c) ? 2 : 3;
            i++;
        }

        return Math.Min(i, text.Length);
    }

    /// <summary>Plain-language messages for the most common mistakes.</summary>
    private static string Explain(string text, int offset, string message)
    {
        if (offset >= text.Length)
            return message;
        var c = text[offset];
        var next = offset + 1 < text.Length ? text[offset + 1] : '\0';
        if ((c == '/' && next is '/' or '*') || c == '#')
            return "Comments are not allowed in JSON.";
        if (c == '\'')
            return "Strings and keys must use double quotes, not single quotes.";
        if (message.Contains("invalid start of a property name", StringComparison.Ordinal) && (char.IsLetter(c) || c is '_' or '$'))
            return "Property names must be in double quotes.";
        if (message.Contains("invalid start of a value", StringComparison.Ordinal) && char.IsLetter(c))
            return "Unknown value. Text must be in double quotes; literals are true, false and null (lowercase).";
        return message;
    }

    private static int LineStart(string text, int offset)
    {
        var i = Math.Min(offset, text.Length);
        while (i > 0 && text[i - 1] != '\n')
            i--;
        return i;
    }

    [GeneratedRegex(@"\s*(Path: \S*)?\s*\|?\s*LineNumber:.*$", RegexOptions.Singleline)]
    private static partial Regex PositionSuffix();

    private static string CleanMessage(string message)
    {
        var clean = PositionSuffix().Replace(message, "").Trim();
        if (clean.StartsWith("Expected depth to be zero", StringComparison.Ordinal))
            return "Unexpected end of JSON: an object or array is not closed.";
        if (clean.Contains("trailing comma", StringComparison.Ordinal))
            return "Trailing comma: JSON does not allow a comma before a closing bracket.";
        if (clean.Contains("is invalid after a single JSON value", StringComparison.Ordinal))
            return "Extra content after the JSON value. Wrap multiple values in an array.";
        return clean.EndsWith('.') ? clean : clean + ".";
    }

    // ------------------------------------------------------------------ format / minify

    /// <param name="indent">Indent unit ("  ", "    ", "\t"), or null to minify.</param>
    /// <exception cref="FormatException">The text is not valid JSON.</exception>
    public static string Format(string text, string? indent, bool sortKeys = false)
    {
        var check = Validate(text);
        if (!check.IsValid)
            throw new FormatException(check.IsEmpty ? "There is no JSON to format." : $"Line {check.Line}, column {check.Column}: {check.Message}");

        var node = new LenientParser(text).ParseDocument(); // exact for valid input
        return Write(node, indent, sortKeys);
    }

    public static JsonRepairResult Repair(string text, string? indent, bool sortKeys = false)
    {
        var parser = new LenientParser(text);
        var node = parser.ParseDocument();
        var output = Write(node, indent, sortKeys);
        return new JsonRepairResult(output, parser.Fixes);
    }

    /// <summary>Turns any text into a JSON string literal.</summary>
    public static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length + 16).Append('"');
        foreach (var c in text)
            AppendEscaped(sb, c);
        return sb.Append('"').ToString();
    }

    /// <summary>Reads a JSON string literal back into plain text (quotes optional).</summary>
    public static string Unescape(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '"' || trimmed[^1] != '"')
            trimmed = "\"" + trimmed + "\"";
        try
        {
            using var doc = JsonDocument.Parse(trimmed); // reflection-free (AOT friendly)
            return doc.RootElement.GetString() ?? "";
        }
        catch (JsonException ex)
        {
            throw new FormatException("Not a valid JSON string: " + CleanMessage(ex.Message));
        }
    }

    private static void AppendEscaped(StringBuilder sb, char c)
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
                    sb.Append("\\u").Append(((int)c).ToString("x4"));
                else
                    sb.Append(c);
                break;
        }
    }

    // ------------------------------------------------------------------ tree + writer

    private abstract class Node;

    private sealed class ObjectNode : Node
    {
        public readonly List<KeyValuePair<string, Node>> Members = new();
    }

    private sealed class ArrayNode : Node
    {
        public readonly List<Node> Items = new();
    }

    /// <summary>A string (with quotes), number or literal, exactly as it will be written.</summary>
    private sealed class ValueNode(string raw) : Node
    {
        public readonly string Raw = raw;
    }

    private static readonly ValueNode Null = new("null");

    private static string Write(Node node, string? indent, bool sortKeys)
    {
        var sb = new StringBuilder();
        WriteNode(sb, node, indent, sortKeys, 0);
        return sb.ToString();
    }

    private static void WriteNode(StringBuilder sb, Node node, string? indent, bool sortKeys, int depth)
    {
        switch (node)
        {
            case ValueNode value:
                sb.Append(value.Raw);
                break;

            case ArrayNode array when array.Items.Count == 0:
                sb.Append("[]");
                break;

            case ArrayNode array:
                sb.Append('[');
                for (var i = 0; i < array.Items.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    NewLine(sb, indent, depth + 1);
                    WriteNode(sb, array.Items[i], indent, sortKeys, depth + 1);
                }
                NewLine(sb, indent, depth);
                sb.Append(']');
                break;

            case ObjectNode obj when obj.Members.Count == 0:
                sb.Append("{}");
                break;

            case ObjectNode obj:
                IEnumerable<KeyValuePair<string, Node>> members = obj.Members;
                if (sortKeys)
                    members = members.OrderBy(m => m.Key, StringComparer.Ordinal);
                sb.Append('{');
                var first = true;
                foreach (var (key, value) in members)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    NewLine(sb, indent, depth + 1);
                    sb.Append(key).Append(indent is null ? ":" : ": ");
                    WriteNode(sb, value, indent, sortKeys, depth + 1);
                }
                NewLine(sb, indent, depth);
                sb.Append('}');
                break;
        }
    }

    private static void NewLine(StringBuilder sb, string? indent, int depth)
    {
        if (indent is null)
            return;
        sb.Append('\n');
        for (var i = 0; i < depth; i++)
            sb.Append(indent);
    }

    // ------------------------------------------------------------------ lenient parser

    [GeneratedRegex(@"^-?(0|[1-9]\d*)(\.\d+)?([eE][+-]?\d+)?$")]
    private static partial Regex StrictNumber();

    [GeneratedRegex(@"^\s*```[\w-]*[ \t]*\r?\n(?<body>[\s\S]*?)\r?\n?[ \t]*```\s*$")]
    private static partial Regex MarkdownFence();

    [GeneratedRegex(@"^\s*[A-Za-z_$][\w$.]*\s*\((?<body>[\s\S]*)\)\s*;?\s*$")]
    private static partial Regex JsonpWrapper();

    /// <summary>
    /// Recursive-descent parser that accepts JSON5/JS-ish input and broken JSON, records what it had
    /// to change, and produces a tree of strict JSON. On valid JSON it makes no changes.
    /// </summary>
    private sealed class LenientParser
    {
        private const string MissingValue = "Filled missing values with null";
        private const string Unexpected = "Removed unexpected characters";

        private readonly string _s;
        private readonly int _end;
        private int _i;
        private int _depth;
        private readonly Dictionary<string, int> _fixes = new();
        private readonly List<string> _order = new();

        public LenientParser(string text)
        {
            if (text.Length > 0 && text[0] == '﻿')
                text = text[1..];

            if (MarkdownFence().Match(text) is { Success: true } fence)
            {
                text = fence.Groups["body"].Value;
                Fix("Removed the Markdown code fence");
            }
            else if (JsonpWrapper().Match(text) is { Success: true } jsonp && !LooksLikeJson(text))
            {
                text = jsonp.Groups["body"].Value;
                Fix("Removed the JSONP callback wrapper");
            }

            _s = text;
            _end = text.Length;
        }

        private static bool LooksLikeJson(string text)
        {
            var t = text.TrimStart();
            return t.Length > 0 && t[0] is '{' or '[' or '"';
        }

        public IReadOnlyList<string> Fixes =>
            _order.Select(f => _fixes[f] > 1 ? $"{f} ({_fixes[f]})" : f).ToList();

        private void Fix(string what)
        {
            if (_fixes.TryGetValue(what, out var n))
                _fixes[what] = n + 1;
            else
            {
                _fixes[what] = 1;
                _order.Add(what);
            }
        }

        public Node ParseDocument()
        {
            SkipTrivia();
            if (_i >= _end)
                throw new FormatException("There is no JSON to repair.");

            var values = new List<Node>();
            while (true)
            {
                SkipTrivia();
                if (_i >= _end)
                    break;
                var c = _s[_i];
                if (c is ']' or '}' or ')' or ';' or ',')
                {
                    _i++;
                    if (c != ',' || values.Count == 0)
                        Fix(Unexpected);
                    continue;
                }

                values.Add(ParseValue());
            }

            if (values.Count == 1)
                return values[0];

            Fix("Wrapped multiple top-level values in an array");
            var array = new ArrayNode();
            array.Items.AddRange(values);
            return array;
        }

        private Node ParseValue()
        {
            while (true)
            {
                SkipTrivia();
                if (_i >= _end)
                {
                    Fix(MissingValue);
                    return Null;
                }

                var c = _s[_i];
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '}' or ']' or ',':
                        Fix(MissingValue);
                        return Null;
                }

                if (IsQuote(c))
                    return new ValueNode(ParseString());
                if (char.IsAsciiDigit(c) || c is '-' or '+' or '.')
                    return ParseNumber();
                if (IsWordChar(c))
                    return ParseWord();

                _i++;
                Fix(Unexpected);
            }
        }

        private void Enter()
        {
            if (++_depth > MaxDepth)
                throw new FormatException($"The JSON is nested more than {MaxDepth} levels deep.");
        }

        private Node ParseObject()
        {
            Enter();
            _i++; // {
            var obj = new ObjectNode();
            var sawComma = false;
            var expecting = true;

            while (true)
            {
                SkipTrivia();
                if (_i >= _end)
                {
                    Fix("Closed unterminated objects and arrays");
                    break;
                }

                var c = _s[_i];
                if (c == '}')
                {
                    _i++;
                    if (sawComma) Fix("Removed trailing commas");
                    break;
                }
                if (c == ']')
                {
                    _i++;
                    Fix("Fixed mismatched brackets");
                    break;
                }
                if (c == ',')
                {
                    _i++;
                    if (obj.Members.Count == 0 || sawComma) Fix("Removed extra commas");
                    sawComma = true;
                    expecting = true;
                    continue;
                }

                string key;
                if (IsQuote(c))
                    key = ParseString();
                else if (IsWordChar(c))
                {
                    var start = _i;
                    while (_i < _end && (IsWordChar(_s[_i]) || _s[_i] is '-' or '.'))
                        _i++;
                    key = Escape(_s[start.._i]);
                    Fix("Quoted unquoted keys");
                }
                else
                {
                    _i++;
                    Fix(Unexpected);
                    continue;
                }

                if (!expecting)
                    Fix("Inserted missing commas");

                SkipTrivia();
                if (_i < _end && _s[_i] is ':' or '=')
                {
                    if (_s[_i] == '=') Fix("Replaced '=' with ':'");
                    _i++;
                }
                else
                {
                    Fix("Inserted missing colons");
                }

                SkipTrivia();
                Node value;
                if (_i >= _end || _s[_i] is ',' or '}' or ']')
                {
                    Fix(MissingValue);
                    value = Null;
                }
                else
                {
                    value = ParseValue();
                }

                obj.Members.Add(new(key, value));
                sawComma = false;
                expecting = false;
            }

            _depth--;
            return obj;
        }

        private Node ParseArray()
        {
            Enter();
            _i++; // [
            var array = new ArrayNode();
            var sawComma = false;
            var expecting = true;

            while (true)
            {
                SkipTrivia();
                if (_i >= _end)
                {
                    Fix("Closed unterminated objects and arrays");
                    break;
                }

                var c = _s[_i];
                if (c == ']')
                {
                    _i++;
                    if (sawComma) Fix("Removed trailing commas");
                    break;
                }
                if (c == '}')
                {
                    _i++;
                    Fix("Fixed mismatched brackets");
                    break;
                }
                if (c == ',')
                {
                    _i++;
                    if (array.Items.Count == 0 || sawComma) Fix("Removed extra commas");
                    sawComma = true;
                    expecting = true;
                    continue;
                }
                if (c == ':')
                {
                    _i++;
                    Fix(Unexpected);
                    continue;
                }

                if (!expecting)
                    Fix("Inserted missing commas");
                array.Items.Add(ParseValue());
                sawComma = false;
                expecting = false;
            }

            _depth--;
            return array;
        }

        private static bool IsQuote(char c) => c is '"' or '\'' or '`' or '“' or '”' or '‘' or '’';

        private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '$';

        private string ParseString()
        {
            var open = _s[_i++];
            var smart = open is '“' or '”' or '‘' or '’';
            var close = open switch
            {
                '“' or '”' => '”',
                '‘' or '’' => '’',
                _ => open
            };
            if (smart) Fix("Replaced curly quotes");
            else if (open == '\'') Fix("Converted single-quoted strings");
            else if (open == '`') Fix("Converted backtick strings");

            var sb = new StringBuilder().Append('"');
            var closed = false;
            while (_i < _end)
            {
                var c = _s[_i];
                if (c == close || (smart && c is '“' or '"'))
                {
                    _i++;
                    closed = true;
                    break;
                }

                if (c == '\\')
                {
                    if (_i + 1 >= _end)
                    {
                        sb.Append("\\\\");
                        _i++;
                        continue;
                    }

                    var n = _s[_i + 1];
                    switch (n)
                    {
                        case '"' or '\\' or '/' or 'b' or 'f' or 'n' or 'r' or 't':
                            sb.Append('\\').Append(n);
                            _i += 2;
                            continue;
                        case 'u' when IsHex4(_i + 2):
                            sb.Append(_s, _i, 6);
                            _i += 6;
                            continue;
                        case '\'' or '`':
                            sb.Append(n);
                            _i += 2;
                            continue;
                        case '\n':
                            _i += 2; // line continuation
                            continue;
                        default:
                            sb.Append("\\\\");
                            _i++;
                            Fix("Escaped stray backslashes");
                            continue;
                    }
                }

                if (c == '"')
                {
                    sb.Append("\\\"");
                    _i++;
                    continue;
                }

                if (c is '\n' or '\r')
                {
                    if (open == '`')
                    {
                        sb.Append(c == '\n' ? "\\n" : "\\r");
                        _i++;
                        continue;
                    }

                    break; // unterminated: close at the end of the line
                }

                if (c < 0x20)
                {
                    AppendEscaped(sb, c);
                    Fix("Escaped control characters");
                    _i++;
                    continue;
                }

                sb.Append(c);
                _i++;
            }

            if (!closed)
                Fix("Closed unterminated strings");
            return sb.Append('"').ToString();
        }

        private bool IsHex4(int at)
        {
            if (at + 4 > _end)
                return false;
            for (var k = at; k < at + 4; k++)
                if (!char.IsAsciiHexDigit(_s[k]))
                    return false;
            return true;
        }

        private Node ParseNumber()
        {
            var start = _i;
            if (_s[_i] is '-' or '+')
            {
                // -Infinity, +Infinity
                if (_i + 1 < _end && char.IsLetter(_s[_i + 1]))
                {
                    _i++;
                    var word = ReadWord();
                    if (word == "Infinity")
                    {
                        Fix("Replaced NaN/Infinity with null");
                        return Null;
                    }

                    _i = start;
                    return ParseWord();
                }
                _i++;
            }

            while (_i < _end)
            {
                var c = _s[_i];
                if (char.IsAsciiHexDigit(c) || c is '.' or '_' or 'x' or 'X' ||
                    (c is '+' or '-' && _s[_i - 1] is 'e' or 'E'))
                    _i++;
                else
                    break;
            }

            // "10px" and similar: keep the whole thing as text
            if (_i < _end && IsWordChar(_s[_i]))
            {
                _i = start;
                return ParseWord();
            }

            var raw = _s[start.._i];
            var normalized = NormalizeNumber(raw);
            if (normalized is null)
            {
                Fix("Quoted invalid numbers");
                return new ValueNode(Escape(raw));
            }

            if (normalized != raw)
                Fix("Fixed number formats");
            return new ValueNode(normalized);
        }

        private static string? NormalizeNumber(string raw)
        {
            if (StrictNumber().IsMatch(raw))
                return raw;

            var s = raw.Replace("_", "");
            var negative = s.StartsWith('-');
            if (s.StartsWith('+') || negative)
                s = s[1..];

            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                BigInteger.TryParse("0" + s[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var hex))
                return (negative ? "-" : "") + hex.ToString(CultureInfo.InvariantCulture);

            if (s.StartsWith('.')) s = "0" + s;               // .5   -> 0.5
            s = s.Replace(".e", "e").Replace(".E", "E");       // 5.e3 -> 5e3
            if (s.EndsWith('.')) s = s[..^1];                   // 5.   -> 5
            var intEnd = s.IndexOfAny(['.', 'e', 'E']);
            var intPart = intEnd < 0 ? s : s[..intEnd];
            var trimmed = intPart.TrimStart('0');
            if (trimmed.Length == 0) trimmed = "0";
            s = trimmed + (intEnd < 0 ? "" : s[intEnd..]);     // 007 -> 7

            s = (negative ? "-" : "") + s;
            return StrictNumber().IsMatch(s) ? s : null;
        }

        private string ReadWord()
        {
            var start = _i;
            while (_i < _end && IsWordChar(_s[_i]))
                _i++;
            return _s[start.._i];
        }

        private Node ParseWord()
        {
            var start = _i;
            var word = ReadWord();
            switch (word)
            {
                case "true" or "false" or "null":
                    return new ValueNode(word);
                case "True" or "TRUE":
                    Fix("Converted True/False/None");
                    return new ValueNode("true");
                case "False" or "FALSE":
                    Fix("Converted True/False/None");
                    return new ValueNode("false");
                case "None" or "NULL" or "Null" or "nil" or "undefined":
                    Fix("Converted True/False/None");
                    return Null;
                case "NaN" or "Infinity":
                    Fix("Replaced NaN/Infinity with null");
                    return Null;
            }

            // Unquoted text: take everything up to the next delimiter on this line.
            _i = start;
            while (_i < _end && _s[_i] is not (',' or '}' or ']' or '\n' or '\r') &&
                   !(_s[_i] == '/' && _i + 1 < _end && _s[_i + 1] is '/' or '*'))
                _i++;
            Fix("Quoted unquoted text");
            return new ValueNode(Escape(_s[start.._i].TrimEnd()));
        }

        private void SkipTrivia()
        {
            while (_i < _end)
            {
                var c = _s[_i];
                if (char.IsWhiteSpace(c) || c == '﻿')
                {
                    _i++;
                }
                else if (c == '/' && _i + 1 < _end && _s[_i + 1] == '/' || c == '#')
                {
                    while (_i < _end && _s[_i] != '\n')
                        _i++;
                    Fix("Removed comments");
                }
                else if (c == '/' && _i + 1 < _end && _s[_i + 1] == '*')
                {
                    var close = _s.IndexOf("*/", _i + 2, StringComparison.Ordinal);
                    _i = close < 0 ? _end : close + 2;
                    Fix("Removed comments");
                }
                else
                {
                    break;
                }
            }
        }
    }
}
