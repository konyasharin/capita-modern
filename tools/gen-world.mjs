// Растеризует границы стран Natural Earth в поле владения CapitaModern.
//
//   node tools/gen-world.mjs <path-to-ne_50m_admin_0_countries.geojson>
//
// На выходе:
//   data/map/world.bin       CMW1 + width + height + width*height байт id владельца
//   data/map/countries.json  id -> имя, iso, индекс цвета в палитре, площадь
//
// Источник: Natural Earth (public domain), 1:50m admin-0 countries.

import fs from 'node:fs'
import path from 'node:path'
import zlib from 'node:zlib'

const WIDTH = 2048
// Мир обрезан по широте: Антарктида вырезана, севернее Гренландии земли нет.
// 84°N сверху, ровно HEIGHT ячеек вниз с тем же шагом, что и по долготе.
const HEIGHT = 800
const LAT_TOP = 84
const DEG_PER_CELL = 360 / WIDTH
const LAT_SPAN = HEIGHT * DEG_PER_CELL
const PALETTE_SIZE = 8

const src = process.argv[2]
if (!src) {
	console.error('usage: node tools/gen-world.mjs <geojson>')
	process.exit(1)
}

const root = path.resolve(import.meta.dirname, '..')
const outDir = path.join(root, 'data', 'map')

const geo = JSON.parse(fs.readFileSync(src, 'utf8'))

// Группируем по суверенному государству: Гренландия уходит к Дании, заморские
// территории — к метрополии. Для игры это правильнее, чем 250 отдельных субъектов.
const countries = new Map()

for (const f of geo.features) {
	const p = f.properties
	const name = p.SOVEREIGNT
	if (!name || name === 'Antarctica') continue

	let c = countries.get(name)
	if (!c) {
		c = {
			name,
			// SOV_A3 у суверенитетов с зависимостями выглядит как US1/CH1/AU1 —
			// настоящий код берём у метрополии (HOMEPART), см. ниже.
			iso: p.SOV_A3,
			continent: p.CONTINENT,
			labelX: p.LABEL_X,
			labelY: p.LABEL_Y,
			polygons: [],
		}
		countries.set(name, c)
	}

	// Метрополия задаёт нормальный трёхбуквенный код для всей страны.
	if (p.HOMEPART === 1 && p.ADM0_A3 && !/\d/.test(p.ADM0_A3)) c.iso = p.ADM0_A3

	const g = f.geometry
	if (!g) continue
	if (g.type === 'Polygon') c.polygons.push(g.coordinates)
	else if (g.type === 'MultiPolygon') c.polygons.push(...g.coordinates)
}

const list = [...countries.values()].sort((a, b) => a.name.localeCompare(b.name))
list.forEach((c, i) => (c.id = i + 1))

if (list.length > 254) {
	console.error(`too many countries: ${list.length}, id must fit in a byte`)
	process.exit(1)
}

const owner = new Uint8Array(WIDTH * HEIGHT)

const toPx = ([lon, lat]) => [
	((lon + 180) / 360) * WIDTH,
	((LAT_TOP - lat) / LAT_SPAN) * HEIGHT,
]

// Scanline с правилом чётности: кольца полигона (внешнее + дыры) обрабатываются
// вместе, поэтому дыры получаются сами собой.
function fillPolygon(rings, id) {
	const edges = []
	let minY = Infinity
	let maxY = -Infinity

	for (const ring of rings) {
		const pts = ring.map(toPx)
		for (let i = 0; i < pts.length; i++) {
			const a = pts[i]
			const b = pts[(i + 1) % pts.length]
			if (a[1] === b[1]) continue
			edges.push([a, b])
			minY = Math.min(minY, a[1], b[1])
			maxY = Math.max(maxY, a[1], b[1])
		}
	}
	if (!edges.length) return

	const y0 = Math.max(0, Math.floor(minY))
	const y1 = Math.min(HEIGHT - 1, Math.ceil(maxY))
	const xs = []

	for (let y = y0; y <= y1; y++) {
		const sy = y + 0.5
		xs.length = 0

		for (const [a, b] of edges) {
			const [ax, ay] = a
			const [bx, by] = b
			if (sy < Math.min(ay, by) || sy >= Math.max(ay, by)) continue
			xs.push(ax + ((sy - ay) / (by - ay)) * (bx - ax))
		}
		if (xs.length < 2) continue
		xs.sort((p, q) => p - q)

		for (let i = 0; i + 1 < xs.length; i += 2) {
			const x0 = Math.max(0, Math.ceil(xs[i] - 0.5))
			const x1 = Math.min(WIDTH - 1, Math.floor(xs[i + 1] - 0.5))
			for (let x = x0; x <= x1; x++) owner[y * WIDTH + x] = id
		}
	}
}

for (const c of list) {
	for (const rings of c.polygons) fillPolygon(rings, c.id)
}

// Государство мельче пикселя (Сингапур, Мальта, острова) иначе исчезнет с карты.
const area = new Int32Array(list.length + 1)
for (let i = 0; i < owner.length; i++) area[owner[i]]++

