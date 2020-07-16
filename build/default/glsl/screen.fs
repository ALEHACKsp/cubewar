#version 440 core

layout (binding=0) uniform sampler2D deferred_surface_buffer;
layout (binding=1) uniform sampler2D deferred_position_buffer;
layout (binding=2) uniform sampler2D deferred_material_buffer;
layout (binding=3) uniform sampler2DArray voxel_array;
layout (binding=4) uniform sampler2DArray x256_array;
layout (binding=5) uniform sampler2DArray x512_array;
layout (binding=6) uniform sampler2D sun_shadow_map;

uniform float pixel_w;
uniform float pixel_h;
uniform float saturation_power;
uniform float gamma_power;
uniform float near_plane;
uniform float far_plane;
uniform mat4 sun_shadow_matrix;

in vec2 sh_uv;
out vec4 final_color;

{{{ MATERIAL RESOLVER CODE}}}

float get_depth(vec2 uv) {
	vec4 material_coords = texture2D(deferred_material_buffer, uv);
	if (material_coords.a == 0) return far_plane;
	return texture2D(deferred_position_buffer, uv).a;
}

vec3 uncharted_tone(vec3 color) {
	float gamma = 0.45;
	float A = 0.15;
	float B = 0.50;
	float C = 0.10;
	float D = 0.20;
	float E = 0.02;
	float F = 0.30;
	float W = 11.2;
	float exposure = 10;
	color *= exposure;
	color = ((color * (A * color + C * B) + D * E) / (color * (A * color + B) + D * F)) - E / F;
	float white = ((W * (A * W + C * B) + D * E) / (W * (A * W + B) + D * F)) - E / F;
	color /= white;
	color = pow(color, vec3(1. / gamma));
	return color;
}

vec3 apply_gamma(vec3 color) {
	return pow(color.rgb, vec3(1.0 / gamma_power));
}

vec3 get_saturated(vec3 color, float outline_d) {
    return mix(vec3(dot(color, vec3(0.2125, 0.7154, 0.0721))), color, outline_d);
}

vec2 promo_outline_offset[4] = vec2[4](
	vec2(-1, 1),
	vec2(0, 1),
	vec2(1, 1),
	vec2(1, 0)
);

vec3 promo_outline(vec3 color) {
	float outline_d = 1.0;
	float z = get_depth(sh_uv);
	float total_z = 0.0;
	float max_z = 0;
	float sample_z1 = 0.0;
	float sample_z2 = 0.0;
	for (int i = 0; i < 4; i++){
		sample_z1 = get_depth(sh_uv.xy + vec2(pixel_w, pixel_h) * promo_outline_offset[i]);
		max_z = max(sample_z1, max_z);
		sample_z2 = get_depth(sh_uv.xy - vec2(pixel_w, pixel_h) * promo_outline_offset[i]);
		max_z = max(sample_z2, max_z);
		outline_d *= clamp(1.0 - ((sample_z1 + sample_z2) - z * 2.0) * 32.0 / z, 0.0, 1.0);
		total_z += sample_z1 + sample_z2;
	}
	float outline_a = 1.0 - clamp((z * 8.0 - total_z) * 64.0 / z, 0.0, 1.0) * clamp(1.0 - ((z * 8.0 - total_z) * 32.0 - 1.0) / z, 0.0, 1.0);
	float outline_b = clamp(1.0 + 8.0 * (z - max_z) / z, 0.0, 1.0);
	float outline_c = clamp(1.0 + 64.0 * (z - max_z) / z, 0.0, 1.0);
	float outline = (0.35 * (outline_a * outline_b) + 0.65) * (0.75 * (1.0 - outline_d) * outline_c + 1.0);
	color = sqrt(sqrt(color));
	color *= outline;
	color *= color;
	color *= color;
	return color;
}

// ---

// Desaturate function:
vec4 desaturate(vec4 iColor, float iExtent) {
	vec3 tCoeff = vec3( 0.3, 0.59, 0.11 );
	vec3 tGray  = vec3( dot( tCoeff, iColor.rgb ) );
	return vec4( mix( iColor.rgb, tGray, iExtent ), iColor.a );
}

