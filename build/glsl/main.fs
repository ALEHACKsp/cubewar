#version 440 core

// layout (binding=0) uniform sampler2DArray texture_array;

in float sh_depth;
in vec3 sh_world_position;
in vec3 sh_world_normal;

layout (location=0) out vec3 out_deferred_surface;
layout (location=1) out vec4 out_deferred_position;
layout (location=2) out vec4 out_deferred_material;

in geometry_data {
	vec3 normal;
	vec3 texture_coords;
} geometry;

void main() {
	out_deferred_surface = sh_world_normal;
	out_deferred_position = vec4(sh_world_position, sh_depth);
	out_deferred_material = vec4(geometry.texture_coords, 1);
}