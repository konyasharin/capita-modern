using CapitaModern.Core.Economy;
using CapitaModern.Core.Loading;
using CapitaModern.Core.World;

string Data(string name) => File.ReadAllText(Path.Combine(RepoPaths.GetRepoRoot(), "data", "economy", name));
string Map(string name) => File.ReadAllText(Path.Combine(RepoPaths.GetRepoRoot(), "data", "map", name));

GameWorld Load() => WorldDataLoader.LoadWorld(
    Map("countries.json"),
    Map("regions.json"),
    Data("buildings.json"),
    Data("start-industry.json"),
    Data("consumption.json"),
    Data("prices.json"),
    Data("reserves.json"),
    Data("goods.json"),
    Data("key-rates.json"),
    File.ReadAllText(Path.Combine(RepoPaths.GetRepoRoot(), "data", "politics", "blocs.json")));

var goods = Enum.GetValues<GoodType>();

// --- А. Что формула делает при постоянном покрытии -------------------------------
Console.WriteLine("=== А. Постоянное покрытие: во сколько раз меняется цена ===");
Console.WriteLine("покрытие  сутки    неделя   месяц    год");

foreach (var coverDays in new[] { 0, 5, 10, 20, 30, 40, 60, 100, 400 })
{
    var prices = new Prices(goods.ToDictionary(good => good, _ => Money.FromWhole(1000)));
    var demand = GoodAmount.FromWhole(100);
    var stock = GoodAmount.FromWhole(100 * coverDays);
    var line = $"{coverDays,4} сут ";

    for (var tick = 1; tick <= 365; tick++)
    {
        prices.MoveFromCover(GoodType.Coal, demand, stock);
        if (tick is 1 or 7 or 30 or 365)
        {
            line += $"  ×{prices.Of(GoodType.Coal).Exact / 1000,-7:F3}";
        }
    }

    Console.WriteLine(line);
}

// --- Б. Сколько суток нужно на известные из жизни движения ------------------------
Console.WriteLine();
Console.WriteLine("=== Б. Сколько суток нашей формуле на настоящий эпизод ===");

(string Name, double Times, int RealDays)[] episodes =
[
    ("газ в Европе 2021, ×11", 11.0, 365),
    ("уголь Newcastle 2021, ×3", 3.0, 365),
    ("литий 2021-22, ×9", 9.0, 400),
    ("нефть Brent зима-весна 2020, ×0.3", 0.3, 90),
    ("нефть Brent 2022, ×1.6", 1.6, 90),
    ("пшеница март 2022, ×1.4", 1.4, 21),
    ("удобрения 2021, ×3", 3.0, 365),
];

foreach (var (name, times, realDays) in episodes)
{
    var prices = new Prices(goods.ToDictionary(good => good, _ => Money.FromWhole(10000)));
    var rising = times > 1;
    var demand = GoodAmount.FromWhole(100);
    // Полный перекос: либо склад пуст, либо спроса нет вовсе.
    var stock = rising ? default : GoodAmount.FromWhole(100);
    var ourDays = 0;

    while (ourDays < 5000)
    {
        var was = prices.Of(GoodType.Coal).Raw;
        prices.MoveFromCover(GoodType.Coal, rising ? demand : default, stock);
        ourDays++;
        var ratio = prices.Of(GoodType.Coal).Exact / 10000;
        if (rising ? ratio >= times : ratio <= times) break;
        if (prices.Of(GoodType.Coal).Raw == was) break;
    }

    Console.WriteLine($"{name,-38} в жизни {realDays,4} сут, у нас {ourDays,4} сут " +
                      $"({(double)realDays / ourDays,5:F1}× медленнее в жизни)");
}

// --- Б2. При каком запасе эпизод воспроизводится за настоящий срок ---------------
Console.WriteLine();
Console.WriteLine("=== Б2. Какое покрытие нужно, чтобы попасть в настоящий срок ===");

foreach (var (name, times, realDays) in episodes)
{
    var best = -1.0;
    var bestGap = double.MaxValue;

    // Перебор покрытия по десятым доли суток: ищем то, при котором за настоящий срок
    // цена уходит ровно во столько раз, во сколько ушла в жизни.
    for (var tenths = 0; tenths <= 4000; tenths++)
    {
        var prices = new Prices(goods.ToDictionary(good => good, _ => Money.FromWhole(100000)));
        var demand = GoodAmount.FromWhole(10);
        var stock = new GoodAmount(GoodAmount.FromWhole(10).Raw * tenths / 10);

        for (var day = 0; day < realDays; day++) prices.MoveFromCover(GoodType.Coal, demand, stock);

        var gap = Math.Abs(prices.Of(GoodType.Coal).Exact / 100000 - times);
        if (gap < bestGap) (bestGap, best) = (gap, tenths / 10.0);
    }

    Console.WriteLine($"{name,-38} запас на {best,5:F1} сут (норма — {Prices.TargetCoverDays})");
}

