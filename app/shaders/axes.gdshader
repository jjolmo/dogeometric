// Drawing axes: solid on the positive side, dotted on the negative side (UV.x < 0 is the distance
// from the origin on the negative half). Dashes keep a roughly constant on-screen size.
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled;

uniform float dashes_per_radian = 220.0;
// Parallel views: the depth at which a perspective view would show the same scale (set by ModelViewport).
uniform float parallel_depth = 1.0;

vec3 srgb_to_linear(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c));
}

void fragment() {
	if (UV.x < 0.0) {
		// Perspective: on-screen size falls with depth; parallel: it is fixed by the view's scale.
		float depth = PROJECTION_MATRIX[3][3] == 0.0 ? max(-VERTEX.z, 1e-4) : parallel_depth;
		float phase = fract(-UV.x / depth * dashes_per_radian);
		if (phase > 0.5) {
			discard;
		}
	}
	ALBEDO = srgb_to_linear(COLOR.rgb);
}
