shader_type spatial;
render_mode unshaded, fog_disabled, depth_draw_never, cull_disabled;

uniform int pixel_size : hint_range(1, 16) = 4;
uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;

void vertex() {
    POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}

void fragment() {
    float size = max(1.0, float(pixel_size));
    vec2 remainder = mod(FRAGCOORD.xy, vec2(size));
    vec2 sample_coord = FRAGCOORD.xy + floor(size * 0.5) - remainder;

    ALBEDO = texture(screen_texture, sample_coord / VIEWPORT_SIZE).rgb;
    ALPHA = 1.0;
}
