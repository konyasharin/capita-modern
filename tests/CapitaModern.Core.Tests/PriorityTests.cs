using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;
using CapitaModern.Core.Politics;
using CapitaModern.Core.World;
using Xunit;

namespace CapitaModern.Core.Tests;

/// <summary>Дефицит делится по весу отрасли. Вес — то, что потом будут менять законы.</summary>
public class PriorityTests
{
    private const BuildingType Civil = BuildingType.ConsumerGoodsPlant;
    private const BuildingType Army = BuildingType.ArmourPlant;

    /// <summary>По заводу на отрасль, каждому нужно 10 угля, а на складе столько-то.</summary>
    private static GameWorld WorldWith(long coal, Dictionary<Sector, int>? weights = null) =>
        Build.World(
            [Build.Region(1, 1, new Dictionary<BuildingType, int> { [Civil] = 1, [Army] = 1 })],
            [Build.Country(1, new Dictionary<GoodType, GoodAmount> { [GoodType.Coal] = Build.Whole(coal) }, weights)],
            Build.Catalog(
                Build.Info(Civil, inputs: new() { [GoodType.Coal] = Build.Whole(10) },
                                  outputs: new() { [GoodType.ConsumerGoods] = Build.Whole(1) },
                                  sector: Sector.Civil),
                Build.Info(Army, inputs: new() { [GoodType.Coal] = Build.Whole(10) },
                                 outputs: new() { [GoodType.Armour] = Build.Whole(1) },
                                 sector: Sector.Military)));

    private static GoodAmount Part(int numerator, int denominator) =>
        new(GoodAmount.Scale * numerator / denominator);

    [Fact]
    public void WeightsStartNormal()
    {
        var priorities = new Priorities();

        foreach (var sector in Enum.GetValues<Sector>())
        {
            Assert.Equal(Priorities.NormalWeight, priorities.WeightOf(sector));
        }
    }

    [Fact]
    public void EqualWeightsSplitShortageEvenly()
    {
        var world = WorldWith(coal: 10);

        new Simulation(world).Tick();

        var country = world.CountryById(1);

        Assert.Equal(Part(1, 2), country.Stock.Of(GoodType.ConsumerGoods));
        Assert.Equal(Part(1, 2), country.Stock.Of(GoodType.Armour));
        Assert.Equal(default, country.Stock.Of(GoodType.Coal));
    }

    /// <summary>Втрое больший вес даёт втрое большую долю: 75 против 25 процентов.</summary>
    [Fact]
    public void HeavierSectorTakesMoreOfTheShortage()
    {
        var world = WorldWith(coal: 10, weights: new Dictionary<Sector, int>
        {
            [Sector.Military] = Priorities.NormalWeight * 3,
        });

        new Simulation(world).Tick();

        var country = world.CountryById(1);

        Assert.Equal(Part(3, 4), country.Stock.Of(GoodType.Armour));
        Assert.Equal(Part(1, 4), country.Stock.Of(GoodType.ConsumerGoods));
        Assert.Equal(default, country.Stock.Of(GoodType.Coal));
    }

    /// <summary>Приоритет включается только в нехватке: когда сырья хватает, он не значит ничего.</summary>
    [Fact]
    public void WeightsDoNothingWhenThereIsEnough()
    {
        var world = WorldWith(coal: 100, weights: new Dictionary<Sector, int>
        {
            [Sector.Military] = Priorities.NormalWeight * 3,
        });

        new Simulation(world).Tick();

        var country = world.CountryById(1);

        Assert.Equal(Build.Whole(1), country.Stock.Of(GoodType.Armour));
        Assert.Equal(Build.Whole(1), country.Stock.Of(GoodType.ConsumerGoods));
        Assert.Equal(Build.Whole(80), country.Stock.Of(GoodType.Coal));
    }

    /// <summary>Нулевой вес отключает отрасль от снабжения полностью.</summary>
    [Fact]
    public void ZeroWeightGetsNothing()
    {
        var world = WorldWith(coal: 10, weights: new Dictionary<Sector, int> { [Sector.Civil] = 0 });

        new Simulation(world).Tick();

        var country = world.CountryById(1);

        Assert.Equal(default, country.Stock.Of(GoodType.ConsumerGoods));
        Assert.Equal(Build.Whole(1), country.Stock.Of(GoodType.Armour));
    }

    /// <summary>Доля не может превысить собственный заказ: лишнее остаётся на складе.</summary>
    [Fact]
    public void ShareNeverExceedsOwnDemand()
    {
        var world = WorldWith(coal: 18, weights: new Dictionary<Sector, int>
        {
            [Sector.Military] = Priorities.NormalWeight * 10,
        });

        new Simulation(world).Tick();

        var country = world.CountryById(1);

        Assert.Equal(Build.Whole(1), country.Stock.Of(GoodType.Armour));
        Assert.True(country.Stock.Of(GoodType.Coal) > default(GoodAmount), "остаток должен остаться на складе");
    }

    [Fact]
    public void NegativeWeightIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Priorities().SetWeight(Sector.Civil, -1));
    }
}
