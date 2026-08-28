using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QuietShelf.Data;

internal static class CoverImageProcessor
{
    internal const int MaxPixelLength = 1600;
    internal const int JpegQuality = 88;

    public static Task SaveOptimizedJpegAsync(string sourcePath, string destinationPath) =>
        Task.Run(() => SaveOptimizedJpeg(sourcePath, destinationPath));

    private static void SaveOptimizedJpeg(string sourcePath, string destinationPath)
    {
        try
        {
            ValidateSignature(sourcePath);
            var dimensions = ReadDimensions(sourcePath);
            var decoded = DecodeBounded(sourcePath, dimensions.Width, dimensions.Height);
            var flattened = FlattenAgainstWhite(decoded);

            var encoder = new JpegBitmapEncoder { QualityLevel = JpegQuality };
            encoder.Frames.Add(BitmapFrame.Create(flattened));
            using var output = File.Create(destinationPath);
            encoder.Save(output);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new InvalidOperationException($"无法读取图片“{Path.GetFileName(sourcePath)}”。", exception);
        }
    }

    private static (int Width, int Height) ReadDimensions(string path)
    {
        using var input = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(
            input,
            BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.DelayCreation,
            BitmapCacheOption.None);
        var frame = decoder.Frames[0];
        if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0)
        {
            throw new InvalidOperationException($"无法读取图片“{Path.GetFileName(path)}”。");
        }
        return (frame.PixelWidth, frame.PixelHeight);
    }

    private static BitmapSource DecodeBounded(string path, int width, int height)
    {
        using var input = File.OpenRead(path);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        image.StreamSource = input;
        if (Math.Max(width, height) > MaxPixelLength)
        {
            if (width >= height)
            {
                image.DecodePixelWidth = MaxPixelLength;
            }
            else
            {
                image.DecodePixelHeight = MaxPixelLength;
            }
        }
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static BitmapSource FlattenAgainstWhite(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var sourceStride = converted.PixelWidth * 4;
        var sourcePixels = new byte[sourceStride * converted.PixelHeight];
        converted.CopyPixels(sourcePixels, sourceStride, 0);

        var destinationStride = converted.PixelWidth * 3;
        var destinationPixels = new byte[destinationStride * converted.PixelHeight];
        for (var sourceIndex = 0; sourceIndex < sourcePixels.Length; sourceIndex += 4)
        {
            var destinationIndex = sourceIndex / 4 * 3;
            var alpha = sourcePixels[sourceIndex + 3];
            destinationPixels[destinationIndex] = BlendWithWhite(sourcePixels[sourceIndex], alpha);
            destinationPixels[destinationIndex + 1] = BlendWithWhite(sourcePixels[sourceIndex + 1], alpha);
            destinationPixels[destinationIndex + 2] = BlendWithWhite(sourcePixels[sourceIndex + 2], alpha);
        }

        var flattened = BitmapSource.Create(
            converted.PixelWidth,
            converted.PixelHeight,
            source.DpiX,
            source.DpiY,
            PixelFormats.Bgr24,
            null,
            destinationPixels,
            destinationStride);
        flattened.Freeze();
        return flattened;
    }

    private static byte BlendWithWhite(byte color, byte alpha) =>
        (byte)((color * alpha + 255 * (255 - alpha) + 127) / 255);

    private static void ValidateSignature(string path)
    {
        Span<byte> signature = stackalloc byte[8];
        using var input = File.OpenRead(path);
        if (input.Read(signature) < signature.Length)
        {
            throw new InvalidOperationException($"无法读取图片“{Path.GetFileName(path)}”。");
        }

        var isJpeg = signature[0] == 0xFF && signature[1] == 0xD8 && signature[2] == 0xFF;
        var isPng = signature.SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var isBitmap = signature[0] == 0x42 && signature[1] == 0x4D;
        if (!isJpeg && !isPng && !isBitmap)
        {
            throw new InvalidOperationException($"封面仅支持 JPG、PNG 或 BMP 图片：{Path.GetFileName(path)}");
        }
    }
}
