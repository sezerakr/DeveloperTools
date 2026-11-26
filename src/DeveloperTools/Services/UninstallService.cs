using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace DeveloperTools.Services;

/// <summary>
/// Removing the desktop app. On Windows the app lists itself in Settings → Apps (per user, no admin rights);
/// "Uninstall" there runs <c>DeveloperTools.exe --uninstall</c>. Only the app's own files are deleted
/// (never a whole folder), so an app unzipped into e.g. Downloads cannot take other files with it.
/// Settings and snippets are kept unless the user chooses to remove them.
/// </summary>
public static class UninstallService
{
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DeveloperTools";

    /// <summary>Files that belong to the app (both platforms), plus *.old leftovers from updates.</summary>
    private static readonly string[] AppFiles =
    [
        "DeveloperTools.exe", "DeveloperTools", "av_libglesv2.dll", "libHarfBuzzSharp.dll", "libSkiaSharp.dll",
        "libHarfBuzzSharp.so", "libSkiaSharp.so", "install.sh", "uninstall.sh", "developertools.desktop",
        "developertools.png", "DeveloperTools.dbg"
    ];

    /// <summary>Only published builds (the ones that update themselves) can be uninstalled from inside the app.</summary>
    public static bool IsAvailable => UpdateService.Enabled && !AppServices.IsBrowser;

    private static string AppDirectory => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
    private static string? ExePath => Environment.ProcessPath;

    // ------------------------------------------------------------------ Windows "Apps" entry

    /// <summary>Writes the Apps &amp; features entry when it is missing or points to another version/folder.</summary>
    public static void RegisterIfNeeded()
    {
        if (!IsAvailable || !OperatingSystem.IsWindows() || ExePath is not { } exe)
            return;
        try
        {
            Register(exe);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            // not critical
        }
    }

    [SupportedOSPlatform("windows")]
    private static void Register(string exe)
    {
        var version = UpdateService.CurrentVersion.ToString(3);
        using (var existing = Registry.CurrentUser.OpenSubKey(UninstallKey))
        {
            if (existing?.GetValue("DisplayVersion") as string == version &&
                existing.GetValue("InstallLocation") as string == AppDirectory)
                return; // up to date: nothing to write
        }

        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        key.SetValue("DisplayName", "DeveloperTools");
        key.SetValue("DisplayVersion", version);
        key.SetValue("Publisher", "DeveloperTools");
        key.SetValue("DisplayIcon", exe);
        key.SetValue("InstallLocation", AppDirectory);
        key.SetValue("UninstallString", $"\"{exe}\" --uninstall");
        key.SetValue("QuietUninstallString", $"\"{exe}\" --uninstall --quiet");
        key.SetValue("URLInfoAbout", "https://github.com/sezerakr/DeveloperTools");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        var size = AppFiles.Select(f => Path.Combine(AppDirectory, f)).Where(File.Exists).Sum(f => new FileInfo(f).Length);
        key.SetValue("EstimatedSize", (int)(size / 1024), RegistryValueKind.DWord);
    }

    // ------------------------------------------------------------------ uninstall

    /// <summary>
    /// Removes shortcuts, the Apps entry and (optionally) settings now, then deletes the program files
    /// a moment after this process exits. The caller closes the app afterwards.
    /// </summary>
    public static void Uninstall(bool keepSettings)
    {
        if (!IsAvailable)
            return;

        if (OperatingSystem.IsWindows())
        {
            var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            TryDelete(Path.Combine(startMenu, "DeveloperTools.lnk"));
            TryDelete(Path.Combine(desktop, "DeveloperTools.lnk"));
            TryDelete(Path.Combine(appData, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar\DeveloperTools.lnk"));
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false); } catch { /* best effort */ }
        }
        else if (OperatingSystem.IsLinux())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            TryDelete(Path.Combine(home, ".local/share/applications/developertools.desktop"));
            TryDelete(Path.Combine(home, ".local/share/icons/hicolor/256x256/apps/developertools.png"));
            var link = Path.Combine(home, ".local/bin/DeveloperTools");
            if (File.Exists(link) && new FileInfo(link).LinkTarget is { } target &&
                Path.GetFullPath(target, Path.GetDirectoryName(link)!).StartsWith(AppDirectory, StringComparison.Ordinal))
                TryDelete(link);
        }

        if (!keepSettings)
        {
            try
            {
                if (Directory.Exists(FileAppStorage.Directory))
                    Directory.Delete(FileAppStorage.Directory, recursive: true); // settings, snippets, translations
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // best effort
            }
        }

        try
        {
            var temp = Path.Combine(Path.GetTempPath(), "DeveloperTools-update");
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        ScheduleProgramRemoval();
    }

    /// <summary>The running files cannot delete themselves; a short helper does it after we exit.</summary>
    private static void ScheduleProgramRemoval()
    {
        var files = Directory.EnumerateFiles(AppDirectory)
            .Where(f => AppFiles.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase) || f.EndsWith(".old", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (files.Count == 0)
            return;

        if (OperatingSystem.IsWindows())
        {
            // Wait for this process to end, delete our files, then the folder only if it is now empty.
            var deletes = string.Join(" ", files.Select(f => $"\"{f}\""));
            var command = $"/c ping -n 3 127.0.0.1 >nul & del /f /q {deletes} & rmdir \"{AppDirectory}\"";
            Process.Start(new ProcessStartInfo("cmd.exe", command) { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
        }
        else
        {
            // Unix allows deleting a running program's files.
            foreach (var f in files)
                TryDelete(f);
            try { if (!Directory.EnumerateFileSystemEntries(AppDirectory).Any()) Directory.Delete(AppDirectory); } catch { /* best effort */ }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // best effort
        }
    }

    /// <summary>Opens the uninstall window in a new process (the current app then closes).</summary>
    public static void StartUninstaller()
    {
        if (ExePath is { } exe)
            Process.Start(new ProcessStartInfo(exe, "--uninstall") { UseShellExecute = false });
    }
}
