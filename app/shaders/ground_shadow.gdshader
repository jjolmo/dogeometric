// Shadows on the ground: invisible except where the model's shadow falls.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, specular_disabled, ambient_light_disabled;

uniform float strength = 0.35;

void fragment() {
	ALBEDO = vec3(0.0);
	ALPHA = 0.0;
}

void light() {
	ALPHA = (1.0 - ATTENUATION) * strength;
}
