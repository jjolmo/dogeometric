using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>Places a copy of a component from the Components panel: it hangs on the cursor by its origin and a
/// click drops it; a gluing component stands on the face under the cursor and sticks to it. Esc goes back to Select.</summary>
public sealed class ComponentPlaceTool(ComponentDefinition definition) : DrawingTool
{
    public override int CommandId => 0;
    public override string CursorImage => "move";
    public override string StatusText => "Pick insertion point.";

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || Current is not { } inf || View.Document is not { } doc)
            return;
        var toLocal = doc.Context.ToWorld.Inverse();
        var (placement, face) = Placement(inf);
        ComponentInstance? placed = null;
        doc.Operation("Place Component", e =>
        {
            placed = e.AddInstance(definition, placement.Then(toLocal));
            placed.GluedTo = face;
        });
        doc.Selection.Set([placed!]);
    }

    /// <summary>Where the component goes (world space) and the face it glues to, if any.</summary>
    private (Transform, Face?) Placement(Core.Inference.InferenceResult inf)
    {
        if (definition.GlueTo != GlueTo.None && inf.Face is { } f && !inf.InGroup)
        {
            var n = inf.EntityToWorld.ApplyNormal(f.Normal).Normalized();
            if (Gluing.Accepts(definition.GlueTo, n))
                return (Gluing.OnFace(inf.Point, n), f);
        }
        return (Transform.Translation(inf.Point), null);
    }

    public override bool KeyDown(InputEventKey key)
    {
        if (key.Keycode == Key.Escape)
        {
            Manager.Activate(new SelectTool());
            return true;
        }
        return base.KeyDown(key);
    }

    public override void Draw(Control overlay)
    {
        if (Current is { } inf)
        {
            // The definition's bounding box at the cursor.
            var b = definition.Entities.Bounds();
            if (!b.IsEmpty)
            {
                var (xf, _) = Placement(inf);
                Vec3 C(int i) => xf.ApplyPoint(new Vec3((i & 1) == 0 ? b.Min.X : b.Max.X, (i & 2) == 0 ? b.Min.Y : b.Max.Y, (i & 4) == 0 ? b.Min.Z : b.Max.Z));
                foreach (var (a, c) in new[] { (0, 1), (2, 3), (4, 5), (6, 7), (0, 2), (1, 3), (4, 6), (5, 7), (0, 4), (1, 5), (2, 6), (3, 7) })
                    DrawWorldLine(overlay, C(a), C(c), new Color(0, 0, 1), 1);
            }
        }
        DrawInference(overlay);
    }
}
