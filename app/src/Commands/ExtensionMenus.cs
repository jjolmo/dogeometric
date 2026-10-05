using Dogeometric.Core.Modeling;
using Dogeometric.App.Tools;
using E = Dogeometric.App.Commands.ExtensionIds;

namespace Dogeometric.App.Commands;

/// <summary>
/// Where the cloned extensions sit in SketchUp's menus, copied from the reference install's menu bar (read from the
/// running SketchUp); commands still to be written show disabled.
/// </summary>
public static class ExtensionMenus
{
    private static int S(SplineKind k) => E.Spline(k);
    private static int F(Deformation d) => E.FredoScale(d);
    private static int T(SurfaceShape s) => E.SurfaceShape(s);

    public static void Apply(CommandRegistry registry)
    {
        registry.SetExtensionMenu("Draw", $$"""
            ---
            Circle by Diameter | {{E.CircleByDiameter}}
            ---
            BezierSpline curves
              Classic Bezier curve | {{S(SplineKind.ClassicBezier)}}
              Polyline | {{S(SplineKind.Polyline)}}
              Polyline Divider for Animation | {{E.SplineDividerAnimation}}
              Polyline Arc Corners | {{S(SplineKind.ArcCorners)}}
              Uniform B-Spline | {{S(SplineKind.UniformBSpline)}}
              Catmull Spline | {{S(SplineKind.CatmullSpline)}}
              Polyline Chamfer | {{S(SplineKind.Chamfer)}}
              Courbette | {{S(SplineKind.Courbette)}}
              Cubic Bezier curve | {{S(SplineKind.CubicBezier)}}
              Polyline Divider | {{S(SplineKind.Divider)}}
              Polyline Dog-Bone Corners | {{S(SplineKind.DogBone)}}
              Polyline T-Bone Corners | {{S(SplineKind.TBone)}}
              F-Spline | {{S(SplineKind.FSpline)}}
              Polyline Segmentor | {{S(SplineKind.Segmentor)}}
            ---
            Sphere | {{E.Sphere}}
            Sandbox
              From Contours | {{E.SandboxFromContours}}
              From Scratch | {{E.SandboxFromScratch}}
            """);

        registry.InsertNative("Tools", "Outer Shell", new MenuNode("Solid Tools", null,
        [
            new("Intersect", CommandIds.SolidIntersect, null, false), new("Union", CommandIds.SolidUnion, null, false),
            new("Subtract", CommandIds.SolidSubtract, null, false), new("Trim", CommandIds.SolidTrim, null, false),
            new("Split", CommandIds.SolidSplit, null, false),
        ], false));
        registry.SetExtensionMenu("Tools", $$"""
            ---
            Fredo6 Collection
              Curviloft
                Loft by Spline | {{E.CurviloftLoft}}
                Loft along path | {{E.CurviloftPath}}
                Skin Contours | {{E.CurviloftSkin}}
              FredoScale
                Quick Launcher... | {{E.FredoScaleLauncher}}
                ---
                Box Scaling | {{F(Deformation.Scale)}}
                Box Scaling to Target | {{E.FredoScaleScaleTarget}}
                ---
                Box Tapering | {{F(Deformation.Taper)}}
                Box Tapering to Target | {{E.FredoScaleTaperTarget}}
                ---
                Box Planar Shearing | {{F(Deformation.Shear)}}
                Box Planar Shearing to Target | {{E.FredoScaleShearTarget}}
                Planar Shearing (Free) | {{E.FredoScaleShearFree}}
                ---
                Box Stretching | {{F(Deformation.Stretch)}}
                Box Stretching to Target | {{E.FredoScaleStretchTarget}}
                ---
                Box Twisting | {{F(Deformation.Twist)}}
                ---
                Box Rotation | {{F(Deformation.Rotate)}}
                Rotation (Free) | {{E.FredoScaleRotateFree}}
                ---
                Radial Bending (Free) | {{F(Deformation.Bend)}}
                ---
                Make Unique | {{E.FredoScaleMakeUnique}}
              JointPushPull
                Quick Launcher... | {{E.JointPushPullLauncher}}
                Joint Push Pull | {{E.JointPushPull}}
                Round Push Pull | {{E.RoundPushPull}}
                Vector Push Pull | {{E.VectorPushPull}}
                Normal Push Pull | {{E.NormalPushPull}}
                Extrude Push Pull | {{E.ExtrudePushPull}}
                Follow Push Pull | {{E.FollowPushPull}}
              RoundCorner
                Round Corner | {{E.RoundCornerRound}}
                Sharp Corner | {{E.RoundCornerSharp}}
                Bevel | {{E.RoundCornerBevel}}
              ToolsOnSurface
                Generic Tools on Surface | {{E.SurfaceGeneric}}
                ---
                Line on Surface | {{T(SurfaceShape.Line)}}
                ---
                Rectangle | {{T(SurfaceShape.Rectangle)}}
                Circle | {{T(SurfaceShape.Circle)}}
                Polygon | {{T(SurfaceShape.Polygon)}}
                Ellipse | {{T(SurfaceShape.Ellipse)}}
                Parallelogram | {{T(SurfaceShape.Parallelogram)}}
                Arc | {{T(SurfaceShape.Arc)}}
                Circle (3 Points) | {{T(SurfaceShape.Circle3P)}}
                Sector (Pie) | {{T(SurfaceShape.Sector)}}
                ---
                Offset on Surface | {{E.SurfaceOffset}}
                Free Hand on Surface | {{T(SurfaceShape.Freehand)}}
                ---
                Edit Contours on Surface | {{T(SurfaceShape.Polyline)}}
                Eraser on Surface | {{E.SurfaceEraser}}
            ---
            Loop subdivision smooth | {{E.LoopSubdivision}}
            Advanced Camera Tools
              Create Camera | {{E.CameraCreate}}
              Look Through Camera | {{E.CameraLookThrough}}
              Lock/Unlock Current Camera | {{E.CameraLock}}
              Show/Hide All Cameras | {{E.CameraShowAll}}
              Show/Hide Camera Frustum Lines | {{E.CameraFrustumLines}}
              Show/Hide Camera Frustum Volume | {{E.CameraFrustumVolume}}
              Reset Camera | {{E.CameraReset}}
            Interact | {{E.Interact}}
            Sandbox
              Smoove | {{E.SandboxSmoove}}
              Stamp | {{E.SandboxStamp}}
              Drape | {{E.SandboxDrape}}
              ---
              Add Detail | {{E.SandboxAddDetail}}
              Flip Edge | {{E.SandboxFlipEdge}}
            Select Curve | {{E.SelectCurve}}
            Selection Toys
              UI Settings | {{E.SelectionToysSettings}}
              Cheat Sheet | {{E.SelectionToysCheatSheet}}
              ---
              Select Edge Loops | {{E.SelectEdgeLoops}}
            Solid Inspector² | {{E.SolidInspector}}
            """);

        registry.SetExtensionMenu("Window", $$"""
            ---
            Component Options | {{E.ComponentOptions}}
            Component Attributes | {{E.ComponentAttributes}}
            """);

        registry.SetExtensionMenu("Extensions", $$"""
            ---
            Make Faces | {{E.MakeFaces}}
            SUbD
              Subdivided | {{E.SubdSubdivided}}
              ---
              Increase Subdivisions | {{E.SubdIncrease}}
              Decrease Subdivisions | {{E.SubdDecrease}}
              ---
              Crease Tool | {{E.SubdCrease}}
              Quad Push/Pull Tool | {{E.SubdQuadPushPull}}
              ---
              Display Edges | {{E.SubdDisplayEdges}}
              ---
              Convert to Plain Mesh | {{E.SubdPlainMesh}}
              ---
              All Meshes
                Subdivision On | {{E.SubdOn}}
                Subdivision Off | {{E.SubdOff}}
              ---
              Preferences… | {{E.SubdPreferences}}
              ---
              Entity Info | {{E.SubdEntityInfo}}
              Getting Started | {{E.SubdGettingStarted}}
            CleanUp³
              Clean… | {{E.CleanUp}}
              Clean with Last Settings | {{E.CleanUpLast}}
              ---
              Erase Hidden Geometry | {{E.CleanUpEraseHidden}}
              Erase Stray Edges | {{E.CleanUpEraseStray}}
              Geometry to Layer0 | {{E.CleanUpToUntagged}}
              Merge Faces | {{E.CleanUpMergeFaces}}
              Merge Materials | {{E.CleanUpMergeMaterials}}
              Repair Edges | {{E.CleanUpRepairEdges}}
            """);
    }
}