// Contrast, saturation and brightness adjustment function:
vec4 contrastSaturationBrightness(vec4 iColor, float iBrightness, float iSaturation, float iContrast) {
	const vec3 tLumAvg   = vec3( 0.5, 0.5, 0.5 );
	const vec3 tLumCoeff = vec3( 0.2125, 0.7154, 0.0721 );
	vec3 tBriClr = iColor.rgb * iBrightness;
	vec3 tIntens = vec3( dot( tBriClr, tLumCoeff ) );
	vec3 tSatClr = mix( tIntens, tBriClr, iSaturation );
	vec3 tConClr = mix( tLumAvg, tSatClr, iContrast );
	return vec4( tConClr, iColor.a );
}

// RGB to HSL colorspace conversion function:
vec3 rgbToHsl(vec3 iColor) {
	vec3  tHsl   = vec3( 0.0 );
	float tMin   = min( min( iColor.r, iColor.g), iColor.b );
	float tMax   = max( max( iColor.r, iColor.g), iColor.b );
	float tDelta = tMax - tMin;
	tHsl.z       = (tMax + tMin) / 2.0;
	if( tDelta != 0.0 ) {
		if( tHsl.z < 0.5 ) { tHsl.y = tDelta / (tMax + tMin); }
		else               { tHsl.y = tDelta / (2.0 - tMax - tMin); }
		float tDeltaR = (((tMax - iColor.r) / 6.0) + (tDelta / 2.0)) / tDelta;
		float tDeltaG = (((tMax - iColor.g) / 6.0) + (tDelta / 2.0)) / tDelta;
		float tDeltaB = (((tMax - iColor.b) / 6.0) + (tDelta / 2.0)) / tDelta;
		if( iColor.r == tMax )      { tHsl.x = tDeltaB - tDeltaG; }
		else if( iColor.g == tMax ) { tHsl.x = (1.0 / 3.0) + tDeltaR - tDeltaB; }
		else if( iColor.b == tMax ) { tHsl.x = (2.0 / 3.0) + tDeltaG - tDeltaR; }
		if( tHsl.x < 0.0 )      { tHsl.x += 1.0; } 
		else if( tHsl.x > 1.0 ) { tHsl.x -= 1.0; }
	}
	return tHsl;
}

// Hue to RGB colorspace conversion helper function:
float hueToRgb(float iF1, float iF2, float iHue) {
	if( iHue < 0.0 )      { iHue += 1.0; }
	else if( iHue > 1.0 ) { iHue -= 1.0; }
	float tRes;
	if( (6.0 * iHue) < 1.0 )     { tRes = iF1 + (iF2 - iF1) * 6.0 * iHue; }
	else if ((2.0 * iHue) < 1.0) { tRes = iF2; }
	else if ((3.0 * iHue) < 2.0) { tRes = iF1 + (iF2 - iF1) * ((2.0 / 3.0) - iHue) * 6.0; }
	else                         { tRes = iF1; }
	return tRes;
}

// HSL to RGB colorspace conversion function:
vec3 hslToRgb(vec3 iHsl) {
	vec3 tRgb;
	if( iHsl.y == 0.0 ) {
		tRgb = vec3( iHsl.z ); 
	}
	else {
		float f2;
		if( iHsl.z < 0.5 ) { f2 = iHsl.z * (1.0 + iHsl.y); }
		else               { f2 = (iHsl.z + iHsl.y) - (iHsl.y * iHsl.z); }
		float f1 = 2.0 * iHsl.z - f2;
		tRgb.r = hueToRgb( f1, f2, iHsl.x + (1.0/3.0) );
		tRgb.g = hueToRgb( f1, f2, iHsl.x);
		tRgb.b = hueToRgb( f1, f2, iHsl.x - (1.0/3.0) );
	}
	return tRgb;
}

// Combines luminance and saturation of iBaseColor with hue of iBlendColor:
vec3 blendHue(vec3 iBaseColor, vec3 iBlendColor) {
	vec3 tBase = rgbToHsl( iBaseColor );
	return hslToRgb( vec3( rgbToHsl(iBlendColor).r, tBase.g, tBase.b ) );
}

