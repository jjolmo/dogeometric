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
    private StyleSettings _style = new();
    private Rgba DefaultFront => _style.FrontColor;
    private Rgba DefaultBack => _style.BackColor;
    private Transform _axes = Transform.Identity;

    public StyleSettings Style => _style;

    /// <summary>Applies the model's style; returns true when the meshes must be built again (edge or face colours).</summary>
    public bool SetStyle(StyleSettings s, Transform axes)
    {
        var old = _style;
        _style = s;
        _axes = axes;
        _profileMaterial.SetShaderParameter("profile_width", _showProfiles ? (float)s.ProfileWidth : 0f);
        _profileMaterial.SetShaderParameter("extension_px", _extension ? (float)s.ExtensionLength : 0f);
        _profileMaterial.SetShaderParameter("depth_cue_px", _depthCue ? (float)s.DepthCueWidth : 0f);
        var edge = Color.Color8(s.EdgeColor.R, s.EdgeColor.G, s.EdgeColor.B);
        _edgeMaterial.SetShaderParameter("color", edge);
        _edgeMaterial.SetShaderParameter("vertex_colors", s.EdgeColorMode != EdgeColorMode.AllSame);
        _profileMaterial.SetShaderParameter("color", edge);
        foreach (var m in _endpointMaterials.Concat(_jitterMaterials))
        {
            m.SetShaderParameter("color", edge);
            m.SetShaderParameter("endpoint_px", (float)s.EndpointLength);
        }
        var rebuild = old.XrayOpacity != s.XrayOpacity || old.Transparency != s.Transparency || old.TransparencyQuality != s.TransparencyQuality
            || old.Dashes != s.Dashes || old.Endpoints != s.Endpoints || old.Jitter != s.Jitter || old.EdgeColorMode != s.EdgeColorMode || old.FrontColor != s.FrontColor || old.BackColor != s.BackColor
            || s.EdgeColorMode != EdgeColorMode.AllSame && old.EdgeColor != s.EdgeColor;
        if (rebuild)
            ForgetMaterials();
        return rebuild;
    }

    /// <summary>An edge's colour by the style: its material's, or its axis colour, else the style's edge colour.</summary>
    private Color EdgeColorOf(Edge edge)
    {
        var fallback = Color.Color8(_style.EdgeColor.R, _style.EdgeColor.G, _style.EdgeColor.B);
        if (_style.EdgeColorMode == EdgeColorMode.ByMaterial)
            return edge.Material is { } m ? Color.Color8(m.Color.R, m.Color.G, m.Color.B) : fallback;
        var d = (edge.End.Position - edge.Start.Position).Normalized();
        var p = UI.AppPreferences.Current;
        return Math.Abs(d.Dot(_axes.X.Normalized())) > 0.9999 ? p.RedAxis
            : Math.Abs(d.Dot(_axes.Y.Normalized())) > 0.9999 ? p.GreenAxis
            : Math.Abs(d.Dot(_axes.Z.Normalized())) > 0.9999 ? p.BlueAxis : fallback;
    }

    private readonly Shader _faceShader = GD.Load<Shader>("res://shaders/face.gdshader");
    private readonly Shader _faceLitShader = GD.Load<Shader>("res://shaders/face_lit.gdshader");
    private readonly Shader _faceTransparentShader = GD.Load<Shader>("res://shaders/face_transparent.gdshader");
    private readonly Shader _faceNicerShader = GD.Load<Shader>("res://shaders/face_transparent_nicer.gdshader");
    private readonly Shader _faceNearShader = GD.Load<Shader>("res://shaders/face_transparent_near.gdshader");
    private readonly ShaderMaterial _edgeMaterial = new() { Shader = GD.Load<Shader>("res://shaders/edge.gdshader") };
    private readonly ShaderMaterial _backEdgeMaterial = new() { Shader = GD.Load<Shader>("res://shaders/back_edge.gdshader") };

    /// <summary>View › Edge Style › Back Edges: hidden edges show dashed.</summary>
    public bool ShowBackEdges
    {
        get => _edgeMaterial.NextPass != null;
        set => _edgeMaterial.NextPass = value ? _backEdgeMaterial : null;
    }
    private readonly ShaderMaterial _profileMaterial = new() { Shader = GD.Load<Shader>("res://shaders/profile.gdshader") };
    private static readonly Shader EdgeEffectShader = GD.Load<Shader>("res://shaders/edge_effect.gdshader");
    private readonly ShaderMaterial[] _endpointMaterials = [EffectMaterial(0, 0), EffectMaterial(0, 1)];
    private readonly ShaderMaterial[] _jitterMaterials = [EffectMaterial(1, 0), EffectMaterial(1, 1)];

    private static ShaderMaterial EffectMaterial(int effect, int pass)
    {
        var m = new ShaderMaterial { Shader = EdgeEffectShader };
        m.SetShaderParameter("effect", effect);
        m.SetShaderParameter("pass", pass);
        return m;
    }

    /// <summary>View › Edge Style › Profiles: silhouettes drawn thick (SketchUp's default width, 3 pixels).</summary>
    public bool ShowProfiles
    {
        get => _showProfiles;
        set
        {
            _showProfiles = value;
            _profileMaterial.SetShaderParameter("profile_width", value ? (float)_style.ProfileWidth : 0f);
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
            _profileMaterial.SetShaderParameter("extension_px", value ? (float)_style.ExtensionLength : 0f);
        }
    }

    /// <summary>View › Edge Style › Depth Cue: near edges thick, far ones thin. Needs a rebuild.</summary>
    public bool ShowDepthCue
    {
        get => _depthCue;
        set
        {
            _depthCue = value;
            _profileMaterial.SetShaderParameter("depth_cue_px", value ? (float)_style.DepthCueWidth : 0f);
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

    /// <summary>Model Info › Rendering › Use anti-aliased textures.</summary>
    public bool SmoothTextures
    {
        get => _smoothTextures;
        set
        {
            _smoothTextures = value;
            foreach (var m in _faceMaterials.Values)
                m.SetShaderParameter("smooth_textures", value);
        }
    }

    private bool _smoothTextures = true;

    /// <summary>The camera being looked through (Advanced Camera Tools): not drawn, since the view is from inside it.</summary>
    public ComponentInstance? LookingThrough { get; set; }

    /// <summary>The group or component open for editing: a subdivided one shows its control mesh as a cage.</summary>
    public Entities? Edited { get; set; }

    /// <summary>SUbD's Subdivision On/Off: when off, subdivided meshes show as their control mesh.</summary>
    public bool Subdivide { get; set; } = true;

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
    /// <summary>Tags › Color by tag: a material standing for each tag's colour.</summary>
    private readonly Dictionary<Tag, Material> _tagMaterials = [];
    private bool _colorByTag;
    private string _tagDashes = "";
    private Tag? _untagged;

    private Material? TagMaterial(Tag? tag)
    {
        if (tag == null || tag == _untagged)
            return null;
        if (!_tagMaterials.TryGetValue(tag, out var m) || m.Color != tag.Color)
            _tagMaterials[tag] = m = new Material { Name = tag.Name, Color = tag.Color };
        return m;
    }

    public void Build(Model model, Node3D root, IEnumerable<Entities>? changed = null)
    {
        if (_colorByTag != model.Options.ColorByTag)
        {
            _colorByTag = model.Options.ColorByTag;
            changed = null;
        }
        _untagged = model.UntaggedTag;
        var dashes = string.Join(",", model.Tags.Select(t => (int)t.Dashes));
        if (_tagDashes != dashes)
        {
            _tagDashes = dashes;
            changed = null;
        }
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
        AddEntities(model.Entities, root, inherited: _colorByTag ? TagMaterial(model.UntaggedTag) ?? new Material { Color = model.UntaggedTag.Color } : null, mirrored: false);
    }

    /// <summary>
    /// Fades everything outside the group or component being edited (<paramref name="path"/>, empty at the top
    /// level), as SketchUp does while you edit one.
    /// </summary>
    public static void FadeOutside(Node3D root, IReadOnlyList<ComponentInstance> path, bool hideRest = false, bool hideSimilar = false,
        float fadeRest = 0.6f, float fadeSimilar = 0.6f)
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
                        mi.SetInstanceShaderParameter("fade", hiddenObject ? 0.6f : outside ? similar ? fadeSimilar : fadeRest : 0f);
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
        if (mesh.Profiles != null && ShowEdges && !QuadEdges)
            foreach (var (on, materials) in new[] { (_style.Endpoints, _endpointMaterials), (_style.Jitter, _jitterMaterials) })
                foreach (var material in on ? materials : [])
                    parent.AddChild(new MeshInstance3D { Mesh = mesh.Profiles, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        // Jitter's strokes stand in for the edges.
        if (mesh.Edges != null && ShowEdges && !QuadEdges && !_style.Jitter)
            parent.AddChild(new MeshInstance3D { Mesh = mesh.Edges, CastShadow = _shadows is { Enabled: true, FromEdges: true } ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off });
        if (mesh.Hidden != null)
            parent.AddChild(new MeshInstance3D { Mesh = mesh.Hidden, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        if (mesh.Guides != null && ShowGuides)
            parent.AddChild(new MeshInstance3D { Mesh = mesh.Guides, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });

        foreach (var inst in entities.Instances)
        {
            if ((inst.Hidden && !ShowHiddenObjects) || inst.Tag is { Visible: false } || inst.Definition.IsImage && inst.Definition.Entities.IsEmpty
                || inst == LookingThrough)
                continue;
            var node = new Node3D { Name = string.IsNullOrEmpty(inst.Name) ? inst.Definition.Name : inst.Name, Transform = ToGodot(inst.Transform) };
            node.SetMeta("instance", (ulong)System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(inst));
            node.SetMeta("definition", (ulong)System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(inst.Definition));
            if (inst.Hidden)
                node.SetMeta("hidden", true); // shown faded (View › Hidden Objects)
            parent.AddChild(node);
            AddEntities(inst.Definition.Entities, node, _colorByTag ? TagMaterial(inst.Tag) ?? inherited : inst.Material ?? inherited, mirrored ^ inst.Transform.IsMirroring);
        }
    }

    private DefinitionMesh MeshFor(Entities e)
    {
        if (_meshes.TryGetValue(e, out var cached))
            return cached;
        if (e.Subdivision > 0 && Subdivide && e.Faces.Count > 0)
            return _meshes[e] = SubdividedMesh(e);

        var groups = new Dictionary<(Material?, Material?), SurfaceData>();
        var smooth = SmoothNormals.For(e);
        var openings = Gluing.Openings(e, MovingInstances);
        foreach (var face in e.Faces)
        {
            if (face.Hidden || face.Tag is { Visible: false })
                continue;
            var key = _colorByTag ? (TagMaterial(face.Tag), TagMaterial(face.Tag)) : (face.FrontMaterial, face.BackMaterial);
            if (!groups.TryGetValue(key, out var data))
                groups[key] = data = new SurfaceData();
            data.AddFace(face, key.Item1, key.Item2, smooth, openings.GetValueOrDefault(face));
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
        var colors = new List<Color>();
        var dashes = new List<Vector2>();
        var starts = new List<float>();
        var dashed = _style.Dashes && e.Edges.Any(x => x.Tag is { Dashes: not LineStyle.Solid });
        foreach (var edge in e.Edges)
        {
            // Soft, smooth and hidden edges are not drawn (SketchUp's default style).
            if ((edge.Flags & (EdgeFlags.Soft | EdgeFlags.Hidden)) != 0 || edge.Tag is { Visible: false })
                continue;
            var start = Space.ToGodot(edge.Start.Position);
            lines.Add(start);
            lines.Add(Space.ToGodot(edge.End.Position));
            if (dashed)
            {
                var style = new Vector2((float)(edge.Tag?.Dashes ?? LineStyle.Solid), 0);
                dashes.AddRange([style, style]);
                starts.AddRange([start.X, start.Y, start.Z, 0, start.X, start.Y, start.Z, 0]);
            }
            if (_style.EdgeColorMode != EdgeColorMode.AllSame)
            {
                var c = EdgeColorOf(edge);
                colors.Add(c);
                colors.Add(c);
            }
        }
        ArrayMesh? edges = null;
        if (lines.Count > 0)
        {
            edges = new ArrayMesh();
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = lines.ToArray();
            if (colors.Count > 0)
                arrays[(int)Mesh.ArrayType.Color] = colors.ToArray();
            var format = (Mesh.ArrayFormat)0;
            if (dashed)
            {
                arrays[(int)Mesh.ArrayType.TexUV] = dashes.ToArray();
                arrays[(int)Mesh.ArrayType.Custom0] = starts.ToArray();
                format = (Mesh.ArrayFormat)((int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift);
            }
            edges.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays, flags: format);
            edges.SurfaceSetMaterial(0, _edgeMaterial);
        }

        var mesh = new DefinitionMesh(faces, surfaces, edges, GuideMesh(e), _showHidden ? HiddenMesh(e) : null, ProfileMesh(e));
        _meshes[e] = mesh;
        return mesh;
    }

    /// <summary>SUbD: a control mesh's smooth surface with its creases and borders as edges, or with the whole control
    /// mesh as a cage while it is open for editing.</summary>
    private DefinitionMesh SubdividedMesh(Entities e)
    {
        var (points, polygons, hard) = CatmullClark.Mesh(e, e.Subdivision);
        var normals = polygons.Select(p => Polygon.Normal(p.Corners.Select(i => points[i]).ToList()).Normalized()).ToList();
        var around = new Dictionary<int, List<int>>();
        for (var f = 0; f < polygons.Count; f++)
            foreach (var i in polygons[f].Corners)
            {
                if (!around.TryGetValue(i, out var list))
                    around[i] = list = [];
                list.Add(f);
            }
        // Neighbours bent less than this share a normal; creases are much sharper.
        const double smoothCos = 0.5;
        var groups = new Dictionary<(Material?, Material?), SurfaceData>();
        for (var f = 0; f < polygons.Count; f++)
        {
            var (corners, src) = polygons[f];
            if (src.Hidden || src.Tag is { Visible: false })
                continue;
            var key = (src.FrontMaterial, src.BackMaterial);
            if (!groups.TryGetValue(key, out var data))
                groups[key] = data = new SurfaceData();
            var n = normals[f];
            Vector3 Normal(int i) => Space.DirToGodot(around[i].Where(g => normals[g].Dot(n) > smoothCos).Aggregate(Vec3.Zero, (a, g) => a + normals[g]).Normalized());
            for (var k = 1; k + 1 < corners.Length; k++)
                foreach (var i in new[] { corners[0], corners[k + 1], corners[k] })
                {
                    data.Vertices.Add(Space.ToGodot(points[i]));
                    data.Normals.Add(Normal(i));
                    data.Uvs.Add(Vector2.Zero);
                    data.Uv2s.Add(Vector2.Zero);
                }
        }
        ArrayMesh? faces = null;
        var surfaces = new List<(Material?, Material?)>();
        foreach (var (key, data) in groups)
        {
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
        if (Edited == e)
            foreach (var edge in e.Edges.Where(x => x.Tag is not { Visible: false }))
            {
                lines.Add(Space.ToGodot(edge.Start.Position));
                lines.Add(Space.ToGodot(edge.End.Position));
            }
        else
        {
            var uses = new Dictionary<(int, int), int>();
            foreach (var (corners, _) in polygons)
                for (var k = 0; k < corners.Length; k++)
                {
                    var (a, b) = (corners[k], corners[(k + 1) % corners.Length]);
                    var key = a < b ? (a, b) : (b, a);
                    uses[key] = uses.GetValueOrDefault(key) + 1;
                }
            foreach (var ((a, b), count) in uses)
                if (count == 1 || hard.Contains((a, b)))
                {
                    lines.Add(Space.ToGodot(points[a]));
                    lines.Add(Space.ToGodot(points[b]));
                }
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
        return new DefinitionMesh(faces, surfaces, edges, GuideMesh(e), _showHidden ? HiddenMesh(e) : null);
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
                custom3.AddRange([soft, list.Count > 1 ? 1 : 0, list.Count > 0 ? 1 : 0, p == b ? 1 : 0]);
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
        var seeThrough = _style.Transparency && ((front?.Opacity ?? 1) < 1 || (back?.Opacity ?? 1) < 1);
        var transparent = xray || seeThrough;
        var lit = SunLit && !transparent && FaceStyle != FaceStyle.HiddenLine;
        var transparentShader = _style.TransparencyQuality == TransparencyQuality.Nicer ? _faceNicerShader : _faceTransparentShader;
        m = new ShaderMaterial { Shader = transparent ? transparentShader : lit ? _faceLitShader : _faceShader };
        var parameters = new List<(StringName, Variant)>();
        void Set(StringName name, Variant value)
        {
            m.SetShaderParameter(name, value);
            parameters.Add((name, value));
        }
        if (lit)
        {
            Set("sun_light", _shadows.Light / 100f);
            Set("sun_dark", _shadows.Dark / 100f);
            Set("receive_shadows", _shadows.Enabled && _shadows.OnFaces);
        }
        var frontColor = ToColor(front, DefaultFront);
        var backColor = ToColor(back, DefaultBack);
        if (FaceStyle == FaceStyle.HiddenLine)
            backColor = frontColor = Colors.White;
        if (!seeThrough)
            frontColor.A = backColor.A = 1;
        if (xray)
        {
            frontColor.A *= (float)_style.XrayOpacity;
            backColor.A *= (float)_style.XrayOpacity;
        }
        // Mirrored instances see their triangles' fronts as backs: give each side the other's colour.
        if (flipped)
            (frontColor, backColor) = (backColor, frontColor);
        Set("front_color", frontColor);
        Set("back_color", backColor);
        Set("smooth_textures", SmoothTextures);
        // Shaded With Textures shows the pictures; the other styles keep the material's average colour.
        if (FaceStyle == FaceStyle.ShadedWithTextures || xray)
        {
            var (ft, bt) = (TextureOf(front), TextureOf(back));
            if (flipped)
                (ft, bt) = (bt, ft);
            Set("front_tex", ft);
            Set("back_tex", bt);
            Set("has_front_tex", ft != null);
            Set("has_back_tex", bt != null);
            Set("swap_uv", flipped);
        }
        // Hidden Line draws faces flat white, without shading.
        if (FaceStyle == FaceStyle.HiddenLine)
            Set("light_dir", Vector3.Zero);
        if (m.Shader == _faceNicerShader)
        {
            var near = new ShaderMaterial { Shader = _faceNearShader, RenderPriority = 1 };
            foreach (var (name, value) in parameters)
                near.SetShaderParameter(name, value);
            m.NextPass = near;
        }
        _faceMaterials[(front, back, flipped)] = m;
        return m;
    }

    private readonly Dictionary<TextureImage, Texture2D?> _textures = [];

    /// <summary>Drops cached materials and textures, after a material's colour or picture changed.</summary>
    public void ForgetMaterials()
    {
        _faceMaterials.Clear();
        _textures.Clear();
        _meshes.Clear();
    }

    /// <summary>The material's picture as a Godot texture (PNG, JPEG, BMP, WebP), decoded once.</summary>
    private Texture2D? TextureOf(Material? m)
    {
        if (m?.Texture is not { Data.Length: > 0 } tex)
            return null;
        if (_textures.TryGetValue(tex, out var cached))
            return cached;
        Texture2D? texture = null;
        if (TextureImages.Decode(tex.Data) is { } image)
        {
            image.GenerateMipmaps();
            texture = ImageTexture.CreateFromImage(image);
        }
        _textures[tex] = texture;
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
