using CapitaModern.Core.Data;
using CapitaModern.Core.World;

namespace CapitaModern.Core.Sim;

/// <summary>Партия: сутки идут одни за другими, экономика считается раз в неделю.</summary>
public sealed class Game
{
    public GameWorld World { get; }

    /// <summary>Страна игрока; её дивизиями боты не управляют.</summary>
    public int? Player { get; set; }

    /// <summary>Сработало после каждых суток и после каждой недели — для интерфейса.</summary>
    public event Action? DayPassed;
    public event Action? WeekPassed;

    public Game(GameWorld world) => World = world;

    public static Game Load(Func<string, string> readText, Func<string, byte[]> readBytes) =>
        new(WorldBuilder.Build(GameData.Load(readText, readBytes)));

    public void Tick()
    {
        World.Day++;
        World.Date = World.Date.AddDays(1);

        WarStep.Day(World, Player);

        if (World.Day % EconomyStep.WeekDays == 0)
        {
            EconomyStep.Run(World);
            TradeStep.Run(World);
            BuildStep.Run(World);
            WarStep.Reinforce(World);
            EconomyStep.Close(World);
            WeekPassed?.Invoke();
        }

        DayPassed?.Invoke();
    }

    public void DeclareWar(int a, int b)
    {
        if (a == b) return;
        World.Wars.Add(a < b ? (a, b) : (b, a));
    }

    /// <summary>Мир на условиях «что держишь, то и твоё»: занятые регионы переходят держателю.</summary>
    public void MakePeace(int a, int b)
    {
        if (!World.Wars.Remove(a < b ? (a, b) : (b, a))) return;

        foreach (var region in World.Regions.Where(r => (r.Owner == a && r.Controller == b) || (r.Owner == b && r.Controller == a)))
        {
            Transfer(region.Id, region.Controller);
        }

        // Бои с третьими странами идут дальше: останавливаются только дивизии, шедшие на бывшего врага.
        foreach (var division in World.Divisions.Where(d => d.Country == a || d.Country == b))
        {
            var enemy = division.Country == a ? b : a;
            var heading = division.Target >= 0 ? division.Target : division.Goal;
            if (heading >= 0 && World.Region(heading).Controller == enemy)
            {
                division.InBattle = false;
                division.Target = -1;
                division.Goal = -1;
                division.Progress = 0;
            }
        }
    }

    /// <summary>Столица, ушедшая к другой стране, переезжает в самый людный свой регион.</summary>
    private void FixCapital(Country country)
    {
        if (World.Region(country.Capital).Owner == country.Id) return;

        var best = World.OwnedBy(country.Id).MaxBy(r => r.Population);
        if (best is not null) country.Capital = best.Id;
    }

    /// <summary>Дивизии, оказавшиеся на чужой мирной земле, уходят в свою столицу.</summary>
    private void SendHome(Region region)
    {
        foreach (var division in World.Divisions.Where(d => d.Alive && d.Region == region.Id && d.Country != region.Controller))
        {
            if (World.AtWar(division.Country, region.Controller)) continue;

            division.Region = World.Country(division.Country).Capital;
            division.InBattle = false;
            division.Target = -1;
            division.Goal = -1;
            division.Progress = 0;
        }
    }

    /// <summary>Регион целиком уходит другой стране: и владение, и контроль, и заводы.</summary>
    /// <remarks>Чужим компаниям в новой стране работать не на кого, их заводы забирает государство.</remarks>
    public void Transfer(int regionId, int country)
    {
        var region = World.Region(regionId);
        var previous = World.Country(region.Owner);
        region.Owner = country;
        region.Controller = country;

        FixCapital(previous);
        SendHome(region);
        EconomyStep.Supply(World, previous);
        EconomyStep.Supply(World, World.Country(country));

        foreach (var plant in region.Plants)
        {
            if (World.CompanyOf(plant.Company) is { } company && company.Country != country) plant.Company = Company.State;
        }

        World.Projects.RemoveAll(p => p.Region == regionId && p.Country != country);
    }

    /// <summary>Стройка за счёт казны. Возвращает false, если денег не хватило.</summary>
    public bool Build(int country, int building, int region)
    {
        var owner = World.Country(country);
        var cost = BuildStep.Cost(World.Catalog, owner, building);
        var place = World.Region(region);
        var info = World.Catalog.Buildings[building];

        if (owner.Treasury < cost || place.Controller != country) return false;
        if (info.Deposit >= 0 && (!place.Deposits.ContainsKey(info.Deposit) || EconomyStep.Richness(place, info) < 1)) return false;

        owner.Treasury -= cost;
        BuildStep.Start(World, owner, building, region, Company.State);
        return true;
    }

    public void Move(Division division, int goal) => WarStep.Order(World, division, goal);

    /// <summary>Смена строя. План забирает всё у компаний; рынок отдаёт государственные заводы новым компаниям.</summary>
    public void SetSystem(int countryId, EconomicSystem system)
    {
        var country = World.Country(countryId);
        if (country.System == system) return;

        country.System = system;
        var plants = World.Regions.Where(r => r.Owner == countryId).SelectMany(r => r.Plants).ToList();

        if (system == EconomicSystem.Planned)
        {
            foreach (var company in World.Companies.Where(c => c.Country == countryId && c.Alive))
            {
                company.Alive = false;
                country.Treasury += Math.Max(0, company.Cash);
                company.Cash = 0;
            }

            foreach (var plant in plants) plant.Company = Company.State;
            return;
        }

        var bySector = new Dictionary<string, Company>();
        foreach (var plant in plants)
        {
            var building = World.Catalog.Buildings[plant.Building];
            if (plant.Company != Company.State || building.Type == "PublicService") continue;

            if (!bySector.TryGetValue(building.Sector, out var company))
            {
                company = new Company
                {
                    Id = World.Companies.Count,
                    Name = $"{country.Iso} {building.Sector}",
                    Country = countryId,
                    Sector = building.Sector,
                };
                World.Companies.Add(company);
                bySector[building.Sector] = company;
            }

            plant.Company = company.Id;
        }
    }
}
