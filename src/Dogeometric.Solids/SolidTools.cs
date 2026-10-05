using Dogeometric.Core.Geometry;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Solids;

/// <summary>
/// SketchUp's Solid Tools on groups/components of one collection. Results are new groups in the same place; the
/// inputs go (or stay, for Trim) exactly as SketchUp's tools do.
/// </summary>
public static class SolidTools
{
    /// <summary>Union: one solid with everything, interior faces removed.</summary>
    public static ComponentInstance Union(Model model, Entities e, IReadOnlyList<ComponentInstance> inputs) =>
        Combine(model, e, inputs, (a, b) => a.Union(b), "Union");

    /// <summary>Outer Shell: the union with its inner cavities filled.</summary>
    public static ComponentInstance OuterShell(Model model, Entities e, IReadOnlyList<ComponentInstance> inputs) =>
        Combine(model, e, inputs, (a, b) =>
        {
            using var union = a.Union(b);
            return union.WithoutCavities();
        }, "Outer Shell");

    /// <summary>Intersect: only the volume common to all.</summary>
    public static ComponentInstance Intersect(Model model, Entities e, IReadOnlyList<ComponentInstance> inputs) =>
        Combine(model, e, inputs, (a, b) => a.Intersect(b), "Intersect");

    /// <summary>Subtract: <paramref name="second"/> minus <paramref name="first"/>; both inputs are replaced by the result.</summary>
    public static ComponentInstance? Subtract(Model model, Entities e, ComponentInstance first, ComponentInstance second)
    {
        using var a = ToSolid(first);
        using var b = ToSolid(second);
        using var r = b.Subtract(a);
        var result = Place(model, e, r, Sources(first, second), second);
        e.Instances.Remove(first);
        e.Instances.Remove(second);
        return result;
    }

    /// <summary>Trim: <paramref name="second"/> minus <paramref name="first"/>, keeping <paramref name="first"/>.</summary>
    public static ComponentInstance? Trim(Model model, Entities e, ComponentInstance first, ComponentInstance second)
    {
        using var a = ToSolid(first);
        using var b = ToSolid(second);
        using var r = b.Subtract(a);
        var result = Place(model, e, r, Sources(first, second), second);
        e.Instances.Remove(second);
        return result;
    }

    /// <summary>Split: the common part and both remainders, each its own group.</summary>
    public static List<ComponentInstance> Split(Model model, Entities e, ComponentInstance first, ComponentInstance second)
    {
        using var a = ToSolid(first);
        using var b = ToSolid(second);
        using var both = a.Intersect(b);
        using var onlyA = a.Subtract(b);
        using var onlyB = b.Subtract(a);
        var sources = Sources(first, second);
        var results = new[] { Place(model, e, onlyA, sources, first), Place(model, e, both, sources, first), Place(model, e, onlyB, sources, second) }
            .OfType<ComponentInstance>().ToList();
        e.Instances.Remove(first);
        e.Instances.Remove(second);
        return results;
    }

