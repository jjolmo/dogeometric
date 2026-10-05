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
    public const int FollowPushPull = 90065;
    public const int RoundPushPull = 90066;
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
    public const int SplineDividerAnimation = 90114;
    public const int SplineEdit = 90120;
    public const int SplineVertexMarks = 90115;
    public const int SplineExtras = 90116;
    public const int SplineCloseNice = 90117;
    public const int SplineCloseLine = 90118;

    public const int SandboxFromContours = 90071;
    public const int SandboxFromScratch = 90072;
    public const int SandboxSmoove = 90073;
    public const int SandboxAddDetail = 90074;
    public const int SandboxFlipEdge = 90075;
    /// <summary>FredoScale's deformation tools, from 90081.</summary>
    public static int FredoScale(Dogeometric.Core.Modeling.Deformation kind) => 90081 + (int)kind;

    public const int SubdSubdivided = 90091;
    public const int JointPushPullLauncher = 90060;
    public const int FredoScaleLauncher = 90080;
    public const int SurfaceGeneric = 90130;
    public const int SubdIncrease = 90121;
    public const int SubdDecrease = 90122;
    public const int SubdCrease = 90123;
    public const int SubdPlainMesh = 90124;
    public const int SubdOn = 90125;
    public const int SubdOff = 90126;
    public const int SandboxDrape = 90076;
    public const int SandboxStamp = 90077;

    /// <summary>Tools on Surface's shape tools, from 90131 (spline ids run to 90112).</summary>
    public static int SurfaceShape(Dogeometric.App.Tools.SurfaceShape shape) => 90131 + (int)shape;

    public const int SurfaceEraser = 90150;
    public const int SurfaceOffset = 90151;
    public const int CurviloftLoft = 90095;
    public const int CurviloftSkin = 90096;
    public const int CurviloftPath = 90097;
    public const int CleanUp = 90041;
    public const int CleanUpLast = 90042;
    public const int CleanUpEraseHidden = 90043;
    public const int CleanUpEraseStray = 90044;
    public const int CleanUpToUntagged = 90045;
    public const int CleanUpMergeFaces = 90046;
    public const int CleanUpMergeMaterials = 90047;
    public const int CleanUpRepairEdges = 90048;

    public const int FredoScaleScaleTarget = 90201;
    public const int FredoScaleTaperTarget = 90202;
    public const int FredoScaleShearTarget = 90203;
    public const int FredoScaleStretchTarget = 90204;
    public const int FredoScaleShearFree = 90205;
    public const int FredoScaleRotateFree = 90206;
    public const int FredoScaleMakeUnique = 90207;
    public const int Interact = 90220;
    public const int SubdQuadPushPull = 90230;
    public const int SubdDisplayEdges = 90231;
    public const int SubdPreferences = 90232;
    public const int SubdEntityInfo = 90234;
    public const int SubdGettingStarted = 90235;
    public const int SelectionToysSettings = 90240;
    public const int SelectionToysCheatSheet = 90241;
    public const int SelectEdgeLoops = 90242;
    public const int ComponentOptions = 90250;
    public const int ComponentAttributes = 90251;
    public const int CameraCreate = 90260;
    public const int CameraLookThrough = 90261;
    public const int CameraLock = 90262;
    public const int CameraShowAll = 90263;
    public const int CameraFrustumLines = 90264;
    public const int CameraFrustumVolume = 90265;
    public const int CameraReset = 90266;
}

/// <summary>SketchUp's Help menu commands.</summary>
public static class HelpIds
{
    public const int Welcome = 24182;
    public const int HelpCenter = 57667;
    public const int ContactUs = 24184;
    public const int CheckForUpdate = 21931;
    public const int CheckYourSystem = 24435;
    public const int Search = 59423;
}

/// <summary>Dogeometric's own commands, which SketchUp does not have.</summary>
public static class OwnIds
{
    public const int RecoverBackup = 95001;
    public const int CenterPoints = 95002;
    public const int EditTextureImage = 95003;

    /// <summary>File › Generate Report (SketchUp's own id is not in the reference tables).</summary>
    public const int GenerateReport = 95004;

    /// <summary>The Shadows toolbar's Shadow Settings button (opens the Shadows panel).</summary>
    public const int ShadowSettings = 95005;
}
