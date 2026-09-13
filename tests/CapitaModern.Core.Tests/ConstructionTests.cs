using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;
using CapitaModern.Core.World;
using Xunit;

namespace CapitaModern.Core.Tests;

/// <summary>Стройка: её ограничивают не только деньги и материалы, но и люди.</summary>
public class ConstructionTests
{
    private const GoodType Coal = GoodType.Coal;
    private const BuildingType Mine = BuildingType.CoalMine;

    /// <param name="needs">Сколько угля в сутки нужно миллиону человек. Без покупателя
    /// стране нечего заработать, а значит и нечего отложить на стройку.</param>
    private static GameWorld WorldWith(int population, int workersPerMine, long needs = 1000) => new(
        [Build.Region(1, 1, new Dictionary<BuildingType, int> { [Mine] = 10 },
            deposits: new Dictionary<GoodType, int> { [Coal] = 100 },
            population: population)],
        [Build.Country(1, stock: new Dictionary<GoodType, GoodAmount>
            {
                [GoodType.Materials] = Build.Whole(1_000_000),
                [GoodType.Metals] = Build.Whole(1_000_000),
            },
            prices: new Dictionary<GoodType, Money> { [Coal] = Money.FromWhole(100) },
            money: 100_000_000, supply: 100_000_000, savings: 100_000_000)],
        Build.Catalog(Build.Info(Mine,
            outputs: new() { [Coal] = Build.Whole(10) },
            deposit: Coal,
            workers: workersPerMine,
            buildCost: new() { [GoodType.Materials] = Build.Whole(100) },
            buildWorkers: 50)),
        new Needs(new Dictionary<GoodType, GoodAmount> { [Coal] = Build.Whole(needs) }),
        Build.Market(new Dictionary<GoodType, Money> { [Coal] = Money.FromWhole(100) }),
        new Elasticity());

    private static int MinesIn(GameWorld world) =>
        world.Regions.SelectMany(region => region.BuildingsCount)
            .Where(pair => pair.Key == Mine).Sum(pair => pair.Value);

    /// <summary>Люди есть — шахты прибавляются: деньги и материалы в достатке.</summary>
    /// <summary>Возведённое входит в ВВП: материалы стройки уже вычтены как потраченные,
    /// и без этого страна, которая строит, теряет на этом измеренный выпуск.</summary>
    [Fact]
    public void BuildingCountsTowardsValueAdded()
    {
        var world = WorldWith(population: 50, workersPerMine: 1);
        var simulation = new Simulation(world);
        var prices = world.CountryById(1).State.Prices;

        var wasBuilt = 0L;
        var checkedDay = false;

        for (var tick = 0; tick < 60 && !checkedDay; tick++)
        {
            simulation.Tick();
            if (simulation.BuiltSoFar == wasBuilt) continue;

            wasBuilt = simulation.BuiltSoFar;
            checkedDay = true;

            // Простая разница «выпуск минус потраченное» — то, чем ВВП был до правки.
            var plain = default(Money);
            foreach (var good in Enum.GetValues<GoodType>())
            {
                plain += prices.CostOf(good, simulation.OutputOf(1, good));
                plain -= prices.CostOf(good, simulation.ConsumedOf(1, good));
            }

            Assert.True(simulation.ValueAddedOf(1) > plain,
                $"возведённое не попало в ВВП: {simulation.ValueAddedOf(1).Exact:F0} против {plain.Exact:F0}");
        }

        Assert.True(checkedDay, "страна так ничего и не построила, проверять нечего");
    }

    [Fact]
    public void FreeHandsLetTheCountryBuild()
    {
        var world = WorldWith(population: 100_000, workersPerMine: 10);
        var simulation = new Simulation(world);

        for (var tick = 0; tick < 50; tick++) simulation.Tick();

        Assert.True(MinesIn(world) > 10, "страна со свободными руками так и не построила ничего");
    }

    /// <summary>Все руки уже на шахтах — строить некому, сколько бы ни было денег.</summary>
    [Fact]
    public void WithoutFreeHandsNothingIsBuilt()
    {
        var world = WorldWith(population: 1000, workersPerMine: 1_000_000);
        var simulation = new Simulation(world);

        for (var tick = 0; tick < 50; tick++) simulation.Tick();

        Assert.Equal(10, MinesIn(world));
    }
}
