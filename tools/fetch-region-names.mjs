// Русские названия областей из Natural Earth -> data/ui/regions-ru.json
//
//   node tools/fetch-region-names.mjs
//
// В regions.json имена английские: они пришли из ne_10m_admin_1 и служат ключом сверки
// с данными. Перевод лежит рядом. У самой Natural Earth поле name_ru есть не у всех
// единиц, а у укрупнений по полю region его нет вовсе — что не нашлось, перечислено
// в конце прогона и добавляется вручную в data/ui/regions-extra-ru.json.

import fs from 'node:fs'
import path from 'node:path'

const SOURCE =
	'https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/' +
	'ne_10m_admin_1_states_provinces.geojson'

const root = path.resolve(import.meta.dirname, '..')
const mapDir = path.join(root, 'data', 'map')
const uiDir = path.join(root, 'data', 'ui')

const regions = JSON.parse(fs.readFileSync(path.join(mapDir, 'regions.json'), 'utf8')).regions
const countries = JSON.parse(fs.readFileSync(path.join(mapDir, 'countries.json'), 'utf8')).countries
const countriesRu = JSON.parse(fs.readFileSync(path.join(uiDir, 'countries-ru.json'), 'utf8')).names

const extraPath = path.join(uiDir, 'regions-extra-ru.json')
const extra = fs.existsSync(extraPath) ? JSON.parse(fs.readFileSync(extraPath, 'utf8')).names : {}

console.log('качаю Natural Earth admin-1...')
const res = await fetch(SOURCE)
if (!res.ok) throw new Error(`HTTP ${res.status}`)

const source = JSON.parse(await res.text())
console.log(`единиц в источнике: ${source.features.length}`)

// Один и тот же перевод может прийти из name_ru или из gn_name; берём первый непустой.
const byName = new Map()
for (const feature of source.features) {
	const p = feature.properties
	const ru = p.name_ru || p.gn_name_ru || ''
	if (!ru) continue

	for (const key of [p.name, p.name_en, p.woe_name, p.gn_name]) {
		if (key && !byName.has(key)) byName.set(key, ru)
	}
}

console.log(`нашлось переводов: ${byName.size}`)

// Страны — на случай областей, названных по стране: так делает генератор там, где
// у страны нет деления admin-1.
const countryRuByName = new Map(countries.map((c) => [c.name, countriesRu[c.iso] ?? c.name]))

const names = {}
const missing = []

for (const region of regions) {
	const ru = extra[region.name] ?? byName.get(region.name) ?? countryRuByName.get(region.name)

	if (ru) names[region.name] = ru
	else if (!missing.includes(region.name)) missing.push(region.name)
}

fs.writeFileSync(
	path.join(uiDir, 'regions-ru.json'),
	JSON.stringify(
		{
			note:
				'Русские названия областей по английскому имени из regions.json. Собирается ' +
				'tools/fetch-region-names.mjs из Natural Earth; чего там нет — в regions-extra-ru.json.',
			names,
		},
		null,
		'\t'
	) + '\n'
)

console.log(`переведено ${Object.keys(names).length} из ${regions.length}, без перевода ${missing.length}`)
for (const name of missing.sort()) console.log(`  нет перевода: ${name}`)
