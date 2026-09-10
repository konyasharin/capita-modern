// Пересчитывает buildCost из отношения капитала к выпуску -> data/economy/buildings.json.
//
//   node tools/gen-buildcost.mjs [--apply]
//
// Зачем. Стоимость постройки была взята на глаз, и вышло, что весь капитал мира стоит
// 2.6 трлн при годовом выпуске в 18.8 — отношение 0.14. В жизни оно около трёх: капитал
// стоит примерно три годовых выпуска, а служит лет двенадцать. С таким разрывом стройка
// шла бы в двадцать раз быстрее настоящей, и мир застроился бы за месяц.
//
// Как считается. Завод должен стоить примерно три своих годовых добавленных стоимости.
// Состав — четыре пятых материалами, пятая металлом: в жизни сталь это процентов
// пятнадцать стоимости стройки, остальное цемент, заполнители и работа. При равных долях
// стройка требовала бы больше металла, чем мир вообще выплавляет.

import fs from 'node:fs'
import path from 'node:path'

const root = path.resolve(import.meta.dirname, '..')
const file = (...p) => path.join(root, ...p)
const read = (...p) => JSON.parse(fs.readFileSync(file(...p), 'utf8'))

/** Во сколько годовых выпусков обходится капитал. Всемирный банк, порядок по миру. */
const CAPITAL_TO_OUTPUT = 3

/** Из чего складывается стоимость стройки, доли. Сталь в жизни около шестой части. */
const MIX = { Materials: 0.8, Metals: 0.2 }

/** Человеко-дней на тысячу долларов стройки.
 *
 *  В стройке занято около 7% мировой рабочей силы — 240 млн человек, — и строят они весь
 *  капитал за срок его службы. Работа не входит в buildCost: то мешок цемента, а это
 *  люди. Их цена — местная зарплата, поэтому в бедной стране та же стройка обходится
 *  дешевле, и это настоящее преимущество. */
const MAN_DAYS_PER_THOUSAND = 11.1

const apply = process.argv.includes('--apply')
const buildings = read('data', 'economy', 'buildings.json')
const prices = read('data', 'economy', 'prices.json').prices
const world = read('data', 'economy', 'production.json').types

const worth = (goods) => Object.entries(goods ?? {}).reduce((sum, [good, n]) => sum + n * (prices[good] ?? 0), 0)

let capital = 0
let output = 0

console.log('тип'.padEnd(24) + 'было'.padStart(10) + 'стало'.padStart(10) + '   выпуск в год')

for (const building of buildings) {
	const yearly = (worth(building.outputs) - worth(building.inputs)) * 365
	const was = worth(building.buildCost)

	// Убыточному заводу цену не назначить — оставляем как есть и отмечаем.
	if (yearly <= 0) {
		console.log(building.type.padEnd(24) + was.toFixed(0).padStart(10) + '  без изменений   убыточен')
		capital += was * world[building.type].world
		continue
	}

	const target = yearly * CAPITAL_TO_OUTPUT
	building.buildCost = {}
	for (const [good, share] of Object.entries(MIX)) {
		building.buildCost[good] = Math.max(1, Math.round((target * share) / prices[good]))
	}

	building.buildWorkers = Math.max(1, Math.round(worth(building.buildCost) * MAN_DAYS_PER_THOUSAND))

	const now = worth(building.buildCost)
	capital += now * world[building.type].world
	output += yearly * world[building.type].world

	console.log(building.type.padEnd(24) + was.toFixed(0).padStart(10) + now.toFixed(0).padStart(10) +
		yearly.toFixed(0).padStart(15))
}

console.log(`\nкапитал мира ${(capital / 1e9).toFixed(2)} трлн, выпуск ${(output / 1e9).toFixed(2)} трлн/год`)
console.log(`отношение ${(capital / output).toFixed(2)} при цели ${CAPITAL_TO_OUTPUT}`)

const builders = buildings.reduce(
	(sum, b) => sum + (b.buildWorkers ?? 0) * world[b.type].world / ((b.lifeYears ?? 20) * 365), 0)
const yearly = buildings.reduce(
	(sum, b) => sum + worth(b.buildCost) * world[b.type].world / ((b.lifeYears ?? 20)), 0)
console.log(`стройки на ${(yearly / 1e9).toFixed(1)} трлн в год, занято ${(builders / 1e6).toFixed(0)} млн при реальных 240`)

if (!apply) {
	console.log('\nничего не записано, для записи запусти с --apply')
	process.exit(0)
}

fs.writeFileSync(file('data', 'economy', 'buildings.json'), JSON.stringify(buildings, null, '\t') + '\n')
console.log('\nзаписано в buildings.json')
