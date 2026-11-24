using Avalonia.Controls;

namespace DeveloperTools.Views;

/// <summary>Lays out a grid's first two children side by side, or stacked when space is narrow.</summary>
public static class TwoPane
{
    /// <param name="firstWidth">Width of the first pane when side by side; null splits the space evenly.</param>
    public static void Apply(Grid grid, bool stacked, double? firstWidth = null, double gap = 16)
    {
        if (grid.Children.Count < 2)
            return;
        var first = grid.Children[0];
        var second = grid.Children[1];

        if (stacked)
        {
            grid.ColumnDefinitions = new ColumnDefinitions("*");
            grid.RowDefinitions = new RowDefinitions("Auto,Auto");
            grid.ColumnSpacing = 0;
            grid.RowSpacing = gap;
            Grid.SetColumn(first, 0);
            Grid.SetColumn(second, 0);
            Grid.SetRow(first, 0);
            Grid.SetRow(second, 1);
        }
        else
        {
            grid.ColumnDefinitions = firstWidth is { } w
                ? new ColumnDefinitions($"{w},*")
                : new ColumnDefinitions("*,*");
            grid.RowDefinitions = new RowDefinitions("*");
            grid.ColumnSpacing = gap;
            grid.RowSpacing = 0;
            Grid.SetRow(first, 0);
            Grid.SetRow(second, 0);
            Grid.SetColumn(first, 0);
            Grid.SetColumn(second, 1);
        }
    }
}
