// Разбивает мировой океан на бассейны по узким местам -> data/map/basins.json.
//
//   node tools/gen-basins.mjs [--apply]
//
// Зачем. Море у нас бесплатно, как только у страны есть порт, поэтому Суэц и Панама
// ничего не значат — а в жизни через них идёт по девять и пять миллиардов сборов в год,
// и перекрытие Ормуза меняет мировую торговлю.
//
// Как. Океан связен: доплыть можно откуда угодно куда угодно, и простая заливка даёт
// один большой бассейн. Но если считать сушей несколько узких мест из chokepoints.json,
// океан распадается сам — Средиземное отделяется от Атлантики, Чёрное от Средиземного,
// Персидский залив от Индийского. Дальше остаётся посмотреть, какая страна какого
// бассейна касается.

import fs from 'node:fs'
import path from 'node:path'

const root = path.resolve(import.meta.dirname, '..')
const file = (...p) => path.join(root, ...p)
const read = (...p) => JSON.parse(fs.readFileSync(file(...p), 'utf8'))

const apply = process.argv.includes('--apply')
const countries = read('data', 'map', 'countries.json').countries
const chokepoints = read('data', 'map', 'chokepoints.json').points

const bin = fs.readFileSync(file('data', 'map', 'world.bin'))
if (bin.subarray(0, 4).toString('ascii') !== 'CMW1') throw new Error('world.bin: не тот заголовок')

const width = bin.readInt32LE(4)
const height = bin.readInt32LE(8)
const owner = bin.subarray(12)

const WATER = 0
const BLOCKED = -1

// Долгота идёт от края до края, а широта обрезана: карта начинается с 83.9 градуса и
// кончается на −56.7, Антарктиды на ней нет. Числа подобраны по центрам стран от
// Норвегии до Новой Зеландии — прямая ложится точно.
const LAT_TOP = 83.92
const LAT_BOTTOM = -56.70

const toX = (lon) => Math.round(((lon + 180) / 360) * width)
const toY = (lat) => Math.round(((LAT_TOP - lat) / (LAT_TOP - LAT_BOTTOM)) * height)

// Проверка проекции: если она поедет, узкие места промахнутся мимо воды молча.
for (const [name, lon, lat, wet] of [
	['середина Атлантики', -30, 30, true],
	['Москва', 37.6, 55.75, false],
	['Амазония', -60, -5, false],
	['Индийский океан', 75, -20, true],
	['Конго', 20, 0, false],
]) {
	const isWater = owner[toY(lat) * width + toX(lon)] === 0
	if (isWater !== wet) throw new Error(`проекция поехала: ${name} вышел ${isWater ? 'водой' : 'сушей'}`)
}

/** −1 у перекрытых клеток, 0 у воды, номер бассейна с единицы. */
const basin = new Int32Array(width * height)

for (const point of chokepoints) {
	const x0 = Math.max(0, toX(point.lon[0]))
	const x1 = Math.min(width - 1, toX(point.lon[1]))
	const y0 = Math.max(0, toY(point.lat[1]))
	const y1 = Math.min(height - 1, toY(point.lat[0]))

	let cells = 0
	for (let y = y0; y <= y1; y++) {
		for (let x = x0; x <= x1; x++) {
			if (owner[y * width + x] !== WATER) continue

			basin[y * width + x] = BLOCKED
			cells++
		}
	}

	point.cells = cells
	if (cells === 0) console.warn(`  ! ${point.name}: рамка не задела воду, проверь координаты`)
}

let count = 0
const sizes = []
const stack = []

for (let start = 0; start < basin.length; start++) {
	if (owner[start] !== WATER || basin[start] !== 0) continue

	count++
	let size = 0
	stack.push(start)
	basin[start] = count

	while (stack.length) {
		const at = stack.pop()
		size++

		const x = at % width
		const y = (at - x) / width
		for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
			const nx = x + dx
			const ny = y + dy
			if (ny < 0 || ny >= height) continue

			// Карта смыкается по долготе: вокруг света можно обойти.
			const wx = nx < 0 ? width - 1 : nx >= width ? 0 : nx
			const to = ny * width + wx
			if (owner[to] !== WATER || basin[to] !== 0) continue

			basin[to] = count
			stack.push(to)
		}
	}

	sizes.push(size)
}

// Каждой стране — бассейны, которых касается её берег.
const touches = new Map(countries.map((c) => [c.id, new Set()]))
for (let y = 0; y < height; y++) {
	for (let x = 0; x < width; x++) {
		const here = owner[y * width + x]
		if (here === WATER) continue

		for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
			const nx = x + dx
			const ny = y + dy
			if (ny < 0 || ny >= height) continue

			const wx = nx < 0 ? width - 1 : nx >= width ? 0 : nx
			const at = ny * width + wx
			if (owner[at] !== WATER) continue

			const which = basin[at]
			if (which > 0) touches.get(here)?.add(which)
		}
	}
}

// Через что бассейны сообщаются: у перекрытой клетки смотрим, что по обе стороны.
const links = new Map()
for (const point of chokepoints) {
	const x0 = Math.max(0, toX(point.lon[0]))
	const x1 = Math.min(width - 1, toX(point.lon[1]))
	const y0 = Math.max(0, toY(point.lat[1]))
	const y1 = Math.min(height - 1, toY(point.lat[0]))

	const around = new Set()
	for (let y = y0; y <= y1; y++) {
		for (let x = x0; x <= x1; x++) {
			if (basin[y * width + x] !== BLOCKED) continue

			for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1], [2, 0], [-2, 0], [0, 2], [0, -2]]) {
				const nx = x + dx
				const ny = y + dy
				if (ny < 0 || ny >= height) continue

				const wx = nx < 0 ? width - 1 : nx >= width ? 0 : nx
				const which = basin[ny * width + wx]
				if (which > 0) around.add(which)
			}
		}
	}

	point.joins = [...around].sort((a, b) => a - b)
	links.set(point.name, point.joins)
}

const big = sizes.map((size, i) => [i + 1, size]).sort((a, b) => b[1] - a[1])

console.log(`карта ${width}x${height}, бассейнов ${count}`)
console.log(`крупнейшие: ${big.slice(0, 8).map(([id, size]) => `${id} (${(size / 1000).toFixed(0)}k)`).join(', ')}`)
console.log('\nузкие места:')
for (const point of chokepoints) {
	console.log(`  ${point.name.padEnd(22)} ${point.owner}  перекрыто ${String(point.cells).padStart(4)} клеток, ` +
		`соединяет ${point.joins.join(' и ') || 'ничего'}`)
}

const seaLess = countries.filter((c) => (touches.get(c.id)?.size ?? 0) === 0)
console.log(`\nбез единого бассейна: ${seaLess.length} стран`)

if (!apply) {
	console.log('\nничего не записано, для записи запусти с --apply')
	process.exit(0)
}

const data = {
	note: 'Морские бассейны и что их соединяет. Считается tools/gen-basins.mjs: океан заливается водой, а узкие места из chokepoints.json считаются сушей, отчего он и распадается. У страны — номера бассейнов, которых касается её берег.',
	basins: Object.fromEntries(big.map(([id, size]) => [id, { cells: size }])),
	straits: chokepoints.map((p) => ({ name: p.name, owner: p.owner, joins: p.joins })),
	byIso: Object.fromEntries(countries.map((c) => [c.iso, [...(touches.get(c.id) ?? [])].sort((a, b) => a - b)])),
}

fs.writeFileSync(file('data', 'map', 'basins.json'), JSON.stringify(data, null, '\t') + '\n')
console.log('\nзаписано в basins.json')
