using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Units;
using Godot;

namespace Dogeometric.App.UI;

/// <summary>A model to start from: built in (units and precision) or saved by the user (a .dog file).</summary>
public sealed record Template(string Name, string Description, string? Path = null, LengthUnit Units = LengthUnit.Millimeters, int Precision = 1);

/// <summary>SketchUp's templates: File › New starts from the default one, New From Template from any.</summary>
public static class Templates
{
    public static string Folder => ProjectSettings.GlobalizePath("user://Templates");

    public static readonly IReadOnlyList<Template> BuiltIn =
    [
        new("Simple Template - Millimeters", "Units: Millimeters. Plain style for general modelling."),
        new("Simple Template - Centimeters", "Units: Centimeters. Plain style for general modelling.", Units: LengthUnit.Centimeters),
        new("Simple Template - Meters", "Units: Meters. Plain style for general modelling.", Units: LengthUnit.Meters, Precision: 2),
        new("3D Printing - Millimeters", "Units: Millimeters, to a tenth. For models to be printed.", Precision: 1),
    ];

    public static List<Template> All()
    {
        var all = BuiltIn.ToList();
        if (Directory.Exists(Folder))
            foreach (var file in Directory.GetFiles(Folder, "*.dog").Order())
            {
                var notes = System.IO.Path.ChangeExtension(file, ".txt");
                all.Add(new(System.IO.Path.GetFileNameWithoutExtension(file), File.Exists(notes) ? File.ReadAllText(notes) : "", file));
            }
        return all;
    }

    public static Template Default => All().FirstOrDefault(t => t.Name == AppPreferences.Current.DefaultTemplate) ?? BuiltIn[0];

    public static Model Create(Template t) => t.Path != null
        ? DogFile.Load(t.Path)
        : new Model { Units = t.Units, UnitPrecision = t.Precision };

    /// <summary>File › Save As Template: the model as a template named <paramref name="name"/>.</summary>
    public static string Save(Model model, string name, string description)
    {
        Directory.CreateDirectory(Folder);
        var safe = string.Concat(name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        DogFile.Save(model, System.IO.Path.Combine(Folder, safe + ".dog"));
        File.WriteAllText(System.IO.Path.Combine(Folder, safe + ".txt"), description);
        return safe;
    }
}
