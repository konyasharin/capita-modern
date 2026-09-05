using CapitaModern.Core.Economy;
using CapitaModern.Core.Loading;
using CapitaModern.Core.World;
using Xunit;

namespace CapitaModern.Core.Tests;

/// <summary>Проверки на настоящих файлах из data: ловят рассинхрон кода и данных.</summary>
public class WorldDataTests
{
    private static readonly Lazy<GameWorld> Shared = new(Load);

    private static GameWorld Load()
    {
        var root = RepoPaths.GetRepoRoot();
        string Read(params string[] parts) => File.ReadAllText(Path.Combine([root, .. parts]));

        return WorldDataLoader.LoadWorld(
            Read("data", "map", "countries.json"),
            Read("data", "map", "regions.json"),
            Read("data", "economy", "buildings.json"),
            Read("data", "economy", "start-industry.json"));
    }

    [Fact]
    public void WorldLoads()
    {
        var world = Shared.Value;

        Assert.Equal(200, world.Countries.Count);
        Assert.Equal(2606, world.Regions.Count);
    }

    [Fact]
    public void StartingIndustryIsThere()
    {
        var total = Shared.Value.Regions
            .SelectMany(region => region.BuildingsCount)
            .Sum(pair => pair.Value);

        Assert.Equal(11899, total);
    }

    [Fact]
    public void CountriesAreFoundById()
    {
        var russia = Shared.Value.Countries.First(country => country.Iso == "RUS");

        Assert.Same(russia, Shared.Value.CountryById(russia.Id));
    }

    [Fact]
    public void UnknownCountryIsAnError()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Shared.Value.CountryById(255));
    }

    [Fact]
    public void EveryBuildingTypeHasARecipe()
    {
        foreach (var type in Enum.GetValues<CapitaModern.Core.Buildings.BuildingType>())
        {
            Assert.NotNull(Shared.Value.Buildings[type]);
        }
    }

    /// <summary>Год симуляции не должен уводить ни один склад в минус.</summary>
    [Fact]
    public void YearOfTicksKeepsStocksPositive()
    {
        var world = Load();
        var simulation = new Simulation(world);

        for (var tick = 0; tick < 365; tick++)
        {
            simulation.Tick();
        }

        foreach (var country in world.Countries)
        {
            foreach (var good in Enum.GetValues<GoodType>())
            {
                Assert.True(country.StockOf(good) >= default(GoodAmount),
                    $"{country.Iso} ушла в минус по {good}");
            }
        }
    }

    [Fact]
    public void ExtractionHappensOnlyWhereDepositsAre()
    {
        var world = Load();
        new Simulation(world).Tick();

        var minedWithoutDeposit = world.Regions
            .SelectMany(region => region.BuildingsCount
                .Where(pair => world.Buildings[pair.Key].RequiresDeposit is { } deposit
                    && !region.HasDeposit(deposit)))
            .Count();

        var mining = world.Regions
            .SelectMany(region => region.BuildingsCount)
            .Count(pair => world.Buildings[pair.Key].RequiresDeposit is not null);

        Assert.Equal(0, minedWithoutDeposit);
        Assert.True(mining > 0, "в данных вообще нет добывающих предприятий");
    }

    [Fact]
    public void TickIsReproducible()
    {
        static long Run()
        {
            var world = Load();
            var simulation = new Simulation(world);
            for (var tick = 0; tick < 30; tick++) simulation.Tick();

            return world.Countries.Sum(country => country.StockOf(GoodType.Metals).Raw);
        }

        Assert.Equal(Run(), Run());
    }
}
