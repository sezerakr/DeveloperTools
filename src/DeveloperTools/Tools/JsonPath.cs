using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeveloperTools.Tools;

public sealed record JsonPathMatch(string Path, JsonNode? Value);

/// <summary>
/// JSONPath subset: $, .name, ['name'], [n], [-n], [*], .*, ..name, ..*, [a,b], [start:end:step],
/// filters [?(@.x == 1)], [?(@.x)], with ==, !=, &lt;, &lt;=, &gt;, &gt;=, =~ (regex) and &amp;&amp; / ||.
/// </summary>
public static class JsonPath
{
    public static JsonNode? Parse(string json) =>
        JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { MaxDepth = JsonTools.MaxDepth, AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });

    /// <exception cref="FormatException">Invalid path.</exception>
    public static List<JsonPathMatch> Evaluate(JsonNode? root, string path)
    {
        path = path.Trim();
        if (path.Length == 0 || path == "$")
            return [new JsonPathMatch("$", root)];
        if (path[0] != '$')
            path = "$" + (path[0] is '.' or '[' ? "" : ".") + path;

        var current = new List<JsonPathMatch> { new("$", root) };
        var i = 1;
        while (i < path.Length)
        {
            if (path[i] == '.' && i + 1 < path.Length && path[i + 1] == '.')
            {
                i += 2;
                var descendants = current.SelectMany(m => Descend(m)).ToList();
                if (i < path.Length && path[i] == '[')
                {
                    current = descendants;
                    continue;
                }
                var name = ReadName(path, ref i);
                current = descendants.SelectMany(m => Child(m, name)).ToList();
            }
            else if (path[i] == '.')
            {
                i++;
                var name = ReadName(path, ref i);
                current = current.SelectMany(m => Child(m, name)).ToList();
            }
            else if (path[i] == '[')
            {
                var close = FindClose(path, i);
                var inner = path[(i + 1)..close].Trim();
                i = close + 1;
                current = current.SelectMany(m => Bracket(m, inner)).ToList();
            }
            else
            {
                throw new FormatException($"Unexpected '{path[i]}' at position {i + 1}. Use .name or [index].");
            }
        }

        return current;
    }

    private static string ReadName(string path, ref int i)
    {
        var start = i;
        while (i < path.Length && path[i] is not ('.' or '['))
            i++;
        var name = path[start..i].Trim();
        if (name.Length == 0)
            throw new FormatException($"A name is missing at position {start + 1}.");
        return name;
    }

    private static int FindClose(string path, int open)
    {
        var depth = 0;
        char? quote = null;
        for (var k = open; k < path.Length; k++)
        {
            var c = path[k];
            if (quote is not null)
            {
                if (c == '\\') k++;
                else if (c == quote) quote = null;
                continue;
            }
            if (c is '\'' or '"') quote = c;
            else if (c == '[') depth++;
            else if (c == ']' && --depth == 0) return k;
        }
        throw new FormatException($"The '[' at position {open + 1} is not closed.");
    }

    private static IEnumerable<JsonPathMatch> Child(JsonPathMatch m, string name)
    {
        if (name == "*")
        {
            foreach (var c in Children(m)) yield return c;
            yield break;
        }
        if (m.Value is JsonObject obj && obj.TryGetPropertyValue(name, out var v))
            yield return new JsonPathMatch(m.Path + Key(name), v);
    }

    private static string Key(string name) =>
        name.All(c => char.IsLetterOrDigit(c) || c == '_') && name.Length > 0 && !char.IsDigit(name[0])
            ? "." + name
            : "['" + name.Replace("'", "\\'") + "']";

    private static IEnumerable<JsonPathMatch> Children(JsonPathMatch m)
    {
        switch (m.Value)
        {
            case JsonObject obj:
                foreach (var (k, v) in obj) yield return new JsonPathMatch(m.Path + Key(k), v);
                break;
            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++) yield return new JsonPathMatch($"{m.Path}[{i}]", arr[i]);
                break;
        }
    }

    private static IEnumerable<JsonPathMatch> Descend(JsonPathMatch m)
    {
        yield return m;
        foreach (var c in Children(m))
            foreach (var d in Descend(c))
                yield return d;
    }

    private static IEnumerable<JsonPathMatch> Bracket(JsonPathMatch m, string inner)
    {
        if (inner == "*")
            return Children(m);

        if (inner.StartsWith('?'))
        {
            var expr = inner[1..].Trim();
            if (expr.StartsWith('(') && expr.EndsWith(')')) expr = expr[1..^1];
            return Children(m).Where(c => Filter(c.Value, expr));
        }

        if (inner.Contains(':') && inner[0] is not ('\'' or '"'))
            return Slice(m, inner);

        var results = new List<JsonPathMatch>();
        foreach (var part in SplitTop(inner, ','))
        {
            var p = part.Trim();
            if (p.Length >= 2 && p[0] is '\'' or '"' && p[^1] == p[0])
                results.AddRange(Child(m, Unquote(p)));
            else if (int.TryParse(p, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var index))
            {
                if (m.Value is JsonArray arr)
                {
                    var k = index < 0 ? arr.Count + index : index;
                    if (k >= 0 && k < arr.Count) results.Add(new JsonPathMatch($"{m.Path}[{k}]", arr[k]));
                }
            }
            else
                throw new FormatException($"[{inner}] is not an index, a quoted name, * or a filter.");
        }
        return results;
    }

    private static IEnumerable<JsonPathMatch> Slice(JsonPathMatch m, string inner)
    {
        if (m.Value is not JsonArray arr)
            yield break;
        var parts = inner.Split(':');
        int? P(int k) => k < parts.Length && parts[k].Trim().Length > 0 ? int.Parse(parts[k].Trim(), CultureInfo.InvariantCulture) : null;
        var step = P(2) ?? 1;
        if (step == 0) throw new FormatException("A slice step cannot be 0.");
        int Norm(int v) => v < 0 ? Math.Max(0, arr.Count + v) : Math.Min(v, arr.Count);
        var start = P(0) is { } s ? Norm(s) : step > 0 ? 0 : arr.Count - 1;
        var end = P(1) is { } e ? Norm(e) : step > 0 ? arr.Count : -1;
        for (var i = start; step > 0 ? i < end : i > end; i += step)
            if (i >= 0 && i < arr.Count)
                yield return new JsonPathMatch($"{m.Path}[{i}]", arr[i]);
    }

    private static string Unquote(string s) => s[1..^1].Replace("\\'", "'").Replace("\\\"", "\"");

    private static List<string> SplitTop(string s, char separator)
    {
        var parts = new List<string>();
        char? quote = null;
        var depth = 0;
        var start = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (quote is not null) { if (c == quote) quote = null; continue; }
            if (c is '\'' or '"') quote = c;
            else if (c is '(' or '[') depth++;
            else if (c is ')' or ']') depth--;
            else if (c == separator && depth == 0)
            {
                parts.Add(s[start..i]);
                start = i + 1;
            }
        }
        parts.Add(s[start..]);
        return parts;
    }

    // ------------------------------------------------------------------ filters

    private static bool Filter(JsonNode? item, string expr)
    {
        var ors = SplitLogical(expr, "||");
        if (ors.Count > 1) return ors.Any(e => Filter(item, e));
        var ands = SplitLogical(expr, "&&");
        if (ands.Count > 1) return ands.All(e => Filter(item, e));

        expr = expr.Trim();
        if (expr.StartsWith('(') && expr.EndsWith(')'))
            return Filter(item, expr[1..^1]);

        foreach (var op in new[] { "==", "!=", "<=", ">=", "=~", "<", ">" })
        {
            var at = IndexOutsideQuotes(expr, op);
            if (at < 0) continue;
            var left = Operand(item, expr[..at].Trim());
            var right = Operand(item, expr[(at + op.Length)..].Trim());
            return Compare(left, right, op);
        }

        // Existence: [?(@.name)]
        var value = Operand(item, expr);
        return value.exists && value.node is not null && !(value.node is JsonValue v && v.TryGetValue<bool>(out var b) && !b);
    }

    private static List<string> SplitLogical(string expr, string op)
    {
        var parts = new List<string>();
        char? quote = null;
        var depth = 0;
        var start = 0;
        for (var i = 0; i < expr.Length; i++)
        {
            var c = expr[i];
            if (quote is not null) { if (c == quote) quote = null; continue; }
            if (c is '\'' or '"') quote = c;
            else if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (depth == 0 && string.CompareOrdinal(expr, i, op, 0, op.Length) == 0)
            {
                parts.Add(expr[start..i]);
                start = i + op.Length;
                i += op.Length - 1;
            }
        }
        parts.Add(expr[start..]);
        return parts;
    }

    private static int IndexOutsideQuotes(string s, string op)
    {
        char? quote = null;
        for (var i = 0; i + op.Length <= s.Length; i++)
        {
            var c = s[i];
            if (quote is not null) { if (c == quote) quote = null; continue; }
            if (c is '\'' or '"') { quote = c; continue; }
            if (string.CompareOrdinal(s, i, op, 0, op.Length) == 0)
            {
                // "<" must not be the start of "<="
                if (op.Length == 1 && i + 1 < s.Length && s[i + 1] == '=') continue;
                return i;
            }
        }
        return -1;
    }

    private static (bool exists, JsonNode? node, string? literal) Operand(JsonNode? item, string text)
    {
        if (text.StartsWith('@'))
        {
            var path = "$" + text[1..];
            var found = Evaluate(item, path);
            return found.Count == 0 ? (false, null, null) : (true, found[0].Value, null);
        }
        if (text.Length >= 2 && text[0] is '\'' or '"' && text[^1] == text[0])
            return (true, JsonValue.Create(Unquote(text)), null);
        if (text.StartsWith('/') && text.LastIndexOf('/') > 0)
            return (true, null, text); // regex literal
        try
        {
            return (true, JsonNode.Parse(text), null);
        }
        catch (JsonException)
        {
            throw new FormatException($"Cannot read '{text}' in the filter. Quote text values: 'value'.");
        }
    }

    private static bool Compare((bool exists, JsonNode? node, string? literal) l, (bool exists, JsonNode? node, string? literal) r, string op)
    {
        if (op == "=~")
        {
            var pattern = r.literal ?? (r.node as JsonValue)?.ToString() ?? "";
            var options = System.Text.RegularExpressions.RegexOptions.None;
            if (pattern.StartsWith('/'))
            {
                var last = pattern.LastIndexOf('/');
                if (pattern[(last + 1)..].Contains('i')) options |= System.Text.RegularExpressions.RegexOptions.IgnoreCase;
                pattern = pattern[1..last];
            }
            return l.node is JsonValue lv && lv.TryGetValue<string>(out var s) &&
                   System.Text.RegularExpressions.Regex.IsMatch(s, pattern, options, TimeSpan.FromSeconds(1));
        }

        if (!l.exists)
            return op == "!=";

        var a = l.node;
        var b = r.node;
        if (a is JsonValue av && b is JsonValue bv)
        {
            if (av.TryGetValue<double>(out var x) && bv.TryGetValue<double>(out var y))
                return op switch { "==" => x == y, "!=" => x != y, "<" => x < y, "<=" => x <= y, ">" => x > y, ">=" => x >= y, _ => false };
            if (av.TryGetValue<string>(out var sa) && bv.TryGetValue<string>(out var sb))
            {
                var c = string.CompareOrdinal(sa, sb);
                return op switch { "==" => c == 0, "!=" => c != 0, "<" => c < 0, "<=" => c <= 0, ">" => c > 0, ">=" => c >= 0, _ => false };
            }
        }

        var equal = JsonNode.DeepEquals(a, b);
        return op switch { "==" => equal, "!=" => !equal, _ => false };
    }
}
