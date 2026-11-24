using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace DeveloperTools.Tools;

public enum CodeTarget
{
    CSharp,
    Kotlin,
    TypeScript
}

/// <summary>Generates C#, Kotlin or TypeScript types from a JSON sample.</summary>
public static class JsonCodeGen
{
    private abstract class Shape;

    private sealed class Prim(string kind) : Shape
    {
        public string Kind = kind; // string, int, long, double, bool, null, any
    }

    private sealed class Arr(Shape item) : Shape
    {
        public Shape Item = item;
    }

    private sealed class Obj : Shape
    {
        public string Name = "Root";
        public readonly List<string> Order = new();
        public readonly Dictionary<string, (Shape Shape, bool Optional)> Fields = new();
        public int Samples = 1;
    }

    private sealed class Nullable(Shape inner) : Shape
    {
        public Shape Inner = inner;
    }

    public static string Generate(string json, CodeTarget target, string rootName = "Root")
    {
        var node = JsonPath.Parse(json);
        var shape = Infer(node);
        var classes = new List<Obj>();
        var names = new HashSet<string>(StringComparer.Ordinal);

        if (shape is Arr { Item: Obj } rootArray)
            Name((Obj)rootArray.Item, Singular(rootName), classes, names);
        else if (shape is Obj obj)
            Name(obj, rootName, classes, names);
        else
            throw new FormatException("Use a JSON object (or an array of objects) to generate types.");

        return target switch
        {
            CodeTarget.CSharp => CSharp(classes),
            CodeTarget.Kotlin => Kotlin(classes),
            _ => TypeScript(classes)
        };
    }

    // ------------------------------------------------------------------ inference