// Combines luminance and hue of iBaseColor with the saturation of iBlendColor:
vec3 blendSaturation(vec3 iBaseColor, vec3 iBlendColor) {
	vec3 tBase = rgbToHsl( iBaseColor );
	return hslToRgb( vec3( tBase.r, rgbToHsl(iBlendColor).g, tBase.b ) );
}

// Combines hue and saturation of iBaseColor with the luminance of iBlendColor:
vec3 blendLuminosity(vec3 iBaseColor, vec3 iBlendColor) {
	vec3 tBase = rgbToHsl( iBaseColor );
	return hslToRgb( vec3( tBase.r, tBase.g, rgbToHsl(iBlendColor).b ) );
}

// Uses brightness of iBaseColor with the hue and saturation of iBlendColor:
vec3 blendColor(vec3 iBaseColor, vec3 iBlendColor) {
	vec3 tBlend = rgbToHsl( iBlendColor );
	return hslToRgb( vec3( tBlend.r, tBlend.g, rgbToHsl(iBaseColor).b ) );
}

// ---

vec3 get_diffuse(vec2 uv) {
	vec4 material_coords = texture2D(deferred_material_buffer, uv);
	if (material_coords.a == 0) return vec3(153.0 * (1.0 / 255.0), 217.0 * (1.0 / 255.0), 234.0 * (1.0 / 255.0)); // sky
	vec3 normal = texture2D(deferred_surface_buffer, uv).rgb;
	float light_dot = max(dot(normal, normalize(vec3(0.25, 0.25, 1))), 0);
	float light_power = (0.7 + (light_dot * 1.2));
	vec3 world_position = texture2D(deferred_position_buffer, uv).rgb;
	vec3 shadow_map_uv = vec4(sun_shadow_matrix * vec4(world_position, 1)).rgb;
	float world_sun_depth = (shadow_map_uv.z + 1.0) * 0.5;
	shadow_map_uv += 1.0;
	shadow_map_uv *= 0.5;
	float shadow_map_sample = texture2D(sun_shadow_map, shadow_map_uv.xy).r;
	float shadow_map_sample_2 = texture2D(sun_shadow_map, shadow_map_uv.xy + vec2(1.0 / 2048.0, 0)).r;
	float shadow_map_sample_3 = texture2D(sun_shadow_map, shadow_map_uv.xy - vec2(1.0 / 2048.0, 0)).r;
	if (shadow_map_uv.x >= 0.0 && shadow_map_uv.x <= 1.0 && shadow_map_uv.y >= 0.0 && shadow_map_uv.y <= 1.0) {
		if (world_sun_depth - 0.002 >= shadow_map_sample) light_power = 0.7;
	}
	if (material_coords.a == 80000) {
		return contrastSaturationBrightness(texture(voxel_array, material_coords.rgb), light_power, 1.0, 1.0).rgb;
	} else return contrastSaturationBrightness(vec4(resolve_material_diffuse(material_coords.a), 1), light_power, 1.0, 1.0).rgb;
}

vec3 get_sharpened(vec3 color, vec2 coords) {
  vec3 sum = vec3(0.0);
  sum += -1.0 * get_diffuse(coords + vec2(-pixel_w , 0.0));
  sum += -1.0 * get_diffuse(coords + vec2(0, -pixel_h));
  sum += 5.0 * color;
  sum += -1.0 * get_diffuse(coords + vec2(0, pixel_h));
  sum += -1.0 * get_diffuse(coords + vec2(pixel_w , 0));
  return sum;
}

void main() {
	final_color = vec4(get_diffuse(sh_uv).rgb, 1);
	final_color = vec4((final_color.rgb * 0.8) + (get_sharpened(final_color.rgb, sh_uv) * 0.2), 1);
	final_color = vec4(uncharted_tone(final_color.rgb), 1);
	// final_color = vec4(promo_outline(final_color.rgb), final_color.a);
	// final_color = vec4((final_color.rgb * 0.5) + (vec4(get_sharpened(final_color.rgb, sh_uv), 1).rgb * 0.5), 1);
	// final_color = vec4(uncharted_tone(final_color.rgb), 1);
}