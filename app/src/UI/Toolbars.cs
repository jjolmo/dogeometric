using Dogeometric.Core.Modeling;
namespace Dogeometric.App.UI;

/// <summary>SketchUp 2021's default toolbars: command ids in SketchUp's order, and each command's icon.</summary>
public static class Toolbars
{
    private const int Sep = Toolbar.Separator;

    public static readonly int[] Standard = [57600, 57601, 57603, Sep, 57635, 57634, 57637, 21021, Sep, 57643, 57644, Sep, 57607, 21076];
    public static readonly int[] Views = [10507, 10501, 10502, 10503, 10505, 10504];
    public static readonly int[] Styles = [10596, 10619, 10597, 10598, 10599, 10600, 10601];
    public static readonly int[] SolidTools = [24198, 24200, 24201, 24202, 24203, 24204];
    public static readonly int[] Section = [21337, 21347, 21348, 21349];
    public static readonly int[] DynamicComponents = [Commands.ExtensionIds.Interact, Commands.ExtensionIds.ComponentOptions, Commands.ExtensionIds.ComponentAttributes];
    public static readonly int[] AdvancedCameraTools =
    [
        Commands.ExtensionIds.CameraCreate, Commands.ExtensionIds.CameraLookThrough, Commands.ExtensionIds.CameraLock, Commands.ExtensionIds.CameraShowAll,
        Commands.ExtensionIds.CameraFrustumLines, Commands.ExtensionIds.CameraFrustumVolume, Commands.ExtensionIds.CameraReset,
    ];
    public static readonly int[] Tags = [Commands.OwnIds.TagList, Commands.OwnIds.TagsPanel];
    public static readonly int[] Shadows = [Commands.OwnIds.ShadowSettings, 10602, Commands.OwnIds.ShadowDate, Commands.OwnIds.ShadowTime];

