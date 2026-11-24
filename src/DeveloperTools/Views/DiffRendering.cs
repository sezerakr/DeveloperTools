using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using DeveloperTools.Tools;

namespace DeveloperTools.Views;

/// <summary>Brushes shared by the diff renderers (resolved from the theme).</summary>
public sealed class DiffPalette
{
    public IBrush AddedBg = Brushes.Transparent;
    public IBrush AddedStrong = Brushes.Transparent;
    public IBrush RemovedBg = Brushes.Transparent;
    public IBrush RemovedStrong = Brushes.Transparent;
    public IBrush Filler = Brushes.Transparent;
    public IPen FillerHatch = new Pen(Brushes.Transparent);
    public IBrush FoldBg = Brushes.Transparent;
    public IBrush FoldFg = Brushes.Gray;
    public IBrush AddedFg = Brushes.Green;
    public IBrush RemovedFg = Brushes.Red;
    public IBrush ModifiedMark = Brushes.Orange;
    public IBrush NumberFg = Brushes.Gray;
    public IBrush CurrentChange = Brushes.Transparent;
}

/// <summary>Paints each display line by tone, word-level changes, hatched filler rows and the current change.</summary>
public sealed class DiffBackgroundRenderer(DiffPalette palette) : IBackgroundRenderer
{
    public IReadOnlyList<DisplayLine> Lines { get; set; } = Array.Empty<DisplayLine>();

    /// <summary>Display lines of the change being navigated (first..last), or -1.</summary>
    public (int first, int last) Current { get; set; } = (-1, -1);

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext dc)
    {
        if (!textView.VisualLinesValid || textView.Document is null)
            return;

        var width = textView.Bounds.Width; // drawing is in viewport coordinates
        foreach (var visual in textView.VisualLines)
        {
            var index = visual.FirstDocumentLine.LineNumber - 1;
            if (index < 0 || index >= Lines.Count)
                continue;
            var line = Lines[index];
            var rect = new Rect(0, visual.VisualTop - textView.VerticalOffset, width, visual.Height);

            switch (line.Tone)
            {
                case LineTone.Added:
                    dc.FillRectangle(palette.AddedBg, rect);
                    DrawSpans(textView, dc, visual.FirstDocumentLine, line, palette.AddedStrong);
                    break;
                case LineTone.Removed:
                    dc.FillRectangle(palette.RemovedBg, rect);
                    DrawSpans(textView, dc, visual.FirstDocumentLine, line, palette.RemovedStrong);
                    break;
                case LineTone.Filler:
                    dc.FillRectangle(palette.Filler, rect);
                    using (dc.PushClip(rect))
                    {
                        for (var x = rect.Left - rect.Height; x < rect.Right; x += 7)
                            dc.DrawLine(palette.FillerHatch, new Point(x, rect.Bottom), new Point(x + rect.Height, rect.Top));
                    }
                    break;
                case LineTone.Fold:
                    dc.FillRectangle(palette.FoldBg, rect);
                    break;
            }

            if (index >= Current.first && index <= Current.last && Current.first >= 0)
                dc.FillRectangle(palette.CurrentChange, new Rect(0, rect.Top, 3, rect.Height));
        }
    }

    private static void DrawSpans(TextView textView, DrawingContext dc, DocumentLine docLine, DisplayLine line, IBrush brush)
    {
        if (line.Spans is null)
            return;
        foreach (var span in line.Spans)
        {
            var start = docLine.Offset + Math.Min(span.Start, docLine.Length);
            var length = Math.Min(span.Length, docLine.EndOffset - start);
            if (length <= 0)
                continue;
            var segment = new TextSegment { StartOffset = start, Length = length };
            foreach (var r in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                dc.FillRectangle(brush, new Rect(r.X, r.Y, r.Width, r.Height), 2);
        }
    }
}

/// <summary>Muted, italic text for fold rows.</summary>
public sealed class FoldColorizer(DiffPalette palette) : DocumentColorizingTransformer
{
    public IReadOnlyList<DisplayLine> Lines { get; set; } = Array.Empty<DisplayLine>();

    protected override void ColorizeLine(DocumentLine line)
    {
        var index = line.LineNumber - 1;
        if (index >= Lines.Count || Lines[index].Tone != LineTone.Fold || line.Length == 0)
            return;
        ChangeLinePart(line.Offset, line.EndOffset, e =>
        {
            e.TextRunProperties.SetForegroundBrush(palette.FoldFg);
            var face = e.TextRunProperties.Typeface;
            e.TextRunProperties.SetTypeface(new Typeface(face.FontFamily, FontStyle.Italic, face.Weight, face.Stretch));
        });
    }
}

/// <summary>Gutter with original line numbers (one or two columns) and a +/- sign.</summary>
public sealed class DiffMargin : AbstractMargin
{
    private readonly DiffPalette _palette;
    private readonly bool _twoColumns;

    public DiffMargin(DiffPalette palette, bool twoColumns)
    {
        _palette = palette;
        _twoColumns = twoColumns;
    }

    public IReadOnlyList<DisplayLine> Lines { get; set; } = Array.Empty<DisplayLine>();
    public int Digits { get; set; } = 3;
    public double FontSize { get; set; } = 13;
    public FontFamily Font { get; set; } = FontFamily.Default;

