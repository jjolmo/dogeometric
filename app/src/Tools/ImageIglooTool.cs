using Dogeometric.Core.View;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>Camera › Image Igloo: look around from a matched photo's camera and see every photo matched from that spot
/// around you, each where it was taken looking.</summary>
public sealed class ImageIglooTool : Tool
{
    private const double RadiansPerPixel = 0.0035;
    private bool _dragging;
    private Node3D? _igloo;

    public override int CommandId => CommandIds.ImageIgloo;
    public override string CursorImage => "positioncamera";
    public override bool IsNavigation => true;
    public override string StatusText => _igloo == null
        ? "Image Igloo: go to a scene with a matched photo first."
        : "Drag to look around the photos matched from here.";

    public override void Activate()
    {
        if (View.Document is not { } doc || View.Photo is not { } shown || PhotoMatch.Solve(shown) is not { } here)
            return;
        // The photos taken from where this one was, as quads far enough to surround the model.
        var distance = Math.Max(View.Camera.Distance, 1000) * 3;
        _igloo = new Node3D();
        foreach (var photo in doc.Model.Scenes.Select(s => s.Photo).OfType<Dogeometric.Core.View.MatchedPhoto>())
        {
            if (PhotoMatch.Solve(photo) is not { } cam || cam.Eye.DistanceTo(here.Eye) > Math.Max(1, here.Eye.Length * 0.02)
                || PhotoMatch.Corners(photo, distance) is not { } c)
                continue;
            var image = new Image();
            if (image.LoadPngFromBuffer(photo.Image) != Error.Ok && image.LoadJpgFromBuffer(photo.Image) != Error.Ok)
                continue;
            var mesh = new ArrayMesh();
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = new[] { c[0], c[2], c[1], c[0], c[3], c[2] }.Select(Viewport.Space.ToGodot).ToArray();
            arrays[(int)Mesh.ArrayType.TexUV] = new Vector2[] { new(0, 1), new(1, 0), new(1, 1), new(0, 1), new(0, 0), new(1, 0) };
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            var material = new StandardMaterial3D
            {
                AlbedoTexture = ImageTexture.CreateFromImage(image),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            mesh.SurfaceSetMaterial(0, material);
            _igloo.AddChild(new MeshInstance3D { Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
        View.ModelRoot.GetParent().AddChild(_igloo);
        // Looking around keeps the eye, so the photos stay lined up; the single matched photo gives way to them.
        View.ShowPhoto(null);
        RefreshStatus();
    }

    public override void Deactivate()
    {
        _igloo?.QueueFree();
        _igloo = null;
    }

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || _igloo == null)
            return;
        _dragging = true;
        View.BeginNavigation();
    }

    public override void MouseMove(Vector2 position, Vector2 relative)
    {
        if (_dragging)
            View.ChangeCamera(c => c.LookAround(-relative.X * RadiansPerPixel, -relative.Y * RadiansPerPixel));
    }

    public override void MouseUp(MouseButton button, Vector2 position)
    {
        if (button == MouseButton.Left)
            _dragging = false;
    }
}
