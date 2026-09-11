// Скачивает иконки товаров и построек с game-icons.net (репозиторий game-icons/icons)
// и нормализует их под проект: без чёрной подложки, цвет — currentColor.
//
//   node tools/fetch-icons.mjs
//
// Лицензия исходников CC BY 3.0, авторы перечислены в assets/icons/ATTRIBUTION.md.

import fs from 'node:fs'
import path from 'node:path'

const RAW = 'https://raw.githubusercontent.com/game-icons/icons/master'

const GOODS = {
	coal: 'delapouite/coal-pile',
	oil: 'skoll/oil-drum',
	gas: 'carl-olsen/flame',
	'iron-ore': 'faithtoken/minerals',
	'copper-ore': 'delapouite/gold-nuggets',
	bauxite: 'delapouite/stone-pile',
	uranium: 'lorc/radioactive',
	'rare-earth': 'lorc/crystal-cluster',
	timber: 'delapouite/wood-pile',
	agriculture: 'delapouite/grain-bundle',
	electricity: 'lorc/arcing-bolt',
	fuel: 'delapouite/jerrycan',
	metals: 'lorc/metal-bar',
	chemicals: 'lorc/bubbling-flask',
	materials: 'delapouite/brick-pile',
	components: 'delapouite/jack-plug',
	electronics: 'lorc/microchip',
	microelectronics: 'lorc/processor',
	food: 'delapouite/meal',
	'consumer-goods': 'delapouite/clothes',
	medicine: 'delapouite/medicine-pills',
	services: 'delapouite/shaking-hands',
	armour: 'cathelineau/great-war-tank',
	artillery: 'quoting/field-gun',
	'small-arms': 'skoll/ak47',
	ammunition: 'sbed/ammo-box',
	'tactical-drones': 'delapouite/delivery-drone',
	'strike-drones': 'lord-berandas/light-fighter',
	missiles: 'lorc/missile-swarm',
	aircraft: 'delapouite/jet-fighter',
	'air-defence': 'delapouite/missile-launcher',
	'electronic-warfare': 'lorc/aerial-signal',
}

const BUILDINGS = {
	'coal-mine': 'delapouite/mine-wagon',
	'oil-rig': 'delapouite/oil-rig',
	'gas-field': 'delapouite/valve',
	'iron-mine': 'delapouite/mine-truck',
	'copper-mine': 'delapouite/miner',
	'bauxite-mine': 'caro-asercion/bucket-wheel-excavator',
	'uranium-mine': 'delapouite/mining-helmet',
	'rare-earth-mine': 'lorc/mining',
	'logging-camp': 'delapouite/chainsaw',
	farm: 'delapouite/farm-tractor',
	'coal-plant': 'delapouite/chimney',
	'gas-plant': 'delapouite/power-generator',
	'nuclear-plant': 'delapouite/nuclear-plant',
	'hydro-plant': 'delapouite/dam',
	refinery: 'delapouite/refinery',
	'steel-mill': 'delapouite/foundry-bucket',
	smelter: 'lorc/anvil',
	'chemical-plant': 'caro-asercion/test-tube-rack',
	'materials-plant': 'delapouite/concrete-bag',
	'components-plant': 'delapouite/wire-coil',
	'electronics-plant': 'delapouite/cpu',
	'microelectronics-plant': 'lorc/circuitry',
	'food-plant': 'delapouite/canned-fish',
	'consumer-goods-plant': 'delapouite/factory',
	'pharma-plant': 'delapouite/lab-coat',
	'armour-plant': 'skoll/tank-tread',
	'artillery-plant': 'lorc/cannon',
	'small-arms-plant': 'skoll/machine-gun',
	'ammunition-plant': 'lorc/bullets',
	'tactical-drone-plant': 'delapouite/helicopter',
	'strike-drone-plant': 'delapouite/starfighter',
	'missile-plant': 'lorc/missile-pod',
	'aircraft-plant': 'skoll/airplane',
	'air-defence-plant': 'lorc/radar-sweep',
	'electronic-warfare-plant': 'lorc/radar-dish',
	'retail-firm': 'delapouite/shopping-cart',
	'transport-firm': 'delapouite/truck',
	'public-service': 'delapouite/greek-temple',
	'business-firm': 'delapouite/modern-city',
}

