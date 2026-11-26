using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeveloperTools.Services;
using DeveloperTools.Tools;

using DeveloperTools.Localization;

namespace DeveloperTools.ViewModels;

public sealed record ZoneOption(TimeZoneInfo Zone, string Name)
{
    public override string ToString() => Name;
}

public partial class TimestampPageViewModel : PageViewModel
{
    private readonly IDialogService _dialogs;
    private readonly DispatcherTimer _clock;

    public TimestampPageViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;
        var local = TimeZoneInfo.Local;
        Zones = new List<ZoneOption> { new(local, $"Local ({local.Id})"), new(TimeZoneInfo.Utc, "UTC") }
            .Concat(TimeZoneInfo.GetSystemTimeZones()
                .Where(z => z.Id != local.Id && z.Id != "UTC")
                .Select(z => new ZoneOption(z, z.DisplayName)))
            .ToList();
        _zone = Zones[0];

        _input = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        _from = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        _to = DateTimeOffset.Now.AddDays(30).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        Convert();
        Difference();

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => Tick();
        Tick();
    }

    public override string Title => Loc.T("Timestamp");
    public override string Subtitle => Loc.T("Unix time, ISO 8601 and time zones");

    // ------------------------------------------------------------------ live clock

    [ObservableProperty] private string _nowSeconds = "";
    [ObservableProperty] private string _nowMillis = "";
    [ObservableProperty] private string _nowIso = "";

    private void Tick()
    {
        var now = DateTimeOffset.UtcNow;
        NowSeconds = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        NowMillis = now.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        NowIso = now.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        // Keep "relative" fresh when showing the current time.
        if (Input.Trim().Equals("now", StringComparison.OrdinalIgnoreCase))
            Convert();
    }

    // The live clock only ticks while the page is on screen.
    public override void OnShown()
    {
        Tick();
        _clock.Start();
    }

    public override void OnHidden() => _clock.Stop();

    [RelayCommand]
    private Task CopyNowSeconds() => _dialogs.CopyToClipboardAsync(NowSeconds);

    [RelayCommand]
    private Task CopyNowMillis() => _dialogs.CopyToClipboardAsync(NowMillis);

    [RelayCommand]
    private Task CopyNowIso() => _dialogs.CopyToClipboardAsync(NowIso);

    // ------------------------------------------------------------------ converter

    public IReadOnlyList<ZoneOption> Zones { get; }

    [ObservableProperty] private ZoneOption _zone;
    [ObservableProperty] private string _input;
    [ObservableProperty] private string _interpretation = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public bool HasError => Error is not null;

    public ObservableCollection<CopyableValue> Results { get; } = new();

    partial void OnInputChanged(string value) => Convert();
    partial void OnZoneChanged(ZoneOption value)
    {
        Convert();
        Difference();
    }

    private void Convert()
    {
        Results.Clear();
        Error = null;
        Interpretation = "";
        if (string.IsNullOrWhiteSpace(Input) || Zone is null)
            return;

        var parsed = TimeTools.Parse(Input, Zone.Zone);
        if (parsed is null)
        {
            Error = Loc.T("Not recognised. Try 1700000000, 1700000000000, 2024-05-01T10:00:00Z, 01.05.2024 10:00 or \"now\".");
            return;
        }

        Interpretation = parsed.Interpretation;
        foreach (var (label, value) in TimeTools.Formats(parsed.Value, Zone.Zone, DateTimeOffset.UtcNow))
            Results.Add(new CopyableValue(value, _dialogs.CopyToClipboardAsync, label));
    }

    [RelayCommand]
    private void UseNow() => Input = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ difference

    [ObservableProperty] private string _from;
    [ObservableProperty] private string _to;
    [ObservableProperty] private string _differenceText = "";
    [ObservableProperty] private string _differenceDetail = "";

    partial void OnFromChanged(string value) => Difference();
    partial void OnToChanged(string value) => Difference();

    private void Difference()
    {
        if (Zone is null || From is null || To is null)
            return;
        var a = TimeTools.Parse(From, Zone.Zone);
        var b = TimeTools.Parse(To, Zone.Zone);
        if (a is null || b is null)
        {
            DifferenceText = Loc.T("Enter two dates or timestamps.");
            DifferenceDetail = "";
            return;
        }

        var span = b.Value - a.Value;
        DifferenceText = (span < TimeSpan.Zero ? "−" : "") + TimeTools.Duration(span);
        DifferenceDetail = string.Create(CultureInfo.InvariantCulture,
            $"{Math.Abs(span.TotalDays):#,0.##} days  ·  {Math.Abs(span.TotalHours):#,0.##} hours  ·  {Math.Abs(span.TotalSeconds):#,0} seconds");
    }

    [RelayCommand]
    private void SwapDates() => (From, To) = (To, From);
}
