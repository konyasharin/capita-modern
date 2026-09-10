using System.Data;
using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;
using CapitaModern.Core.Politics;
using CapitaModern.Core.World;

namespace CapitaModern.Core.Loading;

/// <summary>Собирает мир из файлов. Принимает содержимое, а не пути: в игре файлы
/// достаёт Godot, в консоли — File.</summary>
public static class WorldDataLoader
{
    public static GameWorld LoadWorld(
        string countriesJson,
        string regionsJson,
        string buildingsJson,
        string startBuildingsJson,
        string consumptionJson,
        string pricesJson,
        string reservesJson)
    {
        var consumption = LoadConsumptionFile(consumptionJson).UnitPerMillionPeople;
        var startPrices = LoadPricesFile(pricesJson).Prices;
        var reserves = LoadReservesFile(reservesJson);
        var startBuildings = LoadStartBuildingsFile(startBuildingsJson).StartBuildings;
        var countriesFile = LoadCountriesFile(countriesJson);
        var regionsFile = LoadRegionsFile(regionsJson);
        if (countriesFile.Height != regionsFile.Height || countriesFile.Width != regionsFile.Width)
            throw new InvalidDataException("Не совпадают размеры карты в countries.json и regions.json");

        Region[] regions = regionsFile.Regions.Select(dto =>
            ToRegion(dto, startBuildings.GetValueOrDefault(dto.Id.ToString(), new()))
        ).ToArray();
        // Население нужно до сборки стран: тем, кого нет в reserves.json, деньги
        // считаются по нему.
        var populations = new Dictionary<byte, Population>();
        foreach (var region in regions)
        {
            populations[region.LargestOwner] =
                populations.GetValueOrDefault(region.LargestOwner) + region.Demographics.Population;
        }

        Country[] countries = countriesFile.Countries
            .Select(dto => ToCountry(dto, startPrices, reserves, populations.GetValueOrDefault(dto.Id)))
            .ToArray();
        BuildingCatalog buildingCatalog = BuildingCatalog.FromJson(buildingsJson);

        return new GameWorld(regions, countries, buildingCatalog, consumption,
            new WorldMarket(new Prices(startPrices)));
    }

    private static CountriesFile LoadCountriesFile(string json) => JsonReader.Read<CountriesFile>(json);
    private static RegionsFile LoadRegionsFile(string json) => JsonReader.Read<RegionsFile>(json);
    private static StartBuildingsFile LoadStartBuildingsFile(string json) => JsonReader.Read<StartBuildingsFile>(json);
    private static ConsumptionFile LoadConsumptionFile(string json) => JsonReader.Read<ConsumptionFile>(json);
    private static PricesFile LoadPricesFile(string json) => JsonReader.Read<PricesFile>(json);
    private static ReservesFile LoadReservesFile(string json) => JsonReader.Read<ReservesFile>(json);
    private static Region ToRegion(RegionDto dto, Dictionary<BuildingType, int> buildings) => new(
        dto.Id,
        Population.FromWhole(dto.Population),
        new Dictionary<byte, int>{ [dto.Country] = dto.Cells },
        buildings,
        dto.Deposits
    );
    /// <summary>Три месяца товарного импорта. Вывести это из состояния игры нельзя:
    /// страна без денег не купит сырьё, без сырья не произведёт и никогда не выберется.</summary>
    private static Money ReservesOf(CountryDto dto, ReservesFile reserves, Population population)
    {
        if (reserves.ByIso.TryGetValue(dto.Iso, out var millions)) return Money.FromWhole(millions * 1000);

        return Money.FromWhole(reserves.DefaultPerMillionPeople * population.Whole / 1_000_000 * 1000);
    }

    private static Country ToCountry(
        CountryDto dto,
        IReadOnlyDictionary<GoodType, Money> startPrices,
        ReservesFile reserves,
        Population population) => new(
        dto.Id,
        dto.Name,
        dto.Iso,
        // Номер продавца пока совпадает с номером страны: государство одно на страну.
        // Цены у каждого свои, поэтому копия, а не общий объект.
        new Producer(
            dto.Id,
            new Stock(new Dictionary<GoodType, GoodAmount>()),
            new Treasury(ReservesOf(dto, reserves, population)),
            new Prices(startPrices)),
        new Priorities()
    ); // баланс и склад - заглушки
}
