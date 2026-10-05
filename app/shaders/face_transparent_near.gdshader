// Styles › Transparency quality Nicer, second pass: the near sides of see-through faces.
shader_type spatial;
render_mode unshaded, cull_back, blend_mix, depth_draw_never;
#include "res://shaders/face_transparent.gdshaderinc"