// --- В. Год настоящего мира ------------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== В. Год настоящего мира ===");

var world = Load();
var simulation = new Simulation(world);
var startMoney = world.Countries.Sum(c => c.State.Treasury.Reserves.Value.Exact);

// Реальный ВВП считается в постоянных ценах: иначе подорожание не отличить от роста.
var constant = new Prices(goods.ToDictionary(good => good, world.Market.Prices.Of));

var watched = new[] { "CHN", "USA", "IND", "DEU", "RUS", "JPN", "BRA" }
    .Select(iso => world.Countries.First(country => country.Iso == iso))
    .ToArray();
var startPrices = watched.ToDictionary(
    country => country.Iso,
    country => goods.ToDictionary(good => good, country.State.Prices.Of));

var realGdp = world.Countries.ToDictionary(country => country.Id, _ => default(Money));
var exports = world.Countries.ToDictionary(country => country.Id, _ => default(Money));
var imports = world.Countries.ToDictionary(country => country.Id, _ => default(Money));

void CountRails(int day)
{
    var atRail = 0;
    var sane = 0;
    foreach (var country in world.Countries)
    {
        foreach (var good in goods)
        {
            var times = country.State.Prices.Of(good).Exact / country.State.Prices.StartOf(good).Exact;
            if (times >= Prices.MaxSwingTimes || times <= 1.0 / Prices.MaxSwingTimes) atRail++;
            if (times is > 0.5 and < 2.0) sane++;
        }
    }

    var total = world.Countries.Count * goods.Length;
    Console.WriteLine($"  день {day,3}: на рельсах {100.0 * atRail / total,5:F1}%, " +
                      $"в пределах вдвое от старта {100.0 * sane / total,5:F1}%");
}

Console.WriteLine("Куда уезжают цены:");
var clock = new System.Diagnostics.Stopwatch();
for (var tick = 1; tick <= 365; tick++)
{
    clock.Start();
    simulation.Tick();
    clock.Stop();

    foreach (var country in world.Countries)
    {
        realGdp[country.Id] += simulation.ValueAddedOf(country.Id, constant);
        exports[country.Id] += simulation.ExportsOf(country.Id);
        imports[country.Id] += simulation.ImportsOf(country.Id);
    }

    if (tick is 1 or 7 or 30 or 90 or 365) CountRails(tick);
}

Console.WriteLine($"  тик: {clock.Elapsed.TotalMilliseconds / 365:F2} мс");

Console.WriteLine();
Console.WriteLine("Страна   ВВП год 1   в жизни    экспорт   в жизни    сальдо   в жизни");
(string Iso, double Gdp, double Export, double Balance)[] real =
[
    ("CHN", 14.70, 2.590, 0.534), ("USA", 21.00, 1.425, -0.982), ("IND", 2.70, 0.276, -0.098),
    ("DEU", 3.90, 1.380, 0.209), ("RUS", 1.49, 0.333, 0.094), ("JPN", 5.06, 0.641, 0.006),
    ("BRA", 1.45, 0.209, 0.043),
];

foreach (var (iso, gdp, export, balance) in real)
{
    var id = world.Countries.First(c => c.Iso == iso).Id;
    var ourBalance = (exports[id] - imports[id]).Exact / 1e9;
    Console.WriteLine($"{iso}   {realGdp[id].Exact / 1e9,9:F2} {gdp,9:F2}   {exports[id].Exact / 1e9,8:F2} " +
                      $"{export,9:F2}  {ourBalance,8:F2} {balance,9:F2}");
}

var worldGdp = realGdp.Values.Aggregate(default(Money), (a, b) => a + b).Exact / 1e9;
var worldExport = exports.Values.Aggregate(default(Money), (a, b) => a + b).Exact / 1e9;
Console.WriteLine();
Console.WriteLine($"Мир: реальный ВВП {worldGdp:F2} трлн (материальное производство в жизни ~20 трлн)");
Console.WriteLine($"     товарный экспорт {worldExport:F2} трлн (в жизни 17.6 трлн)");
Console.WriteLine($"     денег в мире {world.Countries.Sum(c => c.State.Treasury.Reserves.Value.Exact) / 1e9:F2} трлн " +
                  $"(на старте {startMoney / 1e9:F2})");

// --- Г. Что осталось на рельсах и почему ------------------------------------------
Console.WriteLine();
Console.WriteLine("=== Г. Товары на границе коридора ===");

