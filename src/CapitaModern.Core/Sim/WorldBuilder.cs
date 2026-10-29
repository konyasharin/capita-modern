using CapitaModern.Core.Data;
using CapitaModern.Core.Economy;
using CapitaModern.Core.World;

namespace CapitaModern.Core.Sim;

/// <summary>Собирает мир на первое января 2020-го. Реальные числа задают только расстановку на старте.</summary>
public static class WorldBuilder
{
    /// <summary>Страны с плановой экономикой на старте.</summary>
    private static readonly HashSet<string> Planned = ["PRK", "CUB"];

    /// <summary>Сколько дивизий во всём мире на старте; делятся по военным расходам.</summary>
    public const int WorldDivisions = 320;

    private static readonly Dictionary<string, string> SectorNames = new()
    {
        ["Mining"] = "добыча", ["Power"] = "энергетика", ["Heavy"] = "тяжпром",
        ["Civil"] = "промышленность", ["Military"] = "оборонка", ["Services"] = "услуги",
    };

    public static GameWorld Build(GameData data)
    {
        var catalog = new Catalog(data.Goods, data.Buildings, data.Prices);
        var goods = catalog.Goods.Count;

        var countryById = new Country?[256];
        foreach (var c in data.Countries.All)
        {
            countryById[c.Id] = new Country
            {
                Id = c.Id,
                Iso = c.Iso,
                Name = c.Name,
                System = Planned.Contains(c.Iso) ? EconomicSystem.Planned : EconomicSystem.Market,
                DefenceShare = data.Defence.GetValueOrDefault(c.Iso, data.DefaultDefence) / 10_000.0,
                Stock = new double[goods],
                PriceMult = Enumerable.Repeat(1.0, goods).ToArray(),
                DailyUse = new double[goods],
                DemandWeek = new double[goods],
                Fill = Enumerable.Repeat(1.0, goods).ToArray(),
                CapacityUse = new double[goods],
                CapacityOut = new double[goods],
                ExportUse = new double[goods],
                ProducedWeek = new double[goods],
                ConsumedWeek = new double[goods],
                ImportedWeek = new double[goods],
                ExportedWeek = new double[goods],
            };
        }

        var regionById = new Region?[data.Grid.Center.Length];
        foreach (var r in data.Regions)
        {
            if (countryById[r.Country] is null) continue;

            var deposits = new Dictionary<int, double>();
            foreach (var (good, amount) in r.Deposits)
            {
                if (catalog.TryGood(good, out var index)) deposits[index] = amount;
            }

            regionById[r.Id] = new Region
            {
                Id = r.Id,
                Name = r.Name,
                Owner = r.Country,
                Controller = r.Country,
                Population = r.Population,
                Deposits = deposits,
                Neighbours = data.Grid.Neighbours[r.Id],
                X = data.Grid.Center[r.Id].X,
                Y = data.Grid.Center[r.Id].Y,
            };
        }

        var needs = data.Consumption
            .Where(pair => catalog.TryGood(pair.Key, out _))
            .Select(pair => (catalog.GoodIndex(pair.Key), pair.Value))
            .ToArray();
        var elasticity = needs
            .Select(n => data.IncomeElasticity.GetValueOrDefault(catalog.Goods[n.Item1].Id, 100) / 100.0)
            .ToArray();

        var world = new GameWorld
        {
            Catalog = catalog,
            Map = data.Map,
            Grid = data.Grid,
            CountryById = countryById,
            RegionById = regionById,
            Templates = Templates(data, catalog),
            Needs = needs,
            NeedElasticity = elasticity,
        };

        SetNeeds(world, data);
        PlacePlants(world, data, Scale(world, data));
        foreach (var country in world.Countries) Prepare(world, country);
        PlaceArmies(world);
        Balance(world);
        foreach (var country in world.Countries) Prepare(world, country);

        return world;
    }

