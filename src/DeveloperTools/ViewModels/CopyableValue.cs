using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DeveloperTools.ViewModels;

/// <summary>A generated or converted value with a copy button that briefly shows a check mark.</summary>
public partial class CopyableValue(string value, Func<string, Task> copy, string? label = null, string? note = null, string? display = null)
    : ObservableObject
{
    public string Value { get; } = value;

    /// <summary>What is shown (may be shortened for very long values); Copy always copies <see cref="Value"/>.</summary>
    public string Display { get; } = display ?? value;
    public string? Label { get; } = label;
    public string? Note { get; } = note;
    public bool HasLabel => !string.IsNullOrEmpty(Label);
    public bool HasNote => !string.IsNullOrEmpty(Note);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotCopied))]
    private bool _isCopied;

    public bool IsNotCopied => !IsCopied;

    [RelayCommand]
    private async Task Copy()
    {
        await copy(Value);
        IsCopied = true;
        await Task.Delay(1400);
        IsCopied = false;
    }
}
