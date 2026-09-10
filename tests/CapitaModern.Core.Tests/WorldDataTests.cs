using System.Text.Json;
using CapitaModern.Core.Economy;
using CapitaModern.Core.Loading;
using CapitaModern.Core.World;
using Xunit;

namespace CapitaModern.Core.Tests;

/// <summary>Проверки на настоящих файлах из data: ловят рассинхрон кода и данных.</summary>
public class WorldDataTests
{
    /// <summary>Настоящий мир грузится один раз на все тесты: это секунды.</summary>
    internal static readonly Lazy<GameWorld> Shared = new(Load);

    private static GameWorld Load()
    {
        var root = RepoPaths.GetRepoRoot();
        string Read(params string[] parts) => File.ReadAllText(Path.Combine([root, .. parts]));

        return WorldDataLoader.LoadWorld(
            Read("data", "map", "countries.json"),
            Read("data", "map", "regions.json"),
            Read("data", "economy", "buildings.json"),
            Read("data", "economy", "start-industry.json"),
            Read("data", "economy", "consumption.json"),
            Read("data", "economy", "prices.json"),
            Read("data", "economy", "reserves.json"),
            Read("data", "economy", "goods.json"),
            Read("data", "economy", "key-rates.json"),
            Read("data", "politics", "blocs.json"));
    }

    [Fact]
    public void WorldLoads()
    {
        var world = Shared.Value;

        Assert.Equal(200, world.Countries.Count);
        Assert.Equal(2606, world.Regions.Count);
    }

    /// <summary>Загрузчик не должен терять предприятия: сверяемся с самим файлом.</summary>
    [Fact]
    public void StartingIndustryIsThere()
    {
        var loaded = Shared.Value.Regions
            .SelectMany(region => region.BuildingsCount)
            .Sum(pair => pair.Value);

        var inFile = JsonDocument
            .Parse(File.ReadAllText(Path.Combine(RepoPaths.GetRepoRoot(), "data", "economy", "start-industry.json")))
            .RootElement.GetProperty("regions").EnumerateObject()
            .Sum(region => region.Value.EnumerateObject().Sum(building => building.Value.GetInt32()));

        Assert.Equal(inFile, loaded);
        Assert.True(loaded > 10_000, "стартовая промышленность подозрительно мала");
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
                Assert.True(country.State.Stock.Of(good) >= default(GoodAmount),
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

    /// <summary>Товар без стартовой цены молча получил бы единицу, и перекос по нему
    /// поехал бы с первого тика.</summary>
    [Fact]
    public void EveryGoodHasAStartPrice()
    {
        var inFile = JsonDocument
            .Parse(File.ReadAllText(Path.Combine(RepoPaths.GetRepoRoot(), "data", "economy", "prices.json")))
            .RootElement.GetProperty("prices");

        foreach (var good in Enum.GetValues<GoodType>())
        {
            Assert.True(inFile.TryGetProperty(good.ToString(), out var price), $"нет цены на {good}");
            Assert.True(price.GetInt32() > 0, $"цена {good} должна быть больше нуля");
        }
    }

    /// <summary>За год цена не должна ни улететь на порядки, ни лечь на пол: и то и
    /// другое означало бы, что шаг подобран неверно.</summary>
    [Fact]
    public void YearOfTicksKeepsPricesSane()
    {
        var world = Load();
        var russia = world.Countries.First(country => country.Iso == "RUS");
        var start = Enum.GetValues<GoodType>().ToDictionary(good => good, russia.State.Prices.Of);

        var simulation = new Simulation(world);
        for (var tick = 0; tick < 365; tick++) simulation.Tick();

        foreach (var good in Enum.GetValues<GoodType>())
        {
            var now = russia.State.Prices.Of(good);
            Assert.True(now.Raw >= start[good].Raw / Prices.MaxSwingTimes, $"{good} упал ниже коридора");
            Assert.True(now.Raw <= start[good].Raw * Prices.MaxSwingTimes, $"{good} вырос выше коридора");
        }
    }

    [Fact]
    public void TickIsReproducible()
    {
        static long Run()
        {
            var world = Load();
            var simulation = new Simulation(world);
            for (var tick = 0; tick < 30; tick++) simulation.Tick();

            return world.Countries.Sum(country => country.State.Stock.Of(GoodType.Metals).Raw);
        }

        Assert.Equal(Run(), Run());
    }
}
