shader_type spatial;
render_mode unshaded, fog_disabled;

uniform float pixel_scale : hint_range(0.01, 1.0) = 0.15;
uniform float color_res : hint_range(1.0, 64.0) = 16.0;
uniform float dither_spread : hint_range(0.0, 1.0) = 0.05;
uniform float dither_size : hint_range(1.0, 10.0) = 1.0;
uniform float target_height : hint_range(256, 1080) = 320;

uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;

float get_bayer_optimized(vec2 coord) {
    ivec2 p = ivec2(mod(coord, 4.0));
    int index = p.y * 4 + p.x;
    
    float m[16] = float[](
        0.0,  8.0,  2.0,  10.0,
        12.0, 4.0,  14.0, 6.0,
        3.0,  11.0, 1.0,  9.0,
        15.0, 7.0,  13.0, 5.0
    );
    
    return m[index] / 16.0;
}

void vertex() {
    POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}

void fragment() {
    // 1. Get clean base screen pixel resolution
	float aspect = VIEWPORT_SIZE.x / VIEWPORT_SIZE.y;
    vec2 screen_res = vec2(target_height * aspect, target_height);
	
    vec2 low_res = max(vec2(1.0), screen_res * pixel_scale);

    // 2. Snap UVs to create pixelation macro-pixels
    vec2 snapped_uv = floor(SCREEN_UV * low_res) / low_res;

    // 3. Sample screen color 
    vec4 color = texture(screen_texture, snapped_uv);
    
    // 4. Calculate dither coordinates based strictly on integer screen pixel space
    // This stops screen scaling/UV stretching bugs between editor and player
    vec2 pixel_coord = floor(SCREEN_UV * screen_res);
    vec2 dither_coord = pixel_coord / max(1.0, dither_size);

    // 5. Apply optimized dither pattern
    float bayer = get_bayer_optimized(dither_coord) - 0.5;
    color.rgb += bayer * dither_spread;

    // 6. Quantize colors & assign output
    color.rgb = floor(color.rgb * color_res) / color_res;

    ALBEDO = color.rgb;
}