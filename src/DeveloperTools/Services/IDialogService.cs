using System;
using System.Threading.Tasks;

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

    Task<string?> PickFolderAsync(string title);

    Task<string?> PickOpenFileAsync(string title, string typeName, params string[] patterns);

    Task<string?> PickSaveFileAsync(string title, string suggestedName, string extension, string typeName);

    Task CopyToClipboardAsync(string text);

    Task<string?> GetClipboardTextAsync();

    Task CopyImageAsync(Avalonia.Media.Imaging.Bitmap bitmap);

    Task OpenUrlAsync(string url);

    void RevealInExplorer(string path);
}
