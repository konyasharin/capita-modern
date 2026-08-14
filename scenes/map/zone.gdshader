shader_type canvas_item;
render_mode unshaded;

// Рамка промзоны: скруглённый квадрат, толщина линии постоянна в экранных пикселях.
// Один MultiMesh на все зоны — нод на зону не создаётся, см. docs/03-industry.md.

uniform vec4 line_color : source_color = vec4(0.184, 0.392, 0.533, 1.0);
uniform vec4 fill_color : source_color = vec4(1.0, 1.0, 1.0, 0.12);
uniform float thickness_px = 2.0;
uniform float corner = 0.16;

// Видимость по зуму: вдалеке рамки только зашумляют карту.
uniform float fade = 1.0;

void fragment() {
	vec2 q = abs(UV - 0.5) - (0.5 - corner);
	float d = length(max(q, vec2(0.0))) + min(max(q.x, q.y), 0.0) - corner;

	float w = max(fwidth(d), 1e-6) * thickness_px;
	float line = 1.0 - smoothstep(0.0, w, abs(d));
	float inside = 1.0 - smoothstep(-w, 0.0, d);

	vec3 col = mix(fill_color.rgb, line_color.rgb, line);
	float alpha = max(inside * fill_color.a, line * line_color.a) * fade;

	COLOR = vec4(col, alpha);
}
