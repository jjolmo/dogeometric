// Hidden Geometry: soft, smooth and hidden edges drawn dashed (depth-tested, unlike Back Edges).
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled;
#include "res://shaders/section.gdshaderinc"

uniform vec4 color : source_color = vec4(0.0, 0.0, 0.0, 1.0);

void vertex() {
	SECTION_TRACK;
}

void fragment() {
	if (SECTION_CUT) {
		discard;
	}
	// Screen-space dashes: about 3 px on, 3 px off along any direction.
	if (mod(floor((FRAGCOORD.x + FRAGCOORD.y) / 3.0), 2.0) > 0.5) {
		discard;
	}
	ALBEDO = color.rgb;
}
