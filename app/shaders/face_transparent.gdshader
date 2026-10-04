// Transparent variant of face.gdshader. SketchUp-style face shading: flat colour per side, lit by a light fixed relative to the camera.
// Godot treats clockwise triangles as front faces; ModelRenderer emits model fronts that way.
shader_type spatial;
render_mode unshaded, cull_disabled, blend_mix, depth_draw_never;

uniform vec4 front_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 back_color : source_color = vec4(0.643, 0.698, 0.733, 1.0);
// View-space direction towards the light: from over the viewer's left shoulder.
uniform vec3 light_dir = vec3(-0.35, 0.45, 0.82);

void fragment() {
	vec4 c = FRONT_FACING ? front_color : back_color;
	float diffuse = abs(dot(normalize(NORMAL), normalize(light_dir)));
	ALBEDO = c.rgb * (0.58 + 0.42 * diffuse);
	ALPHA = c.a;
}
