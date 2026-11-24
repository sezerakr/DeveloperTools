using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DeveloperTools.Markdown;

public enum TokenKind
{
    Keyword,
    Type,
    String,
    Number,
    Comment,
    Function,
    Annotation,
    Constant,
    Property,
    Tag,
    Attribute
}

public readonly record struct CodeToken(int Start, int Length, TokenKind Kind);

/// <summary>
/// Small, dependency-free syntax tokenizer for fenced code blocks.
/// Handles comments, strings, numbers, keywords, types, calls and annotations for C-like languages,
/// plus dedicated scanners for JSON, YAML, markup (XML/HTML/XAML) and CSS.
/// Used by both the preview and the editor so colours match.
/// </summary>
public static partial class CodeHighlighter
{
    private enum Mode { Generic, Json, Yaml, Markup, Css }

    private sealed class Lang
    {
        public required string Name;
        public Mode Mode = Mode.Generic;
        public HashSet<string> Keywords = new();
        public HashSet<string> Literals = new();
        public HashSet<string> Types = new();
        public string? LineComment;
        public (string Open, string Close)? BlockComment;
        public bool CaseInsensitive;
        public bool Annotations;
        public bool TripleQuotes;
        public bool Backtick;
        public bool SingleQuoteStrings = true;
        public bool DollarVariables;
        public bool VerbatimStrings; // C# @"..." and $"..."
        public bool CmdletNames;      // PowerShell Verb-Noun
    }

