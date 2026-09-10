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
    Data("reserves.json"));

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
var startMoney = world.Countries.Sum(c => c.State.Treasury.Balance.Exact);

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
Console.WriteLine($"     денег в мире {world.Countries.Sum(c => c.State.Treasury.Balance.Exact) / 1e9:F2} трлн " +
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
Console.WriteLine("год   реальный ВВП   на рельсах   у богатейшей 10%   стран без денег");

void Report(int year, double gdp)
{
    var cash = world.Countries.Select(c => c.State.Treasury.Balance.Exact).OrderByDescending(x => x).ToArray();
    var top = cash.Take(world.Countries.Count / 10).Sum() / Math.Max(cash.Sum(), 1);
    var broke = cash.Count(x => x <= 0);
    var rails = world.Countries.Sum(c => goods.Count(good =>
    {
        var times = c.State.Prices.Of(good).Exact / c.State.Prices.StartOf(good).Exact;
        return times >= Prices.MaxSwingTimes || times <= 1.0 / Prices.MaxSwingTimes;
    }));

    Console.WriteLine($"{year,3} {gdp,14:F2} трлн {100.0 * rails / (world.Countries.Count * goods.Length),9:F1}% " +
                      $"{top,17:P0} {broke,17}");
}

Report(1, worldGdp);

for (var year = 2; year <= 5; year++)
{
    var yearGdp = default(Money);
    for (var tick = 0; tick < 365; tick++)
    {
        simulation.Tick();
        foreach (var country in world.Countries) yearGdp += simulation.ValueAddedOf(country.Id, constant);
    }

    Report(year, yearGdp.Exact / 1e9);
}

Console.WriteLine();
Console.WriteLine("Кто держит деньги через пять лет:");
foreach (var country in world.Countries.OrderByDescending(c => c.State.Treasury.Balance.Raw).Take(6))
{
    Console.WriteLine($"  {country.Iso} {country.State.Treasury.Balance.Exact / 1e9,8:F2} трлн " +
                      $"(на старте по данным {(world.Countries.Sum(x => x.State.Treasury.Balance.Exact) / 1e9):F2} на всех)");
}

var median = world.Countries.Select(c => c.State.Treasury.Balance.Exact).OrderBy(x => x).ElementAt(100);
Console.WriteLine($"  медиана: {median / 1e6:F0} млн $");
