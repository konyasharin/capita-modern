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

## Стек

Godot 4.7 (.NET) + C#, рендерер GL Compatibility, .NET 8.

```
CapitaModern.csproj        Godot-проект (вид)
src/CapitaModern.Core/     ядро симуляции, чистый C# без Godot
src/CapitaModern.Headless/ консольный прогон симуляции
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
