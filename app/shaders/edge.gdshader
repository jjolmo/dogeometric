// Model edges: unshaded lines pulled slightly towards the camera so they win the depth test against the faces
// they bound, as SketchUp draws edges on top of coplanar faces.
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled;
#include "res://shaders/section.gdshaderinc"
// How far this fades while another group is open for editing (Model Info › Components), as in SketchUp.
instance uniform float fade = 0.0;

uniform vec4 color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
// Styles › Edge color by material or by axis: each edge brings its own colour.
uniform bool vertex_colors = false;
// Relative pull towards the camera (fraction of the distance). Reverse-Z depth is proportional to 1/distance,
// so scaling clip z by (1 + bias) moves the point bias × distance closer: a few millimetres at a few metres.
uniform float depth_bias = 0.0006;

void vertex() {
	SECTION_TRACK;
	POSITION = PROJECTION_MATRIX * MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
	if (PROJECTION_MATRIX[3][3] == 0.0) {
		POSITION.z *= 1.0 + depth_bias;
	} else {
		// Parallel projection: depth is linear over the whole far range (~100 km), so pull a fixed ~5 mm.
		POSITION.z += 5e-8;
	}
}

void fragment() {
	if (SECTION_CUT) {
		discard;
	}
	ALBEDO = vertex_colors ? COLOR.rgb : color.rgb;
	ALBEDO = mix(ALBEDO, vec3(0.72), fade);
}
