using System.Threading.Tasks;
using Dogeometric.App.Tools;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Picking;
using Dogeometric.Core.View;
using Godot;

namespace Dogeometric.App.Viewport;

/// <summary>
/// The drawing area: hosts the 3D world, owns the model camera and routes input. Middle-button orbit/pan,
/// wheel zoom and middle double-click re-centre work on top of any tool, as in SketchUp.
/// </summary>
public partial class ModelViewport : Control
{
    // Measured on SketchUp 2021: 300 px of drag = 21.8° of yaw, 60 px = 4.3° of pitch (≈0.072°/px both ways).
    private const double OrbitRadiansPerPixel = 0.072 * Math.PI / 180;
    private const double WheelZoomFactor = 1.25;
    // Wheel notches closer together than this belong to one camera-history step.
    private const ulong WheelGestureMs = 600;
    // Parallel projection still needs an eye in front of the model; push it this far back (mm).
    private const double ParallelEyeBackoff = 100_000;

    private SubViewport _subViewport = null!;
    private Camera3D _camera = null!;
    private ShaderMaterial _skyMaterial = null!;
    private Control _overlay = null!;

    private Vec3 _navPivot;
    private double _navDepth;
    private bool _middleDragging;
    private ulong _lastWheelMs;

    // Fitted to SketchUp 2021's new-model view (see docs): same horizon, axis directions and scale.
    public ViewCamera Camera { get; } = new(new Vec3(5449, -5345, 2310), new Vec3(1209, 1001, 830));
    public CameraHistory History { get; } = new();
    public ToolManager Tools { get; private set; } = null!;
    public Node3D ModelRoot { get; private set; } = null!;
    public Node3D SelectionRoot { get; private set; } = null!;

    /// <summary>The open document (geometry, selection, context). Set by the document controller.</summary>
    public Document? Document { get; set; }

    /// <summary>Edge pick radius in pixels, like SketchUp's.</summary>
    public const double PickRadiusPixels = 5;
    public AxesRenderer Axes { get; private set; } = null!;

    /// <summary>Bounds of the model contents, used by Zoom Extents.</summary>
    public Func<Bounds3> ModelBounds { get; set; } = () => Bounds3.Empty;

    /// <summary>Redraws a collection with glued instances' openings at their dragged placement (null: back to normal).</summary>
    public Action<Entities, IReadOnlyDictionary<ComponentInstance, Dogeometric.Core.Geometry.Transform>?> PreviewOpenings { get; set; } = (_, _) => { };

    /// <summary>Centre points shown and snapped to (View › Center Points); empty when off.</summary>
    public Func<IReadOnlyList<Core.Inference.CenterPoint>> CenterPoints { get; set; } = () => [];

    /// <summary>Text being typed into the Measurements box (empty when not typing).</summary>
    public string VcbTyping { get; private set; } = "";

    /// <summary>Raised when the Measurements box text should change (typing or tool feedback).</summary>
    public event Action<string>? VcbTextChanged;

    /// <summary>A one-off message for the status bar (results such as an area).</summary>
    public event Action<string>? HintRequested;

    public void ShowHint(string text) => HintRequested?.Invoke(text);

    private static Godot.Viewport.Msaa MsaaFor(int samples) => samples switch
    {
        >= 8 => Godot.Viewport.Msaa.Msaa8X,
        >= 4 => Godot.Viewport.Msaa.Msaa4X,
        >= 2 => Godot.Viewport.Msaa.Msaa2X,
        _ => Godot.Viewport.Msaa.Disabled,
    };

    public void ShowVcbValue(string value)
    {
        if (VcbTyping.Length == 0)
            VcbTextChanged?.Invoke(value);
    }

    /// <summary>Right-click with the Select tool: the window shows SketchUp's context menu here.</summary>
    public event Action<Vector2>? ContextMenuRequested;

    public void RequestContextMenu(Vector2 position) => ContextMenuRequested?.Invoke(position);

    /// <summary>Raised after any camera change (menus show projection state).</summary>
    public event Action? CameraChanged;

    public override void _Ready()
    {
        FocusMode = FocusModeEnum.Click;
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;

        var container = new SubViewportContainer { Stretch = true, MouseFilter = MouseFilterEnum.Ignore };
        container.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(container);

        _subViewport = new SubViewport
        {
            Msaa3D = MsaaFor(UI.AppPreferences.Current.Antialiasing),
            HandleInputLocally = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        container.AddChild(_subViewport);
        UI.AppPreferences.Changed += () => _subViewport.Msaa3D = MsaaFor(UI.AppPreferences.Current.Antialiasing);
        BuildWorld();

        // Overlay watermarks and annotations live in the 3D view's viewport, so its pictures include them.
        _watermarkFront = new TextureRect { MouseFilter = MouseFilterEnum.Ignore, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale };
        _watermarkFront.SetAnchorsPreset(LayoutPreset.FullRect);
        _subViewport.AddChild(_watermarkFront);
        _annotations = new AnnotationCanvas { View = this, MouseFilter = MouseFilterEnum.Ignore };
        _annotations.SetAnchorsPreset(LayoutPreset.FullRect);
        _subViewport.AddChild(_annotations);

        _overlay = new OverlayCanvas { View = this, MouseFilter = MouseFilterEnum.Ignore };
        _overlay.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_overlay);

        Tools = new ToolManager(this, new SelectTool());
        Tools.Changed += UpdateCursor;
        SyncCamera();
    }