    /// <summary>Заводы из стартовой раскладки и хозяева к ним: при рынке компании, при плане государство.</summary>
    /// <summary>Во сколько раз умножить заводы страны по группам: (услуги, прочие).</summary>
    /// <remarks>
    /// Добыча стоит там, где месторождения, её не трогаем. Услуги не возят через границу, поэтому
    /// их мощность подгоняется под нужды самой страны. Прочие отрасли — так, чтобы ВВП страны на
    /// старте был пропорционален настоящему.
    /// </remarks>
    private static Dictionary<int, (double Services, double Other)> Scale(GameWorld world, GameData data)
    {
        var catalog = world.Catalog;
        var services = catalog.GoodIndex("Services");
        var mining = new Dictionary<int, double>();
        var serviceAdded = new Dictionary<int, double>();
        var serviceOut = new Dictionary<int, double>();
        var other = new Dictionary<int, double>();

        foreach (var region in world.Regions)
        {
            if (!data.StartIndustry.TryGetValue(region.Id, out var plants)) continue;

            foreach (var (type, count) in plants)
            {
                var building = catalog.Buildings[catalog.BuildingIndex(type)];
                var added = count * (Value(catalog, building.Outputs) - Value(catalog, building.Inputs));
                var output = count * building.Outputs.Where(o => o.Good == services).Sum(o => o.Amount);

                if (output > 0)
                {
                    serviceAdded[region.Owner] = serviceAdded.GetValueOrDefault(region.Owner) + added;
                    serviceOut[region.Owner] = serviceOut.GetValueOrDefault(region.Owner) + output;
                }
                else
                {
                    var into = building.Sector == "Mining" ? mining : other;
                    into[region.Owner] = into.GetValueOrDefault(region.Owner) + added;
                }
            }
        }

        var worldAdded = mining.Values.Sum() + serviceAdded.Values.Sum() + other.Values.Sum();
        var worldGdp = data.Countries.All.Sum(c => c.Gdp);
        var scale = new Dictionary<int, (double, double)>();

        foreach (var c in data.Countries.All)
        {
            var country = world.CountryById[c.Id];
            if (country is null) continue;

            var people = world.OwnedBy(c.Id).Sum(r => (double)r.Population) / 1e6;
            var need = country.Needs.Where(n => n.Good == services).Sum(n => n.Rate * people);
            var made = serviceOut.GetValueOrDefault(c.Id);
            var forServices = made > 0 ? Math.Clamp(need * 1.05 / made, 0.1, 20) : 1;

            var own = other.GetValueOrDefault(c.Id);
            var forOther = 1.0;
            if (own > 0 && worldGdp > 0)
            {
                var target = c.Gdp / worldGdp * worldAdded - mining.GetValueOrDefault(c.Id)
                    - serviceAdded.GetValueOrDefault(c.Id) * forServices;
                forOther = Math.Clamp(target / own, 0.25, 10);
            }

            scale[c.Id] = (forServices, forOther);
        }

        return scale;
    }

    /// <summary>Потребление на душу идёт за доходом: еда почти не растёт с богатством, услуги и
    /// потребтовары растут быстрее него — закон Энгеля.</summary>
    private static void SetNeeds(GameWorld world, GameData data)
    {
        var people = data.Countries.All.Sum(c => (double)c.Population);
        var average = people > 0 ? data.Countries.All.Sum(c => c.Gdp) / people : 0;

        foreach (var c in data.Countries.All)
        {
            var country = world.CountryById[c.Id];
            if (country is null) continue;

            var income = average > 0 && c.Population > 0 ? c.Gdp / c.Population / average : 1;
            income = Math.Clamp(income, 0.05, 20);

            country.Needs = world.Needs
                .Select(n => (n.Good, n.Rate * Math.Pow(income,
                    data.IncomeElasticity.GetValueOrDefault(world.Catalog.Goods[n.Good].Id, 100) / 100.0)))
                .ToArray();
        }

        // Мировой итог по каждому товару тот же, что в данных: доход меняет только, кому сколько.
        for (var i = 0; i < world.Needs.Length; i++)
        {
            var target = people / 1e6 * world.Needs[i].Rate;
            var total = data.Countries.All
                .Where(c => world.CountryById[c.Id] is not null)
                .Sum(c => c.Population / 1e6 * world.CountryById[c.Id]!.Needs[i].Rate);
            if (total <= 0) continue;

            foreach (var country in world.Countries)
            {
                if (country.Needs.Length > i) country.Needs[i].Rate *= target / total;
            }
        }
    }

