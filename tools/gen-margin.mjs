// Выравнивает запас мощности по всем товарам.
//
//   node tools/gen-margin.mjs [--apply]
//
// Число заводов в production.json выведено из настоящего мирового выпуска, а настоящие
// заводы загружены примерно на три четверти. Значит мощности должно быть на четверть
// больше выпуска — у каждого товара, а не в среднем по миру.
//
// Сейчас запас неравномерен: у сельхозсырья 108%, у меди 82%, а у материалов один процент,
// у электричества два. Узкие товары рвут цепочку при первой же неровности, и мир не может
// воспроизвести свой капитал: материалов нужно 567 тысяч единиц в сутки при мощности 444.
//
// Скрипт считает по каждому товару выпуск и расход (заводы, люди, стройка — как в
// check-balance.mjs) и поднимает число заводов тех товаров, у которых запас ниже цели.

import fs from 'node:fs'
import path from 'node:path'

const root = path.resolve(import.meta.dirname, '..')
const read = (...p) => JSON.parse(fs.readFileSync(path.join(root, ...p), 'utf8'))
const file = (...p) => path.join(root, ...p)

const apply = process.argv.includes('--apply')

// Во сколько раз мощность должна превышать расход. Три четверти загрузки — это треть сверху.
const TARGET = 1.33

const buildings = read('data', 'economy', 'buildings.json')
const production = read('data', 'economy', 'production.json')
const world = production.types
const consumption = read('data', 'economy', 'consumption.json').unitPerMillionPeople
const regions = read('data', 'map', 'regions.json').regions

const peopleMillions = regions.reduce((sum, r) => sum + r.population, 0) / 1e6

// Кто что делает: товар -> типы зданий, которые его выпускают.
const makers = {}
for (const b of buildings) {
	for (const good of Object.keys(b.outputs ?? {})) {
		;(makers[good] ??= []).push(b.type)
	}
}

function balance() {
	const made = {}
	const used = {}

	for (const b of buildings) {
		const n = world[b.type].world * 365
		for (const [good, amount] of Object.entries(b.outputs ?? {})) made[good] = (made[good] ?? 0) + n * amount
		for (const [good, amount] of Object.entries(b.inputs ?? {})) used[good] = (used[good] ?? 0) + n * amount
	}

	// Стройка: в установившемся состоянии мир заменяет капитал за срок его службы, а
	// валовые вложения в жизни в полтора раза больше износа — разница это рост.
	for (const b of buildings) {
		const perYear = (world[b.type].world * 1.67) / (b.lifeYears ?? 20)
		for (const [good, amount] of Object.entries(b.buildCost ?? {})) {
			used[good] = (used[good] ?? 0) + perYear * amount
		}
	}

	for (const [good, rate] of Object.entries(consumption)) {
		used[good] = (used[good] ?? 0) + rate * peopleMillions * 365
	}

	return { made, used }
}

const before = balance()
const raised = {}

// Подъём одних мощностей поднимает спрос на их сырьё, поэтому считаем заново, пока запас
// не сойдётся у всех. Двадцати проходов хватает с большим запасом: цепочка в мире длиной
// в четыре-пять переделов.
for (let pass = 0; pass < 20; pass++) {
	const now = balance()
	let moved = false

	for (const [good, types] of Object.entries(makers)) {
		const need = now.used[good] ?? 0
		const have = now.made[good] ?? 0
		if (need <= 0 || have <= 0) continue

		const margin = have / need
		if (margin >= TARGET) continue

		const times = TARGET / margin
		for (const type of types) {
			world[type].world = Math.round(world[type].world * times)
		}

		raised[good] ??= { margin }
		moved = true
	}

	if (!moved) break
}

const after = balance()

console.log('товар'.padEnd(18) + 'было'.padStart(8) + 'стало'.padStart(9) + '  во сколько раз заводов')
for (const [good, info] of Object.entries(raised)) {
	const now = (after.made[good] ?? 0) / (after.used[good] ?? 1)
	console.log(
		good.padEnd(18) +
			(info.margin * 100 - 100).toFixed(0).padStart(7) +
			'%' +
			(now * 100 - 100).toFixed(0).padStart(8) +
			'%' +
			((after.made[good] ?? 0) / Math.max(1, before.made[good] ?? 1)).toFixed(2).padStart(12)
	)
}

const plants = Object.values(world).reduce((sum, t) => sum + t.world, 0)
console.log(`\nзданий в мире: ${plants.toFixed(0)}`)

if (!apply) {
	console.log('\nничего не записано, для записи запусти с --apply')
	process.exit(0)
}

fs.writeFileSync(file('data', 'economy', 'production.json'), JSON.stringify(production, null, '\t') + '\n')
console.log('\nзаписано в production.json')
