using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.View;

/// <summary>Advanced Camera Tools: cameras placed in the model as groups, each showing its body and its view frustum.</summary>
public static class CameraObjects
{
    public const string CamerasTag = "Cameras";
    public const string FrustumLinesTag = "Frustum Lines";
    public const string FrustumVolumeTag = "Frustum Volume";

    /// <summary>A camera group where <paramref name="view"/> is, looking the same way, its frustum reaching
    /// <paramref name="reach"/> millimetres (its own axes: red to the right, green ahead, blue up).</summary>
    public static ComponentInstance Create(Model model, Entities into, string name, CameraState view, double aspect, double reach)
    {
        var def = new ComponentDefinition { Name = name, IsGroup = true, Camera = new PhysicalCamera(view.FovDegrees, aspect) };
        var e = def.Entities;
        var body = reach * 0.03;
        BodyBox(e, new Vec3(-body / 2, -body * 1.6, -body / 2), new Vec3(body, body * 1.2, body));
        var h = Math.Tan(view.FovDegrees * Math.PI / 360) * reach;
        var w = h * aspect;
        Vec3[] corners = [new(-w, reach, -h), new(w, reach, -h), new(w, reach, h), new(-w, reach, h)];
        var lines = model.GetOrAddTag(FrustumLinesTag);
        var volume = model.GetOrAddTag(FrustumVolumeTag);
        for (var i = 0; i < 4; i++)
        {
            foreach (var edge in StickyGeometry.DrawEdges(e, [Vec3.Zero, corners[i]]))
                edge.Tag = lines;
            foreach (var edge in StickyGeometry.DrawEdges(e, [corners[i], corners[(i + 1) % 4]]))
                edge.Tag = lines;
        }
        var glass = model.Materials.FirstOrDefault(m => m.Name == "Camera Frustum")
            ?? new Material { Name = "Camera Frustum", Color = new Rgba(120, 170, 230), Opacity = 0.2 };
        if (!model.Materials.Contains(glass))
            model.Materials.Add(glass);
        for (var i = 0; i < 4; i++)
        {
            var face = e.AddFace([Vec3.Zero, corners[i], corners[(i + 1) % 4]]);
            (face.Tag, face.FrontMaterial, face.BackMaterial) = (volume, glass, glass);
        }
        model.Definitions.Add(def);
        var dir = (view.Target - view.Eye).Normalized();
        var up = (view.Up - dir * view.Up.Dot(dir)).Normalized();
        var right = dir.Cross(up);
        var inst = into.AddInstance(def, new Transform(right, dir, up, view.Eye));
        inst.Tag = model.GetOrAddTag(CamerasTag);
        return inst;
    }

    /// <summary>The view through a camera group (null for other instances).</summary>
    public static CameraState? ViewOf(ComponentInstance camera, Transform parent, double targetDistance = 1000)
    {
        if (camera.Definition.Camera is not { } cam)
            return null;
        var t = camera.Transform.Then(parent);
        var eye = t.ApplyPoint(Vec3.Zero);
        var dir = t.ApplyVector(Vec3.UnitY).Normalized();
        return new CameraState(eye, eye + dir * targetDistance, t.ApplyVector(Vec3.UnitZ).Normalized(), true, cam.FovDegrees, 1000);
    }

    private static void BodyBox(Entities e, Vec3 min, Vec3 size)
    {
        var (x, y, z) = (new Vec3(size.X, 0, 0), new Vec3(0, size.Y, 0), new Vec3(0, 0, size.Z));
        var p = min;
        e.AddFace([p, p + y, p + x + y, p + x]);
        e.AddFace([p + z, p + x + z, p + x + y + z, p + y + z]);
        e.AddFace([p, p + x, p + x + z, p + z]);
        e.AddFace([p + y, p + y + z, p + x + y + z, p + x + y]);
        e.AddFace([p, p + z, p + y + z, p + y]);
        e.AddFace([p + x, p + x + y, p + x + y + z, p + x + z]);
    }
}
