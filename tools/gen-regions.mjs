// Нарезает сушу на регионы (диаграмма Вороного) -> data/map/regions.json.
//
//   node tools/gen-regions.mjs [--step 13] [--seed 20260814]
//
// Регион — условная единица для просмотра статистики, см. docs/03-industry.md.
// Центры разбрасываются равномерно с дрожанием: регионы выходят округлыми и
// близкими по размеру, но без сеточной регулярности. Ближайший центр ищется
// только среди своей страны — регион не пересекает границу государства.
//
// Здесь же к регионам привязываются месторождения и раскладывается население:
// и то и другое задано координатами, а не номерами регионов, потому что номера
// меняются при правке шага сетки.

import fs from 'node:fs'
import path from 'node:path'

const arg = (name, def) => {
	const i = process.argv.indexOf(`--${name}`)
	return i > 0 && process.argv[i + 1] ? Number(process.argv[i + 1]) : def
}

const STEP = arg('step', 13)
const SEED = arg('seed', 20260814)

// Доля веса, раздаваемая по площади: в cities.json только крупные города, и без
// этого регионы без городов оказались бы безлюдными, а село никуда не делось.
const RURAL_SHARE = 0.6

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

const toCell = (lon, lat) => [
	Math.min(W - 1, Math.max(0, Math.floor(((lon + 180) / 360) * W))),
	Math.min(H - 1, Math.max(0, Math.floor(((LAT_TOP - lat) / (H * DEG)) * H))),
]

// Свой генератор, а не Math.random: нарезка обязана воспроизводиться от seed.
function mulberry32(a) {
	return () => {
		a |= 0
		a = (a + 0x6d2b79f5) | 0
		let t = Math.imul(a ^ (a >>> 15), 1 | a)
		t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t
		return ((t ^ (t >>> 14)) >>> 0) / 4294967296
	}
}

const rnd = mulberry32(SEED)

const seedX = []
const seedY = []
const seedCountry = []
const byCountry = new Map()

function addSeed(x, y, country) {
	const i = seedX.length
	seedX.push(x)
	seedY.push(y)
	seedCountry.push(country)
	if (!byCountry.has(country)) byCountry.set(country, [])
	byCountry.get(country).push(i)
}

for (let gy = 0; gy < H; gy += STEP) {
	for (let gx = 0; gx < W; gx += STEP) {
		const x = Math.min(W - 1, gx + Math.floor(rnd() * STEP))
		const y = Math.min(H - 1, gy + Math.floor(rnd() * STEP))
		const id = owner[y * W + x]
		if (id !== 0) addSeed(x, y, id)
	}
}

// Страна мельче шага сетки могла не получить центра — её ячейки остались бы без региона.
const missing = new Map()
for (let i = 0; i < owner.length; i++) {
	const id = owner[i]
	if (id !== 0 && !byCountry.has(id) && !missing.has(id)) {
		missing.set(id, [i % W, Math.floor(i / W)])
	}
}
for (const [id, xy] of missing) addSeed(xy[0], xy[1], id)

function nearestOf(list, x, y) {
	let best = list[0]
	let bestD = Infinity
	for (const i of list) {
		const dx = seedX[i] - x
		const dy = seedY[i] - y
		const d = dx * dx + dy * dy
		if (d < bestD) {
			bestD = d
			best = i
		}
	}
	return best
}

const cellRegion = new Uint16Array(owner.length)
const cells = new Int32Array(seedX.length)

for (let y = 0; y < H; y++) {
	for (let x = 0; x < W; x++) {
		const at = y * W + x
		const country = owner[at]
		if (country === 0) continue

		// Перебор по индексу, а не поиск по сетке: при равных расстояниях побеждает
		// меньший индекс, и Godot-слой восстанавливает ту же нарезку из regions.json.
		const found = nearestOf(byCountry.get(country), x, y)

		cellRegion[at] = found + 1
		cells[found]++
	}
}

// Месторождения: координата -> ячейка -> регион.
const regionDeposits = new Map()
let lostDeposits = 0

