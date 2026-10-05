using Dogeometric.App.Commands;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.View;
using Godot;

namespace Dogeometric.App.UI;

/// <summary>Tools › Advanced Camera Tools: cameras placed in the model, looked through, locked, shown or hidden.</summary>
public partial class MainWindow
{
    /// <summary>Select Camera Type: frame proportions of common film, video and photo cameras.</summary>
    private static readonly (string Group, (string Name, double Aspect)[] Types)[] CameraTypes =
    [
        ("16mm", [("16mm Camera Aperture", 1.37), ("16mm Super 16 Camera Aperture", 1.66), ("16mm Super 16 HDTV 16:9", 16.0 / 9)]),
        ("35mm", [("35mm 1.66 Projection Aperture", 1.66), ("35mm 1.85 Projection Aperture", 1.85), ("35mm 2.40 Anamorphic Projection", 2.40),
            ("35mm 4-Perf 1.33 Camera Aperture", 1.33), ("35mm full 1.37 Projection Aperture", 1.37)]),
        ("65mm", [("65mm Camera Aperture", 2.28), ("65mm Projection Aperture", 2.2)]),
        ("Digital", [("1/1.7 Sensor 4:3", 4.0 / 3), ("1/2 Sensor 16:9", 16.0 / 9), ("1/2.5 Sensor 4:3", 4.0 / 3), ("2/3 Video 2.40 Extracted area", 2.4)]),
        ("IMAX", [("IMAX 1.85 Safe", 1.85), ("IMAX 2.39 Safe", 2.39), ("IMAX Camera Aperture", 1.43)]),
        ("Photography", [("35mm SLR / Full Frame DSLR", 1.5), ("Four Thirds System", 4.0 / 3), ("Large Format 4x5", 1.25),
            ("Medium Format 6x4.5", 4.0 / 3), ("Medium Format 6x6", 1), ("Medium Format 6x7", 7.0 / 6)]),
    ];

    private void RegisterCameras()
    {
        var v = _viewport;
        _commands.SubmenuBuilders["Select Camera Type"] = menu =>
        {
            var actions = new Dictionary<int, Action>();
            foreach (var (group, types) in CameraTypes)
            {
                var sub = new PopupMenu();
                foreach (var (name, aspect) in types)
                {
                    var id = actions.Count + 1;
                    sub.AddRadioCheckItem(name, id);
                    sub.SetItemChecked(sub.ItemCount - 1, v.FrameAspect is { } a && Math.Abs(a - aspect) < 1e-9);
                    actions[id] = () => v.FrameAspect = aspect;
                    sub.IdPressed += i =>
                    {
                        if (i == id)
                            actions[id]();
                    };
                }
                menu.AddSubmenuNodeItem(group, sub);
            }
            return actions;
        };
        _commands.Register(ExtensionIds.CameraCreate, CreateCamera);
        _commands.Register(ExtensionIds.CameraLookThrough, () =>
        {
            var doc = _document.Document;
            if (doc.Selection.Items.OfType<ComponentInstance>().FirstOrDefault(i => i.Definition.Camera != null) is not { } camera)
            {
                _status.SetHint("Select a camera made with Create Camera first.");
                return;
            }
            v.BeginNavigation();
            v.LockedCamera = null;
            var view = CameraObjects.ViewOf(camera, doc.Context.ToWorld, Math.Max(v.Camera.Distance, 100))!.Value;
            v.ChangeCamera(c => c.Restore(view));
            v.FrameAspect = camera.Definition.Camera!.Aspect;
            _document.LookThrough(camera);
            // Leaving the camera's position shows it again and drops its frame, as Advanced Camera Tools does.
            void Left()
            {
                if (v.Camera.Eye.DistanceTo(view.Eye) < 1e-6)
                    return;
                v.CameraChanged -= Left;
                v.FrameAspect = null;
                _document.LookThrough(null);
            }
            v.CameraChanged += Left;
        });
        _commands.Register(ExtensionIds.CameraLock, () => v.LockedCamera = v.LockedCamera == null ? v.Camera.Save() : null, () => v.LockedCamera != null);
        void TagToggle(int id, string tag) => _commands.Register(id, () =>
        {
            var t = _document.Model.GetOrAddTag(tag);
            t.Visible = !t.Visible;
            _document.RebuildAll();
        }, () => _document.Model.Tags.FirstOrDefault(t => t.Name == tag)?.Visible ?? true);
        TagToggle(ExtensionIds.CameraShowAll, CameraObjects.CamerasTag);
        TagToggle(ExtensionIds.CameraFrustumLines, CameraObjects.FrustumLinesTag);
        TagToggle(ExtensionIds.CameraFrustumVolume, CameraObjects.FrustumVolumeTag);
        _commands.Register(ExtensionIds.CameraReset, () =>
        {
            v.LockedCamera = null;
            v.FrameAspect = null;
            _document.LookThrough(null);
        });
    }

    private void CreateCamera()
    {
        var d = new ConfirmationDialog { Title = "Create Camera", Theme = LightTheme.Create() };
        var cameras = _document.Model.Definitions.Count(x => x.Camera != null);
        var name = new LineEdit { Text = $"Camera {cameras + 1}", CustomMinimumSize = new Vector2(260, 0) };
        var box = new VBoxContainer();
        box.AddChild(new Label { Text = "Camera name:" });
        box.AddChild(name);
        d.AddChild(box);
        d.RegisterTextEnter(name);
        d.Confirmed += () =>
        {
            var doc = _document.Document;
            var view = _viewport.Camera.Save();
            var toLocal = doc.Context.ToWorld.Inverse();
            var local = view with { Eye = toLocal.ApplyPoint(view.Eye), Target = toLocal.ApplyPoint(view.Target), Up = toLocal.ApplyVector(view.Up) };
            var aspect = _viewport.Size.X / Math.Max(1, _viewport.Size.Y);
            var reach = Math.Max(_viewport.Camera.Distance, 100);
            ComponentInstance? made = null;
            doc.Operation("Create Camera", e => made = CameraObjects.Create(doc.Model, e, name.Text.Trim() is { Length: > 0 } n ? n : $"Camera {cameras + 1}", local, aspect, reach));
            if (made != null)
                doc.Selection.Set([made]);
            d.QueueFree();
        };
        d.Canceled += d.QueueFree;
        AddChild(d);
        d.PopupCentered();
        name.GrabFocus();
        name.SelectAll();
    }
}
