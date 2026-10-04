// Section plane cut, shared by the model shaders. section_plane is in Godot world space: xyz the direction of the
// plane's arrows (the view direction of the cut; what lies behind the arrows is removed), w the offset; a zero
// normal means no active section. Macros, because varyings may only be touched in vertex() and fragment().
global uniform vec4 section_plane;
varying vec3 section_world;

#define SECTION_TRACK section_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz
// A small tolerance keeps faces lying on the plane itself.
#define SECTION_CUT (dot(section_plane.xyz, section_world) < section_plane.w - 1e-6)
