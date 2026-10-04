using Dogeometric.Core.Geometry;
using Godot;

namespace Dogeometric.App.Viewport;

/// <summary>Red, green and blue drawing axes through the model origin.</summary>
public partial class AxesRenderer : MeshInstance3D
{
    // SketchUp draws the axes "to infinity"; 1 km each way is beyond any enclosure model.
    private const double HalfLengthMm = 1_000_000;

    // sRGB, sampled from SketchUp 2021's viewport.
    public static readonly Color Red = Color.Color8(178, 0, 0);
    public static readonly Color Green = Color.Color8(0, 178, 0);
    public static readonly Color Blue = Color.Color8(0, 0, 178);

    public override void _Ready()
    {
        var mesh = new ImmediateMesh();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        AddAxis(mesh, Vec3.UnitX, Red);
        AddAxis(mesh, Vec3.UnitY, Green);
        AddAxis(mesh, Vec3.UnitZ, Blue);
        mesh.SurfaceEnd();

        Mesh = mesh;
        MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/axes.gdshader") };
        CastShadow = ShadowCastingSetting.Off;
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
