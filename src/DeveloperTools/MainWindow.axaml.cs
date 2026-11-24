using System;
using Avalonia.Controls;
using Avalonia.Threading;
using DeveloperTools.ViewModels;

namespace DeveloperTools;

/// <summary>Desktop window around <see cref="Views.MainView"/>: unsaved-changes check, clipboard hints, memory trimming.</summary>
public partial class MainWindow : Window
{
    private readonly DispatcherTimer _trimTimer;
    private bool _closeConfirmed;
    private bool _startupTrimmed;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
        Activated += async (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
                await vm.CheckClipboardAsync();
        };

        // Give memory back to the OS when it is not needed: once after startup, and when the
        // window has been in the background for a few seconds (startup garbage, closed files, old previews).
        _trimTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _trimTimer.Tick += (_, _) =>
        {
            _trimTimer.Stop();
            if (!_startupTrimmed || !IsActive)
            {
                _startupTrimmed = true;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            }
        };
        Opened += (_, _) => _trimTimer.Start();
        Deactivated += (_, _) => _trimTimer.Start();
        Activated += (_, _) => { if (_startupTrimmed) _trimTimer.Stop(); };
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
