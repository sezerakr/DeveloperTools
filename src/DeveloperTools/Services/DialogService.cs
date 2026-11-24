using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using DeveloperTools.Views;

namespace DeveloperTools.Services;

/// <param name="topLevel">The window (desktop) or the browser top level (resolved lazily: it exists only once shown).</param>
/// <param name="view">Used for in-app dialogs when there is no OS window.</param>
public sealed class DialogService(Func<TopLevel?> topLevel, MainView view) : IDialogService
{
    private TopLevel Host => topLevel() ?? throw new InvalidOperationException("The app is not shown yet.");

    private Task<DialogResult?> ShowAsync(DialogOptions options) =>
        Host is Window window ? DialogWindow.ShowAsync(window, options) : view.ShowDialogAsync(options);

    public async Task<UnsavedChoice> ConfirmUnsavedAsync(string fileName)
    {
        var result = await ShowAsync(new DialogOptions(
            "Unsaved changes",
            $"\"{fileName}\" has unsaved changes. Save them before continuing?",
            PrimaryText: "Save",
            SecondaryText: "Don't save"));

        return result?.Button switch
        {
            "primary" => UnsavedChoice.Save,
            "secondary" => UnsavedChoice.Discard,
            _ => UnsavedChoice.Cancel
        };
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText, bool danger = false)
    {
        var result = await ShowAsync(new DialogOptions(title, message, confirmText, Danger: danger));
        return result?.Button == "primary";
    }

    public async Task<string?> PromptAsync(string title, string message, string initialValue, string confirmText,
        Func<string, string?>? validate = null)
    {
        var result = await ShowAsync(new DialogOptions(
            title, message, confirmText, HasInput: true, InitialInput: initialValue, Validate: validate));
        return result?.Input;
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        if (!Host.StorageProvider.CanPickFolder)
            return null;
        var folders = await Host.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<IStorageFile?> OpenFileAsync(string title, string typeName, params string[] patterns)
    {
        if (!Host.StorageProvider.CanOpen)
            return null;
        var files = await Host.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(typeName) { Patterns = patterns }, FilePickerFileTypes.All]
        });
        return files.FirstOrDefault();
    }

    public async Task<IStorageFile?> SaveFileAsync(string title, string suggestedName, string extension, string typeName)
    {
        if (!Host.StorageProvider.CanSave)
            return null;
        return await Host.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension,
            ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType(typeName) { Patterns = ["*." + extension] }]
        });
    }

    public async Task CopyToClipboardAsync(string text)
    {
        if (Host.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

    public async Task CopyImageAsync(Avalonia.Media.Imaging.Bitmap bitmap)
    {
        if (Host.Clipboard is { } clipboard)
            await clipboard.SetBitmapAsync(bitmap);
    }

    public async Task<string?> GetClipboardTextAsync() =>
        Host.Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;

    public async Task OpenUrlAsync(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            await Host.Launcher.LaunchUriAsync(uri);
    }

    public void RevealInExplorer(string path)
    {
        if (OperatingSystem.IsBrowser())
            return;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var args = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{path}\"";
                Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
            }
            else
            {
                // Linux/macOS: open the containing folder in the default file manager.
                var dir = File.Exists(path) ? Path.GetDirectoryName(path)! : path;
                var opener = OperatingSystem.IsMacOS() ? "open" : "xdg-open";
                Process.Start(new ProcessStartInfo(opener) { ArgumentList = { dir }, UseShellExecute = false });
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // no file manager available
        }
    }
}
