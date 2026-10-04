namespace Dogeometric.Core.Modeling;

public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)
{
    public static readonly Rgba White = new(255, 255, 255);
    public static readonly Rgba DefaultFront = new(255, 255, 255);
    public static readonly Rgba DefaultBack = new(164, 178, 187);
}

/// <summary>Image data of a textured material. Width/height are the texture's real-world size in mm.</summary>
public sealed class TextureImage
{
    public string FileName { get; set; } = "";
    public byte[] Data { get; set; } = [];
    public double WidthMm { get; set; }
    public double HeightMm { get; set; }
}

public sealed class Material
{
    public string Name { get; set; } = "";
    public Rgba Color { get; set; } = Rgba.White;

    /// <summary>1 = opaque, 0 = fully transparent.</summary>
    public double Opacity { get; set; } = 1;

    public TextureImage? Texture { get; set; }
}

/// <summary>A tag (SketchUp's former "layer"): controls visibility of the entities that use it.</summary>
public sealed class Tag
{
    /// <summary>SketchUp's built-in default tag; entities without a tag use it.</summary>
    public const string UntaggedName = "Untagged";

    public string Name { get; set; } = "";
    public Rgba Color { get; set; } = new(200, 200, 200);
    public bool Visible { get; set; } = true;
}
