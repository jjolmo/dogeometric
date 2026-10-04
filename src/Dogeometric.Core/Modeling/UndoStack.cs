using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

/// <summary>
/// Edit → Undo/Redo. Each operation snapshots the entity collections it is about to touch (plus the model's
/// definition, material and tag lists) and the result afterwards; undo restores the "before" state in place, so
/// object identity (selection, references from other definitions) survives.
/// </summary>
public sealed class UndoStack(Model model)
{
    private const int Capacity = 100;

    private readonly List<Step> _undo = [];
    private readonly List<Step> _redo = [];
    private Pending? _pending;

    public event Action? Changed;

    /// <summary>Entity collections changed by the last commit, undo, redo or abort (for partial redraws).</summary>
    public IReadOnlyCollection<Entities> LastTouched { get; private set; } = [];

    public string? UndoName => _undo.Count > 0 ? _undo[^1].Name : null;
    public string? RedoName => _redo.Count > 0 ? _redo[^1].Name : null;

    /// <summary>
    /// Starts an operation named like SketchUp's ("Erase", "Push/Pull"...). Pass every entity collection the
    /// operation may change. Must be closed with <see cref="Commit"/> or <see cref="Abort"/>.
    /// </summary>
    public void Begin(string name, params Entities[] touched)
    {
        if (_pending != null)
            throw new InvalidOperationException($"Operation '{_pending.Name}' is still open");
        _pending = new Pending(name, touched.Distinct().ToArray(), ModelState.Capture(model), touched.Distinct().Select(EntitiesState.Capture).ToArray());
    }

    /// <summary>Adds more collections to the open operation (e.g. a definition discovered mid-operation).</summary>
    public void Touch(Entities entities)
    {
        if (_pending == null || _pending.Touched.Contains(entities))
            return;
        _pending = _pending with
        {
            Touched = [.. _pending.Touched, entities],
            Before = [.. _pending.Before, EntitiesState.Capture(entities)],
        };
    }

    public void Commit()
    {
        if (_pending is not { } p)
            return;
        _pending = null;
        var after = p.Touched.Select(EntitiesState.Capture).ToArray();
        _undo.Add(new Step(p.Name, p.ModelBefore, p.Before, ModelState.Capture(model), after));
        LastTouched = p.Touched;
        if (_undo.Count > Capacity)
            _undo.RemoveAt(0);
        _redo.Clear();
        Changed?.Invoke();
    }

    /// <summary>Cancels the open operation and puts everything back.</summary>
    /// <summary>Puts back what the pending operation started from, keeping it open (live previews redo it).</summary>
    public void Revert()
    {
        if (_pending is not { } p)
            return;
        p.ModelBefore.Restore(model);
        foreach (var s in p.Before)
            s.Restore();
    }

    public bool IsPending => _pending != null;

    public void Abort()
    {
        if (_pending is not { } p)
            return;
        _pending = null;
        p.ModelBefore.Restore(model);
        foreach (var s in p.Before)
            s.Restore();
        LastTouched = p.Touched;
        Changed?.Invoke();
    }

