// Валюты стран: курс, код и знак -> data/economy/currencies.json
//
//   node tools/fetch-rates.mjs
//
// Показатель PA.NUS.FCRF Всемирного банка — средний за год официальный курс, единиц
// местной валюты за доллар. Это единственный ряд, который покрывает все страны разом;
// биржевые источники знают три десятка валют, а нам нужно двести.
//
// У кого своей валюты нет (Панама, Эквадор, Зимбабве в те годы) курс выходит ровно 1 —
// они и живут на доллар. У кого данных нет вовсе, ставится единица, и такие страны
// перечисляются в конце прогона.
//
// Код и знак валюты берутся из справочника mledoze/countries: знак нужен подписям в
// интерфейсе, а код — тем валютам, у которых своего знака нет.

import fs from 'node:fs'
import path from 'node:path'

const RATES =
	'https://api.worldbank.org/v2/country/all/indicator/PA.NUS.FCRF' +
	'?date=2020&format=json&per_page=400'

const CURRENCIES = 'https://raw.githubusercontent.com/mledoze/countries/master/dist/countries.json'

// Кого нет у Всемирного банка: непризнанные, закрытые и те, кто живёт на чужую валюту.
// Средний курс 2020 года; у Венесуэлы он условен — там была гиперинфляция, и курс менялся
// в разы за месяцы.
const RATES_BY_HAND = {
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

// Непризнанных нет и в справочнике валют: живут они на чужие деньги или на свои.
const CODES_BY_HAND = {
	KAS: { code: 'INR', symbol: '₹', name: 'Indian rupee' },
	CYN: { code: 'TRY', symbol: '₺', name: 'Turkish lira' },
	SOL: { code: 'SLSH', symbol: 'Sl', name: 'Somaliland shilling' },
	SAH: { code: 'MAD', symbol: 'MAD', name: 'Moroccan dirham' },
	FSM: { code: 'USD', symbol: '$', name: 'United States dollar' },
	KOS: { code: 'EUR', symbol: '€', name: 'Euro' },
	SDS: { code: 'SSP', symbol: '£', name: 'South Sudanese pound' },
}

const root = path.resolve(import.meta.dirname, '..')
const countries = JSON.parse(
	fs.readFileSync(path.join(root, 'data', 'map', 'countries.json'), 'utf8'),
).countries

console.log('качаю курсы Всемирного банка...')
const ratesRes = await fetch(RATES)
if (!ratesRes.ok) throw new Error(`HTTP ${ratesRes.status}`)

const [, rows] = await ratesRes.json()

const byIso = new Map()
for (const row of rows ?? []) {
	if (row.value === null || !row.countryiso3code) continue

	byIso.set(row.countryiso3code, row.value)
}

console.log('качаю справочник валют...')
const listRes = await fetch(CURRENCIES)
if (!listRes.ok) throw new Error(`HTTP ${listRes.status}`)

const money = new Map()
for (const entry of await listRes.json()) {
	const [code, about] = Object.entries(entry.currencies ?? {})[0] ?? []
	if (!code) continue

	money.set(entry.cca3, { code, symbol: about.symbol || code, name: about.name })
}

const currencies = {}
const missingRate = []
const missingCode = []

for (const country of countries) {
	const rate = byIso.get(country.iso) ?? RATES_BY_HAND[country.iso]
	if (rate === undefined) missingRate.push(`${country.iso} ${country.name}`)

	const about = money.get(country.iso) ?? CODES_BY_HAND[country.iso]
	if (about === undefined) missingCode.push(`${country.iso} ${country.name}`)

	currencies[country.iso] = {
		// Три значащие цифры: курс в игре всё равно поедет с первого же тика, а длинные
		// хвосты только раздувают файл.
		rate: Number((rate ?? 1).toPrecision(3)),
		code: about?.code ?? 'USD',
		symbol: about?.symbol ?? '$',
		name: about?.name ?? 'United States dollar',
	}
}

const out = path.join(root, 'data', 'economy', 'currencies.json')
fs.writeFileSync(
	out,
	JSON.stringify(
		{
			note:
				'Валюта страны: курс — единиц за доллар, средний за 2020 год (Всемирный банк); ' +
				'код и знак — справочник mledoze/countries.',
			currencies,
		},
		null,
		'\t',
	) + '\n',
)

console.log(`записано ${Object.keys(currencies).length} валют в ${path.relative(root, out)}`)
if (missingRate.length > 0) console.log(`нет курса у ${missingRate.length}: ${missingRate.join(', ')}`)
if (missingCode.length > 0) console.log(`нет кода у ${missingCode.length}: ${missingCode.join(', ')}`)
