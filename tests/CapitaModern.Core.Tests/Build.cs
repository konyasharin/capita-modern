using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;
using CapitaModern.Core.World;

namespace CapitaModern.Core.Tests;

/// <summary>Сборка маленьких миров для тестов.</summary>
internal static class Build
{
    public static GoodAmount Units(long n) => GoodAmount.FromUnits(n);

    public static BuildingInfo Info(
        BuildingType type,
        Dictionary<GoodType, GoodAmount>? inputs = null,
        Dictionary<GoodType, GoodAmount>? outputs = null,
        GoodType? deposit = null) => new()
    {
        Type = type,
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
        new(id, population, new Dictionary<byte, int> { [owner] = cells }, buildings ?? [], deposits ?? []);

    public static Country Country(byte id, Dictionary<GoodType, GoodAmount>? stock = null) =>
        new(id, $"country {id}", $"C{id:00}", 0, stock ?? []);
}