    private static HashSet<string> Words(string words, bool ignoreCase = false) =>
        new(words.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private const string CFamilyLiterals = "true false null";

    private static readonly Dictionary<string, Lang> Languages = BuildLanguages();

    private static Dictionary<string, Lang> BuildLanguages()
    {
        var csharp = new Lang
        {
            Name = "C#",
            Keywords = Words("abstract as async await base break case catch checked class const continue default delegate do else enum event explicit extern finally fixed for foreach get goto if implicit in init interface internal is lock nameof namespace new operator out override params partial private protected public readonly record ref required return sealed set sizeof stackalloc static struct switch this throw try typeof unchecked unsafe using var virtual volatile when where while with yield value file scoped global not and or let from select orderby group into join"),
            Types = Words("bool byte sbyte char decimal double float int uint long ulong short ushort object string void dynamic nint nuint"),
            Literals = Words(CFamilyLiterals),
            LineComment = "//", BlockComment = ("/*", "*/"),
            TripleQuotes = true, VerbatimStrings = true
        };
        var kotlin = new Lang
        {
            Name = "Kotlin",
            Keywords = Words("abstract actual annotation as break by catch class companion const constructor continue crossinline data do dynamic else enum expect external final finally for fun get if import in infix init inline inner interface internal is lateinit noinline object open operator out override package private protected public reified return sealed set super suspend tailrec this throw try typealias typeof val var vararg when where while value field it"),
            Types = Words("Any Unit Nothing Int Long Short Byte Double Float Boolean Char String Array List MutableList Map MutableMap Set MutableSet"),
            Literals = Words(CFamilyLiterals),
            LineComment = "//", BlockComment = ("/*", "*/"),
            TripleQuotes = true, Annotations = true
        };
        var java = new Lang
        {
            Name = "Java",
            Keywords = Words("abstract assert break case catch class const continue default do else enum extends final finally for goto if implements import instanceof interface native new package private protected public return static strictfp super switch synchronized this throw throws transient try var volatile while record sealed permits yield non-sealed"),
            Types = Words("boolean byte char double float int long short void"),
            Literals = Words(CFamilyLiterals),
            LineComment = "//", BlockComment = ("/*", "*/"),
            TripleQuotes = true, Annotations = true
        };
        var javascript = new Lang
        {
            Name = "JavaScript",
            Keywords = Words("async await break case catch class const continue debugger default delete do else export extends finally for from function get if import in instanceof let new of return set static super switch this throw try typeof var void while with yield"),
            Literals = Words("true false null undefined NaN Infinity"),
            LineComment = "//", BlockComment = ("/*", "*/"),
            Backtick = true, Annotations = true
        };
        var typescript = new Lang
        {
            Name = "TypeScript",
            Keywords = new HashSet<string>(javascript.Keywords.Concat(Words("abstract as asserts declare enum implements infer interface keyof module namespace private protected public readonly require satisfies type unique override is"))),
            Types = Words("any bigint boolean never number object string symbol unknown void"),
            Literals = javascript.Literals,
            LineComment = "//", BlockComment = ("/*", "*/"),
            Backtick = true, Annotations = true
        };
        var python = new Lang
        {
            Name = "Python",
            Keywords = Words("and as assert async await break class continue def del elif else except finally for from global if import in is lambda nonlocal not or pass raise return try while with yield match case self cls"),
            Types = Words("int float str bool list dict set tuple bytes object"),
            Literals = Words("True False None"),
            LineComment = "#", TripleQuotes = true, Annotations = true
        };
        var go = new Lang
        {
            Name = "Go",
            Keywords = Words("break case chan const continue default defer else fallthrough for func go goto if import interface map package range return select struct switch type var"),
            Types = Words("bool byte complex64 complex128 error float32 float64 int int8 int16 int32 int64 rune string uint uint8 uint16 uint32 uint64 uintptr any"),
            Literals = Words("true false nil iota"),
            LineComment = "//", BlockComment = ("/*", "*/"), Backtick = true
        };
        var rust = new Lang
        {
            Name = "Rust",
            Keywords = Words("as async await break const continue crate dyn else enum extern fn for if impl in let loop match mod move mut pub ref return self Self static struct super trait type unsafe use where while"),
            Types = Words("bool char f32 f64 i8 i16 i32 i64 i128 isize str u8 u16 u32 u64 u128 usize String Vec Option Result Box"),
            Literals = Words("true false None Some Ok Err"),
            LineComment = "//", BlockComment = ("/*", "*/"), SingleQuoteStrings = false
        };
        var swift = new Lang
        {
            Name = "Swift",
            Keywords = Words("associatedtype class deinit enum extension fileprivate func import init inout internal let open operator private protocol public rethrows static struct subscript typealias var break case continue default defer do else fallthrough for guard if in repeat return switch where while as catch is super self Self throw throws try async await some any final override mutating lazy weak"),
            Literals = Words("true false nil"),
            LineComment = "//", BlockComment = ("/*", "*/"), TripleQuotes = true, Annotations = true
        };
        var dart = new Lang
        {
            Name = "Dart",
            Keywords = Words("abstract as assert async await break case catch class const continue covariant default deferred do dynamic else enum export extends extension external factory final finally for get if implements import in interface is late library mixin new on operator part required rethrow return set show static super switch sync this throw try typedef var void while with yield"),
            Types = Words("int double num bool String List Map Set Future Stream"),
            Literals = Words(CFamilyLiterals),
            LineComment = "//", BlockComment = ("/*", "*/"), TripleQuotes = true, Annotations = true
        };
        var cpp = new Lang
        {
            Name = "C/C++",
            Keywords = Words("auto break case const continue default do else enum extern for goto if inline register return signed sizeof static struct switch typedef union unsigned volatile while class namespace template typename public private protected virtual override new delete this using try catch throw const_cast static_cast dynamic_cast reinterpret_cast constexpr noexcept friend explicit operator include define ifdef ifndef endif pragma"),
            Types = Words("bool char double float int long short void size_t std string vector"),
            Literals = Words("true false nullptr NULL"),
            LineComment = "//", BlockComment = ("/*", "*/")
        };
        var sql = new Lang
        {
            Name = "SQL",
            CaseInsensitive = true,
            Keywords = Words("select from where and or not insert into values update set delete create table alter drop index view join inner left right outer full cross on as group by order having limit offset distinct union all is in like between exists case when then else end primary key foreign references default begin commit rollback transaction with returning asc desc top if declare procedure function returns trigger database schema grant revoke", true),
            Types = Words("int integer bigint smallint varchar nvarchar char text date datetime timestamp boolean bit decimal numeric float real uuid json", true),
            Literals = Words("null true false", true),
            LineComment = "--", BlockComment = ("/*", "*/")
        };
        var shell = new Lang
        {
            Name = "Shell",
            Keywords = Words("if then else elif fi for while until do done case esac in function return exit export local readonly echo cd source alias set unset shift sudo"),
            Literals = Words("true false"),
            LineComment = "#", DollarVariables = true
        };
        var powershell = new Lang
        {
            Name = "PowerShell",
            CaseInsensitive = true,
            Keywords = Words("begin break catch class continue data do dynamicparam else elseif end enum exit filter finally for foreach from function if in param process return switch throw trap try until using while", true),
            Literals = Words("$true $false $null", true),
            LineComment = "#", BlockComment = ("<#", "#>"), DollarVariables = true, CmdletNames = true
        };

        var json = new Lang { Name = "JSON", Mode = Mode.Json, Literals = Words("true false null") };
        var yaml = new Lang { Name = "YAML", Mode = Mode.Yaml, Literals = Words("true false null yes no on off ~", true), LineComment = "#" };
        var markup = new Lang { Name = "XML", Mode = Mode.Markup };
        var html = new Lang { Name = "HTML", Mode = Mode.Markup };
        var css = new Lang { Name = "CSS", Mode = Mode.Css };

        var map = new Dictionary<string, Lang>(StringComparer.OrdinalIgnoreCase);
        void Add(Lang lang, params string[] aliases)
        {
            foreach (var alias in aliases)
                map[alias] = lang;
        }

        Add(csharp, "csharp", "cs", "c#", "cake");
        Add(kotlin, "kotlin", "kt", "kts", "gradle");
        Add(java, "java", "groovy", "scala");
        Add(javascript, "javascript", "js", "jsx", "mjs", "cjs", "node");
        Add(typescript, "typescript", "ts", "tsx");
        Add(python, "python", "py", "python3");
        Add(go, "go", "golang");
        Add(rust, "rust", "rs");
        Add(swift, "swift");
        Add(dart, "dart", "flutter");
        Add(cpp, "c", "cpp", "c++", "h", "hpp", "cc", "objc", "objective-c");
        Add(sql, "sql", "mysql", "postgresql", "postgres", "psql", "tsql", "sqlite", "plsql");
        Add(shell, "bash", "sh", "shell", "zsh", "console", "terminal", "dockerfile", "docker");
        Add(powershell, "powershell", "ps1", "pwsh", "ps");
        Add(json, "json", "jsonc", "json5");
        Add(yaml, "yaml", "yml");
        Add(markup, "xml", "xaml", "axaml", "svg", "csproj", "plist");
        Add(html, "html", "htm", "vue", "svelte");
        Add(css, "css", "scss", "less", "sass");
        return map;
    }

    /// <summary>Display name for a fence language, e.g. "kt" → "Kotlin".</summary>
    public static string DisplayName(string? language)
    {
        var key = Normalize(language);
        if (key.Length == 0)
            return "text";
        return Languages.TryGetValue(key, out var lang) ? lang.Name : key;
    }

    public static bool IsSupported(string? language) => Languages.ContainsKey(Normalize(language));

    private static string Normalize(string? language) =>
        (language ?? "").Trim().Split(' ', '{', ',')[0].Trim();

    public static List<CodeToken> Tokenize(string code, string? language)
    {
        var tokens = new List<CodeToken>();
        if (!Languages.TryGetValue(Normalize(language), out var lang) || code.Length == 0)
            return tokens;

        switch (lang.Mode)
        {
            case Mode.Json:
                ScanJson(code, lang, tokens);
                break;
            case Mode.Yaml:
                ScanYaml(code, lang, tokens);
                break;
            case Mode.Markup:
                ScanMarkup(code, tokens);
                break;
            case Mode.Css:
                ScanCss(code, tokens);
                break;
            default:
                ScanGeneric(code, 0, code.Length, lang, tokens);
                break;
        }

        return tokens;
    }

    // ------------------------------------------------------------------ generic C-like scanner

    [GeneratedRegex(@"\G(0[xX][0-9a-fA-F_]+|0[bB][01_]+|(\d[\d_]*)?\.?\d[\d_]*([eE][+-]?\d+)?)[fFdDlLuUmMnN]*")]
    private static partial Regex NumberRegex();

    private static void ScanGeneric(string code, int start, int end, Lang lang, List<CodeToken> tokens)
    {
        var i = start;
        string? previousWord = null;

        while (i < end)
        {
            var c = code[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            // block comment
            if (lang.BlockComment is { } bc && At(code, i, bc.Open))
            {
                var close = code.IndexOf(bc.Close, i + bc.Open.Length, StringComparison.Ordinal);
                var stop = close < 0 || close >= end ? end : close + bc.Close.Length;
                Add(tokens, i, stop, TokenKind.Comment);
                i = stop;
                continue;
            }

            // line comment
            if ((lang.LineComment is { } lc && At(code, i, lc)))
            {
                var stop = LineEnd(code, i, end);
                Add(tokens, i, stop, TokenKind.Comment);
                i = stop;
                continue;
            }

            // triple-quoted / raw strings
            if (lang.TripleQuotes && (At(code, i, "\"\"\"") || (lang.Name == "Python" && At(code, i, "'''"))))
            {
                var quote = code.Substring(i, 3);
                var close = code.IndexOf(quote, i + 3, StringComparison.Ordinal);
                var stop = close < 0 || close >= end ? end : close + 3;
                Add(tokens, i, stop, TokenKind.String);
                i = stop;
                continue;
            }

            // C# verbatim / interpolated strings: @"..", $"..", $@"..", @$".."
            if (lang.VerbatimStrings && (c == '@' || c == '$'))
            {
                var j = i;
                var verbatim = false;
                while (j < end && j < i + 2 && (code[j] == '@' || code[j] == '$'))
                {
                    verbatim |= code[j] == '@';
                    j++;
                }

                if (j < end && code[j] == '"')
                {
                    var stop = verbatim ? ScanVerbatim(code, j, end) : ScanString(code, j, end, '"', multiline: false);
                    Add(tokens, i, stop, TokenKind.String);
                    i = stop;
                    continue;
                }
            }

            // strings
            if (c == '"' || (c == '\'' && lang.SingleQuoteStrings) || (c == '`' && lang.Backtick))
            {
                var stop = ScanString(code, i, end, c, multiline: c == '`');
                Add(tokens, i, stop, TokenKind.String);
                i = stop;
                continue;
            }

            // Rust char literals / lifetimes: keep 'a' as a string, 'a as plain
            if (c == '\'' && !lang.SingleQuoteStrings && i + 2 < end && code[i + 2] == '\'')
            {
                Add(tokens, i, i + 3, TokenKind.String);
                i += 3;
                continue;
            }

            // numbers
            if (char.IsDigit(c) || (c == '.' && i + 1 < end && char.IsDigit(code[i + 1])))
            {
                var m = NumberRegex().Match(code, i);
                var stop = m.Success && m.Length > 0 ? Math.Min(i + m.Length, end) : i + 1;
                Add(tokens, i, stop, TokenKind.Number);
                i = stop;
                continue;
            }

            // annotations / decorators: @Composable, @Override, @dataclass
            if (c == '@' && lang.Annotations && i + 1 < end && IsIdentStart(code[i + 1]))
            {
                var stop = i + 1;
                while (stop < end && (IsIdentPart(code[stop]) || code[stop] == '.'))
                    stop++;
                Add(tokens, i, stop, TokenKind.Annotation);
                i = stop;
                continue;
            }

            // $variables (shell, PowerShell)
            if (c == '$' && lang.DollarVariables && i + 1 < end && (IsIdentStart(code[i + 1]) || code[i + 1] == '{' || char.IsDigit(code[i + 1])))
            {
                var stop = i + 1;
                if (code[stop] == '{')
                {
                    var close = code.IndexOf('}', stop);
                    stop = close < 0 || close >= end ? end : close + 1;
                }
                else
                {
                    while (stop < end && (IsIdentPart(code[stop]) || code[stop] == ':'))
                        stop++;
                }

                var word = code[i..stop];
                Add(tokens, i, stop, lang.Literals.Contains(word) ? TokenKind.Constant : TokenKind.Property);
                i = stop;
                continue;
            }

            // identifiers
            if (IsIdentStart(c))
            {
                var stop = i + 1;
                while (stop < end && IsIdentPart(code[stop]))
                    stop++;

                // PowerShell cmdlets: Get-ChildItem
                if (lang.CmdletNames && stop < end && code[stop] == '-' && stop + 1 < end && char.IsUpper(code[stop + 1]))
                {
                    stop++;
                    while (stop < end && IsIdentPart(code[stop]))
                        stop++;
                    Add(tokens, i, stop, TokenKind.Function);
                    previousWord = null;
                    i = stop;
                    continue;
                }

                var word = code[i..stop];
                var kind = Classify(word, code, stop, end, lang, previousWord);
                if (kind is { } k)
                    Add(tokens, i, stop, k);
                previousWord = word;
                i = stop;
                continue;
            }

            i++;
        }
    }

    private static readonly HashSet<string> DeclarationKeywords = new(StringComparer.Ordinal)
    {
        "class", "interface", "struct", "enum", "record", "object", "trait", "impl", "type", "typealias", "extends", "implements", "new", "is", "as"
    };

    private static TokenKind? Classify(string word, string code, int after, int end, Lang lang, string? previousWord)
    {
        if (lang.Keywords.Contains(word))
            return TokenKind.Keyword;
        if (lang.Literals.Contains(word))
            return TokenKind.Constant;
        if (lang.Types.Contains(word))
            return TokenKind.Type;

        // a call or declaration: name(  or  name<T>(
        var next = after;
        while (next < end && code[next] is ' ' or '\t')
            next++;
        if (next < end && code[next] == '(')
            return char.IsUpper(word[0]) && previousWord is "new" ? TokenKind.Type : TokenKind.Function;

        if (previousWord is not null && DeclarationKeywords.Contains(previousWord))
            return TokenKind.Type;

        if (char.IsUpper(word[0]))
        {
            // ALL_CAPS → constant, PascalCase → type
            var hasLower = false;
            foreach (var ch in word)
                if (char.IsLower(ch)) { hasLower = true; break; }
            return hasLower ? TokenKind.Type : word.Length > 1 ? TokenKind.Constant : TokenKind.Type;
        }

        return null;
    }

    // ------------------------------------------------------------------ JSON

    private static void ScanJson(string code, Lang lang, List<CodeToken> tokens)
    {
        var i = 0;
        while (i < code.Length)
        {
            var c = code[i];
            if (c == '"')
            {
                var stop = ScanString(code, i, code.Length, '"', multiline: false);
                var next = stop;
                while (next < code.Length && char.IsWhiteSpace(code[next]))
                    next++;
                Add(tokens, i, stop, next < code.Length && code[next] == ':' ? TokenKind.Property : TokenKind.String);
                i = stop;
            }
            else if (c == '-' || char.IsDigit(c))
            {
                var stop = i + 1;
                while (stop < code.Length && (char.IsDigit(code[stop]) || code[stop] is '.' or 'e' or 'E' or '+' or '-'))
                    stop++;
                Add(tokens, i, stop, TokenKind.Number);
                i = stop;
            }
            else if (char.IsLetter(c))
            {
                var stop = i + 1;
                while (stop < code.Length && char.IsLetter(code[stop]))
                    stop++;
                if (lang.Literals.Contains(code[i..stop]))
                    Add(tokens, i, stop, TokenKind.Constant);
                i = stop;
            }
            else if (c == '/' && At(code, i, "//"))
            {
                var stop = LineEnd(code, i, code.Length);
                Add(tokens, i, stop, TokenKind.Comment); // jsonc
                i = stop;
            }
            else
            {
                i++;
            }
        }
    }

    // ------------------------------------------------------------------ YAML

    [GeneratedRegex(@"^([ \t]*(?:-[ \t]+)?)([^\s#:'""\-][^:#]*?|""[^""]*""|'[^']*')([ \t]*:)(?=[ \t]|$)")]
    private static partial Regex YamlKeyRegex();

    private static void ScanYaml(string code, Lang lang, List<CodeToken> tokens)
    {
        var valueLang = new Lang { Name = "YAML", Literals = lang.Literals, LineComment = "#", SingleQuoteStrings = true };
        var lineStart = 0;
        while (lineStart <= code.Length)
        {
            var lineEnd = code.IndexOf('\n', lineStart);
            if (lineEnd < 0)
                lineEnd = code.Length;
            var line = code[lineStart..lineEnd];

            var valueStart = lineStart;
            var m = YamlKeyRegex().Match(line);
            if (m.Success)
            {
                Add(tokens, lineStart + m.Groups[2].Index, lineStart + m.Groups[2].Index + m.Groups[2].Length, TokenKind.Property);
                valueStart = lineStart + m.Length;
            }
            else if (line.TrimStart().StartsWith("---") || line.TrimStart().StartsWith("..."))
            {
                Add(tokens, lineStart, lineEnd, TokenKind.Comment);
                valueStart = lineEnd;
            }

            // Plain scalar values: classify whole words (true/false/numbers), keep comments and quoted strings.
            ScanYamlValue(code, valueStart, lineEnd, valueLang, tokens);
            lineStart = lineEnd + 1;
        }
    }

    private static void ScanYamlValue(string code, int start, int end, Lang lang, List<CodeToken> tokens)
    {
        var i = start;
        while (i < end)
        {
            var c = code[i];
            if (c == '#' && (i == start || char.IsWhiteSpace(code[i - 1])))
            {
                Add(tokens, i, end, TokenKind.Comment);
                return;
            }

            if (c is '"' or '\'')
            {
                var stop = ScanString(code, i, end, c, multiline: false);
                Add(tokens, i, stop, TokenKind.String);
                i = stop;
                continue;
            }

            if (c is '&' or '*' && i + 1 < end && IsIdentStart(code[i + 1]))
            {
                var stop = i + 1;
                while (stop < end && IsIdentPart(code[stop]))
                    stop++;
                Add(tokens, i, stop, TokenKind.Annotation); // anchors / aliases
                i = stop;
                continue;
            }

            if (!char.IsWhiteSpace(c) && c is not '-' and not '[' and not ']' and not '{' and not '}' and not ',' and not '|' and not '>')
            {
                var stop = i;
                while (stop < end && !char.IsWhiteSpace(code[stop]) && code[stop] is not ',' and not ']' and not '}')
                    stop++;
                var word = code[i..stop];
                if (lang.Literals.Contains(word))
                    Add(tokens, i, stop, TokenKind.Constant);
                else if (double.TryParse(word, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
                    Add(tokens, i, stop, TokenKind.Number);
                i = stop;
                continue;
            }

            i++;
        }
    }

    // ------------------------------------------------------------------ XML / HTML

    private static void ScanMarkup(string code, List<CodeToken> tokens)
    {
        var i = 0;
        while (i < code.Length)
        {
            var lt = code.IndexOf('<', i);
            if (lt < 0)
                break;

            if (At(code, lt, "<!--"))
            {
                var close = code.IndexOf("-->", lt + 4, StringComparison.Ordinal);
                var stop = close < 0 ? code.Length : close + 3;
                Add(tokens, lt, stop, TokenKind.Comment);
                i = stop;
                continue;
            }

            var p = lt + 1;
            if (p < code.Length && code[p] is '/' or '?' or '!')
                p++;
            var nameStart = p;
            while (p < code.Length && (IsIdentPart(code[p]) || code[p] is ':' or '.' or '-'))
                p++;
            if (p == nameStart)
            {
                i = lt + 1;
                continue;
            }

            Add(tokens, nameStart, p, TokenKind.Tag);

            // attributes until the closing '>'
            while (p < code.Length && code[p] != '>')
            {
                var c = code[p];
                if (c is '"' or '\'')
                {
                    var stop = ScanString(code, p, code.Length, c, multiline: true);
                    Add(tokens, p, stop, TokenKind.String);
                    p = stop;
                }
                else if (IsIdentStart(c))
                {
                    var stop = p + 1;
                    while (stop < code.Length && (IsIdentPart(code[stop]) || code[stop] is ':' or '.' or '-'))
                        stop++;
                    Add(tokens, p, stop, TokenKind.Attribute);
                    p = stop;
                }
                else
                {
                    p++;
                }
            }

            i = p + 1;
        }
    }

    // ------------------------------------------------------------------ CSS

    private static void ScanCss(string code, List<CodeToken> tokens)
    {
        var depth = 0;
        var i = 0;
        while (i < code.Length)
        {
            var c = code[i];
            if (At(code, i, "/*"))
            {
                var close = code.IndexOf("*/", i + 2, StringComparison.Ordinal);
                var stop = close < 0 ? code.Length : close + 2;
                Add(tokens, i, stop, TokenKind.Comment);
                i = stop;
            }
            else if (At(code, i, "//"))
            {
                var stop = LineEnd(code, i, code.Length); // scss / less
                Add(tokens, i, stop, TokenKind.Comment);
                i = stop;
            }
            else if (c is '"' or '\'')
            {
                var stop = ScanString(code, i, code.Length, c, multiline: false);
                Add(tokens, i, stop, TokenKind.String);
                i = stop;
            }
            else if (c == '{') { depth++; i++; }
            else if (c == '}') { depth = Math.Max(0, depth - 1); i++; }
            else if (c == '@')
            {
                var stop = i + 1;
                while (stop < code.Length && (IsIdentPart(code[stop]) || code[stop] == '-'))
                    stop++;
                Add(tokens, i, stop, TokenKind.Keyword);
                i = stop;
            }
            else if (c == '#' && depth > 0)
            {
                var stop = i + 1;
                while (stop < code.Length && Uri.IsHexDigit(code[stop]))
                    stop++;
                Add(tokens, i, stop, TokenKind.Number);
                i = stop;
            }
            else if (char.IsDigit(c) || (c == '.' && depth > 0 && i + 1 < code.Length && char.IsDigit(code[i + 1])))
            {
                var stop = i + 1;
                while (stop < code.Length && (char.IsLetterOrDigit(code[stop]) || code[stop] is '.' or '%'))
                    stop++;
                Add(tokens, i, stop, TokenKind.Number);
                i = stop;
            }
            else if (IsIdentStart(c) || c is '-' or '.' or '#' or ':' or '&')
            {
                var stop = i + 1;
                while (stop < code.Length && (IsIdentPart(code[stop]) || code[stop] is '-'))
                    stop++;
                if (depth == 0)
                {
                    Add(tokens, i, stop, TokenKind.Tag); // selector
                }
                else
                {
                    var next = stop;
                    while (next < code.Length && code[next] is ' ' or '\t')
                        next++;
                    if (next < code.Length && code[next] == ':' && c != ':')
                        Add(tokens, i, stop, TokenKind.Property);
                    else if (next < code.Length && code[next] == '(')
                        Add(tokens, i, stop, TokenKind.Function);
                    else if (c == '$' || c == '-' && At(code, i, "--"))
                        Add(tokens, i, stop, TokenKind.Constant);
                }

                i = stop;
            }
            else
            {
                i++;
            }
        }
    }

    // ------------------------------------------------------------------ helpers

    private static int ScanString(string code, int start, int end, char quote, bool multiline)
    {
        var i = start + 1;
        while (i < end)
        {
            var c = code[i];
            if (c == '\\')
            {
                i += 2;
                continue;
            }

            if (c == quote)
                return i + 1;
            if (c == '\n' && !multiline)
                return i;
            i++;
        }

        return end;
    }

    private static int ScanVerbatim(string code, int quoteIndex, int end)
    {
        var i = quoteIndex + 1;
        while (i < end)
        {
            if (code[i] == '"')
            {
                if (i + 1 < end && code[i + 1] == '"')
                {
                    i += 2;
                    continue;
                }

                return i + 1;
            }

            i++;
        }

        return end;
    }

    private static int LineEnd(string code, int from, int end)
    {
        var nl = code.IndexOf('\n', from);
        return nl < 0 || nl > end ? end : nl;
    }

    private static bool At(string code, int index, string value) =>
        string.CompareOrdinal(code, index, value, 0, value.Length) == 0;

    private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';
    private static bool IsIdentPart(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static void Add(List<CodeToken> tokens, int start, int end, TokenKind kind)
    {
        if (end > start)
            tokens.Add(new CodeToken(start, end - start, kind));
    }
}
