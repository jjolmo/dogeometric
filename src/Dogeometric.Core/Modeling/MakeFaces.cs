namespace Dogeometric.Core.Modeling;

/// <summary>
/// Make Faces (The Sketchup Dude): creates the faces missing in every closed, flat loop of edges, as drawing those
/// edges again would. Works on the given edges, or on everything in the collection.
/// </summary>
public static class MakeFaces
{
    /// <summary>Faces created in <paramref name="e"/> from <paramref name="edges"/> (all its edges when null).</summary>
    public static int Run(Entities e, IEnumerable<Edge>? edges = null)
    {
        var old = e.Faces.ToHashSet();
        var before = e.Faces.Count;
        var list = (edges ?? e.Edges).Where(e.Edges.Contains).ToList();
        if (list.Count > 0)
            FaceFinder.Update(e, list);
        var created = e.Faces.Where(f => !old.Contains(f)).ToHashSet();
        // New faces closing a solid face outwards, so it is ready for printing; existing faces are left alone.
        var reversed = SolidInspector.Find(e).Where(x => x.Kind == SolidErrorKind.ReversedFace && x.Entities[0] is Face f && created.Contains(f)).ToList();
        SolidInspector.Fix(e, reversed);
        // Faces of a plane are rebuilt as new objects, so count the difference rather than new objects.
        return e.Faces.Count - before;
    }

    /// <summary>The selection's edges, plus everything inside selected groups and components (each definition once).</summary>
    public static int RunOnSelection(Entities context, IReadOnlyCollection<object> selection, Action<Entities> touch)
    {
        if (selection.Count == 0)
            return Run(context);
        var created = Run(context, selection.OfType<Edge>().Concat(selection.OfType<Face>().SelectMany(Topology.EdgesOf)));
        foreach (var def in selection.OfType<ComponentInstance>().Select(i => i.Definition).Distinct())
        {
            touch(def.Entities);
            created += Run(def.Entities);
        }
        return created;
    }
}
