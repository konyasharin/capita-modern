// Разбивает сферу услуг на четыре отрасли -> data/economy/*.json.
//
//   node tools/gen-services.mjs [--apply]
//
// Зачем. Услуги были одним типом предприятия на всё: розницу, транспорт, больницы,
// школы, банки и государство. Из этого выходило две беды.
//
// Первая — люди. Штат брался не из данных о занятости, как у всех остальных типов, а
// был проставлен на глаз: 3628 человек на единицу, то есть 2.33 млрд рабочих мест на
// мир при настоящих 1.6 млрд. Потребность в руках упиралась во всю рабочую силу мира,
// и половина стран сидела с нехваткой людей.
//
// Вторая — входы. Услуги не потребляли ничего, а в жизни сфера услуг это крупнейший
// потребитель электричества и почти единственный потребитель лекарств. У нас у обоих
// товаров промышленного покупателя не было вовсе.
//
// Как разбито. По занятости МОТ и по доле в добавленной стоимости услуг: единиц у типа
// столько, сколько он даёт стоимости, а людей — сколько их занято в жизни. Отсюда сама
// собой выходит разная трудоёмкость: банк даёт ту же стоимость втрое меньшим числом
// людей, чем магазин.

import fs from 'node:fs'
import path from 'node:path'

const root = path.resolve(import.meta.dirname, '..')
const file = (...p) => path.join(root, ...p)
const read = (...p) => JSON.parse(fs.readFileSync(file(...p), 'utf8'))

/** Что заменяем. Его единицы и выпуск раскладываются по новым типам без остатка. */
const OLD = 'ServiceFirm'

/** Занятость в мире, человек — МОТ, 2020. Доля в добавленной стоимости услуг — счета
 *  ООН по видам деятельности. Недвижимость сидит в деловых услугах: людей в ней почти
 *  нет, а стоимости много, оттого у них и самая низкая трудоёмкость. */
const KINDS = [
	{
		type: 'RetailFirm',
		name: 'Торговля и общепит',
		icon: 'retail-firm',
		workers: 620_000_000,
		value: 0.24,
		lifeYears: 25,
		// Магазину нужны свет, доставка и содержание помещений.
		mix: { Electricity: 0.30, Fuel: 0.25, Materials: 0.40, Electronics: 0.15 },
	},
	{
		type: 'TransportFirm',
		name: 'Транспорт и склады',
		icon: 'transport-firm',
		workers: 210_000_000,
		value: 0.14,
		lifeYears: 12,
		mix: { Electricity: 0.08, Fuel: 0.65, Materials: 0.20, Electronics: 0.05 },
	},
	{
		type: 'PublicService',
		name: 'Здравоохранение, школы и государство',
		icon: 'public-service',
		workers: 490_000_000,
		value: 0.28,
		lifeYears: 40,
		mix: { Electricity: 0.32, Fuel: 0.05, Materials: 0.25, Electronics: 0.20, Medicine: 1.0 },
	},
	{
		type: 'BusinessFirm',
		name: 'Финансы и деловые услуги',
		icon: 'business-firm',
		workers: 280_000_000,
		value: 0.34,
		lifeYears: 30,
		mix: { Electricity: 0.30, Fuel: 0.05, Materials: 0.15, Electronics: 0.60 },
	},
]

/** Какую долю мирового выпуска товара забирает вся сфера услуг. В жизни на коммерческий
 *  сектор приходится около четверти электричества, а лекарства покупает почти одно
 *  здравоохранение. Промышленность у нас уже разбирает 78% энергии и топлива — услугам
 *  достаётся то, что осталось. */
const TAKES = {
	Electricity: 0.20,
	Fuel: 0.12,
	Materials: 0.03,
	Electronics: 0.15,
	Medicine: 0.60,
}

const apply = process.argv.includes('--apply')
const buildings = read('data', 'economy', 'buildings.json')
const production = read('data', 'economy', 'production.json')
const employment = read('data', 'economy', 'employment.json')
const price = read('data', 'economy', 'prices.json').prices

const old = buildings.find((b) => b.type === OLD)
if (!old) throw new Error(`${OLD} уже разобран, править нечего`)

const oldWorld = production.types[OLD]
const totalUnits = oldWorld.world
const perUnitOutput = old.outputs.Services

// Сколько всего товара забирают услуги — от мирового выпуска этого товара.
const worldOutput = {}
for (const b of buildings) {
	const count = production.types[b.type]?.world ?? 0
	for (const [good, q] of Object.entries(b.outputs)) worldOutput[good] = (worldOutput[good] ?? 0) + q * count
}

