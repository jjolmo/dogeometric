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

    /// <summary>Parallel views: dashes sized as a perspective view of the same scale would show them.</summary>
    public void SetParallelScale(double orthoHeightMm, double fovDegrees) =>
        ((ShaderMaterial)MaterialOverride).SetShaderParameter("parallel_depth",
            (float)(orthoHeightMm * Space.MetersPerUnit / 2 / Math.Tan(fovDegrees * Math.PI / 360)));

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

    // Pieces growing tenfold from the origin: a line with an end beyond the far plane is dropped by some rasterisers
    // (software Vulkan, and parallel views keep the far plane at 10 km), so each piece clips on its own.
    private static readonly double[] Steps = [0, 1_000, 10_000, 100_000, HalfLengthMm];

    private static void AddAxis(ImmediateMesh mesh, Vec3 axis, Color color)
    {
        for (var i = 0; i + 1 < Steps.Length; i++)
        {
            var (a, b) = (Steps[i], Steps[i + 1]);
            var (ua, ub) = ((float)(a * Space.MetersPerUnit), (float)(b * Space.MetersPerUnit));
            AddVertex(mesh, axis * a, color, ua);
            AddVertex(mesh, axis * b, color, ub);
            // Negative half: UV.x carries the negative distance so the shader can dash it.
            AddVertex(mesh, -axis * a, color, -ua);
            AddVertex(mesh, -axis * b, color, -ub);
        }
    }

    private static void AddVertex(ImmediateMesh mesh, Vec3 p, Color color, float u)
    {
        mesh.SurfaceSetColor(color);
        mesh.SurfaceSetUV(new Vector2(u, 0));
        mesh.SurfaceAddVertex(Space.ToGodot(p));
    }
}
