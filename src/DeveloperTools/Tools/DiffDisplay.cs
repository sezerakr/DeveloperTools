using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DeveloperTools.Tools;

public enum LineTone
{
    None,
    Removed,
    Added,
    Filler,   // empty row that keeps both sides aligned
    Fold      // "… N unchanged lines"
}

/// <summary>One line of a rendered diff document.</summary>
/// <param name="Number">Line number on this side (1-based), 0 for none.</param>
/// <param name="Number2">Inline view: number in the new file (Number is the old one).</param>
/// <param name="Source">Index of the DiffResult row it came from (-1 for folds).</param>
public readonly record struct DisplayLine(
    LineTone Tone,
    int Number,
    int Number2,
    IReadOnlyList<Span>? Spans,
    char Sign,
    int Source,
    bool Modified = false);

public sealed class DiffDocument
{
    public required string Text { get; init; }
    public required IReadOnlyList<DisplayLine> Lines { get; init; }

    /// <summary>Display line (0-based) where each change block starts.</summary>
    public required IReadOnlyList<int> ChangeLines { get; init; }
}

/// <summary>Turns a DiffResult into aligned side-by-side documents or one unified document.</summary>
public static class DiffDisplay
{
    public const int Context = 3;

    /// <summary>Row ranges to show: everything, or only changes with context (the rest folded).</summary>
    private static List<(int start, int end, bool fold)> Segments(DiffResult r, bool onlyChanges)
    {
        var rows = r.Rows;
        if (!onlyChanges || r.Identical)
            return [(0, rows.Count, false)];

        var visible = new bool[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Kind == DiffKind.Equal)
                continue;
            for (var k = System.Math.Max(0, i - Context); k <= System.Math.Min(rows.Count - 1, i + Context); k++)
                visible[k] = true;
        }

        var segments = new List<(int, int, bool)>();
        var s = 0;
        while (s < rows.Count)
        {
            var e = s;
            var v = visible[s];
            while (e < rows.Count && visible[e] == v) e++;
            // Don't fold tiny gaps: showing 1-2 lines is clearer than a fold marker.
            segments.Add((s, e, !v && e - s > 2));
            s = e;
        }
        return segments;
    }

    private static string FoldText(int count) => Localization.Loc.F("⋯  {0:N0} unchanged lines", count);

    public static (DiffDocument left, DiffDocument right) SideBySide(DiffResult r, bool onlyChanges)
    {
        var lt = new StringBuilder();
        var rt = new StringBuilder();
        var ll = new List<DisplayLine>();
        var rl = new List<DisplayLine>();
        var changes = new List<int>();
        var starts = new HashSet<int>(r.ChangeStarts);

        void Add(StringBuilder sb, List<DisplayLine> lines, string text, DisplayLine line)
        {
            if (lines.Count > 0) sb.Append('\n');
            sb.Append(text);
            lines.Add(line);
        }

        foreach (var (start, end, fold) in Segments(r, onlyChanges))
        {
            if (fold)
            {
                var text = FoldText(end - start);
                Add(lt, ll, text, new DisplayLine(LineTone.Fold, 0, 0, null, ' ', -1));
                Add(rt, rl, text, new DisplayLine(LineTone.Fold, 0, 0, null, ' ', -1));
                continue;
            }

            for (var i = start; i < end; i++)
            {
                var row = r.Rows[i];
                if (starts.Contains(i))
                    changes.Add(ll.Count);

                var modified = row.Kind == DiffKind.Modified;
                if (row.Left >= 0)
                    Add(lt, ll, r.LeftLines[row.Left], new DisplayLine(
                        row.Kind == DiffKind.Equal ? LineTone.None : LineTone.Removed,
                        row.Left + 1, 0, row.LeftSpans, row.Kind == DiffKind.Equal ? ' ' : '-', i, modified));
                else
                    Add(lt, ll, "", new DisplayLine(LineTone.Filler, 0, 0, null, ' ', i));

                if (row.Right >= 0)
                    Add(rt, rl, r.RightLines[row.Right], new DisplayLine(
                        row.Kind == DiffKind.Equal ? LineTone.None : LineTone.Added,
                        row.Right + 1, 0, row.RightSpans, row.Kind == DiffKind.Equal ? ' ' : '+', i, modified));
                else
                    Add(rt, rl, "", new DisplayLine(LineTone.Filler, 0, 0, null, ' ', i));
            }
        }

        return (new DiffDocument { Text = lt.ToString(), Lines = ll, ChangeLines = changes },
                new DiffDocument { Text = rt.ToString(), Lines = rl, ChangeLines = changes });
    }

    /// <summary>Unified view: each change block lists removed lines, then added lines.</summary>
    public static DiffDocument Inline(DiffResult r, bool onlyChanges)
    {
        var sb = new StringBuilder();
        var lines = new List<DisplayLine>();
        var changes = new List<int>();

        void Add(string text, DisplayLine line)
        {
            if (lines.Count > 0) sb.Append('\n');
            sb.Append(text);
            lines.Add(line);
        }

        foreach (var (start, end, fold) in Segments(r, onlyChanges))
        {
            if (fold)
            {
                Add(FoldText(end - start), new DisplayLine(LineTone.Fold, 0, 0, null, ' ', -1));
                continue;
            }

            var i = start;
            while (i < end)
            {
                var row = r.Rows[i];
                if (row.Kind == DiffKind.Equal)
                {
                    Add(r.LeftLines[row.Left], new DisplayLine(LineTone.None, row.Left + 1, row.Right + 1, null, ' ', i));
                    i++;
                    continue;
                }

                changes.Add(lines.Count);
                var blockStart = i;
                while (i < end && r.Rows[i].Kind != DiffKind.Equal) i++;
                var block = Enumerable.Range(blockStart, i - blockStart).ToList();
                foreach (var k in block.Where(k => r.Rows[k].Left >= 0))
                {
                    var x = r.Rows[k];
                    Add(r.LeftLines[x.Left], new DisplayLine(LineTone.Removed, x.Left + 1, 0, x.LeftSpans, '-', k, x.Kind == DiffKind.Modified));
                }
                foreach (var k in block.Where(k => r.Rows[k].Right >= 0))
                {
                    var x = r.Rows[k];
                    Add(r.RightLines[x.Right], new DisplayLine(LineTone.Added, 0, x.Right + 1, x.RightSpans, '+', k, x.Kind == DiffKind.Modified));
                }
            }
        }

        return new DiffDocument { Text = sb.ToString(), Lines = lines, ChangeLines = changes };
    }
}
