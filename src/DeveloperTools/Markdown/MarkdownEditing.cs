using System;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit;
using AvaloniaEdit.Document;

namespace DeveloperTools.Markdown;

/// <summary>Markdown formatting commands and typing helpers for an AvaloniaEdit <see cref="TextEditor"/>.</summary>
public sealed partial class MarkdownEditing
{
    // indent, marker ("-", "*", "+", "1." or "1)"), optional task box
    [GeneratedRegex(@"^(?<indent>[ \t]*)(?<marker>[-*+]|(?<num>\d+)[.)])(?<space>[ \t]+)(?<task>\[[ xX]\][ \t]+)?")]
    private static partial Regex ListItemRegex();

    [GeneratedRegex(@"^(#{1,6})[ \t]+")]
    private static partial Regex HeadingRegex();

    private readonly TextEditor _editor;

    public MarkdownEditing(TextEditor editor)
    {
        _editor = editor;
        editor.TextArea.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private TextDocument Document => _editor.Document;

    // ------------------------------------------------------------------ inline formatting

    public void Bold() => Wrap("**", "**", "bold text");
    public void Italic() => Wrap("*", "*", "italic text");
    public void Strikethrough() => Wrap("~~", "~~", "text");
    public void InlineCode() => Wrap("`", "`", "code");

    public void Link()
    {
        var selected = _editor.SelectedText;
        var start = _editor.SelectionStart;
        if (selected.Length > 0 && Uri.TryCreate(selected, UriKind.Absolute, out _))
        {
            // Selected text is a URL: wrap it and select the label.
            Document.Replace(start, selected.Length, $"[link]({selected})");
            _editor.Select(start + 1, 4);
        }
        else
        {
            var label = selected.Length > 0 ? selected : "link text";
            Document.Replace(start, selected.Length, $"[{label}](url)");
            _editor.Select(start + label.Length + 3, 3);
        }

        Focus();
    }

    /// <summary>Wraps the selection in markers, or removes them when already wrapped.</summary>
    private void Wrap(string prefix, string suffix, string placeholder)
    {
        var start = _editor.SelectionStart;
        var length = _editor.SelectionLength;
        var selected = _editor.SelectedText;

        // Already wrapped (markers just outside the selection)? Unwrap.
        if (start >= prefix.Length && start + length + suffix.Length <= Document.TextLength
            && Document.GetText(start - prefix.Length, prefix.Length) == prefix
            && Document.GetText(start + length, suffix.Length) == suffix)
        {
            using (Document.RunUpdate())
            {
                Document.Remove(start + length, suffix.Length);
                Document.Remove(start - prefix.Length, prefix.Length);
            }

            _editor.Select(start - prefix.Length, length);
            Focus();
            return;
        }

        var text = selected.Length > 0 ? selected : placeholder;
        Document.Replace(start, length, prefix + text + suffix);
        _editor.Select(start + prefix.Length, text.Length);
        Focus();
    }

    // ------------------------------------------------------------------ line formatting

    public void Heading(int level)
    {
        var marks = new string('#', level) + " ";
        EditSelectedLines((line, _) =>
        {
            var match = HeadingRegex().Match(line);
            var body = match.Success ? line[match.Length..] : line;
            // Same level again toggles the heading off.
            return match.Success && match.Groups[1].Length == level ? body : marks + body;
        });
    }

    public void BulletList() => ToggleListPrefix(_ => "- ");
    public void NumberedList() => ToggleListPrefix(i => $"{i + 1}. ");
    public void TaskList() => ToggleListPrefix(_ => "- [ ] ");

    public void Quote()
    {
        var lines = SelectedLineTexts();
        var all = lines.All(l => l.StartsWith('>') || l.Trim().Length == 0);
        EditSelectedLines((line, _) => all
            ? Regex.Replace(line, @"^>[ ]?", "")
            : "> " + line);
    }

    private void ToggleListPrefix(Func<int, string> prefixFor)
    {
        var lines = SelectedLineTexts();
        var sample = prefixFor(0);
        var isTask = sample.Contains('[');
        var isNumbered = char.IsDigit(sample[0]);

        bool HasThisKind(string line)
        {
            var m = ListItemRegex().Match(line);
            if (!m.Success) return false;
            if (isTask) return m.Groups["task"].Success;
            if (isNumbered) return m.Groups["num"].Success && !m.Groups["task"].Success;
            return !m.Groups["num"].Success && !m.Groups["task"].Success;
        }

        var removing = lines.Where(l => l.Trim().Length > 0).All(HasThisKind);
        var index = 0;
        EditSelectedLines((line, _) =>
        {
            if (line.Trim().Length == 0)
                return line;
            var m = ListItemRegex().Match(line);
            var indent = m.Success ? m.Groups["indent"].Value : Regex.Match(line, @"^[ \t]*").Value;
            var body = m.Success ? line[m.Length..] : line.TrimStart();
            return removing ? indent + body : indent + prefixFor(index++) + body;
        });
    }

    // ------------------------------------------------------------------ blocks

    public void CodeBlock()
    {
        var selected = _editor.SelectedText;
        var start = _editor.SelectionStart;
        var before = NeedsBlankLineBefore(start) ? "\n" : "";
        var body = selected.Length > 0 ? selected.TrimEnd('\n', '\r') : "";
        var text = $"{before}```\n{body}\n```\n";
        Document.Replace(start, selected.Length, text);
        // Put the caret right after the opening fence so a language can be typed.
        _editor.CaretOffset = start + before.Length + 3;
        _editor.SelectionLength = 0;
        Focus();
    }

    public void Table() => InsertBlock("| Column | Column | Column |\n| ------ | ------ | ------ |\n| Cell   | Cell   | Cell   |\n");

    public void HorizontalRule() => InsertBlock("---\n");

    private void InsertBlock(string block)
    {
        var line = Document.GetLineByOffset(_editor.CaretOffset);
        var lineText = Document.GetText(line);
        string text;
        int offset;
        if (lineText.Trim().Length == 0)
        {
            offset = line.Offset;
            text = (NeedsBlankLineBefore(offset) ? "\n" : "") + block;
            Document.Replace(line.Offset, line.Length, text.TrimEnd('\n'));
        }
        else
        {
            offset = line.EndOffset;
            text = "\n\n" + block.TrimEnd('\n');
            Document.Insert(offset, text);
        }

        _editor.CaretOffset = Math.Min(offset + text.TrimEnd('\n').Length, Document.TextLength);
        Focus();
    }

    // ------------------------------------------------------------------ typing helpers

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (ctrl && !shift)
        {
            Action? action = e.Key switch
            {
                Key.B => Bold,
                Key.I => Italic,
                Key.K => Link,
                Key.E => InlineCode,
                Key.D1 => () => Heading(1),
                Key.D2 => () => Heading(2),
                Key.D3 => () => Heading(3),
                _ => null
            };
            if (action is not null)
            {
                action();
                e.Handled = true;
            }

            return;
        }

        if (ctrl && shift)
        {
            Action? action = e.Key switch
            {
                Key.X => Strikethrough,
                Key.D7 => NumberedList,
                Key.D8 => BulletList,
                Key.D9 => TaskList,
                _ => null
            };
            if (action is not null)
            {
                action();
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && _editor.SelectionLength == 0)
            e.Handled = ContinueList();
        else if (e.Key == Key.Tab && _editor.SelectionLength == 0)
            e.Handled = IndentListItem(outdent: shift);
    }

    /// <summary>Enter inside a list item starts the next item; on an empty item it ends the list.</summary>
    private bool ContinueList()
    {
        var caret = _editor.CaretOffset;
        var line = Document.GetLineByOffset(caret);
        var text = Document.GetText(line);
        var m = ListItemRegex().Match(text);
        if (!m.Success || caret < line.Offset + m.Length)
            return false;

        if (text[m.Length..].Trim().Length == 0)
        {
            // Empty item: remove the marker instead of adding another one.
            Document.Replace(line.Offset, line.Length, m.Groups["indent"].Value);
            return true;
        }

        var marker = m.Groups["num"].Success
            ? $"{int.Parse(m.Groups["num"].Value) + 1}{m.Groups["marker"].Value[^1]}"
            : m.Groups["marker"].Value;
        var next = "\n" + m.Groups["indent"].Value + marker + m.Groups["space"].Value
                   + (m.Groups["task"].Success ? "[ ] " : "");
        Document.Insert(caret, next);
        _editor.CaretOffset = caret + next.Length;
        return true;
    }

    /// <summary>Tab / Shift+Tab on a list item changes its nesting level.</summary>
    private bool IndentListItem(bool outdent)
    {
        var line = Document.GetLineByOffset(_editor.CaretOffset);
        var text = Document.GetText(line);
        if (!ListItemRegex().IsMatch(text))
            return false;

        if (outdent)
        {
            var remove = text.StartsWith("  ") ? 2 : text.StartsWith('\t') ? 1 : 0;
            if (remove > 0)
                Document.Remove(line.Offset, remove);
        }
        else
        {
            Document.Insert(line.Offset, "  ");
        }

        return true;
    }

    // ------------------------------------------------------------------ helpers

    private string[] SelectedLineTexts()
    {
        var (first, last) = SelectedLines();
        return Enumerable.Range(first.LineNumber, last.LineNumber - first.LineNumber + 1)
            .Select(n => Document.GetText(Document.GetLineByNumber(n)))
            .ToArray();
    }

    private (DocumentLine First, DocumentLine Last) SelectedLines()
    {
        var start = _editor.SelectionStart;
        var end = start + _editor.SelectionLength;
        var first = Document.GetLineByOffset(start);
        var last = Document.GetLineByOffset(end);
        // A selection ending at column 0 doesn't include that line.
        if (last.LineNumber > first.LineNumber && end == last.Offset)
            last = last.PreviousLine;
        return (first, last);
    }

    private void EditSelectedLines(Func<string, int, string> transform)
    {
        var (first, last) = SelectedLines();
        var start = first.Offset;
        var end = last.EndOffset;
        var lines = Document.GetText(start, end - start).Split('\n');
        var result = string.Join("\n", lines.Select((l, i) =>
        {
            var cr = l.EndsWith('\r');
            var body = cr ? l[..^1] : l;
            return transform(body, i) + (cr ? "\r" : "");
        }));
        Document.Replace(start, end - start, result);
        _editor.Select(start, result.Length);
        Focus();
    }

    private bool NeedsBlankLineBefore(int offset)
    {
        if (offset == 0)
            return false;
        var line = Document.GetLineByOffset(offset);
        if (offset > line.Offset)
            return true; // mid-line
        var previous = line.PreviousLine;
        return previous is not null && Document.GetText(previous).Trim().Length > 0;
    }

    private void Focus() => _editor.TextArea.Focus();
}
