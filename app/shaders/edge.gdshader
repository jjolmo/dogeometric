// Model edges: unshaded lines pulled slightly towards the camera so they win the depth test against the faces
// they bound, as SketchUp draws edges on top of coplanar faces.
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled;

uniform vec4 color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
// Fraction of clip-space w; reverse-Z means larger z is closer.
uniform float depth_bias = 0.00004;

void vertex() {
	POSITION = PROJECTION_MATRIX * MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
	POSITION.z += depth_bias * POSITION.w;
}

void fragment() {
	ALBEDO = color.rgb;
}
