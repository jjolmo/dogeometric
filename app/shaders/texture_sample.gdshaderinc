// Model Info › Rendering › Use anti-aliased textures: off samples the nearest texel, unfiltered.
uniform bool smooth_textures = true;
#define TEX(t, uv) (smooth_textures ? texture(t, uv).rgb : texelFetch(t, clamp(ivec2(floor(fract(uv) * vec2(textureSize(t, 0)))), ivec2(0), textureSize(t, 0) - 1), 0).rgb)

// Materials › Colorize: the picture with the material colour's hue and saturation, keeping its own lightness (HSL).
uniform bool front_colorize = false;
uniform bool back_colorize = false;

vec3 hsl_of(vec3 c) {
	float hi = max(c.r, max(c.g, c.b));
	float lo = min(c.r, min(c.g, c.b));
	float l = (hi + lo) * 0.5;
	float d = hi - lo;
	if (d < 1e-5) {
		return vec3(0.0, 0.0, l);
	}
	float s = l > 0.5 ? d / (2.0 - hi - lo) : d / (hi + lo);
	float h = hi == c.r ? (c.g - c.b) / d + (c.g < c.b ? 6.0 : 0.0) : hi == c.g ? (c.b - c.r) / d + 2.0 : (c.r - c.g) / d + 4.0;
	return vec3(h / 6.0, s, l);
}

vec3 rgb_of(vec3 hsl) {
	vec3 k = mod(vec3(0.0, 8.0, 4.0) + hsl.x * 12.0, 12.0);
	float a = hsl.y * min(hsl.z, 1.0 - hsl.z);
	return hsl.z - a * max(min(min(k - 3.0, 9.0 - k), 1.0), -1.0);
}

vec3 colorized(vec3 picture, vec3 tint) {
	vec3 t = hsl_of(tint);
	return rgb_of(vec3(t.x, t.y, hsl_of(picture).z));
}
