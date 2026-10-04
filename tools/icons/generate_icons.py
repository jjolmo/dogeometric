"""Generate Dogeometric's toolbar icons (24×24 SVG) into app/icons/.

Original artwork for Dogeometric (GPL-3.0). One consistent style: dark 1.5 px outlines, light grey fills, and the
red/green/blue axis colours plus orange for accents.
"""
import pathlib

OUT = pathlib.Path(__file__).resolve().parents[2] / "app" / "icons"
INK = "#2b2b2b"
FILL = "#e3e6ea"
RED, GREEN, BLUE, ORANGE, PINK = "#d02020", "#1a9a1a", "#2050d0", "#e8862a", "#f2a3b3"

S = f'stroke="{INK}" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"'


def svg(body):
    return f'<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none">{body}</svg>\n'


def cube(highlight=None, style="shaded"):
    """Isometric cube; highlight one face ('top', 'left', 'right') in blue for the view icons."""
    top = "M12 3 L20 7.5 L12 12 L4 7.5 Z"
    left = "M4 7.5 L12 12 L12 21 L4 16.5 Z"
    right = "M20 7.5 L12 12 L12 21 L20 16.5 Z"
    shade = {"top": "#f4f6f8", "left": "#d5dae0", "right": "#b9c1ca"}
    if style == "mono":
        shade = {"top": "#f2f2f2", "left": "#d0d0d0", "right": "#a8a8a8"}
    if style == "textured":
        shade = {"top": "#e9cf9f", "left": "#c9a46a", "right": "#a8824e"}
    if style == "xray":
        out = f'<path d="{top}" fill="#cfd8e3" fill-opacity="0.5"/><path d="{left}" fill="#b9c4d1" fill-opacity="0.5"/><path d="{right}" fill="#a3b0bf" fill-opacity="0.5"/>'
        out += f'<path d="M4 16.5 L12 12 L20 16.5 M12 12 L12 3" fill="none" stroke="{INK}" stroke-width="1"/>'
    elif style in ("wire", "hidden", "backedges"):
        fill = "#ffffff" if style != "wire" else "none"
        out = f'<path d="{top}" fill="{fill}"/><path d="{left}" fill="{fill}"/><path d="{right}" fill="{fill}"/>'
        if style == "wire":
            out += f'<path d="M4 16.5 L12 12 L20 16.5 M12 12 L12 3" fill="none" stroke="{INK}" stroke-width="1"/>'
        if style == "backedges":
            out += f'<path d="M4 16.5 L12 12.4 L20 16.5" fill="none" stroke="{INK}" stroke-width="1" stroke-dasharray="1.5 1.5"/>'
    else:
        out = "".join(f'<path d="{d}" fill="{BLUE if highlight == k else shade[k]}"/>' for k, d in (("top", top), ("left", left), ("right", right)))
    out += f'<path d="M12 3 L20 7.5 L20 16.5 L12 21 L4 16.5 L4 7.5 Z M4 7.5 L12 12 L20 7.5 M12 12 L12 21" {S}/>'
    return out


def magnifier(extra=""):
    return f'<circle cx="10" cy="10" r="6" fill="{FILL}" stroke="{INK}" stroke-width="1.5"/><path d="M14.5 14.5 L20.5 20.5" stroke="{INK}" stroke-width="3" stroke-linecap="round"/>{extra}'


