using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Ps1Forge.Core;

public static class ArtworkProcessor
{
    public static string CreateIcon(string sourcePath, string stagingDirectory)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Artwork file was not found.", sourcePath);

        Directory.CreateDirectory(stagingDirectory);
        var output = Path.Combine(stagingDirectory, "icon0.png");

        using var source = Image.FromFile(sourcePath);
        using var canvas = new Bitmap(512, 512, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(canvas);

        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var scale = Math.Max(512d / source.Width, 512d / source.Height);
        var width = (int)Math.Ceiling(source.Width * scale);
        var height = (int)Math.Ceiling(source.Height * scale);
        var x = (512 - width) / 2;
        var y = (512 - height) / 2;

        graphics.DrawImage(source, x, y, width, height);
        canvas.Save(output, ImageFormat.Png);

        return output;
    }
}
