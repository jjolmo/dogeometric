using Dogeometric.Core.Geometry;
using Dogeometric.Core.Picking;

namespace Dogeometric.Core.Modeling;

/// <summary>
/// An open model with its editing state: selection, active context, undo history and pick acceleration.
/// Tools change geometry only through <see cref="Operation"/> so undo and redraw stay in sync.
/// </summary>
public sealed class Document
{
    public Model Model { get; }
    public Selection Selection { get; } = new();
    public EditContext Context { get; }
    public UndoStack Undo { get; }
    public Picker Picker { get; } = new();

    /// <summary>
    /// Raised after geometry changed (operation, undo, redo) with the entity collections that changed, so renderers
    /// and pickers refresh only those.
    /// </summary>
    public event Action<IReadOnlyCollection<Entities>>? GeometryChanged;

    public Document(Model model)
    {
        Model = model;
        Context = new EditContext(model);
        Undo = new UndoStack(model);
        Undo.Changed += () =>
        {
            foreach (var e in Undo.LastTouched)
                Picker.Invalidate(e);
            // Undo can bring back or remove entities; drop selected items that are no longer in the context.
            var c = Context.Entities;
            var present = new HashSet<object>(c.Faces.Cast<object>().Concat(c.Edges).Concat(c.Instances)
                .Concat(c.Dimensions).Concat(c.Texts).Concat(c.SectionPlanes).Concat(c.GuideLines).Concat(c.GuidePoints));
            if (Selection.Items.Any(i => !present.Contains(i)))
                Selection.Set(Selection.Items.Where(present.Contains).ToList());
            GeometryChanged?.Invoke(Undo.LastTouched);
        };
    }

    /// <summary>Runs <paramref name="change"/> as one undoable step touching the active context.</summary>
    public void Operation(string name, Action<Entities> change)
    {
        var entities = Context.Entities;
        Undo.Begin(name, entities);
        try
        {
            change(entities);
            Undo.Commit();
        }
        catch
        {
            Undo.Abort();
            throw;
        }
    }

    /// <summary>
    /// A live preview of an operation (Push/Pull while dragging): each call undoes the previous preview and applies
    /// <paramref name="change"/> to the real geometry, so the view shows the actual result. The picker keeps the
    /// geometry from before the operation, so inference doesn't snap to what is being changed. Finish with
    /// <see cref="CommitPreview"/> or <see cref="CancelPreview"/>.
    /// </summary>
    public void Preview(string name, Action<Entities> change)
    {
        var entities = Context.Entities;
        if (!Undo.IsPending)
            Undo.Begin(name, entities);
        else
            Undo.Revert();
        try
        {
            change(entities);
        }
        catch
        {
            Undo.Revert();
            throw;
        }
        GeometryChanged?.Invoke(Undo.PendingTouched);
    }

    public void CommitPreview()
    {
        if (Undo.IsPending)
            Undo.Commit();
    }

    public void CancelPreview()
    {
        if (Undo.IsPending)
            Undo.Abort();
    }

    /// <summary>Edit › Delete: erases the selection the way SketchUp does.</summary>
    /// <summary>
    /// Opens a group or component for editing. A group whose definition other copies share becomes unique first,
    /// so editing one copy leaves the others alone, as in SketchUp.
    /// </summary>
    public void Edit(ComponentInstance inst)
    {
        if (inst.IsGroup && Grouping.InstanceCount(Model, inst.Definition) > 1)
            Operation("Make Group Unique", _ => Grouping.MakeUnique(Model, inst));
        Context.Enter(inst);
    }

    public void EraseSelection()
    {
        if (Selection.IsEmpty)
            return;
        var items = Selection.Items.ToList();
        Operation("Erase", e => Editing.Erase(e, items));
        Selection.Clear();
    }
}

