// Режет сушу на реальные административные регионы -> data/map/regions.json + regions.bin.
//
//   node tools/gen-regions.mjs <path-to-ne_10m_admin_1_states_provinces.geojson>
//
// Natural Earth даёт для каждой страны свой уровень деления: у Германии 16 земель,
// а у Словении 193 общины. Поэтому берётся поле region — готовое укрупнение
// (Италия 110 провинций -> 20 областей, Британия 232 округа -> 16 регионов), а где
// его нет, работает сама единица admin-1. Остатки мельче порога сливаются с соседом.
//
// Границы областей идут из масштаба 1:10m, а страны у нас нарезаны из 1:50m, поэтому
// закон один: страну ячейки задаёт world.bin, а область к ней подбирается.

import fs from 'node:fs'
import path from 'node:path'

const MIN_CELLS = 3

// Доля веса, раздаваемая по площади: в cities.json только крупные города.
const RURAL_SHARE = 0.6

const src = process.argv[2]
if (!src) {
	console.error('usage: node tools/gen-regions.mjs <ne_10m_admin_1_states_provinces.geojson>')
	process.exit(1)
}

const root = path.resolve(import.meta.dirname, '..')
const mapDir = path.join(root, 'data', 'map')
const read = (name) => JSON.parse(fs.readFileSync(path.join(mapDir, name), 'utf8'))

const bin = fs.readFileSync(path.join(mapDir, 'world.bin'))
if (bin.toString('ascii', 0, 4) !== 'CMW1') throw new Error('world.bin: не тот формат')

const W = bin.readInt32LE(4)
const H = bin.readInt32LE(8)
const owner = bin.subarray(12)

const LAT_TOP = 84
const DEG = 360 / W

const toPx = ([lon, lat]) => [((lon + 180) / 360) * W, ((LAT_TOP - lat) / (H * DEG)) * H]
const toCell = (lon, lat) => [
	Math.min(W - 1, Math.max(0, Math.floor(((lon + 180) / 360) * W))),
	Math.min(H - 1, Math.max(0, Math.floor(((LAT_TOP - lat) / (H * DEG)) * H))),
]

const geo = JSON.parse(fs.readFileSync(src, 'utf8'))

// Natural Earth даёт каждой стране свой уровень: у России 85 субъектов, у Италии
// 110 провинций. Поле region их укрупняет, но по-разному — Италию до 20 областей,
// а Россию сразу до 8 федеральных округов, что уже слишком крупно. Поэтому для
// каждой страны выбирается то деление, что ближе к целевому размеру области.
const TARGET_CELLS = 150

const cellsOfCountry = new Map()
for (let i = 0; i < owner.length; i++) {
	if (owner[i] !== 0) cellsOfCountry.set(owner[i], (cellsOfCountry.get(owner[i]) ?? 0) + 1)
}

const idOfIso = new Map(read('countries.json').countries.map((c) => [c.iso, c.id]))
const perCountry = new Map()

for (const f of geo.features) {
	const iso = f.properties.adm0_a3
	if (!perCountry.has(iso)) perCountry.set(iso, { fine: new Set(), coarse: new Set(), ok: true })

	const c = perCountry.get(iso)
	c.fine.add(f.properties.name ?? '')
	if (f.properties.region && f.properties.region.trim()) c.coarse.add(f.properties.region.trim())
	else c.ok = false
}

const useCoarse = new Map()
for (const [iso, c] of perCountry) {
	const cells = cellsOfCountry.get(idOfIso.get(iso)) ?? 0
	const want = Math.max(1, Math.round(cells / TARGET_CELLS))
	const fine = c.fine.size
	const coarse = c.coarse.size

	useCoarse.set(iso, c.ok && coarse > 0 && Math.abs(coarse - want) <= Math.abs(fine - want))
}

// Ключ группы — страна плюс укрупнение: одноимённые «Central» в разных странах
// не должны слипнуться.
const groups = new Map()

for (const f of geo.features) {
	const p = f.properties
	const coarse = useCoarse.get(p.adm0_a3) && p.region && p.region.trim() ? p.region.trim() : p.name
	if (!coarse) continue

	const key = `${p.adm0_a3}|${coarse}`
	if (!groups.has(key)) groups.set(key, { name: coarse, polygons: [] })

	const g = f.geometry
	if (!g) continue
	if (g.type === 'Polygon') groups.get(key).polygons.push(g.coordinates)
	else if (g.type === 'MultiPolygon') groups.get(key).polygons.push(...g.coordinates)
}

const list = [...groups.values()]
console.log(`групп из admin-1: ${list.length}`)

