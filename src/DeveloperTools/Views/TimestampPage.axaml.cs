using Avalonia.Controls;

namespace DeveloperTools.Views;

public partial class TimestampPage : UserControl
{
    public TimestampPage()
    {
        InitializeComponent();
        // Narrow windows: the zone picker goes under the input.
        SizeChanged += (_, e) =>
        {
            var narrow = e.NewSize.Width < 720;
            var zone = InputRow.Children[1];
            var now = InputRow.Children[2];
            if (narrow)
            {
                InputRow.ColumnDefinitions = new ColumnDefinitions("*,Auto");
                InputRow.RowDefinitions = new RowDefinitions("Auto,Auto");
                InputRow.RowSpacing = 8;
                Grid.SetColumnSpan(InputBox, 1);
                Grid.SetColumn(now, 1);
                Grid.SetRow(zone, 1);
                Grid.SetColumn(zone, 0);
                Grid.SetColumnSpan(zone, 2);
                ((ComboBox)zone).Width = double.NaN;
                ((ComboBox)zone).HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            }
            else
            {
                InputRow.ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto");
                InputRow.RowDefinitions = new RowDefinitions("Auto");
                Grid.SetRow(zone, 0);
                Grid.SetColumn(zone, 1);
                Grid.SetColumnSpan(zone, 1);
                Grid.SetColumn(now, 2);
                ((ComboBox)zone).Width = 240;
            }
        };
    }
}
