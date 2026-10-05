using System.Globalization;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Picking;

namespace Dogeometric.App.Diagnostics;

/// <summary>Entities in words for the journal and problem reports: what they are, where they sit (inside which
/// groups or components) and their world coordinates in millimetres.</summary>
public static class Describe
{
    public static string P(Vec3 p) => string.Create(CultureInfo.InvariantCulture, $"({p.X:0.##}, {p.Y:0.##}, {p.Z:0.##})");

    public static string Hit(PickHit? hit) => hit == null ? "nothing (empty space)" : Entity(hit.Entity, hit.Path) + " at " + P(hit.Point);

    /// <summary>One entity seen through the instances <paramref name="path"/> that hold it.</summary>
    public static string Entity(object entity, IReadOnlyList<ComponentInstance> path)
    {
        var xf = Transform.Identity;
        foreach (var i in path)
            xf = i.Transform.Then(xf);
        var inside = path.Count == 0 ? "" : " inside " + string.Join(" › ", path.Select(i =>
            $"{(i.Definition.IsGroup ? "group" : "component")} '{(i.Name.Length > 0 ? i.Name : i.Definition.Name)}'"));
        var owner = path.Count > 0 ? path[^1].Definition.Entities : null;
        string Index<T>(List<T> list, T item) => owner == null ? "" : $" #{list.IndexOf(item)}";
        return entity switch
        {
            Face f => FaceText(f, xf, owner == null ? "" : Index(owner.Faces, f), inside),
            Edge e => $"edge{Index(owner?.Edges ?? [], e)} {P(xf.ApplyPoint(e.Start.Position))} → {P(xf.ApplyPoint(e.End.Position))}" +
                      $"{(e.Flags != EdgeFlags.None ? $" [{e.Flags}]" : "")}{inside}",
            ComponentInstance c => $"{(c.Definition.IsGroup ? "group" : "component")} '{(c.Name.Length > 0 ? c.Name : c.Definition.Name)}' " +
                                   $"({c.Definition.Entities.Faces.Count} faces, {c.Definition.Entities.Edges.Count} edges, origin {P(xf.ApplyPoint(c.Transform.Origin))}){inside}",
            GuideLine g => $"guide line through {P(xf.ApplyPoint(g.Point))}{inside}",
            GuidePoint g => $"guide point {P(xf.ApplyPoint(g.Position))}{inside}",
            _ => $"{entity.GetType().Name}{inside}",
        };
    }

    private static string FaceText(Face f, Transform xf, string index, string inside)
    {
        var corners = f.OuterLoop.Points.ToList();
        var area = string.Create(CultureInfo.InvariantCulture, $"{Polygon.Area(corners):0.##}");
        return $"face{index} ({corners.Count} corners{(f.Loops.Count > 1 ? $", {f.Loops.Count - 1} holes" : "")}, " +
               $"normal {P(xf.ApplyNormal(f.Normal).Normalized())}, area {area} mm², " +
               $"corners {string.Join(" ", corners.Take(12).Select(p => P(xf.ApplyPoint(p))))}{(corners.Count > 12 ? " …" : "")}" +
               $"{(f.FrontMaterial != null ? $", front '{f.FrontMaterial.Name}'" : "")}{(f.BackMaterial != null ? $", back '{f.BackMaterial.Name}'" : "")}){inside}";
    }
}
