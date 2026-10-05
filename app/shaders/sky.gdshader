// SketchUp's style background: a sky gradient from the horizon line up to the top edge of the view (screen space,
// like SketchUp) and flat ground below it, either of which can give way to the plain background colour.
shader_type sky;

uniform vec3 sky_top : source_color = vec3(0.184, 0.675, 0.878);
uniform vec3 sky_horizon : source_color = vec3(0.745, 0.898, 0.961);
uniform vec3 ground : source_color = vec3(0.749, 0.749, 0.776);
uniform vec3 background : source_color = vec3(1.0);
uniform bool sky_on = true;
uniform bool ground_on = true;
// 0 opaque ground, 1 none (the background shows through).
uniform float ground_transparency = 0.0;
// Below the ground, the ground is only drawn with Styles › Show ground from below.
uniform bool camera_below = false;
uniform bool ground_from_below = false;
// Horizon height on screen: 0 = top edge, 1 = bottom edge. Set by ModelViewport on every camera change.
uniform float horizon_uv = 0.3;

void sky() {
	if (EYEDIR.y >= 0.0) {
		float t = horizon_uv > 0.0 ? clamp((horizon_uv - SCREEN_UV.y) / horizon_uv, 0.0, 1.0) : 1.0;
		COLOR = sky_on ? mix(sky_horizon, sky_top, t) : background;
	} else {
		bool shown = ground_on && (!camera_below || ground_from_below);
		COLOR = shown ? mix(ground, background, ground_transparency) : background;
	}
}
