using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using DeveloperTools.Markdown;

namespace DeveloperTools.Views;

/// <summary>
/// Document-style text selection for the markdown preview.
/// A drag can start anywhere (margins, padding, empty space between blocks) and can span
/// paragraphs, lists, tables and code blocks. Ctrl+C / Ctrl+A / right-click menu work on the whole selection.
/// </summary>
public sealed class PreviewSelection
{
    private readonly ScrollViewer _scroll;
    private readonly Panel _host;
    private readonly Action<string> _onLink;

    private List<SelectableTextBlock> _blocks = new();
    private (int Block, int Pos)? _anchor;
    private (int Block, int Pos)? _focus;
    private readonly HashSet<SelectableTextBlock> _selected = new();
    private bool _dragging;

    public PreviewSelection(ScrollViewer scroll, Panel host, Action<string> onLink)
    {
        _scroll = scroll;
        _host = host;
        _onLink = onLink;

        _scroll.Focusable = true;
        _scroll.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
        _scroll.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        _scroll.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        _scroll.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

        var copy = new MenuItem { Header = "Copy", InputGesture = new KeyGesture(Key.C, KeyModifiers.Control) };
        copy.Click += (_, _) => CopyToClipboard();
        var selectAll = new MenuItem { Header = "Select all", InputGesture = new KeyGesture(Key.A, KeyModifiers.Control) };
        selectAll.Click += (_, _) => SelectAll();
        var menu = new ContextMenu { ItemsSource = new[] { copy, selectAll } };
        menu.Opening += (_, _) => copy.IsEnabled = HasSelection;
        _scroll.ContextMenu = menu;
    }

    public bool HasSelection => _anchor is { } a && _focus is { } f && a != f;

    /// <summary>Called after the preview content changed: positions are no longer valid.</summary>
    public void Reset()
    {
        _anchor = _focus = null;
        _dragging = false;
        _selected.Clear();
    }

    // ------------------------------------------------------------------ pointer

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(_scroll);
        if (!point.Properties.IsLeftButtonPressed || IsInteractive(e.Source))
            return; // right-click menu, scrollbars, buttons and task checkboxes work as usual

        _blocks = _host.GetVisualDescendants().OfType<SelectableTextBlock>().Where(t => t.IsEffectivelyVisible).ToList();
        if (HitTest(e.GetPosition(_host)) is not { } hit)
            return;

        e.Handled = true; // take over from the individual text blocks
        _scroll.Focus();

