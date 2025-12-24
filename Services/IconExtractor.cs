using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SL_Cleaning.Services;

/// <summary>
/// Utility class for extracting icons from executables, DLLs, and ICO files.
/// </summary>
public static class IconExtractor
{
    private const int DesiredIconSize = 48;

    private static readonly ConcurrentDictionary<string, IconCacheEntry> _iconCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Lazy<ImageSource> _defaultIcon = new(CreateDefaultIcon, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Extracts an icon for the provided path or returns a cached/default icon when unavailable.
    /// </summary>
    public static ImageSource ExtractIcon(string? iconPath)
    {
        _ = TryGetIcon(iconPath, out var icon);
        return icon;
    }

    /// <summary>
    /// Attempts to load an icon for the provided path, returning false when the fallback icon is used.
    /// </summary>
    public static bool TryGetIcon(string? iconPath, out ImageSource icon)
    {
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            icon = _defaultIcon.Value;
            return false;
        }

        var request = ParseIconRequest(iconPath);
        if (string.IsNullOrWhiteSpace(request.Path))
        {
            icon = _defaultIcon.Value;
            return false;
        }

        string cacheKey = $"{request.Path}|{request.Index}";

        var entry = _iconCache.GetOrAdd(cacheKey, _ =>
        {
            var extracted = ExtractIconInternal(request.Path, request.Index);
            if (extracted != null)
            {
                return new IconCacheEntry(extracted, false);
            }

            return new IconCacheEntry(_defaultIcon.Value, true);
        });

        icon = entry.Icon;
        return !entry.IsFallback;
    }

