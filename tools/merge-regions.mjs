// Укрупняет мелкие области -> data/map/regions.json + regions.bin.
//
//   node tools/merge-regions.mjs [--apply]
//
// Зачем. Natural Earth даёт каждой стране свой уровень деления, и после нарезки в сетку
// 2048x800 у мелких стран области выходят по десятку ячеек: у Вьетнама 59 областей на
// 902 ячейки, у Румынии 41 на 887. Такую мелочь не разглядеть, а линии между ними
// сливаются в кашу и рвутся на границах ячеек.
//
// Как. Область должна быть размером примерно с настоящую: сорок-пятьдесят тысяч
// квадратных километров, то есть около сотни наших ячеек. Где областей больше, чем
// нужно по площади страны, самая мелкая сливается с самым мелким соседом — и так, пока
// не останется столько, сколько положено. Страны это не трогает: сливаются только
// области внутри одной.

import fs from 'node:fs'
import path from 'node:path'

/** Сколько ячеек на область. Сто ячеек — это около сорока тысяч кв. км. */
const CELLS_PER_REGION = 110

const apply = process.argv.includes('--apply')
const root = path.resolve(import.meta.dirname, '..')
const mapDir = path.join(root, 'data', 'map')
const read = (name) => JSON.parse(fs.readFileSync(path.join(mapDir, name), 'utf8'))

const file = read('regions.json')
const regions = file.regions
const countries = read('countries.json').countries
const isoOf = new Map(countries.map((c) => [c.id, c.iso]))

const bin = fs.readFileSync(path.join(mapDir, 'regions.bin'))
if (bin.toString('ascii', 0, 4) !== 'CMR1') throw new Error('regions.bin: не тот формат')

const W = bin.readInt32LE(4)
const H = bin.readInt32LE(8)
const cell = new Uint16Array(W * H)
for (let i = 0; i < cell.length; i++) cell[i] = bin.readUInt16LE(12 + i * 2)

const byId = new Map(regions.map((r) => [r.id, r]))

// Кто с кем граничит. Только внутри страны: области разных стран сливать нельзя.
const neighbours = new Map(regions.map((r) => [r.id, new Set()]))
for (let y = 0; y < H; y++) {
	for (let x = 0; x < W; x++) {
		const here = cell[y * W + x]
		if (here === 0) continue

		for (const [dx, dy] of [[1, 0], [0, 1]]) {
			const nx = x + dx
			const ny = y + dy
			if (ny >= H) continue

			const there = cell[ny * W + (nx >= W ? 0 : nx)]
			if (there === 0 || there === here) continue
			if (byId.get(here)?.country !== byId.get(there)?.country) continue

			neighbours.get(here).add(there)
			neighbours.get(there).add(here)
		}
	}
}

// Сколько областей положено стране по её площади.
const cellsOf = new Map()
const listOf = new Map()
for (const r of regions) {
	cellsOf.set(r.country, (cellsOf.get(r.country) ?? 0) + r.cells)
	if (!listOf.has(r.country)) listOf.set(r.country, [])
	listOf.get(r.country).push(r)
}

const merged = new Map()   // id области -> id той, в которую влилась
const gone = new Set()

const resolve = (id) => {
	while (merged.has(id)) id = merged.get(id)
	return id
}

let joins = 0
const report = []

for (const [country, list] of listOf) {
	const target = Math.max(1, Math.min(list.length, Math.round(cellsOf.get(country) / CELLS_PER_REGION)))
	if (list.length <= target) continue

	const alive = new Set(list.map((r) => r.id))
	const before = alive.size

	while (alive.size > target) {
		// Самая мелкая область страны, у которой есть с кем слиться.
		let victim = null
		for (const id of alive) {
			const has = [...neighbours.get(id)].some((n) => alive.has(resolve(n)))
			if (!has) continue
			if (victim === null || byId.get(id).cells < byId.get(victim).cells) victim = id
		}

		if (victim === null) break

		// Сливаем с самым мелким соседом: так области выходят ровнее по размеру.
		let host = null
		for (const raw of neighbours.get(victim)) {
			const n = resolve(raw)
			if (n === victim || !alive.has(n)) continue
			if (host === null || byId.get(n).cells < byId.get(host).cells) host = n
		}

		if (host === null) break

		const from = byId.get(victim)
		const into = byId.get(host)

		into.cells += from.cells
		into.population += from.population
		for (const [good, amount] of Object.entries(from.deposits ?? {})) {
			into.deposits[good] = (into.deposits[good] ?? 0) + amount
		}

		// Середина взвешивается по площади: иначе крохотный кусок утащит её на себя.
		const weight = from.cells / into.cells
		into.lon = Number((into.lon * (1 - weight) + from.lon * weight).toFixed(3))
		into.lat = Number((into.lat * (1 - weight) + from.lat * weight).toFixed(3))

		for (const n of neighbours.get(victim)) {
			const id = resolve(n)
			if (id === host) continue

			neighbours.get(host).add(id)
			neighbours.get(id).add(host)
		}

		merged.set(victim, host)
		gone.add(victim)
		alive.delete(victim)
		joins++
	}

	report.push([isoOf.get(country) ?? country, before, alive.size, cellsOf.get(country)])
}

report.sort((a, b) => (b[1] - b[2]) - (a[1] - a[2]))

console.log('страна'.padEnd(8) + 'было'.padStart(7) + 'стало'.padStart(8) + 'ячеек'.padStart(9))
for (const [iso, before, after, cells] of report.slice(0, 14)) {
	console.log(String(iso).padEnd(8) + String(before).padStart(7) + String(after).padStart(8) + String(cells).padStart(9))
}

const left = regions.filter((r) => !gone.has(r.id))
const sizes = left.map((r) => r.cells).sort((a, b) => a - b)

console.log(`\nобластей ${regions.length} -> ${left.length}, слияний ${joins}`)
console.log(`медиана ${sizes[Math.floor(sizes.length / 2)]} ячеек, мельче десяти ${sizes.filter((s) => s <= 10).length}`)

if (!apply) {
	console.log('\nничего не записано, для записи запусти с --apply')
	process.exit(0)
}

// Номера идут подряд с единицы: в regions.bin они лежат как есть.
const renumber = new Map(left.map((r, i) => [r.id, i + 1]))
for (const r of left) r.id = renumber.get(r.id)

for (let i = 0; i < cell.length; i++) {
	if (cell[i] === 0) continue

	cell[i] = renumber.get(resolve(cell[i])) ?? 0
}

file.regions = left
fs.writeFileSync(path.join(mapDir, 'regions.json'), JSON.stringify(file, null, '\t') + '\n')

const out = Buffer.alloc(12 + cell.length * 2)
out.write('CMR1', 0, 'ascii')
out.writeInt32LE(W, 4)
out.writeInt32LE(H, 8)
for (let i = 0; i < cell.length; i++) out.writeUInt16LE(cell[i], 12 + i * 2)
fs.writeFileSync(path.join(mapDir, 'regions.bin'), out)

console.log('\nзаписано в regions.json и regions.bin')
