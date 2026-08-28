using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuietShelf.Converters;

namespace QuietShelf.Tests;

public sealed class CoverImageConverterTests
{
    [Fact]
    public void Convert_UsesBoundedDecodeWidthByDefault()
    {
        var path = Path.Combine(Path.GetTempPath(), "QuietShelf-Tests-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            var pixels = new byte[2048 * 4];
            var source = BitmapSource.Create(2048, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 2048 * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (var stream = File.Create(path))
            {
                encoder.Save(stream);
            }

            var image = Assert.IsAssignableFrom<BitmapSource>(new CoverImageConverter().Convert(
                path,
                typeof(ImageSource),
                null!,
                CultureInfo.InvariantCulture));

            Assert.Equal(CoverImageConverter.DefaultDecodePixelWidth, image.PixelWidth);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
