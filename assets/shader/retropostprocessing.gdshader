shader_type spatial;
render_mode unshaded, fog_disabled, depth_draw_never, cull_disabled;

uniform int pixel_size : hint_range(1, 16) = 2;
uniform float color_steps : hint_range(4.0, 48.0) = 28.0;
uniform float dither_strength : hint_range(0.0, 1.0) = 0.18;
uniform float contrast : hint_range(0.75, 1.5) = 1.03;
uniform float saturation : hint_range(0.0, 2.0) = 1.08;
uniform vec3 shadow_tint : source_color = vec3(0.22, 0.14, 0.42);
uniform float shadow_tint_strength : hint_range(0.0, 0.6) = 0.10;
uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;

float bayer4(vec2 position) {
    ivec2 p = ivec2(mod(floor(position), 4.0));
    int index = p.y * 4 + p.x;
    float matrix[16] = float[](
        0.0,  8.0,  2.0, 10.0,
        12.0, 4.0, 14.0, 6.0,
        3.0, 11.0, 1.0, 9.0,
        15.0, 7.0, 13.0, 5.0
    );
    return matrix[index] / 16.0;
}

void vertex() {
    POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}

void fragment() {
    float size = max(1.0, float(pixel_size));
    vec2 remainder = mod(FRAGCOORD.xy, vec2(size));
    vec2 sample_coord = FRAGCOORD.xy + floor(size * 0.5) - remainder;

    vec3 color = texture(screen_texture, sample_coord / VIEWPORT_SIZE).rgb;
    float luma = dot(color, vec3(0.2126, 0.7152, 0.0722));

    color = mix(vec3(luma), color, saturation);
    color = (color - vec3(0.5)) * contrast + vec3(0.5);

    float shadow_amount = 1.0 - smoothstep(0.08, 0.58, luma);
    vec3 tinted_shadow = color * mix(vec3(1.0), shadow_tint * 2.25, shadow_tint_strength);
    color = mix(color, tinted_shadow, shadow_amount);

    float steps = max(4.0, color_steps);
    float dither = (bayer4(sample_coord / size) - 0.5) * dither_strength;
    color = floor(clamp(color, vec3(0.0), vec3(1.0)) * steps + dither) / steps;

    ALBEDO = clamp(color, vec3(0.0), vec3(1.0));
    ALPHA = 1.0;
}
