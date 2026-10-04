using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

/// <summary>
/// Edit › Cut/Copy/Paste. The clipboard holds a copy of the selection in world coordinates; nested groups and
/// components keep pointing at their definitions, which a paste into another model adopts.
/// </summary>
public static class Clipboard
{
    private static Entities? _content;

    public static bool IsEmpty => _content == null || All(_content).Count == 0;

    public static void Copy(Document doc)
    {
        if (doc.Selection.IsEmpty)
            return;
        var clip = new Entities();
        Transforming.Copy(clip, doc.Selection.Items.ToList(), doc.Context.ToWorld);
        _content = clip;
    }

    public static void Cut(Document doc)
    {
        Copy(doc);
        doc.EraseSelection();
    }

    /// <summary>World bounds of what's on the clipboard.</summary>
    public static Bounds3 Bounds => _content?.Bounds() ?? Bounds3.Empty;

    /// <summary>Edges of the clipboard content in world coordinates, for the paste preview.</summary>
    public static IEnumerable<(Vec3 A, Vec3 B)> PreviewLines()
    {
        if (_content == null)
            yield break;
        foreach (var e in _content.Edges)
            yield return (e.Start.Position, e.End.Position);
        foreach (var inst in _content.Instances)
        {
            var b = inst.Definition.Entities.Bounds();
            if (b.IsEmpty)
                continue;
            Vec3 C(int i) => inst.Transform.ApplyPoint(new Vec3((i & 1) == 0 ? b.Min.X : b.Max.X, (i & 2) == 0 ? b.Min.Y : b.Max.Y, (i & 4) == 0 ? b.Min.Z : b.Max.Z));
            foreach (var (a, c) in new[] { (0, 1), (2, 3), (4, 5), (6, 7), (0, 2), (1, 3), (4, 6), (5, 7), (0, 4), (1, 5), (2, 6), (3, 7) })
                yield return (C(a), C(c));
        }
    }

    /// <summary>
    /// Pastes into the active context, moved by <paramref name="offset"/> (world); zero is Paste In Place. The
    /// pasted entities become the selection.
    /// </summary>
    public static void Paste(Document doc, Vec3 offset)
    {
        if (_content is not { } clip || IsEmpty)
            return;
        Adopt(doc.Model, clip);
        var toLocal = Transform.Translation(offset).Then(doc.Context.ToWorld.Inverse());
        List<object> pasted = [];
        doc.Operation("Paste", e => pasted = Transforming.Copy(e, All(clip), toLocal));
        doc.Selection.Set(pasted);
    }

    private static List<object> All(Entities e) =>
        [.. e.Faces, .. e.Edges, .. e.Instances, .. e.Dimensions, .. e.Texts, .. e.GuideLines, .. e.GuidePoints];

    /// <summary>Definitions, materials and tags the clipboard uses join the target model when it lacks them.</summary>
    private static void Adopt(Model model, Entities clip)
    {
        var seen = new HashSet<ComponentDefinition>();
        void Visit(Entities e)
        {
            foreach (var f in e.Faces)
            {
                AddMaterial(f.FrontMaterial);
                AddMaterial(f.BackMaterial);
                AddTag(f.Tag);
            }
            foreach (var edge in e.Edges)
                AddTag(edge.Tag);
            foreach (var i in e.Instances)
            {
                AddMaterial(i.Material);
                AddTag(i.Tag);
                if (!seen.Add(i.Definition))
                    continue;
                if (!model.Definitions.Contains(i.Definition))
                    model.Definitions.Add(i.Definition);
                Visit(i.Definition.Entities);
            }
        }
        void AddMaterial(Material? m)
        {
            if (m != null && !model.Materials.Contains(m))
                model.Materials.Add(m);
        }
        void AddTag(Tag? t)
        {
            if (t != null && !model.Tags.Contains(t))
                model.Tags.Add(t);
        }
        Visit(clip);
    }
}
