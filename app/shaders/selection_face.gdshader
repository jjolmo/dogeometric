// SketchUp's selected-face highlight: a regular pattern of blue dots over the face.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled;

uniform vec4 color : source_color = vec4(0.0, 0.0, 1.0, 1.0);
uniform float depth_bias = 0.00002;

void vertex() {
	POSITION = PROJECTION_MATRIX * MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
	POSITION.z += depth_bias * POSITION.w;
}

void fragment() {
	ivec2 p = ivec2(FRAGCOORD.xy);
	if ((p.x % 4) != 0 || (p.y % 4) != 0) {
		discard;
	}
	ALBEDO = color.rgb;
}
