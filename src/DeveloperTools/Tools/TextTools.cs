using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DeveloperTools.Tools;

public enum CaseStyle
{
    Camel,
    Pascal,
    Snake,
    Kebab,
    Constant,
    Dot,
    Title,
    Sentence,
    Upper,
    Lower
}

public sealed record TextStats(int Characters, int CharactersNoSpaces, int Words, int Lines, int Sentences,
    int Paragraphs, int Bytes, TimeSpan ReadingTime);

/// <summary>Case conversion, line operations and text statistics.</summary>
public static partial class TextTools
{
    // ------------------------------------------------------------------ case

    [GeneratedRegex(@"\p{Lu}+(?=\p{Lu}\p{Ll})|\p{Lu}?\p{Ll}+|\p{Lu}+|\p{N}+")]
    private static partial Regex WordParts();

    /// <summary>Splits "parseHTTPResponse_v2 code" into [parse, HTTP, Response, v, 2, code].</summary>
    public static List<string> Words(string text) => WordParts().Matches(text).Select(m => m.Value).ToList();

    public static string ConvertCase(string text, CaseStyle style)
    {
        var culture = CultureInfo.InvariantCulture;
        switch (style)
        {
            case CaseStyle.Upper:
                return text.ToUpperInvariant();
            case CaseStyle.Lower:
                return text.ToLowerInvariant();
            case CaseStyle.Title:
                return culture.TextInfo.ToTitleCase(text.ToLowerInvariant());
            case CaseStyle.Sentence:
                return SentenceCase(text);
        }

        // Identifier styles work per line so a list of names converts in one go.
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var words = Words(line).Select(w => w.ToLowerInvariant()).ToList();
            if (words.Count == 0)
                continue;
            string Cap(string w) => char.ToUpperInvariant(w[0]) + w[1..];
            lines[i] = style switch
            {
                CaseStyle.Camel => words[0] + string.Concat(words.Skip(1).Select(Cap)),
                CaseStyle.Pascal => string.Concat(words.Select(Cap)),
                CaseStyle.Snake => string.Join("_", words),
                CaseStyle.Kebab => string.Join("-", words),
                CaseStyle.Constant => string.Join("_", words).ToUpperInvariant(),
                CaseStyle.Dot => string.Join(".", words),
                _ => line
            } + (lines[i].EndsWith('\r') ? "\r" : "");
        }

        return string.Join("\n", lines);
    }

    private static string SentenceCase(string text)
    {
        var sb = new StringBuilder(text.Length);
        var start = true;
        foreach (var c in text.ToLowerInvariant())
        {
            if (start && char.IsLetter(c))
            {
                sb.Append(char.ToUpperInvariant(c));
                start = false;
            }
            else
            {
                sb.Append(c);
            }

            if (c is '.' or '!' or '?' or '\n')
                start = true;
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ lines

    private static (List<string> lines, string newline) Lines(string text)
    {
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count > 1 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1); // keep a trailing newline out of sorting
        return (lines, newline);
    }

    private static string Join(IEnumerable<string> lines, string newline) => string.Join(newline, lines);

    public static string SortLines(string text, bool descending, bool ignoreCase = true)
    {
        var (lines, nl) = Lines(text);
        var comparer = new NaturalComparer(ignoreCase);
        lines.Sort(comparer);
        if (descending) lines.Reverse();
        return Join(lines, nl);
    }

    public static string ReverseLines(string text)
    {
        var (lines, nl) = Lines(text);
        lines.Reverse();
        return Join(lines, nl);
    }

    public static string UniqueLines(string text, bool ignoreCase = false)
    {
        var (lines, nl) = Lines(text);
        var seen = new HashSet<string>(ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        return Join(lines.Where(seen.Add), nl);
    }

    public static string RemoveEmptyLines(string text)
    {
        var (lines, nl) = Lines(text);
        return Join(lines.Where(l => !string.IsNullOrWhiteSpace(l)), nl);
    }

    public static string TrimLines(string text)
    {
        var (lines, nl) = Lines(text);
        return Join(lines.Select(l => l.Trim()), nl);
    }

    public static string ShuffleLines(string text)
    {
        var (lines, nl) = Lines(text);
        var array = lines.ToArray();
        RandomNumberGenerator.Shuffle<string>(array);
        return Join(array, nl);
    }

    public static string NumberLines(string text)
    {
        var (lines, nl) = Lines(text);
        var width = lines.Count.ToString(CultureInfo.InvariantCulture).Length;
        return Join(lines.Select((l, i) => (i + 1).ToString(CultureInfo.InvariantCulture).PadLeft(width) + ". " + l), nl);
    }

    public static string JoinLines(string text, string separator)
    {
        var (lines, _) = Lines(text);
        return string.Join(separator, lines.Where(l => l.Length > 0));
    }

    public static string SplitToLines(string text, string separator)
    {
        if (separator.Length == 0)
            return text;
        return string.Join("\n", text.Split(separator).Select(s => s.Trim()));
    }

    /// <summary>Compares "file2" before "file10".</summary>
    public sealed class NaturalComparer(bool ignoreCase) : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (x is null || y is null)
                return string.CompareOrdinal(x, y);
            int i = 0, j = 0;
            while (i < x.Length && j < y.Length)
            {
                if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
                {
                    var si = i;
                    var sj = j;
                    while (i < x.Length && char.IsAsciiDigit(x[i])) i++;
                    while (j < y.Length && char.IsAsciiDigit(y[j])) j++;
                    var a = x[si..i].TrimStart('0');
                    var b = y[sj..j].TrimStart('0');
                    if (a.Length != b.Length) return a.Length.CompareTo(b.Length);
                    var c = string.CompareOrdinal(a, b);
                    if (c != 0) return c;
                }
                else
                {
                    var c = ignoreCase
                        ? string.Compare(x[i].ToString(), y[j].ToString(), CultureInfo.CurrentCulture, CompareOptions.IgnoreCase)
                        : x[i].CompareTo(y[j]);
                    if (c != 0) return c;
                    i++;
                    j++;
                }
            }
            return (x.Length - i).CompareTo(y.Length - j);
        }
    }

    // ------------------------------------------------------------------ stats

    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}'’_-]*")]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"[.!?]+(\s|$)")]
    private static partial Regex SentenceEnd();

    public static TextStats Stats(string text)
    {
        var words = WordRegex().Count(text);
        var lines = text.Length == 0 ? 0 : text.Count(c => c == '\n') + 1;
        var sentences = text.Trim().Length == 0 ? 0 : Math.Max(1, SentenceEnd().Count(text));
        var paragraphs = Regex.Split(text.Trim(), @"\n\s*\n").Count(p => p.Trim().Length > 0);
        return new TextStats(
            text.Length,
            text.Count(c => !char.IsWhiteSpace(c)),
            words,
            lines,
            sentences,
            paragraphs,
            Encoding.UTF8.GetByteCount(text),
            TimeSpan.FromMinutes(words / 220.0));
    }
}
