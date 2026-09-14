using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;
using CapitaModern.Core.Loading;
using CapitaModern.Core.World;

// Курс сам по себе ничего не говорит: донг и так стоит тысячи за доллар. Смотреть надо,
// во сколько раз он уехал от старта.
double Swing(Country country) =>
    (double)country.ExchangeRate.Raw / Math.Max(1, country.StartRate.Raw);

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
    File.ReadAllText(Path.Combine(RepoPaths.GetRepoRoot(), "data", "politics", "blocs.json")),
    Data("efficiency.json"),
    Data("money-supply.json"),
    Data("trade-costs.json"),
    File.ReadAllText(Path.Combine(RepoPaths.GetRepoRoot(), "data", "map", "neighbours.json")),
    File.ReadAllText(Path.Combine(RepoPaths.GetRepoRoot(), "data", "map", "basins.json")),
    File.ReadAllText(Path.Combine(RepoPaths.GetRepoRoot(), "data", "economy", "currencies.json")),
    File.ReadAllText(Path.Combine(RepoPaths.GetRepoRoot(), "data", "economy", "companies.json")),
    File.ReadAllText(Path.Combine(RepoPaths.GetRepoRoot(), "data", "politics", "defence.json")),
    File.ReadAllText(Path.Combine(RepoPaths.GetRepoRoot(), "data", "politics", "traits.json")));

var goods = Enum.GetValues<GoodType>();

// Быстрый режим для итераций: один год вместо пяти и без двух опытов на отдельных мирах.
// Полный прогон — только перед коммитом, числа из быстрого с ним не сравнивать.
var fast = args.Contains("--fast");
// Сколько лет прогонять: --years 20 для проверки на длинной дистанции.
var yearsAt = Array.IndexOf(args, "--years");
var years = yearsAt >= 0 && yearsAt + 1 < args.Length
    && int.TryParse(args[yearsAt + 1], out var howLong)
    ? howLong
    : fast ? 1 : 5;

if (fast) Console.WriteLine("### БЫСТРЫЙ РЕЖИМ: один год, опыты Ж и З пропущены\n");

// Разделы А, Б и Б2 убраны вместе с механизмом, который они изучали: цена больше не ползёт
// шагами от покрытия склада, а считается равновесием спроса и предложения. Что осталось от
// тех замеров — в docs/09-reality-check.md.

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

var startPlants = world.Regions.SelectMany(r => r.BuildingsCount).Sum(p => p.Value);

var realGdp = world.Countries.ToDictionary(country => country.Id, _ => default(Money));
// Зарплата копится в местных деньгах, поэтому складываем сразу в мировой мере: курс
// за год уезжает, и делить в конце на конечный было бы неверно.
var wagesYear = world.Countries.ToDictionary(country => country.Id, _ => 0.0);
var employedYear = world.Countries.ToDictionary(country => country.Id, _ => 0L);
var transitYear = world.Countries.ToDictionary(country => country.Id, _ => default(Money));
var exports = world.Countries.ToDictionary(country => country.Id, _ => default(Money));
var taxYear = world.Countries.ToDictionary(country => country.Id, _ => default(Money));
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

Console.WriteLine("Куда уезжают цены и как идёт выпуск по месяцам:");
var clock = new System.Diagnostics.Stopwatch();
for (var tick = 1; tick <= 365; tick++)
{
    clock.Start();
    simulation.Tick();
    clock.Stop();

    foreach (var country in world.Countries)
    {
        realGdp[country.Id] += simulation.ValueAddedOf(country.Id, constant);
        // В стартовых ценах и по стартовому курсу: ВВП считается так же, и делить одно на
        // другое можно только одной линейкой. Стартового курса мало — за пять лет цены
        // внутри страны уезжают в разы, и отношение раздувалось во столько же. У Индии
        // выходило 117% сбора к ВВП.
        var level = Math.Max(1, simulation.PriceLevelOf(country.Id));
        taxYear[country.Id] += new Money(
            (long)((Int128)country.Budget.Collected.Raw * Money.Scale * PriceLevel.Scale
                / country.StartRate.Raw / level));
        exports[country.Id] += simulation.ExportsOf(country.Id, constant);
        // В тех же постоянных ценах, что и ВВП: фонд оплаты — известная доля добавленной
        // стоимости. Считать местные деньги через текущий курс нельзя, тогда зарплату и
        // выработку меряют двумя разными линейками.
        wagesYear[country.Id] +=
            simulation.ValueAddedOf(country.Id, constant).Exact * country.LabourShare / 100;
        // Вместе со своим делом: ВВП кормит и тех, кто не попал на завод, и делить его
        // на одних заводских — значит завышать выработку вдвое-втрое.
        employedYear[country.Id] += simulation.EmployedIn(country.Id) + simulation.SelfEmployedIn(country.Id);
        transitYear[country.Id] += simulation.TransitEarnedBy(country.Id);
        imports[country.Id] += simulation.ImportsOf(country.Id, constant);
    }

    if (tick is 1 or 7 or 30 or 90 or 365) CountRails(tick);

    // Помесячно за первый год: годовая сводка прячет излом, а он именно здесь.
    if ((tick + 1) % 30 == 0 && tick < 365)
    {
        var perDay = world.Countries.Sum(c => simulation.ValueAddedOf(c.Id, constant).Exact);
        var shelves = world.Countries.Sum(c =>
            goods.Sum(good => constant.CostOf(good, c.State.Stock.Of(good)).Exact));
        var could = world.Countries.Sum(c => simulation.PotentialOf(c.Id, constant).Exact);

        Console.WriteLine($"  месяц {(tick + 1) / 30,2}: выпуск {perDay / 1e9 * 365,7:F2} трлн в год "
            + $"из {could / 1e9 * 365,7:F2} возможных ({100.0 * perDay / Math.Max(1, could),5:F1}%), "
            + $"склады {shelves / 1e9,7:F2} трлн");
    }
}

Console.WriteLine($"  тик: {clock.Elapsed.TotalMilliseconds / 365:F2} мс");
Console.WriteLine("  куда уходит тик:");
foreach (var (step, ticks) in simulation.Steps.OrderByDescending(pair => pair.Value).Take(8))
{
    var ms = 1000.0 * ticks / System.Diagnostics.Stopwatch.Frequency / 365;

    Console.WriteLine($"    {step,-18} {ms,6:F2} мс "
        + $"{100.0 * ms / (clock.Elapsed.TotalMilliseconds / 365),5:F1}%");
}

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
Console.WriteLine($"Мир: реальный ВВП {worldGdp:F2} трлн (в жизни 130 трлн по ППС)");
Console.WriteLine($"     товарный экспорт {worldExport:F2} трлн (в жизни 17.6 трлн)");

var (refused, empty) = simulation.UnfilledBids;
var askedTotal = refused + empty;
Console.WriteLine($"     не куплено за год: дорога доставка {refused.Exact / 1e9:F1} млрд ед., " +
    $"нет товара {empty.Exact / 1e9:F1} млрд ед.");
Console.WriteLine($"     денег в мире {world.Countries.Sum(c => c.State.Treasury.Reserves.Value.Exact) / 1e9:F2} трлн " +
                  $"(на старте {startMoney / 1e9:F2})");

// --- Г. Что осталось на рельсах и почему ------------------------------------------
Console.WriteLine();
Console.WriteLine("=== Г. Цены и границы коридора ===");
Console.WriteLine("Во сколько раз медианная цена страны ушла от стартовой:");
foreach (var good in new[] { GoodType.Services, GoodType.Coal, GoodType.Oil, GoodType.Electricity,
    GoodType.Materials, GoodType.Metals, GoodType.Food, GoodType.ConsumerGoods })
{
    var times = world.Countries
        .Select(c => c.State.Prices.Of(good).Exact / c.State.Prices.StartOf(good).Exact)
        .OrderBy(x => x)
        .ElementAt(world.Countries.Count / 2);

    Console.WriteLine($"  {good,-16} ×{times,8:F3}");
}

