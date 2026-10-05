// Transparent variant of face.gdshader. SketchUp-style face shading: flat colour per side, lit by a light fixed relative to the camera.
// Godot treats clockwise triangles as front faces; ModelRenderer emits model fronts that way.
shader_type spatial;
render_mode unshaded, cull_disabled, blend_mix, depth_draw_never;
#include "res://shaders/section.gdshaderinc"
#include "res://shaders/texture_sample.gdshaderinc"
// How far this fades while another group is open for editing (Model Info › Components), as in SketchUp.
instance uniform float fade = 0.0;

uniform vec4 front_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 back_color : source_color = vec4(0.643, 0.698, 0.733, 1.0);
// View-space direction towards the light: from over the viewer's left shoulder.
uniform vec3 light_dir = vec3(-0.35, 0.45, 0.82);
// Textured materials (Shaded With Textures): each side's picture, tiled along UV (front) / UV2 (back). A mirrored
// instance sees its fronts as backs, so the sides' coordinates swap too.
uniform sampler2D front_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D back_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform bool has_front_tex = false;
uniform bool has_back_tex = false;
uniform bool swap_uv = false;

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
		c = vec4(TEX(front_tex, front_uv), c.a);
	} else if (!FRONT_FACING && has_back_tex) {
		c = vec4(TEX(back_tex, back_uv), c.a);
	}
	float diffuse = length(light_dir) > 0.0 ? abs(dot(normalize(NORMAL), normalize(light_dir))) : 1.0;
	ALBEDO = c.rgb * (length(light_dir) > 0.0 ? 0.58 + 0.42 * diffuse : 1.0);
	ALPHA = c.a;
	ALBEDO = mix(ALBEDO, vec3(1.0), fade);
}
