using Dogeometric.App.Commands;
using Dogeometric.App.UI;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Inference;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Picking;
using Dogeometric.Core.Units;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>JointPushPull's Joint, Normal, Vector and Extrude tools: pick faces, click one and move to push-pull them all
/// live; click or type the offset to finish. The palette sets borders, finishing and grouping.</summary>
public sealed class JointPushPullTool(JointPushPullMode mode) : DrawingTool
{
    // The palette's settings are kept for the session, as the extension keeps its own.
    private static JointPushPullBorders _borders = JointPushPullBorders.Contour;
    private static bool _thicken;
    private static bool _asGroup;
    private static double _lastOffset = 10;

    private readonly List<Face> _faces = [];
    private bool _pushing;
    private Vec3 _anchor;
    private Vec3 _normal;
    private double _offset;
    private Window? _palette;

    public override int CommandId => mode switch
    {
        JointPushPullMode.Joint => ExtensionIds.JointPushPull,
        JointPushPullMode.Normal => ExtensionIds.NormalPushPull,
        JointPushPullMode.Vector => ExtensionIds.VectorPushPull,
        JointPushPullMode.Follow => ExtensionIds.FollowPushPull,
        JointPushPullMode.Round => ExtensionIds.RoundPushPull,
        _ => ExtensionIds.ExtrudePushPull,
    };

    public override string CursorImage => "pushpull";
    public override string VcbLabel => "Offset";
    public override string VcbValue => _pushing ? Length.Format(_offset, LengthUnit.Millimeters, 1) : Length.Format(_lastOffset, LengthUnit.Millimeters, 1);

    public override string StatusText => _pushing
        ? "Move to the offset you want, then click (or type the offset)."
        : _faces.Count == 0
            ? "Click faces to push-pull (Ctrl adds), or select them first."
            : $"Faces: {_faces.Count}. Click one of them and move to push-pull; type an offset to apply it at once.";

    private string Name => mode switch
    {
        JointPushPullMode.Joint => "Joint Push Pull",
        JointPushPullMode.Normal => "Normal Push Pull",
        JointPushPullMode.Vector => "Vector Push Pull",
        JointPushPullMode.Follow => "Follow Push Pull",
        JointPushPullMode.Round => "Round Push Pull",
        _ => "Extrude Push Pull",
    };

    public override void Activate()
    {
        if (View.Document is { } doc)
            _faces.AddRange(doc.Selection.Items.OfType<Face>().Where(doc.Context.Entities.Faces.Contains));
        _palette = Palette();
    }