Console.WriteLine();

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
Console.WriteLine("товар               выпуск      заказ   покрытие   из заказа: стройка   люди   заводы");

foreach (var good in goods.OrderBy(good =>
{
    var demand = simulation.WorldDemandOf(good).Exact;
    return demand == 0 ? double.MaxValue : simulation.WorldOutputOf(good).Exact / demand;
}))
{
    var made = simulation.WorldOutputOf(good).Exact;
    var asked = simulation.WorldDemandOf(good).Exact;
    var ratio = asked == 0 ? "заказа нет" : $"{made / asked,7:P0}";
    var forBuild = world.Countries.Sum(c => simulation.BuildWantOf(c.Id, good).Exact);
    var forPeople = world.Countries.Sum(c => simulation.PeopleWantOf(c.Id, good).Exact);
    var forWear = world.Countries.Sum(c => simulation.ReplaceWantOf(c.Id, good).Exact);

    Console.WriteLine($"{good,-18} {made,10:F0} {asked,10:F0}   {ratio}"
        + $"{100 * forBuild / Math.Max(1, asked),18:F0}% {100 * forPeople / Math.Max(1, asked),6:F0}%"
        + $" {100 * (asked - forBuild - forPeople) / Math.Max(1, asked),6:F0}%"
        + $"   на замену {forWear,9:F0}"
        + $", склад {world.Countries.Sum(c => c.State.Stock.Of(good).Exact),12:F0}"
        + $", мощность {world.Countries.Sum(c => simulation.PotentialOutputOf(c.Id, good).Exact),10:F0}");
}

// --- Е. Пять лет: не стекутся ли деньги к экспортёрам -----------------------------
Console.WriteLine();
Console.WriteLine($"=== Е. Лет: {years} ===");
Console.WriteLine("год   реальный ВВП   на рельсах   внешний долг   нагрузка >200%   валюта вдвое"
    + "   предприятий   стройка   износ   отказов   занято млн");

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

    var standing = world.Regions.SelectMany(r => r.BuildingsCount).Sum(pair => pair.Value);
    var busy = world.Countries.Sum(c => (long)simulation.EmployedIn(c.Id)) / 1_000_000;

    Console.WriteLine($"{year,3} {gdp,14:F2} трлн {100.0 * rails / (world.Countries.Count * goods.Length),9:F1}% " +
                      $"{debt,10:F2} трлн {heavy,14} {weak,14}" +
                      $"{standing,14} {simulation.BuiltSoFar,9} {simulation.WornSoFar,7}" +
                      $" [ниша {Simulation.Stall[0]} касса {Simulation.Stall[1]} склад {Simulation.Stall[2]}" +
                      $" руки {Simulation.Stall[3]} заказано {Simulation.Stall[4]}]" +
                      $" [добавка {new Money(Simulation.Flows[0]).Whole / 1e12,6:F1} зарплаты" +
                      $" {new Money(Simulation.Flows[1]).Whole / 1e12,6:F1} владельцам" +
                      $" {new Money(Simulation.Flows[2]).Whole / 1e12,6:F1} стройка" +
                      $" {new Money(Simulation.Flows[3]).Whole / 1e12,6:F1} износ" +
                      $" {new Money(Simulation.Flows[4]).Whole / 1e12,6:F1}" +
                      $" касса {simulation.CompanyPurses().Cash.Whole / 1e12,6:F1}" +
                      $" долг {simulation.CompanyPurses().Debt.Whole / 1e12,6:F1}]" +
                      $"{world.Countries.Count(c => c.DefaultedOnDay > 0),10} {busy,12}");
}

// Сколько добра лежит на складах мира, в постоянных ценах. Прирост за год — это та
// часть ВВП, которая никому не досталась: выпуск, осевший на полке.
double OnShelves() => world.Countries.Sum(c =>
    goods.Sum(good => constant.CostOf(good, c.State.Stock.Of(good)).Exact));

var shelvesWas = OnShelves();

Report(1, worldGdp);

for (var year = 2; year <= years; year++)
{
    var yearGdp = default(Money);
    for (var tick = 0; tick < 365; tick++)
    {
        simulation.Tick();
        foreach (var country in world.Countries)
        {
            yearGdp += simulation.ValueAddedOf(country.Id, constant);
            realGdp[country.Id] += simulation.ValueAddedOf(country.Id, constant);
            taxYear[country.Id] += new Money(
                (long)((Int128)country.Budget.Collected.Raw * Money.Scale / country.StartRate.Raw));
            exports[country.Id] += simulation.ExportsOf(country.Id, constant);
            imports[country.Id] += simulation.ImportsOf(country.Id, constant);
            wagesYear[country.Id] +=
                simulation.ValueAddedOf(country.Id, constant).Exact * country.LabourShare / 100;
            employedYear[country.Id] += simulation.EmployedIn(country.Id);
        }
    }

    Report(year, yearGdp.Exact / 1e9);
}

Console.WriteLine();
Console.WriteLine($"На складах мира {OnShelves() / 1e9:F2} трлн против {shelvesWas / 1e9:F2} год назад:");
Console.WriteLine($"  прирост запасов {(OnShelves() - shelvesWas) / 1e9 / Math.Max(1, years - 1):F2} трлн в год —");
Console.WriteLine("  это выпуск, осевший на полке, и он входит в ВВП наравне с проданным.");