/// <summary>Geometry edits shared by tools and commands.</summary>
public static class Editing
{
    /// <summary>
    /// Removes instances, faces and edges. Erasing an edge also erases the faces it bounds, unless they are coplanar
    /// and heal into one; erasing a face keeps its edges. Vertices no edge uses any more are dropped.
    /// </summary>
    public static void Erase(Entities e, IEnumerable<object> items)
    {
        var set = items.ToHashSet();
        var edges = set.OfType<Edge>().ToHashSet();
        e.Instances.RemoveAll(set.Contains);
        e.Dimensions.RemoveAll(set.Contains);
        e.Texts.RemoveAll(set.Contains);
        e.SectionPlanes.RemoveAll(set.Contains);
        if (e.ActiveSection is { } active && set.Contains(active))
            e.ActiveSection = null;
        // An edge between two coplanar faces facing the same way, with the same materials, heals them into one.
        foreach (var edge in edges.ToList())
        {
            var faces = Topology.FacesOf(e, edge).ToList();
            if (faces.Count == 2 && faces[0] != faces[1] && !faces.Any(set.Contains) && Coplanar(faces[0], faces[1])
                && faces[0].FrontMaterial == faces[1].FrontMaterial && faces[0].BackMaterial == faces[1].BackMaterial
                && FaceFinder.Merge(e, faces[0], faces[1], edge) != null)
                edges.Remove(edge);
        }
        e.Faces.RemoveAll(f => set.Contains(f) || f.Loops.Any(l => l.Edges.Any(x => edges.Contains(x.Edge))));
        e.Edges.RemoveAll(edges.Contains);
        RemoveOrphanVertices(e);
    }

    private static bool Coplanar(Face a, Face b)
    {
        var n = a.Normal.Normalized();
        return n.Dot(b.Normal.Normalized()) > 1 - 1e-9 && b.OuterLoop.Points.All(p => Math.Abs((p - a.OuterLoop.Points.First()).Dot(n)) < Tolerance.Length);
    }

    /// <summary>
    /// SketchUp's Soften/Smooth Edges: every edge between two faces meeting at less than
    /// <paramref name="maxDegrees"/> becomes soft and smooth. Returns how many changed.
    /// </summary>
    public static int SoftenByAngle(Entities e, IEnumerable<Edge> edges, double maxDegrees = 20)
    {
        var cos = Math.Cos(maxDegrees * Math.PI / 180);
        var changed = 0;
        foreach (var edge in edges)
        {
            var faces = Topology.FacesOf(e, edge).Take(3).ToList();
            if (faces.Count != 2 || edge.Flags.HasFlag(EdgeFlags.Soft))
                continue;
            if (faces[0].Normal.Dot(faces[1].Normal) >= cos)
            {
                edge.Flags |= EdgeFlags.Soft | EdgeFlags.Smooth;
                changed++;
            }
        }
        return changed;
    }

    /// <summary>Soften Edges: edges turning by at most <paramref name="maxDegrees"/> soft (smooth too if asked), steeper ones hard;
    /// coplanar ones only with <paramref name="softenCoplanar"/>. Returns how many are soft.</summary>
    public static int SoftenEdges(Entities e, IEnumerable<Edge> edges, double maxDegrees, bool smoothNormals, bool softenCoplanar)
    {
        var soft = 0;
        foreach (var edge in edges)
        {
            var faces = Topology.FacesOf(e, edge).Take(3).ToList();
            if (faces.Count != 2)
                continue;
            var cos = Math.Clamp(faces[0].Normal.Normalized().Dot(faces[1].Normal.Normalized()), -1, 1);
            var angle = Math.Acos(Math.Abs(cos)) * 180 / Math.PI;
            var coplanar = angle < 0.01;
            var on = coplanar ? softenCoplanar : angle <= maxDegrees;
            edge.Flags &= ~(EdgeFlags.Soft | EdgeFlags.Smooth);
            if (on)
            {
                edge.Flags |= EdgeFlags.Soft | (smoothNormals ? EdgeFlags.Smooth : EdgeFlags.None);
                soft++;
            }
        }
        return soft;
    }

    public static void RemoveOrphanVertices(Entities e)
    {
        var used = new HashSet<Vertex>();
        foreach (var edge in e.Edges)
        {
            used.Add(edge.Start);
            used.Add(edge.End);
        }
        e.Vertices.RemoveAll(v => !used.Contains(v));
    }
}

