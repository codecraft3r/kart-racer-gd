shader_type canvas_item;

uniform float color_res : hint_range(1.0, 64.0) = 24.0;
uniform float dither_spread : hint_range(0.0, 1.0) = 0.20;
uniform float dither_size : hint_range(1.0, 10.0) = 2.0;

// Explicit sampler breaks the infinite white feedback loop
uniform sampler2D viewport_texture : hint_default_black, filter_nearest;

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

void fragment() {
    // 1. Read the static texture frame from the target viewport
    vec4 color = texture(viewport_texture, UV);
    
    // 2. Calculate dither coordinates via pixel steps
    vec2 pixel_coord = floor(FRAGCOORD.xy);
    vec2 dither_coord = pixel_coord / max(1.0, dither_size);

    // 3. Apply dither pattern
    float bayer = get_bayer_optimized(dither_coord) - 0.5;
    color.rgb += bayer * dither_spread;

    // 4. Quantize colors
    color.rgb = floor(color.rgb * color_res) / color_res;

    COLOR = color;
}