# CapitaModern

Экономико-политический симулятор государства на 2D-карте. Упор на живую красивую
карту: гладкие непрерывно движущиеся границы, война как ползущий фронт, экономика —
глубокая, но без микроменеджмента.

## Документация

- [`00-vision.md`](./docs/00-vision.md) — видение, столпы, чего в игре нет
- [`01-map.md`](./docs/01-map.md) — **карта и границы, ключевой документ**
- [`02-war.md`](./docs/02-war.md) — фронт, давление, направления атак
- [`03-industry.md`](./docs/03-industry.md) — промзоны, предприятия, производительность
- [`04-fx.md`](./docs/04-fx.md) — события на карте и визуальные эффекты
- [`05-architecture.md`](./docs/05-architecture.md) — проекты, тик, данные, сейвы
- [`06-roadmap.md`](./docs/06-roadmap.md) — фазы и нерешённые вопросы
- [`07-economy.md`](./docs/07-economy.md) — категории товаров, кто строит предприятия

## Стек

Godot 4.7 (.NET) + C#, рендерер GL Compatibility, .NET 8.

```
CapitaModern.csproj        Godot-проект (вид)
scenes/, scripts/          сцены, шейдеры, ноды
src/CapitaModern.Core/     ядро симуляции, чистый C# без Godot
src/CapitaModern.Headless/ консольный прогон симуляции
data/map/                  поле владения, страны, города, месторождения, палитра
data/economy/              товары и постройки
assets/icons/              иконки товаров и построек (game-icons.net, CC BY 3.0)
tools/                     генераторы данных и проверки
```

## Данные

| Файл | Что | Размер |
|---|---|---|
| `data/map/world.bin` | владелец каждой ячейки, 2048×800 | 1.6 МБ |
| `data/map/countries.json` | 200 стран: имя, код, население, ВВП, площадь | 30 КБ |
| `data/map/cities.json` | 7288 городов для расселения населения | 850 КБ |
| `data/map/deposits.json` | 61 месторождение с координатами | 12 КБ |
| `data/economy/goods.json` | 20 товаров | 3 КБ |
| `data/economy/buildings.json` | 24 постройки: рецепты, рабочие, стоимость | 10 КБ |

Генерация и проверка:

```bash
node tools/gen-world.mjs <ne_50m_admin_0_countries.geojson>
node tools/gen-cities.mjs <ne_10m_populated_places_simple.geojson>
node tools/fetch-icons.mjs
node tools/check-data.mjs        # ссылки между файлами и наличие иконок
node tools/check-deposits.mjs    # месторождения на суше и в своей стране
```

## Карта

`data/map/world.bin` уже сгенерирован и лежит в репозитории. Пересобрать (нужен
[ne_50m_admin_0_countries.geojson](https://github.com/nvkelso/natural-earth-vector)):

```bash
node tools/gen-world.mjs <путь-к-geojson> --preview preview.png
```

## Запуск

Игра — F5 в редакторе Godot (главная сцена `scenes/Main.tscn`).

Симуляция без редактора:

```bash
dotnet run --project src/CapitaModern.Headless -- 250
```

Сборка целиком:

```bash
dotnet build CapitaModern.sln
```