Console.WriteLine();
Console.WriteLine("Крупнейшие должники в конце прогона:");
foreach (var country in world.Countries.OrderByDescending(c => c.State.Treasury.Debt.Owed().Raw).Take(6))
{
    var burden = country.State.Treasury.Debt.BurdenToExports(exports[country.Id] / years);
    Console.WriteLine($"  {country.Iso} долг {country.State.Treasury.Debt.Owed().Exact / 1e9,6:F2} трлн, " +
                      $"нагрузка {(burden == int.MaxValue ? "без экспорта" : burden + "%"),12}, " +
                      $"валюта {Swing(country):F2} от старта");
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
Console.WriteLine("Кто держит деньги в конце прогона:");
foreach (var country in world.Countries.OrderByDescending(c => c.State.Treasury.Reserves.Value.Raw).Take(6))
{
    Console.WriteLine($"  {country.Iso} {country.State.Treasury.Reserves.Value.Exact / 1e9,8:F2} трлн, " +
                      $"валюта {Swing(country):F2} от старта");
}

var median = world.Countries.Select(c => c.State.Treasury.Reserves.Value.Exact).OrderBy(x => x).ElementAt(100);
Console.WriteLine($"  медиана: {median / 1e6:F0} млн $");

// --- Ж. Опыт: что если деньги не кончаются -----------------------------------------
// Отделяет нехватку мощностей от нехватки денег. Если с бездонной казной мир выходит
// на свои возможности, значит остаток упирается в курс валют, а не в промышленность.
Console.WriteLine();
Console.WriteLine("=== Ж. Тот же мир, но деньги не кончаются ===");

if (fast) Console.WriteLine("  пропущено");
else
{

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
        richExport += richSim.ExportsOf(country.Id, richConstant);
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

}

// --- З. Заморозка резервов: сколько уцелеет ---------------------------------------
Console.WriteLine();
Console.WriteLine("=== З. Если Запад заморозит резервы ===");

// На свежем мире: за пять лет резервы почти проедены, мерить нечего.
if (fast) Console.WriteLine("  пропущено");
else
{

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

// --- И. Сверка кредита с жизнью ----------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== И. Ставки и нагрузка против настоящих ===");
Console.WriteLine("страна   ставка у нас   в жизни 2020   нагрузка у нас   в жизни");

// Доходность десятилетних гособлигаций и внешний долг к годовому экспорту, 2020.
(string Iso, double Yield, int Burden)[] realDebt =
[
    ("USA", 0.9, 1400), ("DEU", -0.5, 380), ("JPN", 0.0, 480), ("GBR", 0.3, 900),
    ("CHN", 3.2, 90), ("IND", 5.9, 104), ("BRA", 7.0, 260), ("RUS", 6.0, 145),
    ("TUR", 12.9, 256), ("ZAF", 9.0, 190), ("PAK", 10.0, 350), ("ETH", 12.0, 270),
];

foreach (var (iso, yield, realBurden) in realDebt)
{
    var country = world.Countries.First(c => c.Iso == iso);
    var loans = country.State.Treasury.Debt.Loans;
    var ours = loans.Count == 0
        ? double.NaN
        : loans.Sum(l => l.Principal.Exact * l.RateAt(simulation.WorldRate))
          / Math.Max(loans.Sum(l => l.Principal.Exact), 1) / 100;
    var burden = country.State.Treasury.Debt.BurdenToExports(country.ExportsPerDay * 365);

    Console.WriteLine($"{iso}   {(double.IsNaN(ours) ? "не занимает" : ours.ToString("F2") + "%"),12} " +
                      $"{yield,12:F1}% {(burden == int.MaxValue ? "без вывоза" : burden + "%"),16} {realBurden,8}%");
}

var withDebt = world.Countries.Where(c => c.State.Treasury.Debt.Owed().Raw > 0).ToArray();
var allLoans = withDebt.SelectMany(c => c.State.Treasury.Debt.Loans).ToArray();
Console.WriteLine();
Console.WriteLine($"Стран с внешним долгом: {withDebt.Length} из {world.Countries.Count}");
Console.WriteLine($"Займов всего: {allLoans.Length}, средний размер {allLoans.Average(l => l.Principal.Exact) / 1e6:F0} млн $");
Console.WriteLine($"Отказались платить хоть раз: {world.Countries.Count(c => c.DefaultedOnDay > 0)}");

// --- Ч. Откуда берутся отказы ---------------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== Ч. Откуда берутся отказы платить ===");
Console.WriteLine("Число их к пятому году гуляет от сорока до ста десяти при неизменных");
Console.WriteLine("правилах. Смотрим не на итог, а на то, как страна к нему приходит.");
Console.WriteLine();

var refusals = simulation.Refusals;
var talked = world.Countries.Count(c => simulation.TalksOf(c.Id) > 0);
var forgiven = world.Countries.Sum(c => simulation.ForgivenTo(c.Id).Exact);

Console.WriteLine($"Переписали долг: {talked} стран, прощено {forgiven / 1e9:F2} трлн $");
Console.WriteLine($"Всего отказов: {refusals.Count}");

if (refusals.Count > 0)
{
    Console.Write("По годам:");
    for (var year = 1; year <= years; year++)
    {
        var inYear = refusals.Count(r => r.Day > (year - 1) * 365 && r.Day <= year * 365);
        Console.Write($"  {year}-й {inYear}");
    }

    Console.WriteLine();

    // Чем страна была в момент отказа: нагрузка, покрытие резервами, сальдо.
    var burdens = refusals
        .Select(r => r.Capacity.Raw > 0 ? 100.0 * r.Owed.Exact / r.Capacity.Exact : double.PositiveInfinity)
        .Where(double.IsFinite)
        .OrderBy(x => x)
        .ToArray();

    var noCapacity = refusals.Count(r => r.Capacity.Raw <= 0);
    var dry = refusals.Count(r => r.Reserves.Raw <= 0);
    var deficit = refusals.Count(r => r.ImportsPerDay > r.ExportsPerDay);

    Console.WriteLine($"  без всякой возможности платить (вывоза нет вовсе): {noCapacity}");
    Console.WriteLine($"  с пустыми резервами: {dry}");
    Console.WriteLine($"  с ввозом больше вывоза: {deficit}");

    if (burdens.Length > 0)
    {
        Console.WriteLine($"  нагрузка в момент отказа: медиана {burdens[burdens.Length / 2]:F0}%, "
            + $"от {burdens[0]:F0}% до {burdens[^1]:F0}%");
    }

    Console.WriteLine();
    Console.WriteLine("Первые десять отказов:");
    Console.WriteLine("день  страна    долг      мог платить   резервы   ввоз/сут  вывоз/сут");

    foreach (var refusal in refusals.Take(10))
    {
        Console.WriteLine($"{refusal.Day,5} {refusal.Iso,-6} "
            + $"{refusal.Owed.Exact / 1e6,10:F1} млн {refusal.Capacity.Exact / 1e6,10:F1} млн "
            + $"{refusal.Reserves.Exact / 1e6,9:F1} {refusal.ImportsPerDay.Exact / 1e3,9:F1} "
            + $"{refusal.ExportsPerDay.Exact / 1e3,9:F1}");
    }
}
Console.WriteLine($"Плавающих займов: {allLoans.Count(l => l.RateKind == RateKind.Floating)} из {allLoans.Length}");

// --- К. Производительность: главный замер этого шага ------------------------------
// --- Щ. Из чего складывается выпуск ---------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== Щ. Из чего складывается выпуск ===");
Console.WriteLine("Доля отрасли в добавленной стоимости страны, в постоянных ценах.");
Console.WriteLine("В жизни на услуги приходится 70% ВВП богатой страны и 45% бедной.");
Console.WriteLine();
Console.Write("страна ");
foreach (var sector in Enum.GetValues<Sector>()) Console.Write($"{sector,12}");
Console.WriteLine();

foreach (var iso in new[] { "USA", "FRA", "DEU", "GBR", "ITA", "JPN", "CHN", "RUS", "SAU", "VNM", "IND", "NGA" })
{
    var id = world.Countries.First(c => c.Iso == iso).Id;
    var bySector = new Dictionary<Sector, double>();
    var total = 0.0;

    foreach (var good in goods)
    {
        var made = constant.CostOf(good, simulation.OutputOf(id, good)).Exact
            - constant.CostOf(good, simulation.ConsumedOf(id, good)).Exact;

        bySector[simulation.SectorOf(good)] = bySector.GetValueOrDefault(simulation.SectorOf(good)) + made;
        total += made;
    }

    Console.Write($"{iso,-7}");
    foreach (var sector in Enum.GetValues<Sector>())
    {
        Console.Write($"{(total > 0 ? 100 * bySector.GetValueOrDefault(sector) / total : 0),11:F0}%");
    }

    Console.WriteLine();
}

Console.WriteLine();
Console.WriteLine("Услуги: отчего встаёт выпуск. Пустая полка любого входа рецепта");
Console.WriteLine("обнуляет отрасль целиком, и дальше нечем платить за следующий ввоз.");

foreach (var iso in new[] { "USA", "DEU", "CHN", "IND", "NGA", "SAU" })
{
    var whose = world.Countries.First(c => c.Iso == iso);
    var serviceTypes = new[] { BuildingType.RetailFirm, BuildingType.TransportFirm,
        BuildingType.PublicService, BuildingType.BusinessFirm };

    var firms = serviceTypes.Sum(t => world.BuildingsOf(whose.Id, t));
    var made = simulation.OutputOf(whose.Id, GoodType.Services);
    var worth = constant.CostOf(GoodType.Services, made).Exact * 365 / 1e9;

    Console.WriteLine();
    Console.WriteLine($"{iso}: {firms} предприятий, множитель "
        + $"{world.Efficiency.OutputTimes(whose.Id, Sector.Services) / 100.0:F2}, "
        + $"выпуск {made.Exact:F0}, стоимость {worth:F2} трлн");

    Console.WriteLine($"    курс {whose.ExchangeRate.Exact,8:F2}, "
        + $"резервы {whose.State.Treasury.Reserves.Liquid.Exact / 1e6,8:F1} млрд, "
        + $"ввоз в сутки {whose.ImportsPerDay.Exact / 1e3,8:F1} млн, "
        + $"вывоз {whose.ExportsPerDay.Exact / 1e3,8:F1} млн");

    foreach (var input in new[] { GoodType.Electricity, GoodType.Fuel, GoodType.Materials,
                 GoodType.Electronics, GoodType.Medicine })
    {
        Console.WriteLine($"    {input,-12} на складе {whose.State.Stock.Of(input).Exact,10:F0}, "
            + $"просят {simulation.InputOf(whose.Id, input).Exact,8:F0}, "
            + $"своих {simulation.OutputOf(whose.Id, input).Exact,8:F0}, "
            + $"ввезли {simulation.ImportedOf(whose.Id, input).Exact,8:F0}");
    }
}

// --- Ь. Кто именно недогружен ---------------------------------------------------------

Console.WriteLine();
Console.WriteLine("=== Ь. Где стоят заводы, по типам ===");
Console.WriteLine("Доля потерянной загрузки за всю партию. Всего — сколько её у типа было.");
Console.WriteLine();
Console.WriteLine("тип                    всего   сырьё   склад   деньги   руки");

var slots = Enum.GetValues<BuildingType>()
    .Select(t => (Type: t, All: Simulation.LostBy[(int)t * 5 + 3]))
    .Where(r => r.All > 0)
    .OrderByDescending(r => r.All)
    .Take(10);

foreach (var (type, all) in slots)
{
    double Share(int why) => 100.0 * Simulation.LostBy[(int)type * 5 + why] / all;

    Console.WriteLine($"{type,-20} {all / 1e9,8:F1} {Share(0),7:F1} {Share(1),7:F1}" +
                      $" {Share(2),8:F1} {Share(4),6:F1}");
}

// --- Ы. Что выгодно строить -----------------------------------------------------------

Console.WriteLine();
Console.WriteLine("=== Ы. Что страна видит, выбирая стройку ===");
Console.WriteLine("Прибыль за сутки, плата работникам за сутки, цена постройки и во сколько");
Console.WriteLine("раз здание вернёт вложенное за свой век. Строят то, где возврат больше.");
Console.WriteLine();

foreach (var who in new[] { "CHN", "USA", "IND" })
{
    var one = world.Countries.FirstOrDefault(c => c.Iso == who);
    if (one is null) continue;

    Console.WriteLine($"{who}: занято {simulation.EmployedIn(one.Id) / 1e6:F0} млн, " +
                      $"плата за сутки всем {one.Payroll.Whole / 1e9:F1} млрд");
    Console.WriteLine("  здание                прибыль     плата      цена   возврат");

    var rows = Enum.GetValues<BuildingType>()
        .Select(t => (Type: t, Why: simulation.WhyBuild(one.Id, t)))
        .Where(r => r.Why.Cost.Raw > 0)
        .OrderByDescending(r => r.Why.Payback)
        .ToList();

    foreach (var row in rows.Take(4).Concat(rows.Where(r => r.Type is BuildingType.MaterialsPlant
                 or BuildingType.SteelMill or BuildingType.LoggingCamp)))
    {
        Console.WriteLine($"  {row.Type,-20} {row.Why.Profit.Whole,9} {row.Why.Pay.Whole,9}" +
                          $" {row.Why.Cost.Whole,9} {row.Why.Payback / 100.0,9:F2}");
    }

    Console.WriteLine();
}

// --- Ъ. Черты стран ------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== Ъ. Чем страны отличаются друг от друга ===");
Console.WriteLine("Черты розданы по признакам из жизни; курс развития складывается из них.");
Console.WriteLine();

foreach (var iso in new[] { "USA", "CHN", "DEU", "RUS", "SAU", "TUR", "IND", "NGA", "VEN", "CHE" })
{
    var whose = world.Countries.FirstOrDefault(c => c.Iso == iso);
    if (whose is null) continue;

    var mind = whose.Character;

    Console.WriteLine($"{iso}: {string.Join(", ", mind.Traits)}");
    Console.WriteLine($"     станок {mind.Prints}, заём {mind.Borrows}, армия {mind.Arms}, "
        + $"вложения {mind.Invests}, запас {mind.Hoards}, люди {mind.Feeds}, "
        + $"рынок {mind.Holds}, заводы {mind.Builds}");
}

var without = world.Countries.Count(c => c.Character.Traits.Count == 0);
Console.WriteLine();
Console.WriteLine($"Без единой черты: {without} стран из {world.Countries.Count}");

Console.WriteLine();
Console.WriteLine($"Почему ниша не нашлась: убыточно {Simulation.Stall[5]}, "
    + $"не по карману {Simulation.Stall[6]}");

Console.WriteLine();
Console.WriteLine("Где теряется загрузка за всю партию:");
{
    var all = (double)Math.Max(1, Simulation.Lost[3]);

    Console.WriteLine($"  нет сырья  {100 * Simulation.Lost[0] / all,5:F1}%");
    Console.WriteLine($"  склад полон{100 * Simulation.Lost[1] / all,5:F1}%");
    Console.WriteLine($"  нет денег  {100 * Simulation.Lost[2] / all,5:F1}%");
    Console.WriteLine($"  нет рук    {100 * Simulation.Lost[4] / all,5:F1}%");
}

// --- Я. Загрузка мощностей -----------------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== Я. Сколько страна могла дать и сколько дала ===");
Console.WriteLine("Потенциал — все предприятия на полную. В жизни загружено около 78%.");
Console.WriteLine();
Console.WriteLine("страна   потенциал трлн   факт трлн   загрузка   в жизни ВВП");

var worldCould = 0.0;
var worldDid = 0.0;

foreach (var (iso, inLife) in new[] { ("USA", 20.94), ("CHN", 24.14), ("JPN", 5.33),
             ("DEU", 4.53), ("IND", 8.91), ("GBR", 3.02), ("FRA", 3.12), ("RUS", 4.13),
             ("BRA", 3.15), ("SAU", 1.61), ("NGA", 1.00) })
{
    var id = world.Countries.First(c => c.Iso == iso).Id;
    var could = simulation.PotentialOf(id, constant).Exact * 365 / 1e9;
    var did = simulation.ValueAddedOf(id, constant).Exact * 365 / 1e9;

    Console.WriteLine($"{iso,-8} {could,15:F2} {did,11:F2} {(could > 0 ? 100 * did / could : 0),10:F0}%"
        + $" {inLife,13:F2}");
}

foreach (var country in world.Countries)
{
    worldCould += simulation.PotentialOf(country.Id, constant).Exact * 365 / 1e9;
    worldDid += simulation.ValueAddedOf(country.Id, constant).Exact * 365 / 1e9;
}

Console.WriteLine($"мир      {worldCould,15:F2} {worldDid,11:F2} "
    + $"{(worldCould > 0 ? 100 * worldDid / worldCould : 0),10:F0}%        130.00");

Console.WriteLine();
Console.WriteLine("Что именно стоит: выпуск против возможного, по товарам");

foreach (var iso in new[] { "DEU" })
{
    var whose = world.Countries.First(c => c.Iso == iso);

    Console.WriteLine($"  {iso}:");
    foreach (var good in goods)
    {
        var could = simulation.PotentialOutputOf(whose.Id, good).Exact;
        if (could < 100) continue;

        var did = simulation.OutputOf(whose.Id, good).Exact;

        Console.WriteLine($"    {good,-18} {did,10:F0} из {could,10:F0} "
            + $"({100 * did / could,5:F0}%), склад {whose.State.Stock.Of(good).Exact,10:F0}, "
            + $"просят {simulation.InputOf(whose.Id, good).Exact,8:F0}, "
            + $"заявка {simulation.BidOf(whose.Id, good).Exact,9:F0}, "
            + $"ввезли {simulation.ImportedOf(whose.Id, good).Exact,8:F0}, "
            + $"цена {whose.State.Prices.Of(good).Exact,9:F2} (старт "
            + $"{whose.State.Prices.StartOf(good).Exact,8:F2}), в мире "
            + $"{Simulation.InWorld(whose, whose.State.Prices.Of(good)).Exact,9:F2}");
    }
}

// --- Ю. Добыча против настоящей ------------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== Ю. Доля страны в мировой добыче ===");
Console.WriteLine("Доли 2020 года: BP Statistical Review по нефти и газу, IEA по углю.");
Console.WriteLine();

foreach (var (dug, inLife) in new[]
         {
             (GoodType.Oil, new[] { ("USA", 18.6), ("SAU", 12.5), ("RUS", 12.1), ("CAN", 5.9),
                 ("IRQ", 4.7), ("CHN", 4.7), ("BRA", 3.7), ("NGA", 2.0) }),
             (GoodType.Gas, new[] { ("USA", 23.7), ("RUS", 16.6), ("IRN", 6.5), ("CHN", 5.0),
                 ("QAT", 4.4), ("CAN", 4.3), ("AUS", 3.9), ("NOR", 3.1) }),
             (GoodType.Coal, new[] { ("CHN", 50.7), ("IND", 9.8), ("IDN", 7.3), ("AUS", 6.4),
                 ("USA", 6.1), ("RUS", 5.2), ("ZAF", 3.3), ("DEU", 1.1) }),
         })
{
    var everywhere = simulation.WorldOutputOf(dug).Exact;
    Console.WriteLine($"{dug}: мир {everywhere,10:F0} единиц в сутки");

    foreach (var (iso, share) in inLife)
    {
        var whose = world.Countries.FirstOrDefault(c => c.Iso == iso);
        if (whose is null) continue;

        var made = simulation.OutputOf(whose.Id, dug).Exact;

        Console.WriteLine($"   {iso}  {100 * made / Math.Max(1, everywhere),6:F1}% против {share,5:F1}%"
            + $"   ({made,10:F0} единиц)");
    }
}

// --- Э. Что тянет курс -----------------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== Э. Что тянет курс ===");
Console.WriteLine("Три силы в MoveRates, вклад каждой в шаг последнего тика, в сотых процента");
Console.WriteLine("от самого курса. В жизни за пять лет курс ходит на проценты, не в разы.");
Console.WriteLine();
Console.WriteLine("страна   от старта   паритет   сальдо   резервы   уровень цен   мировой");

foreach (var iso in new[] { "USA", "FRA", "DEU", "CHN", "JPN", "KOR", "RUS", "IND", "VNM", "BRA", "TUR" })
{
    var whose = world.Countries.First(c => c.Iso == iso);
    var push = simulation.RatePushOf(whose.Id);
    var rate = Math.Max(1, whose.ExchangeRate.Raw);

    Console.WriteLine($"{iso,-8} {Swing(whose),9:F2} "
        + $"{10_000.0 * push.Parity / rate,9:F1} {10_000.0 * push.Balance / rate,8:F1} "
        + $"{10_000.0 * push.Cushion / rate,9:F1} "
        + $"{simulation.PriceLevelOf(whose.Id) / 100.0,13:F1} {simulation.WorldPriceLevel / 100.0,9:F1}");
}

// --- Ш. ВВП по странам ---------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== Ш. ВВП по странам против настоящего ===");
Console.WriteLine("Сверяемся с ВВП по паритету покупательной способности: наш выпуск считается");
Console.WriteLine("в единых мировых ценах, а это ровно то, что меряет ППС, а не рыночный курс.");
Console.WriteLine("страна   у нас трлн   в жизни   ошибка");

foreach (var (iso, inLife) in new[] { ("USA", 20.94), ("CHN", 24.14), ("JPN", 5.33),
             ("DEU", 4.53), ("IND", 8.91), ("GBR", 3.02), ("FRA", 3.12), ("ITA", 2.40),
             ("BRA", 3.15), ("RUS", 4.13), ("KOR", 2.32), ("IDN", 3.30), ("MEX", 2.43),
             ("SAU", 1.61), ("NGA", 1.00), ("ZAF", 0.76), ("EGY", 1.29), ("VNM", 1.05) })
{
    var id = world.Countries.First(c => c.Iso == iso).Id;
    var ours = realGdp[id].Exact / years / 1e9;

    Console.WriteLine($"{iso,-8} {ours,10:F2} {inLife,9:F2} {100 * (ours / inLife - 1),8:F0}%");
}

Console.WriteLine();
Console.WriteLine("=== К. Выработка на работника ===");
Console.WriteLine("страна   у нас   в жизни   отстаёт у нас   в жизни   занято млн   рук не хватает");

// ВВП страны, делённый на занятых по данным МОТ за 2020 год. Прежняя таблица была на
// глазок и врала: у США стояло 190 при настоящих 146, у Китая 35 при 20, у России 20
// при 24. По ней и судили, а значит судили неверно.
(string Iso, int Real)[] realOutput =
    [("USA", 146), ("DEU", 86), ("JPN", 76), ("TWN", 98), ("CHN", 20),
     ("RUS", 24), ("BRA", 21), ("NGA", 7), ("IND", 6)];

var perWorker = new Dictionary<string, double>();
foreach (var (iso, _) in realOutput)
{
    var id = world.Countries.First(c => c.Iso == iso).Id;
    var employed = simulation.EmployedIn(id);
    // Средняя занятость за весь прогон, а не сегодняшняя: ВВП накоплен за те же годы,
    // и делить одно на другое можно только по одной мерке. Зарплата ниже — по ней же.
    var averageEmployed = employedYear[id] / (365.0 * years);
    perWorker[iso] = averageEmployed > 0 ? realGdp[id].Exact / years / averageEmployed : 0;
}

foreach (var (iso, realValue) in realOutput)
{
    var id = world.Countries.First(c => c.Iso == iso).Id;
    Console.WriteLine($"{iso}   {perWorker[iso],7:F1} {realValue,8} " +
                      $"{(perWorker["USA"] / Math.Max(perWorker[iso], 1e-9)),14:F1}x " +
                      $"{(190.0 / realValue),8:F1}x {simulation.EmployedIn(id) / 1e6,11:F0} " +
                      $"{(simulation.JobsIn(id) > simulation.EmployedIn(id) ? "да" : "нет"),15}");
}

Console.WriteLine();
var employedWorld = world.Countries.Sum(c => simulation.EmployedIn(c.Id));
Console.WriteLine();
Console.WriteLine("Услуги по странам: выпуск, занятость и загрузка");
var servicesWorld = world.Countries.Sum(c => simulation.OutputOf(c.Id, GoodType.Services).Exact);
foreach (var iso in new[] { "USA", "CHN", "DEU", "JPN", "IND", "NGA" })
{
    var c = world.Countries.First(x => x.Iso == iso);
    var made = simulation.OutputOf(c.Id, GoodType.Services).Exact;
    Console.WriteLine($"  {iso}  выпуск {100 * made / Math.Max(1, servicesWorld),5:F1}% мира, " +
                      $"занято {simulation.EmployedIn(c.Id) / 1e6,5:F0} млн из просимых " +
                      $"{simulation.JobsIn(c.Id) / 1e6,5:F0}, загрузка " +
                      $"{simulation.LoadIn(c.Id) * 100.0 / CapitaModern.Core.Economy.Load.Full,5:F1}%");
}

Console.WriteLine();
var ownWork = world.Countries.Sum(c => simulation.SelfEmployedIn(c.Id));

Console.WriteLine($"Занято в мире: {employedWorld / 1e6:F0} млн на предприятиях плюс " +
                  $"{ownWork / 1e6:F0} млн своим делом — всего {(employedWorld + ownWork) / 1e6:F0} млн " +
                  $"(рабочая сила {world.Countries.Sum(c => world.WorkersOf(c.Id)) / 1e6:F0} млн, " +
                  "в жизни занято 3240)");
Console.WriteLine($"В услугах: {simulation.ServiceJobs / 1e6:F0} млн (в жизни около 1600)");
Console.WriteLine($"Вне услуг: {(employedWorld - simulation.ServiceJobs) / 1e6:F0} млн, " +
                  $"из них на стройке {simulation.BuildJobs / 1e6:F0} млн " +
                  "(в наших данных о занятости 1096, в жизни на стройке около 220)");
Console.WriteLine($"Стран, где не хватает рук: {world.Countries.Count(c => simulation.JobsIn(c.Id) > simulation.EmployedIn(c.Id))}");

// --- Л. Зарплаты и внутренний оборот ----------------------------------------------
Console.WriteLine();
Console.WriteLine("=== Л. Зарплаты ===");
Console.WriteLine("страна   у нас тыс.$/год   в жизни   бюджет за тик   деньги населения");

(string Iso, double Wage)[] realWages =
    [("USA", 69.4), ("DEU", 53.7), ("JPN", 38.5), ("CHN", 13.0), ("RUS", 9.9),
     ("BRA", 7.6), ("IND", 2.1), ("NGA", 2.0)];

foreach (var (iso, realWage) in realWages)
{
    var country = world.Countries.First(c => c.Iso == iso);
    // За первый год: к пятому местные цены у экспортёров лежат на полу, и номинал врёт.
    var averageEmployed = employedYear[country.Id] / (365.0 * years);
    var yearly = averageEmployed > 0 ? wagesYear[country.Id] / years / averageEmployed : 0;

    Console.WriteLine($"{iso}   {yearly,14:F1} {realWage,9:F1} " +
                      $"{simulation.BudgetOf(country.Id).Exact / 1e6,15:F0} млн " +
                      $"{country.Households.Savings.Exact / 1e9,12:F2} трлн");
}

Console.WriteLine();
Console.WriteLine($"Местных денег в мире: у казны {world.Countries.Sum(c => c.State.Treasury.Balance.Exact) / 1e9:F1} трлн, " +
                  $"у населения {world.Countries.Sum(c => c.Households.Savings.Exact) / 1e9:F1} трлн");
Console.WriteLine($"Стран с дефицитом бюджета: {world.Countries.Count(c => simulation.BudgetOf(c.Id).Raw < 0)}");
Console.WriteLine($"Стран, где казна пуста: {world.Countries.Count(c => c.State.Treasury.Balance.Raw == 0)}");

// --- М. Коэффициент Энгеля: доля еды в расходах ------------------------------------
Console.WriteLine();
Console.WriteLine("=== М. Коэффициент Энгеля ===");
Console.WriteLine("страна   у нас   в жизни");

foreach (var (iso, real2020) in new[] { ("USA", 6.7), ("DEU", 11.0), ("CHN", 22.0), ("IND", 30.0), ("NGA", 56.0) })
{
    var id = world.Countries.First(c => c.Iso == iso).Id;
    var whose = world.CountryById(id);
    var basket = world.Needs.BaseRates
        .Aggregate(0.0, (sum, pair) => sum + whose.State.Prices.CostOf(pair.Key, pair.Value).Exact);
    var people = world.PopulationOf(id).Whole;
    var perHead = basket / 1e6;

    Console.WriteLine($"{iso}   {simulation.EngelOf(id),5}% {real2020,8:F1}%   корзин {simulation.BasketsIn(id) / 100.0,7:F1}"
        + $"   фонд {whose.Payroll.Exact / 1e3,9:F1} млн против корзины {perHead * people / 1e3,10:F1} млн");

    foreach (var (good, _) in world.Needs.BaseRates)
    {
        var wanted = simulation.PeopleWantOf(id, good);
        var got = simulation.BoughtOf(id, good);

        Console.WriteLine($"     {good,-14} хотели {wanted.Exact,10:F0}, купили {got.Exact,10:F0}, "
            + $"по стартовой цене {whose.State.Prices.StartOf(good).Exact,10:F2}, "
            + $"вышло {whose.State.Prices.CostOf(good, got).Exact / 1e3,9:F1} млн");
    }
}

Console.WriteLine();
Console.WriteLine($"Курс на полу коридора: {world.Countries.Count(c => c.ExchangeRate.Exact <= 0.02)} стран");

Console.WriteLine();
Console.WriteLine("=== Н. Стройка ===");
var plants = world.Regions.SelectMany(r => r.BuildingsCount).Sum(p => p.Value);
Console.WriteLine($"Предприятий в мире: {plants} (на старте {startPlants})");
foreach (var group in world.Regions.SelectMany(r => r.BuildingsCount)
             .GroupBy(p => p.Key).Select(g => (g.Key, Count: g.Sum(p => p.Value)))
             .OrderByDescending(g => g.Count).Take(6))
{
    Console.WriteLine($"  {group.Key,-24} {group.Count}");
}

// --- О. Эмиссия --------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== О. Печатный станок ===");
var printers = world.Countries.Where(c => c.Bank.Printed.Raw > 0)
    .OrderByDescending(c => c.Bank.Printed.Raw / Math.Max(1.0, c.Bank.Supply.Raw)).Take(6).ToArray();

Console.WriteLine($"Печатали: {world.Countries.Count(c => c.Bank.Printed.Raw > 0)} стран из {world.Countries.Count}");
foreach (var country in printers)
{
    var share = 100.0 * country.Bank.Printed.Exact / Math.Max(1, country.Bank.Supply.Exact);
    Console.WriteLine($"  {country.Iso}: напечатано {share,6:F1}% массы, валюта {Swing(country):F2} от старта");
}

// --- П. Перевозка ------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== П. Перевозка и пошлины ===");
var burned = world.Countries.Sum(c => simulation.FuelBurnedIn(c.Id).Exact);
var fuelMade = simulation.WorldOutputOf(GoodType.Fuel).Exact;
Console.WriteLine($"Топлива на перевозку: {burned:F0} из {fuelMade:F0} в сутки — {burned / Math.Max(fuelMade, 1),5:P0} " +
                  "(в жизни международная перевозка грузов берёт 8-10% нефти: море около пяти, авиагруз один, остальное фуры)");

Console.WriteLine("товар             доля перевозки   ввоз в сутки");
foreach (var good in new[] { GoodType.Materials, GoodType.Coal, GoodType.IronOre, GoodType.Food,
             GoodType.Metals, GoodType.Electronics, GoodType.Microelectronics })
{
    var brought = world.Countries.Sum(c => simulation.ImportsOf(c.Id).Exact) > 0 ? 0.0 : 0.0;
    Console.WriteLine($"{good,-18} {world.TradeCosts.FreightOf(good) / 100.0,10:F1}%");
}

Console.WriteLine();
Console.WriteLine("=== Р. Маршруты и транзит ===");
Console.WriteLine($"Прямо в океане: {world.Countries.Count(c => world.Routes.CostTo(c.Id) == 0)} " +
                  $"из {world.Countries.Count}");
foreach (var (low, high, what) in new[] { (1, 30, "через свой пролив"), (31, 100, "через чужой пролив или соседа"),
             (101, 300, "далеко от моря") })
{
    var many = world.Countries.Where(c => world.Routes.CostTo(c.Id) >= low && world.Routes.CostTo(c.Id) <= high).ToArray();
    if (many.Length == 0) continue;

    Console.WriteLine($"  {what}: {many.Length} стран ({string.Join(", ", many.Take(8).Select(c => c.Iso))})");
}

Console.WriteLine($"  отрезаны от рынка: {world.Countries.Count(c => !world.Routes.CanReachMarket(c.Id))}");
Console.WriteLine();
Console.WriteLine("Кто зарабатывает на чужом транзите:");
foreach (var country in world.Countries.OrderByDescending(c => transitYear[c.Id].Raw).Take(5))
{
    Console.WriteLine($"  {country.Iso} {transitYear[country.Id].Exact / 1e6,8:F0} млн $ за первый год");
}

Console.WriteLine($"  всего за год: {transitYear.Values.Sum(m => m.Exact) / 1e6:F0} млн $ " +
                  "(в жизни Суэц 9 млрд, Панама 5 млрд)");

// --- У. Налоги ------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== У. Налоги ===");
Console.WriteLine("страна   собрано за год   к ВВП   в жизни   НДС    НДФЛ   взносы прибыль добыча акциз  пошлина");

foreach (var (iso, inLife) in new[] { ("RUS", 33.0), ("USA", 27.0), ("DEU", 40.0), ("CHN", 21.0), ("IND", 18.0) })
{
    var whose = world.Countries.First(c => c.Iso == iso);

    // Копилось пять лет — делим на пять, и мерим в одной мере: налоги пересчитаны в
    // мировую, ВВП считается в постоянных ценах.
    var year = taxYear[whose.Id].Exact / years;
    var gdp = realGdp[whose.Id].Exact / years;

    string Share(TaxKind kind) => whose.Budget.Collected.Raw > 0
        ? $"{100.0 * whose.Budget.IncomeFrom(kind).Raw / whose.Budget.Collected.Raw,5:F1}%"
        : "    —";

    Console.WriteLine($"{iso}  {year / 1e9,14:F2} трлн {(gdp > 0 ? 100 * year / gdp : 0),6:F1}% {inLife,8:F0}% "
        + $"{Share(TaxKind.Vat)} {Share(TaxKind.Income)} {Share(TaxKind.Payroll)} "
        + $"{Share(TaxKind.Profit)} {Share(TaxKind.Extraction)} {Share(TaxKind.Excise)} "
        + $"{Share(TaxKind.Tariff)}");
}

// --- Т. Компании ---------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== Т. Компании ===");
Console.WriteLine($"Всего компаний: {world.Companies.Count}");

foreach (var iso in new[] { "RUS", "USA", "CHN" })
{
    var whose = world.Countries.First(c => c.Iso == iso);
    var mine = world.CompaniesOf(whose.Id);

    Console.WriteLine($"{iso}: {mine.Count} компаний ({mine.Count(c => c.Known)} известных), "
        + $"зданий {mine.Sum(c => c.Size)}, "
        + $"денег {mine.Sum(c => c.Cash.Exact) / 1e6:F0} млн, "
        + $"многопрофильных {mine.Count(c => c.Focus.Count > 1)}");

    foreach (var company in mine.OrderByDescending(c => c.Size).Take(5))
    {
        Console.WriteLine($"   {company.Name,-26} зданий {company.Size,8} "
            + $"денег {company.Cash.Exact / 1e6,10:F0} млн  отрасли {company.Focus.Count}"
            + (company.Known ? "  известная" : string.Empty));
    }
}

Console.WriteLine();
Console.WriteLine($"Живых компаний: {world.Companies.Count(c => c.Alive)}, "
    + $"разорилось за {years} лет: {world.Countries.Sum(c => simulation.RuinedIn(c.Id))}, "
    + $"продано зданий {world.Countries.Sum(c => simulation.SoldIn(c.Id))}");
Console.WriteLine($"Вклады в банках: {world.Countries.Sum(c => c.Banks.Deposits.Exact) / 1e9:F1} трлн, "
    + $"роздано компаниям {world.Countries.Sum(c => c.Banks.Lent.Exact) / 1e9:F1} трлн, "
    + $"долг компаний {world.Companies.Sum(c => c.Debt.Exact) / 1e9:F1} трлн");

foreach (var iso in new[] { "RUS", "USA", "CHN", "DEU" })
{
    var whose = world.Countries.First(c => c.Iso == iso);
    var mine = world.CompaniesOf(whose.Id);

    Console.WriteLine($"{iso}: живых {mine.Count(c => c.Alive)} из {mine.Count}, "
        + $"разорилось {simulation.RuinedIn(whose.Id)}, "
        + $"вклады {whose.Banks.Deposits.Exact / 1e6:F0} млн, "
        + $"роздано {whose.Banks.Lent.Exact / 1e6:F0} млн");
}

// --- Х. Армия ------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== Х. Госзаказ на оружие ===");
Console.WriteLine("Военные расходы к выпуску: заказ считается от настоящей доли 2020 года,");
Console.WriteLine("а купить получается лишь то, на что хватило бюджета и что нашлось на складе.");
Console.WriteLine();
Console.WriteLine("страна   хотел   купил за день   вышло к выпуску   в жизни");

foreach (var (iso, inLife) in new[] { ("USA", 3.7), ("CHN", 1.7), ("RUS", 4.3), ("SAU", 8.4),
             ("DEU", 1.4), ("IND", 2.9), ("JPN", 1.0), ("BRA", 1.4) })
{
    var whose = world.Countries.First(c => c.Iso == iso);
    var added = simulation.ValueAddedOf(whose.Id);
    var spent = simulation.ArmsBoughtOf(whose.Id);
    var got = added.Raw > 0 ? 100.0 * spent.Exact / added.Exact : 0;

    Console.WriteLine($"{iso}  {whose.DefenceShare / 100.0,6:F2}% {spent.Exact / 1e3,12:F1} млн $ "
        + $"{got,14:F2}% {inLife,8:F1}%");
}

var armed = world.Countries.Count(c => c.Army.Kit.Values.Any(a => a.Raw > 0));
// В мировой мере: у каждой страны свои деньги, и складывать рубли с иенами нельзя.
var worldArms = world.Countries
    .Sum(c => Simulation.InWorld(c, simulation.ArmsBoughtOf(c.Id)).Exact) * 365;

Console.WriteLine();
Console.WriteLine($"Вооружились: {armed} стран из {world.Countries.Count}");
Console.WriteLine($"Мировые военные расходы: {worldArms / 1e9:F2} трлн $ за год "
    + "(в жизни 2.0 трлн $, 2.4% мирового ВВП)");

Console.WriteLine();
Console.WriteLine("Что лежит в арсенале России:");
foreach (var (good, amount) in world.Countries.First(c => c.Iso == "RUS").Army.Kit
             .OrderByDescending(pair => pair.Value.Raw).Take(5))
{
    Console.WriteLine($"  {good,-18} {amount.Exact,12:F1}");
}

// --- Ц. Жильё и дороги ---------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== Ц. Жильё и дороги ===");
Console.WriteLine("Главные покупатели стройматериалов и леса. Жильё люди строят на свои,");
Console.WriteLine("дороги государство из бюджета — и больше, чем в бюджете есть, не построит.");
Console.WriteLine();
Console.WriteLine("страна   жильё к выпуску   дороги к выпуску   в жизни 5.0% и 3.3%");

foreach (var iso in new[] { "USA", "CHN", "RUS", "DEU", "IND", "BRA", "JPN", "NGA" })
{
    var whose = world.Countries.First(c => c.Iso == iso);
    var added = simulation.ValueAddedOf(whose.Id).Exact;
    var houses = added > 0 ? 100 * simulation.HousingOf(whose.Id).Exact / added : 0;
    var roads = added > 0 ? 100 * simulation.RoadsOf(whose.Id).Exact / added : 0;

    Console.WriteLine($"{iso}  {houses,14:F2}% {roads,17:F2}%");
}

Console.WriteLine();
Console.WriteLine("Сходится ли счёт компаний со счётом областей:");
{
    var inRegions = world.Regions.Sum(r => r.BuildingsCount.Sum(pair => (long)pair.Value));
    var inFirms = world.Companies.Where(c => c.Alive).Sum(c => (long)c.Size);
    var gap = inRegions > 0 ? 100.0 * Math.Abs(inRegions - inFirms) / inRegions : 0;

    Console.WriteLine($"  в областях {inRegions}, у компаний {inFirms}, расхождение {gap:F1}%");
}

Console.WriteLine();
Console.WriteLine("Сходятся ли именные доли со складом страны:");
foreach (var iso in new[] { "USA", "CHN", "RUS", "DEU" })
{
    var whose = world.Countries.First(c => c.Iso == iso);
    var mine = world.CompaniesOf(whose.Id).Sum(c => c.Goods.Sum(pair => pair.Amount.Exact));
    var onShelf = goods.Sum(good => whose.State.Stock.Of(good).Exact);
    var gap = onShelf > 0 ? 100 * Math.Abs(onShelf - mine) / onShelf : 0;

    Console.WriteLine($"  {iso}: на складе {onShelf,14:F0}, у компаний {mine,14:F0}, расхождение {gap:F2}%");
}

Console.WriteLine();
Console.WriteLine("Разброс цен внутри страны (100 — как у всех):");
foreach (var iso in new[] { "USA", "CHN", "RUS", "DEU" })
{
    var whose = world.Countries.First(c => c.Iso == iso);
    var mine = world.CompaniesOf(whose.Id).Where(c => c.Alive).ToArray();

    foreach (var good in new[] { GoodType.Food, GoodType.Coal, GoodType.Materials })
    {
        var edges = mine.Where(c => c.Holds(good).Raw > 0).Select(c => c.Edge(good)).ToArray();
        if (edges.Length < 2) continue;

        Console.WriteLine($"  {iso} {good,-12} продавцов {edges.Length,3}, "
            + $"от {edges.Min()} до {edges.Max()}, средняя {edges.Average():F0}");
    }
}

Console.WriteLine();
Console.WriteLine("Казённые услуги к выпуску (в жизни конечное потребление государства 17%):");
foreach (var iso in new[] { "USA", "CHN", "RUS", "DEU", "IND", "BRA" })
{
    var whose = world.Countries.First(c => c.Iso == iso);
    var added = simulation.ValueAddedOf(whose.Id).Exact;

    Console.WriteLine($"  {iso}: {(added > 0 ? 100 * simulation.StateServicesOf(whose.Id).Exact / added : 0),6:F2}%");
}

Console.WriteLine();
Console.WriteLine("Жилой фонд и дороги, в материалах:");
foreach (var iso in new[] { "USA", "CHN", "RUS", "IND" })
{
    var whose = world.Countries.First(c => c.Iso == iso);

    Console.WriteLine($"  {iso}: жильё {whose.Estate.Housing.Exact,14:F0}, "
        + $"дороги {whose.Estate.Roads.Exact,14:F0}");
}

// --- Ф. Выбросы ----------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== Ф. Выбросы ===");
Console.WriteLine("Где цены ушли дальше всего и что их туда двинуло. Масса — во сколько раз");
Console.WriteLine("выросла денежная масса, печать — сколько из неё напечатано.");
Console.WriteLine();
Console.WriteLine("страна  уровень цен   масса   печать   курс    на рельсах");

foreach (var country in world.Countries
             .OrderByDescending(c => simulation.PriceLevelOf(c.Id))
             .Take(8))
{
    var level = simulation.PriceLevelOf(country.Id) / (double)PriceLevel.Scale;
    var supply = country.Bank.Start.Raw > 0
        ? country.Bank.Supply.Exact / country.Bank.Start.Exact
        : 0;
    var printed = 100.0 * country.Bank.Printed.Exact / Math.Max(1, country.Bank.Supply.Exact);
    var rails = goods.Count(good =>
    {
        var times = country.State.Prices.Of(good).Exact / country.State.Prices.StartOf(good).Exact;
        return times >= Prices.MaxSwingTimes || times <= 1.0 / Prices.MaxSwingTimes;
    });

    Console.WriteLine($"{country.Iso}   x{level,10:F1} x{supply,6:F1} {printed,7:F1}% "
        + $"x{country.ExchangeRate.Exact,6:F2} {rails,6} из {goods.Length}");
}

Console.WriteLine();
Console.WriteLine("Сбережения населения: во сколько годовых закупок еды они выросли.");
Console.WriteLine("В жизни у людей на руках лежит меньше годового дохода, а не сотни.");
Console.WriteLine();
Console.WriteLine("страна   сбережения    вклады   к годовой еде");

foreach (var country in world.Countries
             .OrderByDescending(c => c.Households.Savings.Exact + c.Banks.Deposits.Exact)
             .Take(6))
{
    var all = country.Households.Savings + country.Banks.Deposits;
    var foodYear = country.State.Prices.CostOf(
        GoodType.Food, simulation.PeopleWantOf(country.Id, GoodType.Food)).Exact * 365;

    Console.WriteLine($"{country.Iso}  {country.Households.Savings.Exact / 1e6,10:F0} млн "
        + $"{country.Banks.Deposits.Exact / 1e6,10:F0} млн "
        + $"{(foodYear > 0 ? all.Exact / foodYear : 0),10:F1}");
}

Console.WriteLine();
Console.WriteLine("Какие товары чаще всего упираются в коридор:");

foreach (var good in goods
             .OrderByDescending(good => world.Countries.Count(c =>
             {
                 var times = c.State.Prices.Of(good).Exact / c.State.Prices.StartOf(good).Exact;
                 return times >= Prices.MaxSwingTimes || times <= 1.0 / Prices.MaxSwingTimes;
             }))
             .Take(8))
{
    var up = world.Countries.Count(c =>
        c.State.Prices.Of(good).Exact / c.State.Prices.StartOf(good).Exact >= Prices.MaxSwingTimes);
    var down = world.Countries.Count(c =>
        c.State.Prices.Of(good).Exact / c.State.Prices.StartOf(good).Exact <= 1.0 / Prices.MaxSwingTimes);
    if (up + down == 0) continue;

    Console.WriteLine($"  {good,-18} в потолок {up,4} стран, в пол {down,4}");
}

// --- С. Свои цены против мировых ---------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== С. Свои цены против мировых ===");
Console.WriteLine("Закон одной цены: свободно возимый товар не может стоить в стране заметно");
Console.WriteLine("дороже привозного. Сравнивать надо в одних деньгах, потому мировая цена");
Console.WriteLine("переведена по курсу.");
Console.WriteLine();

Console.WriteLine("товар            выпуск мира   спрос мира   покрытие");
foreach (var good in Enum.GetValues<GoodType>())
{
    if (good == GoodType.Services) continue;

    var made = world.Countries.Sum(c => simulation.OutputOf(c.Id, good).Exact);
    var eaten = world.Countries.Sum(c => simulation.InputOf(c.Id, good).Exact);
    if (eaten <= 0) continue;

    Console.WriteLine($"{good,-16} {made,12:F0} {eaten,12:F0} {made / eaten,10:P0}");
}

var rus = world.Countries.First(c => c.Iso == "RUS");

Console.WriteLine();
Console.WriteLine($"Россия, курс {rus.ExchangeRate.Exact:F2}");
Console.WriteLine("товар               наша   мир в рублях   к миру   дней   спрос    свой    ввоз");
foreach (var good in new[]
{
    GoodType.Materials, GoodType.Metals, GoodType.Electricity, GoodType.Coal,
    GoodType.IronOre, GoodType.Food, GoodType.Oil,
})
{
    var mine = rus.State.Prices.Of(good).Exact;
    var abroad = world.Market.Prices.Of(good).Exact * rus.ExchangeRate.Exact;
    var want = simulation.InputOf(rus.Id, good).Exact;
    var days = want > 0 ? rus.State.Stock.Of(good).Exact / want : 0;

    Console.WriteLine($"{good,-14} {mine,10:F2} {abroad,14:F2} {(abroad > 0 ? mine / abroad : 0),8:F2} {days,6:F0}"
        + $" {want,8:F0} {simulation.OutputOf(rus.Id, good).Exact,8:F0} {simulation.ImportedOf(rus.Id, good).Exact,8:F0}");
}
