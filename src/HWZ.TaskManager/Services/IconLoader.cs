using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HWZ.TaskManager.Services;

public static class IconLoader
{
    private const uint ShgfiUseFileAttributes = 0x000000010;
    private const uint ShgfiLargeIcon = 0x000000000;

    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }

        if (Cache.TryGetValue(fullPath, out var existing))
        {
            return existing;
        }

        ImageSource? image = null;
        try
        {
            if (File.Exists(fullPath))
            {
                var info = new NativeMethods.ShFileInfo
                {
                    DisplayName = string.Empty,
                    TypeName = string.Empty
                };

                var result = NativeMethods.SHGetFileInfo(
                    fullPath,
                    0,
                    ref info,
                    (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.ShFileInfo>(),
                    NativeMethods.ShgfiIcon | ShgfiLargeIcon);

                if (result != IntPtr.Zero && info.Icon != IntPtr.Zero)
                {
                    try
                    {
                        var source = Imaging.CreateBitmapSourceFromHIcon(
                            info.Icon,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromWidthAndHeight(32, 32));
                        source.Freeze();
                        image = source;
                    }
                    finally
                    {
                        _ = NativeMethods.DestroyIcon(info.Icon);
                    }
                }
            }
        }
        catch
        {
            image = null;
        }

        Cache[fullPath] = image;
        return image;
    }
}