/// <summary>Move/copy of entities within one collection (Move tool).</summary>
public static class Transforming
{
    /// <summary>
    /// Moves the given faces, edges and instances by <paramref name="offset"/>. Faces and edges move their
    /// vertices, so connected geometry stretches, as SketchUp's Move does.
    /// </summary>
    public static void Move(Entities e, IEnumerable<object> items, Geometry.Vec3 offset) =>
        Apply(e, items, Geometry.Transform.Translation(offset));

    /// <summary>
    /// Scales by (<paramref name="sx"/>, <paramref name="sy"/>, <paramref name="sz"/>) along the axes of
    /// <paramref name="frame"/> about <paramref name="anchor"/> (a point in frame coordinates). The result works in
    /// the coordinates <paramref name="frame"/> maps to, as the Scale tool's box is aligned with a group's axes.
    /// </summary>
    public static Geometry.Transform Scale(Geometry.Transform frame, Geometry.Vec3 anchor, double sx, double sy, double sz) =>
        frame.Inverse()
            .Then(Geometry.Transform.Translation(-anchor))
            .Then(Geometry.Transform.Scaling(sx, sy, sz))
            .Then(Geometry.Transform.Translation(anchor))
            .Then(frame);

    /// <summary>
    /// Flip Along: mirrors the items along one axis (0 red, 1 green, 2 blue) about their middle. A lone group or
    /// component flips along its own axes, anything else along <paramref name="axes"/> (the drawing axes).
    /// </summary>
    public static void Flip(Entities e, IReadOnlyList<object> items, int axis, Geometry.Transform axes)
    {
        Geometry.Transform frame;
        Geometry.Bounds3 box;
        if (items is [ComponentInstance inst])
        {
            frame = inst.Transform;
            box = inst.Definition.Entities.Bounds();
        }
        else
        {
            frame = axes;
            var toFrame = axes.Inverse();
            var points = new List<Geometry.Vec3>();
            foreach (var item in items)
            {
                switch (item)
                {
                    case Face f:
                        points.AddRange(f.OuterLoop.Points);
                        break;
                    case Edge edge:
                        points.Add(edge.Start.Position);
                        points.Add(edge.End.Position);
                        break;
                    case ComponentInstance ci:
                        var b = ci.Definition.Entities.Bounds();
                        if (!b.IsEmpty)
                            for (var i = 0; i < 8; i++)
                                points.Add(ci.Transform.ApplyPoint(new Geometry.Vec3((i & 1) == 0 ? b.Min.X : b.Max.X, (i & 2) == 0 ? b.Min.Y : b.Max.Y, (i & 4) == 0 ? b.Min.Z : b.Max.Z)));
                        break;
                }
            }
            box = Geometry.Bounds3.FromPoints(points.Select(toFrame.ApplyPoint));
        }
        if (box.IsEmpty)
            return;
        var t = Scale(frame, box.Center, axis == 0 ? -1 : 1, axis == 1 ? -1 : 1, axis == 2 ? -1 : 1);
        Apply(e, items, t);
    }

    public static void Apply(Entities e, IEnumerable<object> items, Geometry.Transform t)
    {
        var vertices = new HashSet<Vertex>();
        foreach (var item in items)
        {
            switch (item)
            {
                case Face f:
                    foreach (var v in f.Loops.SelectMany(l => l.Vertices))
                        vertices.Add(v);
                    // A mirroring transform turns loose faces inside out; flip them back, as SketchUp does.
                    // The outside stays the outside, so the materials keep their sides.
                    if (t.Determinant < 0)
                    {
                        FaceFinder.Reverse(f);
                        (f.FrontMaterial, f.BackMaterial) = (f.BackMaterial, f.FrontMaterial);
                    }
                    break;
                case Edge edge:
                    vertices.Add(edge.Start);
                    vertices.Add(edge.End);
                    break;
                case ComponentInstance inst:
                    inst.Transform = inst.Transform.Then(t);
                    break;
                case SectionPlane s:
                    s.Point = t.ApplyPoint(s.Point);
                    s.Normal = t.ApplyNormal(s.Normal).Normalized();
                    break;
                case LinearDimension d:
                    d.Start = t.ApplyPoint(d.Start);
                    d.End = t.ApplyPoint(d.End);
                    d.Offset = t.ApplyVector(d.Offset);
                    break;
                case TextLabel x when x.ScreenPosition == null:
                    x.Point = t.ApplyPoint(x.Point);
                    x.Offset = t.ApplyVector(x.Offset);
                    break;
            }
        }
        foreach (var v in vertices)
            v.Position = t.ApplyPoint(v.Position);
    }

