using System.Globalization;
using System.Text;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.IO;

/// <summary>
/// STEP (AP214) export in millimetres: each connected face set becomes a solid if closed, else a surface model.
/// Groups and components are flattened; hidden geometry and tags are left out.
/// </summary>
public static class StepWriter
{
    public sealed record Result(int Solids, int Surfaces);

    private sealed class Writer
    {
        private readonly StringBuilder _data = new();
        private int _next = 1;

        public int Add(string entity)
        {
            var id = _next++;
            _data.Append('#').Append(id).Append('=').Append(entity).Append(";\n");
            return id;
        }

        public string Data => _data.ToString();
    }

    private static string N(double v)
    {
        var s = v.ToString("0.0#########", CultureInfo.InvariantCulture);
        return s == "-0.0" ? "0.0" : s;
    }

    private static string Text(string s) => "'" + s.Replace("'", "''") + "'";

    public static Result Write(Model model, string path, string name = "Dogeometric")
    {
        using var w = new StreamWriter(path, false, new UTF8Encoding(false));
        var result = Write(model, w, name);
        return result;
    }

    public static Result Write(Model model, TextWriter output, string name = "Dogeometric")
    {
        var d = new Writer();
        var context = d.Add("APPLICATION_CONTEXT('core data for automotive mechanical design processes')");
        d.Add($"APPLICATION_PROTOCOL_DEFINITION('international standard','automotive_design',2000,#{context})");
        var productContext = d.Add($"PRODUCT_CONTEXT('',#{context},'mechanical')");
        var product = d.Add($"PRODUCT({Text(name)},{Text(name)},'',(#{productContext}))");
        var formation = d.Add($"PRODUCT_DEFINITION_FORMATION('','',#{product})");
        var definitionContext = d.Add($"PRODUCT_DEFINITION_CONTEXT('part definition',#{context},'design')");
        var definition = d.Add($"PRODUCT_DEFINITION('design','',#{formation},#{definitionContext})");
        var shape = d.Add($"PRODUCT_DEFINITION_SHAPE('','',#{definition})");
        var length = d.Add("(LENGTH_UNIT()NAMED_UNIT(*)SI_UNIT(.MILLI.,.METRE.))");
        var angle = d.Add("(NAMED_UNIT(*)PLANE_ANGLE_UNIT()SI_UNIT($,.RADIAN.))");
        var solidAngle = d.Add("(NAMED_UNIT(*)SI_UNIT($,.STERADIAN.)SOLID_ANGLE_UNIT())");
        var uncertainty = d.Add($"UNCERTAINTY_MEASURE_WITH_UNIT(LENGTH_MEASURE(1.E-03),#{length},'distance_accuracy_value','')");
        var geometryContext = d.Add($"(GEOMETRIC_REPRESENTATION_CONTEXT(3)GLOBAL_UNCERTAINTY_ASSIGNED_CONTEXT((#{uncertainty}))" +
            $"GLOBAL_UNIT_ASSIGNED_CONTEXT((#{length},#{angle},#{solidAngle}))REPRESENTATION_CONTEXT('',''))");

        int Point(Vec3 p) => d.Add($"CARTESIAN_POINT('',({N(p.X)},{N(p.Y)},{N(p.Z)}))");
        int Direction(Vec3 v)
        {
            var u = v.Normalized();
            return d.Add($"DIRECTION('',({N(u.X)},{N(u.Y)},{N(u.Z)}))");
        }
        int Placement(Vec3 at, Vec3 z, Vec3 x) => d.Add($"AXIS2_PLACEMENT_3D('',#{Point(at)},#{Direction(z)},#{Direction(x)})");

        var items = new List<int> { Placement(Vec3.Zero, Vec3.UnitZ, Vec3.UnitX) };
        var (solids, surfaces) = (0, 0);

        foreach (var (entities, xf) in Visible(model.Entities, Transform.Identity))
        {
            var faces = entities.Faces.Where(f => !f.Hidden && f.Tag is not { Visible: false }).ToList();
            if (faces.Count == 0)
                continue;
            var vertices = new Dictionary<Vertex, int>();
            var edges = new Dictionary<Edge, int>();
            int VertexId(Vertex v) => vertices.TryGetValue(v, out var id) ? id : vertices[v] = d.Add($"VERTEX_POINT('',#{Point(xf.ApplyPoint(v.Position))})");
            int EdgeId(Edge e)
            {
                if (edges.TryGetValue(e, out var id))
                    return id;
                var (a, b) = (xf.ApplyPoint(e.Start.Position), xf.ApplyPoint(e.End.Position));
                var line = d.Add($"LINE('',#{Point(a)},#{d.Add($"VECTOR('',#{Direction(b - a)},{N(a.DistanceTo(b))})")})");
                return edges[e] = d.Add($"EDGE_CURVE('',#{VertexId(e.Start)},#{VertexId(e.End)},#{line},.T.)");
            }

            foreach (var shell in Shells(faces))
            {
                var stepFaces = new List<int>();
                foreach (var face in shell)
                {
                    var world = face.OuterLoop.Points.Select(xf.ApplyPoint).ToList();
                    var normal = Polygon.Normal(world);
                    if (normal.IsZero(1e-12))
                        continue;
                    var bounds = new List<int>();
                    for (var i = 0; i < face.Loops.Count; i++)
                    {
                        var loop = face.Loops[i];
                        var points = loop.Points.Select(xf.ApplyPoint).ToList();
                        // The outline runs counter-clockwise about the face normal, holes clockwise.
                        var flip = Polygon.Normal(points).Dot(normal) > 0 != (i == 0);
                        var oriented = loop.Edges.Select(x =>
                        {
                            var along = !x.Reversed ^ flip;
                            return d.Add($"ORIENTED_EDGE('',*,*,#{EdgeId(x.Edge)},{(along ? ".T." : ".F.")})");
                        }).ToList();
                        if (flip)
                            oriented.Reverse();
                        var edgeLoop = d.Add($"EDGE_LOOP('',({string.Join(",", oriented.Select(o => "#" + o))}))");
                        bounds.Add(d.Add($"{(i == 0 ? "FACE_OUTER_BOUND" : "FACE_BOUND")}('',#{edgeLoop},.T.)"));
                    }
                    var refDir = world[1] - world[0];
                    var plane = d.Add($"PLANE('',#{Placement(world[0], normal, refDir)})");
                    stepFaces.Add(d.Add($"ADVANCED_FACE('',({string.Join(",", bounds.Select(b => "#" + b))}),#{plane},.T.)"));
                }
                if (stepFaces.Count == 0)
                    continue;
                var list = string.Join(",", stepFaces.Select(f => "#" + f));
                if (IsClosed(shell))
                {
                    items.Add(d.Add($"MANIFOLD_SOLID_BREP('',#{d.Add($"CLOSED_SHELL('',({list}))")})"));
                    solids++;
                }
                else
                {
                    items.Add(d.Add($"SHELL_BASED_SURFACE_MODEL('',(#{d.Add($"OPEN_SHELL('',({list}))")}))"));
                    surfaces++;
                }
            }
        }

        var representation = d.Add($"SHAPE_REPRESENTATION('',({string.Join(",", items.Select(i => "#" + i))}),#{geometryContext})");
        d.Add($"SHAPE_DEFINITION_REPRESENTATION(#{shape},#{representation})");

        var stamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        output.Write("ISO-10303-21;\nHEADER;\n");
        output.Write($"FILE_DESCRIPTION(('Dogeometric model'),'2;1');\n");
        output.Write($"FILE_NAME({Text(name)},'{stamp}',(''),(''),'Dogeometric','Dogeometric','');\n");
        output.Write("FILE_SCHEMA(('AUTOMOTIVE_DESIGN { 1 0 10303 214 1 1 1 1 }'));\nENDSEC;\nDATA;\n");
        output.Write(d.Data);
        output.Write("ENDSEC;\nEND-ISO-10303-21;\n");
        return new Result(solids, surfaces);
    }

    /// <summary>Every visible collection with its world placement, nested groups and components included.</summary>
    private static IEnumerable<(Entities, Transform)> Visible(Entities e, Transform xf)
    {
        yield return (e, xf);
        foreach (var i in e.Instances.Where(i => !i.Hidden && i.Tag is not { Visible: false }))
            foreach (var nested in Visible(i.Definition.Entities, i.Transform.Then(xf)))
                yield return nested;
    }

    /// <summary>Faces grouped by the edges they share.</summary>
    private static List<List<Face>> Shells(List<Face> faces)
    {
        var byEdge = new Dictionary<Edge, List<Face>>();
        foreach (var f in faces)
            foreach (var (edge, _) in f.Loops.SelectMany(l => l.Edges))
            {
                if (!byEdge.TryGetValue(edge, out var list))
                    byEdge[edge] = list = [];
                list.Add(f);
            }
        var seen = new HashSet<Face>();
        var shells = new List<List<Face>>();
        foreach (var start in faces)
        {
            if (!seen.Add(start))
                continue;
            var shell = new List<Face>();
            var queue = new Queue<Face>([start]);
            while (queue.Count > 0)
            {
                var f = queue.Dequeue();
                shell.Add(f);
                foreach (var (edge, _) in f.Loops.SelectMany(l => l.Edges))
                    foreach (var other in byEdge[edge])
                        if (seen.Add(other))
                            queue.Enqueue(other);
            }
            shells.Add(shell);
        }
        return shells;
    }

    /// <summary>Closed when every edge is run by two of the shell's faces, in opposite directions.</summary>
    private static bool IsClosed(List<Face> shell)
    {
        var runs = new Dictionary<Edge, (int Forward, int Backward)>();
        foreach (var (edge, reversed) in shell.SelectMany(f => f.Loops.SelectMany(l => l.Edges)))
        {
            var (fw, bw) = runs.GetValueOrDefault(edge);
            runs[edge] = reversed ? (fw, bw + 1) : (fw + 1, bw);
        }
        return runs.Values.All(r => r is (1, 1));
    }
}
