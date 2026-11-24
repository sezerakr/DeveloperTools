using CommunityToolkit.Mvvm.ComponentModel;

namespace DeveloperTools.ViewModels;

public abstract class PageViewModel : ObservableObject
{
    public abstract string Title { get; }
    public abstract string Subtitle { get; }

    /// <summary>The page became the visible one (start timers, refresh).</summary>
    public virtual void OnShown() { }

    /// <summary>Another page is now visible (stop background work).</summary>
    public virtual void OnHidden() { }
}