    public override void Deactivate()
    {
        View.Document?.CancelPreview();
        _palette?.QueueFree();
        _palette = null;
    }

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || View.Document is not { } doc)
            return;
        if (_pushing)
        {
            Commit(doc);
            return;
        }
        if (View.Pick(position) is not { Face: { } face } hit || !hit.Path.SequenceEqual(doc.Context.Path))
            return;
        if (!_faces.Contains(face) || Input.IsKeyPressed(Key.Ctrl))
        {
            if (!Input.IsKeyPressed(Key.Ctrl))
                _faces.Clear();
            if (!_faces.Remove(face))
                _faces.Add(face);
            doc.Selection.Set(_faces.Cast<object>().ToList());
            RefreshStatus();
            return;
        }
        // Start pushing from the clicked face; Vector and Extrude measure along their own direction.
        var toWorld = doc.Context.ToWorld;
        _anchor = hit.Point;
        _normal = toWorld.ApplyNormal(mode == JointPushPullMode.Extrude ? Average() : face.Normal).Normalized();
        if (mode == JointPushPullMode.Vector)
            _normal = MostFacingPlane().Normalized();
        _pushing = true;
        RefreshStatus();
    }

    private Vec3 Average() => _faces.Aggregate(Vec3.Zero, (a, f) => a + f.Normal.Normalized() * f.Area).Normalized();

    public override void MouseMove(Vector2 position, Vector2 relative)
    {
        base.MouseMove(position, relative);
        if (!_pushing)
            return;
        if (Current is { Kind: InferenceKind.Endpoint or InferenceKind.Midpoint or InferenceKind.Center or InferenceKind.OnEdge } snap)
            _offset = (snap.Point - _anchor).Dot(_normal);
        else
        {
            var ray = View.ScreenRay(Mouse);
            _offset = (InferenceEngine.ClosestOnLine(new Ray(ray.Origin, ray.Direction), _anchor, _normal) - _anchor).Dot(_normal);
        }
        View.ShowVcbValue(VcbValue);
        Preview();
    }

    private JointPushPull.Options Options(Document doc) => new()
    {
        Mode = mode,
        Borders = _borders,
        Thicken = _thicken,
        AsGroup = _asGroup,
        Direction = doc.Context.ToWorld.Inverse().ApplyVector(_normal),
        Segments = _segments,
    };

    private static int _segments = 6;

    private void Preview()
    {
        if (View.Document is not { } doc || _faces.Count == 0)
            return;
        var faces = _faces.ToList();
        var offset = _offset;
        try
        {
            doc.Preview(Name, e => JointPushPull.Apply(doc.Model, e, faces, offset, Options(doc)));
        }
        catch (Exception)
        {
            doc.CancelPreview();
        }
        View.QueueOverlayRedraw();
    }

    private void Commit(Document doc)
    {
        if (Math.Abs(_offset) < Tolerance.Length)
        {
            doc.CancelPreview();
        }
        else
        {
            doc.CommitPreview();
            _lastOffset = _offset;
        }
        _pushing = false;
        _faces.Clear();
        doc.Selection.Clear();
        RefreshStatus();
    }

    public override bool ApplyVcb(string text)
    {
        if (View.Document is not { } doc || _faces.Count == 0 || !Length.TryParse(text, LengthUnit.Millimeters, out var mm) || Math.Abs(mm) < Tolerance.Length)
            return false;
        if (!_pushing)
            _normal = doc.Context.ToWorld.ApplyNormal(mode == JointPushPullMode.Vector ? MostFacingPlane() : Average()).Normalized();
        _offset = mm;
        Preview();
        Commit(doc);
        return true;
    }

    public override bool KeyDown(InputEventKey key)
    {
        if (key.Keycode != Key.Escape)
            return base.KeyDown(key);
        if (_pushing)
        {
            View.Document?.CancelPreview();
            _pushing = false;
        }
        else
        {
            _faces.Clear();
            View.Document?.Selection.Clear();
        }
        RefreshStatus();
        return true;
    }

    public override void Draw(Control overlay) => DrawInference(overlay);

    /// <summary>The extension's palette: borders, finishing and group, as option buttons.</summary>
    private Window Palette()
    {
        var w = new Window { Title = Name, Transient = true, Unresizable = true, Theme = LightTheme.Create(), Size = new Vector2I(300, 0), WrapControls = true };
        var root = new PanelContainer();
        root.AddThemeStyleboxOverride("panel", LightTheme.Box(Colors.White, 8, 6));
        var grid = new GridContainer { Columns = 2 };
        grid.AddChild(new Label { Text = "Borders" });
        var borders = new OptionButton();
        borders.AddItem("Contour");
        borders.AddItem("Grid");
        borders.AddItem("None");
        borders.Select((int)_borders);
        borders.ItemSelected += i =>
        {
            _borders = (JointPushPullBorders)i;
            if (_pushing)
                Preview();
        };
        grid.AddChild(borders);
        grid.AddChild(new Label { Text = "Finishing" });
        var finishing = new OptionButton { TooltipText = "Push-Pull erases the original faces; Thicken keeps them, making a shell." };
        finishing.AddItem("Push-Pull");
        finishing.AddItem("Thicken");
        finishing.Select(_thicken ? 1 : 0);
        finishing.ItemSelected += i =>
        {
            _thicken = i == 1;
            if (_pushing)
                Preview();
        };
        grid.AddChild(finishing);
        if (mode == JointPushPullMode.Round)
        {
            grid.AddChild(new Label { Text = "Segments" });
            var segments = new SpinBox { MinValue = 1, MaxValue = 48, Value = _segments };
            segments.ValueChanged += v =>
            {
                _segments = (int)v;
                if (_pushing)
                    Preview();
            };
            grid.AddChild(segments);
        }
        var group = new CheckBox { Text = "Generate as a Group", ButtonPressed = _asGroup };
        group.Toggled += v =>
        {
            _asGroup = v;
            if (_pushing)
                Preview();
        };
        grid.AddChild(group);
        root.AddChild(grid);
        w.AddChild(root);
        View.GetTree().Root.AddChild(w);
        var main = View.GetWindow();
        w.Position = main.Position + new Vector2I(Math.Max(main.Size.X - 360, 0), 130);
        w.Show();
        return w;
    }
}
