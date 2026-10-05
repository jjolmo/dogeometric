using Godot;
using HiddenLine = Dogeometric.Core.IO.HiddenLine;

namespace Dogeometric.App.UI;

/// <summary>
/// SketchUp's Hidden Line Options for 2D drawings: drawing size (full scale in parallel views, else a paper width),
/// profile and section line widths, and edge extensions.
/// </summary>
public static class ExportVectorDialog
{
    public static void Show(Node parent, Vector2I viewSize, double modelMmPerPixel, bool parallel, Action<HiddenLine.Lines> done)
    {
        var p = AppPreferences.Current;
        var dialog = new ConfirmationDialog { Title = "Hidden Line Options", OkButtonText = "Export" };
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(380, 0) };
        SpinBox Mm(double value, double max = 10000) => new() { MinValue = 0.01, MaxValue = max, Step = 0.01, Value = value, Suffix = "mm", CustomMinimumSize = new Vector2(150, 0) };
        HBoxContainer Row(string label, Control field)
        {
            var row = new HBoxContainer();
            row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(120, 0) });
            row.AddChild(field);
            box.AddChild(row);
            return row;
        }

        box.AddChild(new Label { Text = "Drawing Size", ThemeTypeVariation = "HeaderSmall" });
        var fullScale = new CheckBox { Text = "Full scale (1:1)", ButtonPressed = parallel && p.VectorFullScale, Disabled = !parallel };
        box.AddChild(fullScale);
        var width = Mm(p.VectorWidthMm);
        var height = Mm(p.VectorWidthMm * viewSize.Y / Math.Max(viewSize.X, 1));
        Row("Width", width);
        Row("Height", height);
        var linking = false;
        width.ValueChanged += v =>
        {
            if (linking)
                return;
            linking = true;
            height.Value = v * viewSize.Y / Math.Max(viewSize.X, 1);
            linking = false;
        };
        height.ValueChanged += v =>
        {
            if (linking)
                return;
            linking = true;
            width.Value = v * viewSize.X / Math.Max(viewSize.Y, 1);
            linking = false;
        };
        void Enable()
        {
            width.Editable = height.Editable = !fullScale.ButtonPressed;
            if (fullScale.ButtonPressed)
            {
                linking = true;
                (width.Value, height.Value) = (viewSize.X * modelMmPerPixel, viewSize.Y * modelMmPerPixel);
                linking = false;
            }
        }
        fullScale.Toggled += _ => Enable();
        Enable();

        box.AddChild(new Label { Text = "Profile Lines", ThemeTypeVariation = "HeaderSmall" });
        var profiles = new CheckBox { Text = "Show profiles", ButtonPressed = p.VectorShowProfiles };
        box.AddChild(profiles);
        var matchProfiles = new CheckBox { Text = "Match screen display (auto width)", ButtonPressed = p.VectorMatchProfiles };
        box.AddChild(matchProfiles);
        var profileWidth = Mm(p.VectorProfileMm, 20);
        Row("Width", profileWidth);

        box.AddChild(new Label { Text = "Section Lines", ThemeTypeVariation = "HeaderSmall" });
        var sectionWidthOn = new CheckBox { Text = "Specify section line width", ButtonPressed = p.VectorSectionWidth };
        box.AddChild(sectionWidthOn);
        var sectionWidth = Mm(p.VectorSectionMm, 20);
        Row("Width", sectionWidth);

        box.AddChild(new Label { Text = "Extension Lines", ThemeTypeVariation = "HeaderSmall" });
        var extend = new CheckBox { Text = "Extend edges", ButtonPressed = p.VectorExtend };
        box.AddChild(extend);
        var extension = Mm(p.VectorExtensionMm, 100);
        Row("Length", extension);

        void Lines()
        {
            profileWidth.Editable = profiles.ButtonPressed && !matchProfiles.ButtonPressed;
            matchProfiles.Disabled = !profiles.ButtonPressed;
            sectionWidth.Editable = sectionWidthOn.ButtonPressed;
            extension.Editable = extend.ButtonPressed;
        }
        foreach (var check in new[] { profiles, matchProfiles, sectionWidthOn, extend })
            check.Toggled += _ => Lines();
        Lines();

        dialog.AddChild(box);
        dialog.Confirmed += () =>
        {
            p.VectorFullScale = fullScale.ButtonPressed;
            if (!fullScale.ButtonPressed)
                p.VectorWidthMm = width.Value;
            p.VectorShowProfiles = profiles.ButtonPressed;
            p.VectorMatchProfiles = matchProfiles.ButtonPressed;
            p.VectorProfileMm = profileWidth.Value;
            p.VectorSectionWidth = sectionWidthOn.ButtonPressed;
            p.VectorSectionMm = sectionWidth.Value;
            p.VectorExtend = extend.ButtonPressed;
            p.VectorExtensionMm = extension.Value;
            AppPreferences.Save();
            var mmPerPixel = width.Value / Math.Max(viewSize.X, 1);
            done(new HiddenLine.Lines(mmPerPixel,
                ProfileMm: profiles.ButtonPressed && !matchProfiles.ButtonPressed ? profileWidth.Value : 0,
                SectionMm: sectionWidthOn.ButtonPressed ? sectionWidth.Value : 0,
                ExtensionMm: extend.ButtonPressed ? extension.Value : 0,
                Profiles: profiles.ButtonPressed));
        };
        dialog.VisibilityChanged += () =>
        {
            if (!dialog.Visible)
                dialog.QueueFree();
        };
        parent.AddChild(dialog);
        dialog.PopupCentered();
    }
}