    public bool Undo()
    {
        if (_undo.Count == 0)
            return false;
        var step = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        step.ModelBefore.Restore(model);
        foreach (var s in step.Before)
            s.Restore();
        LastTouched = step.Before.Select(s => s.Target).ToArray();
        _redo.Add(step);
        Changed?.Invoke();
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0)
            return false;
        var step = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        step.ModelAfter.Restore(model);
        foreach (var s in step.After)
            s.Restore();
        LastTouched = step.After.Select(s => s.Target).ToArray();
        _undo.Add(step);
        Changed?.Invoke();
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _pending = null;
        Changed?.Invoke();
    }

    private sealed record Pending(string Name, Entities[] Touched, ModelState ModelBefore, EntitiesState[] Before);

    private sealed record Step(string Name, ModelState ModelBefore, EntitiesState[] Before, ModelState ModelAfter, EntitiesState[] After);

    private sealed record ModelState(ComponentDefinition[] Definitions, Material[] Materials, Tag[] Tags, Transform Axes, Units.LengthUnit Units, int Precision)
    {
        public static ModelState Capture(Model m) => new([.. m.Definitions], [.. m.Materials], [.. m.Tags], m.Axes, m.Units, m.UnitPrecision);

        public void Restore(Model m)
        {
            Replace(m.Definitions, Definitions);
            Replace(m.Materials, Materials);
            Replace(m.Tags, Tags);
            m.Axes = Axes;
            m.Units = Units;
            m.UnitPrecision = Precision;
        }
    }

    /// <summary>Everything an operation can change inside one entity collection.</summary>
    private sealed class EntitiesState
    {
        private Entities _target = null!;

        public Entities Target => _target;
        private Vertex[] _vertices = [];
        private Vec3[] _positions = [];
        private Edge[] _edges = [];
        private (Vertex Start, Vertex End, EdgeFlags Flags, Tag? Tag, Material? Material)[] _edgeData = [];
        private Face[] _faces = [];
        private (List<(Edge, bool)>[] Loops, Material? Front, Material? Back, Tag? Tag, bool Hidden)[] _faceData = [];
        private ComponentInstance[] _instances = [];
        private (GuideLine Guide, Geometry.Vec3 Point, Geometry.Vec3 Dir, Geometry.Vec3? Start, Geometry.Vec3? End)[] _guideLines = [];
        private (GuidePoint Guide, Geometry.Vec3 Position)[] _guidePoints = [];
        private (LinearDimension Dim, Vec3 Start, Vec3 End, Vec3 Offset, string Text, Tag? Tag, bool Hidden)[] _dimensions = [];
        private (SectionPlane Plane, Vec3 Point, Vec3 Normal, string Name, Tag? Tag, bool Hidden)[] _sections = [];
        private SectionPlane? _activeSection;
        private (TextLabel Label, string Text, Vec3 Point, Vec3 Offset, (double, double)? Screen, Tag? Tag, bool Hidden)[] _texts = [];
        private (ComponentDefinition Def, Transform Xf, string Name, Tag? Tag, Material? Material, bool Hidden, bool Locked)[] _instanceData = [];

        public static EntitiesState Capture(Entities e) => new()
        {
            _target = e,
            _vertices = [.. e.Vertices],
            _positions = e.Vertices.Select(v => v.Position).ToArray(),
            _edges = [.. e.Edges],
            _edgeData = e.Edges.Select(x => (x.Start, x.End, x.Flags, x.Tag, x.Material)).ToArray(),
            _faces = [.. e.Faces],
            _faceData = e.Faces.Select(f => (f.Loops.Select(l => l.Edges.ToList()).ToArray(), f.FrontMaterial, f.BackMaterial, f.Tag, f.Hidden)).ToArray(),
            _instances = [.. e.Instances],
            _instanceData = e.Instances.Select(i => (i.Definition, i.Transform, i.Name, i.Tag, i.Material, i.Hidden, i.Locked)).ToArray(),
            _guideLines = e.GuideLines.Select(g => (g, g.Point, g.Direction, g.Start, g.End)).ToArray(),
            _guidePoints = e.GuidePoints.Select(g => (g, g.Position)).ToArray(),
            _dimensions = e.Dimensions.Select(d => (d, d.Start, d.End, d.Offset, d.Text, d.Tag, d.Hidden)).ToArray(),
            _sections = e.SectionPlanes.Select(s => (s, s.Point, s.Normal, s.Name, s.Tag, s.Hidden)).ToArray(),
            _activeSection = e.ActiveSection,
            _texts = e.Texts.Select(t => (t, t.Text, t.Point, t.Offset, t.ScreenPosition, t.Tag, t.Hidden)).ToArray(),
        };

        public void Restore()
        {
            Replace(_target.GuideLines, _guideLines.Select(g => g.Guide).ToArray());
            foreach (var (g, point, dir, start, end) in _guideLines)
            {
                g.Point = point;
                g.Direction = dir;
                g.Start = start;
                g.End = end;
            }
            Replace(_target.GuidePoints, _guidePoints.Select(g => g.Guide).ToArray());
            foreach (var (g, pos) in _guidePoints)
                g.Position = pos;
            Replace(_target.Dimensions, _dimensions.Select(d => d.Dim).ToArray());
            foreach (var (d, start, end, offset, text, tag, hidden) in _dimensions)
            {
                d.Start = start;
                d.End = end;
                d.Offset = offset;
                d.Text = text;
                d.Tag = tag;
                d.Hidden = hidden;
            }
            Replace(_target.SectionPlanes, _sections.Select(s => s.Plane).ToArray());
            foreach (var (s, point, normal, name, tag, hidden) in _sections)
            {
                s.Point = point;
                s.Normal = normal;
                s.Name = name;
                s.Tag = tag;
                s.Hidden = hidden;
            }
            _target.ActiveSection = _activeSection;
            Replace(_target.Texts, _texts.Select(t => t.Label).ToArray());
            foreach (var (t, text, point, offset, screen, tag, hidden) in _texts)
            {
                t.Text = text;
                t.Point = point;
                t.Offset = offset;
                t.ScreenPosition = screen;
                t.Tag = tag;
                t.Hidden = hidden;
            }

            Replace(_target.Vertices, _vertices);
            for (var i = 0; i < _vertices.Length; i++)
                _vertices[i].Position = _positions[i];

            Replace(_target.Edges, _edges);
            for (var i = 0; i < _edges.Length; i++)
            {
                var (s, en, flags, tag, mat) = _edgeData[i];
                var edge = _edges[i];
                edge.Start = s;
                edge.End = en;
                edge.Flags = flags;
                edge.Tag = tag;
                edge.Material = mat;
            }

            Replace(_target.Faces, _faces);
            for (var i = 0; i < _faces.Length; i++)
            {
                var (loops, front, back, tag, hidden) = _faceData[i];
                var face = _faces[i];
                face.Loops.Clear();
                foreach (var l in loops)
                {
                    var loop = new FaceLoop();
                    loop.Edges.AddRange(l);
                    face.Loops.Add(loop);
                }
                face.FrontMaterial = front;
                face.BackMaterial = back;
                face.Tag = tag;
                face.Hidden = hidden;
            }

            Replace(_target.Instances, _instances);
            for (var i = 0; i < _instances.Length; i++)
            {
                var (def, xf, name, tag, mat, hidden, locked) = _instanceData[i];
                var inst = _instances[i];
                inst.Definition = def;
                inst.Transform = xf;
                inst.Name = name;
                inst.Tag = tag;
                inst.Material = mat;
                inst.Hidden = hidden;
                inst.Locked = locked;
            }
        }
    }

    private static void Replace<T>(List<T> list, T[] items)
    {
        list.Clear();
        list.AddRange(items);
    }
}
