using System.Text;
using Dogeometric.Core.Geometry;
using Dogeometric.Formats.Skp;

namespace Dogeometric.Formats.Tests;

public class SkpLegacyScenesTests
{
    private static void Text(List<byte> b, string s)
    {
        b.AddRange([0xff, 0xfe, 0xff, (byte)s.Length]);
        b.AddRange(Encoding.Unicode.GetBytes(s));
    }

    private static void Page(List<byte> b, string name, Vec3 eye, Vec3 target, Vec3 up, bool perspective, double fov)
    {
        Text(b, name);
        Text(b, "");
        b.AddRange(BitConverter.GetBytes(0x7f));
        b.AddRange([0x0a, 0x80]);
        foreach (var v in new[] { eye, target, up })
            foreach (var c in new[] { v.X, v.Y, v.Z })
                b.AddRange(BitConverter.GetBytes(c));
        b.AddRange(BitConverter.GetBytes(1.0));
        b.AddRange(BitConverter.GetBytes(1000.0));
        b.Add(perspective ? (byte)1 : (byte)0);
        b.AddRange(BitConverter.GetBytes(fov));
        b.AddRange(BitConverter.GetBytes(100.0));
        b.AddRange(new byte[32]);
    }

    [Fact]
    public void Pre_2021_pages_give_named_scenes_with_their_cameras()
    {
        var b = new List<byte> { 1, 2, 3 };
        Page(b, "Front", new Vec3(0, -100, 10), new Vec3(0, 0, 10), Vec3.UnitZ, true, 35);
        Page(b, "Plan", new Vec3(0, 0, 200), Vec3.Zero, Vec3.UnitY, false, 30);
        var scenes = SkpLegacyScenes.Read([.. b]);
        Assert.Equal(["Front", "Plan"], scenes.Select(s => s.Name));
        var front = scenes[0].Camera!.Value;
        Assert.Equal(new Vec3(0, -2540, 254), front.Eye);
        Assert.True(front.Perspective);
        Assert.Equal(35, front.FovDegrees);
        Assert.False(scenes[1].Camera!.Value.Perspective);
        Assert.Equal(2540, scenes[1].Camera!.Value.OrthoHeight, 6);
    }
}
