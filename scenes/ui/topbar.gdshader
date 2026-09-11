shader_type canvas_item;
render_mode unshaded;

// Фон верхней панели. Плоская заливка выглядит наклейкой поверх карты, поэтому здесь
// вертикальный градиент, светлая кромка сверху, тёмная снизу и едва заметное зерно —
// без него на широком градиенте видны полосы.

uniform vec4 top_color : source_color = vec4(0.075, 0.129, 0.157, 0.96);
uniform vec4 bottom_color : source_color = vec4(0.043, 0.078, 0.098, 0.98);
uniform vec4 edge_color : source_color = vec4(0.20, 0.34, 0.40, 1.0);

uniform float edge_px = 1.0;
uniform float glow = 0.05;
uniform float grain = 0.012;

float hash(vec2 p) {
	p = fract(p * vec2(127.31, 311.7));
	p += dot(p, p + 34.53);
	return fract(p.x * p.y);
}

void fragment() {
	vec2 size = 1.0 / TEXTURE_PIXEL_SIZE;
	float y = UV.y;

	vec4 col = mix(top_color, bottom_color, smoothstep(0.0, 1.0, y));

	// Мягкий подсвет к середине: панель читается как выгнутая пластина.
	col.rgb += glow * (1.0 - abs(y - 0.42) * 2.0);

	float px_y = y * size.y;
	float lip = 1.0 - smoothstep(0.0, edge_px, px_y);
	float foot = smoothstep(size.y - edge_px * 2.0, size.y, px_y);

	col.rgb = mix(col.rgb, edge_color.rgb, lip * 0.55);
	col.rgb = mix(col.rgb, vec3(0.0), foot * 0.5);

	col.rgb += (hash(UV * size) - 0.5) * grain;

	COLOR = col;
}
