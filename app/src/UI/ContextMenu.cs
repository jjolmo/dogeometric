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
                doc.Edit(inst);
            });
            Item("Explode", () =>
            {
                List<object> created = [];
                doc.Operation("Explode", e => created = Grouping.Explode(e, inst));
                doc.Selection.Set(created);
            });
            if (!inst.IsGroup)
            {
                Item("Make Unique", () => doc.Operation("Make Unique", _ => Grouping.MakeUnique(doc.Model, inst)));
                Item("Change Axes", () => view.Tools.Activate(new Tools.AxesTool(inst)));
            }
            menu.AddSeparator();
        }
        if (sel.Count > 0)
        {
            Item("Make Group", () => runCommand(CommandIds.MakeGroup));
            Item("Make Component...", () => runCommand(CommandIds.MakeComponent));
            menu.AddSeparator();
        }

        if (single is SectionPlane section)
        {
            Item("Reverse", () => runCommand(CommandIds.ReverseSection));
            Item(doc.Context.Entities.ActiveSection == section ? "Active Cut ✓" : "Active Cut", () => runCommand(CommandIds.ActiveSectionCut));
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
        if (sel.Any(x => x is Face or Edge or ComponentInstance))
        {
            // SketchUp names the directions after the group's or component's own axes when one is selected.
            var owner = single is ComponentInstance ci ? ci.IsGroup ? "Group's" : "Component's" : null;
            var flip = new PopupMenu();
            flip.AddItem(owner == null ? "Red Direction" : $"{owner} Red", 0);
            flip.AddItem(owner == null ? "Green Direction" : $"{owner} Green", 1);
            flip.AddItem(owner == null ? "Blue Direction" : $"{owner} Blue", 2);
            var items = sel.ToList();
            flip.IdPressed += id => doc.Operation("Flip Along", e => Transforming.Flip(e, items, (int)id, doc.Context.Path.Count == 0 ? doc.Model.Axes : Transform.Identity));
            menu.AddSubmenuNodeItem("Flip Along", flip);
        }
        if (faces.Count > 0 || sel.OfType<ComponentInstance>().Any())
        {
            var intersect = new PopupMenu();
            intersect.AddItem("With Model", 1);
            intersect.AddItem("With Selection", 2);
            intersect.AddItem("With Context", 3);
            intersect.IdPressed += id => runCommand(id switch
            {
                1 => CommandIds.IntersectWithModel,
                2 => CommandIds.IntersectWithSelection,
                _ => CommandIds.IntersectWithContext,
            });
            menu.AddSubmenuNodeItem("Intersect Faces", intersect);
        }
        if (single is Face face)
        {
            Item("Align View", () => AlignView(doc, view, face));
            var back = face.FrontMaterial?.Texture == null && face.BackMaterial?.Texture != null;
            if ((back ? face.BackMaterial : face.FrontMaterial)?.Texture != null)
            {
                var texture = new PopupMenu();
                texture.AddItem("Position", 1);
                texture.IdPressed += _ => view.Tools.Activate(new Tools.TexturePositionTool(face, back));
                menu.AddSubmenuNodeItem("Texture", texture);
            }
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

        if (sel.Count > 0)
            AddSelectionToys(menu, doc, sel);

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

    /// <summary>Selection Toys' items, at the end of the menu as SketchUp lists extensions' items.</summary>
    private static void AddSelectionToys(PopupMenu menu, Document doc, List<object> sel)
    {
        var context = doc.Context.Entities;
        void Set(List<object> items) => doc.Selection.Set(items);
        PopupMenu Sub(string label, IEnumerable<(string Label, Action Run)> items)
        {
            var sub = new PopupMenu();
            var list = items.ToList();
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].Label == "-")
                    sub.AddSeparator();
                else
                    sub.AddItem(list[i].Label, i);
            }
            sub.IdPressed += id => list[(int)id].Run();
            menu.AddSubmenuNodeItem(label, sub);
            return sub;
        }
        menu.AddSeparator();
        if (sel.OfType<ComponentInstance>().Any(i => !i.IsGroup))
            Sub("Instances", [
                ("Select Active", () => Set(SelectionToys.Copies(context, sel, groups: false, sameTag: false))),
                ("Select Active from same Tag", () => Set(SelectionToys.Copies(context, sel, groups: false, sameTag: true))),
            ]);
        if (sel.OfType<ComponentInstance>().Any(i => i.IsGroup))
            Sub("Group Copies", [
                ("Select Active", () => Set(SelectionToys.Copies(context, sel, groups: true, sameTag: false))),
                ("Select Active from same Tag", () => Set(SelectionToys.Copies(context, sel, groups: true, sameTag: true))),
                ("-", () => { }),
                ("Convert into Components", () => doc.Operation("Convert into Components", _ => SelectionToys.GroupsToComponents(doc.Model, sel))),
            ]);
        var faces = sel.OfType<Face>().Any();
        var select = new List<(string, Action)>
        {
            ("Active on Selected Tags", () => Set(SelectionToys.OnTags(context, sel))),
            ("Active with Selected Materials", () => Set(SelectionToys.WithMaterials(context, sel))),
            ("-", () => { }),
            ("Connected By Tag", () => Set(SelectionToys.ConnectedByTag(context, sel))),
        };
        if (faces)
            select.AddRange([
                ("Connected By Material", () => Set(SelectionToys.ConnectedByMaterial(context, sel, back: false))),
                ("Connected By Back Material", () => Set(SelectionToys.ConnectedByMaterial(context, sel, back: true))),
                ("Connected Same Direction Faces", () => Set(SelectionToys.ConnectedFaces(context, sel, SelectionToys.FaceRelation.SameDirection))),
                ("Connected Coplanar Faces", () => Set(SelectionToys.ConnectedFaces(context, sel, SelectionToys.FaceRelation.Coplanar))),
                ("Connected Parallel Faces", () => Set(SelectionToys.ConnectedFaces(context, sel, SelectionToys.FaceRelation.Parallel))),
                ("Connected Perpendicular Faces", () => Set(SelectionToys.ConnectedFaces(context, sel, SelectionToys.FaceRelation.Perpendicular))),
                ("Connected Faces by Area", () => Set(SelectionToys.ConnectedFaces(context, sel, SelectionToys.FaceRelation.SameArea))),
                ("-", () => { }),
                ("Coplanar Faces", () => Set(SelectionToys.Faces(context, sel, SelectionToys.FaceRelation.Coplanar))),
                ("Same Direction Faces", () => Set(SelectionToys.Faces(context, sel, SelectionToys.FaceRelation.SameDirection))),
                ("Parallel Faces", () => Set(SelectionToys.Faces(context, sel, SelectionToys.FaceRelation.Parallel))),
                ("Perpendicular Faces", () => Set(SelectionToys.Faces(context, sel, SelectionToys.FaceRelation.Perpendicular))),
                ("Faces by Area", () => Set(SelectionToys.Faces(context, sel, SelectionToys.FaceRelation.SameArea))),
                ("-", () => { }),
                ("Opposite Faces", () => Set(SelectionToys.OppositeFaces(context, sel))),
            ]);
        Sub("Select ", select);
        var kinds = Enum.GetValues<SelectionKind>();
        Sub("Select Only", kinds.Select(k => (SelectionToys.Label(k), (Action)(() => Set(SelectionToys.Only(k, context, sel))))));
        Sub("Deselect", kinds.Select(k => (SelectionToys.Label(k), (Action)(() => Set(SelectionToys.Without(k, context, sel))))));
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