        if (e.ClickCount == 2)
        {
            SelectWord(hit);
        }
        else if (e.ClickCount >= 3)
        {
            _anchor = (hit.Block, 0);
            _focus = (hit.Block, TextLength(_blocks[hit.Block]));
        }
        else
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift) || _anchor is null)
                _anchor = hit;
            _focus = hit;
            _dragging = true;
            e.Pointer.Capture(_scroll);
        }

        Apply();
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging)
            return;

        // Auto-scroll when dragging past the top or bottom edge.
        var inView = e.GetPosition(_scroll);
        const double edge = 28;
        if (inView.Y < edge)
            _scroll.Offset = new Vector(_scroll.Offset.X, Math.Max(0, _scroll.Offset.Y - (edge - inView.Y)));
        else if (inView.Y > _scroll.Bounds.Height - edge)
            _scroll.Offset = new Vector(_scroll.Offset.X, _scroll.Offset.Y + (inView.Y - (_scroll.Bounds.Height - edge)));

        if (HitTest(e.GetPosition(_host)) is { } hit)
        {
            _focus = hit;
            Apply();
        }
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging)
            return;
        _dragging = false;
        e.Pointer.Capture(null);

        // A click without a drag: open the link under the pointer, if any.
        if (!HasSelection && _anchor is { } a && a.Block < _blocks.Count)
        {
            var text = _blocks[a.Block];
            if (MarkdownRenderer.LinkAt(text, e.GetPosition(text)) is { } url)
                _onLink(url);
        }
    }

    private bool IsInteractive(object? source)
    {
        for (var visual = source as Visual; visual is not null && !ReferenceEquals(visual, _scroll); visual = visual.GetVisualParent())
        {
            if (visual is Button or ScrollBar or Thumb or RepeatButton)
                return true;
            if (visual is StyledElement element && element.Classes.Contains("task-box"))
                return true;
        }

        return false;
    }

    // ------------------------------------------------------------------ hit testing

    /// <summary>Maps a point (in host coordinates) to the nearest text position, even outside any text.</summary>
    private (int Block, int Pos)? HitTest(Point point)
    {
        if (_blocks.Count == 0)
            return null;

        var best = -1;
        var bestDistance = double.MaxValue;
        for (var i = 0; i < _blocks.Count; i++)
        {
            var rect = BoundsInHost(_blocks[i]);
            if (rect is not { } r)
                continue;

            if (point.Y >= r.Top && point.Y <= r.Bottom)
            {
                // On this block's rows: prefer the block horizontally closest (table cells side by side).
                var dx = point.X < r.Left ? r.Left - point.X : point.X > r.Right ? point.X - r.Right : 0;
                if (dx < bestDistance)
                {
                    bestDistance = dx;
                    best = i;
                }
            }
            else if (point.Y < r.Top && best < 0)
            {
                return (i, 0); // in the gap above this block: start of the block
            }
        }

        if (best < 0)
        {
            var last = _blocks.Count - 1;
            return (last, TextLength(_blocks[last])); // below everything: end of the document
        }

        var text = _blocks[best];
        var bounds = BoundsInHost(text)!.Value;
        var local = point - bounds.TopLeft - new Point(text.Padding.Left, text.Padding.Top);
        var layout = text.TextLayout;
        var x = Math.Clamp(local.X, 0, Math.Max(0, layout.WidthIncludingTrailingWhitespace));
        var y = Math.Clamp(local.Y, 0, Math.Max(0, layout.Height - 1));
        var hit = layout.HitTestPoint(new Point(x, y));
        return (best, Math.Clamp(hit.TextPosition, 0, TextLength(text)));
    }

    private Rect? BoundsInHost(Visual visual)
    {
        var topLeft = visual.TranslatePoint(new Point(0, 0), _host);
        return topLeft is { } p ? new Rect(p, visual.Bounds.Size) : null;
    }

    private static string TextOf(SelectableTextBlock text) => text.Inlines?.Text ?? text.Text ?? "";

    private static int TextLength(SelectableTextBlock text) => TextOf(text).Length;

    // ------------------------------------------------------------------ selection

    private void SelectWord((int Block, int Pos) hit)
    {
        var text = TextOf(_blocks[hit.Block]);
        static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';
        int start = hit.Pos, end = hit.Pos;
        while (start > 0 && IsWord(text[start - 1])) start--;
        while (end < text.Length && IsWord(text[end])) end++;
        _anchor = (hit.Block, start);
        _focus = (hit.Block, end);
    }

    private void SelectAll()
    {
        _blocks = _host.GetVisualDescendants().OfType<SelectableTextBlock>().Where(t => t.IsEffectivelyVisible).ToList();
        if (_blocks.Count == 0)
            return;
        _anchor = (0, 0);
        _focus = (_blocks.Count - 1, TextLength(_blocks[^1]));
        Apply();
    }

    private ((int Block, int Pos) Start, (int Block, int Pos) End)? Ordered()
    {
        if (_anchor is not { } a || _focus is not { } f)
            return null;
        return (a.Block, a.Pos).CompareTo((f.Block, f.Pos)) <= 0 ? (a, f) : (f, a);
    }

    /// <summary>Paints the selection on every text block in the range and clears it elsewhere.</summary>
    private void Apply()
    {
        var range = Ordered();
        var inRange = new HashSet<SelectableTextBlock>();

        if (range is { } r)
        {
            for (var i = r.Start.Block; i <= r.End.Block && i < _blocks.Count; i++)
            {
                var text = _blocks[i];
                var start = i == r.Start.Block ? r.Start.Pos : 0;
                var end = i == r.End.Block ? r.End.Pos : TextLength(text);
                text.SelectionStart = start;
                text.SelectionEnd = end;
                inRange.Add(text);
            }
        }

        foreach (var text in _selected)
            if (!inRange.Contains(text))
                text.ClearSelection();

        _selected.Clear();
        _selected.UnionWith(inRange);
    }

    public string SelectedText()
    {
        if (Ordered() is not { } r)
            return "";
        var builder = new StringBuilder();
        for (var i = r.Start.Block; i <= r.End.Block && i < _blocks.Count; i++)
        {
            var part = _blocks[i].SelectedText;
            if (builder.Length > 0)
                builder.Append(Environment.NewLine);
            builder.Append(part);
        }

        return builder.ToString();
    }

    private async void CopyToClipboard()
    {
        var text = SelectedText();
        if (text.Length == 0)
            return;
        if (TopLevel.GetTopLevel(_scroll)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

    // ------------------------------------------------------------------ keyboard

    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (!args.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (args.Key == Key.Escape && HasSelection)
            {
                _anchor = _focus = null;
                Apply();
                args.Handled = true;
            }

            return;
        }

        if (args.Key == Key.C && HasSelection)
        {
            CopyToClipboard();
            args.Handled = true;
        }
        else if (args.Key == Key.A)
        {
            SelectAll();
            args.Handled = true;
        }
    }
}
