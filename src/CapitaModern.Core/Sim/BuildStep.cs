using CapitaModern.Core.Economy;
using CapitaModern.Core.World;

namespace CapitaModern.Core.Sim;

/// <summary>Стройка. Кто строит, решает экономический строй страны.</summary>
public static class BuildStep
{
    public const double BuildDays = 180;

    /// <summary>На какой рост капитала в год рассчитаны стройматериалы мира.</summary>
    public const double GrowthPlan = 0.03;

    /// <summary>Сколько недель вперёд материалы должны быть на складе под уже идущие стройки.</summary>
    private const int MaterialWeeks = 8;

    /// <summary>Сколько недель трат казна держит в запасе, прежде чем вкладываться.</summary>
    private const int ReserveWeeks = 8;

    /// <summary>Как часто компания может начать новую стройку, в сутках.</summary>
    private const int CompanyPause = 28;

    private const int CompanyProjects = 3;

    /// <summary>При рынке государство вкладывается само, только когда цена товара настолько выше обычной.</summary>
    private const double MarketStateShortage = 1.25;

    /// <summary>Для замера: нечего строить, мало денег, пауза, начато.</summary>
    public static readonly long[] Stall = new long[4];

    public static void Run(GameWorld world)
    {
        Progress(world);

        var held = world.Regions
            .Where(r => r.Owner == r.Controller)
            .GroupBy(r => r.Owner)
            .ToDictionary(g => g.Key, g => g.ToList());

        var busy = world.Projects.GroupBy(p => p.Company).ToDictionary(g => g.Key, g => g.Count());
        var reserved = Reserved(world);

        foreach (var country in world.Countries)
        {
            if (!held.TryGetValue(country.Id, out var regions)) continue;

            var booked = reserved.GetValueOrDefault(country.Id) ?? new double[world.Catalog.Goods.Count];
            if (country.System == EconomicSystem.Market) CompaniesBuild(world, country, regions, busy, booked);
            if (country.AutoBuild) StateBuilds(world, country, regions, booked);
        }
    }

    /// <summary>Стройка идёт, пока хватает материалов; готовое встаёт в регион.</summary>
    private static void Progress(GameWorld world)
    {
        var catalog = world.Catalog;

        foreach (var project in world.Projects.ToList())
        {
            var region = world.Region(project.Region);
            var country = world.Country(project.Country);

            if (region.Controller != project.Country)
            {
                world.Projects.Remove(project);
                continue;
            }

            var building = catalog.Buildings[project.Building];
            var part = EconomyStep.WeekDays / project.DaysTotal;
            var fill = 1.0;

            foreach (var (good, amount) in building.BuildCost)
            {
                var need = amount * part;
                country.DemandWeek[good] += need;
                fill = Math.Min(fill, need <= 0 ? 1 : country.Stock[good] / need);
            }

            fill = Math.Clamp(fill, 0, 1);
            foreach (var (good, amount) in building.BuildCost) country.Stock[good] -= amount * part * fill;

            project.DaysDone += EconomyStep.WeekDays * fill;
            if (project.DaysDone < project.DaysTotal) continue;

            world.Projects.Remove(project);
            Raise(region, project.Building, project.Company);
        }
    }

    public static void Raise(Region region, int building, int company)
    {
        var stack = region.Plants.FirstOrDefault(p => p.Building == building && p.Company == company);
        if (stack is null) region.Plants.Add(new PlantStack(building, 1, company));
        else stack.Count++;
    }

    /// <summary>Сколько материалов страна уже обещала идущим стройкам на ближайшие недели.</summary>
    private static Dictionary<int, double[]> Reserved(GameWorld world)
    {
        var reserved = new Dictionary<int, double[]>();
        foreach (var project in world.Projects)
        {
            if (!reserved.TryGetValue(project.Country, out var need))
            {
                need = new double[world.Catalog.Goods.Count];
                reserved[project.Country] = need;
            }

            foreach (var (good, amount) in world.Catalog.Buildings[project.Building].BuildCost)
            {
                need[good] += amount * EconomyStep.WeekDays / project.DaysTotal * MaterialWeeks;
            }
        }

        return reserved;
    }

    /// <summary>Хватит ли материалов на новую стройку сверх обещанного идущим. Если да — бронирует.</summary>
    private static bool Book(GameWorld world, Country country, double[] booked, int building)
    {
        var cost = world.Catalog.Buildings[building].BuildCost;
        foreach (var (good, amount) in cost)
        {
            if (country.Stock[good] < booked[good] + amount) return false;
        }

        foreach (var (good, amount) in cost) booked[good] += amount;
        return true;
    }

