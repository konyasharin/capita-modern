using CapitaModern.Core.Economy;
using CapitaModern.Core.World;

namespace CapitaModern.Core.Sim;

/// <summary>Стройка. Кто строит, решает экономический строй страны.</summary>
public static class BuildStep
{
    public const double BuildDays = 180;

    /// <summary>Сколько недель вперёд материалы должны быть на складе под уже идущие стройки.</summary>
    private const int MaterialWeeks = 8;

    /// <summary>Сколько недель трат казна держит в запасе, прежде чем вкладываться.</summary>
    private const int ReserveWeeks = 8;

    /// <summary>Как часто компания может начать новую стройку, в сутках.</summary>
    private const int CompanyPause = 28;

    private const int CompanyProjects = 3;

    /// <summary>Какую долю уже стоящих зданий этого типа в стране строят разом.</summary>
    private const double BatchShare = 0.005;

    /// <summary>Партией строят, только когда товара заметно не хватает; иначе по одному зданию.</summary>
    private const double BatchShortage = 1.05;

    /// <summary>При рынке государство вкладывается само, только когда цена товара настолько выше обычной.</summary>
    private const double MarketStateShortage = 1.25;

    /// <summary>Ниже этой доли полученного сырья новые заводы на нём не строят.</summary>
    private const double InputsFill = 0.95;

    /// <summary>Цена услуг, когда людям не хватает каждой десятой.</summary>
    private const double ServicesShortage = 1.05;

    /// <summary>Для замера: нечего строить, мало денег, пауза, начато, нет рук.</summary>
    public static readonly long[] Stall = new long[5];

