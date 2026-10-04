using Dogeometric.Core.Modeling;
using Godot;
using Material = Dogeometric.Core.Modeling.Material;

namespace Dogeometric.App.Tools;

/// <summary>
/// SketchUp's Paint Bucket: paints the side of the face you click, or a whole group/component; with a selection,
/// clicking a selected item paints the whole selection. Alt samples, Shift paints every face with the same
/// material, Ctrl the connected ones, Ctrl+Shift those of the same object.
/// </summary>
public sealed class PaintBucketTool(Func<Material?> current, Action<Material?> sample) : Tool
{
    public override int CommandId => CommandIds.PaintBucket;
    public override string CursorImage => "paint";
    public override Input.CursorShape Cursor => Input.CursorShape.PointingHand;

    public override string StatusText => (Input.IsKeyPressed(Key.Alt), Input.IsKeyPressed(Key.Ctrl), Input.IsKeyPressed(Key.Shift)) switch
    {
        (true, _, _) => "Click a face to load its material into the Paint Bucket.",
        (_, true, true) => "Click to paint matching faces of the same object.",
        (_, true, false) => "Click to paint connected faces with matching material.",
        (_, false, true) => "Click to paint matching faces.",
        _ => "Click to paint an item or object.",
    };

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || View.Document is not { } doc || View.Pick(position) is not { } hit)
            return;
        var context = doc.Context.Path;
        if (hit.Path.Count < context.Count || !hit.Path.Take(context.Count).SequenceEqual(context))
            return;
        var target = hit.Path.Count > context.Count ? hit.Path[context.Count] : hit.Entity;

        if (Input.IsKeyPressed(Key.Alt))
        {
            sample(target switch
            {
                Face f => FrontVisible(f, doc) ? f.FrontMaterial : f.BackMaterial,
                ComponentInstance i => i.Material,
                _ => null,
            });
            return;
        }

        var material = current();
        var ctrl = Input.IsKeyPressed(Key.Ctrl);
        var shift = Input.IsKeyPressed(Key.Shift);
        doc.Operation("Paint", e =>
        {
            if (doc.Selection.Contains(target))
            {
                foreach (var item in doc.Selection.Items)
                    Paint(item, material, front: true);
                return;
            }
            if (target is Face face && (ctrl || shift))
            {
                var front = FrontVisible(face, doc);
                var old = front ? face.FrontMaterial : face.BackMaterial;
                IEnumerable<Face> faces = ctrl && !shift
                    ? Topology.Connected(e, face).OfType<Face>()
                    : e.Faces;
                foreach (var f in faces.Where(f => (front ? f.FrontMaterial : f.BackMaterial) == old))
                    Paint(f, material, front);
                return;
            }
            Paint(target, material, target is not Face f2 || FrontVisible(f2, doc));
        });
    }

    private static void Paint(object item, Material? m, bool front)
    {
        switch (item)
        {
            case Face f when front:
                f.FrontMaterial = m;
                break;
            case Face f:
                f.BackMaterial = m;
                break;
            case ComponentInstance i:
                i.Material = m;
                break;
        }
    }

    /// <summary>True when the camera sees the face's front side.</summary>
    private bool FrontVisible(Face f, Document doc)
    {
        var n = doc.Context.ToWorld.ApplyNormal(f.Normal);
        return n.Dot(View.Camera.Direction) < 0;
    }

    public override bool KeyDown(InputEventKey key)
    {
        if (key.Keycode is Key.Alt or Key.Ctrl or Key.Shift)
            RefreshStatus();
        return false;
    }

    public override bool KeyUp(InputEventKey key)
    {
        if (key.Keycode is Key.Alt or Key.Ctrl or Key.Shift)
            RefreshStatus();
        return false;
    }
}
