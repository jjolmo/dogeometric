// Face shading by the sun (Shadows on, or "Use sun for shading"): ambient from Dark, sunlight from Light, cut by
// shadows when the faces receive them. Otherwise as face.gdshader.
shader_type spatial;
render_mode cull_disabled, depth_draw_opaque, specular_disabled, ambient_light_disabled;
#include "res://shaders/section.gdshaderinc"
instance uniform float fade = 0.0;

uniform vec4 front_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 back_color : source_color = vec4(0.643, 0.698, 0.733, 1.0);
uniform sampler2D front_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D back_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform bool has_front_tex = false;
uniform bool has_back_tex = false;
uniform bool swap_uv = false;
uniform float sun_light = 0.8;
uniform float sun_dark = 0.45;
uniform bool receive_shadows = true;

void vertex() {
	SECTION_TRACK;
}

void fragment() {
	if (SECTION_CUT) {
		discard;
	}
	vec4 c = FRONT_FACING ? front_color : back_color;
	vec2 front_uv = swap_uv ? UV2 : UV;
	vec2 back_uv = swap_uv ? UV : UV2;
	if (FRONT_FACING && has_front_tex) {
		c = vec4(texture(front_tex, front_uv).rgb, c.a);
	} else if (!FRONT_FACING && has_back_tex) {
		c = vec4(texture(back_tex, back_uv).rgb, c.a);
	}
	ALBEDO = c.rgb * (1.0 - fade * 0.6);
	EMISSION = c.rgb * sun_dark * (1.0 - fade * 0.6) + vec3(fade * 0.6);
}

void light() {
	float lit = max(dot(NORMAL, LIGHT), 0.0) * (receive_shadows ? ATTENUATION : 1.0);
	DIFFUSE_LIGHT += vec3(sun_light * lit);
}
