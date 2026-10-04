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
}

/// <summary>Dogeometric's own commands, which SketchUp does not have.</summary>
public static class OwnIds
{
    public const int RecoverBackup = 95001;
    public const int CenterPoints = 95002;
}
