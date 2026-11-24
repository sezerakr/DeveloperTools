using Avalonia.Controls;
using Avalonia.Threading;

namespace DeveloperTools.Views;

public partial class EncodePage : UserControl
{
    private const double StackBreakpoint = 900;
    private bool? _stacked;

    public EncodePage()
    {
        InitializeComponent();
        ApplyLayout(false);
        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width < StackBreakpoint);
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => InputBox.Focus(), DispatcherPriority.Background);
    }

    /// <summary>Input and output side by side on wide windows, stacked (each half the height) on narrow ones.</summary>
    private void ApplyLayout(bool stacked)
    {
        if (_stacked == stacked)
            return;
        _stacked = stacked;
        TwoPane.Apply(Panes, stacked);
        if (stacked)
            Panes.RowDefinitions = new RowDefinitions("*,*");
    }
}
