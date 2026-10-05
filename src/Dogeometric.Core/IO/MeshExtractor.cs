using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.IO;

/// <summary>A triangle in model (world) space, wound counter-clockwise seen from its front side; textured, it carries
/// its corners' texture coordinates in images (1 = one copy of the picture).</summary>
public readonly record struct Triangle(Vec3 A, Vec3 B, Vec3 C, Material? Material, TriangleUv? Uv = null)
{
    public Vec3 Normal => (B - A).Cross(C - A).Normalized();
}

public readonly record struct TriangleUv((double U, double V) A, (double U, double V) B, (double U, double V) C);

public sealed class ExportOptions
{
    /// <summary>
    /// Top-level entities to export (faces and instances of <see cref="Model.Entities"/>); null exports everything,
    /// like SketchUp's "Export selection only" unchecked.
    /// </summary>
    public IReadOnlySet<object>? Selection { get; init; }

    /// <summary>Hidden entities and entities on hidden tags are skipped unless this is set.</summary>
    public bool IncludeHidden { get; init; }

    /// <summary>Also emit the back side of every face (two-sided output for renderers that cull back faces).</summary>
    public bool DoubleSided { get; init; }

    /// <summary>
    /// Entity collection the <see cref="Selection"/> belongs to (the group being edited) and its world transform;
    /// null means the model's top level.
    /// </summary>
    public Entities? SelectionContext { get; init; }

    public Transform SelectionContextTransform { get; init; } = Transform.Identity;
}

/// <summary>Flattens a model's faces into world-space triangles for mesh exporters (STL, OBJ, glTF, DAE).</summary>
public static class MeshExtractor
{
    public static List<Triangle> Extract(Model model, ExportOptions? options = null)
    {
        options ??= new ExportOptions();
        var result = new List<Triangle>();
        if (options.SelectionContext is { } ctx)
            Walk(ctx, options.SelectionContextTransform, null, options, result, topLevel: true);
        else
            Walk(model.Entities, Transform.Identity, null, options, result, topLevel: true);
        return result;
    }

    /// <summary>Triangles of one group/component, in the coordinates of the collection that holds it.</summary>
    public static List<Triangle> ExtractInstance(ComponentInstance instance, bool includeHidden = true)
    {
        var result = new List<Triangle>();
        Walk(instance.Definition.Entities, instance.Transform, instance.Material, new ExportOptions { IncludeHidden = includeHidden }, result, topLevel: false);
        return result;
    }

    private static void Walk(Entities entities, Transform xf, Material? inherited, ExportOptions o, List<Triangle> output, bool topLevel)
    {
        var mirrored = xf.IsMirroring;
        // SUbD: a subdivided group's geometry is its smooth surface, not its control mesh.
        var subdivided = entities.Subdivision > 0 && !topLevel;
        if (subdivided)
            AddSubdivided(entities, xf, mirrored, inherited, o, output);
        foreach (var face in subdivided ? [] : entities.Faces)
        {
            if (topLevel && o.Selection != null && !o.Selection.Contains(face))
                continue;
            if (!o.IncludeHidden && (face.Hidden || face.Tag is { Visible: false }))
                continue;
            AddFace(face, xf, mirrored, inherited, o.DoubleSided, output);
        }

        foreach (var inst in entities.Instances)
        {
            if (topLevel && o.Selection != null && !o.Selection.Contains(inst))
                continue;
            if (!o.IncludeHidden && (inst.Hidden || inst.Tag is { Visible: false }))
                continue;
            // SketchUp paints default-material faces inside a group/component with the instance's material.
            Walk(inst.Definition.Entities, inst.Transform.Then(xf), inst.Material ?? inherited, o, output, topLevel: false);
        }
    }

    private static void AddSubdivided(Entities entities, Transform xf, bool mirrored, Material? inherited, ExportOptions o, List<Triangle> output)
    {
        var (points, polygons, _) = CatmullClark.Mesh(entities, entities.Subdivision);
        foreach (var (corners, src) in polygons)
        {
            if (!o.IncludeHidden && (src.Hidden || src.Tag is { Visible: false }))
                continue;
            var p = corners.Select(i => xf.ApplyPoint(points[i])).ToArray();
            for (var k = 1; k + 1 < p.Length; k++)
            {
                var (a, b, c) = mirrored ? (p[0], p[k + 1], p[k]) : (p[0], p[k], p[k + 1]);
                output.Add(new Triangle(a, b, c, src.FrontMaterial ?? inherited));
                if (o.DoubleSided)
                    output.Add(new Triangle(a, c, b, src.BackMaterial ?? inherited));
            }
        }
    }

    private static void AddFace(Face face, Transform xf, bool mirrored, Material? inherited, bool doubleSided, List<Triangle> output)
    {
        var outer = face.OuterLoop.Points.ToList();
        var holes = face.InnerLoops.Select(l => (IReadOnlyList<Vec3>)l.Points.ToList()).ToList();
        var indices = Polygon.Triangulate(outer, holes);
        if (indices.Count == 0)
            return;

        var local = outer.Concat(holes.SelectMany(h => h)).ToArray();
        var points = local.Select(xf.ApplyPoint).ToArray();
        var front = face.FrontMaterial ?? inherited;
        var back = face.BackMaterial ?? inherited;
        // Texture coordinates come from the face's own plane, before the transform, so they travel with it.
        TriangleUv? Uv(Material? m, bool backSide, int a, int b, int c) => m?.Texture == null ? null
            : new TriangleUv(Texturing.Uv(face, backSide, local[a], m), Texturing.Uv(face, backSide, local[b], m), Texturing.Uv(face, backSide, local[c], m));
        for (var i = 0; i < indices.Count; i += 3)
        {
            var (ia, ib, ic) = (indices[i], indices[i + 1], indices[i + 2]);
            // A mirroring transform flips the winding; swap to keep the front side facing out.
            if (mirrored)
                (ib, ic) = (ic, ib);
            output.Add(new Triangle(points[ia], points[ib], points[ic], front, Uv(front, false, ia, ib, ic)));
            if (doubleSided)
                output.Add(new Triangle(points[ia], points[ic], points[ib], back, Uv(back, true, ia, ic, ib)));
        }
    }
}
