// Стартовая эффективность предприятий по странам -> data/economy/efficiency.json.
//
//   node tools/gen-efficiency.mjs [--apply]
//
// Зачем. Завод в модели везде одинаковый, поэтому вся разница в производительности
// между странами должна лежать в множителе. В жизни она в двадцать с лишним раз, у нас
// вчетверо — и оттого нет ни сравнительного преимущества, ни правдоподобного экспорта.
//
// Откуда берётся. Из подушевого ВВП относительно передового: он уже есть в
// countries.json. Но берётся не напрямую: реальный разброс подушевого ВВП больше, чем
// разброс производительности в материальном производстве. Крестьянин с мотыгой всё же
// выращивает еду, а вот услуг в бедной стране почти нет. Поэтому отношение берётся в
// степени, а сама степень подобрана по настоящей выработке на работника.
//
// Важное. Множитель не меняет выпуск: мощности в production.json уже подобраны так,
// чтобы мировой выпуск сошёлся с настоящим. Он меняет число рук — американская ферма с
// комбайном и индийская с полусотней людей дают одно и то же. Значит нормировать надо
// именно занятость: сумма (предприятия / эффективность) должна остаться прежней, а это
// среднее гармоническое, а не обычное. По обычному мир нанимал бы вдвое больше людей,
// чем в жизни.
//
// Разбивка на три множителя — правило, а не данные. Показатели подобраны так, чтобы
// разброс каждого совпал с настоящим: квалификация вдвое-втрое, технология вчетверо,
// состояние капитала сильнее всего. Дальше каждый заживёт своей жизнью: квалификация от
// расходов на образование, технология от древа, состояние от износа и ремонта.

import fs from 'node:fs'
import path from 'node:path'

const root = path.resolve(import.meta.dirname, '..')
const file = (...p) => path.join(root, ...p)
const read = (...p) => JSON.parse(fs.readFileSync(file(...p), 'utf8'))

/** Насколько разрыв в материальном производстве отличается от разрыва в подушевом ВВП.
 *  Подобрано перебором по настоящей выработке на работника: 0.95 даёт наименьшую
 *  среднюю ошибку, 1.37 раза. */
const GAP = 0.95

/** Доли произведения. В сумме единица, иначе произведение разъедется с целью. */
const SHARES = { skill: 0.25, tech: 0.4, condition: 0.35 }

/** Ниже этого не опускается никто: даже мотыга что-то производит. */
const FLOOR = 0.02

/** Передовая страна, к которой всё меряется. */
const FRONTIER = 'USA'

const apply = process.argv.includes('--apply')
const countries = read('data', 'map', 'countries.json').countries
const startIndustry = read('data', 'economy', 'start-industry.json').regions
const regions = read('data', 'map', 'regions.json').regions
const buildings = read('data', 'economy', 'buildings.json')

// Взвешивать надо по рабочим, а не по заводам: ферма просит сорок семь тысяч человек,
// рудник — три. По заводам нормировка промахивается в полтора раза.
const workersPer = new Map(buildings.map((b) => [b.type, b.optimalWorkers]))
const plantsOf = new Map()
const countryOfRegion = new Map(regions.map((r) => [String(r.id), r.country]))
for (const [region, plants] of Object.entries(startIndustry)) {
	const owner = countryOfRegion.get(region)
	if (owner === undefined) continue

	let workers = 0
	for (const [type, count] of Object.entries(plants)) workers += count * (workersPer.get(type) ?? 0)
	plantsOf.set(owner, (plantsOf.get(owner) ?? 0) + workers)
}

const perCapita = (c) => (c.population > 0 ? (c.gdp * 1e6) / c.population : 0)
const frontier = perCapita(countries.find((c) => c.iso === FRONTIER))

const raw = new Map()
for (const country of countries) {
	const own = perCapita(country)
	raw.set(country.iso, own <= 0 ? FLOOR : Math.max(FLOOR, Math.min(1.2, (own / frontier) ** GAP)))
}

// Нормировка по занятости: сумма (предприятия / эффективность) должна остаться той же,
// что при эффективности в единицу. Это среднее гармоническое.
let plants = 0
let inverse = 0
for (const country of countries) {
	const count = plantsOf.get(country.id) ?? 0
	plants += count
	inverse += count / raw.get(country.iso)
}

const mean = plants / inverse

const out = {}
for (const country of countries) {
	const total = raw.get(country.iso) / mean

	out[country.iso] = {
		skill: Math.round(total ** SHARES.skill * 100),
		tech: Math.round(total ** SHARES.tech * 100),
		condition: Math.round(total ** SHARES.condition * 100),
	}
}

console.log(`гармоническая средняя до нормировки ${mean.toFixed(3)}: занятость мира не меняется
`)

const totalOf = (iso) => {
	const e = out[iso]
	return (e.skill / 100) * (e.tech / 100) * (e.condition / 100)
}

// Сверка: во сколько раз страна отстаёт от США у нас и в жизни.
// Настоящая выработка на работника, тыс. $ в год — та же таблица, что в check-economy.
const real = { USA: 190, DEU: 100, JPN: 100, TWN: 95, CHN: 35, RUS: 20, BRA: 15, NGA: 10, IND: 7 }

console.log(`степень ${GAP}, разбивка ${JSON.stringify(SHARES)}\n`)
console.log('страна  наша эффективность   отстаёт у нас   отстаёт в жизни')
for (const [iso, value] of Object.entries(real)) {
	const ours = totalOf(iso)
	console.log(
		iso.padEnd(8) + ours.toFixed(3).padStart(16) +
		`${(totalOf(FRONTIER) / ours).toFixed(1)}x`.padStart(15) +
		`${(real[FRONTIER] / value).toFixed(1)}x`.padStart(18)
	)
}

const all = Object.keys(out).map(totalOf)
console.log(`\nразброс по миру: ${(Math.max(...all) / Math.min(...all)).toFixed(0)}x`)
console.log(`квалификация ${(Math.max(...Object.values(out).map((e) => e.skill)) /
	Math.min(...Object.values(out).map((e) => e.skill))).toFixed(1)}x, ` +
	`технология ${(Math.max(...Object.values(out).map((e) => e.tech)) /
	Math.min(...Object.values(out).map((e) => e.tech))).toFixed(1)}x, ` +
	`состояние ${(Math.max(...Object.values(out).map((e) => e.condition)) /
	Math.min(...Object.values(out).map((e) => e.condition))).toFixed(1)}x`)

if (!apply) {
	console.log('\nничего не записано, для записи запусти с --apply')
	process.exit(0)
}

const data = {
	note: 'Стартовая эффективность предприятий, в сотых. Выпуск умножается на произведение трёх множителей. Считается tools/gen-efficiency.mjs из подушевого ВВП: завод в модели везде одинаковый, значит вся разница в производительности лежит здесь. Разбивка на три — правило, а не данные: показатели подобраны так, чтобы разброс каждого совпал с настоящим. Дальше каждый заживёт своей жизнью — квалификация от образования, технология от древа, состояние от износа.',
	unit: 'сотые доли, 100 — как у передовой страны',
	byIso: out,
}

fs.writeFileSync(file('data', 'economy', 'efficiency.json'), JSON.stringify(data, null, '\t') + '\n')
console.log('\nзаписано в efficiency.json')