    /// <summary>
    /// Loads a .ico file directly.
    /// </summary>
    private static ImageSource? ExtractIconInternal(string filePath, int iconIndex)
    {
        try
        {
            if (IsShellItemPath(filePath))
            {
                var shellIcon = ExtractFromShellItem(filePath);
                if (shellIcon != null)
                {
                    return shellIcon;
                }
            }

            bool fileExists = File.Exists(filePath);

            if (!fileExists)
            {
                return null;
            }

            var extension = Path.GetExtension(filePath);

            if (IsBitmapExtension(extension))
            {
                return LoadBitmapFile(filePath);
            }

            if (extension.Equals(".ico", StringComparison.OrdinalIgnoreCase))
            {
                return LoadIconFile(filePath);
            }

            var resourceIcon = ExtractIconFromExecutable(filePath, iconIndex);
            if (resourceIcon != null)
            {
                return resourceIcon;
            }

            if (IsShellShortcutCandidate(extension))
            {
                var shellIcon = ExtractFromShellItem(filePath);
                if (shellIcon != null)
                {
                    return shellIcon;
                }
            }

            return LoadAssociatedIcon(filePath);
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? LoadIconFile(string filePath)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = DesiredIconSize;
            bitmap.DecodePixelHeight = DesiredIconSize;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? LoadBitmapFile(string filePath)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = DesiredIconSize;
            bitmap.DecodePixelHeight = DesiredIconSize;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts an icon from an executable or DLL file.
    /// </summary>
    private static ImageSource? ExtractIconFromExecutable(string filePath, int iconIndex)
    {
        var iconHandles = new IntPtr[1];
        var iconIds = new uint[1];

        // Attempt to extract the specified icon index first
        uint extracted = PrivateExtractIcons(filePath, iconIndex, DesiredIconSize, DesiredIconSize, iconHandles, iconIds, 1, 0);

        if (extracted == 0 || iconHandles[0] == IntPtr.Zero)
        {
            // Fallback to first icon resource when specified index fails
            extracted = PrivateExtractIcons(filePath, 0, DesiredIconSize, DesiredIconSize, iconHandles, iconIds, 1, 0);
            if (extracted == 0 || iconHandles[0] == IntPtr.Zero)
            {
                return ExtractWithShell(filePath);
            }
        }

        return ConvertIconHandleToImageSource(iconHandles[0]);
    }

    /// <summary>
    /// Converts a GDI+ Bitmap to WPF ImageSource.
    /// </summary>
    private static ImageSource? ConvertBitmapToImageSource(Bitmap bitmap)
    {
        try
        {
            IntPtr hBitmap = bitmap.GetHbitmap();
            try
            {
                var imageSource = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(bitmap.Width, bitmap.Height));

                imageSource.Freeze();
                return imageSource;
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }
        catch
        {
            return null;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern uint PrivateExtractIcons(string lpszFile, int nIconIndex, int cxIcon, int cyIcon, IntPtr[] phicon, uint[] piconid, uint nIcons, uint flags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    private static IconRequest ParseIconRequest(string iconPath)
    {
        string trimmed = iconPath.Trim();

        if (trimmed.StartsWith("@"))
        {
            trimmed = trimmed[1..];
        }

        trimmed = trimmed.Trim('\"');
        trimmed = Environment.ExpandEnvironmentVariables(trimmed);

        if (IsShellItemPath(trimmed))
        {
            return new IconRequest(trimmed, 0);
        }

        if (File.Exists(trimmed))
        {
            return new IconRequest(trimmed, 0);
        }

        int lastComma = trimmed.LastIndexOf(',');
        if (lastComma > 0)
        {
            string pathPart = trimmed[..lastComma].Trim().Trim('\"');
            pathPart = Environment.ExpandEnvironmentVariables(pathPart);

            if (pathPart.StartsWith("@"))
            {
                pathPart = pathPart[1..];
            }

            if (IsShellItemPath(pathPart) || File.Exists(pathPart))
            {
                int iconIndex = 0;
                string indexPart = trimmed[(lastComma + 1)..];
                _ = int.TryParse(indexPart.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out iconIndex);
                return new IconRequest(pathPart, iconIndex);
            }
        }

        return new IconRequest(trimmed, 0);
    }

    private static ImageSource? ConvertIconHandleToImageSource(IntPtr iconHandle)
    {
        if (iconHandle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                iconHandle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            bitmapSource.Freeze();
            return bitmapSource;
        }
        finally
        {
            DestroyIcon(iconHandle);
        }
    }

    private static ImageSource? LoadAssociatedIcon(string filePath)
    {
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(filePath);
            if (icon == null)
            {
                return null;
            }

            using var bitmap = icon.ToBitmap();
            return ConvertBitmapToImageSource(bitmap);
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? ExtractWithShell(string filePath)
    {
        try
        {
            var info = new SHFILEINFO();
            IntPtr result = SHGetFileInfo(
                filePath,
                FILE_ATTRIBUTE_NORMAL,
                ref info,
                (uint)Marshal.SizeOf<SHFILEINFO>(),
                SHGFI_ICON | SHGFI_LARGEICON | SHGFI_USEFILEATTRIBUTES);

            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            {
                return null;
            }

            return ConvertIconHandleToImageSource(info.hIcon);
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource CreateDefaultIcon()
    {
        const int size = DesiredIconSize;
        const int dpi = 96;

        var backgroundBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 66, 99));
        backgroundBrush.Freeze();
        var accentBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(91, 163, 176));
        accentBrush.Freeze();

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var rect = new Rect(4, 4, size - 8, size - 8);
            context.DrawRoundedRectangle(backgroundBrush, new System.Windows.Media.Pen(accentBrush, 2), rect, 6, 6);

            var center = new System.Windows.Point(size / 2.0, size / 2.0);
            context.DrawEllipse(accentBrush, null, center, (size - 18) / 2.0, (size - 18) / 2.0);

            var innerBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(13, 20, 33));
            innerBrush.Freeze();
            context.DrawEllipse(innerBrush, null, center, (size - 30) / 2.0, (size - 30) / 2.0);
        }

        var bitmap = new RenderTargetBitmap(size, size, dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public static ImageSource GetDefaultIcon() => _defaultIcon.Value;

    private readonly record struct IconRequest(string Path, int Index);

    private readonly record struct IconCacheEntry(ImageSource Icon, bool IsFallback);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    private static bool IsShellItemPath(string path) => path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);

    private static bool IsBitmapExtension(string extension) => extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".gif", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".tif", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase);

    private static bool IsShellShortcutCandidate(string extension) => extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".appref-ms", StringComparison.OrdinalIgnoreCase);

    private static ImageSource? ExtractFromShellItem(string shellPath)
    {
        try
        {
            Guid factoryGuid = new("BCC18B79-BA16-442F-80C4-8A59C30C463B");
            SHCreateItemFromParsingName(shellPath, IntPtr.Zero, ref factoryGuid, out IShellItemImageFactory factory);
            try
            {
                var size = new SIZE { cx = DesiredIconSize, cy = DesiredIconSize };
                factory.GetImage(size, SIIGBF.SIIGBF_ICONONLY | SIIGBF.SIIGBF_SCALEUP, out IntPtr hBitmap);
                if (hBitmap == IntPtr.Zero)
                {
                    return null;
                }

                return CreateImageSourceFromHBitmap(hBitmap, DesiredIconSize, DesiredIconSize);
            }
            finally
            {
                if (factory is not null)
                {
                    _ = Marshal.ReleaseComObject(factory);
                }
            }
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? CreateImageSourceFromHBitmap(IntPtr hBitmap, int width, int height)
    {
        if (hBitmap == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var imageSource = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(width, height));
            imageSource.Freeze();
            return imageSource;
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [Flags]
    [SuppressMessage("Performance", "CA2217:Do not mark enums with FlagsAttribute")]
    private enum SIIGBF : uint
    {
        SIIGBF_RESIZETOFIT = 0x0,
        SIIGBF_BIGGERSIZEOK = 0x1,
        SIIGBF_MEMORYONLY = 0x2,
        SIIGBF_ICONONLY = 0x4,
        SIIGBF_THUMBNAILONLY = 0x8,
        SIIGBF_INCACHEONLY = 0x10,
        SIIGBF_CROPTOSQUARE = 0x20,
        SIIGBF_WIDETHUMBNAILS = 0x40,
        SIIGBF_ICONBACKGROUND = 0x80,
        SIIGBF_SCALEUP = 0x100
    }

    [ComImport]
    [Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }
}