const shareSum = KINDS.reduce((s, k) => s + k.value, 0)
if (Math.abs(shareSum - 1) > 1e-9) throw new Error(`доли стоимости дают ${shareSum}, а нужна единица`)

const workersSum = KINDS.reduce((s, k) => s + k.workers, 0)
const round = (x) => (x >= 10 ? Math.round(x) : Number(x.toPrecision(3)))

// Единиц у типа столько, сколько он даёт стоимости: тогда выпуск на единицу у всех один
// и общий выпуск услуг не меняется ни на грамм.
let unitsLeft = totalUnits
const made = []

for (let i = 0; i < KINDS.length; i++) {
	const kind = KINDS[i]
	const units = i === KINDS.length - 1 ? unitsLeft : Math.round(totalUnits * kind.value)
	unitsLeft -= units

	const inputs = {}
	for (const [good, share] of Object.entries(kind.mix)) {
		const taken = (worldOutput[good] ?? 0) * (TAKES[good] ?? 0) * share
		if (taken > 0) inputs[good] = round(taken / units)
	}

	made.push({
		type: kind.type,
		name: kind.name,
		icon: kind.icon,
		sector: 'Services',
		requiresDeposit: null,
		inputs,
		outputs: { Services: perUnitOutput },
		optimalWorkers: Math.round(kind.workers / units),
		buildCost: { ...old.buildCost },
		lifeYears: kind.lifeYears,
		buildWorkers: old.buildWorkers,
		units,
		workers: kind.workers,
	})
}

console.log(`было: ${OLD}, ${totalUnits.toLocaleString('ru')} единиц, ` +
	`${(old.optimalWorkers * totalUnits / 1e6).toFixed(0)} млн рабочих мест\n`)
console.log('тип'.padEnd(16) + 'единиц'.padStart(10) + 'человек'.padStart(10) +
	'на единицу'.padStart(12) + '   входы, доля выпуска')

let checkUnits = 0
let checkWorkers = 0
let checkOutput = 0
let checkInputs = 0

for (const kind of made) {
	const output = price.Services * kind.outputs.Services * kind.units
	const inputs = Object.entries(kind.inputs).reduce((s, [g, q]) => s + price[g] * q * kind.units, 0)

	checkUnits += kind.units
	checkWorkers += kind.workers
	checkOutput += output
	checkInputs += inputs

	console.log(kind.type.padEnd(16) + kind.units.toLocaleString('ru').padStart(10) +
		`${(kind.workers / 1e6).toFixed(0)} млн`.padStart(10) +
		String(kind.optimalWorkers).padStart(12) + `   ${(100 * inputs / output).toFixed(1)}%`)
}

console.log(`\nединиц ${checkUnits.toLocaleString('ru')} из ${totalUnits.toLocaleString('ru')}, ` +
	`рабочих мест ${(checkWorkers / 1e6).toFixed(0)} млн при настоящих 1600`)
console.log(`выпуск услуг ${(checkOutput / 1e6).toFixed(1)} млн $/сут, ` +
	`входы ${(checkInputs / 1e6).toFixed(1)} млн — ${(100 * checkInputs / checkOutput).toFixed(1)}% выпуска`)

console.log('\nсколько мирового выпуска товара уходит в услуги:')
for (const [good, share] of Object.entries(TAKES)) {
	const taken = made.reduce((s, k) => s + (k.inputs[good] ?? 0) * k.units, 0)
	console.log(`  ${good.padEnd(18)} ${taken.toFixed(0).padStart(8)} из ${(worldOutput[good] ?? 0).toFixed(0)} ` +
		`(цель ${(100 * share).toFixed(0)}%)`)
}

if (!apply) {
	console.log('\nничего не записано, для записи запусти с --apply')
	process.exit(0)
}

const at = buildings.findIndex((b) => b.type === OLD)
buildings.splice(at, 1, ...made.map(({ units, workers, ...rest }) => rest))

delete production.types[OLD]
for (const kind of made) {
	production.types[kind.type] = { world: kind.units, tail: 0, byGdp: true, shares: {} }
}

delete employment.workers[OLD]
for (const kind of made) employment.workers[kind.type] = kind.workers

fs.writeFileSync(file('data', 'economy', 'buildings.json'), JSON.stringify(buildings, null, '\t') + '\n')
fs.writeFileSync(file('data', 'economy', 'production.json'), JSON.stringify(production, null, '\t') + '\n')
fs.writeFileSync(file('data', 'economy', 'employment.json'), JSON.stringify(employment, null, '\t') + '\n')
console.log('\nзаписано в buildings.json, production.json и employment.json')
