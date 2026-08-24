// Раскладывает стартовую промышленность по регионам -> data/economy/start-industry.json.
//
//   node tools/gen-industry.mjs
//
// Правило одно для всех типов: сколько предприятий у страны — из реальных долей
// мирового производства (data/economy/production.json), а где именно внутри страны —
// из географии. Месторождения задают только место и потолок роста, но не количество:
// у Венесуэлы огромные запасы нефти и мало промыслов, у Норвегии наоборот.

import fs from 'node:fs'
import path from 'node:path'

const root = path.resolve(import.meta.dirname, '..')
const read = (...p) => JSON.parse(fs.readFileSync(path.join(root, ...p), 'utf8'))

const production = read('data', 'economy', 'production.json')
const buildings = read('data', 'economy', 'buildings.json')
const countries = read('data', 'map', 'countries.json').countries
const regions = read('data', 'map', 'regions.json').regions

const byIso = new Map(countries.map((c) => [c.iso, c]))
const byId = new Map(countries.map((c) => [c.id, c]))
const depositOf = new Map(buildings.map((b) => [b.type, b.requiresDeposit]))

const regionsOf = new Map()
for (const region of regions) {
	if (!regionsOf.has(region.country)) regionsOf.set(region.country, [])
	regionsOf.get(region.country).push(region)
}

// Коды стран в таблице должны существовать: опечатка иначе молча съест долю.
const unknown = new Set()
for (const [type, info] of Object.entries(production.types)) {
	for (const iso of Object.keys(info.shares)) if (!byIso.has(iso)) unknown.add(`${type}: ${iso}`)
}
if (unknown.size) {
	console.error(`неизвестные коды стран:\n  ${[...unknown].join('\n  ')}`)
	process.exit(1)
}

// Метод наибольших остатков: целые части по весам, остаток — самым обделённым.
// Без него суммы по странам и регионам разъедутся с мировым итогом.
function share(total, weights) {
	const sum = weights.reduce((a, b) => a + b, 0)
	if (sum <= 0 || total <= 0) return weights.map(() => 0)

	const exact = weights.map((w) => (total * w) / sum)
	const base = exact.map(Math.floor)
	let left = total - base.reduce((a, b) => a + b, 0)

	const order = exact.map((v, i) => [v - base[i], i]).sort((a, b) => b[0] - a[0])
	for (let k = 0; k < order.length && left > 0; k++, left--) base[order[k][1]]++

	return base
}

const perRegion = new Map()
const fallback = new Set()
const perType = {}

for (const [type, info] of Object.entries(production.types)) {
	const listed = Object.keys(info.shares)
	const listedSet = new Set(listed)
	const tailCountries = countries.filter((c) => !listedSet.has(c.iso) && c.population > 0)

	// Доли из таблицы масштабируются под (100 - tail), хвост раздаётся по населению.
	const listedSum = listed.reduce((s, iso) => s + info.shares[iso], 0)
	const tailShare = info.tail ?? 0
	const scale = (100 - tailShare) / listedSum

	const targets = [
		...listed.map((iso) => ({ country: byIso.get(iso), weight: info.shares[iso] * scale })),
		...tailCountries.map((c) => ({
			country: c,
			weight: (tailShare * c.population) / tailCountries.reduce((s, x) => s + x.population, 0),
		})),
	]

	const counts = share(info.world, targets.map((t) => t.weight))
	const good = depositOf.get(type)
	let placed = 0

	for (let i = 0; i < targets.length; i++) {
		const count = counts[i]
		if (count === 0) continue

		const list = regionsOf.get(targets[i].country.id) ?? []
		if (!list.length) continue

		// Добыча идёт к месторождениям, всё остальное — к людям. Если страна
		// производит сырьё, которого нет в наших месторождениях, ставим по населению.
		let weights = good ? list.map((r) => r.deposits[good] ?? 0) : list.map((r) => r.population)
		if (weights.every((w) => w === 0)) {
			if (good) fallback.add(`${type}: ${targets[i].country.iso}`)
			weights = list.map((r) => r.population)
		}

		const spread = share(count, weights)

		for (let k = 0; k < list.length; k++) {
			if (spread[k] === 0) continue
			if (!perRegion.has(list[k].id)) perRegion.set(list[k].id, {})
			const bag = perRegion.get(list[k].id)
			bag[type] = (bag[type] ?? 0) + spread[k]
			placed += spread[k]
		}
	}

	perType[type] = placed
}

const out = {
	note: 'Стартовая промышленность по регионам. Считается tools/gen-industry.mjs из долей мирового производства и географии; числа приближённые, под балансировку.',
	regions: Object.fromEntries([...perRegion.entries()].sort((a, b) => a[0] - b[0])),
}

fs.writeFileSync(
	path.join(root, 'data', 'economy', 'start-industry.json'),
	JSON.stringify(out, null, '\t') + '\n'
)

const total = Object.values(perType).reduce((a, b) => a + b, 0)
console.log(`предприятий ${total} в ${perRegion.size} регионах из ${regions.length}`)

const byCountry = new Map()
for (const [id, bag] of perRegion) {
	const country = regions.find((r) => r.id === +id).country
	const n = Object.values(bag).reduce((a, b) => a + b, 0)
	byCountry.set(country, (byCountry.get(country) ?? 0) + n)
}

const top = [...byCountry.entries()].sort((a, b) => b[1] - a[1]).slice(0, 8)
console.log(`крупнейшие: ${top.map(([id, n]) => `${byId.get(id).iso} ${n}`).join(', ')}`)

const lost = Object.entries(production.types).filter(([t, i]) => perType[t] !== i.world)
if (lost.length) console.log(`не разложено полностью: ${lost.map(([t]) => t).join(', ')}`)
if (fallback.size) console.log(`без месторождений, поставлено по населению: ${[...fallback].join(', ')}`)