    /// <summary>
    /// Copies the given entities transformed by <paramref name="t"/> into the same collection; copied loose
    /// geometry sticks to what it lands on. Returns the copies (for selection).
    /// </summary>
    public static List<object> Copy(Entities e, IEnumerable<object> items, Geometry.Transform t)
    {
        var list = items.ToList();
        var copies = new List<object>();
        foreach (var inst in list.OfType<ComponentInstance>())
        {
            var c = e.AddInstance(inst.Definition, inst.Transform.Then(t));
            c.Name = inst.Name;
            c.Tag = inst.Tag;
            c.Material = inst.Material;
            if (inst.GluedTo != null)
                c.GluedTo = Gluing.FaceUnder(e, c);
            copies.Add(c);
        }

        var faces = list.OfType<Face>().ToList();
        var edges = list.OfType<Edge>().Concat(faces.SelectMany(Topology.EdgesOf)).Distinct().ToList();
        var before = e.Faces.ToHashSet();
        var newEdges = new List<Edge>();
        foreach (var edge in edges)
            newEdges.AddRange(StickyGeometry.AddSegment(e, t.ApplyPoint(edge.Start.Position), t.ApplyPoint(edge.End.Position)));
        // Faces come back where the copied faces were: their loops close, and FaceFinder fills them.
        FaceFinder.Update(e, newEdges);
        foreach (var f in e.Faces.Where(f => !before.Contains(f)))
        {
            // Copy materials from the face it came from (same shape, moved).
            var centre = f.OuterLoop.Points.Aggregate(Geometry.Vec3.Zero, (a, p) => a + p) / f.OuterLoop.Edges.Count;
            var source = faces.FirstOrDefault(s => t.ApplyPoint(s.OuterLoop.Points.Aggregate(Geometry.Vec3.Zero, (a, p) => a + p) / s.OuterLoop.Edges.Count).DistanceTo(centre) < 1e-3);
            if (source != null)
            {
                if (f.Normal.Dot(t.ApplyNormal(source.Normal)) < 0)
                    FaceFinder.Reverse(f);
                f.FrontMaterial = source.FrontMaterial;
                f.BackMaterial = source.BackMaterial;
                f.Tag = source.Tag;
            }
            copies.Add(f);
        }
        copies.AddRange(newEdges);

        foreach (var d in list.OfType<LinearDimension>())
        {
            var c = new LinearDimension(t.ApplyPoint(d.Start), t.ApplyPoint(d.End), t.ApplyVector(d.Offset)) { Text = d.Text, Tag = d.Tag };
            e.Dimensions.Add(c);
            copies.Add(c);
        }
        foreach (var x in list.OfType<TextLabel>())
        {
            var c = new TextLabel(x.Text) { Point = t.ApplyPoint(x.Point), Offset = t.ApplyVector(x.Offset), ScreenPosition = x.ScreenPosition, Tag = x.Tag };
            e.Texts.Add(c);
            copies.Add(c);
        }
        foreach (var g in list.OfType<GuideLine>())
        {
            var c = new GuideLine(t.ApplyPoint(g.Point), t.ApplyVector(g.Direction))
            {
                Start = g.Start is { } s ? t.ApplyPoint(s) : null,
                End = g.End is { } en ? t.ApplyPoint(en) : null,
            };
            e.GuideLines.Add(c);
            copies.Add(c);
        }
        foreach (var g in list.OfType<GuidePoint>())
        {
            var c = new GuidePoint(t.ApplyPoint(g.Position));
            e.GuidePoints.Add(c);
            copies.Add(c);
        }
        return copies;
    }
}