    private double CharWidth => FontSize * 0.6;
    private double ColumnWidth => Digits * CharWidth + 10;

    protected override Size MeasureOverride(Size availableSize) =>
        new((_twoColumns ? 2 : 1) * ColumnWidth + CharWidth + 18, 0);

    protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
    {
        if (oldTextView is not null)
            oldTextView.VisualLinesChanged -= OnVisualLinesChanged;
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView is not null)
            newTextView.VisualLinesChanged += OnVisualLinesChanged;
        InvalidateVisual();
    }

    private void OnVisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();

    public void Refresh()
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    public override void Render(DrawingContext dc)
    {
        var textView = TextView;
        if (textView is null || !textView.VisualLinesValid)
            return;

        var typeface = new Typeface(Font);
        foreach (var visual in textView.VisualLines)
        {
            var index = visual.FirstDocumentLine.LineNumber - 1;
            if (index < 0 || index >= Lines.Count)
                continue;
            var line = Lines[index];
            var top = visual.VisualTop - textView.VerticalOffset;
            var rect = new Rect(0, top, Bounds.Width, visual.Height);

            var tint = line.Tone switch
            {
                LineTone.Added => _palette.AddedBg,
                LineTone.Removed => _palette.RemovedBg,
                LineTone.Fold => _palette.FoldBg,
                LineTone.Filler => _palette.Filler,
                _ => null
            };
            if (tint is not null)
                dc.FillRectangle(tint, rect);

            var y = top + (visual.Height - FontSize * 1.3) / 2;
            void Number(int n, double right)
            {
                if (n <= 0) return;
                var text = new FormattedText(n.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, typeface, FontSize * 0.92, _palette.NumberFg);
                dc.DrawText(text, new Point(right - text.Width, y));
            }

            if (_twoColumns)
            {
                Number(line.Number, ColumnWidth);
                Number(line.Number2, 2 * ColumnWidth);
            }
            else
            {
                Number(line.Number, ColumnWidth);
            }

            if (line.Sign is '+' or '-')
            {
                var brush = line.Sign == '+' ? _palette.AddedFg : _palette.RemovedFg;
                var sign = new FormattedText(line.Sign == '-' ? "−" : "+", CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, typeface, FontSize, brush);
                dc.DrawText(sign, new Point((_twoColumns ? 2 : 1) * ColumnWidth + 6, y - 1));
            }
        }
    }
}

/// <summary>
/// Overview strip for the whole diff: coloured marks for every change and a box for the visible part.
/// Click or drag to jump.
/// </summary>
public sealed class DiffOverview : Control
{
    private IReadOnlyList<DisplayLine> _lines = Array.Empty<DisplayLine>();
    private IReadOnlyList<DisplayLine>? _other;
    private double _viewTop, _viewHeight;
    private bool _dragging;

    public DiffPalette? Palette { get; set; }
    public IBrush? Track { get; set; }
    public IBrush? ViewportBrush { get; set; }

    /// <summary>Raised with a fraction 0..1 of the document to center on.</summary>
    public event EventHandler<double>? Navigate;

    public DiffOverview()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    /// <param name="other">Side-by-side: the other side's lines (marks show removals and additions together).</param>
    public void SetLines(IReadOnlyList<DisplayLine> lines, IReadOnlyList<DisplayLine>? other = null)
    {
        _lines = lines;
        _other = other;
        InvalidateVisual();
    }

    /// <summary>Visible range as fractions of the document.</summary>
    public void SetViewport(double top, double height)
    {
        _viewTop = top;
        _viewHeight = height;
        InvalidateVisual();
    }

    public override void Render(DrawingContext dc)
    {
        var h = Bounds.Height;
        var w = Bounds.Width;
        if (Track is not null)
            dc.FillRectangle(Track, new Rect(0, 0, w, h));
        var count = _lines.Count;
        if (count == 0 || Palette is null || h <= 0)
            return;

        var scale = h / count;
        var i = 0;
        while (i < count)
        {
            var brush = MarkBrush(i);
            if (brush is null)
            {
                i++;
                continue;
            }
            var start = i;
            while (i < count && ReferenceEquals(MarkBrush(i), brush)) i++;
            var top = start * scale;
            var height = Math.Max(2, (i - start) * scale);
            dc.FillRectangle(brush, new Rect(2, top, w - 4, height));
        }

        if (ViewportBrush is not null && _viewHeight > 0)
            dc.FillRectangle(ViewportBrush, new Rect(0, _viewTop * h, w, Math.Max(6, _viewHeight * h)), 2);
    }

    private IBrush? MarkBrush(int i)
    {
        var line = _lines[i];
        var other = _other is not null && i < _other.Count ? _other[i] : default;
        if (line.Modified || other.Modified)
            return Palette!.ModifiedMark;
        if (line.Tone == LineTone.Added || other.Tone == LineTone.Added)
            return Palette!.AddedFg;
        if (line.Tone == LineTone.Removed || other.Tone == LineTone.Removed)
            return Palette!.RemovedFg;
        return null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _dragging = true;
        e.Pointer.Capture(this);
        Jump(e.GetPosition(this).Y);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging)
            Jump(e.GetPosition(this).Y);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
    }

    private void Jump(double y)
    {
        if (Bounds.Height > 0)
            Navigate?.Invoke(this, Math.Clamp(y / Bounds.Height, 0, 1));
    }
}
