using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;
using CapitaModern.Core.Politics;
using CapitaModern.Core.World;
using Xunit;

namespace CapitaModern.Core.Tests;

/// <summary>Население ест со склада наравне с заводами и по тому же весу.</summary>
public class ConsumptionTests
{
    private const BuildingType Farm = BuildingType.Farm;

    /// <summary>Ставка 2 единицы еды на миллион человек в сутки.</summary>
    private static Dictionary<GoodType, GoodAmount> Rate =>
        new() { [GoodType.Food] = Build.Whole(2) };

    private static GameWorld WorldWith(
        long millions,
        long foodInStock,
        Dictionary<BuildingType, int>? buildings = null,
        Dictionary<Sector, int>? weights = null,
        Dictionary<GoodType, GoodAmount>? rate = null) =>
        new(
            [Build.Region(1, 1, buildings ?? [], population: (int)(millions * 1_000_000))],
            [Build.Country(1, new Dictionary<GoodType, GoodAmount> { [GoodType.Food] = Build.Whole(foodInStock) }, weights)],
            Build.Catalog(Build.Info(Farm, outputs: new() { [GoodType.Food] = Build.Whole(1) }, sector: Sector.Mining)),
            rate ?? Rate);

    [Fact]
    public void PeopleEatFromTheStock()
    {
        var world = WorldWith(millions: 3, foodInStock: 100);

        new Simulation(world).Tick();

        Assert.Equal(Build.Whole(94), world.CountryById(1).Stock.Of(GoodType.Food));
    }

    [Fact]
    public void EatingAddsUpOverTicks()
    {
        var world = WorldWith(millions: 3, foodInStock: 100);

        var simulation = new Simulation(world);
        for (var tick = 0; tick < 5; tick++) simulation.Tick();

        Assert.Equal(Build.Whole(70), world.CountryById(1).Stock.Of(GoodType.Food));
    }

    /// <summary>Вдвое больше людей — вдвое больше съедено.</summary>
    [Fact]
    public void BiggerPopulationEatsMore()
    {
        var small = WorldWith(millions: 3, foodInStock: 100);
        var big = WorldWith(millions: 6, foodInStock: 100);

        new Simulation(small).Tick();
        new Simulation(big).Tick();

        Assert.Equal(Build.Whole(94), small.CountryById(1).Stock.Of(GoodType.Food));
        Assert.Equal(Build.Whole(88), big.CountryById(1).Stock.Of(GoodType.Food));
    }

    [Fact]
    public void EmptyStockMeansNothingEaten()
    {
        var world = WorldWith(millions: 3, foodInStock: 0);

        new Simulation(world).Tick();

        Assert.Equal(default, world.CountryById(1).Stock.Of(GoodType.Food));
    }

    /// <summary>Съесть больше, чем лежит, нельзя — склад просто опустеет.</summary>
    [Fact]
    public void StockNeverGoesNegative()
    {
        var world = WorldWith(millions: 100, foodInStock: 10);

        var simulation = new Simulation(world);
        for (var tick = 0; tick < 3; tick++) simulation.Tick();

        Assert.Equal(default, world.CountryById(1).Stock.Of(GoodType.Food));
    }

    /// <summary>Свежая еда достаётся следующему тику: население ест до пополнения складов.</summary>
    [Fact]
    public void FreshFoodIsEatenNextTick()
    {
        var world = WorldWith(millions: 1, foodInStock: 0,
            buildings: new Dictionary<BuildingType, int> { [Farm] = 10 });

        var simulation = new Simulation(world);
        simulation.Tick();

        Assert.Equal(Build.Whole(10), world.CountryById(1).Stock.Of(GoodType.Food));

        simulation.Tick();

        Assert.Equal(Build.Whole(18), world.CountryById(1).Stock.Of(GoodType.Food));
    }

    /// <summary>Население делит нехватку с заводами по весу, а не берёт вне очереди.</summary>
    [Fact]
    public void PeopleShareShortageWithIndustry()
    {
        var world = new GameWorld(
            [Build.Region(1, 1, new Dictionary<BuildingType, int> { [BuildingType.FoodPlant] = 1 },
                population: 1_000_000)],
            [Build.Country(1, new Dictionary<GoodType, GoodAmount> { [GoodType.Food] = Build.Whole(1) })],
            Build.Catalog(Build.Info(BuildingType.FoodPlant,
                inputs: new() { [GoodType.Food] = Build.Whole(2) },
                outputs: new() { [GoodType.ConsumerGoods] = Build.Whole(1) },
                sector: Sector.Civil)),
            Rate);

        new Simulation(world).Tick();

        var country = world.CountryById(1);

        // Завод и население заказали по 2, на складе 1: каждому по 0.5 еды.
        // Заводу на запуск нужно 2, значит он отработает четверть и выдаст 0.25.
        Assert.Equal(Build.Whole(1) / 4, country.Stock.Of(GoodType.ConsumerGoods));
        Assert.Equal(default, country.Stock.Of(GoodType.Food));
    }

    /// <summary>Нулевой вес населения отдаёт всё заводам.</summary>
    [Fact]
    public void ZeroWeightLeavesPeopleWithNothing()
    {
        var world = new GameWorld(
            [Build.Region(1, 1, new Dictionary<BuildingType, int> { [BuildingType.FoodPlant] = 1 },
                population: 1_000_000)],
            [Build.Country(1,
                new Dictionary<GoodType, GoodAmount> { [GoodType.Food] = Build.Whole(1) },
                new Dictionary<Sector, int> { [Sector.People] = 0 })],
            Build.Catalog(Build.Info(BuildingType.FoodPlant,
                inputs: new() { [GoodType.Food] = Build.Whole(2) },
                outputs: new() { [GoodType.ConsumerGoods] = Build.Whole(1) },
                sector: Sector.Civil)),
            Rate);

        new Simulation(world).Tick();

        Assert.Equal(Build.Whole(1) / 2, world.CountryById(1).Stock.Of(GoodType.ConsumerGoods));
    }

    /// <summary>Без ставок население не ест вовсе — так устроены тесты производства.</summary>
    [Fact]
    public void NoRatesMeansNoEating()
    {
        var world = WorldWith(millions: 3, foodInStock: 100, rate: []);

        new Simulation(world).Tick();

        Assert.Equal(Build.Whole(100), world.CountryById(1).Stock.Of(GoodType.Food));
    }
}
