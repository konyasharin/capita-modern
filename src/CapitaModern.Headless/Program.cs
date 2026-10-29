using CapitaModern.Core.Sim;
using CapitaModern.Core.World;

// Прогон партии без интерфейса: проверка, что за десятилетия числа идут ровно, без скачков.
// Запуск: dotnet run --project src/CapitaModern.Headless -c Release -- [лет] [--war ISO ISO]

var years = args.Length > 0 && int.TryParse(args[0], out var y) ? y : 40;
var root = FindRoot();

var clock = System.Diagnostics.Stopwatch.StartNew();
var game = Game.Load(path => File.ReadAllText(Path.Combine(root, path)), path => File.ReadAllBytes(Path.Combine(root, path)));
var world = game.World;
Console.WriteLine($"мир собран за {clock.ElapsedMilliseconds} мс: стран {world.Countries.Count()}, регионов {world.Regions.Count()}, "
    + $"компаний {world.Companies.Count}, дивизий {world.Divisions.Count}");

var traceAt = Array.IndexOf(args, "--trace");
var traced = traceAt >= 0 && traceAt + 1 < args.Length ? world.Countries.First(c => c.Iso == args[traceAt + 1]) : null;

var goodAt = Array.IndexOf(args, "--good") is var at && at >= 0 && at + 1 < args.Length ? at : -1;
var traceAll = args.Contains("--weekly");
var war = Array.IndexOf(args, "--war");
if (war >= 0 && war + 2 < args.Length)
{
    var a = world.Countries.First(c => c.Iso == args[war + 1]);
    var b = world.Countries.First(c => c.Iso == args[war + 2]);
    game.DeclareWar(a.Id, b.Id);
    Console.WriteLine($"война: {a.Name} против {b.Name}");
}

var use0 = new double[world.Catalog.Goods.Count];
var cap0 = new double[world.Catalog.Goods.Count];
string[] watched = ["USA", "CHN", "RUS", "DEU", "IND", "BRA", "PRK"];
var lastByCountry = world.Countries.ToDictionary(c => c.Id, c => c.GdpReal);
var jitter = world.Countries.ToDictionary(c => c.Id, _ => 0.0);
var lastGdp = World();
var worstWeek = 0.0;
var lastYear = 0.0;

// По каждой стране: ВВП по неделям и худшие значения прочих чисел — для поиска аномалий.
var series = world.Countries.ToDictionary(c => c.Id, c => new List<double> { c.GdpReal });
var worstNeeds = world.Countries.ToDictionary(c => c.Id, _ => 1.0);
var checks = new Checks(world);
var worstInflation = world.Countries.ToDictionary(c => c.Id, _ => 0.0);

Console.WriteLine();
Console.WriteLine("год  ВВП мира трлн  рост   инфл.медиана  нужды  заводов    стройка  компаний  дивизий  худшая неделя ВВП");

