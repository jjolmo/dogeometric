using Dogeometric.App.Commands;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Units;
using Dogeometric.Core.View;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>Camera › Match New Photo: drag the red and green bars onto edges along those axes and the origin where they
/// meet, and the camera follows; a typed distance scales the photo, and Return keeps the match in its scene.</summary>
public sealed class MatchPhotoTool(Scene scene) : Tool
{
    private const float Grab = 8;
    private static readonly Color RedAxis = new(0.85f, 0.1f, 0.1f);
    private static readonly Color GreenAxis = new(0.1f, 0.6f, 0.1f);
    private static readonly Color Yellow = new(0.95f, 0.8f, 0.1f);

    private readonly MatchedPhoto _photo = scene.Photo!.Clone();
    private (bool Red, int Line, bool End)? _grip;
    private bool _origin;

    public override int CommandId => CommandIds.MatchNewPhoto;
    public override string CursorImage => "select";
    public override string VcbLabel => "Distance";
    public override string VcbValue => Length.Format(_photo.Distance, LengthUnit.Millimeters, 0);
    public override string StatusText => "Drag the red and green bars onto edges along those axes, and the origin where they meet. Return when done.";

    public override void Activate()
    {
        _photo.Red = [.. _photo.Red];
        _photo.Green = [.. _photo.Green];
        View.ShowPhoto(_photo);
        View.ShowVcbValue(VcbValue);
    }

    public override void Deactivate() => Keep();

    private void Keep()
    {
        if (PhotoMatch.Solve(_photo) is not { } camera)
            return;
        scene.Photo = _photo.Clone();
        scene.Camera = camera;
    }

    private IEnumerable<(bool Red, int Line, bool End, PhotoPoint P)> Grips()
    {
        for (var i = 0; i < 2; i++)
        {
            yield return (true, i, false, _photo.Red[i].A);
            yield return (true, i, true, _photo.Red[i].B);
            yield return (false, i, false, _photo.Green[i].A);
            yield return (false, i, true, _photo.Green[i].B);
        }
    }

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left)
            return;
        if (View.FromPhoto(_photo.Origin).DistanceTo(position) <= Grab)
        {
            _origin = true;
            return;
        }
        foreach (var g in Grips())
            if (View.FromPhoto(g.P).DistanceTo(position) <= Grab)
            {
                _grip = (g.Red, g.Line, g.End);
                return;
            }
    }

    public override void MouseMove(Vector2 position, Vector2 relative)
    {
        var p = View.ToPhoto(position);
        if (_origin)
            _photo.Origin = p;
        else if (_grip is { } g)
        {
            var lines = g.Red ? _photo.Red : _photo.Green;
            lines[g.Line] = g.End ? (lines[g.Line].A, p) : (p, lines[g.Line].B);
        }
        else
            return;
        Apply();
    }

    public override void MouseUp(MouseButton button, Vector2 position)
    {
        _origin = false;
        _grip = null;
    }

    /// <summary>The camera follows each change; lines that give no camera leave it where it was.</summary>
    private void Apply()
    {
        if (PhotoMatch.Solve(_photo) != null)
            View.ShowPhoto(_photo);
        View.QueueOverlayRedraw();
    }

    public override bool ApplyVcb(string text)
    {
        if (!Length.TryParse(text, LengthUnit.Millimeters, out var mm) || mm <= 0)
            return false;
        _photo.Distance = mm;
        Apply();
        View.ShowVcbValue(VcbValue);
        return true;
    }

    public override bool KeyDown(InputEventKey key)
    {
        if (key.Keycode is Key.Enter or Key.KpEnter)
        {
            Manager.Activate(new SelectTool());
            return true;
        }
        return false;
    }

    public override void Draw(Control overlay)
    {
        var vx = PhotoMatch.VanishingPoint(_photo.Red[0], _photo.Red[1]);
        var vy = PhotoMatch.VanishingPoint(_photo.Green[0], _photo.Green[1]);
        if (vx is { } a && vy is { } b)
        {
            // The horizon runs through both vanishing points.
            var (sa, sb) = (View.FromPhoto(a), View.FromPhoto(b));
            var dir = (sb - sa).Normalized() * 10000;
            overlay.DrawLine(sa - dir, sb + dir, Yellow, 1);
        }
        void Bars((PhotoPoint A, PhotoPoint B)[] lines, PhotoPoint? vanishing, Color color)
        {
            foreach (var (a, b) in lines)
            {
                var (sa, sb) = (View.FromPhoto(a), View.FromPhoto(b));
                if (vanishing is { } v)
                    overlay.DrawDashedLine(sb, View.FromPhoto(v), new Color(color, 0.5f), 1, 4);
                overlay.DrawLine(sa, sb, color, 2.5f);
                foreach (var s in new[] { sa, sb })
                    overlay.DrawRect(new Rect2(s - new Vector2(4, 4), new Vector2(8, 8)), color);
            }
        }
        Bars(_photo.Red, vx, RedAxis);
        Bars(_photo.Green, vy, GreenAxis);
        var o = View.FromPhoto(_photo.Origin);
        overlay.DrawRect(new Rect2(o - new Vector2(5, 5), new Vector2(10, 10)), Yellow);
        overlay.DrawRect(new Rect2(o - new Vector2(5, 5), new Vector2(10, 10)), Colors.Black, false, 1);
    }
}
