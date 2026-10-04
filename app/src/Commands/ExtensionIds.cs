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
    public const int JointPushPull = 90061;
    public const int NormalPushPull = 90062;
    public const int VectorPushPull = 90063;
    public const int ExtrudePushPull = 90064;
    public const int SelectOnlyEdges = 90031;
    public const int SelectOnlyFaces = 90032;
    public const int SelectOnlyGroups = 90033;
    public const int SelectOnlyComponents = 90034;
    public const int DeselectEdges = 90035;
    public const int DeselectFaces = 90036;
    public const int DeselectGroups = 90037;
    public const int DeselectComponents = 90038;
    public const int CircleByDiameter = 90051;
    public const int SelectCurve = 90052;
    public const int LoopSubdivision = 90053;
    public const int Sphere = 90054;
    /// <summary>BezierSpline's curve tools, one id per family from 90101.</summary>
    public static int Spline(Dogeometric.Core.Modeling.SplineKind kind) => 90101 + (int)kind;

    public const int SandboxFromContours = 90071;
    public const int SandboxFromScratch = 90072;
    public const int SandboxSmoove = 90073;
    public const int SandboxAddDetail = 90074;
    public const int SandboxFlipEdge = 90075;
    /// <summary>FredoScale's deformation tools, from 90081.</summary>
    public static int FredoScale(Dogeometric.Core.Modeling.Deformation kind) => 90081 + (int)kind;

    public const int SubdSubdivide = 90091;
    public const int SandboxDrape = 90076;

    /// <summary>Tools on Surface's shape tools, from 90131 (spline ids run to 90112).</summary>
    public static int SurfaceShape(Dogeometric.App.Tools.SurfaceShape shape) => 90131 + (int)shape;

    public const int SurfaceEraser = 90150;
    public const int SurfaceOffset = 90151;
    public const int CurviloftLoft = 90095;
    public const int CurviloftSkin = 90096;
    public const int CleanUp = 90041;
    public const int CleanUpLast = 90042;
    public const int CleanUpEraseHidden = 90043;
    public const int CleanUpEraseStray = 90044;
    public const int CleanUpToUntagged = 90045;
    public const int CleanUpMergeFaces = 90046;
    public const int CleanUpMergeMaterials = 90047;
    public const int CleanUpRepairEdges = 90048;
}

/// <summary>Dogeometric's own commands, which SketchUp does not have.</summary>
public static class OwnIds
{
    public const int RecoverBackup = 95001;
    public const int CenterPoints = 95002;
}
