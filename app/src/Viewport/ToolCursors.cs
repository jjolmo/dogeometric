using Godot;
using System.Text.Json;

namespace Dogeometric.App.Viewport;

/// <summary>
/// SketchUp-style tool cursors from res://cursors/&lt;name&gt;.svg, with hotspots from hotspots.json. The viewport shows
/// them through a cursor shape nothing else uses (Help), so the rest of the UI keeps the system cursors.
/// </summary>
public static class ToolCursors
{
    public const Control.CursorShape Shape = Control.CursorShape.Help;

    private static Dictionary<string, Vector2>? _hotspots;
    private static readonly Dictionary<string, Texture2D?> Textures = [];

    /// <summary>Makes <paramref name="name"/> the image of <see cref="Shape"/>; false when there is no such cursor.</summary>
    public static bool Apply(string name)
    {
        if (name == "" || Load(name) is not { } texture)
            return false;
        Input.SetCustomMouseCursor(texture, (Input.CursorShape)Shape, Hotspots().GetValueOrDefault(name, new Vector2(16, 16)));
        return true;
    }

    private static Texture2D? Load(string name)
    {
        if (!Textures.TryGetValue(name, out var texture))
        {
            var path = $"res://cursors/{name}.svg";
            Textures[name] = texture = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
        }
        return texture;
    }

    private static Dictionary<string, Vector2> Hotspots()
    {
        if (_hotspots != null)
            return _hotspots;
        _hotspots = [];
        using var file = Godot.FileAccess.Open("res://cursors/hotspots.json", Godot.FileAccess.ModeFlags.Read);
        if (file == null)
            return _hotspots;
        try
        {
            foreach (var p in JsonDocument.Parse(file.GetAsText()).RootElement.EnumerateObject())
                _hotspots[p.Name] = new Vector2(p.Value[0].GetSingle(), p.Value[1].GetSingle());
        }
        catch (JsonException)
        {
        }
        return _hotspots;
    }
}
