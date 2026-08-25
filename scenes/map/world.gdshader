shader_type canvas_item;
render_mode unshaded;

// Политическая карта из поля владения. Границы не хранятся геометрией: id владельца
// нельзя фильтровать (среднее между 3 и 7 — не 5), поэтому из дискретной сетки
// строятся непрерывные маски принадлежности, а берег и границы берутся как их
// изолинии уровня 0.5. См. docs/01-map.md.

uniform sampler2D owner_tex : filter_nearest, repeat_disable;
uniform sampler2D region_tex : filter_nearest, repeat_disable;
// Прочность владения ячейкой, 0..1. На спокойной территории единица, у фронта
// проседает — за счёт этого граница едет плавно, а не прыгает целыми ячейками.
uniform sampler2D control_tex : filter_nearest, repeat_disable;
uniform sampler2D palette_tex : filter_nearest, repeat_disable;
uniform vec2 map_size = vec2(2048.0, 800.0);

uniform vec4 ocean_color : source_color = vec4(0.455, 0.761, 0.910, 1.0);
uniform vec4 ocean_deep_color : source_color = vec4(0.310, 0.624, 0.820, 1.0);
uniform vec4 ocean_shelf_color : source_color = vec4(0.663, 0.875, 0.961, 1.0);
uniform vec4 coast_color : source_color = vec4(0.184, 0.392, 0.533, 1.0);
uniform vec4 border_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 select_color : source_color = vec4(1.0, 0.945, 0.659, 1.0);

// Крупная форма берётся из сетки, мелкие изгибы — отсюда. Шум привязан к карте,
// поэтому граница ползёт по нему, а не он вместе с ней.
uniform float warp_amp = 0.75;
uniform float warp_scale = 420.0;

uniform float border_px = 2.2;
uniform float coast_px = 2.6;
uniform float shelf_px = 26.0;
uniform float shadow_px = 5.0;
uniform float shadow_alpha = 0.22;

// Издалека тонкая линия рвётся и рябит: границы утолщаются по мере отдаления.
uniform float far_border_scale = 2.2;
uniform float far_coast_scale = 2.8;

uniform float region_px = 1.1;
uniform vec4 region_line_color : source_color = vec4(0.0, 0.0, 0.0, 0.22);

uniform int hover_id = 0;

const vec2 DIRS[8] = vec2[8](
	vec2(1.0, 0.0), vec2(-1.0, 0.0), vec2(0.0, 1.0), vec2(0.0, -1.0),
	vec2(0.7071, 0.7071), vec2(-0.7071, 0.7071),
	vec2(0.7071, -0.7071), vec2(-0.7071, -0.7071)
);

float hash21(vec2 p) {
	p = fract(p * vec2(127.31, 311.7));
	p += dot(p, p + 34.53);
	return fract(p.x * p.y);
}

float vnoise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	float a = hash21(i);
	float b = hash21(i + vec2(1.0, 0.0));
	float c = hash21(i + vec2(0.0, 1.0));
	float d = hash21(i + vec2(1.0, 1.0));
	return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float fbm(vec2 p) {
	float v = 0.0;
	float a = 0.5;
	for (int i = 0; i < 3; i++) {
		v += a * vnoise(p);
		p *= 2.07;
		a *= 0.5;
	}
	return v;
}

int owner_at(vec2 uv) {
	return int(texture(owner_tex, clamp(uv, vec2(0.0), vec2(1.0))).r * 255.0 + 0.5);
}

// Номер региона разложен по двум каналам: 16 бит в один байт не влезают.
int region_at(vec2 uv) {
	vec2 t = texture(region_tex, clamp(uv, vec2(0.0), vec2(1.0))).rg;
	return int(t.r * 255.0 + 0.5) + int(t.g * 255.0 + 0.5) * 256;
}

float control_at(vec2 uv) {
	return texture(control_tex, clamp(uv, vec2(0.0), vec2(1.0))).r;
}

vec3 country_color(int id) {
	return texture(palette_tex, vec2((float(id) + 0.5) / 256.0, 0.5)).rgb;
}

// Непрерывная доля суши: билинейные веса четырёх ближайших ячеек.
float land_mask(vec2 uv, vec2 texel) {
	vec2 p = uv * map_size - 0.5;
	vec2 cell = floor(p);
	vec2 f = p - cell;
	vec2 uv00 = (cell + 0.5) * texel;

	float m = 0.0;
	if (owner_at(uv00) != 0) m += (1.0 - f.x) * (1.0 - f.y);
	if (owner_at(uv00 + vec2(texel.x, 0.0)) != 0) m += f.x * (1.0 - f.y);
	if (owner_at(uv00 + vec2(0.0, texel.y)) != 0) m += (1.0 - f.x) * f.y;
	if (owner_at(uv00 + texel) != 0) m += f.x * f.y;
	return m;
}

