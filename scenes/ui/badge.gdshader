shader_type canvas_item;
render_mode unshaded;

// Круглая плашка товара: выпуклый кружок в цвет товара с ободком. Цвет задаётся снаружи,
// поэтому одной плашки хватает на все тридцать два товара.

uniform vec4 tint : source_color = vec4(0.6, 0.3, 0.3, 1.0);
uniform float chosen = 0.0;
uniform float dim = 0.0;

void fragment() {
	vec2 middle = UV - 0.5;
	float radius = length(middle) * 2.0;
	float px = fwidth(radius);

	float inside = 1.0 - smoothstep(1.0 - px, 1.0, radius);
	float ring = smoothstep(0.80, 0.86, radius) * inside;

	// Свет сверху слева: без него кружок читается как наклейка, а не как значок.
	float light = 0.5 - middle.x * 0.7 - middle.y * 0.9;
	vec3 body = tint.rgb * mix(0.30, 1.15, clamp(light, 0.0, 1.0));

	// Внутренняя тень у края добавляет глубины.
	body *= 1.0 - 0.45 * smoothstep(0.55, 1.0, radius);

	// Блик сверху и зерно: без них кружок остаётся плоским пятном.
	float gloss = smoothstep(0.55, 0.0, length(middle - vec2(-0.14, -0.18)));
	body += tint.rgb * gloss * 0.35 + vec3(gloss * gloss * 0.10);
	body += (fract(sin(dot(FRAGCOORD.xy, vec2(12.9898, 78.233))) * 43758.5453) - 0.5) * 0.045;

	vec3 edge = mix(tint.rgb * 1.5, vec3(1.0), chosen * 0.6);
	vec3 col = mix(body, edge, ring * (0.55 + chosen * 0.45));

	col = mix(col, vec3(dot(col, vec3(0.33))) * 0.6, dim);

	COLOR = vec4(col, inside);
}