for (var year = 1; year <= years; year++)
{
    var sum = 0.0;
    var weeks = 0;
    var start = clock.ElapsedMilliseconds;

    for (var day = 0; day < 365; day++)
    {
        game.Tick();
        if (world.Day % 7 != 0) continue;

        checks.Week();
        foreach (var c in world.Countries)
        {
            series[c.Id].Add(c.GdpReal);
            worstNeeds[c.Id] = Math.Min(worstNeeds[c.Id], c.Satisfaction);
            worstInflation[c.Id] = Math.Max(worstInflation[c.Id], c.Inflation);
            if (lastByCountry[c.Id] > 0 && year == years) jitter[c.Id] = Math.Max(jitter[c.Id], Math.Abs(c.GdpReal / lastByCountry[c.Id] - 1));
            lastByCountry[c.Id] = c.GdpReal;
        }

        if (world.Day == 7)
        {
            for (var g = 0; g < use0.Length; g++)
            {
                use0[g] = world.Countries.Sum(c => c.DailyUse[g]) * 7;
                cap0[g] = world.Countries.Sum(c => c.CapacityOut[g]) * 7;
            }
        }

        if (goodAt >= 0 && (year == 1 && world.Day / 7 is 1 or 4 or 10 or 30 or 52 || year % 10 == 0 && day >= 358))
        {
            var g = world.Catalog.GoodIndex(args[goodAt + 1]);
            Console.WriteLine($"  неделя {world.Day / 7}: {args[goodAt + 1]} — мощн/нед, выпуск, вывоз, ввоз, расход/нед, запас сут, цена");
            foreach (var c in world.Countries.OrderByDescending(c => c.CapacityOut[g] + c.DailyUse[g]).Take(14))
            {
                Console.WriteLine($"    {c.Iso,-4} {c.CapacityOut[g] * 7,9:F0} {c.ProducedWeek[g],9:F0} {c.ExportedWeek[g],9:F0} {c.ImportedWeek[g],9:F0} "
                    + $"{c.DailyUse[g] * 7,9:F0} {(c.DailyUse[g] > 0 ? c.Stock[g] / c.DailyUse[g] : 0),7:F0} {c.PriceMult[g],5:F2}");
            }
            Console.WriteLine($"    мир: мощн {world.Countries.Sum(c => c.CapacityOut[g]) * 7:F0}, выпуск {world.Countries.Sum(c => c.ProducedWeek[g]):F0}, "
                + $"расход {world.Countries.Sum(c => c.DailyUse[g]) * 7:F0}, вывоз {world.Countries.Sum(c => c.ExportedWeek[g]):F0}");
        }

        if (traced is not null && (traceAll || world.Day / 7 % 26 == 0))
        {
            var worst = Enumerable.Range(0, traced.Fill.Length).OrderBy(g => traced.Fill[g]).Take(3)
                .Select(g => $"{world.Catalog.Goods[g].Id} {traced.Fill[g]:F2}");
            var services = world.Catalog.GoodIndex("Services");
            Console.WriteLine($"  неделя {world.Day / 7,3}: ВВП {traced.GdpReal / 1e9,7:F3} нужды {traced.Satisfaction * 100,4:F0}% "
                + $"услуги {traced.ProducedWeek[services]:F0}/{traced.DailyUse[services] * 7:F0} хуже всего: {string.Join(", ", worst)}");
            foreach (var id in new[] { "Oil", "Gas", "Fuel" })
            {
                var g = world.Catalog.GoodIndex(id);
                var use = traced.DailyUse[g];
                Console.WriteLine($"      {id}: выпуск {traced.ProducedWeek[g]:F0} вывоз {traced.ExportedWeek[g]:F0} ввоз {traced.ImportedWeek[g]:F0} "
                    + $"расход/сут {use:F0} запас {(use > 0 ? traced.Stock[g] / use : 0):F0} сут цена {traced.PriceMult[g]:F2}");
            }
        }

        var now = World();
        if (lastGdp > 0) worstWeek = Math.Max(worstWeek, Math.Abs(now / lastGdp - 1));
        lastGdp = now;
        sum += now;
        weeks++;
    }

    var gdp = sum / weeks / 1e9;
    var previous = lastYear > 0 ? lastYear : gdp;
    lastYear = gdp;
    var inflation = Median(world.Countries.Where(c => c.GdpReal > 0).Select(c => c.Inflation));
    var needs = world.Countries.Where(c => c.GdpReal > 0).Average(c => c.Satisfaction);
    var plants = world.Regions.Sum(r => r.Plants.Sum(p => p.Count));

    Console.WriteLine($"{year,3}  {gdp,13:F2}  {(gdp / previous - 1) * 100,5:F1}%  {inflation * 100,11:F1}%  {needs * 100,4:F0}%  "
        + $"{plants,8}  {world.Projects.Count,8}  {world.Companies.Count(c => c.Alive),8}  {world.Divisions.Count,7}  "
        + $"{worstWeek * 100,8:F2}%   ({clock.ElapsedMilliseconds - start} мс)");
    worstWeek = 0;
}

