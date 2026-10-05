namespace Dogeometric.Core.Modeling;

/// <summary>The Materials panel's model-wide operations.</summary>
public static class Painting
{
    /// <summary>Delete: the material goes, and what was painted with it goes back to Default, as in SketchUp.</summary>
    public static void DeleteMaterial(Model model, Material material)
    {
        foreach (var e in model.AllEntities)
        {
            foreach (var f in e.Faces)
            {
                if (f.FrontMaterial == material)
                    f.FrontMaterial = null;
                if (f.BackMaterial == material)
                    f.BackMaterial = null;
            }
            foreach (var x in e.Edges.Where(x => x.Material == material))
                x.Material = null;
            foreach (var i in e.Instances.Where(i => i.Material == material))
                i.Material = null;
        }
        model.Materials.Remove(material);
    }

    /// <summary>Purge Unused: removes the materials nothing is painted with. Returns how many went.</summary>
    public static int PurgeMaterials(Model model)
    {
        var used = new HashSet<Material>();
        foreach (var e in model.AllEntities)
        {
            used.UnionWith(e.Faces.SelectMany(f => new[] { f.FrontMaterial, f.BackMaterial }).OfType<Material>());
            used.UnionWith(e.Edges.Select(x => x.Material).OfType<Material>());
            used.UnionWith(e.Instances.Select(i => i.Material).OfType<Material>());
        }
        return model.Materials.RemoveAll(m => !used.Contains(m));
    }
}
