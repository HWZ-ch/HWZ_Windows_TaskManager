using System.Windows;
using Microsoft.Win32;

namespace HWZ.TaskManager.Helpers;

public static class ThemeManager
{
    public static event Action<bool>? ThemeChanged;

    public static bool IsDark { get; private set; } = true;

    public static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int intValue ? intValue == 0 : true;
        }
        catch
        {
            return true;
        }
    }

    public static bool ResolveIsDark(string? mode)
    {
        return mode switch
        {
            "Light" => false,
            "System" => SystemPrefersDark(),
            _ => true
        };
    }

    public static void Apply(string? mode)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        var dark = ResolveIsDark(mode);
        var source = new Uri(
            dark ? "Themes/Dark.xaml" : "Themes/Light.xaml",
            UriKind.Relative);
        var dictionary = new ResourceDictionary { Source = source };

        var dictionaries = app.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(dictionary =>
            dictionary.Source is not null &&
            (dictionary.Source.OriginalString.EndsWith("Dark.xaml", StringComparison.OrdinalIgnoreCase) ||
             dictionary.Source.OriginalString.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase)));

        if (existing is not null)
        {
            var index = dictionaries.IndexOf(existing);
            dictionaries[index] = dictionary;
        }
        else
        {
            dictionaries.Insert(0, dictionary);
        }

        IsDark = dark;
        ThemeChanged?.Invoke(dark);
    }
}
