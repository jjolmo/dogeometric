// Model Info › Rendering › Use anti-aliased textures: off samples the nearest texel, unfiltered.
uniform bool smooth_textures = true;
#define TEX(t, uv) (smooth_textures ? texture(t, uv).rgb : texelFetch(t, clamp(ivec2(floor(fract(uv) * vec2(textureSize(t, 0)))), ivec2(0), textureSize(t, 0) - 1), 0).rgb)
