using Dogeometric.Core.Units;
using Dogeometric.Core.View;

namespace Dogeometric.Core.Modeling;

/// <summary>A saved scene (SketchUp's scene tabs).</summary>
public sealed class Scene
{
    public string Name { get; set; } = "";
    public CameraState? Camera { get; set; }
    public HashSet<string> HiddenTags { get; } = [];
}

/// <summary>A whole document: top-level entities plus the definitions, materials and tags they use.</summary>
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
