shader_type canvas_item;
render_mode unshaded;

// Значок товара: не плоская заливка одним цветом, а градиент по высоте с тёмной
// обводкой. Значки у нас силуэтные, в один цвет, и без этого они выглядят наклейкой.

uniform vec4 top : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 bottom : source_color = vec4(0.6, 0.6, 0.6, 1.0);
uniform vec4 edge : source_color = vec4(0.0, 0.0, 0.0, 0.75);

// Толщина обводки в долях значка: пиксели картинки тут не годятся, она вчетверо крупнее.
uniform float rim = 0.055;

void fragment() {
	float own = texture(TEXTURE, UV).a;

	float around = 0.0;
	for (int step = 0; step < 8; step++) {
		float angle = float(step) * 0.7854;
		around = max(around, texture(TEXTURE, UV + vec2(cos(angle), sin(angle)) * rim).a);
	}

	vec3 body = mix(top.rgb, bottom.rgb, smoothstep(0.1, 0.95, UV.y));
	vec3 col = mix(edge.rgb, body, own);

	COLOR = vec4(col, max(own, around * edge.a));
}
