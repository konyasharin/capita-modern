// Урезает расход у рецептов, где сырьё стоит дороже продукции.
//
// Соотношения между входами заданы физикой (столько-то руды на тонну стали),
// а масштаб был выставлен на глаз, и часть рецептов оказалась убыточной при
// реальных ценах. Здесь такие рецепты ужимаются до разумной доли материальных
// затрат, пропорции между входами сохраняются.
//
// Только вниз: поднимать расход нельзя, иначе поедет мировой баланс, который
// откалиброван по реальным выпускам.
//
//   node tools/gen-recipes.mjs

import fs from 'node:fs'
import path from 'node:path'

const root = path.resolve(import.meta.dirname, '..')
const read = (...p) => JSON.parse(fs.readFileSync(path.join(root, ...p), 'utf8'))

const buildings = read('data', 'economy', 'buildings.json')
const price = read('data', 'economy', 'prices.json').prices

// доля стоимости выпуска, приходящаяся на сырьё и комплектующие
const MATERIAL_SHARE = { Mining: 0.35, Power: 0.65, Heavy: 0.68, Civil: 0.6, Military: 0.6 }

const value = (goods) => Object.entries(goods).reduce((sum, [g, q]) => sum + price[g] * q, 0)
const round = (x) => (x >= 10 ? Math.round(x) : Number(x.toPrecision(2)))

let fixed = 0

for (const b of buildings) {
	const inputs = value(b.inputs)
	if (inputs === 0) continue

	const target = value(b.outputs) * MATERIAL_SHARE[b.sector]
	const scale = target / inputs

	// только вниз и только заметно: остальное — шум
	if (scale > 0.9) continue

	for (const good of Object.keys(b.inputs)) b.inputs[good] = round(b.inputs[good] * scale)
	fixed++
	console.log(`${b.type}: расход ×${scale.toFixed(2)}`)
}

fs.writeFileSync(
	path.join(root, 'data', 'economy', 'buildings.json'),
	JSON.stringify(buildings, null, '\t') + '\n'
)
console.log(`поправлено рецептов: ${fixed}`)
