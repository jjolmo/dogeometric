using System.Reflection;
using System.Runtime.InteropServices;

namespace Dogeometric.Solids;

/// <summary>
/// P/Invoke to Manifold's C API (libmanifoldc, Apache-2.0, built by tools/native/build-manifold.sh). Only the
/// double-precision mesh and boolean calls Dogeometric needs.
/// </summary>
internal static unsafe partial class ManifoldNative
{
    private const string Lib = "manifoldc";

    public enum OpType
    {
        Add = 0,
        Subtract = 1,
        Intersect = 2,
    }

    /// <summary>Extra folders to look for the native library in (the app adds its native/&lt;rid&gt; folder).</summary>
    public static readonly List<string> SearchDirectories = [];

    static ManifoldNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(ManifoldNative).Assembly, Resolve);
    }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path)
    {
        if (name != Lib)
            return IntPtr.Zero;
        var file = OperatingSystem.IsWindows() ? "manifoldc.dll" : OperatingSystem.IsMacOS() ? "libmanifoldc.dylib" : "libmanifoldc.so";
        foreach (var dir in SearchDirectories.Append(AppContext.BaseDirectory))
        {
            // A copy that is there but will not load says why (a missing dependency, the wrong architecture).
            var candidate = Path.Combine(dir, file);
            if (File.Exists(candidate))
                return NativeLibrary.Load(candidate);
        }
        return NativeLibrary.TryLoad(file, assembly, path, out var h) ? h : IntPtr.Zero;
    }

    [LibraryImport(Lib, EntryPoint = "manifold_alloc_meshgl64")]
    public static partial IntPtr AllocMeshGL64();

    [LibraryImport(Lib, EntryPoint = "manifold_alloc_manifold")]
    public static partial IntPtr AllocManifold();

    [LibraryImport(Lib, EntryPoint = "manifold_meshgl64")]
    public static partial IntPtr MeshGL64(IntPtr mem, double* vertProps, nuint nVerts, nuint nProps, ulong* triVerts, nuint nTris);

    [LibraryImport(Lib, EntryPoint = "manifold_meshgl64_merge")]
    public static partial IntPtr MeshGL64Merge(IntPtr mem, IntPtr mesh);

    [LibraryImport(Lib, EntryPoint = "manifold_of_meshgl64")]
    public static partial IntPtr OfMeshGL64(IntPtr mem, IntPtr mesh);

    [LibraryImport(Lib, EntryPoint = "manifold_get_meshgl64")]
    public static partial IntPtr GetMeshGL64(IntPtr mem, IntPtr manifold);

    [LibraryImport(Lib, EntryPoint = "manifold_boolean")]
    public static partial IntPtr Boolean(IntPtr mem, IntPtr a, IntPtr b, OpType op);

    [LibraryImport(Lib, EntryPoint = "manifold_status")]
    public static partial int Status(IntPtr manifold);

    [LibraryImport(Lib, EntryPoint = "manifold_is_empty")]
    public static partial int IsEmpty(IntPtr manifold);

    [LibraryImport(Lib, EntryPoint = "manifold_volume")]
    public static partial double Volume(IntPtr manifold);

    [LibraryImport(Lib, EntryPoint = "manifold_meshgl64_num_vert")]
    public static partial nuint NumVert(IntPtr mesh);

    [LibraryImport(Lib, EntryPoint = "manifold_meshgl64_num_tri")]
    public static partial nuint NumTri(IntPtr mesh);

    [LibraryImport(Lib, EntryPoint = "manifold_meshgl64_num_prop")]
    public static partial nuint NumProp(IntPtr mesh);

    [LibraryImport(Lib, EntryPoint = "manifold_meshgl64_vert_properties")]
    public static partial double* VertProperties(void* mem, IntPtr mesh);

    [LibraryImport(Lib, EntryPoint = "manifold_meshgl64_tri_verts")]
    public static partial ulong* TriVerts(void* mem, IntPtr mesh);

    [LibraryImport(Lib, EntryPoint = "manifold_delete_meshgl64")]
    public static partial void DeleteMeshGL64(IntPtr mesh);

    [LibraryImport(Lib, EntryPoint = "manifold_delete_manifold")]
    public static partial void DeleteManifold(IntPtr manifold);
}
