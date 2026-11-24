using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using DeveloperTools.ViewModels;

namespace DeveloperTools.Views;

/// <summary>The whole app UI: sidebar, header, pages, command palette. Hosted by a window (desktop) or the page (browser).</summary>
public partial class MainView : UserControl
{
    /// <summary>Below this width the sidebar collapses to icons.</summary>
    private const double CompactBreakpoint = 1100;

    private const double SidebarWide = 224;
    private const double SidebarCompact = 60;

    private bool? _wasNarrow;
    private MainWindowViewModel? _vm;

    public MainView()
    {
        InitializeComponent();
        PaletteBox.AddHandler(KeyDownEvent, OnPaletteKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        SizeChanged += (_, e) => ApplyBreakpoint(e.NewSize.Width);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _vm = DataContext as MainWindowViewModel;
        if (_vm is not null)
            _vm.PropertyChanged += OnViewModelPropertyChanged;
        ApplySidebar();
        base.OnDataContextChanged(e);
    }

    /// <summary>Collapse/expand automatically only when crossing the breakpoint, so a manual toggle sticks.</summary>
    private void ApplyBreakpoint(double width)
    {
        if (_vm is null || width <= 0)
            return;
        var narrow = width < CompactBreakpoint;
        if (_wasNarrow != narrow)
        {
            _wasNarrow = narrow;
            _vm.IsSidebarCollapsed = narrow;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsSidebarCollapsed))
            ApplySidebar();
        else if (e.PropertyName == nameof(MainWindowViewModel.IsPaletteOpen) && _vm is { IsPaletteOpen: true })
            Dispatcher.UIThread.Post(() => PaletteBox.Focus(), DispatcherPriority.Background);
    }

    private void ApplySidebar()
    {
        var collapsed = _vm?.IsSidebarCollapsed ?? false;
        RootGrid.ColumnDefinitions[0].Width = new GridLength(collapsed ? SidebarCompact : SidebarWide);
        Sidebar.Classes.Set("compact", collapsed);
        SidebarToggle.HorizontalAlignment = collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Right;
        Grid.SetColumn(SidebarToggle, collapsed ? 0 : 1);
        Grid.SetColumnSpan(SidebarToggle, collapsed ? 2 : 1);
    }

    // ------------------------------------------------------------------ command palette

    private void OnPaletteKeyDown(object? sender, KeyEventArgs args)
    {
        if (_vm is null)
            return;
        switch (args.Key)
        {
            case Key.Down:
                _vm.MovePaletteSelection(1);
                if (_vm.PaletteSelection is { } down) PaletteList.ScrollIntoView(down);
                args.Handled = true;
                break;
            case Key.Up:
                _vm.MovePaletteSelection(-1);
                if (_vm.PaletteSelection is { } up) PaletteList.ScrollIntoView(up);
                args.Handled = true;
                break;
            case Key.Enter:
                _vm.RunPaletteItemCommand.Execute(null);
                args.Handled = true;
                break;
            case Key.Escape:
                _vm.IsPaletteOpen = false;
                args.Handled = true;
                break;
        }
    }

    private void OnOverlayPressed(object? sender, PointerPressedEventArgs args)
    {
        if (_vm is not null)
            _vm.IsPaletteOpen = false;
    }

    private void OnPaletteCardPressed(object? sender, PointerPressedEventArgs args) => args.Handled = true;

    private void OnPaletteItemTapped(object? sender, TappedEventArgs args)
    {
        if (args.Source is Visual { DataContext: PaletteItem item })
            _vm?.RunPaletteItemCommand.Execute(item);
    }

    // ------------------------------------------------------------------ in-app dialogs

    /// <summary>Shows a dialog over the app (no OS window) and waits for its result.</summary>
    public async Task<DialogResult?> ShowDialogAsync(DialogOptions options)
    {
        var view = new DialogView();
        var done = new TaskCompletionSource<DialogResult?>();
        view.Configure(options, result => done.TrySetResult(result));
        DialogHost.Content = view;
        DialogOverlay.IsVisible = true;
        Dispatcher.UIThread.Post(view.FocusFirst, DispatcherPriority.Background);
        try
        {
            return await done.Task;
        }
        finally
        {
            DialogOverlay.IsVisible = false;
            DialogHost.Content = null;
        }
    }
}
