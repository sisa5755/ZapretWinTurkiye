using System;
using System.Windows;

namespace ZapretGuiWpf.Helpers;

public enum AppTheme
{
    Light,
    Dark,
}

/// <summary>
/// Swaps the single merged ResourceDictionary in Application.Resources
/// between Themes/Light.xaml and Themes/Dark.xaml. Every brush the UI uses
/// is referenced via {DynamicResource ...}, so this one call re-colors the
/// whole window (and any control created afterwards) instantly - no need to
/// touch each control by hand.
/// </summary>
public static class ThemeManager
{
    public static AppTheme Current { get; private set; } = AppTheme.Light;

    public static void Apply(AppTheme theme)
    {
        Current = theme;
        string path = theme == AppTheme.Dark ? "Themes/Dark.xaml" : "Themes/Light.xaml";

        var dict = new ResourceDictionary { Source = new Uri(path, UriKind.Relative) };
        var resources = Application.Current.Resources;
        resources.MergedDictionaries.Clear();
        resources.MergedDictionaries.Add(dict);
    }

    public static void Toggle() => Apply(Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);
}
