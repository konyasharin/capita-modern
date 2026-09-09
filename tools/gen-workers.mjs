// Пересчитывает optimalWorkers из реальной занятости: занятость отрасли делится
// на мировое число предприятий этого типа.
//
//   node tools/gen-workers.mjs

import fs from 'node:fs'
import path from 'node:path'

const root = path.resolve(import.meta.dirname, '..')
const read = (...p) => JSON.parse(fs.readFileSync(path.join(root, ...p), 'utf8'))

const buildings = read('data', 'economy', 'buildings.json')
const production = read('data', 'economy', 'production.json').types
const employment = read('data', 'economy', 'employment.json').workers

let changed = 0

for (const building of buildings) {
	const total = employment[building.type]
	if (total === undefined) throw new Error(`${building.type}: нет данных о занятости`)

	const workers = Math.round(total / production[building.type].world)
	if (workers !== building.optimalWorkers) changed++
	building.optimalWorkers = workers
}

fs.writeFileSync(
	path.join(root, 'data', 'economy', 'buildings.json'),
	JSON.stringify(buildings, null, '\t') + '\n'
)

const world = buildings.reduce((sum, b) => sum + b.optimalWorkers * production[b.type].world, 0)
console.log(`optimalWorkers пересчитан у ${changed} типов, занято в мире ${(world / 1e6).toFixed(0)} млн`)
