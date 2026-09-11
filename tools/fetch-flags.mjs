// Скачивает флаги стран -> assets/flags/<ISO3>.svg.
//
//   node tools/fetch-flags.mjs
//
// Берутся все коды ISO 3166-1, а не только наши двести: за партию страны появляются и
// исчезают, и флаг должен найтись для любой. Плюс горстка непризнанных, которые есть у
// нас на карте — им коды ISO не выданы, и подбираются они вручную.
//
// Источник: github.com/hampusborgos/country-flags, общественное достояние.

import fs from 'node:fs'
import path from 'node:path'

const FLAGS = 'https://raw.githubusercontent.com/hampusborgos/country-flags/main/svg'
const CODES = 'https://raw.githubusercontent.com/lukes/ISO-3166-Countries-with-Regional-Codes/master/all/all.json'

/** Кого нет в ISO. Косово ходит под условным XK, Западная Сахара под EH. */
const EXTRA = {
	KOS: 'xk',
	SAH: 'eh',
	SDS: 'ss',   // у нас Южный Судан идёт под SDS, в ISO он SSD
}

/** У кого флага нет нигде: рисуем сами, чтобы в игре не зияла дыра. */
const DRAWN = {
	KAS: ['#1a4f8a', '#f0f0f0'],
	CYN: ['#e30a17', '#ffffff'],
	SOL: ['#4189dd', '#ffffff'],
}

const root = path.resolve(import.meta.dirname, '..')
const out = path.join(root, 'assets', 'flags')
fs.mkdirSync(out, { recursive: true })

const countries = JSON.parse(
	fs.readFileSync(path.join(root, 'data', 'map', 'countries.json'), 'utf8')
).countries

const iso = await (await fetch(CODES)).json()
const two = new Map(iso.map((c) => [c['alpha-3'], c['alpha-2'].toLowerCase()]))

for (const [three, code] of Object.entries(EXTRA)) two.set(three, code)

const ours = new Set(countries.map((c) => c.iso))
const wanted = new Set([...two.keys(), ...ours])

let saved = 0
let drawn = 0
const missing = []

for (const three of [...wanted].sort()) {
	const file = path.join(out, `${three}.svg`)
	if (fs.existsSync(file)) {
		saved++
		continue
	}

	if (DRAWN[three]) {
		fs.writeFileSync(file, stripes(DRAWN[three]))
		drawn++
		continue
	}

	const code = two.get(three)
	if (!code) {
		missing.push(three)
		continue
	}

	const response = await fetch(`${FLAGS}/${code}.svg`)
	if (!response.ok) {
		missing.push(`${three} (${code}: ${response.status})`)
		continue
	}

	fs.writeFileSync(file, await response.text())
	saved++
}

/** Простое полотнище в две полосы: заглушка для тех, чьего флага нет в наборе. */
function stripes([top, bottom]) {
	return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 60 40">` +
		`<rect width="60" height="20" fill="${top}"/>` +
		`<rect y="20" width="60" height="20" fill="${bottom}"/></svg>\n`
}

console.log(`флагов ${saved}, нарисовано вручную ${drawn}`)
if (missing.length) console.log(`не нашлось (${missing.length}): ${missing.join(', ')}`)

const без = countries.filter((c) => !fs.existsSync(path.join(out, `${c.iso}.svg`)))
console.log(без.length ? `на карте без флага: ${без.map((c) => c.iso).join(', ')}` : 'на карте все с флагами')
