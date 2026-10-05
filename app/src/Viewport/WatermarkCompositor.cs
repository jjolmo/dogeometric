using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.Viewport;

/// <summary>
/// Lays the style's watermarks out on two view-sized pictures, as SketchUp draws them: the background ones (behind
/// the model, shown by sky.gdshader) and the overlays (over it).
/// </summary>
public static class WatermarkCompositor
{
    private static readonly Dictionary<(TextureImage, double, bool, Rgba), Image> Prepared = [];

    public static (Image? Back, Image? Front) Compose(StyleSettings style, Vector2I size)
    {
        if (!style.ShowWatermarks || size.X < 1 || size.Y < 1)
            return (null, null);
        Image? back = null, front = null;
        foreach (var mark in style.Watermarks.Where(m => m.Visible))
        {
            if (Prepare(mark, style.BackgroundColor) is not { } picture)
                continue;
            ref var target = ref mark.Overlay ? ref front : ref back;
            target ??= Image.CreateEmpty(size.X, size.Y, false, Image.Format.Rgba8);
            Place(target, picture, mark, size);
        }
        return (back, front);
    }

    /// <summary>The picture with the watermark's opacity in its alpha (or its brightness, as a mask).</summary>
    private static Image? Prepare(Watermark mark, Rgba background)
    {
        var key = (mark.Image, mark.Opacity, mark.Mask, background);
        if (Prepared.TryGetValue(key, out var cached))
            return cached;
        if (TextureImages.Decode(mark.Image.Data) is not { } image)
            return null;
        image.Convert(Image.Format.Rgba8);
        var bg = Color.Color8(background.R, background.G, background.B);
        for (var y = 0; y < image.GetHeight(); y++)
            for (var x = 0; x < image.GetWidth(); x++)
            {
                var c = image.GetPixel(x, y);
                image.SetPixel(x, y, mark.Mask
                    ? new Color(bg, c.Luminance * c.A * (float)mark.Opacity)
                    : new Color(c, c.A * (float)mark.Opacity));
            }
        return Prepared[key] = image;
    }

    private static void Place(Image target, Image picture, Watermark mark, Vector2I view)
    {
        var size = new Vector2I(picture.GetWidth(), picture.GetHeight());
        switch (mark.Layout)
        {
            case WatermarkLayout.Stretched:
            {
                var fit = view;
                // A locked aspect ratio fills the view's width, as SketchUp does.
                if (mark.LockAspect)
                    fit = new Vector2I(view.X, Math.Max(1, view.X * size.Y / Math.Max(size.X, 1)));
                Blend(target, Resized(picture, fit), new Vector2I(0, (view.Y - fit.Y) / 2));
                break;
            }
            case WatermarkLayout.Tiled:
            {
                var tile = Resized(picture, Scaled(size, mark.Scale));
                for (var y = 0; y < view.Y; y += tile.GetHeight())
                    for (var x = 0; x < view.X; x += tile.GetWidth())
                        Blend(target, tile, new Vector2I(x, y));
                break;
            }
            default:
            {
                // SketchUp scales a positioned picture to a share of the view's width.
                var width = mark.Scale * view.X;
                var s = new Vector2I((int)Math.Round(width), (int)Math.Round(width * size.Y / Math.Max(size.X, 1)));
                if (s.X < 1 || s.Y < 1)
                    break;
                var column = (int)mark.Position % 3;
                var row = (int)mark.Position / 3;
                Blend(target, Resized(picture, s), new Vector2I(column * (view.X - s.X) / 2, row * (view.Y - s.Y) / 2));
                break;
            }
        }
    }

    private static Vector2I Scaled(Vector2I size, double scale) =>
        new(Math.Max(1, (int)Math.Round(size.X * scale)), Math.Max(1, (int)Math.Round(size.Y * scale)));

    private static Image Resized(Image picture, Vector2I size)
    {
        if (size.X == picture.GetWidth() && size.Y == picture.GetHeight())
            return picture;
        var copy = (Image)picture.Duplicate();
        copy.Resize(size.X, size.Y, Image.Interpolation.Bilinear);
        return copy;
    }

    private static void Blend(Image target, Image picture, Vector2I at) =>
        target.BlendRect(picture, new Rect2I(0, 0, picture.GetWidth(), picture.GetHeight()), at);
}