/// <summary>Make Group, Make Component and Explode.</summary>
public static class Grouping
{
    /// <summary>
    /// SketchUp's Change Axes: <paramref name="axes"/> (in the instance's parent space) become the definition's
    /// axes. Its contents move into them and every instance compensates, so nothing moves on screen.
    /// </summary>
    public static void ChangeAxes(Model model, ComponentInstance inst, Transform axes)
    {
        var def = inst.Definition;
        var frame = axes.Then(inst.Transform.Inverse());
        var toNew = frame.Inverse();
        var e = def.Entities;
        Transforming.Apply(e, e.Faces.Cast<object>().Concat(e.Edges).Concat(e.Instances).Concat(e.SectionPlanes).Concat(e.Dimensions).Concat(e.Texts).ToList(), toNew);
        foreach (var v in e.Vertices.Where(v => !e.Edges.Any(x => x.Start == v || x.End == v)))
            v.Position = toNew.ApplyPoint(v.Position);
        foreach (var g in e.GuideLines)
        {
            g.Point = toNew.ApplyPoint(g.Point);
            g.Direction = toNew.ApplyVector(g.Direction).Normalized();
            if (g.Start is { } a && g.End is { } b)
                (g.Start, g.End) = (toNew.ApplyPoint(a), toNew.ApplyPoint(b));
        }
        foreach (var g in e.GuidePoints)
            g.Position = toNew.ApplyPoint(g.Position);
        foreach (var other in model.Definitions.Select(d => d.Entities).Append(model.Entities).SelectMany(x => x.Instances).Where(x => x.Definition == def))
            other.Transform = frame.Then(other.Transform);
    }

    /// <summary>
    /// Copies everything in <paramref name="src"/> into <paramref name="dst"/> transformed by <paramref name="t"/>,
    /// keeping topology, attributes and nested instances (which share their definitions). Returns the new top-level
    /// entities (faces, edges, instances, dimensions, texts, guides).
    /// </summary>
    public static List<object> CopyEntities(Entities src, Entities dst, Geometry.Transform t)
    {
        var created = new List<object>();
        var vmap = new Dictionary<Vertex, Vertex>();
        var emap = new Dictionary<Edge, Edge>();
        Vertex V(Vertex v) => vmap.TryGetValue(v, out var c) ? c : vmap[v] = dst.AddVertex(t.ApplyPoint(v.Position));
        foreach (var e in src.Edges)
        {
            var c = dst.AddEdge(V(e.Start), V(e.End));
            c.Flags = e.Flags;
            c.Tag = e.Tag;
            c.Material = e.Material;
            c.Curve = e.Curve;
            emap[e] = c;
            created.Add(c);
        }
        var mirrored = t.Determinant < 0;
        foreach (var f in src.Faces)
        {
            var c = new Face { FrontMaterial = f.FrontMaterial, BackMaterial = f.BackMaterial, Tag = f.Tag, Hidden = f.Hidden };
            foreach (var l in f.Loops)
            {
                var loop = new FaceLoop();
                loop.Edges.AddRange(l.Edges.Select(x => (emap[x.Edge], x.Reversed)));
                c.Loops.Add(loop);
            }
            dst.Faces.Add(c);
            if (mirrored)
            {
                FaceFinder.Reverse(c);
                (c.FrontMaterial, c.BackMaterial) = (c.BackMaterial, c.FrontMaterial);
            }
            created.Add(c);
        }
        foreach (var i in src.Instances)
        {
            var c = dst.AddInstance(i.Definition, i.Transform.Then(t));
            c.Name = i.Name;
            c.Material = i.Material;
            c.Tag = i.Tag;
            c.Hidden = i.Hidden;
            c.Locked = i.Locked;
            created.Add(c);
        }
        foreach (var d in src.Dimensions)
        {
            var c = new LinearDimension(t.ApplyPoint(d.Start), t.ApplyPoint(d.End), t.ApplyVector(d.Offset)) { Text = d.Text, Tag = d.Tag, Hidden = d.Hidden };
            dst.Dimensions.Add(c);
            created.Add(c);
        }
        foreach (var x in src.Texts)
        {
            var c = new TextLabel(x.Text) { Point = t.ApplyPoint(x.Point), Offset = t.ApplyVector(x.Offset), ScreenPosition = x.ScreenPosition, Tag = x.Tag, Hidden = x.Hidden };
            dst.Texts.Add(c);
            created.Add(c);
        }
        foreach (var g in src.GuideLines)
        {
            var c = new GuideLine(t.ApplyPoint(g.Point), t.ApplyVector(g.Direction))
            {
                Start = g.Start is { } s ? t.ApplyPoint(s) : null,
                End = g.End is { } en ? t.ApplyPoint(en) : null,
            };
            dst.GuideLines.Add(c);
            created.Add(c);
        }
        foreach (var g in src.GuidePoints)
        {
            var c = new GuidePoint(t.ApplyPoint(g.Position));
            dst.GuidePoints.Add(c);
            created.Add(c);
        }
        return created;
    }

