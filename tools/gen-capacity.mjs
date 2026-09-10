// Доводит стартовые мощности до реального запаса -> data/economy/production.json.
//
//   node tools/gen-capacity.mjs [--apply]
//
// Зачем. Мощности были посчитаны так, что выпуск ровно покрывает расход. В жизни так
// не бывает: обрабатывающая промышленность загружена процентов на 78, то есть держит
// около четверти запаса. При нулевом запасе любая неровность в распределении делает
// товар узким местом, и нехватка множится вниз по всей цепочке — мир не выходит и на
// две трети своих возможностей.
//
// Что делает. Наращивает число предприятий там, где запаса не хватает, пока каждый
// товар со своим потребителем не получит целевой запас. Считается по кругу: больше
// заводов — больше расход их собственного сырья, значит и его надо нарастить.
//
// Чего не трогает. Излишки не режет: у Timber, Materials, электричества и военного
// нет потребителя не потому, что их много, а потому что стройки, услуг и армии ещё
// нет. Урезать их сейчас — ломать то, что скоро понадобится.
//
// Занятость сохраняется: сколько прибавилось предприятий, во столько же раз убавилось
// рабочих на каждом. Предприятие у нас — условная единица мощности, привязаны к жизни
// суммарная мощность и суммарная занятость, а не размер отдельного завода.

import fs from 'node:fs'
import path from 'node:path'

const root = path.resolve(import.meta.dirname, '..')
const file = (...p) => path.join(root, ...p)
const read = (...p) => JSON.parse(fs.readFileSync(file(...p), 'utf8'))

/** Загрузка 78% — столько в среднем держит обрабатывающая промышленность. */
const TARGET_SLACK = 1 / 0.78 - 1

const apply = process.argv.includes('--apply')
const buildings = read('data', 'economy', 'buildings.json')
const production = read('data', 'economy', 'production.json')
const consumption = read('data', 'economy', 'consumption.json').unitPerMillionPeople
const regions = read('data', 'map', 'regions.json').regions

const peopleMillions = regions.reduce((sum, r) => sum + r.population, 0) / 1e6
const byPeople = Object.fromEntries(
	Object.entries(consumption).map(([good, rate]) => [good, rate * peopleMillions])
)

// Кто что делает: у электричества четыре разных источника, растить надо все сразу.
const makers = new Map()
for (const b of buildings) {
	for (const good of Object.keys(b.outputs)) {
		if (!makers.has(good)) makers.set(good, [])
		makers.get(good).push(b)
	}
}

const counts = Object.fromEntries(
	Object.entries(production.types).map(([type, info]) => [type, info.world])
)

function balance() {
	const made = {}
	const used = {}
	for (const b of buildings) {
		const n = counts[b.type]
		for (const [good, amount] of Object.entries(b.outputs)) made[good] = (made[good] ?? 0) + n * amount
		for (const [good, amount] of Object.entries(b.inputs)) used[good] = (used[good] ?? 0) + n * amount

		// Стройка — такой же потребитель: мир ежегодно заменяет капитал за срок службы.
		const rebuilt = n / ((b.lifeYears ?? 20) * 365)
		for (const [good, amount] of Object.entries(b.buildCost ?? {})) {
			used[good] = (used[good] ?? 0) + rebuilt * amount
		}
	}

	return { made, used }
}

let rounds = 0
for (; rounds < 200; rounds++) {
	const { made, used } = balance()
	let moved = false

	for (const [good, plants] of makers) {
		const need = (used[good] ?? 0) + (byPeople[good] ?? 0)
		const out = made[good] ?? 0
		if (need <= 0 || out <= 0) continue

		const required = need * (1 + TARGET_SLACK)
		if (out >= required - 1e-9) continue

		const factor = required / out
		for (const plant of plants) counts[plant.type] *= factor
		moved = true
	}

	if (!moved) break
}

// Округление вверх: лучше чуть больше запаса, чем на волос меньше цели.
for (const type of Object.keys(counts)) counts[type] = Math.ceil(counts[type])

console.log(`сошлось за ${rounds} кругов, целевой запас ${Math.round(TARGET_SLACK * 100)}%\n`)
console.log('тип'.padEnd(24) + 'было'.padStart(7) + 'стало'.padStart(7) + 'рабочих было'.padStart(14) + 'стало'.padStart(8))

let changed = 0
for (const b of buildings) {
	const was = production.types[b.type].world
	const now = counts[b.type]
	if (now === was) continue

	changed++
	// Всего рабочих в отрасли остаётся тем же: заводов больше, каждый мельче.
	const workers = Math.max(1, Math.round((b.optimalWorkers * was) / now))
	console.log(b.type.padEnd(24) + String(was).padStart(7) + String(now).padStart(7) +
		String(b.optimalWorkers).padStart(14) + String(workers).padStart(8))

	production.types[b.type].world = now
	b.optimalWorkers = workers
}

const jobs = buildings.reduce((sum, b) => sum + counts[b.type] * b.optimalWorkers, 0)
console.log(`\nизменено типов: ${changed}, предприятий в мире ${Object.values(counts).reduce((a, b) => a + b, 0)}`)
console.log(`занято ${(jobs / 1e6).toFixed(0)} млн`)

if (!apply) {
	console.log('\nничего не записано, для записи запусти с --apply')
	process.exit(0)
}

const tab = (data) => JSON.stringify(data, null, '\t').replace(/\n/g, '\n') + '\n'
fs.writeFileSync(file('data', 'economy', 'production.json'), tab(production))
fs.writeFileSync(file('data', 'economy', 'buildings.json'), tab(buildings))
console.log('\nзаписано в production.json и buildings.json')
