using System.Globalization;
using System.Text;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.IO;

/// <summary>File › Import of STEP B-rep models (AP203/214/242): each solid becomes a group, its planes, cylinders, cones,
/// spheres, tori and B-spline surfaces tessellated, placed as its assembly says.</summary>
public static class StepImport
{
    /// <summary>Distance a tessellated curve or surface may stray from the true one, in millimetres.</summary>
    public const double Tolerance = 0.05;

    public sealed record Result(Model Model, int Faces, int SkippedFaces);

    public static Result Load(string path) => Read(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));

    public static Result Read(string text, string name)
    {
        var data = StepParser.Parse(text);
        var reader = new Reader(data);
        return reader.Build(name);
    }

    private sealed class Reader(Dictionary<int, StepParser.Entity> data)
    {
        private double _scale = 1;
        private int _faces, _skipped;
        private readonly Dictionary<int, List<Vec3>> _edgeSamples = [];

        private StepParser.Entity E(object? reference) => reference is StepParser.Ref r && data.TryGetValue(r.Id, out var e)
            ? e : throw new InvalidDataException($"missing entity {reference}");

        private Vec3 Point(object? reference)
        {
            var c = (List<object?>)E(reference).Args[1]!;
            return new Vec3(D(c[0]), D(c.Count > 1 ? c[1] : 0.0), D(c.Count > 2 ? c[2] : 0.0)) * _scale;
        }

        private Vec3 Direction(object? reference)
        {
            var c = (List<object?>)E(reference).Args[1]!;
            return new Vec3(D(c[0]), D(c.Count > 1 ? c[1] : 0.0), D(c.Count > 2 ? c[2] : 0.0)).Normalized();
        }

        private static double D(object? o) => o switch
        {
            double d => d,
            long l => l,
            _ => 0,
        };

        /// <summary>An AXIS2_PLACEMENT_3D (or 2D) as origin and right-handed axes.</summary>
        private (Vec3 O, Vec3 X, Vec3 Y, Vec3 Z) Placement(object? reference)
        {
            var e = E(reference);
            var o = Point(e.Args[1]);
            var z = e.Args.Count > 2 && e.Args[2] is StepParser.Ref ? Direction(e.Args[2]) : Vec3.UnitZ;
            var x = e.Args.Count > 3 && e.Args[3] is StepParser.Ref ? Direction(e.Args[3]) : Math.Abs(z.X) < 0.9 ? Vec3.UnitX : Vec3.UnitY;
            if (e.Type == "AXIS2_PLACEMENT_2D")
            {
                z = Vec3.UnitZ;
                x = e.Args.Count > 2 && e.Args[2] is StepParser.Ref ? Direction(e.Args[2]) : Vec3.UnitX;
            }
            x = (x - z * x.Dot(z)).Normalized();
            return (o, x, z.Cross(x), z);
        }

        public Result Build(string name)
        {
            ReadUnits();
            var model = new Model();
            var placements = Placements();
            var colours = Colours();
            var index = 0;
            foreach (var e in data.Values.OrderBy(e => e.Id))
            {
                List<object?> refs;
                switch (e.Type)
                {
                    case "MANIFOLD_SOLID_BREP" or "FACETED_BREP":
                        refs = [e.Args[1]];
                        break;
                    case "BREP_WITH_VOIDS":
                        refs = [e.Args[1], .. (List<object?>)e.Args[2]!];
                        break;
                    case "SHELL_BASED_SURFACE_MODEL":
                        refs = (List<object?>)e.Args[1]!;
                        break;
                    default:
                        continue;
                }
                var (label, transforms) = placements.TryGetValue(e.Id, out var placed)
                    ? placed
                    : (e.Args[0] is string s && s.Length > 0 ? s : $"Solid{index + 1}", [Transform.Identity]);
                index++;
                var faces = refs.SelectMany(r => (List<object?>)E(r).Args[1]!).Select(E).ToList();
                var polygons = new List<(List<Vec3> Points, object? Key)>();
                foreach (var face in faces)
                    try
                    {
                        var before = polygons.Count;
                        Face(face, polygons);
                        if (polygons.Count > before)
                            _faces++;
                        else
                            _skipped++;
                    }
                    catch (Exception ex) when (ex is InvalidDataException or InvalidCastException or ArgumentException or IndexOutOfRangeException)
                    {
                        _skipped++;
                    }
                if (polygons.Count == 0)
                    continue;
                // A part placed more than once in an assembly is a component; once, a group.
                var def = new ComponentDefinition { Name = label, IsGroup = transforms.Count == 1 };
                var made = MeshImport.AddMerged(def.Entities, polygons);
                foreach (var (f, key, _) in made)
                    if (colours.TryGetValue(key is CurvedKey ck ? ck.Face : key is int id ? id : -1, out var paint))
                        f.FrontMaterial = paint;
                // A curved face's triangles stay apart, joined by soft, smooth edges as SketchUp shows curved surfaces.
                var curvedOf = made.Where(m => m.Key is CurvedKey).ToDictionary(m => m.Face, m => ((CurvedKey)m.Key!).Face);
                var users = new Dictionary<Edge, List<int>>();
                foreach (var (f, source) in curvedOf)
                    foreach (var (edge, _) in f.Loops.SelectMany(l => l.Edges))
                    {
                        if (!users.TryGetValue(edge, out var list))
                            users[edge] = list = [];
                        list.Add(source);
                    }
                foreach (var (edge, sources) in users)
                    if (sources.Count == 2 && sources[0] == sources[1])
                        edge.Flags |= EdgeFlags.Soft | EdgeFlags.Smooth;
                model.Definitions.Add(def);
                var solidColour = colours.GetValueOrDefault(e.Id) ?? refs.OfType<StepParser.Ref>().Select(r => colours.GetValueOrDefault(r.Id)).FirstOrDefault(c => c != null);
                foreach (var t in transforms)
                {
                    var instance = model.Entities.AddInstance(def, t);
                    instance.Name = transforms.Count == 1 ? label : "";
                    instance.Material = solidColour;
                }
            }
            if (model.Definitions.Count == 0)
                throw new InvalidDataException($"no B-rep solids or shells in {name}");
            foreach (var m in colours.Values.Distinct())
                model.Materials.Add(m);
            return new Result(model, _faces, _skipped);
        }

        /// <summary>Each solid's part name and placements: a representation related with a transform sits in its parent at
        /// M(item 2)·M(item 1)⁻¹, once per relation; related without one, it shares the parent's frame.</summary>
        private Dictionary<int, (string Name, List<Transform> Transforms)> Placements()
        {
            var reps = data.Values.Where(e => e.Type.EndsWith("REPRESENTATION") && e.Args.Count > 1 && e.Args[1] is List<object?>).ToList();
            var group = reps.ToDictionary(r => r.Id, r => r.Id);
            int Root(int i) => group[i] == i ? i : group[i] = Root(group[i]);
            var placedIn = new List<(int Child, int Parent, Transform T)>();
            foreach (var e in data.Values)
            {
                if (e.Type == "SHAPE_REPRESENTATION_RELATIONSHIP" && e.Args[2] is StepParser.Ref r1 && e.Args[3] is StepParser.Ref r2
                    && group.ContainsKey(r1.Id) && group.ContainsKey(r2.Id))
                    group[Root(r1.Id)] = Root(r2.Id);
                else if (e.Parts is { } parts && parts.FirstOrDefault(p => p.Type == "REPRESENTATION_RELATIONSHIP") is { } rel
                    && parts.FirstOrDefault(p => p.Type == "REPRESENTATION_RELATIONSHIP_WITH_TRANSFORMATION") is { } with
                    && rel.Args[2] is StepParser.Ref child && rel.Args[3] is StepParser.Ref parent
                    && E(with.Args[0]) is { Type: "ITEM_DEFINED_TRANSFORMATION" } transform)
                {
                    var m1 = Frame(transform.Args[2]);
                    var m2 = Frame(transform.Args[3]);
                    placedIn.Add((child.Id, parent.Id, m1.Inverse().Then(m2)));
                }
            }
            var edges = placedIn.Where(x => group.ContainsKey(x.Child) && group.ContainsKey(x.Parent))
                .Select(x => (Child: Root(x.Child), Parent: Root(x.Parent), x.T)).ToList();
            var memo = new Dictionary<int, List<Transform>>();
            List<Transform> World(int g, int depth)
            {
                if (memo.TryGetValue(g, out var known))
                    return known;
                var ups = edges.Where(x => x.Child == g).ToList();
                var result = ups.Count == 0 || depth > 32
                    ? [Transform.Identity]
                    : ups.SelectMany(x => World(x.Parent, depth + 1).Select(w => x.T.Then(w))).ToList();
                return memo[g] = result;
            }

            // The part's name: PRODUCT ← PRODUCT_DEFINITION_FORMATION ← PRODUCT_DEFINITION ← its shape's representation.
            var names = new Dictionary<int, string>();
            foreach (var sdr in data.Values.Where(e => e.Type == "SHAPE_DEFINITION_REPRESENTATION"))
                try
                {
                    var definition = E(E(sdr.Args[0]).Args[2]);
                    var product = E(E(definition.Args[2]).Args[2]);
                    if (product.Args[1] is string n && n.Length > 0 && sdr.Args[1] is StepParser.Ref rep && group.ContainsKey(rep.Id))
                        names[Root(rep.Id)] = n;
                }
                catch (Exception ex) when (ex is InvalidDataException or InvalidCastException or ArgumentException)
                {
                }

            var result = new Dictionary<int, (string, List<Transform>)>();
            foreach (var rep in reps)
                foreach (var item in ((List<object?>)rep.Args[1]!).OfType<StepParser.Ref>())
                    if (data.TryGetValue(item.Id, out var solid) && solid.Type is "MANIFOLD_SOLID_BREP" or "FACETED_BREP" or "BREP_WITH_VOIDS" or "SHELL_BASED_SURFACE_MODEL")
                    {
                        var g = Root(rep.Id);
                        result[item.Id] = (names.GetValueOrDefault(g) ?? (solid.Args[0] is string s && s.Length > 0 ? s : $"Solid{item.Id}"), World(g, 0));
                    }
            return result;
        }

        /// <summary>The colours styled items give faces, shells and solids (STYLED_ITEM → … → COLOUR_RGB), one material
        /// per distinct colour.</summary>
        private Dictionary<int, Material> Colours()
        {
            var materials = new Dictionary<Rgba, Material>();
            var result = new Dictionary<int, Material>();
            (Rgba Colour, string Name)? Find(object? node, int depth)
            {
                if (depth > 10)
                    return null;
                if (node is List<object?> list)
                    return list.Select(x => Find(x, depth + 1)).FirstOrDefault(x => x != null);
                if (node is not StepParser.Ref r || !data.TryGetValue(r.Id, out var e))
                    return null;
                byte B(object? v) => (byte)Math.Round(Math.Clamp(D(v), 0, 1) * 255);
                switch (e.Type)
                {
                    case "COLOUR_RGB":
                        return (new Rgba(B(e.Args[1]), B(e.Args[2]), B(e.Args[3])), e.Args[0] as string ?? "");
                    case "DRAUGHTING_PRE_DEFINED_COLOUR":
                        var named = (e.Args[0] as string ?? "").ToLowerInvariant() switch
                        {
                            "red" => new Rgba(255, 0, 0),
                            "green" => new Rgba(0, 255, 0),
                            "blue" => new Rgba(0, 0, 255),
                            "yellow" => new Rgba(255, 255, 0),
                            "magenta" => new Rgba(255, 0, 255),
                            "cyan" => new Rgba(0, 255, 255),
                            "black" => new Rgba(0, 0, 0),
                            _ => new Rgba(255, 255, 255),
                        };
                        return (named, e.Args[0] as string ?? "");
                    case "SURFACE_STYLE_USAGE":
                        return Find(e.Args[1], depth + 1);
                    case "SURFACE_STYLE_RENDERING" or "SURFACE_STYLE_RENDERING_WITH_PROPERTIES":
                        return Find(e.Args[1], depth + 1);
                    default:
                        // Styles nest colours in their arguments: look through all of them.
                        return e.Args.Select(a => Find(a, depth + 1)).FirstOrDefault(x => x != null);
                }
            }
            foreach (var item in data.Values.Where(e => e.Type is "STYLED_ITEM" or "OVER_RIDING_STYLED_ITEM"))
            {
                if (item.Args.Count < 3 || item.Args[2] is not StepParser.Ref target || Find(item.Args[1], 0) is not { } found)
                    continue;
                if (!materials.TryGetValue(found.Colour, out var material))
                {
                    var c = found.Colour;
                    materials[c] = material = new Material { Name = found.Name.Length > 0 ? found.Name : $"Color_{c.R:X2}{c.G:X2}{c.B:X2}", Color = c };
                }
                result[target.Id] = material;
            }
            return result;
        }

        private Transform Frame(object? placement)
        {
            var (o, x, y, z) = Placement(placement);
            return new Transform(x, y, z, o);
        }

        private void ReadUnits()
        {
            foreach (var e in data.Values)
            {
                if (e.Parts is not { } parts || !parts.Any(p => p.Type == "LENGTH_UNIT"))
                    continue;
                if (parts.FirstOrDefault(p => p.Type == "SI_UNIT") is { } si)
                {
                    _scale = (si.Args.Count > 0 ? si.Args[0] as string : null) switch
                    {
                        "MILLI" => 1,
                        "CENTI" => 10,
                        "DECI" => 100,
                        "KILO" => 1_000_000,
                        "MICRO" => 0.001,
                        _ => 1000,
                    };
                    return;
                }
                if (parts.FirstOrDefault(p => p.Type == "CONVERSION_BASED_UNIT") is { } conversion)
                {
                    _scale = ((conversion.Args[0] as string) ?? "").ToUpperInvariant() switch
                    {
                        "INCH" => 25.4,
                        "FOOT" => 304.8,
                        _ => 1,
                    };
                    return;
                }
            }
        }


        private void Face(StepParser.Entity face, List<(List<Vec3> Points, object? Key)> output)
        {
            var bounds = ((List<object?>)face.Args[1]!).Select(E).ToList();
            var sameSense = face.Type == "FACE_SURFACE" || face.Type == "ADVANCED_FACE" ? !(face.Args.Count > 3 && face.Args[3] is false) : true;
            var loops = new List<List<Vec3>>();
            foreach (var bound in bounds)
            {
                var loop = E(bound.Args[1]);
                var orientation = !(bound.Args.Count > 2 && bound.Args[2] is false);
                List<Vec3> points;
                if (loop.Type == "POLY_LOOP")
                    points = ((List<object?>)loop.Args[1]!).Select(Point).ToList();
                else if (loop.Type == "EDGE_LOOP")
                    points = EdgeLoop(loop);
                else
                    continue;
                if (!orientation)
                    points.Reverse();
                if (points.Count >= 2)
                    loops.Add(points);
            }
            if (face.Type == "FACE" || face.Args.Count < 3)
            {
                Planar(loops, null, face.Id, output);
                return;
            }
            var surface = Surface(E(face.Args[2]));
            if (surface == null)
                return;
            if (surface.IsPlane)
                Planar(loops, surface.Normal(0, 0) * (sameSense ? 1 : -1), face.Id, output);
            else
                Curved(surface, loops, sameSense, face.Id, output);
        }

        private static void Planar(List<List<Vec3>> loops, Vec3? normal, int id, List<(List<Vec3> Points, object? Key)> output)
        {
            loops = loops.Where(l => l.Count >= 3).ToList();
            if (loops.Count == 0)
                return;
            var outer = loops.OrderByDescending(l => Polygon.Area(l)).First();
            var holes = loops.Where(l => l != outer).Select(l => (IReadOnlyList<Vec3>)l).ToList();
            var all = outer.Concat(holes.SelectMany(h => h)).ToList();
            var tris = Polygon.Triangulate(outer, holes);
            for (var i = 0; i + 2 < tris.Count; i += 3)
            {
                var t = new List<Vec3> { all[tris[i]], all[tris[i + 1]], all[tris[i + 2]] };
                if (normal is { } n && Polygon.Normal(t).Dot(n) < 0)
                    t.Reverse();
                output.Add((t, id));
            }
        }

        /// <summary>A curved face: its loops mapped to the surface's parameters, triangulated there, refined where the
        /// triangles stray from the surface, and lifted back.</summary>
        private static void Curved(Surface surface, List<List<Vec3>> loops, bool sameSense, int id, List<(List<Vec3> Points, object? Key)> output)
        {
            var uv = new List<List<(double U, double V)>>();
            var wrapping = new List<List<(double U, double V)>>();
            foreach (var loop in loops)
            {
                var mapped = new List<(double U, double V)>();
                (double U, double V)? last = null;
                foreach (var p in loop)
                {
                    var q = surface.Project(p, last);
                    mapped.Add(q);
                    last = q;
                }
                mapped = SplitPoles(surface, mapped);
                var du = mapped[^1].U - mapped[0].U;
                var dv = mapped[^1].V - mapped[0].V;
                if (surface.PeriodU > 0 && Math.Abs(du) > surface.PeriodU / 2 && Math.Abs(dv) < 1e-6 + Math.Abs(du))
                    wrapping.Add(mapped);
                else
                    uv.Add(mapped);
            }

            // Bands and caps: loops going once around the surface are joined across the seam.
            if (wrapping.Count == 2)
            {
                var (a, b) = (wrapping[0], wrapping[1]);
                var shift = Math.Round((a[0].U - b[^1].U) / surface.PeriodU) * surface.PeriodU;
                var bs = b.Select(p => (p.U + shift, p.V)).ToList();
                uv.Add([.. a, .. Between(surface, a[^1], bs[0]), .. bs, .. Between(surface, bs[^1], a[0])]);
            }
            else if (wrapping.Count == 1)
            {
                var a = wrapping[0];
                var increasing = a[^1].U > a[0].U;
                var v = increasing == sameSense ? surface.VMax : surface.VMin;
                (double, double) p1 = (a[^1].U, v), p2 = (a[0].U, v);
                uv.Add([.. a, .. Between(surface, a[^1], p1), p1, .. Between(surface, p1, p2), p2, .. Between(surface, p2, a[0])]);
            }
            if (uv.Count == 0 && wrapping.Count == 0)
            {
                (double, double)[] c = [(surface.UMin, surface.VMin), (surface.UMax, surface.VMin), (surface.UMax, surface.VMax), (surface.UMin, surface.VMax)];
                uv.Add(Enumerable.Range(0, 4).SelectMany(i => new[] { c[i] }.Concat(Between(surface, c[i], c[(i + 1) % 4]))).ToList());
            }
            uv = uv.Where(l => l.Count >= 3).ToList();
            if (uv.Count == 0)
                return;

            // Triangulate in parameter space, then refine interior edges.
            var outer = uv.OrderByDescending(l => Math.Abs(Area2(l))).First();
            var holes = uv.Where(l => l != outer).ToList();
            var points = outer.Concat(holes.SelectMany(h => h)).ToList();
            var triangles = TriangulateUv(points, [outer.Count, .. holes.Select(h => h.Count)]);
            // A face going all the way round meets itself at the seam, where flips can fold it; it keeps plain splitting.
            var aroundSeam = surface.PeriodU > 0 && outer.Max(q => q.U) - outer.Min(q => q.U) > surface.PeriodU - 1e-6;
            Refine(surface, points, triangles, flips: !aroundSeam);

            // ∂u × ∂v is the surface's normal, so a triangle anticlockwise in (u, v) faces it; the face's sense may flip it.
            var corners = new List<Vec3>();
            var weld = new Dictionary<(long, long, long), int>();
            int Corner((double U, double V) q)
            {
                var p = surface.Eval(q.U, q.V);
                var key = ((long)Math.Round(p.X * 1e6), (long)Math.Round(p.Y * 1e6), (long)Math.Round(p.Z * 1e6));
                if (!weld.TryGetValue(key, out var i))
                {
                    corners.Add(p);
                    weld[key] = i = corners.Count - 1;
                }
                return i;
            }
            var mesh = new List<(int A, int B, int C)>();
            foreach (var (a, b, c) in triangles)
            {
                var uvArea = (points[b].U - points[a].U) * (points[c].V - points[a].V) - (points[c].U - points[a].U) * (points[b].V - points[a].V);
                var (ia, ib, ic) = (Corner(points[a]), Corner(points[b]), Corner(points[c]));
                mesh.Add(uvArea > 0 == sameSense ? (ia, ib, ic) : (ia, ic, ib));
            }
            RemoveSlivers(corners, mesh);
            for (var i = 0; i < mesh.Count; i++)
                output.Add(([corners[mesh[i].A], corners[mesh[i].B], corners[mesh[i].C]], new CurvedKey(id, i)));
        }

        /// <summary>A loop point at a pole (a sphere's, a cone's apex) is a whole side in parameters: it becomes two points,
        /// at the u of the point before it and of the point after it.</summary>
        private static List<(double U, double V)> SplitPoles(Surface s, List<(double U, double V)> loop)
        {
            if (loop.Count < 3 || s.PeriodU <= 0)
                return loop;
            bool Pole((double U, double V) q) => (s.Eval(q.U + 1, q.V) - s.Eval(q.U, q.V)).Length < 1e-9 * Math.Max(1, s.Eval(q.U, q.V).Length);
            if (!loop.Any(Pole))
                return loop;
            var result = new List<(double U, double V)>();
            for (var i = 0; i < loop.Count; i++)
            {
                var q = loop[i];
                if (!Pole(q))
                {
                    result.Add(q);
                    continue;
                }
                var (before, after) = (loop[(i + loop.Count - 1) % loop.Count], loop[(i + 1) % loop.Count]);
                if (!Pole(before))
                    result.Add((before.U, q.V));
                if (!Pole(after))
                    result.Add((after.U, q.V));
            }
            return result;
        }

        /// <summary>Drops triangles flat in space (where parameters fold at a seam or pole); one with a point mid-side hands it
        /// to the neighbour across that side, split in two, so no edge keeps a point only one side has.</summary>
        private static void RemoveSlivers(List<Vec3> p, List<(int A, int B, int C)> mesh)
        {
            for (var guard = 0; guard < 100_000; guard++)
            {
                var index = mesh.FindIndex(t => t.A == t.B || t.B == t.C || t.A == t.C
                    || (p[t.B] - p[t.A]).Cross(p[t.C] - p[t.A]).Length <= 1e-9 * Math.Max(1, (p[t.B] - p[t.A]).LengthSquared + (p[t.C] - p[t.A]).LengthSquared));
                if (index < 0)
                    return;
                var (a, b, c) = mesh[index];
                mesh.RemoveAt(index);
                if (a == b || b == c || a == c)
                    continue;
                // The middle point of the three, and the two ends of the long side.
                int[] t = [a, b, c];
                var middle = t.OrderBy(i => t.Where(j => j != i).Sum(j => -(p[j] - p[i]).Length)).First();
                var ends = t.Where(i => i != middle).ToArray();
                var between = t.Where(i => i != ends[0] && i != ends[1] && (p[i] - p[ends[0]]).Dot(p[i] - p[ends[1]]) <= 0).ToList();
                if (between.Count == 0)
                    continue;
                middle = between[0];
                var other = mesh.FindIndex(n => Has(n, ends[0]) && Has(n, ends[1]) && !Has(n, middle));
                if (other < 0)
                    continue;
                var (x, y, z) = mesh[other];
                // Rotate the neighbour so its shared side is x→y, then split that side at the middle point.
                while (!((x == ends[0] || x == ends[1]) && (y == ends[0] || y == ends[1])))
                    (x, y, z) = (y, z, x);
                mesh[other] = (x, middle, z);
                mesh.Add((middle, y, z));
            }
        }

        private static bool Has((int A, int B, int C) t, int i) => t.A == i || t.B == i || t.C == i;

        /// <summary>Points strictly between <paramref name="p"/> and <paramref name="q"/> (a straight line in parameter space)
        /// so the surface between them stays within the tolerance; for the parts of a boundary that are not model edges.</summary>
        private static List<(double U, double V)> Between(Surface s, (double U, double V) p, (double U, double V) q)
        {
            for (var n = 1; n < 256; n *= 2)
            {
                var ok = true;
                for (var i = 0; i < n && ok; i++)
                {
                    (double U, double V) a = (p.U + (q.U - p.U) * i / n, p.V + (q.V - p.V) * i / n);
                    (double U, double V) b = (p.U + (q.U - p.U) * (i + 1) / n, p.V + (q.V - p.V) * (i + 1) / n);
                    var mid = s.Eval((a.U + b.U) / 2, (a.V + b.V) / 2);
                    ok = mid.DistanceTo((s.Eval(a.U, a.V) + s.Eval(b.U, b.V)) * 0.5) <= Tolerance;
                }
                if (ok)
                    return Enumerable.Range(1, n - 1).Select(i => (p.U + (q.U - p.U) * i / n, p.V + (q.V - p.V) * i / n)).ToList();
            }
            return Enumerable.Range(1, 255).Select(i => (p.U + (q.U - p.U) * i / 256, p.V + (q.V - p.V) * i / 256)).ToList();
        }

        /// <summary>Triangulates loops given as runs of <paramref name="points"/>: points in line with their neighbours skip the
        /// ear clipping and come back by fanning their triangle about its centre, so none is flat.</summary>
        private static List<(int A, int B, int C)> TriangulateUv(List<(double U, double V)> points, List<int> counts)
        {
            var loops = new List<List<int>>();
            var at = 0;
            foreach (var n in counts)
            {
                loops.Add(Enumerable.Range(at, n).ToList());
                at += n;
            }
            bool Straight(int a, int b, int c)
            {
                var (ux, uy) = (points[b].U - points[a].U, points[b].V - points[a].V);
                var (vx, vy) = (points[c].U - points[b].U, points[c].V - points[b].V);
                var cross = ux * vy - uy * vx;
                return Math.Abs(cross) <= 1e-9 * Math.Sqrt((ux * ux + uy * uy) * (vx * vx + vy * vy)) && ux * vx + uy * vy > 0;
            }
            // Corners kept, and for each kept corner the dropped points up to the next one.
            var kept = new List<List<int>>();
            var dropped = new Dictionary<int, List<int>>();
            foreach (var loop in loops)
            {
                var corners = loop.Where((p, i) => !Straight(loop[(i - 1 + loop.Count) % loop.Count], p, loop[(i + 1) % loop.Count])).ToList();
                if (corners.Count < 3)
                    corners = loop;
                kept.Add(corners);
                for (var i = 0; i < corners.Count; i++)
                {
                    var from = loop.IndexOf(corners[i]);
                    var to = loop.IndexOf(corners[(i + 1) % corners.Count]);
                    var between = new List<int>();
                    for (var j = (from + 1) % loop.Count; j != to; j = (j + 1) % loop.Count)
                        between.Add(loop[j]);
                    dropped[corners[i]] = between;
                }
            }
            var outer = kept[0].Select(i => new Vec3(points[i].U, points[i].V, 0)).ToList();
            var holes = kept.Skip(1).Select(l => (IReadOnlyList<Vec3>)l.Select(i => new Vec3(points[i].U, points[i].V, 0)).ToList()).ToList();
            var order = kept.SelectMany(l => l).ToList();
            var tris = Polygon.Triangulate(outer, holes);
            var result = new List<(int, int, int)>();
            var next = kept.SelectMany(l => l.Select((p, i) => (p, l[(i + 1) % l.Count]))).ToDictionary(x => x.p, x => x.Item2);
            for (var i = 0; i + 2 < tris.Count; i += 3)
            {
                int[] t = [order[tris[i]], order[tris[i + 1]], order[tris[i + 2]]];
                // The triangle's outline with the dropped points of the boundary edges it lies on.
                var ring = new List<int>();
                var extra = false;
                for (var k = 0; k < 3; k++)
                {
                    var (a, b) = (t[k], t[(k + 1) % 3]);
                    ring.Add(a);
                    if (next.TryGetValue(a, out var n1) && n1 == b && dropped[a].Count > 0)
                    {
                        ring.AddRange(dropped[a]);
                        extra = true;
                    }
                    else if (next.TryGetValue(b, out var n2) && n2 == a && dropped[b].Count > 0)
                    {
                        ring.AddRange(Enumerable.Reverse(dropped[b]));
                        extra = true;
                    }
                }
                if (!extra)
                {
                    result.Add((t[0], t[1], t[2]));
                    continue;
                }
                points.Add(((points[t[0]].U + points[t[1]].U + points[t[2]].U) / 3, (points[t[0]].V + points[t[1]].V + points[t[2]].V) / 3));
                var centre = points.Count - 1;
                for (var k = 0; k < ring.Count; k++)
                    result.Add((ring[k], ring[(k + 1) % ring.Count], centre));
            }
            return result;
        }

        private static double Area2(List<(double U, double V)> l)
            {
                var a = 0.0;
                for (var i = 0; i < l.Count; i++)
                {
                    var (p, q) = (l[i], l[(i + 1) % l.Count]);
                    a += p.U * q.V - q.U * p.V;
                }
                return a / 2;
            }

        /// <summary>Splits interior edges (shared by two triangles) where the surface bulges away from the chord, so
        /// the boundary, shared with neighbouring faces, keeps its points.</summary>
        private static void Refine(Surface s, List<(double U, double V)> points, List<(int A, int B, int C)> triangles, bool flips)
        {
            if (flips)
                Flip(s, points, triangles);
            for (var round = 0; round < 12; round++)
            {
                if (round > 0 && flips)
                    Flip(s, points, triangles);
                var byEdge = new Dictionary<(int, int), List<int>>();
                for (var t = 0; t < triangles.Count; t++)
                {
                    var (a, b, c) = triangles[t];
                    foreach (var (u, v) in new[] { (a, b), (b, c), (c, a) })
                    {
                        var key = u < v ? (u, v) : (v, u);
                        if (!byEdge.TryGetValue(key, out var list))
                            byEdge[key] = list = [];
                        list.Add(t);
                    }
                }
                var splits = new List<((int, int) Edge, double Error)>();
                foreach (var (edge, list) in byEdge.Where(x => x.Value.Count == 2))
                {
                    var (p, q) = (points[edge.Item1], points[edge.Item2]);
                    var mid = s.Eval((p.U + q.U) / 2, (p.V + q.V) / 2);
                    var chord = (s.Eval(p.U, p.V) + s.Eval(q.U, q.V)) * 0.5;
                    var error = mid.DistanceTo(chord);
                    if (error > Tolerance)
                        splits.Add((edge, error));
                }
                if (splits.Count == 0 || triangles.Count > 200_000)
                    return;
                var done = new HashSet<int>();
                foreach (var (edge, _) in splits.OrderByDescending(x => x.Error))
                {
                    var list = byEdge[edge];
                    if (list.Any(done.Contains))
                        continue;
                    var (p, q) = (points[edge.Item1], points[edge.Item2]);
                    points.Add(((p.U + q.U) / 2, (p.V + q.V) / 2));
                    var m = points.Count - 1;
                    foreach (var t in list)
                    {
                        done.Add(t);
                        var (a, b, c) = triangles[t];
                        // The corner opposite the edge, and the edge in the triangle's own winding.
                        var (x, y, z) = (a, b, c);
                        if (!IsEdge(x, y, edge))
                            (x, y, z) = IsEdge(y, z, edge) ? (y, z, x) : (z, x, y);
                        triangles[t] = (x, m, z);
                        triangles.Add((m, y, z));
                    }
                }
            }
        }

        private static bool IsEdge(int u, int v, (int, int) e) => (u, v) == e || (v, u) == e;

        /// <summary>Lawson flips (Delaunay on the surface): an interior edge seen at over 180° from its opposite corners joins
        /// them instead, clearing thin triangles; boundary edges never move, so neighbouring faces still meet.</summary>
        private static void Flip(Surface s, List<(double U, double V)> points, List<(int A, int B, int C)> triangles)
        {
            var p3 = new Dictionary<int, Vec3>();
            Vec3 P(int i) => p3.TryGetValue(i, out var v) ? v : p3[i] = s.Eval(points[i].U, points[i].V);
            // Seams make distinct parameters one point: edges are compared by their points in space.
            var canonical = new Dictionary<(long, long, long), int>();
            int Canon(int i)
            {
                var q = P(i);
                var key = ((long)Math.Round(q.X * 1e6), (long)Math.Round(q.Y * 1e6), (long)Math.Round(q.Z * 1e6));
                return canonical.TryGetValue(key, out var c) ? c : canonical[key] = i;
            }
            (int, int) Key3(int u, int v) => Canon(u) < Canon(v) ? (Canon(u), Canon(v)) : (Canon(v), Canon(u));
            double Uv(int a, int b, int c) =>
                (points[b].U - points[a].U) * (points[c].V - points[a].V) - (points[c].U - points[a].U) * (points[b].V - points[a].V);
            double Angle(int at, int a, int b)
            {
                var (u, v) = (P(a) - P(at), P(b) - P(at));
                return Math.Acos(Math.Clamp(u.Dot(v) / Math.Max(u.Length * v.Length, 1e-30), -1, 1));
            }
            for (var pass = 0; pass < 20; pass++)
            {
                var byEdge = new Dictionary<(int, int), List<int>>();
                for (var t = 0; t < triangles.Count; t++)
                {
                    var (a, b, c) = triangles[t];
                    foreach (var (u, v) in new[] { (a, b), (b, c), (c, a) })
                    {
                        var key = u < v ? (u, v) : (v, u);
                        if (!byEdge.TryGetValue(key, out var list))
                            byEdge[key] = list = [];
                        list.Add(t);
                    }
                }
                var flipped = 0;
                var touched = new HashSet<int>();
                var made = new HashSet<(int, int)>();
                var spatial = byEdge.Keys.Select(e => Key3(e.Item1, e.Item2)).ToHashSet();
                foreach (var (edge, list) in byEdge)
                {
                    if (list.Count != 2 || touched.Contains(list[0]) || touched.Contains(list[1]))
                        continue;
                    int Opposite((int A, int B, int C) t) => t.A != edge.Item1 && t.A != edge.Item2 ? t.A : t.B != edge.Item1 && t.B != edge.Item2 ? t.B : t.C;
                    var (t1, t2) = (triangles[list[0]], triangles[list[1]]);
                    var (c, d) = (Opposite(t1), Opposite(t2));
                    if (c == d || Angle(c, edge.Item1, edge.Item2) + Angle(d, edge.Item1, edge.Item2) <= Math.PI + 1e-9)
                        continue;
                    // The new diagonal must not already be an edge, or three triangles would share it.
                    var diagonal = Key3(c, d);
                    if (spatial.Contains(diagonal) || made.Contains(diagonal))
                        continue;
                    // The first triangle's own order of the shared edge, so the new pair keeps its winding.
                    var (a, b) = (t1.A, t1.B) == (edge.Item1, edge.Item2) || (t1.B, t1.C) == (edge.Item1, edge.Item2) || (t1.C, t1.A) == (edge.Item1, edge.Item2)
                        ? (edge.Item1, edge.Item2) : (edge.Item2, edge.Item1);
                    var sign = Math.Sign(Uv(a, b, c));
                    // Only a convex quad in parameter space can be flipped without folding; a corner lying on the new
                    // diagonal (up to rounding) would end up inside an edge.
                    var eps = 1e-9 * (Math.Abs(Uv(a, b, c)) + Math.Abs(Uv(b, a, d)));
                    if (sign == 0 || Uv(a, d, c) * sign <= eps || Uv(d, b, c) * sign <= eps)
                        continue;
                    // At seams and poles distinct parameters share a point: flip only four distinct corners, into
                    // triangles facing the way the old pair did.
                    int[] quad = [a, b, c, d];
                    if (quad.Any(i => quad.Any(j => j != i && P(i).DistanceTo(P(j)) < 1e-6)))
                        continue;
                    var before = (P(b) - P(a)).Cross(P(c) - P(a)) + (P(a) - P(b)).Cross(P(d) - P(b));
                    var n1 = (P(d) - P(a)).Cross(P(c) - P(a));
                    var n2 = (P(b) - P(d)).Cross(P(c) - P(d));
                    if (n1.Dot(before) <= 0 || n2.Dot(before) <= 0)
                        continue;
                    triangles[list[0]] = (a, d, c);
                    triangles[list[1]] = (d, b, c);
                    touched.Add(list[0]);
                    touched.Add(list[1]);
                    made.Add(diagonal);
                    flipped++;
                }
                if (flipped == 0)
                    return;
            }
        }


        private List<Vec3> EdgeLoop(StepParser.Entity loop)
        {
            var result = new List<Vec3>();
            foreach (var oriented in ((List<object?>)loop.Args[1]!).Select(E))
            {
                var edge = E(oriented.Args[3]);
                var forward = oriented.Args[4] is not false;
                var samples = EdgeSamples(edge);
                var piece = forward ? samples : Enumerable.Reverse(samples).ToList();
                // Each edge's last point is the next edge's first.
                result.AddRange(piece.Take(piece.Count - 1));
            }
            return result;
        }

        /// <summary>An edge as points from its start vertex to its end vertex, the same for every face using it.</summary>
        private List<Vec3> EdgeSamples(StepParser.Entity edge)
        {
            if (_edgeSamples.TryGetValue(edge.Id, out var cached))
                return cached;
            var start = Point(E(edge.Args[1]).Args[1]);
            var end = Point(E(edge.Args[2]).Args[1]);
            var sameSense = edge.Args.Count < 5 || edge.Args[4] is not false;
            var curve = Curve(E(edge.Args[3]));
            List<Vec3> points;
            if (curve == null)
                points = [start, end];
            else
            {
                // Sample along the curve's own direction, from the edge's start (or end, when the senses differ).
                var (from, to) = sameSense ? (start, end) : (end, start);
                var t0 = curve.Param(from, null);
                var t1 = curve.Param(to, t0);
                if (curve.Period > 0)
                {
                    while (t1 <= t0 + 1e-9)
                        t1 += curve.Period;
                    while (t1 - t0 > curve.Period + 1e-9)
                        t1 -= curve.Period;
                }
                var n = curve.Segments(t0, t1);
                points = Enumerable.Range(0, n + 1).Select(i => curve.Eval(t0 + (t1 - t0) * i / n)).ToList();
                points[0] = from;
                points[^1] = to;
                if (!sameSense)
                    points.Reverse();
            }
            return _edgeSamples[edge.Id] = points;
        }

        private Curve? Curve(StepParser.Entity e)
        {
            switch (e.Type)
            {
                case "LINE":
                    return null;
                case "SURFACE_CURVE" or "SEAM_CURVE" or "INTERSECTION_CURVE":
                    return Curve(E(e.Args[1]));
                case "CIRCLE":
                {
                    var p = Placement(e.Args[1]);
                    return new Ellipse(p.O, p.X, p.Y, D(e.Args[2]) * _scale, D(e.Args[2]) * _scale);
                }
                case "ELLIPSE":
                {
                    var p = Placement(e.Args[1]);
                    return new Ellipse(p.O, p.X, p.Y, D(e.Args[2]) * _scale, D(e.Args[3]) * _scale);
                }
                case "B_SPLINE_CURVE_WITH_KNOTS":
                    return BSplineCurve(e, null);
                case "":
                    // A complex instance: a (rational) B-spline curve split over several partial entities.
                    if (e.Parts!.FirstOrDefault(p => p.Type == "B_SPLINE_CURVE") is { } shape
                        && e.Parts!.FirstOrDefault(p => p.Type == "B_SPLINE_CURVE_WITH_KNOTS") is { } knots)
                    {
                        var weights = e.Parts!.FirstOrDefault(p => p.Type == "RATIONAL_B_SPLINE_CURVE")?.Args[0] as List<object?>;
                        return new BSpline(
                            (int)D(shape.Args[0]),
                            ((List<object?>)shape.Args[1]!).Select(Point).ToList(),
                            Knots(knots.Args[0], knots.Args[1]),
                            weights?.Select(D).ToList());
                    }
                    return null;
                default:
                    return null;
            }
        }

        private BSpline BSplineCurve(StepParser.Entity e, List<double>? weights) => new(
            (int)D(e.Args[1]),
            ((List<object?>)e.Args[2]!).Select(Point).ToList(),
            Knots(e.Args[6], e.Args[7]),
            weights);

        private static List<double> Knots(object? multiplicities, object? values)
        {
            var result = new List<double>();
            var m = ((List<object?>)multiplicities!).Select(D).ToList();
            var k = ((List<object?>)values!).Select(D).ToList();
            for (var i = 0; i < k.Count; i++)
                for (var j = 0; j < (int)m[i]; j++)
                    result.Add(k[i]);
            return result;
        }

        private Surface? Surface(StepParser.Entity e)
        {
            switch (e.Type)
            {
                case "PLANE":
                {
                    var p = Placement(e.Args[1]);
                    return new Plane(p.O, p.X, p.Y, p.Z);
                }
                case "CYLINDRICAL_SURFACE":
                {
                    var p = Placement(e.Args[1]);
                    return new Revolved(p.O, p.X, p.Y, p.Z, Profile.Height, v => D(e.Args[2]) * _scale, v => v, double.NegativeInfinity, double.PositiveInfinity);
                }
                case "CONICAL_SURFACE":
                {
                    var p = Placement(e.Args[1]);
                    var r = D(e.Args[2]) * _scale;
                    var tan = Math.Tan(D(e.Args[3]));
                    return new Revolved(p.O, p.X, p.Y, p.Z, Profile.Height, v => r + v * tan, v => v, double.NegativeInfinity, double.PositiveInfinity);
                }
                case "SPHERICAL_SURFACE":
                {
                    var p = Placement(e.Args[1]);
                    var r = D(e.Args[2]) * _scale;
                    return new Revolved(p.O, p.X, p.Y, p.Z, Profile.Sphere, v => r * Math.Cos(v), v => r * Math.Sin(v), -Math.PI / 2, Math.PI / 2, r);
                }
                case "TOROIDAL_SURFACE":
                {
                    var p = Placement(e.Args[1]);
                    var (major, minor) = (D(e.Args[2]) * _scale, D(e.Args[3]) * _scale);
                    return new Revolved(p.O, p.X, p.Y, p.Z, Profile.Torus, v => major + minor * Math.Cos(v), v => minor * Math.Sin(v), 0, 2 * Math.PI, minor, major);
                }
                case "OFFSET_SURFACE":
                    return Surface(E(e.Args[1])) is { } basis ? new Offset(basis, D(e.Args[2]) * _scale) : null;
                case "SURFACE_OF_LINEAR_EXTRUSION":
                {
                    var vector = E(e.Args[2]);
                    return ProfileCurve(E(e.Args[1])) is { } c ? new Extrusion(c, Direction(vector.Args[1])) : null;
                }
                case "SURFACE_OF_REVOLUTION":
                {
                    var axis = E(e.Args[2]);
                    var dir = axis.Args.Count > 2 && axis.Args[2] is StepParser.Ref ? Direction(axis.Args[2]) : Vec3.UnitZ;
                    return ProfileCurve(E(e.Args[1])) is { } c ? new Revolution(c, Point(axis.Args[1]), dir) : null;
                }
                case "B_SPLINE_SURFACE_WITH_KNOTS":
                    return new BSplineSurface((int)D(e.Args[1]), (int)D(e.Args[2]), Grid(e.Args[3]),
                        Knots(e.Args[8], e.Args[10]), Knots(e.Args[9], e.Args[11]), null);
                case "" when e.Parts!.FirstOrDefault(p => p.Type == "B_SPLINE_SURFACE") is { } shape
                    && e.Parts!.FirstOrDefault(p => p.Type == "B_SPLINE_SURFACE_WITH_KNOTS") is { } knots:
                {
                    var weights = e.Parts!.FirstOrDefault(p => p.Type == "RATIONAL_B_SPLINE_SURFACE")?.Args[0] is List<object?> w
                        ? w.Select(row => ((List<object?>)row!).Select(D).ToList()).ToList()
                        : null;
                    return new BSplineSurface((int)D(shape.Args[0]), (int)D(shape.Args[1]), Grid(shape.Args[2]),
                        Knots(knots.Args[0], knots.Args[2]), Knots(knots.Args[1], knots.Args[3]), weights);
                }
                default:
                    return null;
            }
        }

        /// <summary>A curve swept into a surface; unlike an edge's, a line here is a curve too.</summary>
        private Curve? ProfileCurve(StepParser.Entity e)
        {
            if (e.Type != "LINE")
                return Curve(e);
            var vector = E(e.Args[2]);
            return new Line(Point(e.Args[1]), Direction(vector.Args[1]));
        }

        private List<List<Vec3>> Grid(object? rows) => ((List<object?>)rows!).Select(r => ((List<object?>)r!).Select(Point).ToList()).ToList();
    }


    private abstract class Curve
    {
        public virtual double Period => 0;
        public virtual double TMin => 0;
        public virtual double TMax => 2 * Math.PI;
        public abstract Vec3 Eval(double t);
        public abstract double Param(Vec3 p, double? near);
        public abstract int Segments(double t0, double t1);
    }

    private sealed class Ellipse(Vec3 o, Vec3 x, Vec3 y, double a, double b) : Curve
    {
        public override double Period => 2 * Math.PI;
        public override Vec3 Eval(double t) => o + x * (a * Math.Cos(t)) + y * (b * Math.Sin(t));
        public override double Param(Vec3 p, double? near)
        {
            var d = p - o;
            return Math.Atan2(d.Dot(y) / b, d.Dot(x) / a);
        }

        // Segments small enough to stay within the tolerance, and at least 24 for a full turn (SketchUp's circle).
        public override int Segments(double t0, double t1)
        {
            var r = Math.Max(a, b);
            var step = Math.Min(2 * Math.PI / 24, 2 * Math.Acos(Math.Clamp(1 - StepImport.Tolerance / r, -1, 1)));
            return Math.Max(1, (int)Math.Ceiling(Math.Abs(t1 - t0) / Math.Max(step, 1e-3)));
        }
    }

    private sealed class BSpline(int degree, List<Vec3> control, List<double> knots, List<double>? weights) : Curve
    {
        public override double TMin => knots[degree];
        public override double TMax => knots[^(degree + 1)];
        public override Vec3 Eval(double t) => Nurbs.Curve(degree, control, knots, weights, Math.Clamp(t, TMin, TMax));

        public override double Param(Vec3 p, double? near)
        {
            var (lo, hi) = (knots[degree], knots[^(degree + 1)]);
            var best = lo;
            var bestDistance = double.MaxValue;
            const int samples = 200;
            for (var i = 0; i <= samples; i++)
            {
                var t = lo + (hi - lo) * i / samples;
                var d = Eval(t).DistanceTo(p);
                if (d < bestDistance - 1e-12 || Math.Abs(d - bestDistance) < 1e-9 && near is { } n && Math.Abs(t - n) > Math.Abs(best - n))
                    (best, bestDistance) = (t, d);
            }
            // Golden-section refinement around the best sample.
            var (a, b) = (Math.Max(lo, best - (hi - lo) / samples), Math.Min(hi, best + (hi - lo) / samples));
            for (var i = 0; i < 40; i++)
            {
                var m1 = a + (b - a) * 0.382;
                var m2 = a + (b - a) * 0.618;
                if (Eval(m1).DistanceTo(p) < Eval(m2).DistanceTo(p))
                    b = m2;
                else
                    a = m1;
            }
            return (a + b) / 2;
        }

        public override int Segments(double t0, double t1)
        {
            var n = 8;
            while (n < 512)
            {
                var ok = true;
                for (var i = 0; i < n && ok; i++)
                {
                    var (a, b) = (t0 + (t1 - t0) * i / n, t0 + (t1 - t0) * (i + 1) / n);
                    ok = Eval((a + b) / 2).DistanceTo((Eval(a) + Eval(b)) * 0.5) <= Tolerance;
                }
                if (ok)
                    break;
                n *= 2;
            }
            return n;
        }
    }

    private abstract class Surface
    {
        public virtual bool IsPlane => false;
        public virtual double PeriodU => 0;
        public abstract double UMin { get; }
        public abstract double UMax { get; }
        public abstract double VMin { get; }
        public abstract double VMax { get; }
        public abstract Vec3 Eval(double u, double v);
        public abstract (double U, double V) Project(Vec3 p, (double U, double V)? near);

        /// <summary>The surface's own normal (∂u × ∂v), which the face's sense may flip.</summary>
        public virtual Vec3 Normal(double u, double v)
        {
            const double h = 1e-6;
            var du = Eval(u + h, v) - Eval(u - h, v);
            var dv = Eval(u, v + h) - Eval(u, v - h);
            var n = du.Cross(dv);
            if (n.LengthSquared < 1e-24)
            {
                // At a pole the derivative vanishes: look a little inside.
                du = Eval(u + h, v * 0.999) - Eval(u - h, v * 0.999);
                dv = Eval(u, v * 0.999 + h) - Eval(u, v * 0.999 - h);
                n = du.Cross(dv);
            }
            return n.Normalized();
        }
    }

    private sealed class Plane(Vec3 o, Vec3 x, Vec3 y, Vec3 z) : Surface
    {
        public override bool IsPlane => true;
        public override double UMin => 0;
        public override double UMax => 0;
        public override double VMin => 0;
        public override double VMax => 0;
        public override Vec3 Eval(double u, double v) => o + x * u + y * v;
        public override (double U, double V) Project(Vec3 p, (double U, double V)? near) => ((p - o).Dot(x), (p - o).Dot(y));
        public override Vec3 Normal(double u, double v) => z;
    }

    /// <summary>A (rational) B-spline surface; closed in u when its first and last control columns meet, and then periodic
    /// there like a surface of revolution.</summary>
    private sealed class BSplineSurface : Surface
    {
        private readonly int _p, _q;
        private readonly List<List<Vec3>> _control;
        private readonly List<double> _uKnots, _vKnots;
        private readonly List<List<double>>? _weights;
        private readonly double _period;
        private readonly List<(double U, double V, Vec3 P)> _samples = [];

        public BSplineSurface(int p, int q, List<List<Vec3>> control, List<double> uKnots, List<double> vKnots, List<List<double>>? weights)
        {
            (_p, _q, _control, _uKnots, _vKnots, _weights) = (p, q, control, uKnots, vKnots, weights);
            var closed = Enumerable.Range(0, 5).All(i =>
            {
                var v = VMin + (VMax - VMin) * i / 4;
                return Eval(UMin, v).DistanceTo(Eval(UMax, v)) < 1e-6;
            });
            _period = closed ? UMax - UMin : 0;
            for (var i = 0; i <= 24; i++)
                for (var j = 0; j <= 24; j++)
                {
                    var (u, v) = (UMin + (UMax - UMin) * i / 24, VMin + (VMax - VMin) * j / 24);
                    _samples.Add((u, v, Eval(u, v)));
                }
        }

        public override double PeriodU => _period;
        public override double UMin => _uKnots[_p];
        public override double UMax => _uKnots[^(_p + 1)];
        public override double VMin => _vKnots[_q];
        public override double VMax => _vKnots[^(_q + 1)];

        private double WrapU(double u) => _period > 0 ? UMin + ((u - UMin) % _period + _period) % _period : Math.Clamp(u, UMin, UMax);

        public override Vec3 Eval(double u, double v)
        {
            u = u >= UMax && _period > 0 && u - UMax < 1e-12 ? UMax : WrapU(u);
            v = Math.Clamp(v, VMin, VMax);
            var su = Nurbs.Span(_p, _uKnots, u);
            var sv = Nurbs.Span(_q, _vKnots, v);
            var bu = Nurbs.Basis(_p, _uKnots, su, u);
            var bv = Nurbs.Basis(_q, _vKnots, sv, v);
            var sum = Vec3.Zero;
            var w = 0.0;
            for (var i = 0; i <= _p; i++)
                for (var j = 0; j <= _q; j++)
                {
                    var (r, c) = (su - _p + i, sv - _q + j);
                    var weight = bu[i] * bv[j] * (_weights?[r][c] ?? 1);
                    sum += _control[r][c] * weight;
                    w += weight;
                }
            return w == 0 ? sum : sum * (1 / w);
        }

        /// <summary>The nearest point's parameters by Gauss-Newton, from the previous point along a loop (or the
        /// nearest sample), with u brought next to the previous point's when the surface is closed.</summary>
        public override (double U, double V) Project(Vec3 p, (double U, double V)? near)
        {
            (double U, double V) Solve((double U, double V) start)
            {
                var (u, v) = start;
                for (var k = 0; k < 30; k++)
                {
                    var hu = (UMax - UMin) * 1e-6;
                    var hv = (VMax - VMin) * 1e-6;
                    var s = Eval(u, v);
                    var su = (Eval(u + hu, v) - Eval(u - hu, v)) * (1 / (2 * hu));
                    var sv = (Eval(u, v + hv) - Eval(u, v - hv)) * (1 / (2 * hv));
                    var r = p - s;
                    var (a, b, c) = (su.Dot(su), su.Dot(sv), sv.Dot(sv));
                    var (e, f) = (su.Dot(r), sv.Dot(r));
                    var det = a * c - b * b;
                    if (Math.Abs(det) < 1e-18)
                        break;
                    var (du, dv) = ((c * e - b * f) / det, (a * f - b * e) / det);
                    u += du;
                    v = Math.Clamp(v + dv, VMin, VMax);
                    if (_period == 0)
                        u = Math.Clamp(u, UMin, UMax);
                    if (Math.Abs(du) < 1e-12 && Math.Abs(dv) < 1e-12)
                        break;
                }
                return (u, v);
            }
            var best = near is { } n ? Solve(n) : (double.NaN, double.NaN);
            if (double.IsNaN(best.Item1) || Eval(best.Item1, best.Item2).DistanceTo(p) > StepImport.Tolerance)
            {
                var seed = _samples.MinBy(x => x.P.DistanceTo(p));
                var candidate = Solve((seed.U, seed.V));
                if (double.IsNaN(best.Item1) || Eval(candidate.U, candidate.V).DistanceTo(p) < Eval(best.Item1, best.Item2).DistanceTo(p))
                    best = candidate;
            }
            if (_period > 0)
            {
                var u = WrapU(best.Item1);
                if (near is { } m)
                    u += Math.Round((m.U - u) / _period) * _period;
                best = (u, best.Item2);
            }
            return best;
        }
    }

    /// <summary>A surface moved along its own normal; a point's parameters are those of its foot on the base surface.</summary>
    private sealed class Offset(Surface basis, double distance) : Surface
    {
        public override double PeriodU => basis.PeriodU;
        public override double UMin => basis.UMin;
        public override double UMax => basis.UMax;
        public override double VMin => basis.VMin;
        public override double VMax => basis.VMax;
        public override Vec3 Eval(double u, double v) => basis.Eval(u, v) + basis.Normal(u, v) * distance;
        public override Vec3 Normal(double u, double v) => basis.Normal(u, v);
        public override (double U, double V) Project(Vec3 p, (double U, double V)? near) => basis.Project(p, near);
    }

    private sealed class Line(Vec3 o, Vec3 d) : Curve
    {
        public override double TMin => -1e6;
        public override double TMax => 1e6;
        public override Vec3 Eval(double t) => o + d * t;
        public override double Param(Vec3 p, double? near) => (p - o).Dot(d);
        public override int Segments(double t0, double t1) => 1;
    }

    /// <summary>A profile curve swept along a direction: the point at u on the curve, moved v along it.</summary>
    private sealed class Extrusion(Curve curve, Vec3 d) : Surface
    {
        private readonly double _period = curve.Eval(curve.TMin).DistanceTo(curve.Eval(curve.TMax)) < 1e-6 ? curve.TMax - curve.TMin : 0;
        private readonly Vec3 _origin = curve.Eval(curve.TMin);
        public override double PeriodU => _period;
        public override double UMin => curve.TMin;
        public override double UMax => curve.TMax;
        public override double VMin => double.NegativeInfinity;
        public override double VMax => double.PositiveInfinity;
        public override Vec3 Eval(double u, double v) => curve.Eval(Wrap(u)) + d * v;

        private double Wrap(double u) => _period > 0 ? UMin + ((u - UMin) % _period + _period) % _period : u;

        // The point brought back along the direction to the profile's plane, then found on the curve.
        public override (double U, double V) Project(Vec3 p, (double U, double V)? near)
        {
            var onProfile = p - d * (p - _origin).Dot(d);
            var u = curve.Param(onProfile, near?.U);
            var v = (p - curve.Eval(u)).Dot(d);
            if (_period > 0 && near is { } n)
                u += Math.Round((n.U - u) / _period) * _period;
            return (u, v);
        }
    }

    /// <summary>A profile curve turned about an axis: the curve's point at v, turned u radians.</summary>
    private sealed class Revolution(Curve curve, Vec3 o, Vec3 axis) : Surface
    {
        private readonly Vec3 _reference = Reference(curve, o, axis);
        public override double PeriodU => 2 * Math.PI;
        public override double UMin => 0;
        public override double UMax => 2 * Math.PI;
        public override double VMin => curve.TMin;
        public override double VMax => curve.TMax;

        private static Vec3 Reference(Curve c, Vec3 o, Vec3 axis)
        {
            foreach (var t in new[] { 0.5, 0.25, 0.75, 0.1, 0.9 })
            {
                var q = c.Eval(c.TMin + (c.TMax - c.TMin) * t) - o;
                var r = q - axis * q.Dot(axis);
                if (r.Length > 1e-9)
                    return r.Normalized();
            }
            return (Math.Abs(axis.X) < 0.9 ? Vec3.UnitX : Vec3.UnitY).Cross(axis).Normalized();
        }

        private Vec3 Turn(Vec3 p, double angle)
        {
            var q = p - o;
            return o + q * Math.Cos(angle) + axis.Cross(q) * Math.Sin(angle) + axis * (axis.Dot(q) * (1 - Math.Cos(angle)));
        }

        public override Vec3 Eval(double u, double v) => Turn(curve.Eval(v), u);

        // The point turned back into the profile's half-plane, then found on the curve.
        public override (double U, double V) Project(Vec3 p, (double U, double V)? near)
        {
            var q = p - o;
            var radial = q - axis * q.Dot(axis);
            var u = radial.Length < 1e-9 && near is { } n0 ? n0.U
                : Math.Atan2(_reference.Cross(radial).Dot(axis), _reference.Dot(radial));
            var v = curve.Param(Turn(p, -u), near?.V);
            if (near is { } n)
                u += Math.Round((n.U - u) / (2 * Math.PI)) * 2 * Math.PI;
            return (u, v);
        }
    }

    private enum Profile { Height, Sphere, Torus }

    /// <summary>Keeps each triangle of a curved face apart when faces are merged.</summary>
    private sealed record CurvedKey(int Face, int Index);

    /// <summary>A surface of revolution about the placement's Z: at angle u and parameter v, the point at radius
    /// <c>radius(v)</c> and height <c>height(v)</c>. Cylinders and cones use the height as v; spheres and tori an angle.</summary>
    private sealed class Revolved(Vec3 o, Vec3 x, Vec3 y, Vec3 z, Profile profile, Func<double, double> radius, Func<double, double> height,
        double vMin, double vMax, double minor = 0, double major = 0) : Surface
    {
        public override double PeriodU => 2 * Math.PI;
        public override double UMin => 0;
        public override double UMax => 2 * Math.PI;
        public override double VMin => vMin;
        public override double VMax => vMax;

        public override Vec3 Eval(double u, double v) => o + (x * Math.Cos(u) + y * Math.Sin(u)) * radius(v) + z * height(v);

        public override (double U, double V) Project(Vec3 p, (double U, double V)? near)
        {
            var d = p - o;
            var (px, py, pz) = (d.Dot(x), d.Dot(y), d.Dot(z));
            var r = Math.Sqrt(px * px + py * py);
            var u = r < 1e-9 && near is { } n0 ? n0.U : Math.Atan2(py, px);
            var v = profile switch
            {
                Profile.Sphere => Math.Asin(Math.Clamp(pz / minor, -1, 1)),
                Profile.Torus => Math.Atan2(pz, r - major),
                _ => pz,
            };
            if (near is { } n)
            {
                u += Math.Round((n.U - u) / (2 * Math.PI)) * 2 * Math.PI;
                if (profile == Profile.Torus)
                    v += Math.Round((n.V - v) / (2 * Math.PI)) * 2 * Math.PI;
            }
            else if (profile == Profile.Torus && v < 0)
                v += 2 * Math.PI;
            return (u, v);
        }
    }
}
