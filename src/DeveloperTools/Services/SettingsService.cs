using System;
using System.Text.Json;
using DeveloperTools.Models;

namespace DeveloperTools.Services;

public sealed class SettingsService
{
    private const string Name = "settings.json";

    public SettingsService()
    {
        Current = Load();
    }

    public AppSettings Current { get; }

    /// <summary>Where settings live (a file path on desktop, browser storage on the web).</summary>
    public static string Location => AppServices.Storage.Describe(Name);

    public static string? LocalPath => AppServices.Storage.LocalPath(Name);

    /// <summary>Raised after settings were saved.</summary>
    public event EventHandler? Changed;

    public void Save()
    {
        AppServices.Storage.Write(Name, JsonSerializer.Serialize(Current, AppJsonContext.Default.AppSettings));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static AppSettings Load()
    {
        string? text = null;
        try
        {
            text = AppServices.Storage.Read(Name);
            if (text is not null)
                return JsonSerializer.Deserialize(text, AppJsonContext.Default.AppSettings) ?? new AppSettings();
        }
        catch (Exception)
        {
            // Keep a copy of unreadable settings instead of silently overwriting them on the next save.
            try { if (text is not null) AppServices.Storage.Write(Name + ".bak", text); } catch { /* best effort */ }
        }

        return new AppSettings();
    }
}
