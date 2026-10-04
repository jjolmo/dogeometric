using Godot;

namespace Dogeometric.App.Viewport;

/// <summary>Decodes texture pictures (PNG, JPEG, BMP, WebP) into Godot images by their bytes, not their names.</summary>
public static class TextureImages
{
    public static Image? Decode(byte[] data)
    {
        if (data.Length == 0)
            return null;
        var image = new Image();
        var err = data switch
        {
            [0x89, 0x50, ..] => image.LoadPngFromBuffer(data),
            [0xFF, 0xD8, ..] => image.LoadJpgFromBuffer(data),
            [(byte)'B', (byte)'M', ..] => image.LoadBmpFromBuffer(data),
            [(byte)'R', (byte)'I', (byte)'F', (byte)'F', ..] => image.LoadWebpFromBuffer(data),
            _ => Error.FileUnrecognized,
        };
        return err == Error.Ok ? image : null;
    }
}
