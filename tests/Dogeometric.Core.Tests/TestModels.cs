using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

/// <summary>Small hand-built models for tests.</summary>
public static class TestModels
{
    /// <summary>Adds an axis-aligned closed box (6 faces wound outwards) to <paramref name="e"/>.</summary>
    public static void Box(Entities e, Vec3 min, Vec3 size)
    {
        Vec3 P(double x, double y, double z) => min + new Vec3(x * size.X, y * size.Y, z * size.Z);
        e.AddFace([P(0, 0, 0), P(0, 1, 0), P(1, 1, 0), P(1, 0, 0)]); // bottom, -Z
        e.AddFace([P(0, 0, 1), P(1, 0, 1), P(1, 1, 1), P(0, 1, 1)]); // top, +Z
        e.AddFace([P(0, 0, 0), P(1, 0, 0), P(1, 0, 1), P(0, 0, 1)]); // front, -Y
        e.AddFace([P(0, 1, 0), P(0, 1, 1), P(1, 1, 1), P(1, 1, 0)]); // back, +Y
        e.AddFace([P(0, 0, 0), P(0, 0, 1), P(0, 1, 1), P(0, 1, 0)]); // left, -X
        e.AddFace([P(1, 0, 0), P(1, 1, 0), P(1, 1, 1), P(1, 0, 1)]); // right, +X
    }

    /// <summary>A model with two box groups side by side; returns the model and both instances.</summary>
    public static (Model Model, ComponentInstance A, ComponentInstance B) TwoBoxGroups()
    {
        var model = new Model();
        var defA = new ComponentDefinition { Name = "Group#1", IsGroup = true };
        Box(defA.Entities, Vec3.Zero, new Vec3(10, 20, 30));
        var defB = new ComponentDefinition { Name = "Group#2", IsGroup = true };
        Box(defB.Entities, Vec3.Zero, new Vec3(5, 5, 5));
        model.Definitions.Add(defA);
        model.Definitions.Add(defB);
        var a = model.Entities.AddInstance(defA, Transform.Identity);
        var b = model.Entities.AddInstance(defB, Transform.Translation(new Vec3(100, 0, 0)));
        return (model, a, b);
    }
}
