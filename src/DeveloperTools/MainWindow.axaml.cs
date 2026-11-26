using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using DeveloperTools.Services;
using DeveloperTools.ViewModels;

namespace DeveloperTools;

/// <summary>
/// Desktop window around <see cref="Views.MainView"/>: placement on the right screen, unsaved-changes check,
/// clipboard hints, memory trimming.
/// </summary>
public partial class MainWindow : Window
{
    private const double DefaultWidth = 1360;
    private const double DefaultHeight = 860;
    private const int ScreenMargin = 24; // keep a little desktop visible around a fitted window

    private readonly DispatcherTimer _trimTimer;
    private bool _closeConfirmed;
    private bool _startupTrimmed;
    private SettingsService? _settings;
    private bool _restartAfterClose;

    // Last size/position while in the normal (not maximized/minimized) state, so it can be saved.
    private PixelPoint _normalPosition;
    private Size _normalSize;

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
        Opened += (_, _) =>
        {
            _trimTimer.Start();
            EnsureVisible();
            // Daily update check, well after startup so it never competes with the first paint.
            DispatcherTimer.RunOnce(() =>
            {
                if (DataContext is MainWindowViewModel vm)
                    _ = vm.Updates.CheckInBackgroundAsync();
            }, TimeSpan.FromSeconds(10));
        };
        Closed += (_, _) =>
        {
            if (_restartAfterClose)
                UpdateService.StartNewInstance();
        };
        Deactivated += (_, _) => _trimTimer.Start();
        Activated += (_, _) => { if (_startupTrimmed) _trimTimer.Stop(); };

        PositionChanged += (_, _) => RememberNormalBounds();
        Resized += (_, _) => RememberNormalBounds();
        // Moved to a screen with a different DPI: check once, after Windows has finished rescaling.
        ScalingChanged += (_, _) => Dispatcher.UIThread.Post(EnsureVisible, DispatcherPriority.Background);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainWindowViewModel vm)
            vm.RestartRequested += (_, _) =>
            {
                _restartAfterClose = true;
                Close();
            };
    }

    // ------------------------------------------------------------------ placement

    /// <summary>
    /// Places the window before it is shown: the last saved spot when that is still fully on a screen,
    /// otherwise centered on the primary screen. The size always fits the screen's working area, so the
    /// title bar is never off-screen (e.g. a big window opened on a small second monitor).
    /// </summary>
    public void RestorePlacement(SettingsService settings)
    {
        _settings = settings;
        var s = settings.Current;
        var screens = Screens;
        if (screens is null || screens.ScreenCount == 0)
            return;

        var width = s.WindowWidth ?? DefaultWidth;
        var height = s.WindowHeight ?? DefaultHeight;

        Screen? screen = null;
        if (s.WindowX is { } x && s.WindowY is { } y)
        {
            // The saved window's title bar (its top centre) must still be on a connected screen.
            var titleBar = new PixelPoint(x + 120, y + 12);
            screen = screens.All.FirstOrDefault(sc => sc.WorkingArea.Contains(titleBar));
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        if (screen is not null)
        {
            Place(screen, width, height, new PixelPoint(s.WindowX!.Value, s.WindowY!.Value));
        }
        else
        {
            Place(screens.Primary ?? screens.All[0], width, height, null);
        }

        if (s.WindowMaximized)
            WindowState = WindowState.Maximized;
    }

    /// <summary>Fits the size to the screen and puts the window fully inside its working area.</summary>
    private void Place(Screen screen, double width, double height, PixelPoint? preferred)
    {
        var area = screen.WorkingArea;
        var scaling = screen.Scaling;
        var maxWidth = (area.Width - 2 * ScreenMargin) / scaling;
        var maxHeight = (area.Height - 2 * ScreenMargin) / scaling;

        if (maxWidth < MinWidth || maxHeight < MinHeight)
        {
            // Screen smaller than the minimum window: fill it instead.
            Position = area.Position;
            Width = Math.Max(MinWidth, area.Width / scaling);
            Height = Math.Max(MinHeight, area.Height / scaling);
            WindowState = WindowState.Maximized;
            return;
        }

        Width = Math.Clamp(width, MinWidth, maxWidth);
        Height = Math.Clamp(height, MinHeight, maxHeight);

        var pixelWidth = (int)Math.Round(Width * scaling);
        var pixelHeight = (int)Math.Round(Height * scaling);
        var centered = new PixelPoint(
            area.X + (area.Width - pixelWidth) / 2,
            area.Y + (area.Height - pixelHeight) / 2);

        var position = preferred ?? centered;
        // Keep the whole window (and so the title bar) inside the working area.
        position = new PixelPoint(
            Math.Clamp(position.X, area.X, area.Right - pixelWidth),
            Math.Clamp(position.Y, area.Y, area.Bottom - pixelHeight));
        Position = position;
        _normalPosition = position;
        _normalSize = new Size(Width, Height);
    }

    /// <summary>After showing (or moving to another screen): pull the window back if its title bar is unreachable.</summary>
    private void EnsureVisible()
    {
        if (WindowState != WindowState.Normal || Screens is not { } screens)
            return;
        var screen = screens.ScreenFromWindow(this) ?? screens.Primary;
        if (screen is null)
            return;

        var area = screen.WorkingArea;
        var scaling = screen.Scaling;
        var frame = FrameSize ?? ClientSize;
        var tooBig = frame.Width * scaling > area.Width || frame.Height * scaling > area.Height;
        var titleBarHidden = Position.Y < area.Y || Position.Y > area.Bottom - 40 ||
                             Position.X > area.Right - 120 || Position.X + frame.Width * scaling < area.X + 120;
        if (tooBig || titleBarHidden)
            Place(screen, Width, Height, tooBig ? null : Position);
    }

    private void RememberNormalBounds()
    {
        if (WindowState != WindowState.Normal || !IsVisible)
            return;
        _normalPosition = Position;
        _normalSize = new Size(Width, Height);
    }

    private void SavePlacement()
    {
        if (_settings is null || WindowState == WindowState.Minimized && _normalSize == default)
            return;
        var s = _settings.Current;
        s.WindowMaximized = WindowState == WindowState.Maximized;
        if (_normalSize != default)
        {
            s.WindowX = _normalPosition.X;
            s.WindowY = _normalPosition.Y;
            s.WindowWidth = Math.Round(_normalSize.Width);
            s.WindowHeight = Math.Round(_normalSize.Height);
        }
        try
        {
            _settings.Save();
        }
        catch (Exception)
        {
            // never block closing because settings could not be written
        }
    }

    // ------------------------------------------------------------------ closing

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || DataContext is not MainWindowViewModel vm || !(vm.Markdown.IsDirty || vm.JsonIfCreated?.IsDirty == true))
        {
            SavePlacement();
            return;
        }

        // Ask about unsaved changes, then close again once answered.
        e.Cancel = true;
        if (await vm.Markdown.ConfirmLeaveAsync() && (vm.JsonIfCreated is not { } json || await json.ConfirmLeaveAsync()))
        {
            _closeConfirmed = true;
            Close();
        }
        else
        {
            _restartAfterClose = false; // user chose to stay
        }
    }
}
