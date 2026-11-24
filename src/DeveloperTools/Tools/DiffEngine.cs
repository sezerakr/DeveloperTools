using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DeveloperTools.Tools;

public enum DiffKind
{
    Equal,
    Deleted,   // only on the left
    Inserted,  // only on the right
    Modified   // changed line, paired left/right
}

public sealed record DiffOptions(bool IgnoreWhitespace = false, bool IgnoreCase = false);

/// <summary>A character range inside a line that differs from its counterpart.</summary>
public readonly record struct Span(int Start, int Length);

/// <summary>One aligned row: a left line, a right line, or both. Line indexes are 0-based.</summary>
public sealed class DiffRow
{
    public DiffKind Kind { get; init; }
    public int Left { get; init; } = -1;
    public int Right { get; init; } = -1;
    public IReadOnlyList<Span>? LeftSpans { get; set; }
    public IReadOnlyList<Span>? RightSpans { get; set; }
}

public sealed class DiffResult
{
    public required string[] LeftLines { get; init; }
    public required string[] RightLines { get; init; }
    public required List<DiffRow> Rows { get; init; }

    /// <summary>Row index where each change block (hunk) starts.</summary>
    public required List<int> ChangeStarts { get; init; }

    public int Additions { get; init; }
    public int Deletions { get; init; }
    public int Modifications { get; init; }
    public bool Identical => ChangeStarts.Count == 0;
    public bool TimedOut { get; init; }
    public string? LineEndingNote { get; init; }
}

