shader_type canvas_item;
render_mode unshaded;

// Задник большого окна. Ровная заливка на весь экран выглядит как лист бумаги, поэтому
// здесь тёмная основа, медленные пятна, тонкая горизонтальная штриховка и виньетка.

uniform vec4 deep : source_color = vec4(0.012, 0.024, 0.031, 1.0);
uniform vec4 lift : source_color = vec4(0.106, 0.180, 0.204, 1.0);
uniform float veil = 1.0;

float noise(vec2 at) {
	return fract(sin(dot(at, vec2(12.9898, 78.233))) * 43758.5453);
}

float smoothed(vec2 at) {
	vec2 cell = floor(at);
	vec2 part = smoothstep(0.0, 1.0, fract(at));

	float a = noise(cell);
	float b = noise(cell + vec2(1.0, 0.0));
	float c = noise(cell + vec2(0.0, 1.0));
	float d = noise(cell + vec2(1.0, 1.0));

	return mix(mix(a, b, part.x), mix(c, d, part.x), part.y);
}

void fragment() {
	vec2 uv = UV;

	// Три слоя пятен разного размера — получается облачная, а не клетчатая муть.
	float blot = smoothed(uv * 3.0) * 0.55 + smoothed(uv * 9.0) * 0.3 + smoothed(uv * 24.0) * 0.15;

	vec3 col = mix(deep.rgb, lift.rgb, pow(blot, 1.6) * 1.6);

	// Штриховка через строку: еле заметная, но убирает ощущение пустой заливки.
	col *= 1.0 - 0.07 * step(0.5, fract(FRAGCOORD.y * 0.5));

	// Мягкий свет за средней колонкой: там смотрят чаще всего.
	float glow = 1.0 - smoothstep(0.0, 0.55, length((uv - vec2(0.5, 0.42)) * vec2(1.6, 1.0)));
	col += lift.rgb * glow * 0.35;

	// Виньетка сводит внимание к середине.
	float away = length((uv - 0.5) * vec2(1.15, 1.0));
	col *= 1.0 - 0.45 * smoothstep(0.25, 1.05, away);

	COLOR = vec4(col, veil);
}
