using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace YierPet;

internal static class BitmapUtil
{
    public static BitmapSource ToBitmapSource(Image<Rgba32> image)
    {
        var wb = new WriteableBitmap(
            image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null);
        image.ProcessPixelRows(accessor =>
        {
            wb.Lock();
            try
            {
                var back = wb.BackBuffer;
                for (var y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        var p = row[x];
                        var offset = (y * wb.BackBufferStride) + (x * 4);
                        unsafe
                        {
                            var ptr = (byte*)back + offset;
                            ptr[0] = p.B;
                            ptr[1] = p.G;
                            ptr[2] = p.R;
                            ptr[3] = p.A;
                        }
                    }
                }
                wb.AddDirtyRect(new Int32Rect(0, 0, image.Width, image.Height));
            }
            finally
            {
                wb.Unlock();
            }
        });
        wb.Freeze();
        return wb;
    }

    public static Image<Rgba32> LoadImage(string path)
    {
        using var stream = File.OpenRead(path);
        return Image.Load<Rgba32>(stream);
    }
}
