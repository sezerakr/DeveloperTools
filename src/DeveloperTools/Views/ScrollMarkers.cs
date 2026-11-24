using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DeveloperTools.Views;

/// <summary>
/// Search-result ticks drawn over a vertical scrollbar (like Visual Studio's map mode).
/// Positions are fractions of the scrollable content (0 = top, 1 = bottom).
/// Not hit-testable, so the scrollbar underneath keeps working.
/// </summary>
public sealed class ScrollMarkers : Control
{
    private IReadOnlyList<double> _positions = Array.Empty<double>();
    private double _current = -1;

    public static readonly StyledProperty<IBrush?> MarkBrushProperty =
        AvaloniaProperty.Register<ScrollMarkers, IBrush?>(nameof(MarkBrush));

    public static readonly StyledProperty<IBrush?> CurrentBrushProperty =
        AvaloniaProperty.Register<ScrollMarkers, IBrush?>(nameof(CurrentBrush));

    static ScrollMarkers()
    {
        AffectsRender<ScrollMarkers>(MarkBrushProperty, CurrentBrushProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<ScrollMarkers>(false);
    }

    public IBrush? MarkBrush
    {
        get => GetValue(MarkBrushProperty);
        set => SetValue(MarkBrushProperty, value);
    }

    public IBrush? CurrentBrush
    {
        get => GetValue(CurrentBrushProperty);
        set => SetValue(CurrentBrushProperty, value);
    }

    public void SetMarkers(IReadOnlyList<double> positions, double current)
    {
        _positions = positions;
        _current = current;
        InvalidateVisual();
    }

    public void Clear() => SetMarkers(Array.Empty<double>(), -1);

    public override void Render(DrawingContext context)
    {
        var height = Bounds.Height;
        var width = Bounds.Width;
        if (height <= 0 || _positions.Count == 0)
            return;

        const double tick = 3;
        var brush = MarkBrush ?? Brushes.Gold;
        double lastY = -10;
        foreach (var position in _positions)
        {
            var y = Math.Round(Math.Clamp(position, 0, 1) * (height - tick));
            if (y - lastY < 1)
                continue; // many hits on the same pixel row: draw once
            context.FillRectangle(brush, new Rect(2, y, Math.Max(1, width - 4), tick));
            lastY = y;
        }

        if (_current >= 0)
        {
            var y = Math.Round(Math.Clamp(_current, 0, 1) * (height - 4));
            context.FillRectangle(CurrentBrush ?? Brushes.Orange, new Rect(0, y, width, 4));
        }
    }
}
