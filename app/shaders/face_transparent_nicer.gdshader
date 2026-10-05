// Styles › Transparency quality Nicer, first pass: the far sides of see-through faces, so the near sides blend over them.
shader_type spatial;
render_mode unshaded, cull_front, blend_mix, depth_draw_never;
#include "res://shaders/face_transparent.gdshaderinc"
