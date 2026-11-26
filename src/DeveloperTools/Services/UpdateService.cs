using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using DeveloperTools.Localization;

namespace DeveloperTools.Services;

public enum UpdateState
{
    Idle,
    Checking,
    UpToDate,
    Downloading,
    Installed,   // new files are in place; used from the next start
    Failed,
    Disabled
}

/// <summary>
/// Silent self-update from GitHub releases (desktop only; the web version updates with the site).
/// Downloads the archive for this platform, verifies its SHA-256 (from the release's asset digest),
/// and swaps the files next to the running executable: the OS lets a running program's files be
/// renamed, so the current files become *.old and the new ones take their place. *.old files are
/// removed on the next start. No installer, no admin rights, no prompts.
/// </summary>
public sealed class UpdateService
{
    private const string Repository = "sezerakr/DeveloperTools";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private readonly SettingsService _settings;
    private int _running;

    public UpdateService(SettingsService settings)
    {
        _settings = settings;
    }

    /// <summary>Set by the desktop host for published builds only (not debug runs, not the self-test).</summary>
    public static bool Enabled { get; set; }

    public static Version CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);

    public UpdateState State { get; private set; } = UpdateState.Idle;
    public string Status { get; private set; } = "";
    public Version? InstalledVersion { get; private set; }

    /// <summary>Raised on the calling (UI) context whenever <see cref="State"/> or <see cref="Status"/> changes.</summary>
    public event EventHandler? Changed;

    private static string? PlatformAsset =>
        OperatingSystem.IsWindows() ? "-win-x64.zip" :
        OperatingSystem.IsLinux() ? "-linux-x64.tar.gz" : null;

    private static string AppDirectory => AppContext.BaseDirectory;

    /// <summary>Removes files replaced by a previous update (call once at startup).</summary>
    public static void CleanUpOldFiles()
    {
        if (!Enabled)
            return;
        try
        {
            foreach (var old in Directory.EnumerateFiles(AppDirectory, "*.old"))
                try { File.Delete(old); } catch { /* still locked: next time */ }
        }
        catch (Exception)
        {
            // best effort
        }
    }

    /// <summary>Daily background check (no-op when disabled, checked recently, or already running).</summary>
    public Task CheckInBackgroundAsync()
    {
        var last = _settings.Current.LastUpdateCheck;
        if (!Enabled || !_settings.Current.AutoUpdate || (last is { } l && DateTime.UtcNow - l < CheckInterval))
            return Task.CompletedTask;
        return CheckAsync(manual: false);
    }

    /// <summary>Checks GitHub and, when a newer release exists, downloads and installs it.</summary>
    public async Task CheckAsync(bool manual)
    {
        if (!Enabled)
        {
            Set(UpdateState.Disabled, AppServices.IsBrowser
                ? "The web version is always up to date."
                : "Updates are off for development builds.");
            return;
        }
        if (Interlocked.Exchange(ref _running, 1) == 1)
            return;

        try
        {
            Set(UpdateState.Checking, Loc.T("Checking for updates…"));
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("DeveloperTools-Updater");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            var json = await http.GetStringAsync($"https://api.github.com/repos/{Repository}/releases/latest").ConfigureAwait(false);
            _settings.Current.LastUpdateCheck = DateTime.UtcNow;
            SaveSettings();

            var release = ParseRelease(json);
            if (release is null || release.Version <= CurrentVersion)
            {
                Set(UpdateState.UpToDate, Loc.F("You have the latest version ({0}).", Format(CurrentVersion)));
                return;
            }

            var asset = release.Assets.FirstOrDefault(a => PlatformAsset is { } suffix && a.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            if (asset is null)
            {
                Set(UpdateState.Failed, Loc.F("Version {0} has no download for this system.", Format(release.Version)));
                return;
            }

            if (!CanWrite(AppDirectory))
            {
                Set(UpdateState.Failed, Loc.F("Version {0} is available, but this folder is read-only. Download it from GitHub.", Format(release.Version)));
                return;
            }

            Set(UpdateState.Downloading, Loc.F("Downloading version {0}…", Format(release.Version)));
            var work = Path.Combine(Path.GetTempPath(), "DeveloperTools-update", Format(release.Version));
            if (Directory.Exists(work))
                Directory.Delete(work, recursive: true);
            Directory.CreateDirectory(work);
            var archive = Path.Combine(work, asset.Name);

            await using (var source = await http.GetStreamAsync(asset.Url).ConfigureAwait(false))
            await using (var target = File.Create(archive))
                await source.CopyToAsync(target).ConfigureAwait(false);

            if (asset.Sha256 is { } expected)
            {
                await using var stream = File.OpenRead(archive);
                var actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream).ConfigureAwait(false));
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                {
                    Set(UpdateState.Failed, Loc.T("The download was damaged (checksum mismatch). It will be tried again later."));
                    return;
                }
            }

            var extracted = Path.Combine(work, "files");
            await Task.Run(() => Extract(archive, extracted)).ConfigureAwait(false);
            Install(extracted);

            try { Directory.Delete(work, recursive: true); } catch { /* temp folder */ }
            InstalledVersion = release.Version;
            Set(UpdateState.Installed, Loc.F("Version {0} is installed. Restart to use it.", Format(release.Version)));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                       or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            Set(UpdateState.Failed, manual
                ? "Could not check for updates: " + ex.Message
                : "The last update check did not finish. It will be tried again later.");
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    // ------------------------------------------------------------------ GitHub release

    private sealed record Asset(string Name, string Url, string? Sha256);

    private sealed record Release(Version Version, List<Asset> Assets);

    private static Release? ParseRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean() ||
            root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean())
            return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version))
            return null;

        var assets = new List<Asset>();
        foreach (var a in root.GetProperty("assets").EnumerateArray())
        {
            var name = a.GetProperty("name").GetString() ?? "";
            var url = a.GetProperty("browser_download_url").GetString() ?? "";
            string? sha = null;
            if (a.TryGetProperty("digest", out var digest) && digest.GetString() is { } d && d.StartsWith("sha256:", StringComparison.Ordinal))
                sha = d["sha256:".Length..];
            // Only accept downloads from this repository's releases.
            if (url.StartsWith($"https://github.com/{Repository}/releases/download/", StringComparison.Ordinal))
                assets.Add(new Asset(name, url, sha));
        }
        return new Release(Normalize(version), assets);
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    private static string Format(Version v) => $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";

    // ------------------------------------------------------------------ install

    private static void Extract(string archive, string destination)
    {
        Directory.CreateDirectory(destination);
        if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archive, destination, overwriteFiles: true);
        }
        else
        {
            using var file = File.OpenRead(archive);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            TarFile.ExtractToDirectory(gzip, destination, overwriteFiles: true);
        }
    }

    /// <summary>Replaces the app's own files. Running files are renamed to *.old first (allowed while in use).</summary>
    private static void Install(string extracted)
    {
        var files = Directory.EnumerateFiles(extracted)
            .Where(f => !(OperatingSystem.IsLinux() && Path.GetFileName(f) is "install.sh" or "developertools.desktop" or "developertools.png"))
            .ToList();
        if (!files.Any(f => Path.GetFileNameWithoutExtension(f).Equals("DeveloperTools", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The download does not contain the app.");

        var replaced = new List<(string target, string backup)>();
        try
        {
            foreach (var file in files)
            {
                var target = Path.Combine(AppDirectory, Path.GetFileName(file));
                if (File.Exists(target))
                {
                    var backup = target + ".old";
                    if (File.Exists(backup))
                        File.Delete(backup);
                    File.Move(target, backup);
                    replaced.Add((target, backup));
                }
                File.Move(file, target);
                if (OperatingSystem.IsLinux() && Path.GetFileName(target) == "DeveloperTools")
                    File.SetUnixFileMode(target, File.GetUnixFileMode(target) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }
        }
        catch
        {
            // Put the previous version back so the app keeps working.
            foreach (var (target, backup) in replaced)
            {
                try
                {
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(backup, target);
                }
                catch { /* best effort */ }
            }
            throw;
        }
    }

    private static bool CanWrite(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, ".write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Starts the (updated) app again; the caller closes this instance.</summary>
    public static void StartNewInstance()
    {
        var exe = Environment.ProcessPath;
        if (exe is not null)
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = AppDirectory });
    }

    // ------------------------------------------------------------------ helpers

    // The check runs off the UI thread; settings and listeners are touched on the UI thread only.
    private void SaveSettings() =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            try { _settings.Save(); } catch { /* not critical */ }
        });

    private void Set(UpdateState state, string status) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            State = state;
            Status = status;
            Changed?.Invoke(this, EventArgs.Empty);
        });
}
