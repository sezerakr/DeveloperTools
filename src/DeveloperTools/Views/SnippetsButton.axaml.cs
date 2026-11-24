using System;
using Avalonia.Controls;
using Avalonia.Input;
using DeveloperTools.ViewModels;

namespace DeveloperTools.Views;

public partial class SnippetsButton : UserControl
{
    private SnippetsViewModel? _vm;
    private Flyout Popup => (Flyout)Toggle.Flyout!;

    public SnippetsButton()
    {
        InitializeComponent();
        // The flyout lives in a popup; hand it the same data context.
        Popup.Opening += (_, _) => Body.DataContext = DataContext;
        Popup.Opened += (_, _) => NameBox.Focus();
        NameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && _vm?.SaveCommand.CanExecute(null) == true)
            {
                _vm.SaveCommand.Execute(null);
                e.Handled = true;
            }
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_vm is not null)
            _vm.Loaded -= OnLoaded;
        _vm = DataContext as SnippetsViewModel;
        if (_vm is not null)
            _vm.Loaded += OnLoaded;
        base.OnDataContextChanged(e);
    }

    private void OnLoaded(object? sender, EventArgs e) => Popup.Hide();
}