    /// <summary>How many instances of <paramref name="def"/> the model has, at any depth of the definition tree.</summary>
    public static int InstanceCount(Model model, ComponentDefinition def) =>
        model.Entities.Instances.Count(i => i.Definition == def) +
        model.Definitions.Sum(d => d.Entities.Instances.Count(i => i.Definition == def));

    /// <summary>Every instance of <paramref name="def"/> with the collection that holds it.</summary>
    public static IEnumerable<(ComponentInstance Instance, Entities Owner)> InstancesOf(Model model, ComponentDefinition def) =>
        model.AllEntities.SelectMany(e => e.Instances.Where(i => i.Definition == def).Select(i => (i, e)));

    /// <summary>
    /// Purge Unused: removes definitions no instance uses (repeatedly, as removing one can orphan the ones it
    /// contained). Returns how many went.
    /// </summary>
    public static int PurgeUnused(Model model)
    {
        var removed = 0;
        while (true)
        {
            var used = model.AllEntities.SelectMany(e => e.Instances).Select(i => i.Definition).ToHashSet();
            var unused = model.Definitions.Where(d => !used.Contains(d)).ToList();
            if (unused.Count == 0)
                return removed;
            foreach (var d in unused)
                model.Definitions.Remove(d);
            removed += unused.Count;
        }
    }

    /// <summary>Make Unique: <paramref name="inst"/> gets its own copy of its definition.</summary>
    public static void MakeUnique(Model model, ComponentInstance inst)
    {
        var src = inst.Definition;
        var copy = new ComponentDefinition { Name = src.Name + "#1", Description = src.Description, IsGroup = src.IsGroup };
        CopyEntities(src.Entities, copy.Entities, Geometry.Transform.Identity);
        model.Definitions.Add(copy);
        inst.Definition = copy;
    }

    /// <summary>
    /// Moves the given entities into a new group or component placed where they were. Edges also used by faces
    /// left outside stay outside too (copied into the group), as in SketchUp. Components get their axes at the
    /// lower corner of their bounding box; groups keep the context's axes.
    /// </summary>
    public static ComponentInstance Make(Model model, Entities e, IReadOnlyCollection<object> items, bool asGroup, string? name = null)
    {
        var faces = items.OfType<Face>().ToHashSet();
        var looseEdges = items.OfType<Edge>().ToHashSet();
        var instances = items.OfType<ComponentInstance>().ToList();
        var allEdges = looseEdges.Concat(faces.SelectMany(Topology.EdgesOf)).ToHashSet();

        var points = allEdges.SelectMany(x => new[] { x.Start.Position, x.End.Position }).ToList();
        var bounds = Geometry.Bounds3.FromPoints(points);
        foreach (var inst in instances)
        {
            var b = inst.Definition.Entities.Bounds();
            if (!b.IsEmpty)
                bounds = bounds.Include(TransformedBounds(b, inst.Transform));
        }
        var origin = asGroup || bounds.IsEmpty ? Geometry.Vec3.Zero : bounds.Min;
        var toLocal = Geometry.Transform.Translation(-origin);

        var index = model.Definitions.Count(d => d.IsGroup == asGroup) + 1;
        var def = new ComponentDefinition
        {
            Name = name ?? (asGroup ? $"Group#{index}" : $"Component#{index}"),
            IsGroup = asGroup,
        };
        model.Definitions.Add(def);

        // Copy geometry into the definition, then take from the context what nobody outside still uses.
        Clone(def.Entities, faces, allEdges, toLocal);
        foreach (var inst in instances)
        {
            var moved = def.Entities.AddInstance(inst.Definition, inst.Transform.Then(toLocal));
            moved.Name = inst.Name;
            moved.Material = inst.Material;
            moved.Tag = inst.Tag;
            moved.Hidden = inst.Hidden;
        }

        e.Instances.RemoveAll(instances.Contains);
        e.Faces.RemoveAll(faces.Contains);
        var stillUsed = e.Faces.SelectMany(Topology.EdgesOf).ToHashSet();
        e.Edges.RemoveAll(x => allEdges.Contains(x) && !stillUsed.Contains(x));
        Editing.RemoveOrphanVertices(e);

        return e.AddInstance(def, Geometry.Transform.Translation(origin));
    }

