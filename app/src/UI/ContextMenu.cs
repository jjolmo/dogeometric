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
        var actions = Fill(menu, doc, view, runCommand, items: false);
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

    /// <summary>The selection's menu items into <paramref name="menu"/>; with <paramref name="items"/> only those about
    /// the selected entities, as Edit › Items lists them. Returns each item's action by id.</summary>
    public static Dictionary<int, Action> Fill(PopupMenu menu, Document doc, ModelViewport view, Action<int> runCommand, bool items)
    {
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

        if (!items)
        {
            Item("Entity Info", () => { }, sel.Count > 0);
            Item("Erase", () => runCommand(CommandIds.Delete), sel.Count > 0);
            Item("Hide", () => runCommand(CommandIds.Hide), sel.Count > 0);
            if (sel.OfType<ComponentInstance>().Any())
                Item("Lock", () => runCommand(CommandIds.Lock));
            if (doc.Context.Path.Count > 0)
                Item(doc.Context.Path[^1].IsGroup ? "Close Group" : "Close Component", () => runCommand(CommandIds.CloseGroup));
            menu.AddSeparator();
        }

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
                // As SketchUp's Select › Instances: the copies in the context being edited.
                Item("Select Instances", () => doc.Selection.Set(doc.Context.Entities.Instances.Where(i => i.Definition == inst.Definition).Cast<object>().ToList()));
            }
            var t = inst.Transform;
            var scaled = Math.Abs(t.X.Length - 1) > 1e-9 || Math.Abs(t.Y.Length - 1) > 1e-9 || Math.Abs(t.Z.Length - 1) > 1e-9;
            var skewed = Math.Abs(t.X.Normalized().Dot(t.Y.Normalized())) > 1e-9 || Math.Abs(t.X.Normalized().Dot(t.Z.Normalized())) > 1e-9
                || Math.Abs(t.Y.Normalized().Dot(t.Z.Normalized())) > 1e-9;
            if (scaled)
            {
                Item("Reset Scale", () => doc.Operation("Reset Scale", _ => ContextOps.ResetScale(inst)));
                if (!inst.IsGroup)
                    Item("Scale Definition", () => doc.Operation("Scale Definition", _ => ContextOps.ScaleDefinition(inst)));
            }
            if (skewed)
                Item("Reset Skew", () => doc.Operation("Reset Skew", _ => ContextOps.ResetSkew(inst)));
            if (inst.GluedTo != null)
                Item("Unglue", () => doc.Operation("Unglue", _ => inst.GluedTo = null));
            menu.AddSeparator();
        }
        if (sel.Count > 0 && !items)
        {
            Item("Make Group", () => runCommand(CommandIds.MakeGroup));
            Item("Make Component...", () => runCommand(CommandIds.MakeComponent));
            menu.AddSeparator();
        }

        if (single is LinearDimension dim)
        {
            Item("Edit Text", () => Tools.SelectTool.EditAnnotationText?.Invoke(dim));
            var style = dim.Style ?? doc.Model.Dimensions;
            void Restyle(string name, Func<DimensionStyle, DimensionStyle> change) =>
                doc.Operation(name, _ => dim.Style = change(dim.Style ?? doc.Model.Dimensions));
            Submenu(menu, "Text Position", [
                ("Outside Start", () => Restyle("Text Position", x => x with { AlignToScreen = false, Position = DimensionTextPosition.Outside })),
                ("Centered", () => Restyle("Text Position", x => x with { AlignToScreen = false, Position = DimensionTextPosition.Centered })),
                ("Outside End", () => Restyle("Text Position", x => x with { AlignToScreen = false, Position = DimensionTextPosition.Above })),
            ]);
            Submenu(menu, "Endpoints", Enum.GetValues<DimensionEndpoint>().Select(e => (e switch { DimensionEndpoint.ClosedArrow => "Closed Arrow", DimensionEndpoint.OpenArrow => "Open Arrow", _ => e.ToString() },
                (Action)(() => Restyle("Dimension Endpoints", x => x with { Endpoints = e })))).ToArray());
            Item(style.AlignToScreen ? "Align to Dimension" : "Align to Screen", () => Restyle("Dimension Alignment", x => x with { AlignToScreen = !x.AlignToScreen }));
            menu.AddSeparator();
        }
        if (single is TextLabel label)
        {
            Item("Edit Text", () => Tools.SelectTool.EditAnnotationText?.Invoke(label));
            if (label.ScreenPosition == null)
            {
                void Restyle(string name, Func<TextStyle, TextStyle> change) =>
                    doc.Operation(name, _ => label.Style = change(label.Style ?? doc.Model.LeaderText));
                Submenu(menu, "Arrow", new[] { DimensionEndpoint.None, DimensionEndpoint.Dot, DimensionEndpoint.ClosedArrow, DimensionEndpoint.OpenArrow }
                    .Select(e => (e switch { DimensionEndpoint.ClosedArrow => "Closed Arrow", DimensionEndpoint.OpenArrow => "Open Arrow", _ => e.ToString() },
                        (Action)(() => Restyle("Text Arrow", x => x with { Endpoint = e })))).ToArray());
                Submenu(menu, "Leader", [
                    ("View Based", () => Restyle("Text Leader", x => x with { Leader = LeaderType.ViewBased })),
                    ("Pushpin", () => Restyle("Text Leader", x => x with { Leader = LeaderType.Pushpin })),
                    ("Hidden", () => Restyle("Text Leader", x => x with { Leader = LeaderType.Hidden })),
                ]);
            }
            menu.AddSeparator();
        }
        if (single is LinearDimension { Kind: not DimensionKind.Linear } radial)
        {
            Item(radial.Kind == DimensionKind.Radius ? "Type › Diameter" : "Type › Radius", () =>
                doc.Operation("Dimension Type", _ => RadialDimensions.SetKind(radial,
                    radial.Kind == DimensionKind.Radius ? DimensionKind.Diameter : DimensionKind.Radius)));
            menu.AddSeparator();
        }
        if (sel.Count > 0 && sel.All(x => x is Edge { Curve.Spline: not null }))
            Item("Edit Bezier Curve", () => view.Tools.Activate(new Tools.BezierEditTool()));
        var edges = sel.OfType<Edge>().ToList();
        if (edges.Count > 0 && edges.Count == sel.Count)
        {
            if (edges.Any(x => x.Curve != null))
            {
                Item("Explode Curve", () => doc.Operation("Explode Curve", _ => ContextOps.ExplodeCurves(edges)));
                if (single is Edge { Curve.Radius: > 0 } arcEdge)
                    Item("Find Center", () => doc.Operation("Find Center", e => ContextOps.FindCenter(e, arcEdge)));
                if (edges.Any(x => x.Curve is { Radius: > 0, IsPolygon: false }))
                    Item("Convert to Polygon", () => doc.Operation("Convert to Polygon", _ => ContextOps.ToPolygon(edges)));
            }
            if (edges.Count > 1)
                Item("Weld Edges", () =>
                {
                    Dogeometric.Core.Modeling.Curve? made = null;
                    doc.Operation("Weld Edges", _ => made = ContextOps.Weld(edges));
                    if (made == null)
                        view.ShowHint("Weld Edges: the edges must form one connected chain.");
                });
        }
        if (single is SectionPlane section)
        {
            Item("Reverse", () => runCommand(CommandIds.ReverseSection));
            Item(doc.Context.Entities.ActiveSection == section ? "Active Cut ✓" : "Active Cut", () => runCommand(CommandIds.ActiveSectionCut));
            Item("Align View", () => AlignView(doc, view, section));
            Item("Troubleshoot Section Fill", () =>
            {
                var problems = SectionFill.Problems(Intersect.Slice(doc.Model, section));
                view.SectionProblems = problems;
                view.QueueOverlayRedraw();
                view.ShowHint(problems.Count == 0
                    ? "Section Fill: the cut closes everywhere."
                    : $"Section Fill: the cut breaks at {problems.Count} point(s), marked in red; the geometry there is not closed.");
            });
            Item("Create Group from Slice", () =>
            {
                ComponentInstance? made = null;
                doc.Operation("Create Group from Slice", e => made = Intersect.GroupFromSlice(doc.Model, section, e, doc.Context.ToWorld));
                if (made != null)
                    doc.Selection.Set([made]);
                else
                    view.ShowHint("Create Group from Slice: the section plane cuts nothing.");
            });
            menu.AddSeparator();
        }

        var faces = sel.OfType<Face>().ToList();
        if (faces.Count > 0 && !items)
        {
            var area = new PopupMenu();
            area.AddItem("Selection", 1);
            area.AddItem("Tag", 2);
            area.AddItem("Material", 3);
            area.IdPressed += id =>
            {
                var first = faces[0];
                var all = doc.Context.Entities.Faces;
                var (name, total) = id switch
                {
                    2 => ($"Tag {first.Tag?.Name ?? Tag.UntaggedName}", ContextOps.Area(all.Where(f => f.Tag == first.Tag))),
                    3 => ($"Material {first.FrontMaterial?.Name ?? "Default"}", ContextOps.Area(all.Where(f => f.FrontMaterial == first.FrontMaterial))),
                    _ => ("Selection", ContextOps.Area(faces)),
                };
                view.ShowHint($"Area of {name}: {total / 100:0.##} cm² ({total:0.#} mm²)");
            };
            menu.AddSubmenuNodeItem("Area", area);
        }
        if (faces.Count > 0)
        {
            Item("Reverse Faces", () => doc.Operation("Reverse Faces", _ =>
            {
                foreach (var f in faces)
                    FaceFinder.Reverse(f);
            }));
            if (faces.Count == 1)
                Item("Orient Faces", () => doc.Operation("Orient Faces", e => OrientFaces.Apply(e, faces[0])));
        }
        if (sel.Any(x => x is Face or Edge or ComponentInstance))
        {
            // SketchUp names the directions after the group's or component's own axes when one is selected.
            var owner = single is ComponentInstance ci ? ci.IsGroup ? "Group's" : "Component's" : null;
            var flip = new PopupMenu();
            flip.AddItem(owner == null ? "Red Direction" : $"{owner} Red", 0);
            flip.AddItem(owner == null ? "Green Direction" : $"{owner} Green", 1);
            flip.AddItem(owner == null ? "Blue Direction" : $"{owner} Blue", 2);
            var flipped = sel.ToList();
            flip.IdPressed += id => doc.Operation("Flip Along", e => Transforming.Flip(e, flipped, (int)id, doc.Context.Path.Count == 0 ? doc.Model.Axes : Transform.Identity));
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
                texture.AddItem("Reset Position", 3);
                texture.AddItem("Make Unique Texture", 4);
                texture.AddItem("Edit Texture Image...", 2);
                texture.IdPressed += id =>
                {
                    switch (id)
                    {
                        case 1:
                            view.Tools.Activate(new Tools.TexturePositionTool(face, back));
                            break;
                        case 3:
                            doc.Operation("Reset Position", _ =>
                            {
                                if (back)
                                    face.BackMapping = null;
                                else
                                    face.FrontMapping = null;
                            });
                            break;
                        case 4:
                            doc.Operation("Make Unique Texture", _ => Texturing.MakeUnique(doc.Model, face, back));
                            break;
                        default:
                            runCommand(Commands.OwnIds.EditTextureImage);
                            break;
                    }
                };
                menu.AddSubmenuNodeItem("Texture", texture);
            }
        }
        if (sel.Count > 0 && !items)
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

        if (sel.Count > 0 && !items)
            AddSelectionToys(menu, doc, sel);
        if (items && actions.Count == 0)
        {
            menu.AddItem("No Selection", 0);
            menu.SetItemDisabled(0, true);
        }
        return actions;
    }

    /// <summary>A submenu of plain items ("-" for a separator) under <paramref name="label"/>.</summary>
    private static PopupMenu Submenu(PopupMenu menu, string label, IEnumerable<(string Label, Action Run)> items)
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

    /// <summary>Selection Toys' items, at the end of the menu as SketchUp lists extensions' items.</summary>
    private static void AddSelectionToys(PopupMenu menu, Document doc, List<object> sel)
    {
        var context = doc.Context.Entities;
        void Set(List<object> items) => doc.Selection.Set(items);
        PopupMenu Sub(string label, IEnumerable<(string Label, Action Run)> items) => Submenu(menu, label, items);
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
                ("Connected Faces by Angle", () => AskAngle(menu, angle => Set(SelectionToys.ConnectedFacesByAngle(context, sel, angle)))),
                ("-", () => { }),
                ("Coplanar Faces", () => Set(SelectionToys.Faces(context, sel, SelectionToys.FaceRelation.Coplanar))),
                ("Same Direction Faces", () => Set(SelectionToys.Faces(context, sel, SelectionToys.FaceRelation.SameDirection))),
                ("Parallel Faces", () => Set(SelectionToys.Faces(context, sel, SelectionToys.FaceRelation.Parallel))),
                ("Perpendicular Faces", () => Set(SelectionToys.Faces(context, sel, SelectionToys.FaceRelation.Perpendicular))),
                ("Faces by Area", () => Set(SelectionToys.Faces(context, sel, SelectionToys.FaceRelation.SameArea))),
                ("-", () => { }),
                ("Opposite Faces", () => Set(SelectionToys.OppositeFaces(context, sel))),
            ]);
        if (faces || sel.OfType<Edge>().Any())
            select.AddRange([
                ("-", () => { }),
                ("Select Edge Loops", () => Set(SelectionToys.EdgeLoops(context, sel))),
                ("Quad-face Loops", () => Set(SelectionToys.QuadFaceLoops(context, sel))),
            ]);
        Sub("Select ", select);
        var kinds = Enum.GetValues<SelectionKind>();
        Sub("Select Only", kinds.Select(k => (SelectionToys.Label(k), (Action)(() => Set(SelectionToys.Only(k, context, sel))))));
        Sub("Deselect", kinds.Select(k => (SelectionToys.Label(k), (Action)(() => Set(SelectionToys.Without(k, context, sel))))));
    }

    // Selection Toys asks for the angle each time; it remembers the last one.
    private static double _lastAngle = 15;

    private static void AskAngle(Node near, Action<double> run)
    {
        var host = near.GetParent() ?? near;
        var dialog = new ConfirmationDialog { Title = "Connected Faces by Angle" };
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = "Maximum angle between faces:" });
        var spin = new SpinBox { MinValue = 0, MaxValue = 180, Step = 0.5, Value = _lastAngle, Suffix = "°" };
        row.AddChild(spin);
        dialog.AddChild(row);
        dialog.Confirmed += () =>
        {
            _lastAngle = spin.Value;
            run(spin.Value);
            dialog.QueueFree();
        };
        dialog.Canceled += dialog.QueueFree;
        host.AddChild(dialog);
        dialog.PopupCentered();
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

    /// <summary>Looks straight along the section plane's arrows at the cut, framing the model (plans and elevations).</summary>
    private static void AlignView(Document doc, ModelViewport view, SectionPlane section)
    {
        var xf = doc.Context.ToWorld;
        var n = xf.ApplyNormal(section.Normal).Normalized();
        var at = xf.ApplyPoint(section.Point);
        view.BeginNavigation();
        var dist = Math.Max(view.Camera.Distance, 1);
        view.Camera.Set(at - n * dist, at, Math.Abs(n.Z) > 0.99 ? Vec3.UnitY : Vec3.UnitZ);
        view.ZoomToBounds(doc.Model.Entities.Bounds());
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
