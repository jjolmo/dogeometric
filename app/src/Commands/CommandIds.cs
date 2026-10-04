namespace Dogeometric.App;

/// <summary>SketchUp 2021 command ids (from SketchUp.exe's menu resources), reused so menus map 1:1.</summary>
public static class CommandIds
{
    // File
    public const int New = 57600;
    public const int Open = 57601;
    public const int Save = 57603;
    public const int SaveAs = 57604;
    public const int SaveCopyAs = 21136;
    public const int Import = 21933;
    public const int Export3DModel = 21149;
    public const int Exit = 57665;

    // Edit
    public const int Undo = 57643;
    public const int Redo = 57644;
    public const int Delete = 21021;
    public const int SelectAll = 21101;
    public const int SelectNone = 21102;
    public const int InvertSelection = 24447;
    public const int CloseGroup = 21202;
    public const int MakeGroup = 21182;
    public const int MakeComponent = 21083;

    // View › Face Style
    public const int StyleXRay = 10596;
    public const int StyleWireframe = 10597;
    public const int StyleHiddenLine = 10598;
    public const int StyleShaded = 10599;
    public const int StyleShadedTextures = 10600;
    public const int StyleMonochrome = 10601;

    // Edit › Hide / Lock
    public const int Hide = 21052;
    public const int UnhideSelected = 21152;
    public const int UnhideLast = 21099;
    public const int UnhideAll = 21053;
    public const int Lock = 21906;
    public const int UnlockSelected = 21915;
    public const int UnlockAll = 21914;

    // Tools › Solid Tools
    public const int OuterShell = 24198;
    public const int SolidIntersect = 24200;
    public const int SolidUnion = 24201;
    public const int SolidSubtract = 24202;
    public const int SolidTrim = 24203;
    public const int SolidSplit = 24204;

    // Help
    public const int About = 57664;

    // View
    public const int ToggleAxes = 10522;

    // Camera
    public const int PreviousCamera = 10529;
    public const int NextCamera = 10629;
    public const int ViewTop = 10501;
    public const int ViewFront = 10502;
    public const int ViewRight = 10503;
    public const int ViewLeft = 10504;
    public const int ViewBack = 10505;
    public const int ViewBottom = 10506;
    public const int ViewIso = 10507;
    public const int ParallelProjection = 10630;
    public const int Perspective = 10519;
    public const int Orbit = 10508;
    public const int Pan = 10523;
    public const int Zoom = 10509;
    public const int ZoomExtents = 10527;

    // Tools
    public const int Select = 21022;

    // Draw
    public const int Line = 21020;
    public const int Rectangle = 21094;
    public const int Circle = 21096;
    public const int Polygon = 21095;
    public const int Arc2Point = 21065;
    public const int PushPull = 21041;
    public const int Move = 21048;
    public const int Eraser = 21019;
    public const int TapeMeasure = 21024;
    public const int Rotate = 21129;
    public const int Scale = 21236;
    public const int FollowMe = 21525;
    public const int Dimension = 21410;
    public const int Protractor = 21057;
    public const int Axes = 21126;
    public const int Freehand = 21031;
    public const int Arc = 21069;
    public const int Pie = 21070;
    public const int Arc3Point = 21071;
    public const int RotatedRectangle = 24223;
    public const int PositionCamera = 21169;
    public const int LookAround = 10525;
    public const int Walk = 10520;
    public const int BackEdges = 10619;
    public const int HiddenGeometry = 21155;
    public const int Fog = 10618;
    public const int HiddenObjects = 21153;
    public const int HideRestOfModel = 21586;
    public const int HideSimilarComponents = 21587;
    public const int Text3D = 21940;
    public const int ModelInfo = 21076;
    public const int Preferences = 10521;
    public const int AddScene = 21067;
    public const int SceneTabs = 10534;
    public const int UpdateScene = 21068;
    public const int DeleteScene = 21078;
    public const int NextScene = 10535;
    public const int PreviousScene = 10536;
    public const int SectionPlane = 21337;
    public const int ReverseSection = 21334;
    public const int ActiveSectionCut = 21335;
    public const int DisplaySectionPlanes = 21347;
    public const int DisplaySectionCuts = 21348;
    public const int IntersectWithModel = 21524;
    public const int IntersectWithSelection = 21527;
    public const int IntersectWithContext = 21526;
    public const int ZoomWindow = 10526;
    public const int Cut = 57635;
    public const int Copy = 57634;
    public const int Paste = 57637;
    public const int PasteInPlace = 21939;
    public const int Text = 21405;
    public const int Offset = 21100;
    public const int PaintBucket = 21074;
    public const int DeleteGuides = 21044;
    public const int ToggleGuides = 21980;
}
