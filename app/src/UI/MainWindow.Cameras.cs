using Dogeometric.App.Commands;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.View;
using Godot;

namespace Dogeometric.App.UI;

/// <summary>Tools › Advanced Camera Tools: cameras placed in the model, looked through, locked, shown or hidden.</summary>
public partial class MainWindow
{
    private void RegisterCameras()
    {
        var v = _viewport;
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
