// Сверяет экономику с реальностью: убыточные рецепты, уровень зарплаты,
// производительность по странам. Допуски описаны в docs/08-economic-model.md.
//
//   node tools/check-economy.mjs

import fs from 'node:fs'
import path from 'node:path'

const root = path.resolve(import.meta.dirname, '..')
const read = (...p) => JSON.parse(fs.readFileSync(path.join(root, ...p), 'utf8'))

const buildings = read('data', 'economy', 'buildings.json')
const production = read('data', 'economy', 'production.json').types
const price = read('data', 'economy', 'prices.json').prices
const industry = read('data', 'economy', 'start-industry.json').regions
const regions = new Map(read('data', 'map', 'regions.json').regions.map((r) => [String(r.id), r]))
const iso = new Map(read('data', 'map', 'countries.json').countries.map((c) => [c.id, c.iso]))

const LABOUR_SHARE = 0.5
const TICKS = 365
const errors = []
const warnings = []
const spread = []

const value = (goods) => Object.entries(goods).reduce((sum, [g, q]) => sum + price[g] * q, 0)

// 1. Рецепт не может стоить дороже того, что производит
console.log('добавленная стоимость за тик, тыс. $')
for (const b of buildings) {
	const added = value(b.outputs) - value(b.inputs)
	if (added <= 0) errors.push(`${b.type}: убыточен при стартовых ценах (${added.toFixed(0)})`)
	if (added > 0 && added / b.optimalWorkers > 20) {
		warnings.push(`${b.type}: ${(added / b.optimalWorkers).toFixed(1)} тыс. $ на работника за сутки — многовато`)
	}
}

// 2. Средняя зарплата в мире
let worldAdded = 0
let worldWorkers = 0
for (const b of buildings) {
	worldAdded += (value(b.outputs) - value(b.inputs)) * production[b.type].world * TICKS
	worldWorkers += b.optimalWorkers * production[b.type].world
}
const wage = (worldAdded * LABOUR_SHARE) / worldWorkers
console.log(`  мировая добавленная стоимость ${(worldAdded / 1e9).toFixed(1)} трлн $/год`)
console.log(`  занято ${(worldWorkers / 1e6).toFixed(0)} млн, средняя зарплата ${(wage * 1000).toFixed(0)} $/год`)
if (wage * 1000 < 5000 || wage * 1000 > 40000) errors.push(`средняя зарплата ${(wage * 1000).toFixed(0)} $ вне разумного`)

// 3. Производительность по странам против реальной
const REAL = { USA: 190, DEU: 100, JPN: 100, TWN: 95, CHN: 35, RUS: 20, BRA: 15, IND: 7, NGA: 10 }
const added = new Map()
const workers = new Map()

for (const [id, plants] of Object.entries(industry)) {
	const country = iso.get(regions.get(id).country)
	for (const [type, count] of Object.entries(plants)) {
		const b = buildings.find((x) => x.type === type)
		added.set(country, (added.get(country) ?? 0) + (value(b.outputs) - value(b.inputs)) * count * TICKS)
		workers.set(country, (workers.get(country) ?? 0) + b.optimalWorkers * count)
	}
}

console.log('\nдобавленная стоимость на работника, тыс. $/год')
const base = added.get('USA') / workers.get('USA')
for (const [country, real] of Object.entries(REAL)) {
	if (!workers.get(country)) continue
	const ours = added.get(country) / workers.get(country)
	const ratio = ours / base / (real / REAL.USA)
	const mark = ratio > 3 || ratio < 1 / 3 ? '  <<<' : ''
	console.log(`  ${country}  наша ${ours.toFixed(0).padStart(4)}  реальная ${String(real).padStart(4)}  к США ${(ours / base).toFixed(2)} против ${(real / REAL.USA).toFixed(2)}${mark}`)
	if (ratio > 3 || ratio < 1 / 3) spread.push(country)
}

if (spread.length) {
	console.log(`
  Разброс производительности вдвое-втрое ниже реального (${spread.join(', ')}).`)
	console.log('  Так и должно быть, пока нет эффективности: завод у нас везде одинаков.')
	console.log('  См. раздел 4 в docs/08-economic-model.md.')
}

for (const w of warnings) console.log(`  ! ${w}`)
for (const e of errors) console.log(`  ОШИБКА ${e}`)
if (errors.length) process.exitCode = 1
