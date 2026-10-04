using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class ComponentTests
{
    [Fact]
    public void Purge_unused_removes_orphans_and_what_only_they_used()
    {
        var model = new Model();
        var inner = new ComponentDefinition { Name = "Screw" };
        var outer = new ComponentDefinition { Name = "Lid" };
        var used = new ComponentDefinition { Name = "Board" };
        outer.Entities.AddInstance(inner, Transform.Identity);
        model.Definitions.AddRange([inner, outer, used]);
        model.Entities.AddInstance(used, Transform.Identity);
        Assert.Equal(2, Grouping.PurgeUnused(model));
        Assert.Equal([used], model.Definitions);
    }

    [Fact]
    public void Instances_are_found_at_any_depth()
    {
        var (model, a, _) = TestModels.TwoBoxGroups();
        var holder = new ComponentDefinition { Name = "Holder" };
        holder.Entities.AddInstance(a.Definition, Transform.Translation(new Vec3(0, 0, 100)));
        model.Definitions.Add(holder);
        model.Entities.AddInstance(holder, Transform.Identity);
        Assert.Equal(2, Grouping.InstancesOf(model, a.Definition).Count());
    }

    [Fact]
    public void Editing_a_shared_group_makes_it_unique_first()
    {
        var (model, a, _) = TestModels.TwoBoxGroups();
        var copy = model.Entities.AddInstance(a.Definition, Transform.Translation(new Vec3(0, 0, 500)));
        var doc = new Document(model);
        doc.Edit(copy);
        Assert.NotSame(a.Definition, copy.Definition);
        Assert.Equal(a.Definition.Entities.Faces.Count, copy.Definition.Entities.Faces.Count);
        Assert.Same(copy, doc.Context.Path[^1]);
    }
}
