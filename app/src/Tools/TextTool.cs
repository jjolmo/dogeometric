using Dogeometric.App.Viewport;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Inference;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Units;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>
/// SketchUp's Text: click an entity to attach a leader text (its default says what it is: a point's coordinates,
/// an edge's length, a face's area, a group's name), move to place the text and click, then type. Clicking empty
/// space places a screen text.
/// </summary>
public sealed class TextTool : DrawingTool
{
    private Vec3? _anchor;
    private string _default = "";
    private bool _editing;

    public override int CommandId => CommandIds.Text;
    public override string CursorImage => "label";

    public override string StatusText => _anchor == null
        ? "Select object to attach text to or position on screen."
        : "Select position for text.";

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || _editing || Current is not { } inf || View.Document is not { } doc)
            return;
        if (_anchor == null)
        {
            if (View.Pick(position) == null && inf.Kind is not (InferenceKind.Endpoint or InferenceKind.Midpoint))
            {
                // Empty space: screen text.
                Edit(doc, position, "Enter text", text => doc.Operation("Text", e => e.Texts.Add(new TextLabel(text)
                {
                    ScreenPosition = (position.X / View.Size.X, position.Y / View.Size.Y),
                })));
                return;
            }
            _anchor = inf.Point;
            _default = DefaultText(doc, inf);
            RefreshStatus();
            return;
        }

        var anchor = _anchor.Value;
        var end = TextPoint();
        var toLocal = doc.Context.ToWorld.Inverse();
        Edit(doc, position, _default, text => doc.Operation("Text", e => e.Texts.Add(new TextLabel(text)
        {
            Point = toLocal.ApplyPoint(anchor),
            Offset = toLocal.ApplyVector(end - anchor),
        })));
        _anchor = null;
        RefreshStatus();
    }

    private void Edit(Document doc, Vector2 at, string initial, Action<string> done)
    {
        _editing = true;
        InlineTextEditor.Show(View, at, initial, text =>
        {
            _editing = false;
            done(text);
        }, () => _editing = false);
    }

    /// <summary>Where the text goes: the cursor, at the anchor's depth.</summary>
    private Vec3 TextPoint()
    {
        var anchor = _anchor!.Value;
        var ray = View.ScreenRay(Mouse);
        return InferenceEngine.IntersectPlane(new Core.Picking.Ray(ray.Origin, ray.Direction), View.Camera.Direction, anchor) ?? anchor;
    }

    /// <summary>SketchUp's default text for what was clicked.</summary>
    private static string DefaultText(Document doc, InferenceResult inf)
    {
        var model = doc.Model;
        string L(double mm) => Length.Format(mm, model.Units, model.UnitPrecision);
        if (inf.Kind is InferenceKind.Endpoint or InferenceKind.Midpoint or InferenceKind.Origin)
        {
            var p = inf.Point;
            return $"{L(p.X)}, {L(p.Y)}, {L(p.Z)}";
        }
        if (inf is { Kind: InferenceKind.OnEdge, Edge: { } edge })
            return L(edge.Length);
        if (inf is { Kind: InferenceKind.OnFace, Face: { } face })
        {
            var scale = Length.ToMillimeters(model.Units);
            var area = face.Area / (scale * scale);
            return $"{area.ToString("F" + model.UnitPrecision, System.Globalization.CultureInfo.InvariantCulture)} {Length.Symbol(model.Units)}²";
        }
        return "Enter text";
    }

    public override bool KeyDown(InputEventKey key)
    {
        if (key.Keycode == Key.Escape && _anchor != null)
        {
            _anchor = null;
            RefreshStatus();
            View.QueueOverlayRedraw();
            return true;
        }
        return base.KeyDown(key);
    }

    public override void Draw(Control overlay)
    {
        if (_anchor is { } a && View.ToScreen(a) is { } sa)
        {
            var end = Mouse;
            overlay.DrawLine(sa, end, Colors.Black, 1, true);
            overlay.DrawCircle(sa, 2.5f, Colors.Black);
            var font = overlay.GetThemeDefaultFont();
            var size = font.GetStringSize(_default, HorizontalAlignment.Left, -1, 13);
            var x = end.X < sa.X ? end.X - size.X - 3 : end.X + 3;
            overlay.DrawString(font, new Vector2(x, end.Y + size.Y / 2 - 3), _default, HorizontalAlignment.Left, -1, 13, Colors.Black);
            return;
        }
        DrawInference(overlay);
    }
}