foreach (var good in goods)
{
    var high = world.Countries.Count(c => c.State.Prices.Of(good).Exact >= c.State.Prices.StartOf(good).Exact * Prices.MaxSwingTimes);
    var low = world.Countries.Count(c => c.State.Prices.Of(good).Exact <= c.State.Prices.StartOf(good).Exact / Prices.MaxSwingTimes);
    if (high + low < 20) continue;

    var made = world.Regions.SelectMany(r => r.BuildingsCount)
        .Any(pair => world.Buildings[pair.Key].Outputs.ContainsKey(good));
    Console.WriteLine($"{good,-18} у потолка {high,3}, у пола {low,3} стран" +
                      (made ? "" : "   <- в мире вообще нет такого производства"));
}

// --- Д. Что мир производит против того, что заказывает ----------------------------
Console.WriteLine();
Console.WriteLine("=== Д. Выпуск против заказа, за сутки ===");
Console.WriteLine("товар               выпуск      заказ   покрытие");

foreach (var good in goods.OrderBy(good =>
{
    var demand = simulation.WorldDemandOf(good).Exact;
    return demand == 0 ? double.MaxValue : simulation.WorldOutputOf(good).Exact / demand;
}))
{
    var made = simulation.WorldOutputOf(good).Exact;
    var asked = simulation.WorldDemandOf(good).Exact;
    var ratio = asked == 0 ? "заказа нет" : $"{made / asked,7:P0}";
    Console.WriteLine($"{good,-18} {made,10:F0} {asked,10:F0}   {ratio}");
}

// --- Е. Пять лет: не стекутся ли деньги к экспортёрам -----------------------------
Console.WriteLine();
Console.WriteLine("=== Е. Пять лет ===");
Console.WriteLine("год   реальный ВВП   на рельсах   внешний долг   нагрузка >200%   валюта вдвое");

void Report(int year, double gdp)
{
    var rails = world.Countries.Sum(c => goods.Count(good =>
    {
        var times = c.State.Prices.Of(good).Exact / c.State.Prices.StartOf(good).Exact;
        return times >= Prices.MaxSwingTimes || times <= 1.0 / Prices.MaxSwingTimes;
    }));

        var weak = world.Countries.Count(c => c.ExchangeRate > Money.FromWhole(2));
    var debt = world.Countries.Sum(c => c.State.Treasury.Debt.Owed().Exact) / 1e9;
    var heavy = world.Countries.Count(c =>
        c.State.Treasury.Debt.BurdenToExports(exports[c.Id] / Math.Max(year, 1)) > 200);

    Console.WriteLine($"{year,3} {gdp,14:F2} трлн {100.0 * rails / (world.Countries.Count * goods.Length),9:F1}% " +
                      $"{debt,10:F2} трлн {heavy,14} {weak,14}");
}

Report(1, worldGdp);

for (var year = 2; year <= 5; year++)
{
    var yearGdp = default(Money);
    for (var tick = 0; tick < 365; tick++)
    {
        simulation.Tick();
        foreach (var country in world.Countries)
        {
            yearGdp += simulation.ValueAddedOf(country.Id, constant);
            exports[country.Id] += simulation.ExportsOf(country.Id);
        }
    }

    Report(year, yearGdp.Exact / 1e9);
}

Console.WriteLine();
Console.WriteLine("Крупнейшие должники через пять лет:");
foreach (var country in world.Countries.OrderByDescending(c => c.State.Treasury.Debt.Owed().Raw).Take(6))
{
    var burden = country.State.Treasury.Debt.BurdenToExports(exports[country.Id] / 5);
    Console.WriteLine($"  {country.Iso} долг {country.State.Treasury.Debt.Owed().Exact / 1e9,6:F2} трлн, " +
                      $"нагрузка {(burden == int.MaxValue ? "без экспорта" : burden + "%"),12}, " +
                      $"курс x{country.ExchangeRate.Exact:F2}");
}

Console.WriteLine();
Console.WriteLine("Кто раздал в долг больше всех (плавающие ставки идут за ключевой):");
var lentBy = world.Countries.ToDictionary(c => c.Id, _ => 0.0);
foreach (var borrower in world.Countries)
{
    foreach (var loan in borrower.State.Treasury.Debt.Loans)
    {
        if (loan.Lender is { } id) lentBy[id] += loan.Principal.Exact;
    }
}

foreach (var (id, sum) in lentBy.OrderByDescending(p => p.Value).Take(5))
{
    var lender = world.CountryById(id);
    Console.WriteLine($"  {lender.Iso} {sum / 1e9,6:F2} трлн, ключевая {lender.KeyRate / 100.0:F2}%");
}

