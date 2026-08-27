using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QuietShelf.Converters;

public sealed class CoverImageConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<ImageCacheKey, WeakReference<ImageSource>> Cache = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var decodePixelWidth = parameter switch
            {
                int width when width > 0 => width,
                string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) && width > 0 => width,
                _ => 0
            };
            var key = new ImageCacheKey(Path.GetFullPath(path), decodePixelWidth);
            if (Cache.TryGetValue(key, out var reference) && reference.TryGetTarget(out var cached))
            {
                return cached;
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            if (decodePixelWidth > 0)
            {
                image.DecodePixelWidth = decodePixelWidth;
            }
            image.UriSource = new Uri(key.Path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();

            Cache[key] = new WeakReference<ImageSource>(image);
            RemoveExpiredEntries();
            return image;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static void RemoveExpiredEntries()
    {
        if (Cache.Count <= 256)
        {
            return;
        }

        foreach (var entry in Cache)
        {
            if (!entry.Value.TryGetTarget(out _))
            {
                Cache.TryRemove(entry.Key, out _);
            }
        }
    }

    private readonly record struct ImageCacheKey(string Path, int DecodePixelWidth);
}
