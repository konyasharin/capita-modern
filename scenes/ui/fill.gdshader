shader_type canvas_item;
render_mode unshaded;

// Заливка полосок и плашек. Ровный цвет выглядит как кусок пластика, поэтому здесь
// поперечный градиент, косые полосы и мелкое зерно. Цвет задаётся снаружи.

uniform vec4 tint : source_color = vec4(0.5, 0.7, 0.5, 1.0);
uniform float streaks = 0.05;

float grain(vec2 at) {
	return fract(sin(dot(at, vec2(12.9898, 78.233))) * 43758.5453);
}

void fragment() {
	// Светлее сверху, темнее снизу: полоса становится выпуклой.
	float lift = mix(1.22, 0.72, UV.y);

	// Косые полосы намекают на движение, но остаются на грани заметности.
	float bands = sin((UV.x * 26.0) + UV.y * 6.0) * streaks;

	vec3 col = tint.rgb * (lift + bands);
	col += (grain(FRAGCOORD.xy) - 0.5) * 0.035;

	COLOR = vec4(col, tint.a);
}
