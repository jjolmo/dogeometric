using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.IO;

/// <summary>File › Import of IFC (2x3 and 4): each building element becomes a group classified with its IFC type, from
/// extrusions, face sets, faceted B-reps and mapped items, placed through its local placements, with surface colours
/// and the openings that void it cut away.</summary>
public static class IfcImport
{
    public sealed record Result(Model Model, int Elements, int SkippedItems);

    /// <summary>An element's body polygons (with their paint) minus closed opening volumes, both in the element's own
    /// coordinates; null leaves the body as it was.</summary>
    public delegate List<(List<Vec3> Points, object? Key)>? OpeningCutter(List<(List<Vec3> Points, object? Key)> body, IReadOnlyList<List<List<Vec3>>> openings);

    /// <param name="cut">Subtracts openings (the solids library's booleans); without it walls keep their openings closed.</param>
    public static Result Load(string path, OpeningCutter? cut = null) => Read(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path), cut);

    public static Result Read(string text, string name, OpeningCutter? cut = null) => new Reader(StepParser.Parse(text), cut).Build(name);

    private sealed class Reader(Dictionary<int, StepParser.Entity> data, OpeningCutter? cut)
    {
        private double _scale = 1;
        private int _skipped;
        private readonly Dictionary<int, Material?> _itemColours = [];
        private readonly Dictionary<Rgba, Material> _materials = [];

        private StepParser.Entity E(object? reference) => reference is StepParser.Ref r && data.TryGetValue(r.Id, out var e)
            ? e : throw new InvalidDataException($"missing entity {reference}");

        private static double D(object? o) => o switch { double d => d, long l => l, _ => 0 };

        private Vec3 Point(object? reference)
        {
            var c = (List<object?>)E(reference).Args[0]!;
            return new Vec3(D(c[0]), D(c.Count > 1 ? c[1] : 0.0), D(c.Count > 2 ? c[2] : 0.0)) * _scale;
        }

        private Vec3 Direction(object? reference)
        {
            var c = (List<object?>)E(reference).Args[0]!;
            return new Vec3(D(c[0]), D(c.Count > 1 ? c[1] : 0.0), D(c.Count > 2 ? c[2] : 0.0)).Normalized();
        }

        /// <summary>An IfcAxis2Placement3D (or 2D) as a transform.</summary>
        private Transform Axes(object? reference)
        {
            if (reference is not StepParser.Ref)
                return Transform.Identity;
            var e = E(reference);
            var o = Point(e.Args[0]);
            Vec3 z, x;
            if (e.Type == "IFCAXIS2PLACEMENT2D")
                (z, x) = (Vec3.UnitZ, e.Args.Count > 1 && e.Args[1] is StepParser.Ref ? Direction(e.Args[1]) : Vec3.UnitX);
            else
            {
                z = e.Args.Count > 1 && e.Args[1] is StepParser.Ref ? Direction(e.Args[1]) : Vec3.UnitZ;
                x = e.Args.Count > 2 && e.Args[2] is StepParser.Ref ? Direction(e.Args[2]) : Math.Abs(z.X) < 0.9 ? Vec3.UnitX : Vec3.UnitY;
            }
            x = (x - z * x.Dot(z)).Normalized();
            return new Transform(x, z.Cross(x), z, o);
        }

        /// <summary>An IfcLocalPlacement in world terms: its axes inside the placement it is relative to.</summary>
        private Transform Placement(object? reference, int depth = 0)
        {
            if (reference is not StepParser.Ref || depth > 64)
                return Transform.Identity;
            var e = E(reference);
            if (e.Type != "IFCLOCALPLACEMENT")
                return Transform.Identity;
            return Axes(e.Args[1]).Then(Placement(e.Args[0], depth + 1));
        }

        public Result Build(string name)
        {
            ReadUnits();
            ReadColours();
            var model = new Model();
            var elements = 0;
            // IfcRelVoidsElement: the building element (argument 4) and the opening cut from it (argument 5).
            var voids = data.Values.Where(e => e.Type == "IFCRELVOIDSELEMENT" && e.Args.Count > 5 && e.Args[4] is StepParser.Ref && e.Args[5] is StepParser.Ref)
                .GroupBy(e => ((StepParser.Ref)e.Args[4]!).Id).ToDictionary(g => g.Key, g => g.Select(e => E(e.Args[5])).ToList());
            foreach (var e in data.Values.OrderBy(x => x.Id))
            {
                if (e.Type is "IFCSITE" or "IFCBUILDING" or "IFCBUILDINGSTOREY" or "IFCSPACE" or "IFCOPENINGELEMENT" or "IFCANNOTATION" or "IFCGRID"
                    || Body(e) is not { Count: > 0 } polygons)
                    continue;
                if (cut != null && voids.TryGetValue(e.Id, out var openings))
                {
                    var toLocal = Placement(e.Args[5]).Inverse();
                    var volumes = openings.Select(o => (Placement: Placement(o.Args[5]).Then(toLocal), Polygons: Body(o)))
                        .Where(o => o.Polygons is { Count: > 0 })
                        .Select(o => o.Polygons!.Select(p => p.Points.Select(o.Placement.ApplyPoint).ToList()).ToList()).ToList();
                    if (volumes.Count > 0 && cut(polygons, volumes) is { } cutAway)
                        polygons = cutAway;
                }
                var label = e.Args[2] is string n && n.Length > 0 ? n : Type(e.Type);
                var def = new ComponentDefinition { Name = label, IsGroup = true, IfcType = Type(e.Type) };
                foreach (var (face, key, _) in MeshImport.AddMerged(def.Entities, polygons))
                    face.FrontMaterial = key as Material;
                // Faceted curves (round columns, pipes) read smooth, as SketchUp's Soften Edges at its default 20°.
                Editing.SoftenEdges(def.Entities, def.Entities.Edges.ToList(), 20, smoothNormals: true, softenCoplanar: false);
                model.Definitions.Add(def);
                model.Entities.AddInstance(def, Placement(e.Args[5])).Name = label;
                elements++;
            }
            foreach (var m in _materials.Values)
                model.Materials.Add(m);
            if (elements == 0)
                throw new InvalidDataException($"no building elements with geometry in {name}");
            return new Result(model, elements, _skipped);
        }

        /// <summary>A product's body polygons in its own coordinates (its placement is argument 5, its shape argument 6).</summary>
        private List<(List<Vec3> Points, object? Key)>? Body(StepParser.Entity product)
        {
            if (product.Args.Count < 7 || product.Args[6] is not StepParser.Ref shapeRef || !data.TryGetValue(shapeRef.Id, out var shape)
                || shape.Type != "IFCPRODUCTDEFINITIONSHAPE")
                return null;
            var representations = ((List<object?>)shape.Args[2]!).Select(E).ToList();
            var body = representations.FirstOrDefault(r => r.Args[1] as string == "Body") ?? representations.FirstOrDefault(r => r.Args[1] as string != "Axis" && r.Args[1] as string != "Box");
            if (body == null)
                return null;
            var polygons = new List<(List<Vec3> Points, object? Key)>();
            foreach (var item in ((List<object?>)body.Args[3]!).Select(E))
                Item(item, Transform.Identity, null, polygons, 0);
            return polygons;
        }

        private static readonly string[] OtherTypes =
        [
            "IfcWallStandardCase", "IfcSlabStandardCase", "IfcBeamStandardCase", "IfcColumnStandardCase", "IfcMemberStandardCase",
            "IfcPlateStandardCase", "IfcDoorStandardCase", "IfcWindowStandardCase", "IfcWallElementedCase", "IfcSlabElementedCase",
            "IfcFurnishingElement", "IfcFlowTerminal", "IfcFlowSegment", "IfcFlowFitting", "IfcFlowController", "IfcEnergyConversionDevice",
            "IfcDistributionElement", "IfcDistributionFlowElement", "IfcBuildingElement", "IfcBuiltElement", "IfcCivilElement",
        ];

        /// <summary>The IFC type's name in its usual capitals (IFCWALLSTANDARDCASE is IfcWallStandardCase).</summary>
        private static string Type(string upper) =>
            Classification.Types.Select(t => t.Name).Concat(OtherTypes).FirstOrDefault(t => t.Equals(upper, StringComparison.OrdinalIgnoreCase))
            ?? "Ifc" + string.Concat(upper.Length > 3 ? upper[3..4] + upper[4..].ToLowerInvariant() : "");

        private void ReadUnits()
        {
            foreach (var e in data.Values.Where(e => e.Type == "IFCSIUNIT" && e.Args.Count > 3 && e.Args[1] as string == "LENGTHUNIT"))
            {
                _scale = (e.Args[2] as string) switch { "MILLI" => 1, "CENTI" => 10, "DECI" => 100, "KILO" => 1_000_000, _ => 1000 };
                return;
            }
            foreach (var e in data.Values.Where(e => e.Type == "IFCCONVERSIONBASEDUNIT" && e.Args.Count > 2 && e.Args[1] as string == "LENGTHUNIT"))
                _scale = ((e.Args[2] as string) ?? "").ToUpperInvariant() switch { "INCH" => 25.4, "FOOT" => 304.8, _ => 1 };
        }

        /// <summary>IfcStyledItem colours by the representation item they paint.</summary>
        private void ReadColours()
        {
            foreach (var styled in data.Values.Where(e => e.Type == "IFCSTYLEDITEM" && e.Args[0] is StepParser.Ref))
            {
                var found = Colour(styled.Args[1], 0);
                if (found is { } c)
                {
                    if (!_materials.TryGetValue(c.Colour, out var m))
                        _materials[c.Colour] = m = new Material { Name = c.Name.Length > 0 ? c.Name : $"Color_{c.Colour.R:X2}{c.Colour.G:X2}{c.Colour.B:X2}", Color = c.Colour, Opacity = c.Opacity };
                    _itemColours[((StepParser.Ref)styled.Args[0]!).Id] = m;
                }
            }
        }

        private (Rgba Colour, double Opacity, string Name)? Colour(object? node, int depth)
        {
            if (depth > 8)
                return null;
            if (node is List<object?> list)
                return list.Select(x => Colour(x, depth + 1)).FirstOrDefault(x => x != null);
            if (node is not StepParser.Ref r || !data.TryGetValue(r.Id, out var e))
                return null;
            byte B(object? v) => (byte)Math.Round(Math.Clamp(D(v), 0, 1) * 255);
            switch (e.Type)
            {
                case "IFCSURFACESTYLE":
                    return Colour(e.Args[2], depth + 1) is { } inner ? inner with { Name = e.Args[0] as string ?? inner.Name } : null;
                case "IFCSURFACESTYLERENDERING" or "IFCSURFACESTYLESHADING":
                    return Colour(e.Args[0], depth + 1) is { } c ? c with { Opacity = 1 - (e.Args.Count > 1 ? D(e.Args[1]) : 0) } : null;
                case "IFCCOLOURRGB":
                    return (new Rgba(B(e.Args[1]), B(e.Args[2]), B(e.Args[3])), 1, "");
                default:
                    return e.Args.Select(a => Colour(a, depth + 1)).FirstOrDefault(x => x != null);
            }
        }

        /// <summary>A representation item's faces, in its product's coordinates, keyed by the material painting them.</summary>
        private void Item(StepParser.Entity item, Transform xf, Material? paint, List<(List<Vec3> Points, object? Key)> output, int depth)
        {
            if (depth > 16)
                return;
            paint = _itemColours.GetValueOrDefault(item.Id) ?? paint;
            void Add(List<Vec3> polygon)
            {
                if (polygon.Count >= 3)
                    output.Add((polygon.Select(xf.ApplyPoint).ToList(), paint));
            }
            switch (item.Type)
            {
                case "IFCEXTRUDEDAREASOLID":
                {
                    var position = Axes(item.Args[1]).Then(xf);
                    var direction = Direction(item.Args[2]);
                    var depthMm = D(item.Args[3]) * _scale;
                    foreach (var (outer, holes) in Profile(E(item.Args[0])))
                        Prism(outer, holes, direction * depthMm, position, paint, output);
                    break;
                }
                case "IFCTRIANGULATEDFACESET":
                {
                    var points = Coordinates(item.Args[0]);
                    foreach (var t in (List<object?>)item.Args[3]!)
                        Add(((List<object?>)t!).Select(i => points[(int)D(i) - 1]).ToList());
                    break;
                }
                case "IFCPOLYGONALFACESET":
                {
                    var points = Coordinates(item.Args[0]);
                    foreach (var face in ((List<object?>)item.Args[2]!).Select(E))
                    {
                        var outer = ((List<object?>)face.Args[0]!).Select(i => points[(int)D(i) - 1]).ToList();
                        if (face.Type == "IFCINDEXEDPOLYGONALFACEWITHVOIDS" && face.Args.Count > 1)
                        {
                            var holes = ((List<object?>)face.Args[1]!).Select(h => (IReadOnlyList<Vec3>)((List<object?>)h!).Select(i => points[(int)D(i) - 1]).ToList()).ToList();
                            Triangulated(outer, holes, Add);
                        }
                        else
                            Add(outer);
                    }
                    break;
                }
                case "IFCFACETEDBREP":
                    Faces(E(item.Args[0]).Args[1], Add);
                    break;
                case "IFCSHELLBASEDSURFACEMODEL" or "IFCFACEBASEDSURFACEMODEL":
                    foreach (var shell in ((List<object?>)item.Args[0]!).Select(E))
                        Faces(shell.Args[0], Add);
                    break;
                case "IFCMAPPEDITEM":
                {
                    var map = E(item.Args[0]);
                    var target = Operator(item.Args[1]);
                    var inner = Axes(map.Args[0]).Inverse().Then(target).Then(xf);
                    foreach (var mapped in ((List<object?>)E(map.Args[1]).Args[3]!).Select(E))
                        Item(mapped, inner, paint, output, depth + 1);
                    break;
                }
                case "IFCBOOLEANCLIPPINGRESULT" or "IFCBOOLEANRESULT":
                    // The cut is not applied: the first operand stands in for the result.
                    Item(E(item.Args[1]), xf, paint, output, depth + 1);
                    _skipped++;
                    break;
                default:
                    _skipped++;
                    break;
            }
        }

        private void Faces(object? faces, Action<List<Vec3>> add)
        {
            foreach (var face in ((List<object?>)faces!).Select(E))
            {
                var loops = new List<List<Vec3>>();
                List<Vec3>? outer = null;
                foreach (var bound in ((List<object?>)face.Args[0]!).Select(E))
                {
                    var loop = E(bound.Args[0]);
                    if (loop.Type != "IFCPOLYLOOP")
                        continue;
                    var points = ((List<object?>)loop.Args[0]!).Select(Point).ToList();
                    if (bound.Args.Count > 1 && bound.Args[1] is false)
                        points.Reverse();
                    if (bound.Type == "IFCFACEOUTERBOUND")
                        outer = points;
                    else
                        loops.Add(points);
                }
                outer ??= loops.OrderByDescending(l => Polygon.Area(l)).FirstOrDefault();
                if (outer == null)
                    continue;
                var holes = loops.Where(l => l != outer).Select(l => (IReadOnlyList<Vec3>)l).ToList();
                if (holes.Count == 0)
                    add(outer);
                else
                    Triangulated(outer, holes, add);
            }
        }

        private static void Triangulated(List<Vec3> outer, IReadOnlyList<IReadOnlyList<Vec3>> holes, Action<List<Vec3>> add)
        {
            var all = outer.Concat(holes.SelectMany(h => h)).ToList();
            var tris = Polygon.Triangulate(outer, holes);
            var normal = Polygon.Normal(outer);
            for (var i = 0; i + 2 < tris.Count; i += 3)
            {
                var t = new List<Vec3> { all[tris[i]], all[tris[i + 1]], all[tris[i + 2]] };
                if (Polygon.Normal(t).Dot(normal) < 0)
                    t.Reverse();
                add(t);
            }
        }

        private List<Vec3> Coordinates(object? list) =>
            ((List<object?>)E(list).Args[0]!).Select(p => (List<object?>)p!).Select(c => new Vec3(D(c[0]), D(c[1]), D(c.Count > 2 ? c[2] : 0.0)) * _scale).ToList();

        /// <summary>An IfcCartesianTransformationOperator3D (axes, origin and scale) as a transform.</summary>
        private Transform Operator(object? reference)
        {
            if (reference is not StepParser.Ref)
                return Transform.Identity;
            var e = E(reference);
            var x = e.Args[0] is StepParser.Ref ? Direction(e.Args[0]) : Vec3.UnitX;
            var y = e.Args[1] is StepParser.Ref ? Direction(e.Args[1]) : Vec3.UnitY;
            var o = Point(e.Args[2]);
            var s = e.Args.Count > 3 && e.Args[3] is double d ? d : 1;
            var z = e.Args.Count > 4 && e.Args[4] is StepParser.Ref ? Direction(e.Args[4]) : x.Cross(y).Normalized();
            return new Transform(x * s, y * s, z * s, o);
        }

        /// <summary>A profile's outline and holes in its XY plane.</summary>
        private List<(List<Vec3> Outer, List<List<Vec3>> Holes)> Profile(StepParser.Entity profile)
        {
            switch (profile.Type)
            {
                case "IFCRECTANGLEPROFILEDEF" or "IFCRECTANGLEHOLLOWPROFILEDEF" or "IFCROUNDEDRECTANGLEPROFILEDEF":
                {
                    var at = Axes(profile.Args[2]);
                    var (w, h) = (D(profile.Args[3]) * _scale / 2, D(profile.Args[4]) * _scale / 2);
                    List<Vec3> outer = [new(-w, -h, 0), new(w, -h, 0), new(w, h, 0), new(-w, h, 0)];
                    var holes = new List<List<Vec3>>();
                    if (profile.Type == "IFCRECTANGLEHOLLOWPROFILEDEF")
                    {
                        var t = D(profile.Args[5]) * _scale;
                        holes.Add([new(-w + t, -h + t, 0), new(-w + t, h - t, 0), new(w - t, h - t, 0), new(w - t, -h + t, 0)]);
                    }
                    return [(outer.Select(at.ApplyPoint).ToList(), holes.Select(l => l.Select(at.ApplyPoint).ToList()).ToList())];
                }
                case "IFCCIRCLEPROFILEDEF" or "IFCCIRCLEHOLLOWPROFILEDEF":
                {
                    var at = Axes(profile.Args[2]);
                    var r = D(profile.Args[3]) * _scale;
                    var outer = Shapes.RegularPolygon(Vec3.Zero, Vec3.UnitZ, Vec3.UnitX, r, 24);
                    var holes = new List<List<Vec3>>();
                    if (profile.Type == "IFCCIRCLEHOLLOWPROFILEDEF")
                    {
                        var hole = Shapes.RegularPolygon(Vec3.Zero, Vec3.UnitZ, Vec3.UnitX, r - D(profile.Args[4]) * _scale, 24);
                        hole.Reverse();
                        holes.Add(hole);
                    }
                    return [(outer.Select(at.ApplyPoint).ToList(), holes.Select(l => l.Select(at.ApplyPoint).ToList()).ToList())];
                }
                case "IFCARBITRARYCLOSEDPROFILEDEF":
                    return [(Curve(E(profile.Args[2])), [])];
                case "IFCARBITRARYPROFILEDEFWITHVOIDS":
                    return [(Curve(E(profile.Args[2])), ((List<object?>)profile.Args[3]!).Select(c => Curve(E(c))).ToList())];
                case "IFCDERIVEDPROFILEDEF":
                {
                    var op = E(profile.Args[3]);
                    var x = op.Args[0] is StepParser.Ref ? Direction(op.Args[0]) : Vec3.UnitX;
                    var y = op.Args[1] is StepParser.Ref ? Direction(op.Args[1]) : new Vec3(-x.Y, x.X, 0);
                    var t = new Transform(x, y, Vec3.UnitZ, Point(op.Args[2]));
                    return Profile(E(profile.Args[2])).Select(p => (p.Outer.Select(t.ApplyPoint).ToList(), p.Holes.Select(h => h.Select(t.ApplyPoint).ToList()).ToList())).ToList();
                }
                default:
                    _skipped++;
                    return [];
            }
        }

        /// <summary>A closed profile curve as points: polylines, indexed polycurves (arcs as chords) and circles.</summary>
        private List<Vec3> Curve(StepParser.Entity curve)
        {
            switch (curve.Type)
            {
                case "IFCPOLYLINE":
                {
                    var points = ((List<object?>)curve.Args[0]!).Select(Point).ToList();
                    if (points.Count > 1 && points[0].DistanceTo(points[^1]) < 1e-9)
                        points.RemoveAt(points.Count - 1);
                    return points;
                }
                case "IFCINDEXEDPOLYCURVE":
                {
                    var points = Coordinates(curve.Args[0]);
                    var result = new List<Vec3>();
                    if (curve.Args[1] is List<object?> segments)
                        foreach (var segment in segments.OfType<List<object?>>().SelectMany(x => x).OfType<List<object?>>())
                            result.AddRange(segment.Select(i => points[(int)D(i) - 1]));
                    else
                        result.AddRange(points);
                    var distinct = new List<Vec3>();
                    foreach (var p in result)
                        if (distinct.Count == 0 || distinct[^1].DistanceTo(p) > 1e-9)
                            distinct.Add(p);
                    if (distinct.Count > 1 && distinct[0].DistanceTo(distinct[^1]) < 1e-9)
                        distinct.RemoveAt(distinct.Count - 1);
                    return distinct;
                }
                case "IFCCIRCLE":
                {
                    var at = Axes(curve.Args[0]);
                    return Shapes.RegularPolygon(Vec3.Zero, Vec3.UnitZ, Vec3.UnitX, D(curve.Args[1]) * _scale, 24).Select(at.ApplyPoint).ToList();
                }
                default:
                    _skipped++;
                    return [];
            }
        }

        /// <summary>A profile swept along a vector: its two ends and a side per profile edge, facing out.</summary>
        private static void Prism(List<Vec3> outer, List<List<Vec3>> holes, Vec3 sweep, Transform at, Material? paint, List<(List<Vec3> Points, object? Key)> output)
        {
            if (outer.Count < 3)
                return;
            // Outline anticlockwise and holes clockwise seen from the sweep, so the ends and sides face out.
            if (Polygon.Normal(outer).Dot(sweep) < 0)
                outer.Reverse();
            foreach (var h in holes.Where(h => h.Count >= 3 && Polygon.Normal(h).Dot(sweep) > 0))
                h.Reverse();
            void Add(IEnumerable<Vec3> polygon) => output.Add((polygon.Select(at.ApplyPoint).ToList(), paint));
            var loops = new[] { outer }.Concat(holes.Where(h => h.Count >= 3)).ToList();
            var all = loops.SelectMany(l => l).ToList();
            var tris = Polygon.Triangulate(outer, loops.Skip(1).Select(l => (IReadOnlyList<Vec3>)l).ToList());
            for (var i = 0; i + 2 < tris.Count; i += 3)
            {
                var (a, b, c) = (all[tris[i]], all[tris[i + 1]], all[tris[i + 2]]);
                var top = new List<Vec3> { a + sweep, b + sweep, c + sweep };
                if (Polygon.Normal(top).Dot(sweep) < 0)
                    top.Reverse();
                Add(top);
                var bottom = new List<Vec3> { a, b, c };
                if (Polygon.Normal(bottom).Dot(sweep) > 0)
                    bottom.Reverse();
                Add(bottom);
            }
            foreach (var loop in loops)
                for (var i = 0; i < loop.Count; i++)
                {
                    var (p, q) = (loop[i], loop[(i + 1) % loop.Count]);
                    Add([p, q, q + sweep, p + sweep]);
                }
        }
    }
}
