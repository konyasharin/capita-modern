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

    /// <summary>Мир без потребления населением: тесты производства о нём не знают.</summary>
    public static GameWorld World(Region[] regions, Country[] countries, BuildingCatalog catalog) =>
        new(regions, countries, catalog, new Dictionary<GoodType, GoodAmount>());

    public static Country Country(
        byte id,
        Dictionary<GoodType, GoodAmount>? stock = null,
        Dictionary<Sector, int>? weights = null) =>
        new(id, $"country {id}", $"C{id:00}", new Treasury(0), new Stock(stock ?? []), new Priorities(weights));
}
