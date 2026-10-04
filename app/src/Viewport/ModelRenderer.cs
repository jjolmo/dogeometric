using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;
using Godot;
using GTransform = Godot.Transform3D;
using Material = Dogeometric.Core.Modeling.Material;
using Model = Dogeometric.Core.Modeling.Model;

namespace Dogeometric.App.Viewport;

/// <summary>
/// Turns a <see cref="Model"/> into Godot nodes. Each definition becomes one face mesh and one edge mesh, shared by
/// all its instances (the way SketchUp stores components), so big models stay light.
/// </summary>
public sealed class ModelRenderer
{
    private static readonly Rgba DefaultFront = new(255, 255, 255);
    private static readonly Rgba DefaultBack = new(164, 178, 187);

    private readonly Shader _faceShader = GD.Load<Shader>("res://shaders/face.gdshader");
    private readonly Shader _faceTransparentShader = GD.Load<Shader>("res://shaders/face_transparent.gdshader");
    private readonly ShaderMaterial _edgeMaterial = new() { Shader = GD.Load<Shader>("res://shaders/edge.gdshader") };

    private readonly Dictionary<(Material?, Material?), ShaderMaterial> _faceMaterials = [];
    private readonly Dictionary<Entities, DefinitionMesh> _meshes = [];

    /// <summary>Mesh data of one entity collection. Surfaces are keyed by (front, back) material; null = default.</summary>
    private sealed record DefinitionMesh(ArrayMesh? Faces, List<(Material? Front, Material? Back)> Surfaces, ArrayMesh? Edges);

    /// <summary>
    /// Replaces the children of <paramref name="root"/> with the model's geometry. Meshes of collections that did
    /// not change are reused; pass the changed ones in <paramref name="changed"/> (null = all).
    /// </summary>
    public void Build(Model model, Node3D root, IEnumerable<Entities>? changed = null)
    {
        if (changed == null)
            _meshes.Clear();
        else
            foreach (var e in changed)
                _meshes.Remove(e);
        foreach (var child in root.GetChildren())
        {
            root.RemoveChild(child);
            child.QueueFree();
        }
        AddEntities(model.Entities, root, inherited: null);
    }

