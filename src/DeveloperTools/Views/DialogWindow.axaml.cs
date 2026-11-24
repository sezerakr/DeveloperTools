using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;

namespace DeveloperTools.Views;

/// <summary>OS dialog window (desktop) around <see cref="DialogView"/>.</summary>
public partial class DialogWindow : Window
{
    public DialogWindow()
    {
        InitializeComponent();
    }

    public static Task<DialogResult?> ShowAsync(Window owner, DialogOptions options)
    {
        var dialog = new DialogWindow { Title = options.Title };
        dialog.Body.Configure(options, result => dialog.Close(result));
        dialog.Opened += (_, _) => Dispatcher.UIThread.Post(dialog.Body.FocusFirst, DispatcherPriority.Background);
        return dialog.ShowDialog<DialogResult?>(owner);
    }
}
