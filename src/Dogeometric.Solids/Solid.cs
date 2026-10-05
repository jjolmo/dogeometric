using Dogeometric.Core.Geometry;
using Dogeometric.Core.IO;

namespace Dogeometric.Solids;

/// <summary>A closed triangle mesh held by Manifold. Dispose to free the native object.</summary>
public sealed unsafe class Solid : IDisposable
{
    internal IntPtr Handle { get; private set; }

    private Solid(IntPtr handle) => Handle = handle;

    public double Volume => ManifoldNative.Volume(Handle);
    public bool IsEmpty => ManifoldNative.IsEmpty(Handle) != 0;

    /// <summary>
    /// Builds a solid from world-space triangles (as <see cref="MeshExtractor"/> gives them). Vertices closer than
    /// <paramref name="weld"/> mm become one. Throws when the triangles don't close a volume.
    /// </summary>
    public static Solid FromTriangles(IReadOnlyList<Triangle> triangles, double weld = Tolerance.Length)
    {
        var index = new Dictionary<(long, long, long), ulong>();
        var positions = new List<double>();
        var tris = new List<ulong>(triangles.Count * 3);
        var cell = weld * 4;
        ulong Id(Vec3 p)
        {
            var key = ((long)Math.Round(p.X / cell), (long)Math.Round(p.Y / cell), (long)Math.Round(p.Z / cell));
            if (index.TryGetValue(key, out var id))
                return id;
            id = (ulong)(positions.Count / 3);
            index[key] = id;
            positions.Add(p.X);
            positions.Add(p.Y);
            positions.Add(p.Z);
            return id;
        }
        foreach (var t in triangles)
        {
            ulong a = Id(t.A), b = Id(t.B), c = Id(t.C);
            if (a == b || b == c || a == c)
                continue;
            tris.Add(a);
            tris.Add(b);
            tris.Add(c);
        }

        var pos = positions.ToArray();
        var tri = tris.ToArray();
        IntPtr mesh;
        fixed (double* pp = pos)
        fixed (ulong* tp = tri)
            mesh = ManifoldNative.MeshGL64(ManifoldNative.AllocMeshGL64(), pp, (nuint)(pos.Length / 3), 3, tp, (nuint)(tri.Length / 3));
        // Vertices are welded above. (manifold_meshgl64_merge is not used: when there is nothing to merge it hands
        // back the input mesh itself, so freeing both would free it twice.)
        var handle = ManifoldNative.OfMeshGL64(ManifoldNative.AllocManifold(), mesh);
        ManifoldNative.DeleteMeshGL64(mesh);
        var status = ManifoldNative.Status(handle);
        if (status != 0)
        {
            ManifoldNative.DeleteManifold(handle);
            throw new InvalidOperationException(status == 2 ? "not a solid (the mesh is not closed and manifold)" : $"Manifold error {status}");
        }
        return new Solid(handle);
    }

    public Solid Union(Solid other) => Op(other, ManifoldNative.OpType.Add);
    public Solid Subtract(Solid other) => Op(other, ManifoldNative.OpType.Subtract);
    public Solid Intersect(Solid other) => Op(other, ManifoldNative.OpType.Intersect);

    private Solid Op(Solid other, ManifoldNative.OpType op) =>
        new(ManifoldNative.Boolean(ManifoldNative.AllocManifold(), Handle, other.Handle, op));

    /// <summary>The solid as triangles (counter-clockwise from outside).</summary>
    public (List<Vec3> Positions, List<int> Triangles) ToMesh()
    {
        var mesh = ManifoldNative.GetMeshGL64(ManifoldNative.AllocMeshGL64(), Handle);
        try
        {
            var nv = (int)ManifoldNative.NumVert(mesh);
            var np = (int)ManifoldNative.NumProp(mesh);
            var nt = (int)ManifoldNative.NumTri(mesh);
            var props = new double[nv * np];
            var tris = new ulong[nt * 3];
            fixed (double* p = props)
                ManifoldNative.VertProperties(p, mesh);
            fixed (ulong* t = tris)
                ManifoldNative.TriVerts(t, mesh);
            var positions = new List<Vec3>(nv);
            for (var i = 0; i < nv; i++)
                positions.Add(new Vec3(props[i * np], props[i * np + 1], props[i * np + 2]));
            return (positions, tris.Select(x => (int)x).ToList());
        }
        finally
        {
            ManifoldNative.DeleteMeshGL64(mesh);
        }
    }

    /// <summary>The solid without its cavities: connected shells that enclose negative volume.</summary>
    public Solid WithoutCavities()
    {
        var (positions, tris) = ToMesh();
        var parent = Enumerable.Range(0, positions.Count).ToArray();
        int Root(int i) => parent[i] == i ? i : parent[i] = Root(parent[i]);
        for (var t = 0; t < tris.Count; t += 3)
        {
            parent[Root(tris[t + 1])] = Root(tris[t]);
            parent[Root(tris[t + 2])] = Root(tris[t]);
        }
        var volume = new Dictionary<int, double>();
        for (var t = 0; t < tris.Count; t += 3)
        {
            var root = Root(tris[t]);
            volume[root] = volume.GetValueOrDefault(root) + positions[tris[t]].Dot(positions[tris[t + 1]].Cross(positions[tris[t + 2]])) / 6;
        }
        var kept = new List<Triangle>();
        for (var t = 0; t < tris.Count; t += 3)
            if (volume[Root(tris[t])] > 0)
                kept.Add(new Triangle(positions[tris[t]], positions[tris[t + 1]], positions[tris[t + 2]], null));
        return FromTriangles(kept);
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero)
        {
            ManifoldNative.DeleteManifold(Handle);
            Handle = IntPtr.Zero;
        }
    }
}
