// Back Edges: model edges again, through the faces in front of them, as short dashes (second pass of the edge
// material; visible edges stay solid because the first pass draws them over these).
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled, depth_test_disabled;
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
