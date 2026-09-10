// Дробит условные единицы производства -> data/economy/*.json.
//
//   node tools/gen-units.mjs [--apply]
//
// Зачем. Предприятие у нас не завод, а условная единица мощности: огромный комбинат —
// это несколько единиц. Но единиц было слишком мало: тысяча угольных разрезов на весь
// мир означает, что маленькой стране достаётся ноль или один, а построить новый она не
// может никогда — он стоит как половина её экономики.
//
// Что делает. Умножает число единиц и во столько же раз уменьшает каждую: выпуск, расход,
// штат, стоимость постройки и человеко-дни. Мир от этого не меняется ни на грамм, зато
// зерно становится мельче: страна строит понемногу и часто, а не раз в десятилетие.
//
// Записанный масштаб хранится в production.json, поэтому запускать можно сколько угодно
// раз — тул приводит данные к нужному, а не умножает вслепую.

import fs from 'node:fs'
import path from 'node:path'

const root = path.resolve(import.meta.dirname, '..')
const file = (...p) => path.join(root, ...p)
const read = (...p) => JSON.parse(fs.readFileSync(file(...p), 'utf8'))

/** Во сколько раз мельче исходных данных. Сто даёт сотни тысяч единиц у ходовых типов. */
const TARGET = 100

const apply = process.argv.includes('--apply')
const buildings = read('data', 'economy', 'buildings.json')
const production = read('data', 'economy', 'production.json')

const now = production.unitScale ?? 1
const factor = TARGET / now

if (factor === 1) {
	console.log(`масштаб уже ${TARGET}, менять нечего`)
	process.exit(0)
}

const split = (goods) => {
	for (const good of Object.keys(goods ?? {})) {
		// Округление до сотых: мельче единицы товара в данных всё равно нет.
		goods[good] = Math.round((goods[good] / factor) * 100) / 100
	}
}

for (const building of buildings) {
	split(building.inputs)
	split(building.outputs)
	split(building.buildCost)
	building.optimalWorkers = Math.max(1, Math.round(building.optimalWorkers / factor))
	building.buildWorkers = Math.max(1, Math.round((building.buildWorkers ?? 1) / factor))
	production.types[building.type].world = Math.round(production.types[building.type].world * factor)
}

production.unitScale = TARGET

const total = Object.values(production.types).reduce((sum, t) => sum + t.world, 0)
const biggest = Object.entries(production.types).sort((a, b) => b[1].world - a[1].world).slice(0, 5)

console.log(`масштаб ${now} -> ${TARGET}, единиц в мире ${total.toLocaleString('ru')}`)
for (const [type, info] of biggest) console.log(`  ${type.padEnd(24)} ${info.world.toLocaleString('ru')}`)

if (!apply) {
	console.log('\nничего не записано, для записи запусти с --apply')
	process.exit(0)
}

fs.writeFileSync(file('data', 'economy', 'buildings.json'), JSON.stringify(buildings, null, '\t') + '\n')
fs.writeFileSync(file('data', 'economy', 'production.json'), JSON.stringify(production, null, '\t') + '\n')
console.log('\nзаписано в buildings.json и production.json')
