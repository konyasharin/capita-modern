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
    Data("prices.json"));

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
        prices.Move(GoodType.Coal, demand, stock);
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
        prices.Move(GoodType.Coal, rising ? demand : default, stock);
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

        for (var day = 0; day < realDays; day++) prices.Move(GoodType.Coal, demand, stock);

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
var watched = new[] { "CHN", "USA", "IND", "DEU", "RUS" }
    .Select(iso => world.Countries.First(country => country.Iso == iso))
    .ToArray();

var startPrices = watched.ToDictionary(
    country => country.Iso,
    country => goods.ToDictionary(good => good, country.State.Prices.Of));

var yearValueAdded = world.Countries.ToDictionary(country => country.Id, _ => default(Money));
var firstTickValueAdded = new Dictionary<byte, Money>();

// Сколько цен уехало на рельсы коридора — по ходу года, а не только в конце.
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
        var added = simulation.ValueAddedOf(country.Id);
        yearValueAdded[country.Id] += added;
        if (tick == 1) firstTickValueAdded[country.Id] = added;
    }

    if (tick is 1 or 7 or 30 or 90 or 365) CountRails(tick);
}

Console.WriteLine($"  тик: {clock.Elapsed.TotalMilliseconds / 365:F2} мс");

Console.WriteLine();
Console.WriteLine("Страна  ВВП год 1   по ценам старта   в жизни 2020");
(string Iso, double Real)[] real =
    [("CHN", 14.7), ("USA", 21.0), ("IND", 2.7), ("DEU", 3.9), ("RUS", 1.49)];

foreach (var (iso, realGdp) in real)
{
    var country = world.Countries.First(c => c.Iso == iso);
    Console.WriteLine($"{iso}   {yearValueAdded[country.Id].Exact / 1e9,8:F2} трлн " +
                      $"{firstTickValueAdded[country.Id].Exact * 365 / 1e9,12:F2} трлн " +
                      $"{realGdp,12:F2} трлн");
}

Console.WriteLine();
Console.WriteLine($"Мир: {yearValueAdded.Values.Aggregate(default(Money), (a, b) => a + b).Exact / 1e9:F2} трлн " +
                  $"по ходу года, {firstTickValueAdded.Values.Aggregate(default(Money), (a, b) => a + b).Exact * 365 / 1e9:F2} " +
                  $"трлн по ценам старта, в жизни 85 трлн");

Console.WriteLine();
foreach (var country in watched)
{
    var moves = goods
        .Select(good => (good, times: country.State.Prices.Of(good).Exact / startPrices[country.Iso][good].Exact))
        .OrderByDescending(pair => pair.times)
        .ToArray();

    Console.WriteLine($"{country.Iso}: подорожало {string.Join(", ", moves.Take(3).Select(m => $"{m.good} x{m.times:F1}"))}" +
                      $" | подешевело {string.Join(", ", moves.TakeLast(3).Select(m => $"{m.good} x{m.times:F2}"))}");
    Console.WriteLine($"      осталось в пределах вдвое от старта: " +
                      $"{moves.Count(m => m.times is > 0.5 and < 2.0)} из {goods.Length}");
}
