using Godot;

namespace Dogeometric.App.Tools;

/// <summary>Select tool. Selection itself arrives with the geometry core; for now it is the idle tool.</summary>
public sealed class SelectTool : Tool
{
    public override int CommandId => CommandIds.Select;
    public override string StatusText => "Click or drag to select objects. Shift = Add/Subtract. Ctrl = Add. Shift + Ctrl = Subtract.";
}
