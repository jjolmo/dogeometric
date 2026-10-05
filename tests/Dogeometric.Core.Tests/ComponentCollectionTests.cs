using Dogeometric.Core.IO;

namespace Dogeometric.Core.Tests;

public class ComponentCollectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dog-collection-" + Guid.NewGuid().ToString("N"));

    public ComponentCollectionTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Screws", "M3"));
        Directory.CreateDirectory(Path.Combine(_root, ".cache"));
        File.WriteAllText(Path.Combine(_root, "standoff.dog"), "");
        File.WriteAllText(Path.Combine(_root, "Board.skp"), "");
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "");
        File.WriteAllText(Path.Combine(_root, ".hidden.skp"), "");
        File.WriteAllText(Path.Combine(_root, "Screws", "M3", "M3 screw 10mm.skp"), "");
        File.WriteAllText(Path.Combine(_root, ".cache", "screw copy.skp"), "");
    }

    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void Browse_lists_folders_then_models_and_skips_hidden_and_other_files()
    {
        var entries = ComponentCollection.Browse(_root);
        Assert.Equal(["Screws", "Board", "standoff"], entries.Select(e => e.Name));
        Assert.True(entries[0].IsFolder);
        Assert.False(entries[1].IsFolder);
    }

    [Fact]
    public void Search_finds_models_in_subfolders_by_part_of_the_name()
    {
        var found = ComponentCollection.Search(_root, "SCREW");
        Assert.Equal(Path.Combine(_root, "Screws", "M3", "M3 screw 10mm.skp"), Assert.Single(found).Path);
        Assert.Empty(ComponentCollection.Search(_root, "washer"));
    }

    [Fact]
    public void Thumbnail_is_the_png_near_the_start_of_a_skp()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, (byte)'I', (byte)'E', (byte)'N', (byte)'D', 9, 9, 9, 9];
        var path = Path.Combine(_root, "Board.skp");
        File.WriteAllBytes(path, [0xFF, 0xFE, 0xFF, 7, .. png, 0x42, 0x42]);
        Assert.Equal(png, ComponentCollection.Thumbnail(path));
        Assert.Null(ComponentCollection.Thumbnail(Path.Combine(_root, "standoff.dog")));
        File.WriteAllBytes(path, [1, 2, 3]);
        Assert.Null(ComponentCollection.Thumbnail(path));
    }
}