Console.WriteLine();
Console.WriteLine("Кто держит деньги через пять лет:");
foreach (var country in world.Countries.OrderByDescending(c => c.State.Treasury.Reserves.Value.Raw).Take(6))
{
    Console.WriteLine($"  {country.Iso} {country.State.Treasury.Reserves.Value.Exact / 1e9,8:F2} трлн, " +
                      $"курс ×{country.ExchangeRate.Exact:F2}");
}

var median = world.Countries.Select(c => c.State.Treasury.Reserves.Value.Exact).OrderBy(x => x).ElementAt(100);
Console.WriteLine($"  медиана: {median / 1e6:F0} млн $");

// --- Ж. Опыт: что если деньги не кончаются -----------------------------------------
// Отделяет нехватку мощностей от нехватки денег. Если с бездонной казной мир выходит
// на свои возможности, значит остаток упирается в курс валют, а не в промышленность.
Console.WriteLine();
Console.WriteLine("=== Ж. Тот же мир, но деньги не кончаются ===");

var rich = Load();
foreach (var country in rich.Countries)
{
    country.State.Treasury.Reserves.Add(ReserveKind.ForeignCurrency, WorldMarket.WorldIssuer,
        WorldMarket.WorldIssuer, Money.FromWhole(1_000_000_000));
}

var richSim = new Simulation(rich);
var richConstant = new Prices(goods.ToDictionary(good => good, rich.Market.Prices.Of));
var richGdp = default(Money);
var richExport = default(Money);

for (var tick = 0; tick < 365; tick++)
{
    richSim.Tick();
    foreach (var country in rich.Countries)
    {
        richGdp += richSim.ValueAddedOf(country.Id, richConstant);
        richExport += richSim.ExportsOf(country.Id);
    }
}

var richRails = rich.Countries.Sum(c => goods.Count(good =>
{
    var times = c.State.Prices.Of(good).Exact / c.State.Prices.StartOf(good).Exact;
    return times >= Prices.MaxSwingTimes || times <= 1.0 / Prices.MaxSwingTimes;
}));

Console.WriteLine($"  реальный ВВП {richGdp.Exact / 1e9:F2} трлн против {worldGdp:F2} с настоящими деньгами");
Console.WriteLine($"  экспорт {richExport.Exact / 1e9:F2} трлн против {worldExport:F2}");
Console.WriteLine($"  на рельсах {100.0 * richRails / (rich.Countries.Count * goods.Length):F1}%");
Console.WriteLine();
Console.WriteLine("  покрытие конечных товаров:");
foreach (var good in new[] { GoodType.ConsumerGoods, GoodType.Food, GoodType.Medicine, GoodType.Components, GoodType.Electronics })
{
    Console.WriteLine($"    {good,-16} {richSim.WorldOutputOf(good).Exact / richSim.WorldDemandOf(good).Exact,6:P0}");
}

// --- З. Заморозка резервов: сколько уцелеет ---------------------------------------
Console.WriteLine();
Console.WriteLine("=== З. Если Запад заморозит резервы ===");

// На свежем мире: за пять лет резервы почти проедены, мерить нечего.
var fresh = Load();
var west = fresh.Countries
    .Where(c => fresh.Relations.BlocOf(c.Id) == CapitaModern.Core.Politics.Bloc.West)
    .Select(c => c.Id)
    .ToArray();

foreach (var iso in new[] { "RUS", "CHN", "IND", "BRA", "SAU" })
{
    var country = fresh.Countries.First(c => c.Iso == iso);
    var reserves = country.State.Treasury.Reserves;
    var before = reserves.Liquid.Exact;
    foreach (var freezer in west) reserves.Freeze(freezer);
    var after = reserves.Liquid.Exact;
    foreach (var freezer in west) reserves.Unfreeze(freezer);

    Console.WriteLine($"  {iso}: уцелело {(before > 0 ? after / before : 0),6:P0} " +
                      $"({after / 1e9:F1} из {before / 1e9:F1} трлн $)");
}

Console.WriteLine();
Console.WriteLine("Кто кому не даст в долг:");
foreach (var (a, b) in new[] { ("JPN", "RUS"), ("CHN", "RUS"), ("USA", "CHN"), ("CHN", "ETH"), ("JPN", "ETH") })
{
    var from = world.Countries.First(c => c.Iso == a);
    var to = world.Countries.First(c => c.Iso == b);
    var attitude = world.Relations.Between(from.Id, to.Id);
    var politics = CreditMarket.PoliticsOn(attitude);

    Console.WriteLine($"  {a} -> {b}: отношение {attitude,4}, " +
                      (politics is null
                          ? "не даст вовсе"
                          : $"надбавка {politics.Value / 100.0,6:F2}%, итого {(CreditMarket.BaseRate + from.KeyRate + politics.Value) / 100.0:F2}%"));
}
