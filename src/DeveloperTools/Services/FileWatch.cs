using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace DeveloperTools.Services;

/// <summary>
/// Notices changes made outside the app, using the OS change notifications (no polling, no CPU while idle).
/// Folder watchers report added/removed/renamed files and folders; the file watcher reports edits to the open file.
/// Bursts of events (editors often write several times per save) are merged into one callback on the UI thread.
/// </summary>
public sealed class FileWatch : IDisposable
{
    private readonly List<FileSystemWatcher> _folders = new();
    private readonly DispatcherTimer _treeTimer;
    private readonly DispatcherTimer _fileTimer;
    private FileSystemWatcher? _file;
    private string? _filePath;
    private string _folderKey = "";

    /// <param name="treeChanged">Files or folders were added, removed or renamed.</param>
    /// <param name="fileChanged">The watched file was changed, removed or renamed.</param>
    public FileWatch(Action treeChanged, Action fileChanged)
    {
        _treeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _treeTimer.Tick += (_, _) =>
        {
            _treeTimer.Stop();
            treeChanged();
        };
        _fileTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _fileTimer.Tick += (_, _) =>
        {
            _fileTimer.Stop();
            fileChanged();
        };
    }

    /// <summary>File watching needs a real file system (not the browser).</summary>
    public static bool Supported => !OperatingSystem.IsBrowser();

    /// <summary>Watches these folders and everything below them. Re-watching the same set is free.</summary>
    public void WatchFolders(IReadOnlyCollection<string> folders)
    {
        var key = string.Join("|", folders);
        if (key == _folderKey)
            return;
        _folderKey = key;

        foreach (var w in _folders)
            w.Dispose();
        _folders.Clear();

        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder))
                continue;
            try
            {
                var w = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    InternalBufferSize = 64 * 1024
                };
                w.Created += (_, e) => OnTreeEvent(e.FullPath);
                w.Deleted += (_, e) => OnTreeEvent(e.FullPath);
                w.Renamed += (_, e) => { OnTreeEvent(e.OldFullPath); OnTreeEvent(e.FullPath); };
                w.Error += (_, _) => Restart(_treeTimer); // buffer overflow: just rescan
                w.EnableRaisingEvents = true;
                _folders.Add(w);
            }
            catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                // folder cannot be watched (e.g. some network drives): the F5 refresh still works
            }
        }
    }

    /// <summary>Watches one file (the open document); null stops watching.</summary>
    public void WatchFile(string? path)
    {
        if (string.Equals(path, _filePath, StringComparison.OrdinalIgnoreCase) && _file is not null)
            return;
        _file?.Dispose();
        _file = null;
        _filePath = path;
        if (path is null || Path.GetDirectoryName(path) is not { } dir || !Directory.Exists(dir))
            return;
        try
        {
            _file = new FileSystemWatcher(dir, Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
            };
            _file.Changed += (_, _) => Restart(_fileTimer);
            _file.Created += (_, _) => Restart(_fileTimer);
            _file.Deleted += (_, _) => Restart(_fileTimer);
            _file.Renamed += (_, _) => Restart(_fileTimer);
            _file.EnableRaisingEvents = true;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            _file = null;
        }
    }

    private void OnTreeEvent(string path)
    {
        // Only folders and markdown files change what the explorer shows.
        var ext = Path.GetExtension(path);
        if (ext.Length == 0 || MarkdownTree.IsMarkdown(path))
            Restart(_treeTimer);
    }

    /// <summary>Called from watcher threads: (re)start the debounce timer on the UI thread.</summary>
    private static void Restart(DispatcherTimer timer) =>
        Dispatcher.UIThread.Post(() =>
        {
            timer.Stop();
            timer.Start();
        });

    /// <summary>Reads a file another program may still have open.</summary>
    public static async Task<string> ReadSharedAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync();
    }

    public void Dispose()
    {
        foreach (var w in _folders)
            w.Dispose();
        _folders.Clear();
        _file?.Dispose();
        _file = null;
    }
}