    /// <summary>Explode: the instance's contents return to the context, transformed, sticking to what is there.</summary>
    public static List<object> Explode(Entities e, ComponentInstance inst)
    {
        var src = inst.Definition.Entities;
        var t = inst.Transform;
        var created = new List<object>();
        var before = e.Faces.ToHashSet();

        var newEdges = new List<Edge>();
        foreach (var edge in src.Edges)
        {
            var segs = StickyGeometry.AddSegment(e, t.ApplyPoint(edge.Start.Position), t.ApplyPoint(edge.End.Position));
            foreach (var s in segs)
                s.Flags = edge.Flags;
            newEdges.AddRange(segs);
        }
        FaceFinder.Update(e, newEdges);
        foreach (var f in e.Faces.Where(f => !before.Contains(f)))
        {
            // Match each new face to the source face it reproduces, for orientation and materials.
            var c = Centroid(f);
            var source = src.Faces.FirstOrDefault(s => t.ApplyPoint(Centroid(s)).DistanceTo(c) < 1e-3);
            if (source != null)
            {
                if (f.Normal.Dot(t.ApplyNormal(source.Normal)) < 0)
                    FaceFinder.Reverse(f);
                f.FrontMaterial = source.FrontMaterial ?? inst.Material;
                f.BackMaterial = source.BackMaterial ?? inst.Material;
                f.Tag = source.Tag;
            }
            created.Add(f);
        }
        foreach (var child in src.Instances)
        {
            var c = e.AddInstance(child.Definition, child.Transform.Then(t));
            c.Name = child.Name;
            c.Material = child.Material ?? inst.Material;
            c.Tag = child.Tag;
            created.Add(c);
        }
        created.AddRange(newEdges);
        e.Instances.Remove(inst);
        return created;
    }

    private static void Clone(Entities target, IEnumerable<Face> faces, IEnumerable<Edge> edges, Geometry.Transform t)
    {
        var vmap = new Dictionary<Vertex, Vertex>();
        var emap = new Dictionary<Edge, Edge>();
        Vertex V(Vertex v)
        {
            if (!vmap.TryGetValue(v, out var c))
                vmap[v] = c = target.AddVertex(t.ApplyPoint(v.Position));
            return c;
        }
        foreach (var edge in edges)
        {
            var c = target.AddEdge(V(edge.Start), V(edge.End));
            c.Flags = edge.Flags;
            c.Tag = edge.Tag;
            c.Material = edge.Material;
            emap[edge] = c;
        }
        foreach (var f in faces)
        {
            var c = new Face { FrontMaterial = f.FrontMaterial, BackMaterial = f.BackMaterial, Tag = f.Tag, Hidden = f.Hidden };
            foreach (var loop in f.Loops)
            {
                var l = new FaceLoop();
                l.Edges.AddRange(loop.Edges.Select(x => (emap[x.Edge], x.Reversed)));
                c.Loops.Add(l);
            }
            target.Faces.Add(c);
        }
    }

    private static Geometry.Vec3 Centroid(Face f)
    {
        var pts = f.OuterLoop.Points.ToList();
        return pts.Aggregate(Geometry.Vec3.Zero, (a, p) => a + p) / pts.Count;
    }

    private static Geometry.Bounds3 TransformedBounds(Geometry.Bounds3 b, Geometry.Transform t) =>
        Geometry.Bounds3.FromPoints(Enumerable.Range(0, 8).Select(i => t.ApplyPoint(new Geometry.Vec3(
            (i & 1) == 0 ? b.Min.X : b.Max.X, (i & 2) == 0 ? b.Min.Y : b.Max.Y, (i & 4) == 0 ? b.Min.Z : b.Max.Z))));
}
