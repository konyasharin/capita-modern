// Сквозная проверка данных: ссылки между файлами и наличие иконок.
//
//   node tools/check-data.mjs
//
// Ловит то, что не видит парсер одного файла: рецепт ссылается на несуществующий
// товар, у товара нет производителя, иконка не скачана.

import fs from 'node:fs'
import path from 'node:path'

const root = path.resolve(import.meta.dirname, '..')
const read = (...p) => JSON.parse(fs.readFileSync(path.join(root, ...p), 'utf8'))

const goods = read('data', 'economy', 'goods.json')
const buildings = read('data', 'economy', 'buildings.json')
const deposits = read('data', 'map', 'deposits.json').deposits
const countries = read('data', 'map', 'countries.json').countries
const cities = read('data', 'map', 'cities.json').cities

const errors = []
const warnings = []

const goodIds = new Set(goods.map((g) => g.id))
if (goodIds.size !== goods.length) errors.push('goods.json: повторяющиеся id')

for (const b of buildings) {
	for (const [g, n] of [...Object.entries(b.inputs), ...Object.entries(b.outputs)]) {
		if (!goodIds.has(g)) errors.push(`${b.type}: неизвестный товар ${g}`)
		if (!(n > 0)) errors.push(`${b.type}: ${g} = ${n}`)
	}
	for (const [g, n] of Object.entries(b.buildCost ?? {})) {
		if (!goodIds.has(g)) errors.push(`${b.type}: стройка требует неизвестный товар ${g}`)
		if (!(n > 0)) errors.push(`${b.type}: стройка ${g} = ${n}`)
	}
	if (b.requiresDeposit && !goodIds.has(b.requiresDeposit)) {
		errors.push(`${b.type}: неизвестное месторождение ${b.requiresDeposit}`)
	}
	if (!Object.keys(b.outputs).length) errors.push(`${b.type}: ничего не производит`)
	if (!(b.optimalWorkers > 0)) errors.push(`${b.type}: некорректные рабочие`)
}

const produced = new Set(buildings.flatMap((b) => Object.keys(b.outputs)))
for (const g of goods) {
	if (!produced.has(g.id)) errors.push(`${g.id}: нет производителя`)
}

const consumed = new Set(
	buildings.flatMap((b) => [...Object.keys(b.inputs), ...Object.keys(b.buildCost ?? {})])
)
for (const g of goods) {
	if (g.category !== 'final' && !consumed.has(g.id)) {
		warnings.push(`${g.id}: не используется ни в одном рецепте`)
	}
}

// Добывающая постройка без месторождения будет работать где угодно.
for (const b of buildings) {
	const raw = Object.keys(b.outputs).every(
		(g) => goods.find((x) => x.id === g)?.category === 'resource'
	)
	if (raw && !Object.keys(b.inputs).length && !b.requiresDeposit) {
		warnings.push(`${b.type}: добыча без привязки к месторождению`)
	}
}

for (const d of deposits) {
	if (!goodIds.has(d.good)) errors.push(`месторождение ${d.name}: неизвестный товар ${d.good}`)
}

const withDeposit = new Set(deposits.map((d) => d.good))
for (const b of buildings) {
	if (b.requiresDeposit && !withDeposit.has(b.requiresDeposit)) {
		errors.push(`${b.type}: месторождений ${b.requiresDeposit} нет ни одного`)
	}
}

for (const [group, list, key] of [['goods', goods, 'icon'], ['buildings', buildings, 'icon']]) {
	for (const item of list) {
		const file = path.join(root, 'assets', 'icons', group, `${item[key]}.svg`)
		if (!fs.existsSync(file)) errors.push(`нет иконки ${group}/${item[key]}.svg`)
	}
}

// Города привязываются к миру по координатам, а не по коду страны: код у Natural
// Earth свой (HKG, GRL, PRI), а зависимые территории у нас входят в метрополию.
// Значит проверять надо не код, а то, что город вообще попал на сушу.
const bin = fs.readFileSync(path.join(root, 'data', 'map', 'world.bin'))
const W = bin.readInt32LE(4)
const H = bin.readInt32LE(8)
const owner = bin.subarray(12)
const DEG = 360 / W
const LAT_TOP = 84

let drowned = 0
let offMap = 0

for (const c of cities) {
	const x = Math.floor(((c.lon + 180) / 360) * W)
	const y = Math.floor(((LAT_TOP - c.lat) / (H * DEG)) * H)

	if (x < 0 || y < 0 || x >= W || y >= H) {
		offMap++
	} else if (owner[y * W + x] === 0) {
		drowned++
	}
}

if (offMap) warnings.push(`городов вне карты (Антарктида, полюса): ${offMap}`)
if (drowned) warnings.push(`городов в морской ячейке: ${drowned} из ${cities.length}`)

const noPop = countries.filter((c) => !c.population).length
if (noPop) warnings.push(`стран без населения: ${noPop}`)

console.log(`товаров ${goods.length}, построек ${buildings.length}, месторождений ${deposits.length}`)
console.log(`стран ${countries.length}, городов ${cities.length}`)
console.log(`население: ${(countries.reduce((s, c) => s + c.population, 0) / 1e9).toFixed(2)} млрд`)

for (const w of warnings) console.log(`  ! ${w}`)
for (const e of errors) console.log(`  ОШИБКА: ${e}`)

process.exit(errors.length ? 1 : 0)
