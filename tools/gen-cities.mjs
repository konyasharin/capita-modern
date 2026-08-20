// Города из Natural Earth populated places -> data/map/cities.json.
//
//   node tools/gen-cities.mjs <path-to-ne_10m_populated_places_simple.geojson>
//
// Нужны, чтобы раскладывать население страны по регионам: без них пришлось бы
// размазывать людей ровным слоем и Сибирь оказалась бы населённее Поволжья.
// Привязка к координатам, а не к номерам регионов — нарезка генерируется.

import fs from 'node:fs'
import path from 'node:path'

const src = process.argv[2]
if (!src) {
	console.error('usage: node tools/gen-cities.mjs <geojson>')
	process.exit(1)
}

const root = path.resolve(import.meta.dirname, '..')
const geo = JSON.parse(fs.readFileSync(src, 'utf8'))

// Ячейка карты ~19.5 км, поэтому приморский город часто попадает в морскую ячейку
// и его население потерялось бы: регионы есть только на суше. Такие города
// двигаем на ближайшую сушу.
const bin = fs.readFileSync(path.join(root, 'data', 'map', 'world.bin'))
const W = bin.readInt32LE(4)
const H = bin.readInt32LE(8)
const owner = bin.subarray(12)
const DEG = 360 / W
const LAT_TOP = 84

const toCell = (lon, lat) => [
	Math.floor(((lon + 180) / 360) * W),
	Math.floor(((LAT_TOP - lat) / (H * DEG)) * H),
]

function nearestLand(x0, y0, limit = 12) {
	for (let r = 1; r <= limit; r++) {
		for (let dy = -r; dy <= r; dy++) {
			for (let dx = -r; dx <= r; dx++) {
				if (Math.max(Math.abs(dx), Math.abs(dy)) !== r) continue
				const x = x0 + dx
				const y = y0 + dy
				if (x < 0 || y < 0 || x >= W || y >= H) continue
				if (owner[y * W + x] !== 0) return [x, y]
			}
		}
	}
	return null
}

const cities = []
let snapped = 0
let dropped = 0

for (const f of geo.features) {
	const p = f.properties
	const pop = p.pop_max || p.pop_min || 0
	if (!pop) continue

	let [lon, lat] = f.geometry.coordinates
	const [cx, cy] = toCell(lon, lat)

	if (cx < 0 || cy < 0 || cx >= W || cy >= H) {
		dropped++
		continue
	}

	if (owner[cy * W + cx] === 0) {
		const land = nearestLand(cx, cy)
		if (!land) {
			dropped++
			continue
		}
		lon = (land[0] + 0.5) * DEG - 180
		lat = LAT_TOP - (land[1] + 0.5) * DEG
		snapped++
	}

	cities.push({
		name: p.nameascii || p.name,
		iso: p.adm0_a3,
		capital: p.adm0cap === 1 ? 1 : 0,
		lon: +lon.toFixed(3),
		lat: +lat.toFixed(3),
		pop,
	})
}

cities.sort((a, b) => b.pop - a.pop)

const out = {
	note: 'Города нужны для распределения населения страны по регионам. Привязка к координатам, а не к id региона.',
	source: 'Natural Earth 1:10m populated places (public domain)',
	cities,
}

fs.writeFileSync(path.join(root, 'data', 'map', 'cities.json'), JSON.stringify(out, null, '\t') + '\n')

const sum = cities.reduce((s, c) => s + c.pop, 0)
console.log(`городов ${cities.length}, суммарно ${(sum / 1e9).toFixed(2)} млрд`)
console.log(`подвинуто на сушу: ${snapped}, отброшено: ${dropped}`)
console.log(`крупнейшие: ${cities.slice(0, 5).map((c) => c.name).join(', ')}`)
