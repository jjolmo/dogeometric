// Guide lines: dark grey dashes with a roughly constant on-screen length (see axes.gdshader).
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled;
#include "res://shaders/section.gdshaderinc"

uniform vec4 color : source_color = vec4(0.25, 0.25, 0.25, 1.0);
uniform float dashes_per_radian = 160.0;

void vertex() {
	SECTION_TRACK;
}

void fragment() {
	if (SECTION_CUT) {
		discard;
	}
	float depth = max(-VERTEX.z, 1e-4);
	if (fract(UV.x / depth * dashes_per_radian) > 0.55) {
		discard;
	}
	ALBEDO = color.rgb;
}
