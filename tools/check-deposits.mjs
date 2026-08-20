// Проверяет data/map/deposits.json по сгенерированной карте:
// точка должна попадать на сушу и в ту страну, что указана в поле country.
//
//   node tools/check-deposits.mjs [--snap]
//
// --snap: подвинуть точки, оказавшиеся в море, на ближайшую ячейку своей страны
//         и переписать deposits.json.

import fs from 'node:fs'
import path from 'node:path'

const root = path.resolve(import.meta.dirname, '..')
const mapDir = path.join(root, 'data', 'map')

const bin = fs.readFileSync(path.join(mapDir, 'world.bin'))
if (bin.toString('ascii', 0, 4) !== 'CMW1') throw new Error('world.bin: не тот формат')

const WIDTH = bin.readInt32LE(4)
const HEIGHT = bin.readInt32LE(8)
const owner = bin.subarray(12)

const LAT_TOP = 84
const DEG_PER_CELL = 360 / WIDTH
const LAT_SPAN = HEIGHT * DEG_PER_CELL

const meta = JSON.parse(fs.readFileSync(path.join(mapDir, 'countries.json'), 'utf8'))
const byId = new Map(meta.countries.map((c) => [c.id, c]))
const byIso = new Map(meta.countries.map((c) => [c.iso, c]))

const file = JSON.parse(fs.readFileSync(path.join(mapDir, 'deposits.json'), 'utf8'))

const toCell = (lon, lat) => [
	Math.min(WIDTH - 1, Math.max(0, Math.floor(((lon + 180) / 360) * WIDTH))),
	Math.min(HEIGHT - 1, Math.max(0, Math.floor(((LAT_TOP - lat) / LAT_SPAN) * HEIGHT))),
]

// Ближайшая ячейка нужной страны: месторождения на шельфе иначе попадают в океан.
function nearestOf(x0, y0, iso, limit = 40) {
	const want = byIso.get(iso)?.id
	let best = null

	for (let r = 1; r <= limit && !best; r++) {
		for (let dy = -r; dy <= r; dy++) {
			for (let dx = -r; dx <= r; dx++) {
				if (Math.max(Math.abs(dx), Math.abs(dy)) !== r) continue
				const x = x0 + dx
				const y = y0 + dy
				if (x < 0 || y < 0 || x >= WIDTH || y >= HEIGHT) continue
				if (owner[y * WIDTH + x] !== want) continue
				const d = dx * dx + dy * dy
				if (!best || d < best.d) best = { x, y, d, r }
			}
		}
	}
	return best
}

const snap = process.argv.includes('--snap')
let ok = 0
const problems = []

for (const dep of file.deposits) {
	const [x, y] = toCell(dep.lon, dep.lat)
	const id = owner[y * WIDTH + x]
	const here = byId.get(id)

	if (here && here.iso === dep.country) {
		ok++
		continue
	}

	const near = nearestOf(x, y, dep.country)
	const where = id === 0 ? 'море' : here ? here.iso : `id ${id}`

	problems.push({ dep, where, near })

	if (snap && near) {
		dep.lon = +(((near.x + 0.5) * DEG_PER_CELL) - 180).toFixed(3)
		dep.lat = +(LAT_TOP - (near.y + 0.5) * DEG_PER_CELL).toFixed(3)
	}
}

console.log(`карта ${WIDTH}x${HEIGHT}, месторождений ${file.deposits.length}, на месте ${ok}`)

for (const p of problems) {
	const fix = p.near ? `ближайшая ${p.dep.country} в ${p.near.r} ячейках` : `${p.dep.country} рядом не найдена`
	console.log(`  ${p.dep.good} ${p.dep.name}: ожидалось ${p.dep.country}, попало в ${p.where} — ${fix}`)
}

if (snap && problems.length) {
	fs.writeFileSync(path.join(mapDir, 'deposits.json'), JSON.stringify(file, null, '\t') + '\n')
	console.log(`подвинуто: ${problems.filter((p) => p.near).length}`)
}