    private static void PlacePlants(GameWorld world, GameData data, Dictionary<int, (double Services, double Other)> scale)
    {
        var catalog = world.Catalog;
        var bySector = new Dictionary<(int Country, string Sector), List<Company>>();
        var turn = new Dictionary<(int, string), int>();

        foreach (var region in world.Regions)
        {
            if (!data.StartIndustry.TryGetValue(region.Id, out var plants)) continue;

            var country = world.Country(region.Owner);
            foreach (var (type, start) in plants)
            {
                if (start <= 0) continue;

                var building = catalog.Buildings[catalog.BuildingIndex(type)];
                var factor = scale.GetValueOrDefault(country.Id, (1, 1));
                var count = building.Sector == "Mining"
                    ? start
                    : Math.Max(1, (int)Math.Round(start * (building.Sector == "Services" ? factor.Services : factor.Other)));
                var owner = Company.State;

                if (country.System == EconomicSystem.Market && building.Type != "PublicService")
                {
                    var key = (country.Id, building.Sector);
                    if (!bySector.TryGetValue(key, out var list))
                    {
                        list = FoundCompanies(world, data, country, building.Sector);
                        bySector[key] = list;
                    }

                    if (list.Count > 0)
                    {
                        var next = turn.GetValueOrDefault(key);
                        owner = list[next % list.Count].Id;
                        turn[key] = next + 1;
                    }
                }

                region.Plants.Add(new PlantStack(building.Index, count, owner));
            }
        }
    }

    /// <summary>Компании отрасли в стране: именные из данных, а нет таких — одна безымянная.</summary>
    private static List<Company> FoundCompanies(GameWorld world, GameData data, Country country, string sector)
    {
        var names = data.Companies.GetValueOrDefault(country.Iso)?
            .Where(c => c.Sector == sector)
            .Select(c => c.Name)
            .ToList() ?? [];

        // Оборонку без именных компаний держит государство.
        if (names.Count == 0 && sector == "Military") return [];
        if (names.Count == 0) names.Add($"{country.Iso} {SectorNames.GetValueOrDefault(sector, sector)}");

        var list = new List<Company>();
        foreach (var name in names)
        {
            var company = new Company { Id = world.Companies.Count, Name = name, Country = country.Id, Sector = sector };
            world.Companies.Add(company);
            list.Add(company);
        }

        return list;
    }

    /// <summary>Склады на норму запаса, ВВП, казна и столица до первого тика.</summary>
    private static void Prepare(GameWorld world, Country country)
    {
        var catalog = world.Catalog;
        var regions = world.OwnedBy(country.Id).ToList();
        if (regions.Count == 0) return;

        country.Capital = regions.MaxBy(r => r.Population)!.Id;

        var population = regions.Sum(r => (double)r.Population);
        Array.Clear(country.DailyUse);
        foreach (var (good, rate) in country.Needs) country.DailyUse[good] += population / 1e6 * rate;

        var added = 0.0;
        var workers = 0.0;
        foreach (var plant in regions.SelectMany(r => r.Plants))
        {
            var building = catalog.Buildings[plant.Building];
            foreach (var (good, amount) in building.Inputs) country.DailyUse[good] += amount * plant.Count;

            added += plant.Count * (Value(catalog, building.Outputs) - Value(catalog, building.Inputs));
            workers += plant.Count * building.Workers;
            plant.Load = 1.0;
        }

        for (var g = 0; g < catalog.Goods.Count; g++)
        {
            if (!catalog.Goods[g].Perishable) country.Stock[g] = country.DailyUse[g] * EconomyStep.NormDays;
        }

        country.GdpReal = added * 365;
        country.Potential = country.GdpReal;
        country.IncomeStart = population > 0 ? country.Potential / population : 0;
        country.Needs0 = country.Needs.ToArray();
        country.Treasury = country.GdpReal * 0.05;
        country.Workforce0 = population * EconomyStep.WorkingShare;
        country.WorkNeed0 = Math.Max(1, workers);
    }

    /// <summary>Мощность каждой отрасли мира — спрос на её товар с запасом.</summary>
    private const double Margin = 1.15;

