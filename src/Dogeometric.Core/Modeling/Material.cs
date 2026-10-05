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

    /// <summary>Materials › Edit › Colorize: the picture takes the colour's hue and saturation, keeping its own lightness.</summary>
    public bool Colorize { get; set; }
}

/// <summary>A tag (SketchUp's former "layer"): controls visibility of the entities that use it.</summary>
public sealed class Tag
{
    /// <summary>SketchUp's built-in default tag; entities without a tag use it.</summary>
    public const string UntaggedName = "Untagged";

    public string Name { get; set; } = "";
    public Rgba Color { get; set; } = new(200, 200, 200);
    public bool Visible { get; set; } = true;

    /// <summary>Tags › Dashes: how this tag's edges are drawn when the style shows dashes.</summary>
    public LineStyle Dashes { get; set; }
}

/// <summary>SketchUp's line styles for tags, in its order.</summary>
public enum LineStyle
{
    Solid, ShortDash, Dash, Dot, DashDot, DashDoubleDot, DashTripleDot,
    DoubleDashDot, DoubleDashDoubleDot, DoubleDashTripleDot, LongDashDash, LongDashDoubleDash,
}

public static class LineStyles
{
    public static readonly string[] Names =
    [
        "Solid Basic", "Short dash", "Dash", "Dot", "Dash dot", "Dash double-dot", "Dash triple-dot",
        "Double-dash dot", "Double-dash double-dot", "Double-dash triple-dot", "Long-dash dash", "Long-dash double-dash",
    ];

    public static string Name(LineStyle s) => Names[(int)s];

    public static LineStyle Parse(string? name) =>
        Array.FindIndex(Names, n => n.Equals(name, StringComparison.OrdinalIgnoreCase)) is var i and >= 0 ? (LineStyle)i : LineStyle.Solid;

    /// <summary>Alternating on and off lengths in screen pixels, as SketchUp 2021 draws them at 1 px wide; empty when solid.</summary>
    public static int[] Pattern(LineStyle s) => s switch
    {
        LineStyle.ShortDash => [6, 6],
        LineStyle.Dash => [12, 6],
        LineStyle.Dot => [1, 6],
        LineStyle.DashDot => [12, 6, 1, 6],
        LineStyle.DashDoubleDot => [12, 6, 1, 6, 1, 6],
        LineStyle.DashTripleDot => [12, 6, 1, 6, 1, 6, 1, 6],
        LineStyle.DoubleDashDot => [12, 6, 12, 6, 1, 6],
        LineStyle.DoubleDashDoubleDot => [12, 6, 12, 6, 1, 6, 1, 6],
        LineStyle.DoubleDashTripleDot => [12, 6, 12, 6, 1, 6, 1, 6, 1, 6],
        LineStyle.LongDashDash => [36, 10, 12, 10],
        LineStyle.LongDashDoubleDash => [36, 10, 12, 10, 12, 10],
        _ => [],
    };
}
