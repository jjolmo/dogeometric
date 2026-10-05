using Dogeometric.Core.Geometry;
using Godot;

namespace Dogeometric.App.Viewport;

/// <summary>Red, green and blue drawing axes through the model origin.</summary>
public partial class AxesRenderer : MeshInstance3D
{
    // SketchUp draws the axes "to infinity"; 1 km each way is beyond any enclosure model.
    private const double HalfLengthMm = 1_000_000;

    public override void _Ready()
    {
        Build();
        UI.AppPreferences.Changed += Build;
        MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/axes.gdshader") };
        CastShadow = ShadowCastingSetting.Off;
    }

    public override void _ExitTree() => UI.AppPreferences.Changed -= Build;

    /// <summary>The axes in Preferences › Accessibility's colours, a little darker as SketchUp 2021 draws them.</summary>
    private void Build()
    {
        var p = UI.AppPreferences.Current;
        var mesh = new ImmediateMesh();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        AddAxis(mesh, Vec3.UnitX, p.RedAxis.Darkened(0.19f));
        AddAxis(mesh, Vec3.UnitY, p.GreenAxis.Darkened(0.19f));
        AddAxis(mesh, Vec3.UnitZ, p.BlueAxis.Darkened(0.19f));
        mesh.SurfaceEnd();
        Mesh = mesh;
    }

    private static void AddAxis(ImmediateMesh mesh, Vec3 axis, Color color)
    {
        var end = (float)(HalfLengthMm * Space.MetersPerUnit);
        AddVertex(mesh, Vec3.Zero, color, 0);
        AddVertex(mesh, axis * HalfLengthMm, color, end);
        // Negative half: UV.x carries the negative distance so the shader can dash it.
        AddVertex(mesh, Vec3.Zero, color, -0f);
        AddVertex(mesh, -axis * HalfLengthMm, color, -end);
    }

    private static void AddVertex(ImmediateMesh mesh, Vec3 p, Color color, float u)
    {
        mesh.SurfaceSetColor(color);
        mesh.SurfaceSetUV(new Vector2(u, 0));
        mesh.SurfaceAddVertex(Space.ToGodot(p));
    }
}
