shader_type canvas_item;
render_mode unshaded;

// Круглый флаг с ободком. Флаги приходят прямоугольными и разных пропорций, поэтому
// круг вырезается здесь, а не в картинке: иначе для каждой страны понадобился бы
// свой обрезанный файл.

uniform vec4 ring_color : source_color = vec4(0.20, 0.34, 0.40, 1.0);
uniform float ring_px = 1.5;
uniform float shade = 0.16;

void fragment() {
	vec2 size = 1.0 / TEXTURE_PIXEL_SIZE;
	vec2 middle = UV - 0.5;
	float radius = length(middle) * 2.0;

	float px = 2.0 / max(size.x, size.y);
	float inside = 1.0 - smoothstep(1.0 - px, 1.0, radius);
	float ring = smoothstep(1.0 - ring_px * px - px, 1.0 - ring_px * px, radius) * inside;

	vec4 flag = texture(TEXTURE, UV);

	// Лёгкая тень снизу: кружок читается как значок, а не как дырка.
	flag.rgb *= 1.0 - shade * smoothstep(0.1, 1.0, UV.y) * radius;

	vec3 col = mix(flag.rgb, ring_color.rgb, ring);

	COLOR = vec4(col, inside * flag.a);
}
