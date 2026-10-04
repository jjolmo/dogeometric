// Silhouettes (border edges, or between a face towards the eye and one away): soft ones 1 px, all thick with Profiles.
// One quad per edge: CUSTOM0 other end and side, CUSTOM1/2 the faces' planes, CUSTOM3 soft and second-face flags.
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled;
#include "res://shaders/section.gdshaderinc"
instance uniform float fade = 0.0;

uniform vec4 color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform float profile_width = 0.0;
uniform float depth_bias = 0.0006;

void vertex() {
	SECTION_TRACK;
	vec3 other = CUSTOM0.xyz;
	bool perspective = PROJECTION_MATRIX[3][3] == 0.0;
	// Facing is judged against each face's plane, so every edge of a face agrees and silhouettes stay connected.
	mat4 to_model = inverse(MODEL_MATRIX);
	vec3 eye = (to_model * vec4(INV_VIEW_MATRIX[3].xyz, 1.0)).xyz;
	vec3 back = (to_model * vec4(INV_VIEW_MATRIX[2].xyz, 0.0)).xyz;
	float f1 = perspective ? dot(CUSTOM1.xyz, eye) - CUSTOM1.w : dot(CUSTOM1.xyz, back);
	float f2 = perspective ? dot(CUSTOM2.xyz, eye) - CUSTOM2.w : dot(CUSTOM2.xyz, back);
	bool silhouette = CUSTOM3.y < 0.5 || (f1 > 0.0) != (f2 > 0.0);
	float width = profile_width > 0.0 ? profile_width : (CUSTOM3.x > 0.5 ? 1.0 : 0.0);
	vec4 a = PROJECTION_MATRIX * MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
	vec4 b = PROJECTION_MATRIX * MODELVIEW_MATRIX * vec4(other, 1.0);
	if (!silhouette || width <= 0.0) {
		POSITION = vec4(2.0, 2.0, 2.0, 1.0);
	} else {
		vec2 half_size = VIEWPORT_SIZE * 0.5;
		vec2 sa = a.xy / a.w * half_size;
		vec2 sb = b.xy / b.w * half_size;
		vec2 d = sb - sa;
		vec2 dir = length(d) > 1e-4 ? normalize(d) : vec2(1.0, 0.0);
		// Out sideways by half the width and past the end by as much, so thick corners meet.
		vec2 offset = (vec2(-dir.y, dir.x) * CUSTOM0.w - dir) * width * 0.5;
		a.xy += offset / half_size * a.w;
		if (perspective) {
			a.z *= 1.0 + depth_bias;
		} else {
			a.z += 5e-8;
		}
		POSITION = a;
	}
}

void fragment() {
	if (SECTION_CUT) {
		discard;
	}
	ALBEDO = mix(color.rgb, vec3(0.72), fade * 0.6);
}
