using System.Globalization;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.IO;

/// <summary>
/// File › Import of AutoCAD DXF drawings (a board outline from KiCad, a panel drawing): lines, polylines (with arc
/// bulges), circles, arcs and 3D faces become edges in one group, closed loops fill with faces as when drawn, circles and arcs
/// stay curves (so they dimension by radius) and layers become tags. The unit comes from $INSUNITS (millimetres when
/// it is missing).
/// </summary>
public static class DxfImport
{
    private sealed class Entity(string type)
    {
        public string Type { get; } = type;
        public string Layer { get; set; } = "0";
        public List<(int Code, string Value)> Codes { get; } = [];

        public double Get(int code, double fallback = 0) =>
            Codes.LastOrDefault(c => c.Code == code) is { Value: { } v } && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : fallback;

        public int Flags => (int)Get(70);
    }

    public static Model Load(string path) => Read(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));

    public static Model Read(string text, string name = "DXF")
    {
        var lines = text.Replace("\r", "").Split('\n');
        var pairs = new List<(int Code, string Value)>();
        for (var i = 0; i + 1 < lines.Length; i += 2)
            if (int.TryParse(lines[i].Trim(), out var code))
                pairs.Add((code, lines[i + 1].Trim()));

        var scale = 1.0;
        for (var i = 0; i + 1 < pairs.Count; i++)
            if (pairs[i] is (9, "$INSUNITS") && pairs[i + 1].Code == 70)
                scale = (int)double.Parse(pairs[i + 1].Value, CultureInfo.InvariantCulture) switch
                {
                    1 => 25.4, 2 => 304.8, 5 => 10, 6 => 1000, _ => 1,
                };

        // Entities of the ENTITIES section, with their group codes.
        var entities = new List<Entity>();
        var inEntities = false;
        Entity? current = null;
        for (var i = 0; i < pairs.Count; i++)
        {
            var (code, value) = pairs[i];
            if (code == 2 && i > 0 && pairs[i - 1] is (0, "SECTION"))
                inEntities = value == "ENTITIES";
            if (!inEntities)
                continue;
            if (code == 0)
            {
                current = value is "ENDSEC" ? null : new Entity(value);
                if (current != null)
                    entities.Add(current);
                continue;
            }
            if (current == null)
                continue;
            if (code == 8)
                current.Layer = value;
            current.Codes.Add((code, value));
        }

        var model = new Model();
        var def = new ComponentDefinition { Name = name, IsGroup = true };
        var e = def.Entities;
        // A drawing that brings its own faces (3D faces, as SketchUp and other modellers export) gets no faces from its lines.
        var hasFaces = entities.Any(x => x.Type == "3DFACE");
        var drawn = new HashSet<Edge>();
        var faces3d = new List<(List<Vec3> Points, object? Layer)>();
        Vec3 P(double x, double y, double z = 0) => new(x * scale, y * scale, z * scale);
        void Draw(List<Vec3> pts, bool closed, string layer, Curve? curve = null, Vec3? normal = null)
        {
            if (pts.Count < 2)
                return;
            var before = e.Edges.Count;
            drawn.UnionWith(StickyGeometry.DrawEdges(e, pts, closed, normal ?? Vec3.UnitZ, curve, fill: !hasFaces));
            // DXF's layer "0" is SketchUp's Untagged.
            var tag = layer == "0" ? null : model.GetOrAddTag(layer);
            foreach (var edge in e.Edges.Skip(before))
                edge.Tag = tag;
        }

        // Faces first, so lines and curves can follow their edges.
        for (var pass = hasFaces ? 0 : 1; pass < 2; pass++)
        for (var k = 0; k < entities.Count; k++)
        {
            var ent = entities[k];
            if ((ent.Type == "3DFACE") != (pass == 0))
                continue;
            switch (ent.Type)
            {
                case "3DFACE":
                {
                    // A 3D face's fourth corner repeats the third on triangles; a warped quad is two triangles.
                    var c = Enumerable.Range(0, 4).Select(i => P(ent.Get(10 + i), ent.Get(20 + i), ent.Get(30 + i))).ToList();
                    if (c[3].DistanceTo(c[2]) < Tolerance.Length)
                        c.RemoveAt(3);
                    var corners = c.Where((p, i) => c.FindIndex(q => q.DistanceTo(p) < Tolerance.Length) == i).ToList();
                    if (corners.Count < 3)
                        break;
                    var n = Polygon.Normal(corners).Normalized();
                    if (corners.Count == 4 && Math.Abs((corners[3] - corners[0]).Dot(n)) > Tolerance.Length)
                    {
                        faces3d.Add(([corners[0], corners[1], corners[2]], ent.Layer));
                        faces3d.Add(([corners[0], corners[2], corners[3]], ent.Layer));
                    }
                    else
                        faces3d.Add((corners, ent.Layer));
                    break;
                }
                case "LINE":
                    Draw([P(ent.Get(10), ent.Get(20), ent.Get(30)), P(ent.Get(11), ent.Get(21), ent.Get(31))], false, ent.Layer);
                    break;
                case "CIRCLE":
                case "ARC":
                case "ELLIPSE":
                {
                    Vec3 n, ax, ay, c;
                    double r, a0, sweep, ratio = 1;
                    var full = ent.Type == "CIRCLE";
                    if (ent.Type == "ELLIPSE")
                    {
                        // An ellipse is in world coordinates: centre, major axis (relative), minor/major ratio, parameters.
                        n = new Vec3(ent.Get(210), ent.Get(220), ent.Get(230, 1)).Normalized();
                        var major = new Vec3(ent.Get(11), ent.Get(21), ent.Get(31)) * scale;
                        (c, r, ratio) = (P(ent.Get(10), ent.Get(20), ent.Get(30)), major.Length, ent.Get(40, 1));
                        (ax, ay) = (major.Normalized(), n.Cross(major.Normalized()));
                        (a0, sweep) = (ent.Get(41), ent.Get(42, 2 * Math.PI) - ent.Get(41));
                        full = Math.Abs(Math.Abs(sweep) - 2 * Math.PI) < 1e-9;
                    }
                    else
                    {
                        // Circles and arcs lie in their entity's coordinate system (DXF's arbitrary-axis rule on code 210).
                        n = new Vec3(ent.Get(210), ent.Get(220), ent.Get(230, 1)).Normalized();
                        ax = (Math.Abs(n.X) < 1.0 / 64 && Math.Abs(n.Y) < 1.0 / 64 ? Vec3.UnitY : Vec3.UnitZ).Cross(n).Normalized();
                        ay = n.Cross(ax);
                        c = ax * (ent.Get(10) * scale) + ay * (ent.Get(20) * scale) + n * (ent.Get(30) * scale);
                        r = ent.Get(40) * scale;
                        a0 = full ? 0 : ent.Get(50) * Math.PI / 180;
                        sweep = full ? 2 * Math.PI : ent.Get(51) * Math.PI / 180 - a0;
                    }
                    if (sweep <= 0)
                        sweep += 2 * Math.PI;
                    if (Math.Abs(ratio - 1) > 1e-6)
                    {
                        // A true ellipse has no radius to keep: it comes in as plain edges.
                        var steps = Math.Max(4, (int)Math.Ceiling(48 * sweep / (2 * Math.PI)));
                        var pts = Enumerable.Range(0, full ? steps : steps + 1)
                            .Select(i => a0 + sweep * i / steps).Select(t => c + ax * (r * Math.Cos(t)) + ay * (r * ratio * Math.Sin(t))).ToList();
                        Draw(pts, full, ent.Layer, normal: n);
                        break;
                    }
                    var curve = new Curve { Center = c, Normal = n, Radius = r, Segments = 24 };
                    double Along(Vec3 p)
                    {
                        var a = Math.Atan2((p - c).Dot(ay), (p - c).Dot(ax)) - a0;
                        return ((a % (2 * Math.PI)) + 2 * Math.PI) % (2 * Math.PI);
                    }
                    // With faces already in, follow their vertices on the circle so the curve is their edges.
                    var onCircle = hasFaces
                        ? faces3d.SelectMany(f => f.Points).Where(p => Math.Abs(p.DistanceTo(c) - r) < Tolerance.Length * 10
                            && Math.Abs((p - c).Dot(n)) < Tolerance.Length * 10 && (full || Along(p) <= sweep + 1e-6))
                            .DistinctBy(p => (Math.Round(p.X / Tolerance.Length), Math.Round(p.Y / Tolerance.Length), Math.Round(p.Z / Tolerance.Length))).OrderBy(Along).ToList()
                        : [];
                    if (onCircle.Count >= (full ? 3 : 2))
                        Draw(onCircle, full, ent.Layer, curve, n);
                    else if (full)
                        Draw(Shapes.RegularPolygon(c, n, ax, r, 24), true, ent.Layer, curve, n);
                    else
                        Draw(Shapes.CenterArc(c, n, c + (ax * Math.Cos(a0) + ay * Math.Sin(a0)) * r, sweep, Math.Max(2, (int)Math.Ceiling(24 * sweep / (2 * Math.PI)))), false, ent.Layer, curve, n);
                    break;
                }
                case "LWPOLYLINE":
                {
                    // Vertices come as 10/20 pairs, each optionally followed by its bulge (42) towards the next one.
                    var vertices = new List<(double X, double Y, double Bulge)>();
                    foreach (var (code, value) in ent.Codes)
                    {
                        var v = double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
                        if (code == 10)
                            vertices.Add((v, 0, 0));
                        else if (code == 20 && vertices.Count > 0)
                            vertices[^1] = vertices[^1] with { Y = v };
                        else if (code == 42 && vertices.Count > 0)
                            vertices[^1] = vertices[^1] with { Bulge = v };
                    }
                    DrawPolyline(vertices, (ent.Flags & 1) != 0, ent.Layer);
                    break;
                }
                case "POLYLINE":
                {
                    var vertices = new List<(double X, double Y, double Bulge)>();
                    while (k + 1 < entities.Count && entities[k + 1].Type == "VERTEX")
                    {
                        var v = entities[++k];
                        vertices.Add((v.Get(10), v.Get(20), v.Get(42)));
                    }
                    DrawPolyline(vertices, (ent.Flags & 1) != 0, ent.Layer);
                    break;
                }
            }
        }

        void DrawPolyline(List<(double X, double Y, double Bulge)> vertices, bool closed, string layer)
        {
            var pts = new List<Vec3>();
            var count = closed ? vertices.Count : vertices.Count - 1;
            for (var i = 0; i < count; i++)
            {
                var (x, y, bulge) = vertices[i];
                var (nx, ny, _) = vertices[(i + 1) % vertices.Count];
                var a = P(x, y);
                var b = P(nx, ny);
                pts.Add(a);
                if (Math.Abs(bulge) < 1e-9)
                    continue;
                // A bulge is tan(angle / 4); its arc runs anticlockwise when positive.
                var sweep = 4 * Math.Atan(bulge);
                var chord = b - a;
                var radius = chord.Length / (2 * Math.Sin(Math.Abs(sweep) / 2));
                var mid = (a + b) * 0.5;
                var toCentre = Vec3.UnitZ.Cross(chord).Normalized() * (radius * Math.Cos(Math.Abs(sweep) / 2)) * Math.Sign(sweep);
                var centre = mid + toCentre;
                var steps = Math.Max(2, (int)Math.Ceiling(24 * Math.Abs(sweep) / (2 * Math.PI)));
                var arc = Shapes.CenterArc(centre, Vec3.UnitZ, a, sweep, steps);
                pts.AddRange(arc.Skip(1).Take(arc.Count - 2));
            }
            if (!closed)
                pts.Add(P(vertices[^1].X, vertices[^1].Y));
            Draw(pts, closed, layer);
        }

        // As SketchUp's import option "Merge coplanar faces": the triangles of exported faces become whole faces again,
        // split only along the drawing's own lines and curves (the visible edges).
        if (hasFaces)
        {
            static (long, long, long) Key(Vec3 p) => ((long)Math.Round(p.X / Tolerance.Length), (long)Math.Round(p.Y / Tolerance.Length), (long)Math.Round(p.Z / Tolerance.Length));
            var visible = drawn.Select(x => (Key(x.Start.Position), Key(x.End.Position))).ToHashSet();
            foreach (var (face, layer, _) in MeshImport.AddMerged(e, faces3d, (a, b) => visible.Contains((Key(a), Key(b))) || visible.Contains((Key(b), Key(a)))))
                face.Tag = layer is string layerName && layerName != "0" ? model.GetOrAddTag(layerName) : null;
        }
        model.Definitions.Add(def);
        model.Entities.AddInstance(def, Transform.Identity);
        return model;
    }
}
