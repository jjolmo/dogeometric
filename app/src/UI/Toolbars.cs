namespace Dogeometric.App.UI;

/// <summary>SketchUp 2021's default toolbars: command ids in SketchUp's order, and each command's icon.</summary>
public static class Toolbars
{
    private const int Sep = Toolbar.Separator;

    public static readonly int[] Standard = [57600, 57601, 57603, Sep, 57635, 57634, 57637, 21021, Sep, 57643, 57644, Sep, 57607, 21076];
    public static readonly int[] Views = [10507, 10501, 10502, 10503, 10505, 10504];
    public static readonly int[] Styles = [10596, 10619, 10597, 10598, 10599, 10600, 10601];
    public static readonly int[] SolidTools = [24198, 24200, 24201, 24202, 24203, 24204];

    // Extension toolbars.
    public static readonly int[] SolidInspector = [Commands.ExtensionIds.SolidInspector];
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
        [Commands.ExtensionIds.RoundCornerSharp] = "roundcorner_sharp",
        [Commands.ExtensionIds.RoundCornerBevel] = "roundcorner_bevel",
        [24198] = "solid_outer_shell", [24200] = "solid_intersect", [24201] = "solid_union",
        [24202] = "solid_subtract", [24203] = "solid_trim", [24204] = "solid_split",
        [21169] = "position_camera", [10525] = "look_around", [10520] = "walk", [21337] = "section_plane",
    };
}
