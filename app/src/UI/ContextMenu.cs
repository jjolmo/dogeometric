using Dogeometric.App.Viewport;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.UI;

/// <summary>SketchUp's right-click menu for the current selection.</summary>
public static class ContextMenu
{
    public static void Show(Control host, Vector2 screenPosition, Document doc, ModelViewport view, Action<int> runCommand)
    {
        var menu = new PopupMenu();
        var actions = new Dictionary<int, Action>();
        var next = 1;
        void Item(string label, Action action, bool enabled = true)
        {
            var id = next++;
            menu.AddItem(label, id);
            menu.SetItemDisabled(menu.ItemCount - 1, !enabled);
            actions[id] = action;
        }

        var sel = doc.Selection.Items.ToList();
        var single = sel.Count == 1 ? sel[0] : null;

        Item("Entity Info", () => { }, sel.Count > 0);
        Item("Erase", () => runCommand(CommandIds.Delete), sel.Count > 0);
        Item("Hide", () => runCommand(CommandIds.Hide), sel.Count > 0);
        menu.AddSeparator();

        if (single is ComponentInstance inst)
        {
            Item(inst.IsGroup ? "Edit Group" : "Edit Component", () =>
            {
                doc.Selection.Clear();
                doc.Context.Enter(inst);
            });
            Item("Explode", () =>
            {
                List<object> created = [];
                doc.Operation("Explode", e => created = Grouping.Explode(e, inst));
                doc.Selection.Set(created);
            });
            if (!inst.IsGroup)
            {
                Item("Make Unique", () => doc.Operation("Make Unique", _ => MakeUnique(doc.Model, inst)));
            }
            menu.AddSeparator();
        }
        if (sel.Count > 0)
        {
            Item("Make Group", () => runCommand(CommandIds.MakeGroup));
            Item("Make Component...", () => runCommand(CommandIds.MakeComponent));
            menu.AddSeparator();
        }

        var faces = sel.OfType<Face>().ToList();
        if (faces.Count > 0)
        {
            Item("Reverse Faces", () => doc.Operation("Reverse Faces", _ =>
            {
                foreach (var f in faces)
                    FaceFinder.Reverse(f);
            }));
        }
        if (single is Face face)
        {
            Item("Align View", () => AlignView(doc, view, face));
        }
        if (sel.Count > 0)
        {
            Item("Zoom Selection", () => ZoomSelection(doc, view));
            menu.AddSeparator();

            var select = new PopupMenu();
            var selectActions = new Dictionary<int, Action>();
            void SelItem(string label, Action a)
            {
                var id = selectActions.Count + 1;
                select.AddItem(label, id);
                selectActions[id] = a;
            }
            var first = sel[0];
            SelItem("All Connected", () => doc.Selection.Set(Topology.Connected(doc.Context.Entities, first).ToList()));
            SelItem("All on Same Tag", () =>
            {
                var tag = first switch { Face f => f.Tag, Edge edge => edge.Tag, ComponentInstance i => i.Tag, _ => null };
                var ents = doc.Context.Entities;
                doc.Selection.Set(ents.Faces.Where(f => f.Tag == tag).Cast<object>().Concat(ents.Edges.Where(x => x.Tag == tag)).Concat(ents.Instances.Where(i => i.Tag == tag)).ToList());
            });
            if (first is Face ff)
                SelItem("All with Same Material", () => doc.Selection.Set(doc.Context.Entities.Faces.Where(f => f.FrontMaterial == ff.FrontMaterial).Cast<object>().ToList()));
            select.IdPressed += id => selectActions[(int)id]();
            menu.AddSubmenuNodeItem("Select", select);
        }

        menu.IdPressed += id =>
        {
            if (actions.TryGetValue((int)id, out var a))
                a();
        };
        menu.PopupHide += menu.QueueFree;
        host.AddChild(menu);
        menu.Position = (Vector2I)(host.GetScreenPosition() + screenPosition);
        menu.Popup();
    }

    /// <summary>Make Unique: this instance gets its own copy of the definition.</summary>
    private static void MakeUnique(Model model, ComponentInstance inst)
    {
        var src = inst.Definition;
        var copy = new ComponentDefinition { Name = src.Name + "#1", Description = src.Description, IsGroup = src.IsGroup };
        var vmap = new Dictionary<Vertex, Vertex>();
        var emap = new Dictionary<Edge, Edge>();
        Vertex V(Vertex v) => vmap.TryGetValue(v, out var c) ? c : vmap[v] = copy.Entities.AddVertex(v.Position);
        foreach (var e in src.Entities.Edges)
        {
            var c = copy.Entities.AddEdge(V(e.Start), V(e.End));
            c.Flags = e.Flags;
            c.Tag = e.Tag;
            c.Material = e.Material;
            c.Curve = e.Curve;
            emap[e] = c;
        }
        foreach (var f in src.Entities.Faces)
        {
            var c = new Face { FrontMaterial = f.FrontMaterial, BackMaterial = f.BackMaterial, Tag = f.Tag, Hidden = f.Hidden };
            foreach (var l in f.Loops)
            {
                var loop = new FaceLoop();
                loop.Edges.AddRange(l.Edges.Select(x => (emap[x.Edge], x.Reversed)));
                c.Loops.Add(loop);
            }
            copy.Entities.Faces.Add(c);
        }
        foreach (var i in src.Entities.Instances)
        {
            var c = copy.Entities.AddInstance(i.Definition, i.Transform);
            c.Name = i.Name;
            c.Material = i.Material;
            c.Tag = i.Tag;
        }
        model.Definitions.Add(copy);
        inst.Definition = copy;
    }

    private static void AlignView(Document doc, ModelViewport view, Face face)
    {
        var xf = doc.Context.ToWorld;
        var n = xf.ApplyNormal(face.Normal);
        var centre = xf.ApplyPoint(face.OuterLoop.Points.Aggregate(Vec3.Zero, (a, p) => a + p) / face.OuterLoop.Edges.Count);
        view.BeginNavigation();
        var dist = Math.Max(view.Camera.Distance, 1);
        view.Camera.Set(centre + n * dist, centre, Math.Abs(n.Z) > 0.99 ? Vec3.UnitY : Vec3.UnitZ);
        view.ZoomToBounds(Bounds3.FromPoints(face.OuterLoop.Points.Select(xf.ApplyPoint)));
    }

    private static void ZoomSelection(Document doc, ModelViewport view)
    {
        var xf = doc.Context.ToWorld;
        var b = Bounds3.Empty;
        foreach (var item in doc.Selection.Items)
        {
            switch (item)
            {
                case Face f:
                    b = b.Include(Bounds3.FromPoints(f.OuterLoop.Points.Select(xf.ApplyPoint)));
                    break;
                case Edge e:
                    b = b.Include(xf.ApplyPoint(e.Start.Position)).Include(xf.ApplyPoint(e.End.Position));
                    break;
                case ComponentInstance i:
                    var ib = i.Definition.Entities.Bounds();
                    if (!ib.IsEmpty)
                        b = b.Include(xf.ApplyPoint(i.Transform.ApplyPoint(ib.Min))).Include(xf.ApplyPoint(i.Transform.ApplyPoint(ib.Max)));
                    break;
            }
        }
        if (!b.IsEmpty)
        {
            view.BeginNavigation();
            view.ZoomToBounds(b);
        }
    }
}