    private static Shape Infer(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return new Prim("null");
            case JsonObject o:
            {
                var obj = new Obj();
                foreach (var (k, v) in o)
                {
                    if (obj.Fields.ContainsKey(k)) continue;
                    obj.Order.Add(k);
                    obj.Fields[k] = (Infer(v), false);
                }
                return obj;
            }
            case JsonArray a:
            {
                Shape? item = null;
                foreach (var v in a)
                    item = item is null ? Infer(v) : Merge(item, Infer(v));
                return new Arr(item ?? new Prim("any"));
            }
            case JsonValue v:
                return v.GetValueKind() switch
                {
                    JsonValueKind.String => new Prim("string"),
                    JsonValueKind.True or JsonValueKind.False => new Prim("bool"),
                    JsonValueKind.Number => v.TryGetValue<int>(out _) ? new Prim("int")
                        : v.TryGetValue<long>(out _) ? new Prim("long") : new Prim("double"),
                    _ => new Prim("null")
                };
        }
        return new Prim("any");
    }

    private static Shape Merge(Shape a, Shape b)
    {
        if (a is Prim { Kind: "null" }) return MakeNullable(b);
        if (b is Prim { Kind: "null" }) return MakeNullable(a);
        if (a is Nullable na) return MakeNullable(Merge(na.Inner, b is Nullable nb0 ? nb0.Inner : b));
        if (b is Nullable nb) return MakeNullable(Merge(a, nb.Inner));

        if (a is Prim pa && b is Prim pb)
        {
            if (pa.Kind == pb.Kind) return pa;
            var nums = new[] { "int", "long", "double" };
            if (nums.Contains(pa.Kind) && nums.Contains(pb.Kind))
                return new Prim(Array.IndexOf(nums, pa.Kind) > Array.IndexOf(nums, pb.Kind) ? pa.Kind : pb.Kind);
            return new Prim("any");
        }
        if (a is Arr aa && b is Arr ab)
            return new Arr(Merge(aa.Item, ab.Item));
        if (a is Obj oa && b is Obj ob)
        {
            var merged = new Obj { Samples = oa.Samples + ob.Samples };
            foreach (var k in oa.Order.Concat(ob.Order).Distinct())
            {
                merged.Order.Add(k);
                var inA = oa.Fields.TryGetValue(k, out var fa);
                var inB = ob.Fields.TryGetValue(k, out var fb);
                merged.Fields[k] = inA && inB
                    ? (Merge(fa.Shape, fb.Shape), fa.Optional || fb.Optional)
                    : (inA ? fa.Shape : fb.Shape, true);
            }
            return merged;
        }
        return new Prim("any");
    }

    private static Shape MakeNullable(Shape s) => s is Nullable or Prim { Kind: "null" or "any" } ? s : new Nullable(s);

    private static void Name(Obj obj, string name, List<Obj> classes, HashSet<string> names)
    {
        var unique = name;
        for (var n = 2; !names.Add(unique); n++) unique = name + n;
        obj.Name = unique;
        classes.Add(obj);
        foreach (var key in obj.Order)
            NameNested(obj.Fields[key].Shape, Pascal(key), classes, names);
    }

    private static void NameNested(Shape s, string name, List<Obj> classes, HashSet<string> names)
    {
        switch (s)
        {
            case Obj o: Name(o, name, classes, names); break;
            case Arr a: NameNested(a.Item, Singular(name), classes, names); break;
            case Nullable n: NameNested(n.Inner, name, classes, names); break;
        }
    }

    private static string Pascal(string key)
    {
        var words = TextTools.Words(key);
        var name = string.Concat(words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
        if (name.Length == 0) name = "Field";
        if (char.IsDigit(name[0])) name = "_" + name;
        return name;
    }

    private static string Camel(string key)
    {
        var p = Pascal(key);
        return p.StartsWith('_') ? p : char.ToLowerInvariant(p[0]) + p[1..];
    }

    private static string Singular(string name) =>
        name.EndsWith("ies") && name.Length > 3 ? name[..^3] + "y"
        : name.EndsWith("ses") || name.EndsWith("xes") ? name[..^2]
        : name.EndsWith('s') && !name.EndsWith("ss") && name.Length > 1 ? name[..^1]
        : name + "Item";

    // ------------------------------------------------------------------ writers

    private static readonly HashSet<string> CSharpKeywords = new(["class", "event", "object", "string", "int", "long", "double", "bool", "params", "namespace", "operator", "default", "base", "public", "private", "static", "void", "new", "this", "lock", "ref", "out", "in", "is", "as", "fixed", "checked", "decimal", "float", "char", "byte", "case", "switch", "return", "if", "else", "for", "foreach", "while", "do", "using", "struct", "enum", "interface", "true", "false", "null"]);

    private static string CSharp(List<Obj> classes)
    {
        var sb = new StringBuilder("using System.Collections.Generic;\nusing System.Text.Json.Serialization;\n\n" +
            "// Names that only differ in casing match with JsonSerializerOptions.Web (camelCase, case-insensitive).\n");
        foreach (var c in classes)
        {
            sb.Append($"\npublic class {c.Name}\n{{\n");
            foreach (var key in c.Order)
            {
                var (shape, optional) = c.Fields[key];
                var prop = Pascal(key);
                if (prop == c.Name) prop += "Value";
                var type = CsType(shape);
                if (optional && !type.EndsWith('?')) type += "?";
                if (!prop.Equals(key, StringComparison.OrdinalIgnoreCase)) sb.Append($"    [JsonPropertyName(\"{key.Replace("\"", "\\\"")}\")]\n");
                var init = type.EndsWith('?') ? "" : type == "string" ? " = \"\";" : type.StartsWith("List<") ? " = new();" : IsRef(shape) ? " = new();" : "";
                sb.Append($"    public {type} {prop} {{ get; set; }}{init}\n");
                if (key != c.Order[^1]) sb.Append('\n');
            }
            sb.Append("}\n");
        }
        return sb.ToString();
    }

    private static bool IsRef(Shape s) => s is Obj;

    private static string CsType(Shape s) => s switch
    {
        Prim p => p.Kind switch { "string" => "string", "int" => "int", "long" => "long", "double" => "double", "bool" => "bool", _ => "object?" },
        Arr a => $"List<{CsType(a.Item)}>",
        Obj o => o.Name,
        Nullable n => CsType(n.Inner).TrimEnd('?') + "?",
        _ => "object?"
    };

    private static string Kotlin(List<Obj> classes)
    {
        var sb = new StringBuilder("import kotlinx.serialization.SerialName\nimport kotlinx.serialization.Serializable\n");
        foreach (var c in classes)
        {
            sb.Append($"\n@Serializable\ndata class {c.Name}(\n");
            for (var i = 0; i < c.Order.Count; i++)
            {
                var key = c.Order[i];
                var (shape, optional) = c.Fields[key];
                var name = Camel(key);
                var type = KtType(shape);
                if (optional && !type.EndsWith('?')) type += "?";
                var serial = name != key ? $"@SerialName(\"{key.Replace("\"", "\\\"")}\") " : "";
                var def = type.EndsWith('?') ? " = null" : "";
                sb.Append($"    {serial}val {Ticks(name)}: {type}{def}{(i < c.Order.Count - 1 ? "," : "")}\n");
            }
            sb.Append(")\n");
        }
        var code = sb.ToString();
        return code.Contains("JsonElement")
            ? code.Replace("import kotlinx.serialization.Serializable\n", "import kotlinx.serialization.Serializable\nimport kotlinx.serialization.json.JsonElement\n")
            : code;
    }

    private static string Ticks(string name) =>
        name is "val" or "var" or "fun" or "class" or "object" or "in" or "is" or "as" or "when" or "if" or "else" or "return" or "package" or "interface" or "typealias" or "null" or "true" or "false" or "this" or "super" or "throw" or "try" or "do" or "while" or "for" or "break" or "continue"
            ? $"`{name}`" : name;

    private static string KtType(Shape s) => s switch
    {
        Prim p => p.Kind switch { "string" => "String", "int" => "Int", "long" => "Long", "double" => "Double", "bool" => "Boolean", _ => "JsonElement?" },
        Arr a => $"List<{KtType(a.Item)}>",
        Obj o => o.Name,
        Nullable n => KtType(n.Inner).TrimEnd('?') + "?",
        _ => "JsonElement?"
    };

    private static string TypeScript(List<Obj> classes)
    {
        var sb = new StringBuilder();
        foreach (var c in classes)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append($"export interface {c.Name} {{\n");
            foreach (var key in c.Order)
            {
                var (shape, optional) = c.Fields[key];
                var name = key.All(ch => char.IsLetterOrDigit(ch) || ch is '_' or '$') && !char.IsDigit(key[0]) ? key : $"\"{key.Replace("\"", "\\\"")}\"";
                sb.Append($"  {name}{(optional ? "?" : "")}: {TsType(shape)};\n");
            }
            sb.Append("}\n");
        }
        return sb.ToString();
    }

    private static string TsType(Shape s) => s switch
    {
        Prim p => p.Kind switch { "string" => "string", "int" or "long" or "double" => "number", "bool" => "boolean", "null" => "unknown | null", _ => "unknown" },
        Arr a => a.Item is Nullable or Prim { Kind: "any" } ? $"({TsType(a.Item)})[]" : $"{TsType(a.Item)}[]",
        Obj o => o.Name,
        Nullable n => TsType(n.Inner) + " | null",
        _ => "unknown"
    };
}