for (const d of read('deposits.json').deposits) {
	const cell = toCell(d.lon, d.lat)
	const rid = cellRegion[cell[1] * W + cell[0]]
	if (rid === 0) {
		lostDeposits++
		continue
	}
	if (!regionDeposits.has(rid)) regionDeposits.set(rid, {})
	const bag = regionDeposits.get(rid)
	bag[d.good] = (bag[d.good] || 0) + d.amount
}

// Население: города дают вес, остальное раздаётся по площади.
const cityWeight = new Float64Array(seedX.length + 1)
let lostCities = 0

for (const c of read('cities.json').cities) {
	const cell = toCell(c.lon, c.lat)
	const rid = cellRegion[cell[1] * W + cell[0]]
	if (rid === 0) {
		lostCities++
		continue
	}
	cityWeight[rid] += c.pop
}

const countryById = new Map(read('countries.json').countries.map((c) => [c.id, c]))
const regionsOf = new Map()

for (let i = 0; i < seedX.length; i++) {
	const country = seedCountry[i]
	if (!regionsOf.has(country)) regionsOf.set(country, [])
	regionsOf.get(country).push(i)
}

const population = new Int32Array(seedX.length + 1)

for (const [countryId, list] of regionsOf) {
	const total = countryById.get(countryId)?.population || 0
	if (!total) continue

	const cityTotal = list.reduce((s, i) => s + cityWeight[i + 1], 0)
	const cellTotal = list.reduce((s, i) => s + cells[i], 0) || 1
	const areaPool = cityTotal > 0 ? cityTotal * RURAL_SHARE : 1

	const weights = list.map((i) => cityWeight[i + 1] + (cells[i] / cellTotal) * areaPool)
	const sum = weights.reduce((a, b) => a + b, 0) || 1

	// Метод наибольших остатков: иначе округление потеряет или добавит людей.
	const exact = weights.map((w) => (total * w) / sum)
	const base = exact.map(Math.floor)
	let left = total - base.reduce((a, b) => a + b, 0)

	const order = exact.map((v, k) => [v - base[k], k]).sort((a, b) => b[0] - a[0])
	for (let k = 0; k < order.length && left > 0; k++, left--) base[order[k][1]]++

	list.forEach((i, k) => (population[i + 1] = base[k]))
}

const regions = []

for (let i = 0; i < seedX.length; i++) {
	if (cells[i] === 0) continue

	regions.push({
		id: i + 1,
		country: seedCountry[i],
		cells: cells[i],
		x: seedX[i],
		y: seedY[i],
		lon: +((seedX[i] + 0.5) * DEG - 180).toFixed(3),
		lat: +(LAT_TOP - (seedY[i] + 0.5) * DEG).toFixed(3),
		population: population[i + 1],
		deposits: regionDeposits.get(i + 1) || {},
	})
}

const meta = {
	note: 'Нарезка суши на регионы. Геометрия задана центрами (x, y): ячейка принадлежит ближайшему центру своей страны.',
	step: STEP,
	seed: SEED,
	width: W,
	height: H,
	regions,
}

fs.writeFileSync(path.join(mapDir, 'regions.json'), JSON.stringify(meta, null, '\t') + '\n')

const sizes = regions.map((r) => r.cells).sort((a, b) => a - b)
const pop = regions.reduce((s, r) => s + r.population, 0)
const withDeposits = regions.filter((r) => Object.keys(r.deposits).length).length

console.log(`регионов ${regions.length}, шаг ${STEP}, seed ${SEED}`)
console.log(`ячеек: мин ${sizes[0]}, медиана ${sizes[sizes.length >> 1]}, макс ${sizes[sizes.length - 1]}`)
console.log(`население ${(pop / 1e9).toFixed(2)} млрд, регионов с месторождениями ${withDeposits}`)
if (lostDeposits) console.log(`месторождений вне суши: ${lostDeposits}`)
if (lostCities) console.log(`городов вне суши: ${lostCities}`)
