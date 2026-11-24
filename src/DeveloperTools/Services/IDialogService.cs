using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace DeveloperTools.Services;

public enum UnsavedChoice
{
    Save,
    Discard,
    Cancel
}

public interface IDialogService
{
    Task<UnsavedChoice> ConfirmUnsavedAsync(string fileName);

    Task<bool> ConfirmAsync(string title, string message, string confirmText, bool danger = false);

    /// <param name="validate">Returns an error message, or null when the input is valid.</param>
    Task<string?> PromptAsync(string title, string message, string initialValue, string confirmText,
        Func<string, string?>? validate = null);

    /// <summary>A local folder path (desktop only; null in the browser).</summary>
    Task<string?> PickFolderAsync(string title);

    /// <summary>A file to read; works on desktop and in the browser.</summary>
    Task<IStorageFile?> OpenFileAsync(string title, string typeName, params string[] patterns);

    /// <summary>A file to write (browser: download or File System Access API).</summary>
    Task<IStorageFile?> SaveFileAsync(string title, string suggestedName, string extension, string typeName);

    Task CopyToClipboardAsync(string text);

    Task<string?> GetClipboardTextAsync();

    Task CopyImageAsync(Avalonia.Media.Imaging.Bitmap bitmap);

    Task OpenUrlAsync(string url);

    /// <summary>Shows a file or folder in the OS file manager (no-op in the browser).</summary>
    void RevealInExplorer(string path);
}

/// <summary>Read/write helpers for picked files (stream based, so they also work in the browser).</summary>
public static class StorageFiles
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static string Describe(IStorageFile file) => file.TryGetLocalPath() ?? file.Name;

    public static async Task<long?> SizeAsync(IStorageFile file) => (await file.GetBasicPropertiesAsync()).Size is { } s ? (long)s : null;

    public static async Task<byte[]> ReadBytesAsync(IStorageFile file)
    {
        await using var stream = await file.OpenReadAsync();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }

    public static async Task<string> ReadTextAsync(IStorageFile file)
    {
        await using var stream = await file.OpenReadAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync();
    }

    public static Task WriteTextAsync(IStorageFile file, string text) => WriteBytesAsync(file, Utf8NoBom.GetBytes(text));

    public static async Task WriteBytesAsync(IStorageFile file, byte[] bytes)
    {
        await using var stream = await file.OpenWriteAsync();
        stream.SetLength(0);
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }
}