ICONS = {
    # Large Tool Set
    "select": f'<path d="M6 2.5 L6 19 L10 15 L13 21.5 L15.8 20.3 L12.8 13.9 L18.5 13.9 Z" fill="{INK}"/>',
    "make_component": cube() + f'<circle cx="18.5" cy="18.5" r="4.5" fill="{GREEN}"/><path d="M18.5 16 V21 M16 18.5 H21" stroke="#fff" stroke-width="1.6"/>',
    "paint_bucket": f'<path d="M5 11 L12 4 L19 11 L12 18 Z" fill="{FILL}" {S}/><path d="M5 11 H19" {S}/><path d="M19.5 13.5 C21 16 21 17.5 19.5 18.5 C18 17.5 18 16 19.5 13.5 Z" fill="{BLUE}"/><path d="M3 21 H21" stroke="{RED}" stroke-width="2.5"/>',
    "eraser": f'<path d="M3.5 15 L12 6.5 L19 13.5 L12.5 20 H7 Z" fill="{PINK}" {S}/><path d="M7.5 11 L14.5 18" {S}/><path d="M12.5 20 H21" {S}/>',
    "line": f'<path d="M3 21 L21 3" stroke="{INK}" stroke-width="1.5"/><path d="M14 4.5 L19.5 10 L9 20.5 L3.5 20.5 L3.5 15 Z" fill="#f6c94a" {S}/><path d="M3.5 20.5 L6 18" {S}/>',
    "freehand": f'<path d="M3 17 C6 7 9 7 10 13 C11 19 15 19 16 12 C17 6 20 6 21 9" {S}/>',
    "rectangle": f'<rect x="3.5" y="6" width="17" height="12" fill="{FILL}" {S}/>',
    "rotated_rectangle": f'<path d="M3 12 L11 4 L21 14 L13 22 Z" fill="{FILL}" {S}/>',
    "circle": f'<circle cx="12" cy="12" r="8.5" fill="{FILL}" {S}/><circle cx="12" cy="12" r="1.2" fill="{INK}"/>',
    "polygon": f'<path d="M12 3.5 L19.4 7.8 L19.4 16.2 L12 20.5 L4.6 16.2 L4.6 7.8 Z" fill="{FILL}" {S}/><circle cx="12" cy="12" r="1.2" fill="{INK}"/>',
    "arc": f'<path d="M4 18 A 10 10 0 0 1 18 4" {S}/><path d="M4 18 L18 18 M18 18 L18 4" stroke="{INK}" stroke-width="1" stroke-dasharray="2 1.5"/><circle cx="18" cy="18" r="1.4" fill="{RED}"/>',
    "arc_2point": f'<path d="M3.5 18 Q12 1 20.5 18" {S}/><path d="M3.5 18 H20.5" stroke="{INK}" stroke-width="1" stroke-dasharray="2 1.5"/><circle cx="3.5" cy="18" r="1.5" fill="{RED}"/><circle cx="20.5" cy="18" r="1.5" fill="{RED}"/>',
    "arc_3point": f'<path d="M3.5 18 Q12 1 20.5 18" {S}/><circle cx="3.5" cy="18" r="1.5" fill="{RED}"/><circle cx="12" cy="9.6" r="1.5" fill="{RED}"/><circle cx="20.5" cy="18" r="1.5" fill="{RED}"/>',
    "pie": f'<path d="M5 19 L5 6 A 13 13 0 0 1 18 19 Z" fill="{FILL}" {S}/>',
    "move": f'<path d="M12 2.5 V21.5 M2.5 12 H21.5" stroke="{RED}" stroke-width="2"/><path d="M12 2.5 L9 5.5 M12 2.5 L15 5.5 M12 21.5 L9 18.5 M12 21.5 L15 18.5 M2.5 12 L5.5 9 M2.5 12 L5.5 15 M21.5 12 L18.5 9 M21.5 12 L18.5 15" stroke="{RED}" stroke-width="2" stroke-linecap="round"/>',
    "push_pull": f'<path d="M3 16 L12 20.5 L21 16 L12 11.5 Z" fill="{FILL}" {S}/><path d="M12 15 V3 M8.5 6.5 L12 3 L15.5 6.5" stroke="{RED}" stroke-width="2" fill="none" stroke-linecap="round" stroke-linejoin="round"/>',
    "rotate": f'<path d="M19 12 A 7 7 0 1 1 15.5 5.9" stroke="{RED}" stroke-width="2" fill="none" stroke-linecap="round"/><path d="M12.5 3.5 L16.5 5.5 L14 9" stroke="{RED}" stroke-width="2" fill="none" stroke-linecap="round" stroke-linejoin="round"/><circle cx="12" cy="12" r="1.5" fill="{INK}"/>',
    "follow_me": f'<path d="M3 19 H14 Q20 19 20 13 V4" stroke="{RED}" stroke-width="1.5" fill="none" stroke-dasharray="2.5 1.5"/><path d="M3 15 L7 15 L7 19 L3 19 Z" fill="{FILL}" {S}/><path d="M4 9 Q9 10 10 15" {S}/>',
    "scale": f'<rect x="3.5" y="8.5" width="12" height="12" fill="{FILL}" {S}/><path d="M10 14 L20.5 3.5 M14.5 3.5 H20.5 V9.5" stroke="{RED}" stroke-width="2" fill="none" stroke-linecap="round" stroke-linejoin="round"/><rect x="2" y="7" width="3" height="3" fill="{GREEN}"/><rect x="14" y="19" width="3" height="3" fill="{GREEN}"/>',
    "offset": f'<rect x="3" y="3" width="18" height="18" rx="3" fill="{FILL}" {S}/><rect x="7" y="7" width="10" height="10" rx="1" {S} stroke-dasharray="2.5 1.5"/>',
    "tape_measure": f'<circle cx="9" cy="12" r="6.5" fill="#f6c94a" {S}/><circle cx="9" cy="12" r="2" fill="{INK}"/><path d="M14 16 H22 V19 H11" fill="#f6c94a" {S}/><path d="M16 16 V17.5 M18.5 16 V17.5" {S}/>',
    "dimension": f'<path d="M3 5 V19 M21 5 V19" {S}/><path d="M3 12 H21 M3 12 L6 9.5 M3 12 L6 14.5 M21 12 L18 9.5 M21 12 L18 14.5" stroke="{BLUE}" stroke-width="1.5" fill="none" stroke-linecap="round"/>',
    "protractor": f'<path d="M3 18 A 9 9 0 0 1 21 18 Z" fill="{FILL}" {S}/><path d="M12 18 L18 11.5" stroke="{RED}" stroke-width="1.5"/><path d="M6 13.5 L7 14.5 M12 9 V10.5 M18 13.5 L17 14.5" {S}/>',
    "text": f'<path d="M3 4 H21 V15 H11 L6 20 V15 H3 Z" fill="#ffffff" {S}/><path d="M7 12 L9.5 6.5 L12 12 M8 10 H11" {S}/><path d="M14 6.5 H17 M15.5 6.5 V12" {S}/>',
    "axes": f'<path d="M5 19 H21" stroke="{RED}" stroke-width="2"/><path d="M5 19 L16 9" stroke="{GREEN}" stroke-width="2"/><path d="M5 19 V3" stroke="{BLUE}" stroke-width="2"/><circle cx="5" cy="19" r="1.8" fill="#f6c94a" stroke="{INK}"/>',
    "text_3d": f'<path d="M5 20 L10.5 5 H13.5 L19 20" fill="none" stroke="#9aa6b2" stroke-width="5" stroke-linejoin="round"/><path d="M4 19 L9.5 4 H12.5 L18 19 M6.5 13 H15.5" {S}/>',
    "section_plane": f'<path d="M4 7 L14 3 L14 17 L4 21 Z" fill="{ORANGE}" fill-opacity="0.35" stroke="{ORANGE}" stroke-width="1.5" stroke-linejoin="round"/><path d="M16 9 H21 M19 7 L21 9 L19 11 M16 15 H21 M19 13 L21 15 L19 17" stroke="{ORANGE}" stroke-width="1.5" fill="none" stroke-linecap="round" stroke-linejoin="round"/>',
    "orbit": f'<circle cx="12" cy="12" r="4.5" fill="{FILL}" {S}/><path d="M3 12 A 9 5 0 0 0 21 12" stroke="{RED}" stroke-width="1.8" fill="none"/><path d="M21 12 A 9 5 0 0 0 5 9" stroke="{RED}" stroke-width="1.8" fill="none"/><path d="M5.5 6.5 L4.5 9.5 L7.5 10" stroke="{RED}" stroke-width="1.8" fill="none" stroke-linecap="round" stroke-linejoin="round"/>',
    "pan": f'<path d="M8 13 V5.5 A1.5 1.5 0 0 1 11 5.5 V11 V4 A1.5 1.5 0 0 1 14 4 V11 V5 A1.5 1.5 0 0 1 17 5 V12 V8 A1.5 1.5 0 0 1 20 8 V15 C20 19 17.5 21.5 13.5 21.5 C10 21.5 8.5 20 6.5 17 L4 13 A1.6 1.6 0 0 1 6.8 11.5 Z" fill="#ffffff" {S}/>',
    "zoom": magnifier(f'<path d="M7 10 H13 M10 7 V13" {S}/>'),
    "zoom_window": magnifier(f'<rect x="7" y="7.5" width="6" height="5" {S} stroke-dasharray="1.6 1.2"/>'),
    "zoom_extents": magnifier(f'<path d="M6.5 6.5 L8.5 8.5 M13.5 6.5 L11.5 8.5 M6.5 13.5 L8.5 11.5 M13.5 13.5 L11.5 11.5" stroke="{RED}" stroke-width="1.5" stroke-linecap="round"/>'),
    "previous_camera": magnifier(f'<path d="M13 10 H7.5 M9.5 8 L7.5 10 L9.5 12" stroke="{RED}" stroke-width="1.5" fill="none" stroke-linecap="round" stroke-linejoin="round"/>'),
    "position_camera": f'<circle cx="12" cy="4.5" r="2.2" fill="{INK}"/><path d="M12 7 V14 M8 10 H16 M12 14 L9 21 M12 14 L15 21" {S}/><path d="M17 4.5 H21" stroke="{RED}" stroke-width="1.5" stroke-linecap="round"/>',
    "look_around": f'<path d="M2 12 C5 6 19 6 22 12 C19 18 5 18 2 12 Z" fill="#ffffff" {S}/><circle cx="12" cy="12" r="3.5" fill="{BLUE}"/><circle cx="12" cy="12" r="1.5" fill="{INK}"/>',
    "walk": f'<path d="M7 3 C9 3 10 5 9.5 8 C9 10.5 6 10.5 5.5 8 C5 5 5.5 3 7 3 Z M6 12 H9 V14 A1.5 1.5 0 0 1 6 14 Z" fill="{INK}"/><path d="M16 9 C18 9 19 11 18.5 14 C18 16.5 15 16.5 14.5 14 C14 11 14.5 9 16 9 Z M15 18 H18 V20 A1.5 1.5 0 0 1 15 20 Z" fill="{INK}"/>',
    # Standard
    "new": f'<path d="M5 2.5 H14 L19 7.5 V21.5 H5 Z" fill="#ffffff" {S}/><path d="M14 2.5 V7.5 H19" {S}/>',
    "open": f'<path d="M2.5 6 V19.5 H18.5 L21.5 10 H6 L3.5 18" fill="#f6c94a" {S}/><path d="M2.5 6 V4.5 H9 L10.5 6 H17 V10" fill="none" {S}/>',
    "save": f'<path d="M3.5 3.5 H17 L20.5 7 V20.5 H3.5 Z" fill="{BLUE}" {S}/><rect x="7" y="3.5" width="9" height="6" fill="#ffffff" {S}/><rect x="6.5" y="13" width="11" height="7.5" fill="#ffffff" {S}/>',
    "cut": f'<circle cx="7" cy="17" r="3" fill="#ffffff" {S}/><circle cx="17" cy="17" r="3" fill="#ffffff" {S}/><path d="M9 15 L17 3 M15 15 L7 3" {S}/>',
    "copy": f'<rect x="8.5" y="8.5" width="12" height="13" fill="#ffffff" {S}/><path d="M15.5 8.5 V2.5 H3.5 V15.5 H8.5" fill="#ffffff" {S}/>',
    "paste": f'<rect x="4" y="4" width="14" height="17" rx="1" fill="#c99a5b" {S}/><rect x="8" y="2.5" width="6" height="3" fill="#ffffff" {S}/><rect x="10" y="9" width="11" height="12.5" fill="#ffffff" {S}/>',
    "erase": f'<path d="M5 5 L19 19 M19 5 L5 19" stroke="{RED}" stroke-width="2.5" stroke-linecap="round"/>',
    "undo": f'<path d="M9 5 L4 10 L9 15" {S}/><path d="M4 10 H14 A6 6 0 0 1 14 22 H9" {S}/>',
    "redo": f'<path d="M15 5 L20 10 L15 15" {S}/><path d="M20 10 H10 A6 6 0 0 0 10 22 H15" {S}/>',
    "print": f'<rect x="6" y="3" width="12" height="6" fill="#ffffff" {S}/><rect x="2.5" y="9" width="19" height="8" rx="1" fill="{FILL}" {S}/><rect x="6" y="14" width="12" height="7" fill="#ffffff" {S}/>',
    "model_info": f'<circle cx="12" cy="12" r="9.5" fill="{BLUE}"/><circle cx="12" cy="7" r="1.6" fill="#ffffff"/><path d="M12 11 V18" stroke="#ffffff" stroke-width="2.5" stroke-linecap="round"/>',
    # Views
    "view_iso": cube(),
    "view_top": cube("top"),
    "view_front": cube("left"),
    "view_right": cube("right"),
    "view_back": cube("right") + f'<path d="M14 15 L18 13" stroke="#ffffff" stroke-width="1.2" stroke-dasharray="1.2 1"/>',
    "view_left": cube("left") + f'<path d="M6 13 L10 15" stroke="#ffffff" stroke-width="1.2" stroke-dasharray="1.2 1"/>',
    # Styles
    "style_xray": cube(style="xray"),
    "style_back_edges": cube(style="backedges"),
    "style_wireframe": cube(style="wire"),
    "style_hidden_line": cube(style="hidden"),
    "style_shaded": cube(),
    "style_textured": cube(style="textured"),
    "style_monochrome": cube(style="mono"),
}


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for name, body in ICONS.items():
        (OUT / f"{name}.svg").write_text(svg(body))
    print(f"wrote {len(ICONS)} icons to {OUT}")


if __name__ == "__main__":
    main()