/// <summary>
/// Line diff (Myers, linear space) with unmatched-line discarding, readable block placement,
/// similarity-based pairing of changed lines and word-level highlights within them.
/// </summary>
public static partial class DiffEngine
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(4);

    public static string[] SplitLines(string text)
    {
        if (text.Length == 0)
            return [];
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        // A trailing newline does not make an extra empty line.
        return lines.Length > 1 && lines[^1].Length == 0 ? lines[..^1] : lines;
    }

    public static DiffResult Compare(string left, string right, DiffOptions options)
    {
        var a = SplitLines(left);
        var b = SplitLines(right);

        // Same text up to normalization: compare lines through a key.
        var ids = new Dictionary<string, int>();
        int[] Ids(string[] lines) => lines.Select(l => Id(ids, Key(l, options))).ToArray();
        var ia = Ids(a);
        var ib = Ids(b);

        var deadline = Stopwatch.StartNew();
        var (delA, insB, timedOut) = LineDiff(ia, ib, deadline);
        var blankId = ids.TryGetValue("", out var blank) ? blank : -1;
        Slide(ia, delA, blankId);
        Slide(ib, insB, blankId);

        var rows = BuildRows(a, b, delA, insB);
        var starts = new List<int>();
        int adds = 0, dels = 0, mods = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Kind != DiffKind.Equal && (i == 0 || rows[i - 1].Kind == DiffKind.Equal))
                starts.Add(i);
            switch (row.Kind)
            {
                case DiffKind.Inserted: adds++; break;
                case DiffKind.Deleted: dels++; break;
                case DiffKind.Modified: mods++; break;
            }
        }

        return new DiffResult
        {
            LeftLines = a,
            RightLines = b,
            Rows = rows,
            ChangeStarts = starts,
            Additions = adds,
            Deletions = dels,
            Modifications = mods,
            TimedOut = timedOut,
            LineEndingNote = LineEndings(left, right)
        };
    }

    private static int Id(Dictionary<string, int> ids, string key)
    {
        if (!ids.TryGetValue(key, out var id))
        {
            id = ids.Count;
            ids[key] = id;
        }
        return id;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private static string Key(string line, DiffOptions o)
    {
        if (o.IgnoreWhitespace)
            line = Whitespace().Replace(line, "");
        if (o.IgnoreCase)
            line = line.ToLowerInvariant();
        return line;
    }

    private static string? LineEndings(string left, string right)
    {
        static string Style(string t) =>
            t.Contains("\r\n") ? "CRLF" : t.Contains('\n') ? "LF" : "";
        var l = Style(left);
        var r = Style(right);
        return l.Length > 0 && r.Length > 0 && l != r ? $"Line endings differ ({l} vs {r}); they are ignored." : null;
    }

    // ------------------------------------------------------------------ core diff

    /// <summary>Marks which elements of a were deleted and of b were inserted.</summary>
    public static (bool[] delA, bool[] insB, bool timedOut) LineDiff(int[] a, int[] b, Stopwatch? clock = null)
    {
        var delA = new bool[a.Length];
        var insB = new bool[b.Length];
        clock ??= Stopwatch.StartNew();

        // Elements with no counterpart anywhere can never match: mark them up front and diff the rest.
        // This keeps very different inputs fast without changing the result.
        var inB = new HashSet<int>(b);
        var inA = new HashSet<int>(a);
        var mapA = new List<int>(a.Length);
        var mapB = new List<int>(b.Length);
        for (var i = 0; i < a.Length; i++)
            if (inB.Contains(a[i])) mapA.Add(i); else delA[i] = true;
        for (var j = 0; j < b.Length; j++)
            if (inA.Contains(b[j])) mapB.Add(j); else insB[j] = true;

        var fa = mapA.Select(i => a[i]).ToArray();
        var fb = mapB.Select(j => b[j]).ToArray();
        var marksA = new bool[fa.Length];
        var marksB = new bool[fb.Length];
        var myers = new Myers(fa, fb, marksA, marksB, clock);
        myers.Run();

        for (var i = 0; i < fa.Length; i++)
            if (marksA[i]) delA[mapA[i]] = true;
        for (var j = 0; j < fb.Length; j++)
            if (marksB[j]) insB[mapB[j]] = true;
        return (delA, insB, myers.TimedOut);
    }

    private sealed class Myers(int[] a, int[] b, bool[] delA, bool[] insB, Stopwatch clock)
    {
        public bool TimedOut { get; private set; }

        public void Run()
        {
            var stack = new Stack<(int xo, int xl, int yo, int yl)>();
            stack.Push((0, a.Length, 0, b.Length));
            while (stack.Count > 0)
            {
                var (xo, xl, yo, yl) = stack.Pop();
                while (xo < xl && yo < yl && a[xo] == b[yo]) { xo++; yo++; }
                while (xl > xo && yl > yo && a[xl - 1] == b[yl - 1]) { xl--; yl--; }

                if (xo == xl)
                {
                    for (var y = yo; y < yl; y++) insB[y] = true;
                    continue;
                }
                if (yo == yl)
                {
                    for (var x = xo; x < xl; x++) delA[x] = true;
                    continue;
                }

                var (sx, sy) = clock.Elapsed > Budget ? (-1, -1) : Bisect(xo, xl, yo, yl);
                if (sx < 0 || (sx == xo && sy == yo) || (sx == xl && sy == yl))
                {
                    if (clock.Elapsed > Budget) TimedOut = true;
                    for (var x = xo; x < xl; x++) delA[x] = true;
                    for (var y = yo; y < yl; y++) insB[y] = true;
                    continue;
                }

                stack.Push((sx, xl, sy, yl));
                stack.Push((xo, sx, yo, sy));
            }
        }

        /// <summary>Finds a point on an optimal edit path (the "middle snake"), searching from both ends.</summary>
        private (int x, int y) Bisect(int xo, int xl, int yo, int yl)
        {
            int n = xl - xo, m = yl - yo;
            var maxD = (n + m + 1) / 2;
            var offset = maxD + 1;
            var length = 2 * maxD + 4;
            var v1 = new int[length];
            var v2 = new int[length];
            Array.Fill(v1, -1);
            Array.Fill(v2, -1);
            v1[offset + 1] = 0;
            v2[offset + 1] = 0;
            var delta = n - m;
            var front = (delta & 1) != 0;
            int k1Start = 0, k1End = 0, k2Start = 0, k2End = 0;

            for (var d = 0; d < maxD; d++)
            {
                if ((d & 63) == 0 && clock.Elapsed > Budget)
                    return (-1, -1);

                for (var k1 = -d + k1Start; k1 <= d - k1End; k1 += 2)
                {
                    var i1 = offset + k1;
                    var x1 = k1 == -d || (k1 != d && v1[i1 - 1] < v1[i1 + 1]) ? v1[i1 + 1] : v1[i1 - 1] + 1;
                    var y1 = x1 - k1;
                    while (x1 < n && y1 < m && a[xo + x1] == b[yo + y1]) { x1++; y1++; }
                    v1[i1] = x1;
                    if (x1 > n) k1End += 2;
                    else if (y1 > m) k1Start += 2;
                    else if (front)
                    {
                        var i2 = offset + delta - k1;
                        if (i2 >= 0 && i2 < length && v2[i2] != -1 && x1 >= n - v2[i2])
                            return (xo + x1, yo + y1);
                    }
                }

                for (var k2 = -d + k2Start; k2 <= d - k2End; k2 += 2)
                {
                    var i2 = offset + k2;
                    var x2 = k2 == -d || (k2 != d && v2[i2 - 1] < v2[i2 + 1]) ? v2[i2 + 1] : v2[i2 - 1] + 1;
                    var y2 = x2 - k2;
                    while (x2 < n && y2 < m && a[xo + n - x2 - 1] == b[yo + m - y2 - 1]) { x2++; y2++; }
                    v2[i2] = x2;
                    if (x2 > n) k2End += 2;
                    else if (y2 > m) k2Start += 2;
                    else if (!front)
                    {
                        var i1 = offset + delta - k2;
                        if (i1 >= 0 && i1 < length && v1[i1] != -1)
                        {
                            var x1 = v1[i1];
                            var y1 = x1 - (i1 - offset);
                            if (x1 >= n - x2)
                                return (xo + x1, yo + y1);
                        }
                    }
                }
            }

            return (-1, -1);
        }
    }

    /// <summary>
    /// Moves each run of changed lines to the most readable equivalent position:
    /// as high as possible, then down until the run ends on a blank line when that is an option
    /// (so an inserted function block carries its own trailing blank line, like git shows it).
    /// </summary>
    private static void Slide(int[] lines, bool[] changed, int blankId)
    {
        var n = changed.Length;
        var i = 0;
        while (i < n)
        {
            if (!changed[i]) { i++; continue; }
            var start = i;
            while (i < n && changed[i]) i++;
            var end = i;
            var len = end - start;

            while (start > 0 && !changed[start - 1] && lines[start - 1] == lines[end - 1])
            {
                start--;
                end--;
                changed[start] = true;
                changed[end] = false;
            }

            var best = start;
            if (blankId >= 0 && lines[end - 1] != blankId)
            {
                int s = start, e = end;
                while (e < n && !changed[e] && lines[s] == lines[e])
                {
                    s++;
                    e++;
                    if (lines[e - 1] == blankId)
                    {
                        best = s;
                        break;
                    }
                }
            }

            if (best != start)
            {
                for (var k = start; k < start + len; k++) changed[k] = false;
                for (var k = best; k < best + len; k++) changed[k] = true;
            }

            i = best + len;
        }
    }

    // ------------------------------------------------------------------ rows

    private static List<DiffRow> BuildRows(string[] a, string[] b, bool[] delA, bool[] insB)
    {
        var rows = new List<DiffRow>(Math.Max(a.Length, b.Length));
        int i = 0, j = 0;
        while (i < a.Length || j < b.Length)
        {
            if (i < a.Length && j < b.Length && !delA[i] && !insB[j])
            {
                rows.Add(new DiffRow { Kind = DiffKind.Equal, Left = i++, Right = j++ });
                continue;
            }

            // A change block: deleted lines then inserted lines up to the next common line.
            var dels = new List<int>();
            var ins = new List<int>();
            while (i < a.Length && delA[i]) dels.Add(i++);
            while (j < b.Length && insB[j]) ins.Add(j++);
            if (dels.Count == 0 && ins.Count == 0)
            {
                // Only one side left (defensive); emit the rest.
                if (i < a.Length) dels.Add(i++);
                else if (j < b.Length) ins.Add(j++);
            }

            AlignBlock(a, b, dels, ins, rows);
        }

        return rows;
    }

    /// <summary>Pairs similar deleted/inserted lines (in order) as modifications; the rest stay pure deletes/inserts.</summary>
    private static void AlignBlock(string[] a, string[] b, List<int> dels, List<int> ins, List<DiffRow> rows)
    {
        if (dels.Count == 0 || ins.Count == 0)
        {
            foreach (var d in dels) rows.Add(new DiffRow { Kind = DiffKind.Deleted, Left = d });
            foreach (var n in ins) rows.Add(new DiffRow { Kind = DiffKind.Inserted, Right = n });
            return;
        }

        // Huge blocks: pair line k with line k only where they look alike (keeps it fast).
        var pairs = (long)dels.Count * ins.Count <= 160_000
            ? BestPairs(a, b, dels, ins)
            : Enumerable.Range(0, Math.Min(dels.Count, ins.Count))
                .Where(k => Similarity(Bigrams(a[dels[k]]), Bigrams(b[ins[k]]), a[dels[k]], b[ins[k]]) >= 0.45)
                .Select(k => (k, k)).ToList();

        int di = 0, ni = 0;
        foreach (var (pd, pn) in pairs)
        {
            while (di < pd) rows.Add(new DiffRow { Kind = DiffKind.Deleted, Left = dels[di++] });
            while (ni < pn) rows.Add(new DiffRow { Kind = DiffKind.Inserted, Right = ins[ni++] });
            var row = new DiffRow { Kind = DiffKind.Modified, Left = dels[di++], Right = ins[ni++] };
            (row.LeftSpans, row.RightSpans) = WordDiff(a[row.Left], b[row.Right]);
            rows.Add(row);
        }

        while (di < dels.Count) rows.Add(new DiffRow { Kind = DiffKind.Deleted, Left = dels[di++] });
        while (ni < ins.Count) rows.Add(new DiffRow { Kind = DiffKind.Inserted, Right = ins[ni++] });
    }

    /// <summary>Order-preserving pairing that maximises total line similarity (small DP over the block).</summary>
    private static List<(int, int)> BestPairs(string[] a, string[] b, List<int> dels, List<int> ins)
    {
        int n = dels.Count, m = ins.Count;
        var gramsA = dels.Select(d => Bigrams(a[d])).ToArray();
        var gramsB = ins.Select(x => Bigrams(b[x])).ToArray();
        var sim = new double[n, m];
        for (var x = 0; x < n; x++)
            for (var y = 0; y < m; y++)
                sim[x, y] = Similarity(gramsA[x], gramsB[y], a[dels[x]], b[ins[y]]);

        var score = new double[n + 1, m + 1];
        for (var x = 1; x <= n; x++)
            for (var y = 1; y <= m; y++)
            {
                var best = Math.Max(score[x - 1, y], score[x, y - 1]);
                var s = sim[x - 1, y - 1];
                if (s >= 0.45)
                    best = Math.Max(best, score[x - 1, y - 1] + s);
                score[x, y] = best;
            }

        var pairs = new List<(int, int)>();
        int i = n, j = m;
        while (i > 0 && j > 0)
        {
            var s = sim[i - 1, j - 1];
            if (s >= 0.45 && Math.Abs(score[i, j] - (score[i - 1, j - 1] + s)) < 1e-9)
            {
                pairs.Add((i - 1, j - 1));
                i--;
                j--;
            }
            else if (Math.Abs(score[i, j] - score[i - 1, j]) < 1e-9) i--;
            else j--;
        }

        pairs.Reverse();
        return pairs;
    }

    private static Dictionary<int, int> Bigrams(string s)
    {
        var t = s.Trim();
        var grams = new Dictionary<int, int>();
        for (var i = 0; i + 1 < t.Length; i++)
        {
            var key = (t[i] << 16) | t[i + 1];
            grams[key] = grams.GetValueOrDefault(key) + 1;
        }
        return grams;
    }

    private static double Similarity(Dictionary<int, int> x, Dictionary<int, int> y, string sx, string sy)
    {
        var tx = sx.Trim();
        var ty = sy.Trim();
        if (tx.Length == 0 || ty.Length == 0)
            return tx.Length == ty.Length ? 1 : 0;
        if (tx.Length < 2 || ty.Length < 2)
            return tx == ty ? 1 : 0.5;
        var common = 0;
        foreach (var (k, c) in x)
            if (y.TryGetValue(k, out var d))
                common += Math.Min(c, d);
        return 2.0 * common / (tx.Length - 1 + ty.Length - 1);
    }

    // ------------------------------------------------------------------ word-level

    [GeneratedRegex(@"\w+|\s+|[^\w\s]")]
    private static partial Regex Tokens();

    /// <summary>Character ranges that differ between two versions of a line, at word granularity.</summary>
    public static (IReadOnlyList<Span> left, IReadOnlyList<Span> right) WordDiff(string left, string right)
    {
        var lt = Tokens().Matches(left).Select(m => (m.Index, m.Value)).ToArray();
        var rt = Tokens().Matches(right).Select(m => (m.Index, m.Value)).ToArray();
        var ids = new Dictionary<string, int>();
        var la = lt.Select(t => Id(ids, t.Value)).ToArray();
        var ra = rt.Select(t => Id(ids, t.Value)).ToArray();
        var (del, ins, _) = LineDiff(la, ra);

        // Whitespace-only tokens between two changed tokens join the highlight for a calmer look.
        return (Merge(lt, del, left), Merge(rt, ins, right));
    }

    private static List<Span> Merge((int Index, string Value)[] tokens, bool[] changed, string line)
    {
        var spans = new List<Span>();
        for (var i = 0; i < tokens.Length; i++)
        {
            if (!changed[i])
                continue;
            var start = tokens[i].Index;
            var end = start + tokens[i].Value.Length;
            while (i + 1 < tokens.Length)
            {
                if (changed[i + 1])
                {
                    i++;
                    end = tokens[i].Index + tokens[i].Value.Length;
                }
                else if (i + 2 < tokens.Length && changed[i + 2] && string.IsNullOrWhiteSpace(tokens[i + 1].Value))
                {
                    i += 2;
                    end = tokens[i].Index + tokens[i].Value.Length;
                }
                else break;
            }
            // Indentation changes alone are noise next to the content changes: trim whitespace off the ends.
            while (start < end && char.IsWhiteSpace(line[start])) start++;
            while (end > start && char.IsWhiteSpace(line[end - 1])) end--;
            if (end > start)
                spans.Add(new Span(start, end - start));
        }
        return spans;
    }

    // ------------------------------------------------------------------ unified patch

    /// <summary>Unified diff (git style) with the given context lines.</summary>
    public static string UnifiedPatch(DiffResult r, string leftName, string rightName, int context = 3)
    {
        var sb = new StringBuilder();
        sb.Append("--- ").Append(leftName).Append('\n');
        sb.Append("+++ ").Append(rightName).Append('\n');

        // Expand modified rows into -/+ lines as git does.
        var lines = new List<(char sign, int left, int right)>();
        for (var i = 0; i < r.Rows.Count;)
        {
            var row = r.Rows[i];
            if (row.Kind == DiffKind.Equal)
            {
                lines.Add((' ', row.Left, row.Right));
                i++;
                continue;
            }

            var block = new List<DiffRow>();
            while (i < r.Rows.Count && r.Rows[i].Kind != DiffKind.Equal) block.Add(r.Rows[i++]);
            foreach (var x in block.Where(x => x.Left >= 0)) lines.Add(('-', x.Left, -1));
            foreach (var x in block.Where(x => x.Right >= 0)) lines.Add(('+', -1, x.Right));
        }

        var k = 0;
        while (k < lines.Count)
        {
            if (lines[k].sign == ' ') { k++; continue; }
            var start = Math.Max(0, k - context);
            var end = k;
            // extend while changes are within 2*context of each other
            var last = k;
            while (end < lines.Count)
            {
                if (lines[end].sign != ' ') last = end;
                else if (end - last > 2 * context) break;
                end++;
            }
            end = Math.Min(lines.Count, last + context + 1);

            int ls = -1, rs = -1, lc = 0, rc = 0;
            for (var x = start; x < end; x++)
            {
                var (sign, l, rr) = lines[x];
                if (sign != '+') { lc++; if (ls < 0) ls = l; }
                if (sign != '-') { rc++; if (rs < 0) rs = rr; }
            }
            if (ls < 0) ls = PrevLine(lines, start, left: true);
            if (rs < 0) rs = PrevLine(lines, start, left: false);
            sb.Append($"@@ -{ls + (lc == 0 ? 0 : 1)},{lc} +{rs + (rc == 0 ? 0 : 1)},{rc} @@\n");
            for (var x = start; x < end; x++)
            {
                var (sign, l, rr) = lines[x];
                sb.Append(sign).Append(sign == '+' ? r.RightLines[rr] : r.LeftLines[l]).Append('\n');
            }
            k = end;
        }

        return sb.ToString();
    }

    private static int PrevLine(List<(char sign, int left, int right)> lines, int before, bool left)
    {
        for (var x = before - 1; x >= 0; x--)
        {
            var v = left ? lines[x].left : lines[x].right;
            if (v >= 0) return v + 1; // 1-based number of the line before the hunk
        }
        return 0;
    }
}
