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

// Tags › Dashes: UV.x is the tag's line style, CUSTOM0 the edge's start, where the pattern begins.
varying float line_style;
varying vec3 clip_xyw;
varying vec2 start_px;

// On and off lengths in pixels of each line style (see LineStyles.Pattern).
bool dash_on(int style, float d) {
	float seg[10];
	int n = 0;
	float gap = style >= 10 ? 10.0 : 6.0;
	float first = style == 1 ? 6.0 : style == 3 ? 1.0 : style >= 10 ? 36.0 : 12.0;
	seg[n++] = first;
	if (style >= 7 && style <= 9) {
		seg[n++] = 12.0;
	}
	int dots = style == 4 || style == 7 ? 1 : style == 5 || style == 8 ? 2 : style == 6 || style == 9 ? 3 : 0;
	for (int i = 0; i < dots; i++) {
		seg[n++] = 1.0;
	}
	int dashes = style == 10 ? 1 : style == 11 ? 2 : 0;
	for (int i = 0; i < dashes; i++) {
		seg[n++] = 12.0;
	}
	float period = 0.0;
	for (int i = 0; i < n; i++) {
		period += seg[i] + gap;
	}
	float t = mod(d, period);
	for (int i = 0; i < n; i++) {
		if (t < seg[i]) {
			return true;
		}
		t -= seg[i] + gap;
		if (t < 0.0) {
			return false;
		}
	}
	return false;
}

void vertex() {
	SECTION_TRACK;
	POSITION = PROJECTION_MATRIX * MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
	line_style = UV.x;
	clip_xyw = vec3(POSITION.xy, POSITION.w);
	vec4 s = PROJECTION_MATRIX * MODELVIEW_MATRIX * vec4(CUSTOM0.xyz, 1.0);
	start_px = s.xy / s.w * VIEWPORT_SIZE * 0.5;
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
	if (line_style > 0.5) {
		vec2 px = clip_xyw.xy / clip_xyw.z * VIEWPORT_SIZE * 0.5;
		if (!dash_on(int(line_style + 0.5), length(px - start_px))) {
			discard;
		}
	}
	ALBEDO = vertex_colors ? COLOR.rgb : color.rgb;
	ALBEDO = mix(ALBEDO, vec3(0.72), fade);
}