// Scanline с правилом чётности — тот же приём, что и для стран.
const paint = new Int32Array(owner.length).fill(-1)

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
	const y1 = Math.min(H - 1, Math.ceil(maxY))
	const xs = []

	for (let y = y0; y <= y1; y++) {
		const sy = y + 0.5
		xs.length = 0

		for (const [a, b] of edges) {
			if (sy < Math.min(a[1], b[1]) || sy >= Math.max(a[1], b[1])) continue
			xs.push(a[0] + ((sy - a[1]) / (b[1] - a[1])) * (b[0] - a[0]))
		}
		if (xs.length < 2) continue
		xs.sort((p, q) => p - q)

		for (let i = 0; i + 1 < xs.length; i += 2) {
			const x0 = Math.max(0, Math.ceil(xs[i] - 0.5))
			const x1 = Math.min(W - 1, Math.floor(xs[i + 1] - 0.5))
			for (let x = x0; x <= x1; x++) {
				const at = y * W + x
				if (owner[at] !== 0) paint[at] = id
			}
		}
	}
}

for (let i = 0; i < list.length; i++) {
	for (const rings of list[i].polygons) fillPolygon(rings, i)
}

let painted = 0
let land = 0
for (let i = 0; i < owner.length; i++) {
	if (owner[i] === 0) continue
	land++
	if (paint[i] >= 0) painted++
}
console.log(`закрашено ${painted} из ${land} ячеек суши (${((painted / land) * 100).toFixed(1)}%)`)

// Область не может лежать в двух государствах: страну задаёт world.bin, поэтому
// группа, залезшая за границу, разрезается по странам.
const regionOf = new Int32Array(owner.length).fill(-1)
const regions = []
const keyToRegion = new Map()

for (let i = 0; i < owner.length; i++) {
	const g = paint[i]
	if (g < 0 || owner[i] === 0) continue

	const key = g * 256 + owner[i]
	let r = keyToRegion.get(key)

	if (r === undefined) {
		r = regions.length
		regions.push({ name: list[g].name, country: owner[i], cells: 0 })
		keyToRegion.set(key, r)
	}

	regionOf[i] = r
	regions[r].cells++
}

// Пробелы от несовпадения масштабов 1:10m и 1:50m заполняются волной от размеченных
// ячеек, не пересекая государственную границу.
const queue = []
for (let i = 0; i < owner.length; i++) if (regionOf[i] >= 0) queue.push(i)

let head = 0
while (head < queue.length) {
	const at = queue[head++]
	const x = at % W
	const y = (at / W) | 0

	for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
		const nx = x + dx
		const ny = y + dy
		if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue

		const n = ny * W + nx
		if (owner[n] === 0 || regionOf[n] >= 0 || owner[n] !== owner[at]) continue

		regionOf[n] = regionOf[at]
		regions[regionOf[at]].cells++
		queue.push(n)
	}
}

// Страна, которой в admin-1 не нашлось вовсе, получает один регион на всю себя.
const countryTable = read('countries.json').countries
const nameOfCountry = new Map(countryTable.map((c) => [c.id, c.name]))
const orphanRegion = new Map()

for (let i = 0; i < owner.length; i++) {
	if (owner[i] === 0 || regionOf[i] >= 0) continue

	let r = orphanRegion.get(owner[i])
	if (r === undefined) {
		r = regions.length
		regions.push({ name: nameOfCountry.get(owner[i]) ?? `страна ${owner[i]}`, country: owner[i], cells: 0 })
		orphanRegion.set(owner[i], r)
	}

	regionOf[i] = r
	regions[r].cells++
}

// Соседство по общей границе — по нему мелкие области приклеиваются к самому
// «своему» соседу, а не к случайному.
const neighbours = regions.map(() => new Map())

for (let y = 0; y < H; y++) {
	for (let x = 0; x < W; x++) {
		const at = y * W + x
		const a = regionOf[at]
		if (a < 0) continue

		for (const [dx, dy] of [[1, 0], [0, 1]]) {
			const nx = x + dx
			const ny = y + dy
			if (nx >= W || ny >= H) continue

			const b = regionOf[ny * W + nx]
			if (b < 0 || b === a || regions[a].country !== regions[b].country) continue

			neighbours[a].set(b, (neighbours[a].get(b) ?? 0) + 1)
			neighbours[b].set(a, (neighbours[b].get(a) ?? 0) + 1)
		}
	}
}

const parent = regions.map((_, i) => i)
const find = (i) => (parent[i] === i ? i : (parent[i] = find(parent[i])))

const order = regions.map((r, i) => i).sort((a, b) => regions[a].cells - regions[b].cells)
let merged = 0

for (const i of order) {
	const self = find(i)
	if (regions[self].cells >= MIN_CELLS) continue

	let best = -1
	let bestShared = 0

	for (const [n, shared] of neighbours[i]) {
		const target = find(n)
		if (target === self) continue
		if (shared > bestShared) {
			bestShared = shared
			best = target
		}
	}

	if (best < 0) continue

	parent[self] = best
	regions[best].cells += regions[self].cells
	if (regions[self].cells > regions[best].cells / 2) {
		// имя остаётся у большей части
	}
	for (const [n, shared] of neighbours[i]) neighbours[best].set(n, (neighbours[best].get(n) ?? 0) + shared)
	merged++
}

console.log(`слито мелких областей: ${merged}`)

