using System.IO.Compression;
using System.Text;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Formats.Skp;

/// <summary>
/// Reads a .skp's shadow settings from the "TempShadowInfo" dictionary SketchUp keeps with the model (location,
/// light, dark, what shows). The shadow time is not in it, so it keeps its default.
/// </summary>
public static class SkpShadows
{
    public static ShadowSettings? Read(string path)
    {
        byte[] data;
        try
        {
            data = ModelBytes(path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
        var values = ReadModern(data) ?? ReadLegacy(data);
        return values == null ? null : ToSettings(values);
    }

    /// <summary>SketchUp 2021+ files are zips holding model.dat; older ones are the model itself.</summary>
    private static byte[] ModelBytes(string path)
    {
        using var file = File.OpenRead(path);
        var magic = new byte[2];
        file.ReadExactly(magic);
        file.Position = 0;
        if (magic[0] == 'P' && magic[1] == 'K')
        {
            using var zip = new ZipArchive(file, ZipArchiveMode.Read);
            if (zip.GetEntry("model.dat") is { } entry)
            {
                using var s = entry.Open();
                using var m = new MemoryStream();
                s.CopyTo(m);
                return m.ToArray();
            }
        }
        return File.ReadAllBytes(path);
    }

    /// <summary>2021+: tagged records, a key (b6 36, length, ASCII) then its value (a4 38 wrapping a typed record).</summary>
    public static Dictionary<string, object>? ReadModern(byte[] d)
    {
        var at = d.AsSpan().IndexOf("TempShadowInfo"u8);
        if (at < 0)
            return null;
        var i = at + "TempShadowInfo".Length;
        var values = new Dictionary<string, object>();
        while (i + 6 < d.Length)
        {
            // Skip to the next key record; the dictionary ends at its closing record (b7 36).
            if (d[i] == 0xb7 && d[i + 1] == 0x36)
                break;
            if (d[i] != 0xb6 || d[i + 1] != 0x36)
            {
                i++;
                continue;
            }
            var keyLength = BitConverter.ToInt32(d, i + 2);
            if (keyLength <= 0 || keyLength > 64 || i + 6 + keyLength > d.Length)
                break;
            var key = Encoding.ASCII.GetString(d, i + 6, keyLength);
            i += 6 + keyLength;
            if (d[i] != 0xa4 || d[i + 1] != 0x38)
                break;
            var type = d[i + 6];
            var size = BitConverter.ToInt32(d, i + 8);
            var v = i + 12;
            if (v + size > d.Length)
                break;
            values[key] = type switch
            {
                0xad => Encoding.UTF8.GetString(d, v, size),
                0xa7 => BitConverter.ToInt32(d, v),
                0xa9 => BitConverter.ToDouble(d, v),
                0xaa => d[v] != 0,
                _ => (object)"",
            };
            i = v + size;
        }
        return values.Count > 0 ? values : null;
    }

    /// <summary>Before 2021: keys as ff fe ff, length, UTF-16 text, then a type byte and the value.</summary>
    public static Dictionary<string, object>? ReadLegacy(byte[] d)
    {
        var marker = Encoding.Unicode.GetBytes("TempShadowInfo");
        var at = d.AsSpan().IndexOf(marker);
        if (at < 0)
            return null;
        var i = at + marker.Length;
        string? Text(ref int p)
        {
            if (p + 4 > d.Length || d[p] != 0xff || d[p + 1] != 0xfe || d[p + 2] != 0xff)
                return null;
            var n = d[p + 3];
            if (p + 4 + 2 * n > d.Length)
                return null;
            var s = Encoding.Unicode.GetString(d, p + 4, 2 * n);
            p += 4 + 2 * n;
            return s;
        }
        var values = new Dictionary<string, object>();
        while (Text(ref i) is { Length: > 0 } key && i < d.Length)
        {
            var type = d[i++];
            switch (type)
            {
                case 0x07:
                    values[key] = d[i++] != 0;
                    break;
                case 0x04:
                    values[key] = BitConverter.ToInt32(d, i);
                    i += 4;
                    break;
                case 0x06:
                    values[key] = BitConverter.ToDouble(d, i);
                    i += 8;
                    break;
                case 0x0a:
                    values[key] = Text(ref i) ?? "";
                    break;
                default:
                    return values.Count > 0 ? values : null;
            }
        }
        return values.Count > 0 ? values : null;
    }

    private static ShadowSettings ToSettings(Dictionary<string, object> v)
    {
        var d = new ShadowSettings();
        bool Flag(string k, bool fallback) => v.TryGetValue(k, out var x) && x is bool b ? b : fallback;
        double Number(string k, double fallback) => v.TryGetValue(k, out var x) ? x switch { double f => f, int n => n, _ => fallback } : fallback;
        return d with
        {
            Enabled = Flag("DisplayShadows", d.Enabled),
            OnFaces = Flag("DisplayOnAllFaces", d.OnFaces),
            OnGround = Flag("DisplayOnGroundPlane", d.OnGround),
            FromEdges = Flag("EdgesCastShadows", d.FromEdges),
            UseSunForShading = Flag("UseSunForAllShading", d.UseSunForShading),
            Latitude = Number("Latitude", d.Latitude),
            Longitude = Number("Longitude", d.Longitude),
            NorthAngle = Number("NorthAngle", d.NorthAngle),
            UtcOffset = Number("TZOffset", d.UtcOffset),
            Light = (int)Number("Light", d.Light),
            Dark = (int)Number("Dark", d.Dark),
        };
    }
}