/** Значки интерфейса: показатели, вкладки, тревоги. */
const UI = {
	// Верхняя панель
	population: 'delapouite/three-friends',
	employment: 'delapouite/miner',
	output: 'delapouite/upgrade',
	plants: 'delapouite/factory',
	inflation: 'delapouite/price-tag',
	treasury: 'delapouite/money-stack',
	debt: 'delapouite/pay-money',
	rate: 'lorc/scales',

	// Вкладки
	'tab-overview': 'lorc/scroll-unfurled',
	'tab-goods': 'delapouite/cardboard-box',
	'tab-industry': 'delapouite/crane',
	'tab-trade': 'delapouite/cargo-ship',
	'tab-finance': 'delapouite/bank',
	'tab-politics': 'lorc/globe',

	// Тревоги
	'alert-hands': 'lorc/hand',
	'alert-idle': 'lorc/stopwatch',
	'alert-frozen': 'lorc/frozen-orb',
	'alert-default': 'lorc/tombstone',
	'alert-shortage': 'lorc/empty-hourglass',
	'alert-deficit': 'delapouite/receive-money',

	// Прочее
	sound: 'delapouite/speaker',
	mute: 'delapouite/speaker-off',
	close: 'sbed/cancel',
}

const root = path.resolve(import.meta.dirname, '..')

// Чёрная подложка занимает весь холст — в игре она не нужна. Сама иконка делается белой:
// Godot рисует currentColor чёрным, а чёрное не покрасить умножением — цвет задаётся
// через Modulate уже в интерфейсе.
function normalize(svg) {
	return svg
		.replace(/<path d="M0 0h512v512H0z"\s*\/>/g, '')
		.replace(/<path d="M0 0h512v512H0"\s*\/>/g, '')
		.replace(/fill="currentColor"/g, 'fill="#ffffff"')
		.replace(/<svg /, '<svg fill="#ffffff" ')
}

async function grab(group, name, source) {
	const res = await fetch(`${RAW}/${source}.svg`)
	// Не роняем весь прогон: имена на game-icons меняются, проще увидеть список промахов.
	if (!res.ok) {
		missed.push(`${group}/${name} -> ${source}`)
		return null
	}

	const dir = path.join(root, 'assets', 'icons', group)
	fs.mkdirSync(dir, { recursive: true })
	fs.writeFileSync(path.join(dir, `${name}.svg`), normalize(await res.text()))

	return source.split('/')[0]
}

const authors = new Set()
const missed = []
let count = 0

for (const [group, map] of [['goods', GOODS], ['buildings', BUILDINGS], ['ui', UI]]) {
	for (const [name, source] of Object.entries(map)) {
		const author = await grab(group, name, source)
		if (author === null) continue

		authors.add(author)
		count++
	}
	console.log(`${group}: ${Object.keys(map).length}`)
}

for (const miss of missed) console.log(`не нашлась: ${miss}`)

fs.writeFileSync(
	path.join(root, 'assets', 'icons', 'ATTRIBUTION.md'),
	`# Иконки\n\nВзяты с [game-icons.net](https://game-icons.net), лицензия ` +
		`[CC BY 3.0](https://creativecommons.org/licenses/by/3.0/).\n` +
		`Изменения: убрана подложка, цвет заменён на currentColor.\n\n` +
		`Авторы: ${[...authors].sort().join(', ')}.\n\n` +
		`Обновление: \`node tools/fetch-icons.mjs\`\n`
)

console.log(`всего ${count}, авторов ${authors.size}`)