Console.WriteLine();
Console.WriteLine("страны с самым кривым ходом ВВП (старт → 4 нед → 1 год → 5 лет → конец, % к старту; худшая неделя; мин. нужды; макс. инфляция):");
var anomalies = world.Countries.Where(c => series[c.Id][0] > 1e6 && series[c.Id].Count > 2).Select(c =>
{
    var s = series[c.Id];
    double At(int week) => s[Math.Min(week, s.Count - 1)] / s[0] * 100;
    var jump = s.Skip(2).Zip(s.Skip(1), (b, a) => a > 0 ? Math.Abs(b / a - 1) : 0).DefaultIfEmpty(0).Max();
    var low = s.Skip(1).Min() / s[0];
    return (Country: c, W4: At(4), Y1: At(52), Y5: At(260), End: At(s.Count - 1), Jump: jump, Low: low);
}).OrderBy(a => Math.Min(a.Low, 1 - a.Jump * 5)).Take(15);
foreach (var a in anomalies)
{
    Console.WriteLine($"  {a.Country.Iso,-4} {a.Country.GdpReal / 1e9,7:F3} трлн: 100 → {a.W4,4:F0} → {a.Y1,4:F0} → {a.Y5,4:F0} → {a.End,4:F0}; "
        + $"мин {a.Low * 100:F0}%, неделя {a.Jump * 100:F1}%, нужды {worstNeeds[a.Country.Id] * 100:F0}%, инфл. {worstInflation[a.Country.Id] * 100:F1}%");
    var c = a.Country;
    var short_ = c.Needs.Select(n => n.Good).Distinct().Where(g => c.DailyUse[g] > 0)
        .Select(g => (Good: world.Catalog.Goods[g].Id, Got: c.ConsumedWeek[g] / Math.Max(1e-9, c.Needs.Where(n => n.Good == g).Sum(n => n.Rate) * world.OwnedBy(c.Id).Sum(r => (double)r.Population) / 1e6 * 7)))
        .OrderBy(x => x.Got).Take(3);
    Console.WriteLine("        хуже всего с нуждами: " + string.Join(", ", short_.Select(x => $"{x.Good} {x.Got * 100:F0}%")));
}

Console.WriteLine();
var growth = world.Countries.Where(c => series[c.Id][0] > 1e6 && series[c.Id].Count > 60)
    .Select(c => (c.Iso, Rate: Math.Pow(series[c.Id][^1] / series[c.Id][1], 52.0 / (series[c.Id].Count - 2)) - 1)).OrderBy(x => x.Rate).ToList();
if (growth.Count > 0)
{
    Console.WriteLine($"рост ВВП в год по странам: медиана {growth[growth.Count / 2].Rate * 100:F1}%; медленнее всех "
        + string.Join(", ", growth.Take(5).Select(x => $"{x.Iso} {x.Rate * 100:F1}%")) + "; быстрее всех "
        + string.Join(", ", growth.TakeLast(5).Select(x => $"{x.Iso} {x.Rate * 100:F1}%")));
}

foreach (var iso in new[] { "USA", "CHN", "RUS", "DEU" })
{
    var c = world.Countries.First(x => x.Iso == iso);
    var edge = world.Catalog.Goods.Where(g => c.DailyUse[g.Index] > 0 && (c.PriceMult[g.Index] < 0.65 || c.PriceMult[g.Index] > 1.5))
        .Select(g => $"{g.Id} ×{c.PriceMult[g.Index]:F2} ({c.Stock[g.Index] / (c.DailyUse[g.Index] + c.ExportUse[g.Index]):F0} сут)");
    Console.WriteLine($"  {iso}: цены у края коридора: {string.Join(", ", edge)}");
}

Console.WriteLine();
checks.Print();