// Перенумеровываем: наружу идут плотные id с единицы, 0 значит «нет региона».
const finalId = new Int32Array(regions.length).fill(0)
const kept = []

for (let i = 0; i < regions.length; i++) {
	if (find(i) !== i || regions[i].cells === 0) continue
	kept.push(i)
	finalId[i] = kept.length
}

const cell = new Uint16Array(owner.length)
const sumX = new Float64Array(kept.length + 1)
const sumY = new Float64Array(kept.length + 1)
const count = new Int32Array(kept.length + 1)

for (let y = 0; y < H; y++) {
	for (let x = 0; x < W; x++) {
		const at = y * W + x
		if (regionOf[at] < 0) continue

		const id = finalId[find(regionOf[at])]
		cell[at] = id
		sumX[id] += x
		sumY[id] += y
		count[id]++
	}
}

// Месторождения и города привязываются по координате: id регионов меняются при
// любой правке нарезки, поэтому в файлах лежат широта и долгота.
const regionDeposits = new Map()
for (const d of read('deposits.json').deposits) {
	const [x, y] = toCell(d.lon, d.lat)
	const id = cell[y * W + x]
	if (!id) continue
	if (!regionDeposits.has(id)) regionDeposits.set(id, {})
	const bag = regionDeposits.get(id)
	bag[d.good] = (bag[d.good] ?? 0) + d.amount
}

const cityWeight = new Float64Array(kept.length + 1)
for (const c of read('cities.json').cities) {
	const [x, y] = toCell(c.lon, c.lat)
	cityWeight[cell[y * W + x]] += c.pop
}

const byCountry = new Map()
for (let k = 0; k < kept.length; k++) {
	const country = regions[kept[k]].country
	if (!byCountry.has(country)) byCountry.set(country, [])
	byCountry.get(country).push(k + 1)
}

const population = new Int32Array(kept.length + 1)
const countryById = new Map(countryTable.map((c) => [c.id, c]))

for (const [countryId, ids] of byCountry) {
	const total = countryById.get(countryId)?.population ?? 0
	if (!total) continue

	// Города дают вес, остальное раздаётся по площади: в cities.json только крупные,
	// и без второго слагаемого сельские области вышли бы пустыми.
	const cityTotal = ids.reduce((s, id) => s + cityWeight[id], 0)
	const cellTotal = ids.reduce((s, id) => s + count[id], 0) || 1
	const pool = cityTotal > 0 ? cityTotal * RURAL_SHARE : 1

	const weights = ids.map((id) => cityWeight[id] + (count[id] / cellTotal) * pool)
	const sum = weights.reduce((a, b) => a + b, 0) || 1

	const exact = weights.map((w) => (total * w) / sum)
	const base = exact.map(Math.floor)
	let left = total - base.reduce((a, b) => a + b, 0)

	const queue2 = exact.map((v, k) => [v - base[k], k]).sort((a, b) => b[0] - a[0])
	for (let k = 0; k < queue2.length && left > 0; k++, left--) base[queue2[k][1]]++

	ids.forEach((id, k) => (population[id] = base[k]))
}

const out = []
for (let k = 0; k < kept.length; k++) {
	const id = k + 1
	const r = regions[kept[k]]

	out.push({
		id,
		country: r.country,
		name: r.name,
		cells: count[id],
		lon: +(((sumX[id] / count[id] + 0.5) * DEG) - 180).toFixed(3),
		lat: +(LAT_TOP - (sumY[id] / count[id] + 0.5) * DEG).toFixed(3),
		population: population[id],
		deposits: regionDeposits.get(id) ?? {},
	})
}

fs.writeFileSync(
	path.join(mapDir, 'regions.json'),
	JSON.stringify(
		{
			note: 'Реальные административные области из Natural Earth admin-1, укрупнённые по полю region. Геометрия лежит в regions.bin: восстановить её из точки нельзя, как было у диаграммы Вороного.',
			source: 'Natural Earth 1:10m admin-1 states and provinces (public domain)',
			width: W,
			height: H,
			regions: out,
		},
		null,
		'\t'
	) + '\n'
)

const binOut = Buffer.alloc(12 + cell.length * 2)
binOut.write('CMR1', 0, 'ascii')
binOut.writeInt32LE(W, 4)
binOut.writeInt32LE(H, 8)
Buffer.from(cell.buffer).copy(binOut, 12)
fs.writeFileSync(path.join(mapDir, 'regions.bin'), binOut)

const sizes = out.map((r) => r.cells).sort((a, b) => a - b)
console.log(`регионов ${out.length}`)
console.log(`ячеек: мин ${sizes[0]}, медиана ${sizes[sizes.length >> 1]}, макс ${sizes[sizes.length - 1]}`)
console.log(`население ${(out.reduce((s, r) => s + r.population, 0) / 1e9).toFixed(2)} млрд`)
console.log(`пример: ${out.filter((r) => r.name).slice(0, 4).map((r) => r.name).join(', ')}`)