    /// <summary>Приводит мировую мощность каждого товара к мировому спросу, сохраняя, где что стоит.</summary>
    /// <remarks>
    /// Стартовая раскладка досталась от прежней модели с множителями выпуска, и без них отрасли
    /// вышли раздутыми вразнобой: нефтедобычи в девять раз больше мирового спроса. Тогда заводы
    /// стоят на крохах мощности, а кто из стран продаёт, решает случай. Спрос считается сверху
    /// вниз за несколько проходов: нужды людей и армий, потом заводы, которые их кормят, потом их сырьё.
    /// </remarks>
    private static void Balance(GameWorld world)
    {
        var catalog = world.Catalog;
        var goods = catalog.Goods.Count;
        var plants = world.Regions.SelectMany(r => r.Plants).ToList();

        var final = new double[goods];
        foreach (var country in world.Countries)
        {
            var people = world.OwnedBy(country.Id).Sum(r => (double)r.Population) / 1e6;
            foreach (var (good, rate) in country.Needs) final[good] += people * rate;
        }

        foreach (var division in world.Divisions)
        {
            foreach (var (good, amount) in world.Templates[division.Template].Equipment) final[good] += amount * 0.10 / 365;
        }

        // Стройка — тоже потребитель: закладываем рост капитала на несколько процентов в год.
        foreach (var plant in plants)
        {
            foreach (var (good, amount) in catalog.Buildings[plant.Building].BuildCost)
            {
                final[good] += plant.Count * amount * BuildStep.GrowthPlan / 365;
            }
        }

        var size = plants.ToDictionary(p => p, p => (double)p.Count);
        for (var pass = 0; pass < 8; pass++)
        {
            var use = (double[])final.Clone();
            var made = new double[goods];

            foreach (var (plant, count) in size)
            {
                var building = catalog.Buildings[plant.Building];
                foreach (var (good, amount) in building.Inputs) use[good] += count * amount;
                foreach (var (good, amount) in building.Outputs) made[good] += count * amount;
            }

            foreach (var plant in plants)
            {
                var building = catalog.Buildings[plant.Building];
                if (building.Outputs.Length == 0 || catalog.Goods[building.Outputs[0].Good].Perishable) continue;

                var good = building.Outputs[0].Good;
                if (made[good] <= 0) continue;

                size[plant] *= Math.Clamp(use[good] * Margin / made[good], 0.5, 2.0);
            }
        }

        // Округляем с переносом остатка внутри типа, чтобы итог по миру не уплывал.
        var carry = new double[catalog.Buildings.Count];
        foreach (var plant in plants)
        {
            var exact = size[plant] + carry[plant.Building];
            var whole = (int)Math.Floor(exact);
            carry[plant.Building] = exact - whole;
            plant.Count = whole;
        }

        foreach (var region in world.Regions) region.Plants.RemoveAll(p => p.Count <= 0);
    }

    private static double Value(Catalog catalog, (int Good, double Amount)[] items) =>
        items.Sum(item => item.Amount * catalog.Goods[item.Good].BasePrice);

    private static List<DivisionTemplate> Templates(GameData data, Catalog catalog)
    {
        var onMap = data.Divisions.Where(d => d.Role is "ground" or "support").ToList();
        double Worth(DivisionDto d) => d.Equipment.Sum(e => e.Value * catalog.Goods[catalog.GoodIndex(e.Key)].BasePrice);

        var unit = Worth(onMap.First(d => d.Type == "MotorRifle"));

        return onMap.Select((d, i) => new DivisionTemplate(
            i, d.Type, d.Name, d.Role == "ground", d.Manpower,
            d.Equipment.Select(e => (catalog.GoodIndex(e.Key), e.Value)).ToArray(),
            d.Upkeep.Select(e => (catalog.GoodIndex(e.Key), e.Value)).ToArray(),
            Worth(d) / unit)).ToList();
    }

    /// <summary>Армии по военным расходам: доля в мировых военных тратах — доля дивизий.</summary>
    private static void PlaceArmies(GameWorld world)
    {
        var spend = world.Countries.ToDictionary(c => c.Id, c => c.GdpReal * c.DefenceShare);
        var total = spend.Values.Sum();
        if (total <= 0) return;

        string[] mix = ["Infantry", "MotorRifle", "Infantry", "Tank", "MotorRifle", "Airborne", "Infantry", "Marine"];
        var byType = world.Templates.ToDictionary(t => t.Type, t => t.Index);

        foreach (var country in world.Countries)
        {
            var count = (int)Math.Round(WorldDivisions * spend[country.Id] / total);
            if (count == 0 && world.OwnedBy(country.Id).Sum(r => r.Population) > 3_000_000) count = 1;
            if (count == 0) continue;

            var regions = world.OwnedBy(country.Id).OrderByDescending(r => r.Population).ToList();
            for (var i = 0; i < count; i++)
            {
                world.Divisions.Add(new Division
                {
                    Id = world.NextDivisionId++,
                    Country = country.Id,
                    Template = byType[mix[i % mix.Length]],
                    Region = regions[i % regions.Count].Id,
                });
            }
        }
    }
}
