// Курсы валют на 2020 год из Всемирного банка -> data/economy/exchange-rates.json
//
//   node tools/fetch-rates.mjs
//
// Показатель PA.NUS.FCRF — средний за год официальный курс, единиц местной валюты за
// доллар. Это единственный ряд, который покрывает все страны разом; биржевые источники
// знают три десятка валют, а нам нужно двести.
//
// У кого своей валюты нет (Панама, Эквадор, Зимбабве в те годы) курс выходит ровно 1 —
// они и живут на доллар. У кого данных нет вовсе, ставится единица, и такие страны
// перечисляются в конце прогона.

import fs from 'node:fs'
import path from 'node:path'

const SOURCE =
	'https://api.worldbank.org/v2/country/all/indicator/PA.NUS.FCRF' +
	'?date=2020&format=json&per_page=400'

// Кого нет у Всемирного банка: непризнанные, закрытые и те, кто живёт на чужую валюту.
// Средний курс 2020 года; у Венесуэлы он условен — там была гиперинфляция, и курс менялся
// в разы за месяцы.
const BY_HAND = {
	AND: 0.876, // евро
	KOS: 0.876, // евро
	VAT: 0.876, // евро
	CYN: 7.01, // турецкая лира
	SAH: 9.5, // марокканский дирхам
	KAS: 74.1, // индийская рупия
	TWN: 29.5,
	TKM: 3.5,
	CUB: 24,
	PRK: 900, // официальный курс КНДР, рыночный много выше
	SOM: 580,
	SOL: 8500,
	SDS: 165,
	VEN: 236000,
}

const root = path.resolve(import.meta.dirname, '..')
const countries = JSON.parse(
	fs.readFileSync(path.join(root, 'data', 'map', 'countries.json'), 'utf8'),
).countries

console.log('качаю курсы Всемирного банка...')
const res = await fetch(SOURCE)
if (!res.ok) throw new Error(`HTTP ${res.status}`)

const [, rows] = await res.json()

const byIso = new Map()
for (const row of rows ?? []) {
	if (row.value === null || !row.countryiso3code) continue

	byIso.set(row.countryiso3code, row.value)
}

const rates = {}
const missing = []

for (const country of countries) {
	const rate = byIso.get(country.iso) ?? BY_HAND[country.iso]
	if (rate === undefined) {
		missing.push(`${country.iso} ${country.name}`)
		rates[country.iso] = 1
		continue
	}

	// Три значащие цифры: курс в игре всё равно поедет с первого же тика, а длинные
	// хвосты только раздувают файл.
	rates[country.iso] = Number(rate.toPrecision(3))
}

const out = path.join(root, 'data', 'economy', 'exchange-rates.json')
fs.writeFileSync(
	out,
	JSON.stringify({ note: 'Единиц местной валюты за доллар, средний курс 2020 года, Всемирный банк.', rates }, null, '\t') + '\n',
)

console.log(`записано ${Object.keys(rates).length} курсов в ${path.relative(root, out)}`)
if (missing.length > 0) {
	console.log(`нет данных у ${missing.length}: ${missing.join(', ')}`)
}
