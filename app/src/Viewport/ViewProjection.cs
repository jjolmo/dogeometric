using Dogeometric.Core.Geometry;
using Dogeometric.Core.Inference;
using Dogeometric.Core.Picking;
using Godot;

namespace Dogeometric.App.Viewport;

/// <summary>Adapts <see cref="ModelViewport"/> to what the inference engine needs.</summary>
public sealed class ViewProjection(ModelViewport view) : IViewProjection
{
    public Ray RayAt(double x, double y)
    {
        var (o, d) = view.ScreenRay(new Vector2((float)x, (float)y));
        return new Ray(o, d);
    }

    public (double X, double Y)? ToScreen(Vec3 world) => view.ToScreen(world) is { } s ? (s.X, s.Y) : null;

    public PickHit? Pick(double x, double y) => view.Pick(new Vector2((float)x, (float)y));

    public Vec3 ViewDirection => view.Camera.Direction;
}
