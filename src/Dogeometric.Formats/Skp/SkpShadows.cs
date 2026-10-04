using System.IO.Compression;
using System.Text;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Formats.Skp;

/// <summary>
/// Reads a .skp's shadow settings: from the model's shadow-info record in SketchUp 2021+ files (time, location,
/// light, dark, what shows), else from the "TempShadowInfo" dictionary some files carry (no time; may be stale).
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
        if (ReadRecord(data) is { } record)
            return record;
        var values = ReadModern(data) ?? ReadLegacy(data);
        return values == null ? null : ToSettings(values);
    }

    /// <summary>SketchUp 2021+ files hold model.dat in a zip (after a short header from 2021 to 2023); older ones are
    /// the model itself.</summary>
    private static byte[] ModelBytes(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var zipStart = bytes.AsSpan(0, Math.Min(bytes.Length, 4096)).IndexOf("PK\u0003\u0004"u8);
        if (zipStart < 0)
            return bytes;
        using var zip = new ZipArchive(new MemoryStream(bytes, zipStart, bytes.Length - zipStart), ZipArchiveMode.Read);
        if (zip.GetEntry("model.dat") is not { } entry)
            return bytes;
        using var s = entry.Open();
        using var m = new MemoryStream();
        s.CopyTo(m);
        return m.ToArray();
    }

    /// <summary>
    /// The shadow-info record of a 2021+ model.dat: tagged values 0x6591 (time, local time written as UTC seconds)
    /// to 0x65A0, identified against SketchUp's own files.
    /// </summary>
    public static ShadowSettings? ReadRecord(byte[] d)
    {
        var at = d.AsSpan().IndexOf(new byte[] { 0x91, 0x65, 4, 0, 0, 0 });
        if (at < 0)
            return null;
        var values = new Dictionary<int, byte[]>();
        for (var i = at; i + 6 <= d.Length;)
        {
            var tag = d[i] | d[i + 1] << 8;
            var size = BitConverter.ToInt32(d, i + 2);
            if (tag is < 0x6591 or > 0x65a0 || size < 0 || i + 6 + size > d.Length)
                break;
            values[tag] = d.AsSpan(i + 6, size).ToArray();
            i += 6 + size;
        }
        if (!values.ContainsKey(0x659f))
            return null;
        bool Flag(int tag, bool fallback) => values.TryGetValue(tag, out var v) && v.Length == 1 ? v[0] != 0 : fallback;
        double Number(int tag, double fallback) => values.TryGetValue(tag, out var v) && v.Length == 8 ? BitConverter.ToDouble(v) : fallback;
        int Integer(int tag, int fallback) => values.TryGetValue(tag, out var v) && v.Length == 4 ? BitConverter.ToInt32(v) : fallback;
        var d0 = new ShadowSettings();
        var north = d0.NorthAngle;
        if (values.TryGetValue(0x6598, out var n) && n.Length == 24)
        {
            var (x, y) = (BitConverter.ToDouble(n, 0), BitConverter.ToDouble(n, 8));
            if (Math.Abs(x) + Math.Abs(y) > 1e-9)
                north = Math.Atan2(x, y) * 180 / Math.PI;
        }
        var time = values.TryGetValue(0x6591, out var t) && t.Length == 4
            ? DateTime.SpecifyKind(DateTimeOffset.FromUnixTimeSeconds(BitConverter.ToInt32(t)).UtcDateTime, DateTimeKind.Unspecified)
            : d0.Time;
        return d0 with
        {
            Time = time,
            Longitude = Number(0x6595, d0.Longitude),
            Latitude = Number(0x6596, d0.Latitude),
            UtcOffset = Number(0x6597, d0.UtcOffset),
            NorthAngle = north,
            Enabled = Flag(0x6599, d0.Enabled),
            OnFaces = Flag(0x659b, d0.OnFaces),
            OnGround = Flag(0x659c, d0.OnGround),
            FromEdges = Flag(0x659d, d0.FromEdges),
            Light = Integer(0x659e, d0.Light),
            Dark = Integer(0x659f, d0.Dark),
            UseSunForShading = Flag(0x65a0, d0.UseSunForShading),
        };
    }

    /// <summary>Zipped model.dat: tagged records, a key (b6 36, length, ASCII) then its value (a4 38 wrapping a typed record).</summary>
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

    /// <summary>Older files: keys as ff fe ff, length, UTF-16 text, then a type byte and the value.</summary>
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