    // Extension toolbars.
    public static readonly int[] SolidInspector = [Commands.ExtensionIds.SolidInspector];
    public static readonly int[] MakeFaces = [Commands.ExtensionIds.MakeFaces];
    // Extension toolbars as the reference install shows them (read from SketchUp's own toolbar list).
    private static int Tos(Tools.SurfaceShape shape) => Commands.ExtensionIds.SurfaceShape(shape);
    private static int Fsc(Deformation kind) => Commands.ExtensionIds.FredoScale(kind);
    public static readonly int[] ToolsOnSurface =
    [
        Commands.ExtensionIds.SurfaceGeneric, Sep, Tos(Tools.SurfaceShape.Line), Sep,
        Tos(Tools.SurfaceShape.Rectangle), Tos(Tools.SurfaceShape.Circle), Tos(Tools.SurfaceShape.Polygon), Tos(Tools.SurfaceShape.Ellipse),
        Tos(Tools.SurfaceShape.Parallelogram), Tos(Tools.SurfaceShape.Arc), Tos(Tools.SurfaceShape.Circle3P), Tos(Tools.SurfaceShape.Sector), Sep,
        Commands.ExtensionIds.SurfaceOffset, Tos(Tools.SurfaceShape.Freehand), Sep,
        Tos(Tools.SurfaceShape.Polyline), Commands.ExtensionIds.SurfaceEraser,
    ];
    public static readonly int[] Curviloft = [Commands.ExtensionIds.CurviloftLoft, Commands.ExtensionIds.CurviloftPath, Commands.ExtensionIds.CurviloftSkin];
    public static readonly int[] FredoScale =
    [
        Commands.ExtensionIds.FredoScaleLauncher, Sep, Fsc(Deformation.Scale), Sep, Fsc(Deformation.Taper), Sep,
        Fsc(Deformation.Shear), Commands.ExtensionIds.FredoScaleShearFree, Sep, Fsc(Deformation.Stretch), Sep, Fsc(Deformation.Twist), Sep,
        Fsc(Deformation.Rotate), Commands.ExtensionIds.FredoScaleRotateFree, Sep, Fsc(Deformation.Bend),
    ];
    public static readonly int[] Sandbox =
    [
        Commands.ExtensionIds.SandboxFromContours, Commands.ExtensionIds.SandboxFromScratch, Sep, Commands.ExtensionIds.SandboxSmoove,
        Commands.ExtensionIds.SandboxStamp, Commands.ExtensionIds.SandboxDrape, Commands.ExtensionIds.SandboxAddDetail, Commands.ExtensionIds.SandboxFlipEdge,
    ];
    private static int Bz(SplineKind kind) => Commands.ExtensionIds.Spline(kind);
    public static readonly int[] BezierSpline =
    [
        Bz(SplineKind.ClassicBezier), Bz(SplineKind.Polyline), Bz(SplineKind.DividerAnimation), Bz(SplineKind.ArcCorners),
        Bz(SplineKind.UniformBSpline), Bz(SplineKind.CatmullSpline), Bz(SplineKind.Chamfer), Bz(SplineKind.Courbette), Bz(SplineKind.CubicBezier),
        Bz(SplineKind.Divider), Bz(SplineKind.DogBone), Bz(SplineKind.TBone), Bz(SplineKind.FSpline), Sep,
        Commands.ExtensionIds.SplineEdit, Commands.ExtensionIds.SplineVertexMarks, Commands.ExtensionIds.SplineExtras,
        Commands.ExtensionIds.SplineCloseNice, Commands.ExtensionIds.SplineCloseLine,
    ];
    public static readonly int[] JointPushPull = [Commands.ExtensionIds.JointPushPullLauncher, Commands.ExtensionIds.JointPushPull, Commands.ExtensionIds.RoundPushPull, Commands.ExtensionIds.VectorPushPull, Commands.ExtensionIds.NormalPushPull, Commands.ExtensionIds.ExtrudePushPull, Commands.ExtensionIds.FollowPushPull];
    public static readonly int[] SelectCurve = [Commands.ExtensionIds.SelectCurve];
    public static readonly int[] Subd =
    [
        Commands.ExtensionIds.SubdSubdivided, Sep, Commands.ExtensionIds.SubdIncrease, Commands.ExtensionIds.SubdDecrease, Sep,
        Commands.ExtensionIds.SubdCrease, Commands.ExtensionIds.SubdQuadPushPull, Sep, Commands.ExtensionIds.SubdDisplayEdges, Sep,
        Commands.ExtensionIds.SubdEntityInfo, Sep, Commands.ExtensionIds.SubdGettingStarted,
    ];
    public static readonly int[] SelectionToys =
    [
        Commands.ExtensionIds.SelectOnlyEdges, Commands.ExtensionIds.SelectOnlyFaces, Commands.ExtensionIds.SelectOnlyGroups, Commands.ExtensionIds.SelectOnlyComponents, Sep,
        Commands.ExtensionIds.DeselectEdges, Commands.ExtensionIds.DeselectFaces, Commands.ExtensionIds.DeselectGroups, Commands.ExtensionIds.DeselectComponents,
    ];
    public static readonly int[] RoundCorner = [Commands.ExtensionIds.RoundCornerRound, Commands.ExtensionIds.RoundCornerSharp, Commands.ExtensionIds.RoundCornerBevel];
    public static readonly int[] GettingStarted = [21022, 21019, 21020, 21065, 21094, 21041, 21100, Sep, 21048, 21129, 21236, Sep, 21024, 21405, 21074, Sep, 10508, 10523, 10509, 10527];
    public static readonly int[] Principal = [21022, 21083, 21074, 21019];
    public static readonly int[] Drawing = [21020, 21031, 21094, 24223, 21096, 21095, 21069, 21065, 21071, 21070];
    public static readonly int[] Edit = [21048, 21041, 21129, 21525, 21236, 21100];
    public static readonly int[] Construction = [21024, 21410, 21057, 21405, 21126, 21940];
    public static readonly int[] Camera = [10508, 10523, 10509, 10526, 10527, 10529, 10629, Sep, 21169, 10525, 10520];

    /// <summary>The Large Tool Set, read in pairs (left column, right column).</summary>
    public static readonly int[] LargeToolSet =
    [
        21022, 21083,
        21074, 21019,
        21020, 21031,
        21094, 24223,
        21096, 21095,
        21069, 21065,
        21071, 21070,
        21048, 21041,
        21129, 21525,
        21236, 21100,
        21024, 21410,
        21057, 21405,
        21126, 21940,
        10508, 10523,
        10509, 10526,
        10527, 10529,
        21169, 10525,
        10520, 21337,
    ];