    /// <summary>
    /// IFC openings (<see cref="IfcImport.OpeningCutter"/>): the body minus each closed opening volume, as triangles
    /// keeping the paint of the body face in their plane; the reveals take the body's most used paint. Null when the
    /// body is not a closed solid or no opening is.
    /// </summary>
    public static List<(List<Vec3> Points, object? Key)>? CutOpenings(List<(List<Vec3> Points, object? Key)> body, IReadOnlyList<List<List<Vec3>>> openings)
    {
        static List<Triangle> Triangles(IEnumerable<List<Vec3>> polygons)
        {
            var result = new List<Triangle>();
            foreach (var p in polygons)
            {
                var idx = Polygon.Triangulate(p);
                for (var i = 0; i + 2 < idx.Count; i += 3)
                    result.Add(new Triangle(p[idx[i]], p[idx[i + 1]], p[idx[i + 2]], null));
            }
            return result;
        }

        Solid solid;
        try
        {
            solid = Solid.FromTriangles(Triangles(body.Select(b => b.Points)));
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        try
        {
            var cut = false;
            foreach (var opening in openings)
            {
                Solid hole;
                try
                {
                    hole = Solid.FromTriangles(Triangles(opening));
                }
                catch (InvalidOperationException)
                {
                    continue;
                }
                using (hole)
                {
                    var next = solid.Subtract(hole);
                    solid.Dispose();
                    solid = next;
                    cut = true;
                }
            }
            if (!cut)
                return null;
            var planes = body.Where(b => b.Points.Count >= 3).Select(b => (Normal: Polygon.Normal(b.Points), b.Points, b.Key))
                .Where(b => !b.Normal.IsZero(1e-12)).Select(b => (b.Normal, D: b.Normal.Dot(b.Points[0]), b.Key)).ToList();
            var fallback = body.GroupBy(b => b.Key).OrderByDescending(g => g.Count()).First().Key;
            var (positions, tris) = solid.ToMesh();
            var result = new List<(List<Vec3> Points, object? Key)>();
            for (var i = 0; i + 2 < tris.Count; i += 3)
            {
                List<Vec3> t = [positions[tris[i]], positions[tris[i + 1]], positions[tris[i + 2]]];
                var n = Polygon.Normal(t);
                var source = planes.FirstOrDefault(p => p.Normal.Dot(n) > 1 - 1e-6 && Math.Abs(p.D - n.Dot(t[0])) < 1e-3);
                result.Add((t, source.Normal.IsZero(1e-12) ? fallback : source.Key));
            }
            return result;
        }
        finally
        {
            solid.Dispose();
        }
    }

    /// <summary>True when the group/component is a closed manifold volume (SketchUp's "Solid Group").</summary>
    public static bool IsSolid(ComponentInstance i) => MeshCheck.Analyze(MeshExtractor.ExtractInstance(i)).IsWatertight;

    private static ComponentInstance Combine(Model model, Entities e, IReadOnlyList<ComponentInstance> inputs, Func<Solid, Solid, Solid> op, string name)
    {
        if (inputs.Count < 2)
            throw new InvalidOperationException($"{name} needs at least two solids");
        var solids = inputs.Select(ToSolid).ToList();
        try
        {
            var acc = solids[0];
            for (var i = 1; i < solids.Count; i++)
            {
                var next = op(acc, solids[i]);
                if (acc != solids[0])
                    acc.Dispose();
                acc = next;
            }
            var result = Place(model, e, acc, Sources(inputs.ToArray()), inputs[0])
                ?? throw new InvalidOperationException($"{name} left nothing");
            if (acc != solids[0])
                acc.Dispose();
            foreach (var i in inputs)
                e.Instances.Remove(i);
            return result;
        }
        finally
        {
            foreach (var s in solids)
                s.Dispose();
        }
    }

    private static Solid ToSolid(ComponentInstance i)
    {
        try
        {
            return Solid.FromTriangles(MeshExtractor.ExtractInstance(i));
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException($"'{(string.IsNullOrEmpty(i.Name) ? i.Definition.Name : i.Name)}' is {ex.Message}", ex);
        }
    }

    /// <summary>Planes and materials of the inputs' faces, so result faces keep their paint.</summary>
    private static List<SolidRebuilder.PlaneMaterial> Sources(params ComponentInstance[] inputs)
    {
        var list = new List<SolidRebuilder.PlaneMaterial>();
        foreach (var t in inputs.SelectMany(i => MeshExtractor.ExtractInstance(i)))
        {
            var n = t.Normal;
            if (n.IsZero(1e-12))
                continue;
            var d = n.Dot(t.A);
            if (!list.Any(p => p.Normal.Dot(n) > 1 - 1e-6 && Math.Abs(p.D - d) < 1e-3))
                list.Add(new SolidRebuilder.PlaneMaterial(n, d, t.Material, null, null));
        }
        return list;
    }

    private static ComponentInstance? Place(Model model, Entities e, Solid solid, List<SolidRebuilder.PlaneMaterial> sources, ComponentInstance like)
    {
        if (solid.IsEmpty)
            return null;
        var (positions, tris) = solid.ToMesh();
        var def = new ComponentDefinition { Name = $"Group#{model.Definitions.Count(d => d.IsGroup) + 1}", IsGroup = true };
        SolidRebuilder.Build(def.Entities, positions, tris, sources);
        model.Definitions.Add(def);
        var inst = e.AddInstance(def, Transform.Identity);
        inst.Tag = like.Tag;
        inst.Name = like.Name;
        return inst;
    }
}

/// <summary>Where to find Manifold's native library (the app adds its native/&lt;rid&gt; folder at start-up).</summary>
public static class SolidsNative
{
    public static void AddSearchDirectory(string directory) => ManifoldNative.SearchDirectories.Add(directory);
}
