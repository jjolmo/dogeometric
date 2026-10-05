using Dogeometric.Core.Units;
using Dogeometric.Core.View;

namespace Dogeometric.Core.Modeling;

/// <summary>A saved scene (SketchUp's scene tabs).</summary>
public sealed class Scene
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>Scenes panel › Include in animation: off, playing and exporting skip the scene.</summary>
    public bool InAnimation { get; set; } = true;
    public CameraState? Camera { get; set; }
    public HashSet<string> HiddenTags { get; } = [];

    /// <summary>The photo this scene's camera was matched to (Camera › Match New Photo).</summary>
    public MatchedPhoto? Photo { get; set; }
}

/// <summary>A whole document: top-level entities plus the definitions, materials and tags they use.</summary>
/// <summary>Model Info's Components, Credits and Rendering settings.</summary>
public sealed record ModelOptions
{
    /// <summary>Credits › Model author.</summary>
    public string Author { get; init; } = "";

    /// <summary>Components › how much the rest of the model and other copies of the edited component fade (0 to 1).</summary>
    public double FadeRest { get; init; } = 0.6;
    public double FadeSimilar { get; init; } = 0.6;

    /// <summary>Components › Show component axes.</summary>
    public bool ShowComponentAxes { get; init; }

    /// <summary>Rendering › Use anti-aliased textures.</summary>
    public bool SmoothTextures { get; init; } = true;

    /// <summary>Fog panel: where fog starts and is full, from 0 (the model's near side) to 1 (well past its far
    /// side), and its colour when it isn't the background's.</summary>
    public double FogStart { get; init; }
    public double FogEnd { get; init; } = 1;
    public Rgba? FogColor { get; init; }

    /// <summary>Model Info › Units › Angle Units: decimals shown, and whether angles snap and every how many degrees.</summary>
    public int AnglePrecision { get; init; } = 1;
    public bool AngleSnapping { get; init; } = true;
    public double AngleSnap { get; init; } = 15;

    /// <summary>Model Info › Units › Length snapping: lengths drawn by dragging round to this step when enabled.</summary>
    public bool LengthSnapping { get; init; }
    public double LengthSnap { get; init; } = 1;

    /// <summary>Tags panel › Color by tag: faces show their tag's colour (untagged ones their group's).</summary>
    public bool ColorByTag { get; init; }
}

/// <summary>SketchUp's dimension endpoint styles.</summary>
public enum DimensionEndpoint { None, Slash, Dot, ClosedArrow, OpenArrow }

public sealed class Model
{
    public Entities Entities { get; } = new();
    public List<ComponentDefinition> Definitions { get; } = [];
    public List<Material> Materials { get; } = [];
    public List<Tag> Tags { get; } = [new() { Name = Tag.UntaggedName }];
    public List<Scene> Scenes { get; } = [];
    public LengthUnit Units { get; set; } = LengthUnit.Millimeters;
    public int UnitPrecision { get; set; } = 1;
    public ShadowSettings Shadows { get; set; } = new();

    /// <summary>Model Info › Dimensions and Text: text sizes (points) and how dimension lines end.</summary>
    public int DimensionFontSize { get; set; } = 12;
    public DimensionEndpoint DimensionEndpoints { get; set; } = DimensionEndpoint.ClosedArrow;
    public int TextFontSize { get; set; } = 12;

    /// <summary>Model Info › Animation: scene transitions (and their length in seconds) and the pause on each scene.</summary>
    public bool SceneTransitions { get; set; } = true;
    public double SceneTransitionSeconds { get; set; } = 2;
    public double SceneDelaySeconds { get; set; }

    /// <summary>View › Section Fill: the active section's cut is filled.</summary>
    public bool ShowSectionFill { get; set; } = true;

    public ModelOptions Options { get; set; } = new();

    /// <summary>
    /// SketchUp's drawing axes (Axes tool): origin and red/green/blue directions, orthonormal. Inference, arrow-key
    /// locks and the ground plane follow them.
    /// </summary>
    public Geometry.Transform Axes { get; set; } = Geometry.Transform.Identity;

    /// <summary>Text of the document's source, for diagnostics (e.g. "SketchUp 21.1.332").</summary>
    public string SourceVersion { get; set; } = "";

    /// <summary>Every entity collection: the top level and each definition.</summary>
    public IEnumerable<Entities> AllEntities => Definitions.Select(d => d.Entities).Prepend(Entities);

    public Tag UntaggedTag => Tags[0];

    public Tag GetOrAddTag(string name)
    {
        if (string.IsNullOrEmpty(name) || name is "Layer0" or Tag.UntaggedName)
            return UntaggedTag;
        var tag = Tags.FirstOrDefault(t => t.Name == name);
        if (tag == null)
        {
            tag = new Tag { Name = name };
            Tags.Add(tag);
        }
        return tag;
    }
}
