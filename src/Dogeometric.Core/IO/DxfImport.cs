using System.Globalization;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.IO;

/// <summary>
/// File › Import of AutoCAD DXF drawings (a board outline from KiCad, a panel drawing): lines, polylines (with arc
/// bulges), circles and arcs become edges in one group, closed loops fill with faces as when drawn, circles and arcs
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
        Vec3 P(double x, double y, double z = 0) => new(x * scale, y * scale, z * scale);
        void Draw(List<Vec3> pts, bool closed, string layer, Curve? curve = null)
        {
            if (pts.Count < 2)
                return;
            var before = e.Edges.Count;
            StickyGeometry.DrawEdges(e, pts, closed, Vec3.UnitZ, curve);
            // DXF's layer "0" is SketchUp's Untagged.
            var tag = layer == "0" ? null : model.GetOrAddTag(layer);
            foreach (var edge in e.Edges.Skip(before))
                edge.Tag = tag;
        }

        for (var k = 0; k < entities.Count; k++)
        {
            var ent = entities[k];
            switch (ent.Type)
            {
                case "LINE":
                    Draw([P(ent.Get(10), ent.Get(20), ent.Get(30)), P(ent.Get(11), ent.Get(21), ent.Get(31))], false, ent.Layer);
                    break;
                case "CIRCLE":
                {
                    var c = P(ent.Get(10), ent.Get(20), ent.Get(30));
                    var r = ent.Get(40) * scale;
                    var curve = new Curve { Center = c, Normal = Vec3.UnitZ, Radius = r, Segments = 24 };
                    Draw(Shapes.RegularPolygon(c, Vec3.UnitZ, Vec3.UnitX, r, 24), true, ent.Layer, curve);
                    break;
                }
                case "ARC":
                {
                    var c = P(ent.Get(10), ent.Get(20), ent.Get(30));
                    var r = ent.Get(40) * scale;
                    double a0 = ent.Get(50) * Math.PI / 180, a1 = ent.Get(51) * Math.PI / 180;
                    var sweep = a1 - a0;
                    if (sweep <= 0)
                        sweep += 2 * Math.PI;
                    var curve = new Curve { Center = c, Normal = Vec3.UnitZ, Radius = r, Segments = 24 };
                    var start = c + new Vec3(Math.Cos(a0), Math.Sin(a0), 0) * r;
                    Draw(Shapes.CenterArc(c, Vec3.UnitZ, start, sweep, Math.Max(2, (int)Math.Ceiling(24 * sweep / (2 * Math.PI)))), false, ent.Layer, curve);
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

        model.Definitions.Add(def);
        model.Entities.AddInstance(def, Transform.Identity);
        return model;
    }
}
