using System.Windows.Media;

namespace HWZ.TaskManager.Helpers;

public static class AccentPalette
{
    private static readonly (Color Start, Color End)[] Palette =
    [
        (Color.FromRgb(77, 141, 255), Color.FromRgb(56, 189, 248)),
        (Color.FromRgb(139, 92, 246), Color.FromRgb(192, 132, 252)),
        (Color.FromRgb(16, 185, 129), Color.FromRgb(52, 211, 153)),
        (Color.FromRgb(245, 158, 11), Color.FromRgb(251, 191, 36)),
        (Color.FromRgb(244, 63, 94), Color.FromRgb(251, 113, 133)),
        (Color.FromRgb(14, 165, 233), Color.FromRgb(34, 211, 238)),
        (Color.FromRgb(99, 102, 241), Color.FromRgb(129, 140, 248)),
        (Color.FromRgb(20, 184, 166), Color.FromRgb(45, 212, 191))
    ];

    private static readonly Dictionary<int, LinearGradientBrush> Cache = new();

    public static LinearGradientBrush Create(string key)
    {
        var hash = 0;
        foreach (var character in key)
        {
            hash = unchecked(hash * 31 + character);
        }

        var index = Math.Abs(hash % Palette.Length);
        if (Cache.TryGetValue(index, out var existing))
        {
            return existing;
        }

        var (start, end) = Palette[index];
        var brush = new LinearGradientBrush(start, end, new System.Windows.Point(0, 0), new System.Windows.Point(1, 1));
        brush.Freeze();
        Cache[index] = brush;
        return brush;
    }
}