let rescued = 0
for (const c of list) {
	if (area[c.id] > 0) continue
	if (!Number.isFinite(c.labelX) || !Number.isFinite(c.labelY)) continue
	const [px, py] = toPx([c.labelX, c.labelY])
	const x = Math.min(WIDTH - 1, Math.max(0, Math.round(px)))
	const y = Math.min(HEIGHT - 1, Math.max(0, Math.round(py)))
	owner[y * WIDTH + x] = c.id
	area[c.id] = 1
	rescued++
}

// Соседство по 4-связности -> жадная раскраска, чтобы соседи не совпадали цветом.
const adj = new Map(list.map((c) => [c.id, new Set()]))

for (let y = 0; y < HEIGHT; y++) {
	for (let x = 0; x < WIDTH; x++) {
		const a = owner[y * WIDTH + x]
		if (!a) continue
		if (x + 1 < WIDTH) {
			const b = owner[y * WIDTH + x + 1]
			if (b && b !== a) {
				adj.get(a).add(b)
				adj.get(b).add(a)
			}
		}
		if (y + 1 < HEIGHT) {
			const b = owner[(y + 1) * WIDTH + x]
			if (b && b !== a) {
				adj.get(a).add(b)
				adj.get(b).add(a)
			}
		}
	}
}

const color = new Map()
const byDegree = [...list].sort((a, b) => adj.get(b.id).size - adj.get(a.id).size)

// Из допустимых цветов берём самый редкий: иначе жадность сваливает половину карты
// в цвет 0 и получается однотонное полотно.
const used = new Array(PALETTE_SIZE).fill(0)

for (const c of byDegree) {
	const taken = new Set()
	for (const n of adj.get(c.id)) {
		if (color.has(n)) taken.add(color.get(n))
	}
	let pick = -1
	for (let i = 0; i < PALETTE_SIZE; i++) {
		if (taken.has(i)) continue
		if (pick < 0 || used[i] < used[pick]) pick = i
	}
	if (pick < 0) pick = used.indexOf(Math.min(...used))
	color.set(c.id, pick)
	used[pick]++
}

fs.mkdirSync(outDir, { recursive: true })

const bin = Buffer.alloc(12 + owner.length)
bin.write('CMW1', 0, 'ascii')
bin.writeInt32LE(WIDTH, 4)
bin.writeInt32LE(HEIGHT, 8)
Buffer.from(owner.buffer).copy(bin, 12)
fs.writeFileSync(path.join(outDir, 'world.bin'), bin)

const meta = {
	width: WIDTH,
	height: HEIGHT,
	source: 'Natural Earth 1:50m admin-0 countries (public domain)',
	countries: list
		.filter((c) => area[c.id] > 0)
		.map((c) => ({
			id: c.id,
			name: c.name,
			iso: c.iso,
			continent: c.continent,
			color: color.get(c.id),
			cells: area[c.id],
		})),
}
fs.writeFileSync(path.join(outDir, 'countries.json'), JSON.stringify(meta, null, '\t'))

// --preview <file.png>: проверить глазами, что растеризация не поехала
const previewArg = process.argv.indexOf('--preview')
if (previewArg > 0 && process.argv[previewArg + 1]) {
	const palette = JSON.parse(
		fs.readFileSync(path.join(root, 'data', 'map', 'palette.json'), 'utf8')
	)
	const rgb = (hex) => [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16))
	const land = palette.countries.map(rgb)
	const sea = rgb(palette.ocean)

	const raw = Buffer.alloc(HEIGHT * (1 + WIDTH * 3))
	for (let y = 0; y < HEIGHT; y++) {
		const row = y * (1 + WIDTH * 3)
		raw[row] = 0
		for (let x = 0; x < WIDTH; x++) {
			const id = owner[y * WIDTH + x]
			const c = id ? land[color.get(id) % land.length] : sea
			raw.set(c, row + 1 + x * 3)
		}
	}

	const crcTable = Array.from({ length: 256 }, (_, n) => {
		let c = n
		for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1
		return c >>> 0
	})
	const crc32 = (buf) => {
		let c = 0xffffffff
		for (const b of buf) c = crcTable[(c ^ b) & 0xff] ^ (c >>> 8)
		return (c ^ 0xffffffff) >>> 0
	}
	const chunk = (type, data) => {
		const len = Buffer.alloc(4)
		len.writeUInt32BE(data.length)
		const body = Buffer.concat([Buffer.from(type, 'ascii'), data])
		const crc = Buffer.alloc(4)
		crc.writeUInt32BE(crc32(body))
		return Buffer.concat([len, body, crc])
	}
	const ihdr = Buffer.alloc(13)
	ihdr.writeUInt32BE(WIDTH, 0)
	ihdr.writeUInt32BE(HEIGHT, 4)
	ihdr[8] = 8
	ihdr[9] = 2

	fs.writeFileSync(
		process.argv[previewArg + 1],
		Buffer.concat([
			Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
			chunk('IHDR', ihdr),
			chunk('IDAT', zlib.deflateSync(raw)),
			chunk('IEND', Buffer.alloc(0)),
		])
	)
	console.log(`preview: ${process.argv[previewArg + 1]}`)
}

const land = owner.reduce((n, v) => n + (v ? 1 : 0), 0)
console.log(`countries: ${meta.countries.length} (rescued ${rescued})`)
console.log(`land: ${((land / owner.length) * 100).toFixed(1)}% of ${WIDTH}x${HEIGHT}`)
console.log(`colors used: ${new Set(color.values()).size}`)
