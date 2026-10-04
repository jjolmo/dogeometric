// SketchUp's selected-face highlight: a regular pattern of blue dots over the face.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled;

uniform vec4 color : source_color = vec4(0.0, 0.0, 1.0, 1.0);
// Relative pull towards the camera (fraction of the distance), see edge.gdshader.
uniform float depth_bias = 0.0003;

void vertex() {
	POSITION = PROJECTION_MATRIX * MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
	if (PROJECTION_MATRIX[3][3] == 0.0) {
		POSITION.z *= 1.0 + depth_bias;
	} else {
		// Parallel projection: depth is linear over the whole far range (~100 km), so pull a fixed ~5 mm.
		POSITION.z += 5e-8;
	}
}

void fragment() {
	ivec2 p = ivec2(FRAGCOORD.xy);
	if ((p.x % 4) != 0 || (p.y % 4) != 0) {
		discard;
	}
	ALBEDO = color.rgb;
}
