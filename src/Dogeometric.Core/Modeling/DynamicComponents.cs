using System.Globalization;
using System.Text.RegularExpressions;
using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

/// <summary>
/// Dynamic Components' Interact: a component's "onClick" attribute, such as ANIMATE("rotz",0,90) or SET("x",0,50),
/// moves it to the next value each click: turning about or sliding along its own axes from where it started.
/// </summary>
public static partial class DynamicComponents
{
    public const string OnClick = "onClick";
    private const string State = "_onClickStep";

    [GeneratedRegex(@"^\s*(?:ANIMATE|ANIMATESLOW|ANIMATEFAST|SET)\s*\(\s*""?(rotx|roty|rotz|x|y|z)""?\s*((?:,\s*-?[\d.]+\s*)+)\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Action();

    public static bool CanInteract(ComponentInstance inst) =>
        inst.Definition.Attributes.FirstOrDefault(a => a.Name.Equals(OnClick, StringComparison.OrdinalIgnoreCase)) is { } a && Action().IsMatch(a.Value);

    /// <summary>One click on <paramref name="inst"/>: its next onClick value applied. False when it has none it understands.</summary>
    public static bool Click(ComponentInstance inst)
    {
        var attrs = inst.Definition.Attributes;
        if (attrs.FirstOrDefault(a => a.Name.Equals(OnClick, StringComparison.OrdinalIgnoreCase)) is not { } onClick
            || Action().Match(onClick.Value) is not { Success: true } m)
            return false;
        var property = m.Groups[1].Value.ToLowerInvariant();
        var values = m.Groups[2].Value.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(v => double.Parse(v.Trim(), CultureInfo.InvariantCulture)).ToList();
        if (values.Count < 2)
            return false;
        var state = attrs.FirstOrDefault(a => a.Name == State);
        if (state == null)
            attrs.Add(state = new ComponentAttribute { Name = State, Value = "0" });
        var step = int.TryParse(state.Value, out var s) ? s : 0;
        var next = (step + 1) % values.Count;
        var delta = values[next] - values[step];
        state.Value = next.ToString(CultureInfo.InvariantCulture);
        var t = inst.Transform;
        var origin = t.ApplyPoint(Vec3.Zero);
        inst.Transform = property switch
        {
            "rotx" => t.Then(Transform.Rotation(t.X.Normalized(), delta * Math.PI / 180, origin)),
            "roty" => t.Then(Transform.Rotation(t.Y.Normalized(), delta * Math.PI / 180, origin)),
            "rotz" => t.Then(Transform.Rotation(t.Z.Normalized(), delta * Math.PI / 180, origin)),
            "x" => t.Then(Transform.Translation(t.X.Normalized() * delta)),
            "y" => t.Then(Transform.Translation(t.Y.Normalized() * delta)),
            _ => t.Then(Transform.Translation(t.Z.Normalized() * delta)),
        };
        return true;
    }
}
