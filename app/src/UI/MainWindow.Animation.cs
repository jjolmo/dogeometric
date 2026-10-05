using Dogeometric.App.Commands;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.View;
using Godot;

namespace Dogeometric.App.UI;

/// <summary>File › Export › Animation: the scenes played as in View › Animation, saved frame by frame.</summary>
public partial class MainWindow
{
    private const int AnimationFps = 24;

    private void ExportAnimation()
    {
        if (_document.Model.Scenes.Count(s => s.Camera != null) < 2)
        {
            Alert("Export Animation", "The model needs at least two scenes to make an animation.");
            return;
        }
        _document.PickExport("Export Animation", ["*.mp4 ; MP4 Video", "*.webm ; WebM Video", "*.png ; Image Set"], path => _ = RecordAnimation(path));
    }

    /// <summary>The camera at each frame: each scene held for the scene delay, then the transition to the next.</summary>
    private List<(int Scene, CameraState Camera)> AnimationFrames()
    {
        var model = _document.Model;
        var scenes = model.Scenes.Select((s, i) => (s, i)).Where(x => x.s.Camera != null).ToList();
        var frames = new List<(int, CameraState)>();
        for (var k = 0; k < scenes.Count; k++)
        {
            var (scene, index) = scenes[k];
            var from = scene.Camera!.Value;
            var hold = Math.Max(1, (int)Math.Round(model.SceneDelaySeconds * AnimationFps));
            for (var f = 0; f < hold; f++)
                frames.Add((index, from));
            if (k + 1 == scenes.Count || !model.SceneTransitions)
                continue;
            var to = scenes[k + 1].s.Camera!.Value;
            var count = Math.Max(1, (int)Math.Round(model.SceneTransitionSeconds * AnimationFps));
            for (var f = 1; f <= count; f++)
            {
                var t = (double)f / count;
                var s = t * t * (3 - 2 * t);
                Vec3 L(Vec3 a, Vec3 b) => a + (b - a) * s;
                frames.Add((index, new CameraState(L(from.Eye, to.Eye), L(from.Target, to.Target), L(from.Up, to.Up).Normalized(),
                    to.Perspective, from.FovDegrees + (to.FovDegrees - from.FovDegrees) * s, from.OrthoHeight + (to.OrthoHeight - from.OrthoHeight) * s)));
            }
        }
        return frames;
    }

    private async Task RecordAnimation(string path)
    {
        var frames = AnimationFrames();
        var before = _viewport.Camera.Save();
        var video = !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
        var folder = video ? Path.Combine(Path.GetTempPath(), $"dogeometric-animation-{Guid.NewGuid():N}") : Path.GetDirectoryName(path)!;
        var stem = video ? "frame" : Path.GetFileNameWithoutExtension(path);
        Directory.CreateDirectory(folder);
        var shown = -1;
        for (var i = 0; i < frames.Count; i++)
        {
            var (scene, camera) = frames[i];
            if (scene != shown)
            {
                _scenes.Go(scene, instant: true);
                shown = scene;
            }
            _viewport.ChangeCamera(c => c.Restore(camera));
            // Two frames: one to draw the new camera, one so the capture is of it.
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            _viewport.Snapshot().SavePng(Path.Combine(folder, $"{stem}{i + 1:D4}.png"));
            _status.SetHint($"Exporting animation: frame {i + 1} of {frames.Count}");
        }
        _viewport.ChangeCamera(c => c.Restore(before));
        if (video)
        {
            var codec = path.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ? new[] { "-c:v", "libvpx-vp9", "-b:v", "0", "-crf", "32" } : ["-c:v", "libx264", "-pix_fmt", "yuv420p"];
            // Video frames need even sizes.
            string[] args = ["-y", "-framerate", AnimationFps.ToString(), "-i", Path.Combine(folder, "frame%04d.png"),
                "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2", .. codec, path];
            var output = new Godot.Collections.Array();
            var code = OS.Execute("ffmpeg", args, output, true);
            Directory.Delete(folder, true);
            if (code != 0)
            {
                Alert("Export Animation", "Could not make the video (is ffmpeg installed?). Export as an image set instead.");
                return;
            }
        }
        _status.SetHint($"Exported {Path.GetFileName(path)} ({frames.Count} frames)");
    }
}
