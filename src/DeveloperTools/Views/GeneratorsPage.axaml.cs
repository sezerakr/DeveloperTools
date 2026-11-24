using System;
using System.ComponentModel;
using Avalonia.Controls;
using DeveloperTools.ViewModels;

namespace DeveloperTools.Views;

public partial class GeneratorsPage : UserControl
{
    private const double StackBreakpoint = 820;
    private GeneratorsPageViewModel? _vm;
    private bool? _stacked;

    public GeneratorsPage()
    {
        InitializeComponent();
        ApplyLayout(false);
        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width < StackBreakpoint);
    }

    private void ApplyLayout(bool stacked)
    {
        if (_stacked == stacked)
            return;
        _stacked = stacked;
        foreach (var pane in new[] { PasswordPane, SecretPane, UuidPane, LoremPane, FakePane, QrPane })
            TwoPane.Apply(pane, stacked, firstWidth: 340);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _vm = DataContext as GeneratorsPageViewModel;
        if (_vm is not null)
            _vm.PropertyChanged += OnViewModelPropertyChanged;
        UpdateMeter();
        base.OnDataContextChanged(e);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GeneratorsPageViewModel.StrengthLevel) or nameof(GeneratorsPageViewModel.PasswordError))
            UpdateMeter();
    }

    /// <summary>Lights level+1 of five bars: red when weak, amber when fair, green when strong.</summary>
    private void UpdateMeter()
    {
        var level = _vm is { PasswordError: null } vm ? vm.StrengthLevel : -1;
        var tone = level switch { <= 1 => "weak", 2 => "fair", _ => "strong" };
        for (var i = 0; i < Meter.Children.Count; i++)
        {
            var bar = Meter.Children[i];
            var on = i <= level;
            bar.Classes.Set("weak", on && tone == "weak");
            bar.Classes.Set("fair", on && tone == "fair");
            bar.Classes.Set("strong", on && tone == "strong");
        }
    }
}
