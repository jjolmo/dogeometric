namespace Dogeometric.Core.IO;

/// <summary>
/// A local component collection (Components panel › Open or create a local collection): a folder of models, each
/// one a component to place, browsed folder by folder or searched through.
/// </summary>
public static class ComponentCollection
{
    public sealed record Entry(string Path, string Name, bool IsFolder);

    public static bool IsModel(string path) =>
        System.IO.Path.GetExtension(path).ToLowerInvariant() is ".skp" or ".dog";

    /// <summary>The folder's subfolders, then its models, each by name; hidden ones are left out.</summary>
    public static List<Entry> Browse(string folder)
    {
        if (!Directory.Exists(folder))
            return [];
        var folders = new DirectoryInfo(folder).EnumerateDirectories()
            .Where(d => !IsHidden(d))
            .Select(d => new Entry(d.FullName, d.Name, true));
        var models = new DirectoryInfo(folder).EnumerateFiles()
            .Where(f => !IsHidden(f) && IsModel(f.Name))
            .Select(f => new Entry(f.FullName, System.IO.Path.GetFileNameWithoutExtension(f.Name), false));
        return folders.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .Concat(models.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase))
            .ToList();
    }

    /// <summary>Models anywhere under <paramref name="folder"/> whose name contains <paramref name="text"/>.</summary>
    public static List<Entry> Search(string folder, string text, int limit = 500)
    {
        if (!Directory.Exists(folder))
            return [];
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden };
        return Directory.EnumerateFiles(folder, "*", options)
            .Where(p => IsModel(p) && !p.Split(System.IO.Path.DirectorySeparatorChar).Any(part => part.StartsWith('.'))
                && System.IO.Path.GetFileNameWithoutExtension(p).Contains(text.Trim(), StringComparison.CurrentCultureIgnoreCase))
            .Select(p => new Entry(p, System.IO.Path.GetFileNameWithoutExtension(p), false))
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(limit)
            .ToList();
    }

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>The PNG preview stored in the file (near the start of a .skp, as an entry of a .dog), or null.</summary>
    public static byte[]? Thumbnail(string path)
    {
        if (!File.Exists(path))
            return null;
        if (System.IO.Path.GetExtension(path).Equals(".dog", StringComparison.OrdinalIgnoreCase))
            return DogFile.ReadThumbnail(path);
        if (!System.IO.Path.GetExtension(path).Equals(".skp", StringComparison.OrdinalIgnoreCase))
            return null;
        var head = new byte[64 * 1024];
        int read;
        using (var s = File.OpenRead(path))
            read = s.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        var span = head.AsSpan(0, read);
        var start = span.IndexOf(PngSignature);
        if (start < 0)
            return null;
        var end = span[start..].IndexOf("IEND"u8);
        // The IEND chunk ends with its 4-byte CRC.
        return end < 0 ? null : span.Slice(start, end + 8).ToArray();
    }

    private static bool IsHidden(FileSystemInfo info) => info.Name.StartsWith('.') || info.Attributes.HasFlag(FileAttributes.Hidden);
}
