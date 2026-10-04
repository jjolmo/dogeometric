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
}
