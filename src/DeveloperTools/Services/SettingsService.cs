using System;
using System.IO;
using System.Text.Json;
using DeveloperTools.Models;

namespace DeveloperTools.Services;

public sealed class SettingsService
{
    public static readonly string SettingsDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeveloperTools");

    public static readonly string SettingsFile = Path.Combine(SettingsDirectory, "settings.json");

    public SettingsService()
    {
        Current = Load();
    }

    public AppSettings Current { get; }

    /// <summary>Raised after settings were saved.</summary>
    public event EventHandler? Changed;

    public void Save()
    {
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(Current, AppJsonContext.Default.AppSettings));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
                return JsonSerializer.Deserialize(File.ReadAllText(SettingsFile), AppJsonContext.Default.AppSettings)
                       ?? new AppSettings();
        }
        catch (Exception)
        {
            // Keep a copy of an unreadable file instead of silently overwriting it on the next save.
            try { File.Copy(SettingsFile, SettingsFile + ".bak", overwrite: true); } catch { /* best effort */ }
        }

        return new AppSettings();
    }
}
