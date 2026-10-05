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
    private readonly Shader _faceLitShader = GD.Load<Shader>("res://shaders/face_lit.gdshader");
    private readonly Shader _faceTransparentShader = GD.Load<Shader>("res://shaders/face_transparent.gdshader");
    private readonly ShaderMaterial _edgeMaterial = new() { Shader = GD.Load<Shader>("res://shaders/edge.gdshader") };
    private readonly ShaderMaterial _backEdgeMaterial = new() { Shader = GD.Load<Shader>("res://shaders/back_edge.gdshader") };

    /// <summary>View › Edge Style › Back Edges: hidden edges show dashed.</summary>
    public bool ShowBackEdges
    {
        get => _edgeMaterial.NextPass != null;
        set => _edgeMaterial.NextPass = value ? _backEdgeMaterial : null;
    }
    private readonly ShaderMaterial _profileMaterial = new() { Shader = GD.Load<Shader>("res://shaders/profile.gdshader") };

    /// <summary>View › Edge Style › Profiles: silhouettes drawn thick (SketchUp's default width, 3 pixels).</summary>
    public bool ShowProfiles
    {
        get => _showProfiles;
        set
        {
            _showProfiles = value;
            _profileMaterial.SetShaderParameter("profile_width", value ? 3f : 0f);
        }
    }

    private bool _showProfiles;

    /// <summary>View › Edge Style › Extension: edges run a few pixels past their ends. Needs a rebuild.</summary>
    public bool ShowExtension
    {
        get => _extension;
        set
        {
            _extension = value;
            _profileMaterial.SetShaderParameter("extension_px", value ? 3f : 0f);
        }
    }

    /// <summary>View › Edge Style › Depth Cue: near edges thick, far ones thin. Needs a rebuild.</summary>
    public bool ShowDepthCue
    {
        get => _depthCue;
        set
        {
            _depthCue = value;
            _profileMaterial.SetShaderParameter("depth_cue_px", value ? 4f : 0f);
        }
    }

    private bool _extension;
    private bool _depthCue;

    /// <summary>Hard edges are drawn as quads (by the profile shader) rather than lines.</summary>
    private bool QuadEdges => _extension || _depthCue;

    /// <summary>Depth Cue's range: the distances (model units) of the model's nearest and farthest points.</summary>
    public void SetDepthRange(double near, double far) =>
        _profileMaterial.SetShaderParameter("depth_range", new Vector2((float)(near * Space.MetersPerUnit), (float)(far * Space.MetersPerUnit)));
    private readonly ShaderMaterial _guideMaterial = new() { Shader = GD.Load<Shader>("res://shaders/guide.gdshader") };

    /// <summary>View › Guides.</summary>
    public bool ShowGuides { get; set; } = true;

    /// <summary>View › Edge Style › Edges. Needs a rebuild.</summary>
    public bool ShowEdges
    {
        get => _showEdges;
        set
        {
            _showEdges = value;
            _profileMaterial.SetShaderParameter("show_edges", value);
        }
    }

    private bool _showEdges = true;

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

    public ShadowSettings Shadows => _shadows;

    /// <summary>Glued instances being dragged and where they are drawn, so their openings follow them.</summary>
    public IReadOnlyDictionary<ComponentInstance, Transform>? MovingInstances { get; set; }

    /// <summary>
    /// Takes the model's shadow settings. Light and Dark update the materials in place; returns true when the
    /// geometry must be rebuilt (lit or not, shadows cast or received).
    /// </summary>
    public bool SetShadows(ShadowSettings s)
    {
        var old = _shadows;
        _shadows = s;
        if (s.Enabled != old.Enabled || s.UseSunForShading != old.UseSunForShading || s.OnFaces != old.OnFaces || s.FromEdges != old.FromEdges)
        {
            _faceMaterials.Clear();
            _meshes.Clear();
            return true;
        }
        foreach (var m in _faceMaterials.Values.Where(m => m.Shader == _faceLitShader))
        {
            m.SetShaderParameter("sun_light", s.Light / 100f);
            m.SetShaderParameter("sun_dark", s.Dark / 100f);
        }
        return false;
    }

    private ShadowSettings _shadows = new();
    private bool SunLit => _shadows.Enabled || _shadows.UseSunForShading;

    // SketchUp's default template style shows textures.
    private FaceStyle _faceStyle = FaceStyle.ShadedWithTextures;

    private readonly Dictionary<(Material?, Material?, bool), ShaderMaterial> _faceMaterials = [];
    private readonly Dictionary<Entities, DefinitionMesh> _meshes = [];

    /// <summary>Mesh data of one entity collection. Surfaces are keyed by (front, back) material; null = default.</summary>
    private sealed record DefinitionMesh(ArrayMesh? Faces, List<(Material? Front, Material? Back)> Surfaces, ArrayMesh? Edges, ArrayMesh? Guides, ArrayMesh? Hidden = null, ArrayMesh? Profiles = null);

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
            var faces = new MeshInstance3D { Mesh = mesh.Faces, CastShadow = _shadows.Enabled ? GeometryInstance3D.ShadowCastingSetting.DoubleSided : GeometryInstance3D.ShadowCastingSetting.Off };
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
        if (mesh.Profiles != null && (ShowEdges || ShowProfiles) && (FaceStyle != FaceStyle.Wireframe || QuadEdges))
            parent.AddChild(new MeshInstance3D { Mesh = mesh.Profiles, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        if (mesh.Edges != null && ShowEdges && !QuadEdges)
            parent.AddChild(new MeshInstance3D { Mesh = mesh.Edges, CastShadow = _shadows is { Enabled: true, FromEdges: true } ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off });
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
        var smooth = SmoothNormals.For(e);
        var openings = Gluing.Openings(e, MovingInstances);
        foreach (var face in e.Faces)
        {
            if (face.Hidden || face.Tag is { Visible: false })
                continue;
            var key = (face.FrontMaterial, face.BackMaterial);
            if (!groups.TryGetValue(key, out var data))
                groups[key] = data = new SurfaceData();
            data.AddFace(face, face.FrontMaterial, face.BackMaterial, smooth, openings.GetValueOrDefault(face));
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
            arrays[(int)Mesh.ArrayType.TexUV] = data.Uvs.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV2] = data.Uv2s.ToArray();
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

        var mesh = new DefinitionMesh(faces, surfaces, edges, GuideMesh(e), _showHidden ? HiddenMesh(e) : null, ProfileMesh(e));
        _meshes[e] = mesh;
        return mesh;
    }

    /// <summary>Drawn edges as quads for profile.gdshader: silhouettes, and every hard edge with Extension or Depth Cue.</summary>
    private ArrayMesh? ProfileMesh(Entities e)
    {
        var faces = new Dictionary<Edge, List<Face>>();
        foreach (var face in e.Faces)
        {
            if (face.Hidden || face.Tag is { Visible: false })
                continue;
            foreach (var edge in Topology.EdgesOf(face))
            {
                if (!faces.TryGetValue(edge, out var list))
                    faces[edge] = list = [];
                list.Add(face);
            }
        }
        var verts = new List<Vector3>();
        var custom0 = new List<float>();
        var custom1 = new List<float>();
        var custom2 = new List<float>();
        var custom3 = new List<float>();
        float[] Plane(Face f)
        {
            var n = Space.DirToGodot(f.Normal.Normalized());
            return [n.X, n.Y, n.Z, n.Dot(Space.ToGodot(f.OuterLoop.Edges[0].Edge.Start.Position))];
        }
        foreach (var edge in e.Edges)
        {
            if ((edge.Flags & EdgeFlags.Hidden) != 0 || edge.Tag is { Visible: false })
                continue;
            var list = faces.GetValueOrDefault(edge) ?? [];
            var soft = (edge.Flags & EdgeFlags.Soft) != 0 ? 1f : 0f;
            if (list.Count == 0 && soft > 0)
                continue;
            var p1 = list.Count > 0 ? Plane(list[0]) : [0, 0, 0, 0];
            var p2 = list.Count > 1 ? Plane(list[1]) : [0, 0, 0, 0];
            var a = Space.ToGodot(edge.Start.Position);
            var b = Space.ToGodot(edge.End.Position);
            void Corner(Vector3 p, Vector3 other, float side)
            {
                verts.Add(p);
                custom0.AddRange([other.X, other.Y, other.Z, side]);
                custom1.AddRange(p1);
                custom2.AddRange(p2);
                custom3.AddRange([soft, list.Count > 1 ? 1 : 0, list.Count > 0 ? 1 : 0, 0]);
            }
            // The shader measures sideways from each end towards the other, so B's sides are mirrored.
            Corner(a, b, -1);
            Corner(a, b, 1);
            Corner(b, a, -1);
            Corner(a, b, -1);
            Corner(b, a, -1);
            Corner(b, a, 1);
        }
        if (verts.Count == 0)
            return null;
        var mesh = new ArrayMesh();
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Custom0] = custom0.ToArray();
        arrays[(int)Mesh.ArrayType.Custom1] = custom1.ToArray();
        arrays[(int)Mesh.ArrayType.Custom2] = custom2.ToArray();
        arrays[(int)Mesh.ArrayType.Custom3] = custom3.ToArray();
        var rgba = (int)Mesh.ArrayCustomFormat.RgbaFloat;
        var format = (Mesh.ArrayFormat)((rgba << (int)Mesh.ArrayFormat.FormatCustom0Shift) | (rgba << (int)Mesh.ArrayFormat.FormatCustom1Shift)
            | (rgba << (int)Mesh.ArrayFormat.FormatCustom2Shift) | (rgba << (int)Mesh.ArrayFormat.FormatCustom3Shift));
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, flags: format);
        mesh.SurfaceSetMaterial(0, _profileMaterial);
        return mesh;
    }

    /// <summary>Hidden Geometry: hidden faces as a dot pattern (surface 0) and hidden or soft edges dashed (surface 1).</summary>
    private ArrayMesh? HiddenMesh(Entities e)
    {
        var data = new SurfaceData();
        foreach (var face in e.Faces.Where(f => f.Hidden && f.Tag is not { Visible: false }))
            data.AddFace(face, face.FrontMaterial, face.BackMaterial);
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
        var lit = SunLit && !transparent && FaceStyle != FaceStyle.HiddenLine;
        m = new ShaderMaterial { Shader = transparent ? _faceTransparentShader : lit ? _faceLitShader : _faceShader };
        if (lit)
        {
            m.SetShaderParameter("sun_light", _shadows.Light / 100f);
            m.SetShaderParameter("sun_dark", _shadows.Dark / 100f);
            m.SetShaderParameter("receive_shadows", _shadows.Enabled && _shadows.OnFaces);
        }
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
        // Shaded With Textures shows the pictures; the other styles keep the material's average colour.
        if (FaceStyle == FaceStyle.ShadedWithTextures || xray)
        {
            var (ft, bt) = (TextureOf(front), TextureOf(back));
            if (flipped)
                (ft, bt) = (bt, ft);
            m.SetShaderParameter("front_tex", ft);
            m.SetShaderParameter("back_tex", bt);
            m.SetShaderParameter("has_front_tex", ft != null);
            m.SetShaderParameter("has_back_tex", bt != null);
            m.SetShaderParameter("swap_uv", flipped);
        }
        // Hidden Line draws faces flat white, without shading.
        if (FaceStyle == FaceStyle.HiddenLine)
            m.SetShaderParameter("light_dir", Vector3.Zero);
        _faceMaterials[(front, back, flipped)] = m;
        return m;
    }

    private readonly Dictionary<Material, Texture2D?> _textures = [];

    /// <summary>The material's picture as a Godot texture (PNG, JPEG, BMP, WebP), decoded once.</summary>
    private Texture2D? TextureOf(Material? m)
    {
        if (m?.Texture is not { Data.Length: > 0 } tex)
            return null;
        if (_textures.TryGetValue(m, out var cached))
            return cached;
        Texture2D? texture = null;
        if (TextureImages.Decode(tex.Data) is { } image)
        {
            image.GenerateMipmaps();
            texture = ImageTexture.CreateFromImage(image);
        }
        _textures[m] = texture;
        return texture;
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

    /// <summary>
    /// SketchUp's smoothing: at a corner, the normal averages every face reached from this one across smooth edges
    /// meeting at that vertex, so smoothed faces shade as one curved surface.
    /// </summary>
    private sealed class SmoothNormals
    {
        private readonly Dictionary<Edge, List<Face>> _facesOf = [];

        /// <summary>Null when nothing in <paramref name="e"/> is smoothed (flat shading, no extra work).</summary>
        public static SmoothNormals? For(Entities e)
        {
            if (!e.Edges.Any(x => x.Flags.HasFlag(EdgeFlags.Smooth)))
                return null;
            var s = new SmoothNormals();
            foreach (var f in e.Faces)
                foreach (var edge in Topology.EdgesOf(f))
                {
                    if (!s._facesOf.TryGetValue(edge, out var list))
                        s._facesOf[edge] = list = [];
                    list.Add(f);
                }
            return s;
        }

        public Vec3? At(Face face, Vertex v)
        {
            var normal = face.Normal.Normalized();
            var sum = normal;
            var seen = new HashSet<Face> { face };
            var stack = new Stack<Face>([face]);
            while (stack.Count > 0)
            {
                var f = stack.Pop();
                foreach (var edge in Topology.EdgesOf(f))
                {
                    if (!edge.Flags.HasFlag(EdgeFlags.Smooth) || (edge.Start != v && edge.End != v))
                        continue;
                    foreach (var g in _facesOf.GetValueOrDefault(edge) ?? [])
                        if (seen.Add(g))
                        {
                            var n = g.Normal.Normalized();
                            // A neighbour wound the other way still bends the same surface.
                            sum += n.Dot(normal) < 0 ? -n : n;
                            stack.Push(g);
                        }
                }
            }
            return seen.Count > 1 && sum.Length > 1e-9 ? sum.Normalized() : null;
        }
    }

    /// <summary>Triangles of one material pair, wound clockwise (Godot's front faces); flat normals unless smoothed.</summary>
    private sealed class SurfaceData
    {
        public List<Vector3> Vertices { get; } = [];
        public List<Vector3> Normals { get; } = [];
        public List<Vector2> Uvs { get; } = [];  // front side's texture coordinates
        public List<Vector2> Uv2s { get; } = []; // back side's

        public void AddFace(Face face, Material? front, Material? back, SmoothNormals? smooth = null, List<List<Vec3>>? openings = null)
        {
            var outer = face.OuterLoop.Points.ToList();
            var holes = face.InnerLoops.Select(l => (IReadOnlyList<Vec3>)l.Points.ToList())
                .Concat(openings ?? []).ToList();
            var corners = face.Loops.SelectMany(l => l.Vertices).ToArray();
            var idx = Polygon.Triangulate(outer, holes);
            if (idx.Count == 0)
                return;
            var pts = outer.Concat(holes.SelectMany(h => h)).Select(Space.ToGodot).ToArray();
            var n = Space.DirToGodot(face.Normal);
            var model = outer.Concat(holes.SelectMany(h => h)).ToArray();
            // Texture coordinates as SketchUp computes them; V flips because images run top-down in Godot.
            Vector2 Uv(Material? m, bool backSide, Vec3 p)
            {
                if (m?.Texture == null)
                    return Vector2.Zero;
                var (u, v) = Texturing.Uv(face, backSide, p, m);
                return new Vector2((float)u, (float)-v);
            }
            for (var i = 0; i < idx.Count; i += 3)
            {
                // Model fronts are counter-clockwise; Godot's are clockwise.
                foreach (var k in new[] { idx[i], idx[i + 2], idx[i + 1] })
                {
                    Vertices.Add(pts[k]);
                    Normals.Add(k < corners.Length && smooth?.At(face, corners[k]) is { } sn ? Space.DirToGodot(sn) : n);
                    Uvs.Add(Uv(front, false, model[k]));
                    Uv2s.Add(Uv(back, true, model[k]));
                }
            }
        }
    }
}
