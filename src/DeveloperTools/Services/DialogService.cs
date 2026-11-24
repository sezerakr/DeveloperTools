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

public sealed class DialogService(Window owner) : IDialogService
{
    public async Task<UnsavedChoice> ConfirmUnsavedAsync(string fileName)
    {
        var result = await DialogWindow.ShowAsync(owner, new DialogOptions(
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
        var result = await DialogWindow.ShowAsync(owner, new DialogOptions(title, message, confirmText, Danger: danger));
        return result?.Button == "primary";
    }

    public async Task<string?> PromptAsync(string title, string message, string initialValue, string confirmText,
        Func<string, string?>? validate = null)
    {
        var result = await DialogWindow.ShowAsync(owner, new DialogOptions(
            title, message, confirmText, HasInput: true, InitialInput: initialValue, Validate: validate));
        return result?.Input;
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<string?> PickOpenFileAsync(string title, string typeName, params string[] patterns)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(typeName) { Patterns = patterns }, FilePickerFileTypes.All]
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<string?> PickSaveFileAsync(string title, string suggestedName, string extension, string typeName)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension,
            ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType(typeName) { Patterns = ["*." + extension] }]
        });
        return file?.TryGetLocalPath();
    }

    public async Task CopyToClipboardAsync(string text)
    {
        if (owner.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

    public async Task CopyImageAsync(Avalonia.Media.Imaging.Bitmap bitmap)
    {
        if (owner.Clipboard is { } clipboard)
            await clipboard.SetBitmapAsync(bitmap);
    }

    public async Task<string?> GetClipboardTextAsync() =>
        owner.Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;

    public async Task OpenUrlAsync(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            await owner.Launcher.LaunchUriAsync(uri);
    }

    public void RevealInExplorer(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            var args = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{path}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
        }
        else
        {
            var dir = File.Exists(path) ? Path.GetDirectoryName(path)! : path;
            _ = owner.Launcher.LaunchUriAsync(new Uri(dir));
        }
    }
}
