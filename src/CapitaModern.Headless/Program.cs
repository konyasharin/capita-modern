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

var war = Array.IndexOf(args, "--war");
if (war >= 0 && war + 2 < args.Length)
{
    var a = world.Countries.First(c => c.Iso == args[war + 1]);
    var b = world.Countries.First(c => c.Iso == args[war + 2]);
    game.DeclareWar(a.Id, b.Id);
    Console.WriteLine($"война: {a.Name} против {b.Name}");
}

string[] watched = ["USA", "CHN", "RUS", "DEU", "IND", "BRA", "PRK"];
var lastByCountry = world.Countries.ToDictionary(c => c.Id, c => c.GdpReal);
var jitter = world.Countries.ToDictionary(c => c.Id, _ => 0.0);
var lastGdp = World();
var worstWeek = 0.0;
var lastYear = 0.0;

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

        foreach (var c in world.Countries)
        {
            if (lastByCountry[c.Id] > 0 && year == years) jitter[c.Id] = Math.Max(jitter[c.Id], Math.Abs(c.GdpReal / lastByCountry[c.Id] - 1));
            lastByCountry[c.Id] = c.GdpReal;
        }

        if (traced is not null && year <= 2)
        {
            var worst = Enumerable.Range(0, traced.Fill.Length).OrderBy(g => traced.Fill[g]).Take(3)
                .Select(g => $"{world.Catalog.Goods[g].Id} {traced.Fill[g]:F2}");
            var services = world.Catalog.GoodIndex("Services");
            Console.WriteLine($"  неделя {world.Day / 7,3}: ВВП {traced.GdpReal / 1e9,7:F3} нужды {traced.Satisfaction * 100,4:F0}% "
                + $"услуги {traced.ProducedWeek[services]:F0}/{traced.DemandWeek[services]:F0} хуже всего: {string.Join(", ", worst)}");
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
Console.WriteLine($"стройка компаний за партию: нечего {BuildStep.Stall[0]}, мало денег {BuildStep.Stall[1]}, пауза {BuildStep.Stall[2]}, начато {BuildStep.Stall[3]}");
Console.WriteLine("сильнее всех дрожит ВВП в последний год: " + string.Join(", ", world.Countries.OrderByDescending(c => jitter[c.Id] * c.GdpReal)
    .Take(6).Select(c => $"{c.Iso} {jitter[c.Id] * 100:F1}% ({c.GdpReal / 1e9:F2} трлн)")));
var companyCash = world.Companies.Where(c => c.Alive).Sum(c => c.Cash);
Console.WriteLine($"деньги компаний {companyCash / 1e9:F2} трлн, казны {world.Countries.Sum(c => c.Treasury) / 1e9:F2} трлн");
Console.WriteLine("выше всех уровень цен: " + string.Join(", ", world.Countries.OrderByDescending(c => c.PriceLevel).Take(6)
    .Select(c => $"{c.Iso} {c.PriceLevel:G3} (инфл. {c.Inflation * 100:F0}%, ВВП {c.GdpReal / 1e9:F3} трлн)")));

Console.WriteLine();
Console.WriteLine("товар               выпуск/нед      спрос/нед   покрытие   запас, суток   цена: мин..макс");
foreach (var good in world.Catalog.Goods)
{
    var g = good.Index;
    var made = world.Countries.Sum(c => c.ProducedWeek[g]);
    var asked = world.Countries.Sum(c => c.DemandWeek[g]);
    var stock = world.Countries.Sum(c => c.Stock[g]);
    var use = world.Countries.Sum(c => c.DailyUse[g]);
    var mults = world.Countries.Where(c => c.DailyUse[g] > 0).Select(c => c.PriceMult[g]).ToList();
    Console.WriteLine($"{good.Id,-18} {made,12:F0}  {asked,13:F0}  {(asked > 0 ? made / asked * 100 : 0),8:F0}%  "
        + $"{(use > 0 ? stock / use : 0),13:F0}   {(mults.Count > 0 ? mults.Min() : 0):F2}..{(mults.Count > 0 ? mults.Max() : 0):F2}");
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
