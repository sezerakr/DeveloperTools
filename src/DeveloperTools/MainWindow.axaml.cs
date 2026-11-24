using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using DeveloperTools.ViewModels;

namespace DeveloperTools;

public partial class MainWindow : Window
{
    /// <summary>Below this window width the sidebar collapses to icons.</summary>
    private const double CompactBreakpoint = 1100;

    private const double SidebarWide = 224;
    private const double SidebarCompact = 60;

    private bool _closeConfirmed;
    private bool? _wasNarrow;
    private MainWindowViewModel? _vm;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
        Activated += async (_, _) =>
        {
            if (_vm is not null)
                await _vm.CheckClipboardAsync();
        };
        PaletteBox.AddHandler(KeyDownEvent, OnPaletteKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        // Give memory back to Windows when it is not needed: once after startup, and when the
        // window has been in the background for a few seconds (startup garbage, closed files, old previews).
        _trimTimer = new DispatcherTimer { Interval = System.TimeSpan.FromSeconds(3) };
        _trimTimer.Tick += (_, _) =>
        {
            _trimTimer.Stop();
            if (!_startupTrimmed || !IsActive)
            {
                _startupTrimmed = true;
                System.GC.Collect(System.GC.MaxGeneration, System.GCCollectionMode.Aggressive, blocking: true, compacting: true);
            }
        };
        Opened += (_, _) => _trimTimer.Start();
        Deactivated += (_, _) => _trimTimer.Start();
        Activated += (_, _) => { if (_startupTrimmed) _trimTimer.Stop(); };
    }

    private readonly DispatcherTimer _trimTimer;
    private bool _startupTrimmed;

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

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _vm = DataContext as MainWindowViewModel;
        if (_vm is not null)
            _vm.PropertyChanged += OnViewModelPropertyChanged;
        ApplySidebar();
        base.OnDataContextChanged(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ClientSizeProperty || _vm is null)
            return;

        // Collapse/expand automatically only when crossing the breakpoint,
        // so a manual toggle is respected while resizing within a range.
        var narrow = ClientSize.Width < CompactBreakpoint;
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

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || DataContext is not MainWindowViewModel vm || !(vm.Markdown.IsDirty || vm.JsonIfCreated?.IsDirty == true))
            return;

        // Ask about unsaved changes, then close again once answered.
        e.Cancel = true;
        if (await vm.Markdown.ConfirmLeaveAsync() && (vm.JsonIfCreated is not { } json || await json.ConfirmLeaveAsync()))
        {
            _closeConfirmed = true;
            Close();
        }
    }
}
