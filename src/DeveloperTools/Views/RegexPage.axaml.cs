using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using DeveloperTools.ViewModels;

namespace DeveloperTools.Views;

public partial class RegexPage : UserControl
{
    private const double StackBreakpoint = 900;
    private readonly MatchHighlighter _highlighter = new();
    private RegexPageViewModel? _vm;
    private bool? _stacked;

    public RegexPage()
    {
        InitializeComponent();
        TestEditor.Options.EnableHyperlinks = false;
        TestEditor.Options.EnableEmailHyperlinks = false;
        TestEditor.Options.HighlightCurrentLine = false;
        foreach (var margin in TestEditor.TextArea.LeftMargins)
            if (margin is AvaloniaEdit.Editing.LineNumberMargin)
                margin.Margin = new Thickness(4, 0, 12, 0);
        TestEditor.TextArea.TextView.BackgroundRenderers.Add(_highlighter);

        ActualThemeVariantChanged += (_, _) => ApplyTheme();
        ApplyTheme();
        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width < StackBreakpoint);
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => PatternBox.Focus(), DispatcherPriority.Background);
        PatternBox.GotFocus += (_, _) => PatternFrame.BorderBrush = Resource("Fg.Muted");
        PatternBox.LostFocus += (_, _) => PatternFrame.BorderBrush = Resource("Line.Strong");
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.HighlightsChanged -= OnHighlightsChanged;
            _vm.SelectRequested -= OnSelectRequested;
        }
        _vm = DataContext as RegexPageViewModel;
        if (_vm is not null)
        {
            _vm.HighlightsChanged += OnHighlightsChanged;
            _vm.SelectRequested += OnSelectRequested;
            _vm.RunNow();
        }
        base.OnDataContextChanged(e);
    }

    private void ApplyLayout(bool stacked)
    {
        if (_stacked == stacked)
            return;
        _stacked = stacked;
        TwoPane.Apply(Panes, stacked, gap: 0);
        if (stacked)
        {
            Panes.RowDefinitions = new RowDefinitions("*,*");
            SidePanel.BorderThickness = new Thickness(0, 1, 0, 0);
        }
        else
        {
            Panes.ColumnDefinitions = new ColumnDefinitions("*,380");
            SidePanel.BorderThickness = new Thickness(1, 0, 0, 0);
        }
    }

    private void ApplyTheme()
    {
        _highlighter.First = Resource("Regex.Match1");
        _highlighter.Second = Resource("Regex.Match2");
        TestEditor.TextArea.SelectionBrush = Resource("Selection");
        TestEditor.TextArea.SelectionBorder = new Pen(Resource("Fg.Primary"), 1.5);
        TestEditor.TextArea.SelectionForeground = null;
        TestEditor.TextArea.CaretBrush = Resource("Fg.Primary");
        TestEditor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    private IBrush Resource(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;

    private void OnHighlightsChanged(object? sender, EventArgs e)
    {
        _highlighter.Ranges = _vm?.Highlights ?? Array.Empty<(int, int)>();
        TestEditor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    private void OnSelectRequested(object? sender, (int Start, int Length) match)
    {
        var doc = TestEditor.Document;
        if (match.Start > doc.TextLength)
            return;
        TestEditor.Select(match.Start, Math.Min(match.Length, doc.TextLength - match.Start));
        var location = doc.GetLocation(match.Start);
        TestEditor.ScrollTo(location.Line, location.Column);
        TestEditor.TextArea.Focus();
    }
}

/// <summary>Alternating backgrounds for regex matches; only visible ranges are drawn.</summary>
public sealed class MatchHighlighter : IBackgroundRenderer
{
    public IReadOnlyList<(int Start, int Length)> Ranges { get; set; } = Array.Empty<(int, int)>();
    public IBrush First { get; set; } = Brushes.Yellow;
    public IBrush Second { get; set; } = Brushes.LightBlue;

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext dc)
    {
        if (!textView.VisualLinesValid || Ranges.Count == 0 || textView.Document is null)
            return;
        var lines = textView.VisualLines;
        if (lines.Count == 0)
            return;
        var viewStart = lines[0].FirstDocumentLine.Offset;
        var viewEnd = lines[^1].LastDocumentLine.EndOffset;
        var docLength = textView.Document.TextLength;

        // Binary search the first range that may be visible.
        int lo = 0, hi = Ranges.Count - 1, first = Ranges.Count;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (Ranges[mid].Start + Ranges[mid].Length >= viewStart) { first = mid; hi = mid - 1; }
            else lo = mid + 1;
        }

        for (var i = first; i < Ranges.Count && Ranges[i].Start <= viewEnd; i++)
        {
            var (start, length) = Ranges[i];
            if (start > docLength)
                break;
            var brush = i % 2 == 0 ? First : Second;
            if (length == 0)
            {
                // Empty match: thin marker.
                var segment0 = new TextSegment { StartOffset = start, Length = 0 };
                foreach (var r in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment0))
                    dc.FillRectangle(brush, new Rect(r.X - 1, r.Y, 2, r.Height));
                continue;
            }

            var segment = new TextSegment { StartOffset = start, Length = Math.Min(length, docLength - start) };
            foreach (var r in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                dc.FillRectangle(brush, r, 2);
        }
    }
}
