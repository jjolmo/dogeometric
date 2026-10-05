using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.Viewport;

/// <summary>
/// Draws the selection like SketchUp: selected edges in blue, selected faces with a blue dot pattern, selected
/// groups/components with a blue bounding box; and the box of the group being edited, dashed grey.
/// </summary>
public sealed class SelectionRenderer
{
    private static readonly Color Selected = new(0, 0, 1);

    private readonly ShaderMaterial _lines = Lines(Selected);
    private readonly ShaderMaterial _contextLines = Lines(new Color(0.45f, 0.45f, 0.45f));
    private readonly ShaderMaterial _faces = new() { Shader = GD.Load<Shader>("res://shaders/selection_face.gdshader") };

    private static ShaderMaterial Lines(Color c)
    {
        var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/edge.gdshader") };
        m.SetShaderParameter("color", c);
        m.SetShaderParameter("depth_bias", 0.0012f);
        return m;
    }

    public void Build(Document doc, Node3D root)
    {
        foreach (var child in root.GetChildren())
        {
            root.RemoveChild(child);
            child.QueueFree();
        }
        var xf = doc.Context.ToWorld;
        var lines = new List<Vector3>();
        var tris = new List<Vector3>();

        foreach (var item in doc.Selection.Items)
        {
            switch (item)
            {
                case Edge e:
                    lines.Add(Space.ToGodot(xf.ApplyPoint(e.Start.Position)));
                    lines.Add(Space.ToGodot(xf.ApplyPoint(e.End.Position)));
                    break;
                case Face f:
                    var outer = f.OuterLoop.Points.ToList();
                    var holes = f.InnerLoops.Select(l => (IReadOnlyList<Vec3>)l.Points.ToList()).ToList();
                    var all = outer.Concat(holes.SelectMany(h => h)).Select(p => Space.ToGodot(xf.ApplyPoint(p))).ToArray();
                    foreach (var i in Polygon.Triangulate(outer, holes))
                        tris.Add(all[i]);
                    break;
                case ComponentInstance inst:
                    AddBox(lines, CatmullClark.ShownBounds(inst.Definition.Entities), inst.Transform.Then(xf));
                    break;
            }
        }

        AddMesh(root, lines, Mesh.PrimitiveType.Lines, _lines);
        AddMesh(root, tris, Mesh.PrimitiveType.Triangles, _faces);

        // The group/component being edited gets a grey box, as in SketchUp.
        if (doc.Context.Path.Count > 0)
        {
            var active = doc.Context.Path[^1];
            var parent = doc.Context.Path.Take(doc.Context.Path.Count - 1).Aggregate(Transform.Identity, (acc, i) => i.Transform.Then(acc));
            var box = new List<Vector3>();
            AddBox(box, active.Definition.Entities.Bounds(), active.Transform.Then(parent));
            AddMesh(root, box, Mesh.PrimitiveType.Lines, _contextLines);
        }
    }

    private static void AddBox(List<Vector3> lines, Bounds3 b, Transform xf)
    {
        if (b.IsEmpty)
            return;
        var c = Enumerable.Range(0, 8)
            .Select(i => Space.ToGodot(xf.ApplyPoint(new Vec3((i & 1) == 0 ? b.Min.X : b.Max.X, (i & 2) == 0 ? b.Min.Y : b.Max.Y, (i & 4) == 0 ? b.Min.Z : b.Max.Z))))
            .ToArray();
        int[][] pairs = [[0, 1], [2, 3], [4, 5], [6, 7], [0, 2], [1, 3], [4, 6], [5, 7], [0, 4], [1, 5], [2, 6], [3, 7]];
        foreach (var p in pairs)
        {
            lines.Add(c[p[0]]);
            lines.Add(c[p[1]]);
        }
    }

    private static void AddMesh(Node3D root, List<Vector3> verts, Mesh.PrimitiveType type, ShaderMaterial material)
    {
        if (verts.Count == 0)
            return;
        var mesh = new ArrayMesh();
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        mesh.AddSurfaceFromArrays(type, arrays);
        mesh.SurfaceSetMaterial(0, material);
        root.AddChild(new MeshInstance3D { Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }
}