    public static readonly Dictionary<int, string> Icons = new()
    {
        [57600] = "new", [57601] = "open", [57603] = "save",
        [57635] = "cut", [57634] = "copy", [57637] = "paste", [21021] = "erase",
        [57643] = "undo", [57644] = "redo", [57607] = "print", [21076] = "model_info",
        [10507] = "view_iso", [10501] = "view_top", [10502] = "view_front", [10503] = "view_right", [10505] = "view_back", [10504] = "view_left",
        [10596] = "style_xray", [10619] = "style_back_edges", [10597] = "style_wireframe", [10598] = "style_hidden_line",
        [10599] = "style_shaded", [10600] = "style_textured", [10601] = "style_monochrome",
        [21022] = "select", [21083] = "make_component", [21074] = "paint_bucket", [21019] = "eraser",
        [21020] = "line", [21031] = "freehand", [21094] = "rectangle", [24223] = "rotated_rectangle",
        [21096] = "circle", [21095] = "polygon", [21069] = "arc", [21065] = "arc_2point", [21071] = "arc_3point", [21070] = "pie",
        [21048] = "move", [21041] = "push_pull", [21129] = "rotate", [21525] = "follow_me", [21236] = "scale", [21100] = "offset",
        [21024] = "tape_measure", [21410] = "dimension", [21057] = "protractor", [21405] = "text", [21126] = "axes", [21940] = "text_3d",
        [10508] = "orbit", [10523] = "pan", [10509] = "zoom", [10526] = "zoom_window", [10527] = "zoom_extents", [10529] = "previous_camera", [10629] = "next_camera",
        [Commands.ExtensionIds.SolidInspector] = "solid_inspector",
        [Commands.ExtensionIds.RoundCornerRound] = "roundcorner_round",
        [Commands.ExtensionIds.MakeFaces] = "make_faces",
        [Commands.ExtensionIds.CurviloftLoft] = "curviloft_loft",
        [Commands.ExtensionIds.CurviloftSkin] = "curviloft_skin",
        [Commands.ExtensionIds.CurviloftPath] = "curviloft_path",
        [Commands.ExtensionIds.SandboxFromContours] = "sandbox_from_contours",
        [Commands.ExtensionIds.SandboxFromScratch] = "sandbox_from_scratch",
        [Commands.ExtensionIds.SandboxSmoove] = "sandbox_smoove",
        [Commands.ExtensionIds.SandboxAddDetail] = "sandbox_add_detail",
        [Commands.ExtensionIds.SandboxFlipEdge] = "sandbox_flip_edge",
        [Commands.ExtensionIds.JointPushPull] = "jpp_joint",
        [Commands.ExtensionIds.NormalPushPull] = "jpp_normal",
        [Commands.ExtensionIds.VectorPushPull] = "jpp_vector",
        [Commands.ExtensionIds.ExtrudePushPull] = "jpp_extrude",
        [Commands.ExtensionIds.FollowPushPull] = "jpp_follow",
        [Commands.ExtensionIds.RoundPushPull] = "jpp_round",
        [Commands.ExtensionIds.SelectCurve] = "select_curve",
        [Commands.ExtensionIds.SelectOnlyEdges] = "select_only_edges",
        [Commands.ExtensionIds.SelectOnlyFaces] = "select_only_faces",
        [Commands.ExtensionIds.SelectOnlyGroups] = "select_only_groups",
        [Commands.ExtensionIds.SelectOnlyComponents] = "select_only_components",
        [Commands.ExtensionIds.DeselectEdges] = "deselect_edges",
        [Commands.ExtensionIds.DeselectFaces] = "deselect_faces",
        [Commands.ExtensionIds.DeselectGroups] = "deselect_groups",
        [Commands.ExtensionIds.DeselectComponents] = "deselect_components",
        [Commands.ExtensionIds.RoundCornerSharp] = "roundcorner_sharp",
        [Commands.ExtensionIds.RoundCornerBevel] = "roundcorner_bevel",
        [24198] = "solid_outer_shell", [24200] = "solid_intersect", [24201] = "solid_union",
        [24202] = "solid_subtract", [24203] = "solid_trim", [24204] = "solid_split",
        [21169] = "position_camera", [10525] = "look_around", [10520] = "walk", [21337] = "section_plane",
    };