    private void BuildWorld()
    {
        var root = new Node3D { Name = "World" };
        _subViewport.AddChild(root);

        _skyMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/sky.gdshader") };
        var sky = new Sky { SkyMaterial = _skyMaterial };
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = sky,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(1, 1, 1),
            AmbientLightEnergy = 0.45f,
            FogLightColor = new Color(0.74f, 0.76f, 0.79f),
            TonemapMode = Godot.Environment.ToneMapper.Linear,
        };
        root.AddChild(new WorldEnvironment { Environment = env });
        _environment = env;

        _camera = new Camera3D { Current = true };
        root.AddChild(_camera);

        Axes = new AxesRenderer();
        root.AddChild(Axes);

        ModelRoot = new Node3D { Name = "Model" };
        root.AddChild(ModelRoot);

        _sun = new DirectionalLight3D
        {
            Visible = false,
            LightEnergy = 1,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal,
            ShadowBias = 0.02f,
            ShadowNormalBias = 1f,
        };
        root.AddChild(_sun);
        var groundMesh = new PlaneMesh { Size = new Vector2(1, 1) };
        _ground = new MeshInstance3D
        {
            Mesh = groundMesh,
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/ground_shadow.gdshader") },
        };
        root.AddChild(_ground);
        SelectionRoot = new Node3D { Name = "Selection" };
        root.AddChild(SelectionRoot);
    }

    // ---------------------------------------------------------------- camera operations (used by tools/commands)

    public void BeginNavigation() => History.Record(Camera.Save());

    public void BeginOrbit(Vector2 screen)
    {
        BeginNavigation();
        _navPivot = PickGeometry(screen) ?? Camera.Target;
        _navDepth = 0;
    }

    public void BeginPan(Vector2 screen)
    {
        BeginNavigation();
        var anchor = PickGeometry(screen) ?? Camera.Target;
        _navDepth = Math.Max(Camera.DepthOf(anchor), 1);
    }

    public void OrbitBy(Vector2 relative, bool gravity)
    {
        Camera.Orbit(_navPivot, -relative.X * OrbitRadiansPerPixel, -relative.Y * OrbitRadiansPerPixel, gravity);
        SyncCamera();
    }

    public void PanBy(Vector2 relative)
    {
        if (_navDepth <= 0)
            _navDepth = Math.Max(Camera.Distance, 1);
        Camera.Pan(relative.X, relative.Y, Size.Y, _navDepth);
        SyncCamera();
    }

    public void ZoomAt(Vec3 anchor, double factor)
    {
        Camera.ZoomAt(anchor, factor);
        SyncCamera();
    }

    /// <summary>Applies a camera change from a tool and updates the view.</summary>
    public void ChangeCamera(Action<ViewCamera> change)
    {
        change(Camera);
        SyncCamera();
    }

    public void ChangeFovBy(double degrees)
    {
        Camera.SetFov(Camera.FovDegrees + degrees);
        SyncCamera();
    }

    public void ZoomExtents()
    {
        BeginNavigation();
        var bounds = ModelBounds();
        // An empty model frames a person-sized box at the origin, like a new SketchUp model.
        if (bounds.IsEmpty)
            bounds = new Bounds3(new Vec3(-500, -500, 0), new Vec3(500, 500, 1800));
        Camera.ZoomExtents(bounds, Aspect);
        SyncCamera();
    }

    /// <summary>Zoom Window: the part of the view inside <paramref name="rect"/> fills the view.</summary>
    public void ZoomWindow(Rect2 rect)
    {
        if (rect.Size.X < 2 || rect.Size.Y < 2)
            return;
        BeginNavigation();
        var centre = rect.GetCenter();
        var (origin, direction) = ScreenRay(centre);
        var dir = Camera.Direction;
        // Zoom onto what's under the window's centre, or onto the current target depth.
        var depth = PickGeometry(centre) is { } hit ? (hit - Camera.Eye).Dot(dir) : Camera.DepthOf(Camera.Target);
        var along = direction.Dot(dir);
        var point = Camera.Perspective && along > 1e-9 ? origin + direction * (depth / along) : origin + direction * Math.Max(depth, 1);
        var factor = Math.Max(rect.Size.X / Size.X, rect.Size.Y / Size.Y);
        if (Camera.Perspective)
        {
            Camera.Set(point - dir * Math.Max(depth * factor, 1), point, Camera.Up);
        }
        else
        {
            Camera.Set(point - dir * Camera.Distance, point, Camera.Up);
            Camera.OrthoHeight *= factor;
        }
        SyncCamera();
    }

    /// <summary>Frames <paramref name="bounds"/> keeping the view direction (Zoom Selection, Align View).</summary>
    public void ZoomToBounds(Bounds3 bounds)
    {
        Camera.ZoomExtents(bounds, Aspect);
        SyncCamera();
    }

    /// <summary>Width/height of the drawing area; the window's when the control has no size yet (start-up).</summary>
    private double Aspect => Size.X > 1 && Size.Y > 1
        ? Size.X / Size.Y
        : GetWindow().Size.X / (double)Math.Max(GetWindow().Size.Y, 1);

    public void SetStandardView(StandardView view)
    {
        BeginNavigation();
        Camera.SetStandardView(view);
        SyncCamera();
    }

    /// <summary>Camera › Two-Point Perspective.</summary>
    public void SetTwoPoint()
    {
        BeginNavigation();
        if (!Camera.Perspective)
            SetPerspective(true);
        if (Camera.SetTwoPoint())
            SyncCamera();
    }

    public void SetPerspective(bool perspective)
    {
        if (Camera.TwoPointShift != null && perspective)
        {
            Camera.ClearTwoPoint();
            SyncCamera();
            return;
        }
        Camera.ClearTwoPoint();
        if (Camera.Perspective == perspective)
            return;
        BeginNavigation();
        // Keep the apparent size at the target when switching projection.
        if (!perspective)
            Camera.OrthoHeight = 2 * Camera.Distance * Math.Tan(Camera.HalfFovRadians);
        else
            Camera.Set(Camera.Target - Camera.Direction * (Camera.OrthoHeight / (2 * Math.Tan(Camera.HalfFovRadians))), Camera.Target, Camera.Up);
        Camera.Perspective = perspective;
        SyncCamera();
    }

    public void PreviousCamera()
    {
        if (History.Back(Camera.Save()) is { } state)
            ApplyState(state);
    }

    public void NextCamera()
    {
        if (History.Forward(Camera.Save()) is { } state)
            ApplyState(state);
    }

    private void ApplyState(CameraState state)
    {
        Camera.Restore(state);
        SyncCamera();
    }

    private void SyncCamera()
    {
        if (_camera == null)
            return;
        if (LockedCamera is { } locked && !locked.Matches(Camera.Save()))
            Camera.Restore(locked);

        var dir = Camera.Direction;
        var eye = Camera.Perspective ? Camera.Eye : Camera.Target - dir * (Camera.Distance + ParallelEyeBackoff);
        _camera.LookAtFromPosition(Space.ToGodot(eye), Space.ToGodot(eye + dir * 1000), Space.DirToGodot(Camera.Up));

        if (Camera.Perspective)
        {
            _camera.Projection = Camera3D.ProjectionType.Perspective;
            _camera.Fov = (float)Camera.FovDegrees;
            // Near plane scales with the viewing distance; reverse-Z keeps depth precision with a far plane this large.
            _camera.Near = (float)Math.Clamp(Camera.Distance * Space.MetersPerUnit * 0.001, 1e-5, 1);
            if (Camera.TwoPointShift is { } shift)
            {
                // A shifted lens: the same field of view, its centre moved up or down at the near plane.
                _camera.Projection = Camera3D.ProjectionType.Frustum;
                _camera.Size = (float)(2 * _camera.Near * Math.Tan(Camera.HalfFovRadians));
                _camera.FrustumOffset = new Vector2(0, (float)(_camera.Near * shift));
            }
        }
        else
        {
            _camera.Projection = Camera3D.ProjectionType.Orthogonal;
            _camera.Size = (float)(Camera.OrthoHeight * Space.MetersPerUnit);
            _camera.Near = 0.01f;
            Axes?.SetParallelScale(Camera.OrthoHeight, Camera.FovDegrees);
        }
        // Godot builds the frustum in single precision: a far/near ratio much past 1e6 makes it degenerate and every
        // object gets culled (the "create_frustum_points" errors), so the far plane follows the near one.
        _camera.Far = _camera.Near * 1e6f;

        UpdateHorizon();
        _annotations?.QueueRedraw();
        _skyMaterial.SetShaderParameter("camera_below", Camera.Eye.Z < 0);
        UpdateFog();
        UpdateShadowRange();
        // As in SketchUp, a matched photo goes away once the camera leaves it.
        if (Photo != null && !_photoCamera.Matches(Camera.Save()))
            ShowPhoto(null);
        _overlay?.QueueRedraw();
        CameraChanged?.Invoke();
    }

    /// <summary>Advanced Camera Tools: while set, navigation can't move the camera off this view.</summary>
    public CameraState? LockedCamera { get; set; }

    /// <summary>Advanced Camera Tools: the frame's width over height, shown with bars outside it; null for none.</summary>
    public double? FrameAspect
    {
        get => _frameAspect;
        set
        {
            _frameAspect = value;
            _overlay?.QueueRedraw();
        }
    }

    private double? _frameAspect;
    private TextureRect? _photoRect;
    private CameraState _photoCamera;

    /// <summary>The matched photo shown behind the model (Camera › Match New Photo, a scene with a photo).</summary>
    public MatchedPhoto? Photo { get; private set; }

    /// <summary>Shows <paramref name="photo"/> behind the model with the camera it matches; null hides it.</summary>
    public void ShowPhoto(MatchedPhoto? photo)
    {
        if (photo != null && PhotoMatch.Solve(photo) is { } matched)
        {
            if (Photo?.Image != photo.Image || _photoRect == null)
            {
                _photoRect?.QueueFree();
                var image = new Image();
                if (image.LoadPngFromBuffer(photo.Image) != Error.Ok && image.LoadJpgFromBuffer(photo.Image) != Error.Ok)
                    return;
                _photoRect = new TextureRect { Texture = ImageTexture.CreateFromImage(image), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore };
                AddChild(_photoRect);
                MoveChild(_photoRect, 0);
            }
            Photo = photo;
            Camera.Restore(matched);
            _photoCamera = Camera.Save();
            LayoutPhoto();
        }
        else
        {
            Photo = null;
            _photoRect?.QueueFree();
            _photoRect = null;
        }
        _subViewport.TransparentBg = Photo != null;
        if (_environment != null)
            _environment.BackgroundMode = Photo != null ? Godot.Environment.BGMode.ClearColor : Godot.Environment.BGMode.Sky;
        SyncCamera();
    }

    /// <summary>The photo spans the view's height (the field of view is vertical), centred.</summary>
    private void LayoutPhoto()
    {
        if (_photoRect == null || Photo == null)
            return;
        var height = Size.Y;
        _photoRect.Size = new Vector2((float)(height * Photo.Aspect), height);
        _photoRect.Position = new Vector2((Size.X - _photoRect.Size.X) / 2, 0);
    }

    /// <summary>Screen position of a point on the matched photo.</summary>
    public Vector2 FromPhoto(PhotoPoint p) => Size / 2 + new Vector2((float)p.X, (float)-p.Y) * Size.Y;

    public PhotoPoint ToPhoto(Vector2 screen) => new((screen.X - Size.X / 2) / Size.Y, -(screen.Y - Size.Y / 2) / Size.Y);

    /// <summary>
    /// SketchUp's sky gradient runs in screen space from the horizon line to the top of the view, so the shader
    /// needs the horizon's height on screen (0 = top, 1 = bottom).
    /// </summary>
    private void UpdateHorizon()
    {
        var dir = Camera.Direction;
        var flat = new Vec3(dir.X, dir.Y, 0);
        double horizonUv = dir.Z > 0 ? 1.5 : -0.5;
        if (!flat.IsZero(1e-9) && _subViewport.Size.Y > 0)
        {
            var far = Camera.Eye + flat.Normalized() * 1e9;
            if (ToScreen(far) is { } screen)
                horizonUv = screen.Y / _subViewport.Size.Y;
        }
        _skyMaterial.SetShaderParameter("horizon_uv", (float)horizonUv);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized && _camera != null)
        {
            UpdateHorizon();
            LayoutPhoto();
            UpdateWatermarks();
        }
    }

    // ---------------------------------------------------------------- picking

    public void QueueOverlayRedraw()
    {
        _overlay.QueueRedraw();
        _annotations?.QueueRedraw();
    }

    private AnnotationCanvas? _annotations;

    /// <summary>Model size of one pixel in a parallel view (1 in perspective, where there is no single scale).</summary>
    public double MillimetresPerPixel => Camera.Perspective ? 1 : Camera.OrthoHeight / Math.Max(Size.Y, 1);

    /// <summary>The view as a hidden-line drawing, in viewport pixels.</summary>
    public List<Core.IO.HiddenLine.Segment> HiddenLineDrawing()
    {
        if (Document is not { } doc)
            return [];
        var eye = Camera.Eye;
        var back = Camera.Direction.Normalized();
        Core.Picking.Ray RayTo(Vec3 p) => Camera.Perspective
            ? new Core.Picking.Ray(eye, (p - eye).Normalized())
            : new Core.Picking.Ray(p - back * ParallelEyeBackoff, back);
        (double X, double Y)? Screen(Vec3 p) => ToScreen(p) is { } s && s.X >= -Size.X && s.X <= 2 * Size.X && s.Y >= -Size.Y && s.Y <= 2 * Size.Y ? (s.X, s.Y) : null;
        return Core.IO.HiddenLine.Visible(doc.Model, Screen, RayTo, doc.Picker);
    }

    /// <summary>The view as drawn, with its annotations and watermarks (exports and printing show them, as in SketchUp).</summary>
    public Image Snapshot() => _subViewport.GetTexture().GetImage();

    /// <summary>
    /// File › Export › 2D Graphic options: the view drawn at <paramref name="size"/> pixels (same camera, its height
    /// kept), with or without anti-aliasing and with the background left transparent.
    /// </summary>
    public async Task<Image> RenderImage(Vector2I size, bool antialias, bool transparent)
    {
        var container = (SubViewportContainer)_subViewport.GetParent();
        var msaa = _subViewport.Msaa3D;
        var background = _environment!.BackgroundMode;
        var clear = _environment.BackgroundColor;
        container.Stretch = false;
        _subViewport.Size = size;
        _subViewport.Msaa3D = antialias ? Godot.Viewport.Msaa.Msaa4X : Godot.Viewport.Msaa.Disabled;
        if (transparent)
        {
            _subViewport.TransparentBg = true;
            _environment.BackgroundMode = Godot.Environment.BGMode.ClearColor;
            _environment.BackgroundColor = new Color(0, 0, 0, 0);
        }
        UpdateHorizon();
        UpdateWatermarks();
        QueueOverlayRedraw();
        for (var i = 0; i < 2; i++)
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = _subViewport.GetTexture().GetImage();
        _subViewport.Msaa3D = msaa;
        _subViewport.TransparentBg = false;
        _environment.BackgroundMode = background;
        _environment.BackgroundColor = clear;
        container.Stretch = true;
        UpdateHorizon();
        UpdateWatermarks();
        QueueOverlayRedraw();
        return image;
    }

    /// <summary>Dimensions and texts, drawn on the overlay.</summary>
    public AnnotationOverlay Annotations { get; } = new();

    private Godot.Environment? _environment;
    private Bounds3 _fogBounds = Bounds3.Empty;
    private (double Start, double End) _fogRange = (0, 1);

    /// <summary>Styles › Background: plain colour, sky and ground (with its transparency and whether it shows from below).</summary>
    public void ApplyStyle(StyleSettings s)
    {
        Color C(Rgba c) => Color.Color8(c.R, c.G, c.B);
        var sky = C(s.SkyColor);
        _skyMaterial.SetShaderParameter("sky_top", sky);
        // SketchUp fades its sky to almost white at the horizon.
        _skyMaterial.SetShaderParameter("sky_horizon", sky.Lerp(Colors.White, 0.7f));
        _skyMaterial.SetShaderParameter("ground", C(s.GroundColor));
        _skyMaterial.SetShaderParameter("background", C(s.BackgroundColor));
        _skyMaterial.SetShaderParameter("sky_on", s.Sky);
        _skyMaterial.SetShaderParameter("ground_on", s.Ground);
        _skyMaterial.SetShaderParameter("ground_transparency", (float)s.GroundTransparency);
        _skyMaterial.SetShaderParameter("ground_from_below", s.GroundFromBelow);
        _style = s;
        ShowSectionPlanes = s.SectionPlanes;
        ShowSectionCuts = s.SectionCuts;
        if (Axes != null)
            Axes.Visible = s.ModelAxes;
        UpdateWatermarks();
        _sectionFillColor = C(s.SectionFillColor);
        if (_sectionFill?.MaterialOverride is StandardMaterial3D fill)
            fill.AlbedoColor = _sectionFillColor;
    }

    private Color _sectionFillColor = Color.Color8(63, 63, 63);
    private StyleSettings _style = new();
    private TextureRect _watermarkFront = null!;

    /// <summary>Styles › Watermarks: lays them out again for the view's size.</summary>
    private void UpdateWatermarks()
    {
        if (_watermarkFront == null)
            return;
        var (back, front) = WatermarkCompositor.Compose(_style, _subViewport.Size);
        _skyMaterial.SetShaderParameter("has_watermarks", back != null);
        _skyMaterial.SetShaderParameter("watermarks", back != null ? ImageTexture.CreateFromImage(back) : null);
        _watermarkFront.Texture = front != null ? ImageTexture.CreateFromImage(front) : null;
    }

    /// <summary>Fog panel: start and end (0 to 1 across the model's depth) and colour (null for the background's).</summary>
    public void SetFog(double start, double end, Color? color)
    {
        _fogRange = (start, Math.Max(end, start + 0.01));
        if (_environment != null)
            _environment.FogLightColor = color ?? new Color(0.74f, 0.76f, 0.79f);
        UpdateFog();
    }

    /// <summary>View › Fog: geometry fades into the background colour with distance, as SketchUp's fog.</summary>
    public bool ShowFog
    {
        get => _environment?.FogEnabled ?? false;
        set
        {
            if (_environment == null)
                return;
            _environment.FogEnabled = value;
            _environment.FogMode = Godot.Environment.FogModeEnum.Depth;
            _environment.FogDensity = 1;
            _environment.FogDepthCurve = 1;
            _environment.FogSkyAffect = 0; // the sky gradient stays
            _fogBounds = ModelBounds(); // measured once: big models are slow to measure on every camera move
            UpdateFog();
        }
    }

    /// <summary>Fog starts at the model's near side and is full at its far side, following the camera.</summary>
    private void UpdateFog()
    {
        if (_environment is not { FogEnabled: true })
            return;
        var bounds = _fogBounds;
        var distance = Camera.Distance;
        var radius = bounds.IsEmpty ? distance : bounds.Diagonal * 0.5;
        var centreDepth = bounds.IsEmpty ? distance : Math.Max(Camera.DepthOf(bounds.Center), 1);
        var near = Math.Max(centreDepth - radius, 0);
        var far = centreDepth + radius * 1.5;
        _environment.FogDepthBegin = (float)((near + (far - near) * _fogRange.Start) * Space.MetersPerUnit);
        _environment.FogDepthEnd = (float)((near + (far - near) * _fogRange.End) * Space.MetersPerUnit);
    }

    private DirectionalLight3D _sun = null!;
    private MeshInstance3D _ground = null!;
    private Bounds3 _shadowBounds = Bounds3.Empty;

    /// <summary>
    /// Points the sun and turns its shadows on or off from the model's settings (the faces' shading comes from the
    /// renderer's lit materials).
    /// </summary>
    public void ApplyShadows(ShadowSettings s)
    {
        var dir = s.SunDirection();
        var lit = s.Enabled || s.UseSunForShading;
        _sun.Visible = lit && dir.Z > 0;
        if (_sun.Visible)
            _sun.Basis = Basis.LookingAt(-Space.DirToGodot(dir), Math.Abs(dir.Z) > 0.999 ? Vector3.Forward : Vector3.Up);
        _sun.ShadowEnabled = s.Enabled;
        _shadowBounds = ModelBounds();
        _ground.Visible = s.Enabled && s.OnGround && _sun.Visible && !_shadowBounds.IsEmpty;
        if (_ground.Visible)
        {
            // Wide enough for a low sun's long shadows.
            var size = (float)(Math.Max(_shadowBounds.Diagonal, 1) * 20 * Space.MetersPerUnit);
            var c = _shadowBounds.Center;
            _ground.Transform = new Transform3D(Basis.Identity.Scaled(new Vector3(size, 1, size)), Space.ToGodot(new Vec3(c.X, c.Y, 0)));
            ((ShaderMaterial)_ground.MaterialOverride).SetShaderParameter("strength", s.Light / 100f * 0.45f);
        }
        UpdateShadowRange();
    }

    /// <summary>The sun's shadow map covers from the eye to just past the model, so small models get sharp shadows.</summary>
    private void UpdateShadowRange()
    {
        if (_sun is not { ShadowEnabled: true })
            return;
        var radius = _shadowBounds.IsEmpty ? Camera.Distance : _shadowBounds.Diagonal;
        var depth = _shadowBounds.IsEmpty ? Camera.Distance : Math.Max(Camera.DepthOf(_shadowBounds.Center), 0);
        _sun.DirectionalShadowMaxDistance = (float)((depth + radius * 2) * Space.MetersPerUnit);
    }

    /// <summary>View › Section Planes / Section Cuts.</summary>
    public bool ShowSectionPlanes { get; set; } = true;
    public bool ShowSectionCuts { get; set; } = true;

    /// <summary>World segments where the active section cuts the model (drawn as thick lines).</summary>
    public List<(Vec3 A, Vec3 B)> SectionCut { get; private set; } = [];

    /// <summary>Places the drawn axes where the model's drawing axes are (Axes tool).</summary>
    public void UpdateAxes()
    {
        if (Document != null)
            Axes.Transform = ModelRenderer.ToGodot(Document.Model.Axes);
    }

    private MeshInstance3D? _sectionFill;

    private void UpdateSectionFill(SectionPlane? plane)
    {
        if (_sectionFill == null)
        {
            var material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                AlbedoColor = _sectionFillColor,
            };
            _sectionFill = new MeshInstance3D { MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            ModelRoot.GetParent().AddChild(_sectionFill);
        }
        var triangles = plane != null && ShowSectionCuts && Document!.Model.ShowSectionFill
            ? SectionFill.Triangles(SectionCut, plane.Normal)
            : [];
        if (triangles.Count == 0)
        {
            _sectionFill.Mesh = null;
            return;
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = triangles.Select(Space.ToGodot).ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _sectionFill.Mesh = mesh;
    }

    /// <summary>Applies the model's active section: the shaders' cut plane and the cut lines.</summary>
    public void UpdateSection()
    {
        var plane = Document?.Model.Entities.ActiveSection;
        if (plane == null || !ShowSectionCuts)
        {
            RenderingServer.GlobalShaderParameterSet("section_plane", Vector4.Zero);
            SectionCut = [];
        }
        else
        {
            var n = Space.DirToGodot(plane.Normal);
            var p = Space.ToGodot(plane.Point);
            RenderingServer.GlobalShaderParameterSet("section_plane", new Vector4(n.X, n.Y, n.Z, n.Dot(p)));
            SectionCut = Intersect.SectionCut(Document!.Model);
        }
        UpdateSectionFill(plane);
        QueueOverlayRedraw();
    }

    /// <summary>Model-space ray through a viewport pixel.</summary>
    public (Vec3 Origin, Vec3 Direction) ScreenRay(Vector2 screen)
    {
        var origin = Space.FromGodot(_camera.ProjectRayOrigin(screen));
        var direction = Space.DirFromGodot(_camera.ProjectRayNormal(screen)).Normalized();
        return (origin, direction);
    }

    /// <summary>Model point under the cursor, or null when the ray hits no geometry.</summary>
    public Vec3? PickGeometry(Vector2 screen) => Pick(screen)?.Point;

    /// <summary>Face or edge under the cursor (edges within <see cref="PickRadiusPixels"/>), with its instance path.</summary>
    public PickHit? Pick(Vector2 screen)
    {
        if (Document is not { } doc)
            return null;
        var (origin, direction) = ScreenRay(screen);
        var perPixel = Camera.Perspective
            ? 2 * Math.Tan(Camera.HalfFovRadians) / Math.Max(Size.Y, 1)
            : Camera.OrthoHeight / Math.Max(Size.Y, 1);
        Func<double, double> tolerance = Camera.Perspective
            ? t => t * perPixel * PickRadiusPixels
            : _ => perPixel * PickRadiusPixels;
        return doc.Picker.Pick(doc.Model.Entities, new Ray(origin, direction), tolerance, IsVisible);
    }

    private static bool IsVisible(object o) => o switch
    {
        Face f => !f.Hidden && f.Tag is not { Visible: false },
        Edge e => (e.Flags & (EdgeFlags.Hidden | EdgeFlags.Soft)) == 0 && e.Tag is not { Visible: false },
        ComponentInstance i => !i.Hidden && i.Tag is not { Visible: false },
        _ => true,
    };

    /// <summary>
    /// Point under the cursor for zooming: geometry if any, otherwise the point on the ray at the target's depth.
    /// </summary>
    /// <summary>Geometry under the cursor, else the ground (the drawing axes' red-green plane), else null.</summary>
    public Vec3? PickGround(Vector2 screen)
    {
        if (PickGeometry(screen) is { } hit)
            return hit;
        var (origin, direction) = ScreenRay(screen);
        var axes = Document?.Model.Axes ?? Transform.Identity;
        return Core.Inference.InferenceEngine.IntersectPlane(new Core.Picking.Ray(origin, direction), axes.Z.Normalized(), axes.Origin);
    }

    public Vec3 PickPoint(Vector2 screen)
    {
        if (PickGeometry(screen) is { } hit)
            return hit;
        var (origin, direction) = ScreenRay(screen);
        var depth = Camera.Perspective ? Camera.Distance : Camera.Distance + ParallelEyeBackoff;
        var along = direction.Dot(Camera.Direction);
        return origin + direction * (depth / Math.Max(along, 1e-6));
    }

    /// <summary>Screen position of a model point, or null when it is behind the camera.</summary>
    public Vector2? ToScreen(Vec3 point)
    {
        var p = Space.ToGodot(point);
        return _camera.IsPositionBehind(p) ? null : _camera.UnprojectPosition(p);
    }

    // ---------------------------------------------------------------- input

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton mb:
                HandleMouseButton(mb);
                break;
            case InputEventMouseMotion mm:
                HandleMouseMotion(mm);
                break;
            case InputEventKey key:
                HandleKey(key);
                break;
        }
        UpdateCursor();
    }

    private string _cursorImage = "";

    /// <summary>The active tool's SketchUp cursor (orbit/pan while navigating with the middle button).</summary>
    private void UpdateCursor()
    {
        var name = _middleDragging ? (Input.IsKeyPressed(Key.Shift) ? "pan" : "orbit") : Tools.Active.CursorImage;
        if (name == _cursorImage)
            return;
        _cursorImage = name;
        MouseDefaultCursorShape = ToolCursors.Apply(name) ? ToolCursors.Shape : (CursorShape)Tools.Active.Cursor;
    }

    private void HandleMouseButton(InputEventMouseButton mb)
    {
        if (mb.Pressed)
            GrabFocus();

        switch (mb.ButtonIndex)
        {
            case MouseButton.WheelUp or MouseButton.WheelDown when mb.Pressed:
                var now = Time.GetTicksMsec();
                if (now - _lastWheelMs > WheelGestureMs)
                    BeginNavigation();
                _lastWheelMs = now;
                var factor = (mb.ButtonIndex == MouseButton.WheelUp) != UI.AppPreferences.Current.InvertWheelZoom ? WheelZoomFactor : 1 / WheelZoomFactor;
                ZoomAt(PickPoint(mb.Position), factor);
                break;

            case MouseButton.Middle when mb.Pressed && mb.DoubleClick:
                CenterOn(mb.Position);
                break;

            case MouseButton.Middle when mb.Pressed:
                _middleDragging = true;
                if (mb.ShiftPressed)
                    BeginPan(mb.Position);
                else
                    BeginOrbit(mb.Position);
                break;

            case MouseButton.Middle:
                _middleDragging = false;
                break;

            default:
                if (mb.Pressed)
                    Tools.Active.MouseDown(mb.ButtonIndex, mb.Position);
                else
                    Tools.Active.MouseUp(mb.ButtonIndex, mb.Position);
                break;
        }
        AcceptEvent();
    }

    private void HandleMouseMotion(InputEventMouseMotion mm)
    {
        if (_middleDragging)
        {
            if (mm.ShiftPressed)
            {
                if (_navDepth <= 0)
                    _navDepth = Camera.Distance;
                PanBy(mm.Relative);
            }
            else
            {
                OrbitBy(mm.Relative, gravity: !mm.CtrlPressed);
            }
        }
        else
        {
            Tools.Active.MouseMove(mm.Position, mm.Relative);
        }
        QueueOverlayRedraw();
        AcceptEvent();
    }

    private void HandleKey(InputEventKey key)
    {
        if (key.Pressed && HandleVcbKey(key))
        {
            AcceptEvent();
            return;
        }
        var consumed = key.Pressed ? Tools.Active.KeyDown(key) : Tools.Active.KeyUp(key);
        if (!consumed && key.Pressed && key.Keycode == Key.Escape && Tools.Active.IsNavigation)
        {
            Tools.ActivatePrevious();
            consumed = true;
        }
        if (consumed)
            AcceptEvent();
    }

    /// <summary>
    /// SketchUp's Measurements box takes typing without being clicked: a digit (or '-', '.', ',') starts it while a
    /// tool that measures is active, and from then on letters belong to it too ("25cm"), so they don't fire tool
    /// shortcuts. Enter applies, Backspace edits, Esc discards.
    /// </summary>
    private bool HandleVcbKey(InputEventKey key)
    {
        if (Tools.Active.VcbLabel.Length == 0 || key.CtrlPressed || key.AltPressed)
            return false;
        var ch = key.Unicode > 0 ? (char)key.Unicode : '\0';
        var typing = VcbTyping.Length > 0;
        if (!typing && !(char.IsDigit(ch) || ch is '-' or '.' or ','))
            return false;

        switch (key.Keycode)
        {
            case Key.Enter or Key.KpEnter:
                var text = VcbTyping;
                VcbTyping = "";
                if (!Tools.Active.ApplyVcb(text))
                    OS.Alert($"Invalid value: {text}", "Measurements");
                VcbTextChanged?.Invoke(Tools.Active.VcbValue);
                return true;
            case Key.Backspace:
                VcbTyping = VcbTyping.Length > 0 ? VcbTyping[..^1] : "";
                VcbTextChanged?.Invoke(VcbTyping);
                return true;
            case Key.Escape:
                VcbTyping = "";
                VcbTextChanged?.Invoke(Tools.Active.VcbValue);
                return true;
        }
        if (ch == '\0' || char.IsControl(ch))
            return typing;
        VcbTyping += ch;
        VcbTextChanged?.Invoke(VcbTyping);
        return true;
    }

    /// <summary>Middle double-click: pan so the clicked point becomes the view centre and orbit target.</summary>
    private void CenterOn(Vector2 screen)
    {
        BeginNavigation();
        var point = PickPoint(screen);
        var dir = Camera.Direction;
        var onAxis = Camera.Eye + dir * (point - Camera.Eye).Dot(dir);
        var delta = point - onAxis;
        Camera.Set(Camera.Eye + delta, point, Camera.Up);
        SyncCamera();
    }
}
