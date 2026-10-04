using System.Text;
using Dogeometric.Formats.Skp;

namespace Dogeometric.Formats.Tests;

public class SkpShadowsTests
{
    private static byte[] Legacy()
    {
        var b = new List<byte>();
        void Text(string s)
        {
            b.AddRange([0xff, 0xfe, 0xff, (byte)s.Length]);
            b.AddRange(Encoding.Unicode.GetBytes(s));
        }
        b.AddRange(Encoding.Unicode.GetBytes("TempShadowInfo"));
        Text("City");
        b.Add(0x0a);
        Text("Boulder (CO)");
        Text("Dark");
        b.Add(0x04);
        b.AddRange(BitConverter.GetBytes(45));
        Text("DisplayShadows");
        b.AddRange([0x07, 0x01]);
        Text("Latitude");
        b.Add(0x06);
        b.AddRange(BitConverter.GetBytes(40.0183));
        b.AddRange([0xff, 0xfe, 0xff, 0x00]);
        return [.. b];
    }

    private static byte[] Modern()
    {
        var b = new List<byte>();
        b.AddRange("TempShadowInfo"u8.ToArray());
        void Value(string key, byte type, byte[] value)
        {
            b.AddRange([0xb6, 0x36]);
            b.AddRange(BitConverter.GetBytes(key.Length));
            b.AddRange(Encoding.ASCII.GetBytes(key));
            b.AddRange([0xa4, 0x38]);
            b.AddRange(BitConverter.GetBytes(6 + value.Length));
            b.AddRange([type, 0x38]);
            b.AddRange(BitConverter.GetBytes(value.Length));
            b.AddRange(value);
        }
        Value("Light", 0xa7, BitConverter.GetBytes(60));
        Value("TZOffset", 0xa9, BitConverter.GetBytes(-8.0));
        Value("DisplayShadows", 0xaa, [1]);
        b.AddRange([0xb7, 0x36, 4, 0, 0, 0]);
        return [.. b];
    }

    [Fact]
    public void The_pre_2021_dictionary_is_read()
    {
        var v = SkpShadows.ReadLegacy(Legacy())!;
        Assert.Equal("Boulder (CO)", v["City"]);
        Assert.Equal(45, v["Dark"]);
        Assert.Equal(true, v["DisplayShadows"]);
        Assert.Equal(40.0183, (double)v["Latitude"], 6);
    }

    [Fact]
    public void The_2021_dictionary_is_read()
    {
        var v = SkpShadows.ReadModern(Modern())!;
        Assert.Equal(60, v["Light"]);
        Assert.Equal(-8.0, v["TZOffset"]);
        Assert.Equal(true, v["DisplayShadows"]);
    }

    [Fact]
    public void The_shadow_info_record_gives_time_place_and_switches()
    {
        var b = new List<byte>();
        void Value(int tag, byte[] v)
        {
            b.AddRange([(byte)(tag & 0xff), (byte)(tag >> 8)]);
            b.AddRange(BitConverter.GetBytes(v.Length));
            b.AddRange(v);
        }
        b.AddRange([1, 2, 3]);
        Value(0x6591, BitConverter.GetBytes((int)new DateTimeOffset(2013, 11, 8, 13, 30, 0, TimeSpan.Zero).ToUnixTimeSeconds()));
        Value(0x6592, [0]);
        Value(0x6595, BitConverter.GetBytes(2.17));
        Value(0x6596, BitConverter.GetBytes(41.38));
        Value(0x6597, BitConverter.GetBytes(1.0));
        Value(0x6598, [.. BitConverter.GetBytes(1.0), .. BitConverter.GetBytes(0.0), .. BitConverter.GetBytes(0.0)]);
        Value(0x6599, [1]);
        Value(0x659a, [0]);
        Value(0x659b, [1]);
        Value(0x659c, [0]);
        Value(0x659d, [1]);
        Value(0x659e, BitConverter.GetBytes(70));
        Value(0x659f, BitConverter.GetBytes(30));
        Value(0x65a0, [1]);
        var s = SkpShadows.ReadRecord([.. b])!;
        Assert.Equal(new DateTime(2013, 11, 8, 13, 30, 0), s.Time);
        Assert.Equal(41.38, s.Latitude);
        Assert.Equal(1.0, s.UtcOffset);
        Assert.Equal(90, s.NorthAngle, 9);
        Assert.True(s.Enabled && s.OnFaces && !s.OnGround && s.FromEdges && s.UseSunForShading);
        Assert.Equal((70, 30), (s.Light, s.Dark));
    }

    [Fact]
    public void SketchUps_3d_printing_template_has_its_default_shadows()
    {
        var file = Directory.Exists(CorpusDir)
            ? Directory.EnumerateFiles(CorpusDir, "Temp08a - 3D Printing.skp", SearchOption.AllDirectories).FirstOrDefault()
            : null;
        if (file == null)
            return;
        var s = SkpShadows.Read(file)!;
        Assert.Equal((11, 8, 13, 30), (s.Time.Month, s.Time.Day, s.Time.Hour, s.Time.Minute));
        Assert.Equal(-7, s.UtcOffset);
        Assert.False(s.Enabled);
        Assert.Equal((80, 45), (s.Light, s.Dark));
    }

    private static readonly string CorpusDir = Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "", "dogeometric-corpus");
}
