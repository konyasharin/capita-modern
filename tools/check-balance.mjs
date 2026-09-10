// Баланс производства и потребления по миру -> сходятся ли цепочки.
//
//   node tools/check-balance.mjs
//
// Считает, сколько каждого товара за год делают все предприятия мира и сколько его
// же съедают рецепты и население. Ловит две вещи: сырья не хватает на переделы, или
// наоборот производится то, что никому не нужно.
//
// Запас — это выпуск сверх расхода. Нулевой запас так же плох, как дефицит: любая
// неровность в распределении, и товар становится узким местом для всей цепочки.

import fs from 'node:fs'
import path from 'node:path'

const root = path.resolve(import.meta.dirname, '..')
const read = (...p) => JSON.parse(fs.readFileSync(path.join(root, ...p), 'utf8'))

const buildings = read('data', 'economy', 'buildings.json')
const goods = read('data', 'economy', 'goods.json')
const world = read('data', 'economy', 'production.json').types
const consumption = read('data', 'economy', 'consumption.json').unitPerMillionPeople
const regions = read('data', 'map', 'regions.json').regions

const peopleMillions = regions.reduce((sum, r) => sum + r.population, 0) / 1e6

const made = {}
const used = {}
const byPeople = {}

for (const b of buildings) {
	const n = world[b.type].world * 365

	for (const [good, amount] of Object.entries(b.outputs)) made[good] = (made[good] ?? 0) + n * amount
	for (const [good, amount] of Object.entries(b.inputs)) used[good] = (used[good] ?? 0) + n * amount
}

for (const [good, rate] of Object.entries(consumption)) {
	byPeople[good] = rate * peopleMillions * 365
}

const million = (v) => (v / 1e6).toFixed(2)
const problems = []

console.log(`население ${peopleMillions.toFixed(0)} млн, млн единиц в год\n`)
console.log(
	'товар'.padEnd(17) + 'выпуск'.padStart(9) + 'заводам'.padStart(9) +
	'людям'.padStart(9) + 'запас'.padStart(9) + '  вердикт'
)

for (const g of goods) {
	const out = made[g.id] ?? 0
	const industry = used[g.id] ?? 0
	const people = byPeople[g.id] ?? 0
	const need = industry + people
	const slack = need > 0 && out > 0 ? out / need - 1 : null

	let mark = '  ок'
	if (out === 0) mark = '  НЕ ДЕЛАЕТСЯ'
	else if (need === 0) mark = '  никому не нужен'
	else if (slack < 0) mark = `  дефицит ${Math.round(-slack * 100)}%`
	else if (slack < 0.15) mark = `  впритык, запас ${Math.round(slack * 100)}%`
	else if (slack > 3) mark = `  лишнее, запас ${Math.round(slack * 100)}%`

	if (mark.includes('дефицит') || mark.includes('впритык') || mark.includes('НЕ')) problems.push(g.id)

	console.log(
		g.id.padEnd(17) + million(out).padStart(9) + million(industry).padStart(9) +
		million(people).padStart(9) + (slack === null ? '—' : `${Math.round(slack * 100)}%`).padStart(9) + mark
	)
}

console.log('\n«никому не нужен» — потребителя ещё нет: стройка, армия, услуги.')
if (problems.length) console.log(`требуют внимания: ${problems.join(', ')}`)
