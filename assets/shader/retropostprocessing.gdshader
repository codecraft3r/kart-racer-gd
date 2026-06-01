shader_type spatial;
// unshaded prevents 3D lighting engines from overriding your retro colors
// fog_disabled keeps the screen quad from turning into environmental fog
render_mode unshaded, fog_disabled;

uniform float pixel_scale : hint_range(0.01, 1.0) = 0.35;
uniform float color_res : hint_range(1.0, 64.0) = 32.0;
uniform float dither_spread : hint_range(0.0, 1.0) = 0.1;
uniform float dither_size : hint_range(1.0, 10.0) = 0.5;

uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;

// Flattened 4x4 matrix mapping for optimized GPU execution without branching
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
    // Ensures the quad fills the screen perfectly in a 3D setup
    POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}

void fragment() {
    // 1. Calculate resolution variables
    vec2 screen_res = vec2(512.0,448.0);
    vec2 low_res = max(vec2(1.0), screen_res * pixel_scale);

    // 2. Snap UVs to create pixelation
    vec2 snapped_uv = floor(SCREEN_UV * low_res) / low_res;

    // 3. Sample screen color 
    vec4 color = texture(screen_texture, snapped_uv);
    
// 4. Calculate clean dither coordinates
    // We multiply the snapped macro-pixels directly by our dither size factor.
    // Operating strictly on low_res ensures the pattern locks to our pixel grid.
    vec2 dither_uv = floor(SCREEN_UV * low_res) / dither_size;

    // 5. Apply optimized dither pattern
    float bayer = get_bayer_optimized(dither_uv) - 0.5;
    color.rgb += bayer * dither_spread;

    // 6. Quantize colors & assign to 3D pipeline output
    color.rgb = floor(color.rgb * color_res) / color_res;

    ALBEDO = color.rgb;
}