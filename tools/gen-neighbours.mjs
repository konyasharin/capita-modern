// Кто с кем граничит и кто выходит к морю -> data/map/neighbours.json.
//
//   node tools/gen-neighbours.mjs [--apply]
//
// Зачем. Соседство считалось на лету в демо фронта и внутри gen-regions, но никуда не
// сохранялось. Без него нет ни сухопутных маршрутов, ни проливов, ни транзита: товар у
// нас возникает у продавца и появляется у покупателя, никакого пути нет, и перекрыть
// нечего.
//
// Что даёт. У каждой страны — соседи по суше с длиной общей границы и признак выхода к
// морю. Отсюда потом строятся маршруты: приморская страна везёт сама, а страна без
// моря — через соседей, и если соседи враждебны, то никак.
//
// Понадобится и войне: фронт идёт по тем же границам.

import fs from 'node:fs'
import path from 'node:path'

const root = path.resolve(import.meta.dirname, '..')
const file = (...p) => path.join(root, ...p)

const apply = process.argv.includes('--apply')
const countries = JSON.parse(fs.readFileSync(file('data', 'map', 'countries.json'), 'utf8')).countries

const bin = fs.readFileSync(file('data', 'map', 'world.bin'))
if (bin.subarray(0, 4).toString('ascii') !== 'CMW1') throw new Error('world.bin: не тот заголовок')

const width = bin.readInt32LE(4)
const height = bin.readInt32LE(8)
const owner = bin.subarray(12)
if (owner.length !== width * height) throw new Error('world.bin: размер не сошёлся с шапкой')

/** Ноль — вода. Ею мы и отличаем берег от сухопутной границы. */
const WATER = 0

const shared = new Map()
const coastal = new Set()

const key = (a, b) => (a < b ? `${a}|${b}` : `${b}|${a}`)

for (let y = 0; y < height; y++) {
	for (let x = 0; x < width; x++) {
		const here = owner[y * width + x]
		if (here === WATER) continue

		// Справа и снизу: так каждая пара ячеек проверяется ровно один раз.
		for (const [dx, dy] of [[1, 0], [0, 1]]) {
			const nx = x + dx
			const ny = y + dy
			if (nx >= width || ny >= height) continue

			const there = owner[ny * width + nx]
			if (there === here) continue

			if (there === WATER) {
				coastal.add(here)
				continue
			}

			shared.set(key(here, there), (shared.get(key(here, there)) ?? 0) + 1)
		}

		// Слева и сверху вода тоже делает берег, а сушу мы уже посчитали с той стороны.
		for (const [dx, dy] of [[-1, 0], [0, -1]]) {
			const nx = x + dx
			const ny = y + dy
			if (nx < 0 || ny < 0) continue
			if (owner[ny * width + nx] === WATER) coastal.add(here)
		}
	}
}

const byId = new Map(countries.map((c) => [c.id, c]))
const out = {}

for (const country of countries) {
	const neighbours = {}
	for (const [pair, cells] of shared) {
		const [a, b] = pair.split('|').map(Number)
		if (a !== country.id && b !== country.id) continue

		const other = byId.get(a === country.id ? b : a)
		if (other) neighbours[other.iso] = cells
	}

	out[country.iso] = {
		coastal: coastal.has(country.id),
		// Соседи по убыванию границы: с кем граница длиннее, через того и возят.
		neighbours: Object.fromEntries(Object.entries(neighbours).sort((x, y) => y[1] - x[1])),
	}
}

const landlocked = countries.filter((c) => !coastal.has(c.id))
const alone = landlocked.filter((c) => Object.keys(out[c.iso].neighbours).length === 0)

console.log(`карта ${width}x${height}, стран ${countries.length}`)
console.log(`с выходом к морю ${coastal.size}, без него ${landlocked.length}`)
console.log(`пар соседей ${shared.size}`)
console.log(`\nбез моря и без соседей: ${alone.length ? alone.map((c) => c.iso).join(', ') : 'нет'}`)
console.log(`\nу кого больше всего соседей:`)
for (const country of countries
	.map((c) => [c.iso, Object.keys(out[c.iso].neighbours).length])
	.sort((a, b) => b[1] - a[1])
	.slice(0, 5)) {
	console.log(`  ${country[0]} — ${country[1]}`)
}

if (!apply) {
	console.log('\nничего не записано, для записи запусти с --apply')
	process.exit(0)
}

const data = {
	note: 'Кто с кем граничит по суше и кто выходит к морю. Считается tools/gen-neighbours.mjs из world.bin: числа при соседях — сколько ячеек общей границы. Нужно маршрутам, транзиту и фронту.',
	byIso: out,
}

fs.writeFileSync(file('data', 'map', 'neighbours.json'), JSON.stringify(data, null, '\t') + '\n')
console.log('\nзаписано в neighbours.json')