    static Toolbars()
    {
        foreach (var kind in Enum.GetValues<Dogeometric.Core.Modeling.SplineKind>())
            Icons[Commands.ExtensionIds.Spline(kind)] = "spline_" + kind.ToString().ToLowerInvariant();
        foreach (var shape in Enum.GetValues<Dogeometric.App.Tools.SurfaceShape>())
            Icons[Commands.ExtensionIds.SurfaceShape(shape)] = "tos_" + shape.ToString().ToLowerInvariant();
        Icons[Commands.ExtensionIds.SandboxDrape] = "sandbox_drape";
        Icons[Commands.ExtensionIds.SandboxStamp] = "sandbox_stamp";
        Icons[Commands.ExtensionIds.SplineEdit] = "spline_edit";
        Icons[Commands.ExtensionIds.JointPushPullLauncher] = "jpp_launcher";
        Icons[Commands.ExtensionIds.FredoScaleLauncher] = "fredoscale_launcher";
        Icons[Commands.ExtensionIds.SurfaceGeneric] = "tos_generic";
        Icons[Commands.ExtensionIds.SubdSubdivided] = "subd_subdivided";
        Icons[Commands.ExtensionIds.SubdIncrease] = "subd_increase";
        Icons[Commands.ExtensionIds.SubdDecrease] = "subd_decrease";
        Icons[Commands.ExtensionIds.SubdCrease] = "subd_crease";
        Icons[Commands.ExtensionIds.SubdQuadPushPull] = "subd_quad_pushpull";
        Icons[21347] = "display_section_planes";
        Icons[Commands.ExtensionIds.CameraCreate] = "act_create";
        Icons[Commands.ExtensionIds.Interact] = "dc_interact";
        Icons[Commands.ExtensionIds.ComponentOptions] = "dc_options";
        Icons[Commands.ExtensionIds.ComponentAttributes] = "dc_attributes";
        Icons[Commands.ExtensionIds.CameraLookThrough] = "act_look_through";
        Icons[Commands.ExtensionIds.CameraLock] = "act_lock";
        Icons[Commands.ExtensionIds.CameraShowAll] = "act_show_cameras";
        Icons[Commands.ExtensionIds.CameraFrustumLines] = "act_frustum_lines";
        Icons[Commands.ExtensionIds.CameraFrustumVolume] = "act_frustum_volume";
        Icons[Commands.ExtensionIds.CameraReset] = "act_reset";
        Icons[Commands.OwnIds.TagsPanel] = "tags_panel";
        Icons[21348] = "display_section_cuts";
        Icons[21349] = "display_section_fill";
        Icons[10602] = "shadows_display";
        Icons[Commands.OwnIds.ShadowSettings] = "shadow_settings";
        Icons[Commands.ExtensionIds.SubdDisplayEdges] = "subd_display_edges";
        Icons[Commands.ExtensionIds.SubdEntityInfo] = "subd_entity_info";
        Icons[Commands.ExtensionIds.SubdGettingStarted] = "subd_help";
        Icons[Commands.ExtensionIds.FredoScaleShearFree] = "fredoscale_shear_free";
        Icons[Commands.ExtensionIds.FredoScaleRotateFree] = "fredoscale_rotate_free";
        Icons[Commands.ExtensionIds.SplineVertexMarks] = "spline_vertex_marks";
        Icons[Commands.ExtensionIds.SplineExtras] = "spline_extras";
        Icons[Commands.ExtensionIds.SplineCloseNice] = "spline_close_nice";
        Icons[Commands.ExtensionIds.SplineCloseLine] = "spline_close_line";
        Icons[Commands.ExtensionIds.SurfaceEraser] = "tos_eraser";
        Icons[Commands.ExtensionIds.SurfaceOffset] = "tos_offset";
        foreach (var kind in Enum.GetValues<Dogeometric.Core.Modeling.Deformation>())
            Icons[Commands.ExtensionIds.FredoScale(kind)] = "fredoscale_" + kind.ToString().ToLowerInvariant();
    }
}
