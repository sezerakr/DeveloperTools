using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using DeveloperTools.ViewModels;

namespace DeveloperTools.Views;

/// <summary>
/// Shows the current page and keeps views of pages already visited, so switching back is instant
/// (no re-creating editors, re-parsing XAML or re-rendering previews). Hidden pages are not measured or rendered.
/// </summary>
public sealed class PageHost : Panel
{
    public static readonly StyledProperty<PageViewModel?> PageProperty =
        AvaloniaProperty.Register<PageHost, PageViewModel?>(nameof(Page));

    private readonly Dictionary<PageViewModel, Control> _views = new();

    public PageViewModel? Page
    {
        get => GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PageProperty)
            Show(change.GetNewValue<PageViewModel?>());
    }

    private void Show(PageViewModel? page)
    {
        if (page is not null && !_views.ContainsKey(page))
        {
            var view = Create(page);
            view.DataContext = page;
            _views[page] = view;
            Children.Add(view);
        }

        foreach (var (vm, view) in _views)
            view.IsVisible = ReferenceEquals(vm, page);
    }

    private static Control Create(PageViewModel page) => page switch
    {
        HomePageViewModel => new HomePage(),
        MarkdownPageViewModel => new MarkdownPage(),
        JsonPageViewModel => new JsonPage(),
        DiffPageViewModel => new DiffPage(),
        RegexPageViewModel => new RegexPage(),
        TimestampPageViewModel => new TimestampPage(),
        TextPageViewModel => new TextPage(),
        GeneratorsPageViewModel => new GeneratorsPage(),
        EncodePageViewModel => new EncodePage(),
        SettingsPageViewModel => new SettingsPage(),
        _ => throw new InvalidOperationException($"No view for {page.GetType().Name}")
    };
}