// Граница как отрыв победителя от второго: там, где они сравнялись, проходит линия.
// Изолиния «веса победителя» для этого не годится — вокруг точки, где сходятся три
// или четыре области, вес победителя проседает ниже половины, и уровень 0.5
// замыкается в кольцо. У отрыва такой беды нет: на стыке он просто обращается в ноль.
float border(float margin, float width) {
	return 1.0 - smoothstep(0.0, width, margin / max(fwidth(margin), 1e-6));
}

// Изолиния 0.5 маски, антиалиасенная и постоянной экранной толщины.
float contour(float mask, float width) {
	float d = (mask - 0.5) / max(fwidth(mask), 1e-6);
	return 1.0 - smoothstep(0.0, width, abs(d));
}

float edge_fill(float mask) {
	return clamp((mask - 0.5) / max(fwidth(mask), 1e-6) + 0.5, 0.0, 1.0);
}

void fragment() {
	vec2 texel = 1.0 / map_size;
	vec2 px = fwidth(UV);

	// Сколько ячеек карты приходится на экранный пиксель: мера отдаления.
	float cells_per_px = px.x * map_size.x;
	float far = smoothstep(0.45, 1.5, cells_per_px);

	// Вдали шум работает мельче пикселя и только рябит — там он не нужен.
	vec2 w = vec2(fbm(UV * warp_scale), fbm(UV * warp_scale + 19.7)) - 0.5;
	vec2 uv = UV + w * warp_amp * (1.0 - far * 0.8) * texel;

	vec2 p = uv * map_size - 0.5;
	vec2 cell = floor(p);
	vec2 f = p - cell;
	vec2 uv00 = (cell + 0.5) * texel;

	int i00 = owner_at(uv00);
	int i10 = owner_at(uv00 + vec2(texel.x, 0.0));
	int i01 = owner_at(uv00 + vec2(0.0, texel.y));
	int i11 = owner_at(uv00 + texel);

	float w00 = (1.0 - f.x) * (1.0 - f.y);
	float w10 = f.x * (1.0 - f.y);
	float w01 = (1.0 - f.x) * f.y;
	float w11 = f.x * f.y;

	float land = (i00 != 0 ? w00 : 0.0) + (i10 != 0 ? w10 : 0.0)
		+ (i01 != 0 ? w01 : 0.0) + (i11 != 0 ? w11 : 0.0);

	// Вес кандидата — сумма весов всех ячеек с тем же владельцем, но каждая ячейка
	// голосует за своего с силой control, а против чужого — с остатком. Пока
	// прочность единица, это ровно прежняя формула; на фронте ослабевшая ячейка
	// начинает голосовать за противника, и граница смещается на доли ячейки.
	float c00 = control_at(uv00);
	float c10 = control_at(uv00 + vec2(texel.x, 0.0));
	float c01 = control_at(uv00 + vec2(0.0, texel.y));
	float c11 = control_at(uv00 + texel);

	float s00 = w00 * c00 + w10 * (i10 == i00 ? c10 : 1.0 - c10)
		+ w01 * (i01 == i00 ? c01 : 1.0 - c01) + w11 * (i11 == i00 ? c11 : 1.0 - c11);
	float s10 = w10 * c10 + w00 * (i00 == i10 ? c00 : 1.0 - c00)
		+ w01 * (i01 == i10 ? c01 : 1.0 - c01) + w11 * (i11 == i10 ? c11 : 1.0 - c11);
	float s01 = w01 * c01 + w00 * (i00 == i01 ? c00 : 1.0 - c00)
		+ w10 * (i10 == i01 ? c10 : 1.0 - c10) + w11 * (i11 == i01 ? c11 : 1.0 - c11);
	float s11 = w11 * c11 + w00 * (i00 == i11 ? c00 : 1.0 - c00)
		+ w10 * (i10 == i11 ? c10 : 1.0 - c10) + w01 * (i01 == i11 ? c01 : 1.0 - c01);

	int id = i00;
	float own = s00;
	if (s10 > own) { id = i10; own = s10; }
	if (s01 > own) { id = i01; own = s01; }
	if (s11 > own) { id = i11; own = s11; }

	// Владелец суши: победитель среди наземных кандидатов, иначе океан съедает берег.
	int land_id = id;
	if (land_id == 0) {
		float best = 0.0;
		if (i00 != 0 && s00 > best) { land_id = i00; best = s00; }
		if (i10 != 0 && s10 > best) { land_id = i10; best = s10; }
		if (i01 != 0 && s01 > best) { land_id = i01; best = s01; }
		if (i11 != 0 && s11 > best) { land_id = i11; best = s11; }
	}

	float shelf = 0.0;
	for (int i = 0; i < 8; i++) {
		vec2 r = max(px, texel) * shelf_px;
		if (owner_at(uv + DIRS[i] * r * 0.45) != 0) {
			shelf += 0.075;
		}
		if (owner_at(uv + DIRS[i] * r) != 0) {
			shelf += 0.05;
		}
	}

	vec3 water = mix(ocean_color.rgb, ocean_shelf_color.rgb, smoothstep(0.0, 0.85, clamp(shelf, 0.0, 1.0)));

	// Суша отбрасывает тень на воду: карта читается как наклейка, а не как заливка.
	float shadow = edge_fill(land_mask(uv - px * shadow_px * vec2(0.6, 0.8), texel));
	water = mix(water, ocean_deep_color.rgb, shadow * shadow_alpha);

	vec3 ground = country_color(land_id);
	bool hovered = hover_id != 0 && land_id == hover_id;

	// Подсветка только выбранной страны: затемнять остальные — значит мигать всей
	// картой. Цвет не подмешивается, а высветляется: смесь с жёлтым даёт грязь.
	if (hovered) {
		ground = clamp(ground * 1.06 + 0.09, 0.0, 1.0);
	}

	// Берег — изолиния маски суши: гладкий на любом зуме, без лесенки из ячеек.
	float on_land = edge_fill(land);
	vec3 col = mix(water, ground, on_land);

	// Условное деление суши на регионы: проступает только вблизи.
	float regions = 1.0 - smoothstep(0.3, 0.85, cells_per_px);
	if (regions > 0.003 && land > 0.5) {
		int r00 = region_at(uv00);
		int r10 = region_at(uv00 + vec2(texel.x, 0.0));
		int r01 = region_at(uv00 + vec2(0.0, texel.y));
		int r11 = region_at(uv00 + texel);

		float q00 = w00 + (r10 == r00 ? w10 : 0.0) + (r01 == r00 ? w01 : 0.0) + (r11 == r00 ? w11 : 0.0);
		float q10 = w10 + (r00 == r10 ? w00 : 0.0) + (r01 == r10 ? w01 : 0.0) + (r11 == r10 ? w11 : 0.0);
		float q01 = w01 + (r00 == r01 ? w00 : 0.0) + (r10 == r01 ? w10 : 0.0) + (r11 == r01 ? w11 : 0.0);
		float q11 = w11 + (r00 == r11 ? w00 : 0.0) + (r10 == r11 ? w10 : 0.0) + (r01 == r11 ? w01 : 0.0);

		int rid = r00;
		float region_own = q00;
		if (q10 > region_own) { rid = r10; region_own = q10; }
		if (q01 > region_own) { rid = r01; region_own = q01; }
		if (q11 > region_own) { rid = r11; region_own = q11; }

		float region_rival = 0.0;
		if (r00 != rid) region_rival = max(region_rival, q00);
		if (r10 != rid) region_rival = max(region_rival, q10);
		if (r01 != rid) region_rival = max(region_rival, q01);
		if (r11 != rid) region_rival = max(region_rival, q11);

		col = mix(col, region_line_color.rgb,
			border(region_own - region_rival, region_px) * regions * region_line_color.a * on_land);
	}

	// Толстая тёмная обводка по берегу и белые границы внутри суши.
	col = mix(col, coast_color.rgb, contour(land, coast_px * mix(1.0, far_coast_scale, far)));

	float rival = 0.0;
	if (i00 != id) rival = max(rival, s00);
	if (i10 != id) rival = max(rival, s10);
	if (i01 != id) rival = max(rival, s01);
	if (i11 != id) rival = max(rival, s11);

	float width = border_px * mix(1.0, far_border_scale, far) * (hovered ? 1.7 : 1.0);
	float political = border(own - rival, width) * on_land * step(0.5, land);
	col = mix(col, hovered ? select_color.rgb : border_color.rgb, political);

	COLOR = vec4(col, 1.0);
}
