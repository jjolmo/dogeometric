using System.Text;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.View;

namespace Dogeometric.Formats.Skp;

/// <summary>
/// Scenes of pre-2021 .skp files, which OpenSKP does not read: each page record is its name and label (UTF-16), a
/// 32-bit value, then its camera (class reference 0x800A): eye, target and up in inches, 1.0, 1000, perspective,
/// field of view and parallel height. Found by matching pages against what SketchUp reports for the same file.
/// </summary>
public static class SkpLegacyScenes
{
    private const double MmPerInch = 25.4;

    public static List<Scene> Read(byte[] d)
    {
        var scenes = new List<Scene>();
        for (var i = 0; i + 2 + 9 * 8 + 8 * 2 + 1 + 16 <= d.Length; i++)
        {
            if (d[i] != 0x0a || d[i + 1] != 0x80 || Backwards(d, i - 4) is not { } label || Backwards(d, label.Start) is not { } name)
                continue;
            double D(int k) => BitConverter.ToDouble(d, i + 2 + k * 8);
            Vec3 V(int k) => new(D(k), D(k + 1), D(k + 2));
            var (eye, target, up) = (V(0), V(3), V(6));
            if (Math.Abs(up.Length - 1) > 1e-6 || eye.DistanceTo(target) < 1e-9 || name.Text.Length == 0)
                continue;
            var at = i + 2 + 9 * 8 + 8 * 2;
            var perspective = d[at] != 0;
            var fov = BitConverter.ToDouble(d, at + 1);
            var orthoHeight = BitConverter.ToDouble(d, at + 9);
            if (fov is <= 0 or >= 180)
                continue;
            scenes.Add(new Scene
            {
                Name = name.Text,
                Saves = SceneProperties.Camera | SceneProperties.VisibleTags,
                Camera = new CameraState(eye * MmPerInch, target * MmPerInch, up, perspective, fov, orthoHeight * MmPerInch),
            });
        }
        return scenes;
    }

    /// <summary>The UTF-16 string ending at <paramref name="end"/> (exclusive), written as ff fe ff, length, text.</summary>
    private static (string Text, int Start)? Backwards(byte[] d, int end)
    {
        for (var n = 0; n <= 120; n++)
        {
            var start = end - 2 * n - 4;
            if (start < 0)
                return null;
            if (d[start] == 0xff && d[start + 1] == 0xfe && d[start + 2] == 0xff && d[start + 3] == n)
                return (Encoding.Unicode.GetString(d, start + 4, 2 * n), start);
        }
        return null;
    }
}
