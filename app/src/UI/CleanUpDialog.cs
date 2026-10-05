using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.UI;

/// <summary>CleanUp³'s "Clean…" dialog: scope and options in the extension's groups; Clean runs them.</summary>
public static class CleanUpDialog
{
    /// <summary>The settings of the last clean ("Clean with Last Settings").</summary>
    public static CleanUpOptions Last { get; set; } = new();

    public static bool ShowStatistics { get; set; } = true;

    public static void Show(Node parent, Action<CleanUpOptions> clean)
    {
        var o = Last;
        var d = new ConfirmationDialog { Title = "CleanUp³", OkButtonText = "Clean", CancelButtonText = "Cancel" };
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(380, 0) };
        box.AddThemeConstantOverride("separation", 2);

        void Heading(string text)
        {
            var l = new Label { Text = text };
            l.AddThemeColorOverride("font_color", Color.Color8(90, 90, 90));
            box.AddChild(new HSeparator());
            box.AddChild(l);
        }
        CheckBox Check(string text, bool value, string tip = "")
        {
            var c = new CheckBox { Text = text, ButtonPressed = value, TooltipText = tip };
            box.AddChild(c);
            return c;
        }

        Heading("General");
        var scopeRow = new HBoxContainer();
        scopeRow.AddChild(new Label { Text = "Scope" });
        var group = new ButtonGroup();
        var scopes = new Dictionary<CleanUpOptions.Scopes, CheckBox>();
        foreach (var s in Enum.GetValues<CleanUpOptions.Scopes>())
        {
            var label = s == CleanUpOptions.Scopes.Local ? "Local" : s.ToString();
            var r = new CheckBox { Text = label, ButtonGroup = group, ButtonPressed = s == o.Scope };
            scopes[s] = r;
            scopeRow.AddChild(r);
        }
        box.AddChild(scopeRow);
        var stats = Check("Show Statistics", ShowStatistics, "Shows a summary of what was done at the end of the cleanup.");

        Heading("Optimisations");
        var purge = Check("Purge Unused", o.Purge, "Purges all unused items in the model (components, materials, tags).");
        var hidden = Check("Erase Hidden Geometry", o.EraseHidden, "Erases all hidden entities in the current scope.");
        var duplicates = Check("Erase Duplicate Faces", o.EraseDuplicateFaces, "Erases faces occupying the same space as another face.");

        Heading("Tags");
        var untagged = Check("Geometry to Untagged", o.GeometryToUntagged, "Puts all edges and faces on Untagged.");

        Heading("Materials");
        var materials = Check("Merge Identical Materials", o.MergeMaterials, "Merges all identical materials in the model.");

        Heading("Coplanar Faces");
        var merge = Check("Merge Coplanar Faces", o.MergeFaces, "Removes edges separating coplanar faces.");
        var normals = Check("Ignore Normals", o.IgnoreNormals, "Faces are considered coplanar even if they face opposite ways.");
        var ignoreMaterials = Check("Ignore Materials", o.IgnoreMaterials, "Faces are merged even though their material is different.");
        var uv = Check("Ignore UV", o.IgnoreUv, "Faces are merged even though their UV mapping is different.");

        Heading("Edges");
        var split = Check("Repair Split Edges", o.RepairSplitEdges);
        var stray = Check("Erase Stray Edges", o.EraseStrayEdges, "Removes all edges not connected to any face.");
        var edgeMaterials = Check("Remove Edge Materials", o.RemoveEdgeMaterials);
        var smoothRow = new HBoxContainer();
        smoothRow.AddChild(new Label { Text = "Smooth Edges by Angle" });
        var smooth = new SpinBox { MinValue = 0, MaxValue = 180, Step = 0.5, Value = o.SmoothAngle, Suffix = "°" };
        d.RegisterTextEnter(smooth.GetLineEdit());
        smoothRow.AddChild(smooth);
        box.AddChild(smoothRow);

        d.AddChild(box);
        d.Confirmed += () =>
        {
            Last = new CleanUpOptions
            {
                Scope = scopes.First(kv => kv.Value.ButtonPressed).Key,
                Purge = purge.ButtonPressed,
                EraseHidden = hidden.ButtonPressed,
                EraseDuplicateFaces = duplicates.ButtonPressed,
                GeometryToUntagged = untagged.ButtonPressed,
                MergeMaterials = materials.ButtonPressed,
                MergeFaces = merge.ButtonPressed,
                IgnoreNormals = normals.ButtonPressed,
                IgnoreMaterials = ignoreMaterials.ButtonPressed,
                IgnoreUv = uv.ButtonPressed,
                RepairSplitEdges = split.ButtonPressed,
                EraseStrayEdges = stray.ButtonPressed,
                RemoveEdgeMaterials = edgeMaterials.ButtonPressed,
                SmoothAngle = smooth.Value,
            };
            ShowStatistics = stats.ButtonPressed;
            d.QueueFree();
            clean(Last);
        };
        d.Canceled += d.QueueFree;
        parent.AddChild(d);
        d.PopupCentered();
    }

    public static void Statistics(Node parent, SortedDictionary<string, int> stats, TimeSpan elapsed)
    {
        if (!ShowStatistics)
            return;
        var lines = stats.Select(kv => $"> {kv.Key}: {kv.Value}").Append($"> Total Elapsed Time: {elapsed.TotalSeconds:0.000}s");
        MessageDialog.Show(parent, "CleanUp³", "Cleanup Statistics:\n" + string.Join("\n", lines));
    }
}
