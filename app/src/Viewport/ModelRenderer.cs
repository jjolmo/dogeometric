using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;
using Godot;
using GTransform = Godot.Transform3D;
using Material = Dogeometric.Core.Modeling.Material;
using Model = Dogeometric.Core.Modeling.Model;

namespace Dogeometric.App.Viewport;

/// <summary>View › Face Style.</summary>
public enum FaceStyle
{
    XRay,
    Wireframe,
    HiddenLine,
    Shaded,
    ShadedWithTextures,
    Monochrome,
}

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
    private readonly ShaderMaterial _backEdgeMaterial = new() { Shader = GD.Load<Shader>("res://shaders/back_edge.gdshader") };

    /// <summary>View › Edge Style › Back Edges: hidden edges show dashed.</summary>
    public bool ShowBackEdges
    {
        get => _edgeMaterial.NextPass != null;
        set => _edgeMaterial.NextPass = value ? _backEdgeMaterial : null;
    }
    private readonly ShaderMaterial _guideMaterial = new() { Shader = GD.Load<Shader>("res://shaders/guide.gdshader") };

    /// <summary>View › Guides.</summary>
    public bool ShowGuides { get; set; } = true;

    /// <summary>View › Edge Style › Edges. Needs a rebuild.</summary>
    public bool ShowEdges { get; set; } = true;

    /// <summary>View › Hidden Objects: hidden groups and components show faded. Needs a rebuild.</summary>
    public bool ShowHiddenObjects { get; set; }

    /// <summary>View › Hidden Geometry: hidden faces dotted, soft/smooth/hidden edges dashed. Needs a rebuild.</summary>
    public bool ShowHiddenGeometry
    {
        get => _showHidden;
        set
        {
            _showHidden = value;
            _meshes.Clear();
        }
    }

    private bool _showHidden;
    private readonly ShaderMaterial _hiddenEdgeMaterial = new() { Shader = GD.Load<Shader>("res://shaders/hidden_edge.gdshader") };
    private readonly ShaderMaterial _hiddenFaceMaterial = HiddenFaceMaterial();

    private static ShaderMaterial HiddenFaceMaterial()
    {
        var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/selection_face.gdshader") };
        m.SetShaderParameter("color", new Color(0.45f, 0.45f, 0.45f));
        return m;
    }

    /// <summary>View › Face Style. Changing it needs a rebuild (materials are cached per style).</summary>
    public FaceStyle FaceStyle
    {
        get => _faceStyle;
        set
        {
            _faceStyle = value;
            _faceMaterials.Clear();
            _meshes.Clear();
        }
    }

    private FaceStyle _faceStyle = FaceStyle.Shaded;

    private readonly Dictionary<(Material?, Material?, bool), ShaderMaterial> _faceMaterials = [];
    private readonly Dictionary<Entities, DefinitionMesh> _meshes = [];

    /// <summary>Mesh data of one entity collection. Surfaces are keyed by (front, back) material; null = default.</summary>
    private sealed record DefinitionMesh(ArrayMesh? Faces, List<(Material? Front, Material? Back)> Surfaces, ArrayMesh? Edges, ArrayMesh? Guides, ArrayMesh? Hidden = null);

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
        AddEntities(model.Entities, root, inherited: null, mirrored: false);
    }

    /// <summary>
    /// Fades everything outside the group or component being edited (<paramref name="path"/>, empty at the top
    /// level), as SketchUp does while you edit one.
    /// </summary>
    public static void FadeOutside(Node3D root, IReadOnlyList<ComponentInstance> path, bool hideRest = false, bool hideSimilar = false)
    {
        var keys = path.Select(i => (ulong)System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(i)).ToList();
        var edited = path.Count > 0 ? (ulong)System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(path[^1].Definition) : 0UL;
        // inside: under the open instance; similar: under another copy of the edited component.
        void Walk(Node node, int depth, bool inside, bool similar, bool hiddenObject = false)
        {
            foreach (var child in node.GetChildren())
            {
                switch (child)
                {
                    case MeshInstance3D mi:
                        var outside = keys.Count > 0 && !inside;
                        mi.SetInstanceShaderParameter("fade", outside || hiddenObject ? 1f : 0f);
                        mi.Visible = !outside || (similar ? !hideSimilar : !hideRest);
                        break;
                    case Node3D n when n.HasMeta("instance"):
                        // Inside once the whole path matched; on the path while its prefix matches.
                        var onPath = !inside && depth < keys.Count && n.GetMeta("instance").AsUInt64() == keys[depth];
                        var isSimilar = similar || !onPath && !inside && keys.Count > 0 && n.GetMeta("definition").AsUInt64() == edited;
                        Walk(n, onPath ? depth + 1 : depth, inside || onPath && depth + 1 == keys.Count, isSimilar, hiddenObject || n.HasMeta("hidden"));
                        break;
                }
            }
        }
        Walk(root, 0, false, false);
    }

    private void AddEntities(Entities entities, Node3D parent, Material? inherited, bool mirrored)
    {
        var mesh = MeshFor(entities);
        if (mesh.Faces != null && FaceStyle != FaceStyle.Wireframe)
        {
            var faces = new MeshInstance3D { Mesh = mesh.Faces, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            for (var i = 0; i < mesh.Surfaces.Count; i++)
            {
                var (front, back) = mesh.Surfaces[i];
                // Default-material faces take the material of the group/component they are in.
                var f = front ?? inherited;
                var b = back ?? inherited;
                // A mirrored instance turns its triangles' winding around, so the shader sees fronts as backs:
                // swapping the colours shows each side as SketchUp does.
                if (mirrored)
                    faces.SetSurfaceOverrideMaterial(i, FaceMaterial(f, b, flipped: true));
                else if (inherited != null && (front == null || back == null))
                    faces.SetSurfaceOverrideMaterial(i, FaceMaterial(f, b));
            }
            parent.AddChild(faces);
        }
        if (mesh.Edges != null && ShowEdges)
            parent.AddChild(new MeshInstance3D { Mesh = mesh.Edges, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        if (mesh.Hidden != null)
            parent.AddChild(new MeshInstance3D { Mesh = mesh.Hidden, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        if (mesh.Guides != null && ShowGuides)
            parent.AddChild(new MeshInstance3D { Mesh = mesh.Guides, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });

        foreach (var inst in entities.Instances)
        {
            if ((inst.Hidden && !ShowHiddenObjects) || inst.Tag is { Visible: false } || inst.Definition.IsImage && inst.Definition.Entities.IsEmpty)
                continue;
            var node = new Node3D { Name = string.IsNullOrEmpty(inst.Name) ? inst.Definition.Name : inst.Name, Transform = ToGodot(inst.Transform) };
            node.SetMeta("instance", (ulong)System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(inst));
            node.SetMeta("definition", (ulong)System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(inst.Definition));
            if (inst.Hidden)
                node.SetMeta("hidden", true); // shown faded (View › Hidden Objects)
            parent.AddChild(node);
            AddEntities(inst.Definition.Entities, node, inst.Material ?? inherited, mirrored ^ inst.Transform.IsMirroring);
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

        var mesh = new DefinitionMesh(faces, surfaces, edges, GuideMesh(e), _showHidden ? HiddenMesh(e) : null);
        _meshes[e] = mesh;
        return mesh;
    }

    /// <summary>Hidden Geometry: hidden faces as a dot pattern (surface 0) and hidden or soft edges dashed (surface 1).</summary>
    private ArrayMesh? HiddenMesh(Entities e)
    {
        var data = new SurfaceData();
        foreach (var face in e.Faces.Where(f => f.Hidden && f.Tag is not { Visible: false }))
            data.AddFace(face);
        var lines = new List<Vector3>();
        foreach (var edge in e.Edges)
        {
            if ((edge.Flags & (EdgeFlags.Soft | EdgeFlags.Hidden)) == 0 || edge.Tag is { Visible: false })
                continue;
            lines.Add(Space.ToGodot(edge.Start.Position));
            lines.Add(Space.ToGodot(edge.End.Position));
        }
        if (data.Vertices.Count == 0 && lines.Count == 0)
            return null;
        var mesh = new ArrayMesh();
        if (data.Vertices.Count > 0)
        {
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = data.Vertices.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = data.Normals.ToArray();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, _hiddenFaceMaterial);
        }
        if (lines.Count > 0)
        {
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = lines.ToArray();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, _hiddenEdgeMaterial);
        }
        return mesh;
    }

    /// <summary>Guide lines (infinite ones drawn 1 km each way) and guide points as small crosses.</summary>
    private ArrayMesh? GuideMesh(Entities e)
    {
        if (e.GuideLines.Count == 0 && e.GuidePoints.Count == 0)
            return null;
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        void Add(Vec3 a, Vec3 b, float uvStart = 0)
        {
            var ga = Space.ToGodot(a);
            var gb = Space.ToGodot(b);
            verts.Add(ga);
            verts.Add(gb);
            uvs.Add(new Vector2(uvStart, 0));
            uvs.Add(new Vector2(uvStart + ga.DistanceTo(gb), 0));
        }
        // Infinite guides reach 1 km each way, in pieces growing tenfold from the point nearest the origin: a single
        // segment with both ends far off screen is dropped by some rasterisers (software Vulkan does).
        double[] steps = [-1_000_000, -100_000, -10_000, -1_000, 0, 1_000, 10_000, 100_000, 1_000_000];
        foreach (var g in e.GuideLines)
        {
            if (g.Start is { } s && g.End is { } en)
            {
                Add(s, en);
                continue;
            }
            var centre = g.Point - g.Direction * g.Point.Dot(g.Direction);
            for (var i = 0; i + 1 < steps.Length; i++)
                Add(centre + g.Direction * steps[i], centre + g.Direction * steps[i + 1], (float)((steps[i] - steps[0]) * Space.MetersPerUnit));
        }
        foreach (var p in e.GuidePoints)
        {
            const double r = 20;
            Add(p.Position - Vec3.UnitX * r, p.Position + Vec3.UnitX * r);
            Add(p.Position - Vec3.UnitY * r, p.Position + Vec3.UnitY * r);
            Add(p.Position - Vec3.UnitZ * r, p.Position + Vec3.UnitZ * r);
        }
        var mesh = new ArrayMesh();
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
        mesh.SurfaceSetMaterial(0, _guideMaterial);
        return mesh;
    }

    private ShaderMaterial FaceMaterial(Material? front, Material? back, bool flipped = false)
    {
        if (_faceMaterials.TryGetValue((front, back, flipped), out var m))
            return m;
        // Monochrome and Hidden Line ignore materials; X-ray makes every face see-through.
        if (FaceStyle is FaceStyle.Monochrome or FaceStyle.HiddenLine)
            (front, back) = (null, null);
        var xray = FaceStyle == FaceStyle.XRay;
        var transparent = xray || (front?.Opacity ?? 1) < 1 || (back?.Opacity ?? 1) < 1;
        m = new ShaderMaterial { Shader = transparent ? _faceTransparentShader : _faceShader };
        var frontColor = ToColor(front, DefaultFront);
        var backColor = ToColor(back, DefaultBack);
        if (FaceStyle == FaceStyle.HiddenLine)
            backColor = frontColor = Colors.White;
        if (xray)
        {
            frontColor.A *= 0.5f;
            backColor.A *= 0.5f;
        }
        // Mirrored instances see their triangles' fronts as backs: give each side the other's colour.
        if (flipped)
            (frontColor, backColor) = (backColor, frontColor);
        m.SetShaderParameter("front_color", frontColor);
        m.SetShaderParameter("back_color", backColor);
        // Hidden Line draws faces flat white, without shading.
        if (FaceStyle == FaceStyle.HiddenLine)
            m.SetShaderParameter("light_dir", Vector3.Zero);
        _faceMaterials[(front, back, flipped)] = m;
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
