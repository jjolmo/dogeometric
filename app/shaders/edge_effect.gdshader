// Styles › Edge effects drawn on profile.gdshader's quads (hard edges only): Endpoints thicken each end of an edge
// over a few pixels, Jitter redraws the edge as a loose stroke whose ends wander by a seeded amount.
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled;
#include "res://shaders/section.gdshaderinc"
instance uniform float fade = 0.0;

uniform vec4 color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
// Which end this pass thickens (0 the start, 1 the end), or which of the jitter strokes it is.
uniform float pass = 0.0;
// 0 endpoints, 1 jitter.
uniform int effect = 0;
uniform float endpoint_px = 9.0;
uniform float endpoint_width = 3.0;
uniform float jitter_px = 3.0;
uniform float depth_bias = 0.0006;

float hash(vec3 p) {
	return fract(sin(dot(p, vec3(12.9898, 78.233, 37.719)) + pass * 17.17) * 43758.5453) * 2.0 - 1.0;
}

void vertex() {
	SECTION_TRACK;
	vec3 other = CUSTOM0.xyz;
	bool is_end = CUSTOM3.w > 0.5;
	vec4 p = PROJECTION_MATRIX * MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
	vec4 q = PROJECTION_MATRIX * MODELVIEW_MATRIX * vec4(other, 1.0);
	vec2 half_size = VIEWPORT_SIZE * 0.5;
	vec2 sp = p.xy / p.w * half_size;
	vec2 sq = q.xy / q.w * half_size;
	float len = length(sq - sp);
	vec2 dir = len > 1e-4 ? (sq - sp) / len : vec2(1.0, 0.0);
	vec2 side = vec2(-dir.y, dir.x) * CUSTOM0.w;
	vec4 at = p;
	vec2 offset;
	bool skip = CUSTOM3.x > 0.5 || len < 1e-4;
	if (skip) {
		at = vec4(2.0, 2.0, 2.0, 1.0);
	} else if (effect == 0) {
		// The corner at the other end comes back to endpoint_px from the thickened end (screen-space distance u).
		if (is_end != (pass > 0.5)) {
			float u = min(endpoint_px, len) / len;
			float s = u * q.w / (u * q.w + (1.0 - u) * p.w);
			at = mix(q, p, s);
		}
		offset = side * endpoint_width * 0.5;
	} else {
		// Each end wanders the same way for both of its corners, so the stroke stays a straight quad.
		vec3 seed = VERTEX * 1000.0 + other * 10.0;
		offset = side * 0.5 + (vec2(hash(seed), hash(seed.yzx)) * jitter_px) - dir * jitter_px * abs(hash(seed.zxy));
	}
	if (!skip) {
		at.xy += offset / half_size * at.w;
		if (PROJECTION_MATRIX[3][3] == 0.0) {
			at.z *= 1.0 + depth_bias;
		} else {
			at.z += 5e-8;
		}
	}
	POSITION = at;
}

void fragment() {
	if (SECTION_CUT) {
		discard;
	}
	ALBEDO = mix(color.rgb, vec3(0.72), fade);
}