    private static void CompaniesBuild(
        GameWorld world, Country country, List<Region> regions, Dictionary<int, int> busy, double[] booked)
    {
        foreach (var company in world.Companies.Where(c => c.Alive && c.Country == country.Id))
        {
            if (world.Day - company.BuiltAt < CompanyPause || busy.GetValueOrDefault(company.Id) >= CompanyProjects)
            {
                Stall[2]++;
                continue;
            }

            var choice = Best(world, country, regions, b => b.Sector == company.Sector && b.Type != "PublicService", 0.9);
            if (choice is null)
            {
                Stall[0]++;
                continue;
            }

            var cost = Cost(world.Catalog, country, choice.Value.Building);
            if (company.Cash < cost || !Book(world, country, booked, choice.Value.Building))
            {
                Stall[1]++;
                continue;
            }

            Stall[3]++;

            company.Cash -= cost;
            company.BuiltAt = world.Day;
            Start(world, country, choice.Value.Building, choice.Value.Region, company.Id);
        }
    }

    /// <summary>Государство строит из запаса казны. При рынке — только туда, где явный дефицит.</summary>
    private static void StateBuilds(GameWorld world, Country country, List<Region> regions, double[] booked)
    {
        var weekly = country.GdpReal * country.PriceLevel / 52 * (EconomyStep.CivilSpending + country.DefenceShare);
        var spare = country.Treasury - weekly * ReserveWeeks;

        var bar = country.System == EconomicSystem.Market ? MarketStateShortage : 0.9;
        for (var tries = 0; tries < 4 && spare > 0; tries++)
        {
            var choice = Best(world, country, regions, _ => true, bar);
            if (choice is null) return;

            var cost = Cost(world.Catalog, country, choice.Value.Building);
            if (cost > spare || !Book(world, country, booked, choice.Value.Building)) return;

            country.Treasury -= cost;
            spare -= cost;
            Start(world, country, choice.Value.Building, choice.Value.Region, Company.State);
        }
    }

    public static void Start(GameWorld world, Country country, int building, int region, int company) =>
        world.Projects.Add(new Project
        {
            Building = building,
            Region = region,
            Country = country.Id,
            Company = company,
            DaysTotal = BuildDays,
        });

    public static double Cost(Catalog catalog, Country country, int building) =>
        catalog.Buildings[building].BuildCost.Sum(c => c.Amount * country.Price(c.Good, catalog.Goods[c.Good].BasePrice));

    /// <summary>Что выгоднее всего строить: доход на вложенное, умноженный на дефицит товара.</summary>
    /// <param name="bar">Ниже какого множителя цены товара его не строят: значит, его хватает.</param>
    private static (int Building, int Region)? Best(
        GameWorld world, Country country, List<Region> regions, Func<Building, bool> allowed, double bar)
    {
        var catalog = world.Catalog;
        (int, int)? best = null;
        var bestScore = 0.0;

        foreach (var building in catalog.Buildings.Where(allowed))
        {
            if (building.Outputs.Length == 0 || building.BuildCost.Length == 0) continue;

            var scarcity = building.Outputs.Max(o => country.PriceMult[o.Good]);
            if (scarcity < bar) continue;

            var revenue = building.Outputs.Sum(o => o.Amount * country.Price(o.Good, catalog.Goods[o.Good].BasePrice));
            var cost = building.Inputs.Sum(i => i.Amount * country.Price(i.Good, catalog.Goods[i.Good].BasePrice));
            var price = Cost(catalog, country, building.Index);
            if (revenue <= cost || price <= 0) continue;

            var score = (revenue - cost) / price * Math.Pow(scarcity, 3);
            if (score <= bestScore) continue;

            var region = Place(regions, building);
            if (region is null) continue;

            bestScore = score;
            best = (building.Index, region.Id);
        }

        return best;
    }

    /// <summary>Добыча — туда, где месторождение; остальное — где людей много, а таких зданий мало.</summary>
    public static Region? Place(IEnumerable<Region> regions, Building building) =>
        regions
            .Where(r => building.Deposit < 0 || r.Deposits.ContainsKey(building.Deposit))
            .MaxBy(r => building.Deposit >= 0
                ? r.Deposits[building.Deposit] / (1.0 + r.Count(building.Index))
                : r.Population / (1.0 + r.Count(building.Index)));
}
