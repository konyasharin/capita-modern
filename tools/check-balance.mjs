// Баланс производства и потребления по миру -> сходятся ли цепочки.
//
//   node tools/check-balance.mjs
//
// Считает, сколько каждого товара за год делают все предприятия мира и сколько его
// же съедают рецепты. Ловит две вещи: сырья не хватает на переделы, или наоборот
// производится то, что никому не нужно.

import fs from 'node:fs'
import path from 'node:path'

const root = path.resolve(import.meta.dirname, '..')
const read = (...p) => JSON.parse(fs.readFileSync(path.join(root, ...p), 'utf8'))

const buildings = read('data', 'economy', 'buildings.json')
const goods = read('data', 'economy', 'goods.json')
const world = read('data', 'economy', 'production.json').types

const made = {}
const used = {}

for (const b of buildings) {
	const n = world[b.type].world * 365

	for (const [good, amount] of Object.entries(b.outputs)) made[good] = (made[good] ?? 0) + n * amount
	for (const [good, amount] of Object.entries(b.inputs)) used[good] = (used[good] ?? 0) + n * amount
}

const million = (v) => (v / 1e6).toFixed(2)
const problems = []

console.log('товар'.padEnd(15) + 'выпуск'.padStart(10) + 'расход'.padStart(10) + '  баланс   единица')

for (const g of goods) {
	const out = made[g.id] ?? 0
	const inn = used[g.id] ?? 0
	const final = g.category === 'final'
	const ratio = out > 0 ? inn / out : 0

	let mark = '  ок'
	if (final) mark = '  людям'
	else if (out === 0) mark = '  НЕ ДЕЛАЕТСЯ'
	else if (ratio > 1.15) mark = `  дефицит ${Math.round((ratio - 1) * 100)}%`
	else if (ratio < 0.5) mark = `  лишнее ${Math.round((1 - ratio) * 100)}%`

	if (mark.includes('дефицит') || mark.includes('НЕ')) problems.push(g.id)

	console.log(
		g.id.padEnd(15) + million(out).padStart(10) + million(inn).padStart(10) +
		mark.padEnd(18) + (g.unit ?? '')
	)
}

console.log('\nмлн единиц в год; «людям» — конечный товар, потребление населением пока вне модели')
if (problems.length) console.log(`требуют внимания: ${problems.join(', ')}`)
