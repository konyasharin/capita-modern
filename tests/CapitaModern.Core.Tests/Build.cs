using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;
using CapitaModern.Core.Politics;
using CapitaModern.Core.World;

namespace CapitaModern.Core.Tests;

/// <summary>Сборка маленьких миров для тестов.</summary>
internal static class Build
{
    public static GoodAmount Whole(long n) => GoodAmount.FromWhole(n);

    public static BuildingInfo Info(
        BuildingType type,
        Dictionary<GoodType, GoodAmount>? inputs = null,
        Dictionary<GoodType, GoodAmount>? outputs = null,
        GoodType? deposit = null,
        Sector sector = Sector.Heavy) => new()
    {
        Type = type,
        Sector = sector,
        Inputs = inputs ?? [],
        Outputs = outputs ?? [],
        RequiresDeposit = deposit,
    };

    /// <summary>Каталог требует все типы построек, поэтому недостающие добираются пустыми.</summary>
    public static BuildingCatalog Catalog(params BuildingInfo[] custom)
    {
        var byType = Enum.GetValues<BuildingType>().ToDictionary(type => type, type => Info(type));

        foreach (var info in custom)
        {
            byType[info.Type] = info;
        }

        return new BuildingCatalog(byType.Values);
    }

    public static Region Region(
        int id,
        byte owner,
        Dictionary<BuildingType, int>? buildings = null,
        Dictionary<GoodType, int>? deposits = null,
        int cells = 10,
        int population = 1000) =>
        new(id, Population.FromWhole(population), new Dictionary<byte, int> { [owner] = cells }, buildings ?? [], deposits ?? []);

    /// <summary>Резервы одной кучей у ничейного эмитента: тестам эмитент не важен.</summary>
    public static Reserve[] Cash(long money) =>
        [new Reserve(ReserveKind.ForeignCurrency, WorldMarket.WorldIssuer, WorldMarket.WorldIssuer,
            Money.FromWhole(money))];

    /// <summary>Рынок со стартовыми ценами. Одна страна на нём торговать не с кем.</summary>
    public static WorldMarket Market(Dictionary<GoodType, Money>? prices = null) => new(new Prices(prices));

    /// <summary>Мир без потребления населением: тесты производства о нём не знают.</summary>
    public static GameWorld World(
        Region[] regions,
        Country[] countries,
        BuildingCatalog catalog,
        Dictionary<GoodType, Money>? marketPrices = null,
        Dictionary<GoodType, int>? demand = null,
        Dictionary<GoodType, int>? supply = null) =>
        new(regions, countries, catalog, new Dictionary<GoodType, GoodAmount>(),
            Market(marketPrices), new Elasticity(demand, supply));

    public static Country Country(
        byte id,
        Dictionary<GoodType, GoodAmount>? stock = null,
        Dictionary<Sector, int>? weights = null,
        Dictionary<GoodType, Money>? prices = null,
        long money = 0) =>
        new(id, $"country {id}", $"C{id:00}",
            new Producer(id, new Stock(stock ?? []), new Treasury(Cash(money)), new Prices(prices)),
            new Priorities(weights));
}