/// <summary>JSON ⇄ YAML using YamlDotNet's node model (keeps key order; strings that look like other types are quoted).</summary>
public static class YamlTools
{
    public static string JsonToYaml(string json)
    {
        var node = JsonPath.Parse(json);
        var stream = new YamlStream(new YamlDocument(ToYaml(node)));
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        stream.Save(writer, assignAnchors: false);
        var text = writer.ToString().Replace("\r\n", "\n");
        if (text.EndsWith("...\n")) text = text[..^4];
        return text.TrimEnd() + "\n";
    }

    private static YamlNode ToYaml(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return new YamlScalarNode("null") { Style = ScalarStyle.Plain };
            case JsonObject o:
            {
                var map = new YamlMappingNode();
                foreach (var (k, v) in o)
                    map.Add(new YamlScalarNode(k) { Style = NeedsQuotes(k) ? ScalarStyle.DoubleQuoted : ScalarStyle.Plain }, ToYaml(v));
                return map;
            }
            case JsonArray a:
            {
                var seq = new YamlSequenceNode();
                foreach (var v in a) seq.Add(ToYaml(v));
                return seq;
            }
            case JsonValue v:
                var kind = v.GetValueKind();
                if (kind == JsonValueKind.String)
                {
                    var s = v.GetValue<string>();
                    var style = s.Contains('\n') ? ScalarStyle.Literal : NeedsQuotes(s) ? ScalarStyle.DoubleQuoted : ScalarStyle.Plain;
                    return new YamlScalarNode(s) { Style = style };
                }
                return new YamlScalarNode(v.ToJsonString()) { Style = ScalarStyle.Plain };
        }
        return new YamlScalarNode("null");
    }

    private static bool NeedsQuotes(string s) =>
        s.Length == 0 || IsPlainNonString(s) || s.Trim() != s ||
        s.IndexOfAny([':', '#', '{', '}', '[', ']', ',', '&', '*', '!', '|', '>', '\'', '"', '%', '@', '`']) >= 0 ||
        s.StartsWith('-') || s.StartsWith('?');

    private static bool IsPlainNonString(string s) =>
        s is "null" or "Null" or "NULL" or "~" or "true" or "True" or "TRUE" or "false" or "False" or "FALSE"
            or "yes" or "no" or "on" or "off" or "Yes" or "No" or "On" or "Off" ||
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ||
        s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || s is ".inf" or "-.inf" or ".nan";

    public static string YamlToJson(string yaml, string indent = "  ")
    {
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException ex)
        {
            throw new FormatException($"Line {ex.Start.Line}, column {ex.Start.Column}: {ex.InnerException?.Message ?? ex.Message}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            throw new FormatException("The YAML is not valid. Check brackets, quotes and indentation.");
        }

        if (stream.Documents.Count == 0)
            throw new FormatException("There is no YAML to convert.");
        var sb = new StringBuilder();
        if (stream.Documents.Count == 1)
            Write(sb, stream.Documents[0].RootNode, indent, 0);
        else
        {
            sb.Append('[');
            for (var i = 0; i < stream.Documents.Count; i++)
            {
                sb.Append(i == 0 ? "\n" + indent : ",\n" + indent);
                Write(sb, stream.Documents[i].RootNode, indent, 1);
            }
            sb.Append("\n]");
        }
        return sb.ToString();
    }

    private static void Write(StringBuilder sb, YamlNode node, string indent, int depth)
    {
        string Pad(int d) => "\n" + string.Concat(Enumerable.Repeat(indent, d));
        switch (node)
        {
            case YamlMappingNode map:
                if (map.Children.Count == 0) { sb.Append("{}"); return; }
                sb.Append('{');
                var first = true;
                foreach (var (k, v) in map.Children)
                {
                    sb.Append(first ? "" : ",").Append(Pad(depth + 1));
                    first = false;
                    sb.Append(JsonTools.Escape(k is YamlScalarNode ks ? ks.Value ?? "" : k.ToString())).Append(": ");
                    Write(sb, v, indent, depth + 1);
                }
                sb.Append(Pad(depth)).Append('}');
                break;
            case YamlSequenceNode seq:
                if (seq.Children.Count == 0) { sb.Append("[]"); return; }
                sb.Append('[');
                for (var i = 0; i < seq.Children.Count; i++)
                {
                    sb.Append(i == 0 ? "" : ",").Append(Pad(depth + 1));
                    Write(sb, seq.Children[i], indent, depth + 1);
                }
                sb.Append(Pad(depth)).Append(']');
                break;
            case YamlScalarNode scalar:
                sb.Append(Scalar(scalar));
                break;
            default:
                sb.Append("null");
                break;
        }
    }

    /// <summary>YAML 1.2 core schema for plain scalars; quoted and block scalars are always strings.</summary>
    private static string Scalar(YamlScalarNode s)
    {
        var v = s.Value ?? "";
        if (s.Style is not (ScalarStyle.Plain or ScalarStyle.Any))
            return JsonTools.Escape(v);
        if (!s.Tag.IsEmpty && !s.Tag.IsNonSpecific && s.Tag.Value is "tag:yaml.org,2002:str" or "!!str")
            return JsonTools.Escape(v);
        switch (v)
        {
            case "" or "~" or "null" or "Null" or "NULL": return "null";
            case "true" or "True" or "TRUE": return "true";
            case "false" or "False" or "FALSE": return "false";
        }
        if (System.Text.RegularExpressions.Regex.IsMatch(v, @"^[-+]?(0|[1-9][0-9]*)$"))
            return v.TrimStart('+');
        if (System.Text.RegularExpressions.Regex.IsMatch(v, @"^[-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?$") &&
            double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return JsonTools.Format(d.ToString("R", CultureInfo.InvariantCulture), null);
        if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(v[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
            return hex.ToString(CultureInfo.InvariantCulture);
        return JsonTools.Escape(v);
    }
}