    public static void Run(GameWorld world)
    {
        Progress(world);

        var held = world.Regions
            .Where(r => r.Owner == r.Controller)
            .GroupBy(r => r.Owner)
            .ToDictionary(g => g.Key, g => g.ToList());

        var busy = world.Projects.GroupBy(p => p.Company).ToDictionary(g => g.Key, g => g.Count());
        var reserved = Reserved(world);
        var coming = world.Projects.GroupBy(p => p.Country)
            .ToDictionary(g => g.Key, g => g.Sum(p => world.Catalog.Buildings[p.Building].Workers * p.Count));

        foreach (var country in world.Countries)
        {
            if (!held.TryGetValue(country.Id, out var regions)) continue;

            var booked = reserved.GetValueOrDefault(country.Id) ?? new double[world.Catalog.Goods.Count];
            var hands = FreeHands(world, country, regions) - coming.GetValueOrDefault(country.Id);
            // Сначала государство закрывает явный дефицит, потом компании — руки достаются нужному в первую очередь.
            if (country.AutoBuild) StateBuilds(world, country, regions, booked, ref hands);
            if (country.System == EconomicSystem.Market) CompaniesBuild(world, country, regions, busy, booked, ref hands);
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
                var need = amount * part * project.Count;
                country.DemandWeek[good] += need;
                fill = Math.Min(fill, need <= 0 ? 1 : country.Stock[good] / need);
            }

            fill = Math.Clamp(fill, 0, 1);
            foreach (var (good, amount) in building.BuildCost) country.Stock[good] -= amount * part * project.Count * fill;

            project.DaysDone += EconomyStep.WeekDays * fill;
            if (project.DaysDone < project.DaysTotal) continue;

            world.Projects.Remove(project);
            // Компания могла разориться или уйти под план, пока шла стройка: тогда завод государству.
            var owner = world.CompanyOf(project.Company) is { Alive: true } company ? company.Id : Company.State;
            Raise(region, project.Building, owner, project.Count);
        }
    }

    public static void Raise(Region region, int building, int company, int count = 1)
    {
        var stack = region.Plants.FirstOrDefault(p => p.Building == building && p.Company == company);
        if (stack is null) region.Plants.Add(new PlantStack(building, count, company));
        else stack.Count += count;
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
                need[good] += amount * project.Count * EconomyStep.WeekDays / project.DaysTotal * MaterialWeeks;
            }
        }

        return reserved;
    }

    /// <summary>Хватит ли материалов на новую стройку сверх обещанного идущим. Если да — бронирует.</summary>
    private static bool Book(GameWorld world, Country country, double[] booked, int building, int count)
    {
        var cost = world.Catalog.Buildings[building].BuildCost;
        foreach (var (good, amount) in cost)
        {
            if (country.Stock[good] < booked[good] + amount * count) return false;
        }

        foreach (var (good, amount) in cost) booked[good] += amount * count;
        return true;
    }

    /// <summary>Сколько зданий строить разом: до доли от уже стоящих в стране, сколько позволяют деньги и руки.</summary>
    /// <remarks>По одному зданию в месяц большая страна не могла нарастить добычу, и к 30-му году в мире кончалась нефть.</remarks>
    private static int Batch(
        Country country, List<Region> regions, Building building, double money, double cost, double hands, double[] booked)
    {
        var short_ = building.Outputs.Max(o => country.PriceMult[o.Good]) >= BatchShortage;
        var standing = short_ ? regions.Sum(r => r.Count(building.Index)) : 0;
        var count = Math.Max(1, (int)(standing * BatchShare));
        if (cost > 0) count = Math.Min(count, (int)(money / cost));
        if (building.Workers > 0) count = Math.Min(count, (int)(hands / building.Workers));
        foreach (var (good, amount) in building.BuildCost)
        {
            if (amount > 0) count = Math.Min(count, (int)((country.Stock[good] - booked[good]) / amount));
        }

        return Math.Max(1, count);
    }

    /// <summary>Сколько рабочих мест ещё можно открыть, чтобы на старых заводах людей не стало меньше, чем на старте.</summary>
    /// <remarks>Без этого стройка отнимала руки у услуг: у Ботсваны за 8 лет услуг стало на треть меньше.</remarks>
    private static double FreeHands(GameWorld world, Country country, List<Region> regions)
    {
        if (country.Workforce0 <= 0) return 0;

        var workforce = regions.Sum(r => (double)r.Population) * EconomyStep.WorkingShare;
        var jobs = EconomyStep.Jobs(world.Catalog, regions.SelectMany(r => r.Plants));
        return workforce / country.Workforce0 * country.WorkNeed0 - jobs;
    }

    private static void CompaniesBuild(
        GameWorld world, Country country, List<Region> regions, Dictionary<int, int> busy, double[] booked, ref double hands)
    {
        var wishes = new List<(Company Company, (int Building, int Region, double Score) Choice)>();
        foreach (var company in world.Companies.Where(c => c.Alive && c.Country == country.Id))
        {
            if (world.Day - company.BuiltAt < CompanyPause || busy.GetValueOrDefault(company.Id) >= CompanyProjects)
            {
                Stall[2]++;
                continue;
            }

            var wish = Best(world, country, regions, b => b.Sector == company.Sector && b.Type != "PublicService", 0.9);
            if (wish is null)
            {
                Stall[0]++;
                continue;
            }

            wishes.Add((company, wish.Value));
        }

        // Руки получает тот, чей товар нужнее и выгоднее, а не тот, кто первый в списке.
        foreach (var (company, choice) in wishes.OrderByDescending(w => w.Choice.Score))
        {

            var building = world.Catalog.Buildings[choice.Building];
            var cost = Cost(world.Catalog, country, choice.Building);
            if (company.Cash < cost)
            {
                Stall[1]++;
                continue;
            }

            // Самому нужному из тех, кому по карману, не хватает рук — остальные ждут, а не разбирают руки по мелочи.
            if (building.Workers > hands)
            {
                Stall[4]++;
                break;
            }

            var count = Batch(country, regions, building, company.Cash, cost, hands, booked);
            if (!Book(world, country, booked, choice.Building, count))
            {
                Stall[1]++;
                continue;
            }

            Stall[3]++;
            hands -= building.Workers * count;

            company.Cash -= cost * count;
            company.BuiltAt = world.Day;
            Start(world, country, choice.Building, choice.Region, company.Id, count);
        }
    }

    /// <summary>Государство строит из запаса казны. При рынке — только туда, где явный дефицит.</summary>
    private static void StateBuilds(GameWorld world, Country country, List<Region> regions, double[] booked, ref double hands)
    {
        var weekly = country.GdpReal * country.PriceLevel / 52 * (EconomyStep.CivilSpending + country.DefenceShare);
        var spare = country.Treasury - weekly * ReserveWeeks;

        var bar = country.System == EconomicSystem.Market ? MarketStateShortage : 0.9;
        for (var tries = 0; tries < 4 && spare > 0; tries++)
        {
            var choice = Best(world, country, regions, _ => true, bar);
            if (choice is null) return;

            var building = world.Catalog.Buildings[choice.Value.Building];
            var cost = Cost(world.Catalog, country, choice.Value.Building);
            if (building.Workers > hands || cost > spare) return;

            var count = Batch(country, regions, building, spare, cost, hands, booked);
            if (!Book(world, country, booked, choice.Value.Building, count)) return;

            hands -= building.Workers * count;
            country.Treasury -= cost * count;
            spare -= cost * count;
            Start(world, country, choice.Value.Building, choice.Value.Region, Company.State, count);
        }
    }

    public static void Start(GameWorld world, Country country, int building, int region, int company, int count = 1) =>
        world.Projects.Add(new Project
        {
            Building = building,
            Region = region,
            Country = country.Id,
            Company = company,
            DaysTotal = BuildDays,
            Count = count,
        });

    public static double Cost(Catalog catalog, Country country, int building) =>
        catalog.Buildings[building].BuildCost.Sum(c => c.Amount * country.Price(c.Good, catalog.Goods[c.Good].BasePrice));

    /// <summary>Что выгоднее всего строить: доход на вложенное, умноженный на дефицит товара.</summary>
    /// <param name="bar">Ниже какого множителя цены товара его не строят: значит, его хватает.</param>
    private static (int Building, int Region, double Score)? Best(
        GameWorld world, Country country, List<Region> regions, Func<Building, bool> allowed, double bar)
    {
        var catalog = world.Catalog;
        (int, int, double)? best = null;
        var bestScore = 0.0;

        foreach (var building in catalog.Buildings.Where(allowed))
        {
            if (building.Outputs.Length == 0 || building.BuildCost.Length == 0) continue;

            // Услуги не привезёшь из-за границы: их достраивают, как только людям не хватает каждой десятой.
            var scarcity = building.Outputs.Max(o => country.PriceMult[o.Good]);
            var enough = building.Outputs.Any(o => catalog.Goods[o.Good].Perishable) ? Math.Min(bar, ServicesShortage) : bar;
            if (scarcity < enough) continue;

            // Сырьё не доходит и до стоящих заводов — новый не из чего кормить: иначе металлургию строили
            // где угодно, спрос на бокситы рос всемеро, и нехватка расползалась по цепочке.
            if (building.Inputs.Any(i => country.Fill[i.Good] < InputsFill)) continue;

            var revenue = building.Outputs.Sum(o => o.Amount * country.Price(o.Good, catalog.Goods[o.Good].BasePrice));
            var cost = building.Inputs.Sum(i => i.Amount * country.Price(i.Good, catalog.Goods[i.Good].BasePrice));
            var price = Cost(catalog, country, building.Index);
            if (revenue <= cost || price <= 0) continue;

            var score = (revenue - cost) / price * Math.Pow(scarcity, 3);
            if (score <= bestScore) continue;

            var region = Place(regions, building);
            if (region is null) continue;

            bestScore = score;
            best = (building.Index, region.Id, score);
        }

        return best;
    }

    /// <summary>Добыча — туда, где месторождение; остальное — где людей много, а таких зданий мало.</summary>
    /// <remarks>Рудник ставят по остатку запаса, а не по стартовому богатству: на выработанное не идут.</remarks>
    public static Region? Place(IEnumerable<Region> regions, Building building) =>
        regions
            .Where(r => building.Deposit < 0 || r.Deposits.ContainsKey(building.Deposit) && EconomyStep.Richness(r, building) >= 1)
            .MaxBy(r => building.Deposit >= 0
                ? r.Reserve.GetValueOrDefault(building.Deposit, r.Deposits[building.Deposit]) / (1.0 + r.Count(building.Index))
                : r.Population / (1.0 + r.Count(building.Index)));
}