    private void AddEntities(Entities entities, Node3D parent, Material? inherited)
    {
        var mesh = MeshFor(entities);
        if (mesh.Faces != null)
        {
            var faces = new MeshInstance3D { Mesh = mesh.Faces, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            // Default-material faces take the material of the group/component they are in.
            if (inherited != null)
            {
                for (var i = 0; i < mesh.Surfaces.Count; i++)
                {
                    var (front, back) = mesh.Surfaces[i];
                    if (front == null || back == null)
                        faces.SetSurfaceOverrideMaterial(i, FaceMaterial(front ?? inherited, back ?? inherited));
                }
            }
            parent.AddChild(faces);
        }
        if (mesh.Edges != null)
            parent.AddChild(new MeshInstance3D { Mesh = mesh.Edges, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });

        foreach (var inst in entities.Instances)
        {
            if (inst.Hidden || inst.Tag is { Visible: false } || inst.Definition.IsImage && inst.Definition.Entities.IsEmpty)
                continue;
            var node = new Node3D { Name = string.IsNullOrEmpty(inst.Name) ? inst.Definition.Name : inst.Name, Transform = ToGodot(inst.Transform) };
            parent.AddChild(node);
            AddEntities(inst.Definition.Entities, node, inst.Material ?? inherited);
        }
    }

    private DefinitionMesh MeshFor(Entities e)
    {
        if (_meshes.TryGetValue(e, out var cached))
            return cached;

        var groups = new Dictionary<(Material?, Material?), SurfaceData>();
        foreach (var face in e.Faces)
        {
            if (face.Hidden || face.Tag is { Visible: false })
                continue;
            var key = (face.FrontMaterial, face.BackMaterial);
            if (!groups.TryGetValue(key, out var data))
                groups[key] = data = new SurfaceData();
            data.AddFace(face);
        }

        ArrayMesh? faces = null;
        var surfaces = new List<(Material?, Material?)>();
        foreach (var (key, data) in groups)
        {
            if (data.Vertices.Count == 0)
                continue;
            faces ??= new ArrayMesh();
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = data.Vertices.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = data.Normals.ToArray();
            faces.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            faces.SurfaceSetMaterial(faces.GetSurfaceCount() - 1, FaceMaterial(key.Item1, key.Item2));
            surfaces.Add(key);
        }

        var lines = new List<Vector3>();
        foreach (var edge in e.Edges)
        {
            // Soft, smooth and hidden edges are not drawn (SketchUp's default style).
            if ((edge.Flags & (EdgeFlags.Soft | EdgeFlags.Hidden)) != 0 || edge.Tag is { Visible: false })
                continue;
            lines.Add(Space.ToGodot(edge.Start.Position));
            lines.Add(Space.ToGodot(edge.End.Position));
        }
        ArrayMesh? edges = null;
        if (lines.Count > 0)
        {
            edges = new ArrayMesh();
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = lines.ToArray();
            edges.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
            edges.SurfaceSetMaterial(0, _edgeMaterial);
        }

        var mesh = new DefinitionMesh(faces, surfaces, edges);
        _meshes[e] = mesh;
        return mesh;
    }

    private ShaderMaterial FaceMaterial(Material? front, Material? back)
    {
        if (_faceMaterials.TryGetValue((front, back), out var m))
            return m;
        var transparent = (front?.Opacity ?? 1) < 1 || (back?.Opacity ?? 1) < 1;
        m = new ShaderMaterial { Shader = transparent ? _faceTransparentShader : _faceShader };
        m.SetShaderParameter("front_color", ToColor(front, DefaultFront));
        m.SetShaderParameter("back_color", ToColor(back, DefaultBack));
        _faceMaterials[(front, back)] = m;
        return m;
    }

    private static Color ToColor(Material? m, Rgba fallback)
    {
        var c = m?.Color ?? fallback;
        return Color.Color8(c.R, c.G, c.B, (byte)Math.Round((m?.Opacity ?? 1) * 255));
    }

    /// <summary>Model-space transform as a Godot transform (axes swapped, mm → m).</summary>
    public static GTransform ToGodot(Transform t)
    {
        // G(p) = S(T(S⁻¹ p)): basis columns are the model transform applied to Godot's axes expressed in model space.
        Vector3 Column(Vector3 godotAxis) => Space.DirToGodot(t.ApplyVector(Space.DirFromGodot(godotAxis)));
        var basis = new Basis(Column(Vector3.Right), Column(Vector3.Up), Column(Vector3.Back));
        return new GTransform(basis, Space.ToGodot(t.Origin));
    }

    /// <summary>Triangles of one material pair, wound clockwise (Godot's front faces) with flat normals.</summary>
    private sealed class SurfaceData
    {
        public List<Vector3> Vertices { get; } = [];
        public List<Vector3> Normals { get; } = [];

        public void AddFace(Face face)
        {
            var outer = face.OuterLoop.Points.ToList();
            var holes = face.InnerLoops.Select(l => (IReadOnlyList<Vec3>)l.Points.ToList()).ToList();
            var idx = Polygon.Triangulate(outer, holes);
            if (idx.Count == 0)
                return;
            var pts = outer.Concat(holes.SelectMany(h => h)).Select(Space.ToGodot).ToArray();
            var n = Space.DirToGodot(face.Normal);
            for (var i = 0; i < idx.Count; i += 3)
            {
                // Model fronts are counter-clockwise; Godot's are clockwise.
                Vertices.Add(pts[idx[i]]);
                Vertices.Add(pts[idx[i + 2]]);
                Vertices.Add(pts[idx[i + 1]]);
                Normals.Add(n);
                Normals.Add(n);
                Normals.Add(n);
            }
        }
    }
}
