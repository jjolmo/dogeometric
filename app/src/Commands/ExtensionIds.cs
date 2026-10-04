namespace Dogeometric.App.Commands;

/// <summary>
/// Command ids of the cloned SketchUp extensions. SketchUp gives extension commands no fixed ids, so they take a
/// range of their own, well above SketchUp's.
/// </summary>
public static class ExtensionIds
{
    public const int SolidInspector = 90001;
    public const int RoundCornerRound = 90011;
    public const int RoundCornerSharp = 90012;
    public const int RoundCornerBevel = 90013;
    public const int MakeFaces = 90021;
    public const int SelectOnlyEdges = 90031;
    public const int SelectOnlyFaces = 90032;
    public const int SelectOnlyGroups = 90033;
    public const int SelectOnlyComponents = 90034;
    public const int DeselectEdges = 90035;
    public const int DeselectFaces = 90036;
    public const int DeselectGroups = 90037;
    public const int DeselectComponents = 90038;
}

/// <summary>Dogeometric's own commands, which SketchUp does not have.</summary>
public static class OwnIds
{
    public const int RecoverBackup = 95001;
    public const int CenterPoints = 95002;
}