Console.WriteLine();
Console.WriteLine("страна  ВВП трлн  инфляция  уровень цен  нужды  казна/ВВП  цены товаров: мин..макс");
foreach (var iso in watched)
{
    var c = world.Countries.FirstOrDefault(x => x.Iso == iso);
    if (c is null) continue;

    var nominal = c.GdpReal * c.PriceLevel;
    Console.WriteLine($"{iso,-6} {c.GdpReal / 1e9,9:F2}  {c.Inflation * 100,7:F1}%  {c.PriceLevel,11:F2}  {c.Satisfaction * 100,4:F0}%  "
        + $"{(nominal > 0 ? c.Treasury / nominal : 0) * 100,8:F0}%  {c.PriceMult.Min():F2}..{c.PriceMult.Max():F2}  {c.System}");
}

Console.WriteLine();
Console.WriteLine($"стройка компаний за партию: нечего {BuildStep.Stall[0]}, мало денег {BuildStep.Stall[1]}, пауза {BuildStep.Stall[2]}, нет рук {BuildStep.Stall[4]}, начато {BuildStep.Stall[3]}");
Console.WriteLine("сильнее всех дрожит ВВП в последний год: " + string.Join(", ", world.Countries.OrderByDescending(c => jitter[c.Id] * c.GdpReal)
    .Take(6).Select(c => $"{c.Iso} {jitter[c.Id] * 100:F1}% ({c.GdpReal / 1e9:F2} трлн)")));
var companyCash = world.Companies.Where(c => c.Alive).Sum(c => c.Cash);
Console.WriteLine($"деньги компаний {companyCash / 1e9:F2} трлн, казны {world.Countries.Sum(c => c.Treasury) / 1e9:F2} трлн");
Console.WriteLine("выше всех уровень цен: " + string.Join(", ", world.Countries.OrderByDescending(c => c.PriceLevel).Take(6)
    .Select(c => $"{c.Iso} {c.PriceLevel:G3} (инфл. {c.Inflation * 100:F0}%, ВВП {c.GdpReal / 1e9:F3} трлн)")));

Console.WriteLine();
Console.WriteLine("товар               выпуск/нед      спрос/нед   покрытие   запас, суток   цена: мин..макс   мощн/спрос на 1-й нед   спрос к 1-й нед");
foreach (var good in world.Catalog.Goods)
{
    var g = good.Index;
    var made = world.Countries.Sum(c => c.ProducedWeek[g]);
    var asked = world.Countries.Sum(c => c.DailyUse[g] * 7);
    var stock = world.Countries.Sum(c => c.Stock[g]);
    var use = world.Countries.Sum(c => c.DailyUse[g]);
    var mults = world.Countries.Where(c => c.DailyUse[g] > 0).Select(c => c.PriceMult[g]).ToList();
    Console.WriteLine($"{good.Id,-18} {made,12:F0}  {asked,13:F0}  {(asked > 0 ? made / asked * 100 : 0),8:F0}%  "
        + $"{(use > 0 ? stock / use : 0),13:F0}   {(mults.Count > 0 ? mults.Min() : 0):F2}..{(mults.Count > 0 ? mults.Max() : 0):F2}"
        + $"   {(use0[g] > 0 ? cap0[g] / use0[g] : 0),18:F2}   {(use0[g] > 0 ? use * 7 / use0[g] : 0),14:F2}");
}

if (war >= 0)
{
    Console.WriteLine();
    foreach (var c in world.Countries.Where(c => world.Enemies(c.Id).Any()))
    {
        Console.WriteLine($"{c.Iso}: держит регионов {world.ControlledBy(c.Id).Count()} (своих {world.OwnedBy(c.Id).Count()}), "
            + $"дивизий {world.Divisions.Count(d => d.Country == c.Id)}");
    }
}

double World() => world.Countries.Sum(c => c.GdpReal);

static double Median(IEnumerable<double> values)
{
    var sorted = values.OrderBy(v => v).ToArray();
    return sorted.Length == 0 ? 0 : sorted[sorted.Length / 2];
}

static string FindRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "data", "map"))) dir = dir.Parent;

    return dir?.FullName ?? throw new DirectoryNotFoundException("не найдена папка data/map");
}
