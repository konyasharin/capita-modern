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
        string reservesJson,
        string goodsJson,
        string keyRatesJson,
        string blocsJson)
    {
        var consumption = LoadConsumptionFile(consumptionJson).UnitPerMillionPeople;
        var startPrices = LoadPricesFile(pricesJson).Prices;
        var reserves = LoadReservesFile(reservesJson);
        var keyRates = JsonReader.Read<KeyRatesFile>(keyRatesJson);
        var blocs = JsonReader.Read<BlocsFile>(blocsJson);
        var goodDtos = JsonReader.Read<GoodDto[]>(goodsJson);
        var elasticity = new Elasticity(
            goodDtos.ToDictionary(dto => dto.Id, dto => dto.DemandElasticity),
            goodDtos.ToDictionary(dto => dto.Id, dto => dto.SupplyElasticity));
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

        var idByIso = countriesFile.Countries.ToDictionary(dto => dto.Iso, dto => dto.Id);
        Country[] countries = countriesFile.Countries
            .Select(dto => ToCountry(dto, startPrices, reserves, idByIso, populations.GetValueOrDefault(dto.Id)))
            .ToArray();

        // Приходящую валюту по умолчанию кладут туда же, где лежит основная часть
        // резервов. Увести её в другое место — решение игрока.
        var mainCustodian = reserves.Composition
            .Where(pair => idByIso.ContainsKey(pair.Key))
            .OrderByDescending(pair => pair.Value)
            .Select(pair => idByIso[pair.Key])
            .FirstOrDefault();

        foreach (var country in countries)
        {
            country.KeyRate = keyRates.ByIso.GetValueOrDefault(country.Iso, keyRates.DefaultRate);
            country.State.Custody = mainCustodian;
        }
        BuildingCatalog buildingCatalog = BuildingCatalog.FromJson(buildingsJson);

        var relations = new Relations(countries.ToDictionary(
            country => country.Id,
            country => blocs.ByIso.GetValueOrDefault(country.Iso, Bloc.NonAligned)));

        return new GameWorld(regions, countries, buildingCatalog, consumption,
            new WorldMarket(new Prices(startPrices)), elasticity, relations);
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

    /// <summary>Раскладывает сумму по валютам. Доли нормируются по своей сумме: часть
    /// мировых резервов лежит в валютах, которых у нас нет.</summary>
    /// <remarks>Остаток от деления уходит эмитенту с наибольшей долей, иначе на двухстах
    /// странах наберётся заметная недостача.</remarks>
    private static Reserve[] Split(
        CountryDto dto,
        Money total,
        ReservesFile reserves,
        IReadOnlyDictionary<string, byte> idByIso)
    {
        // Золото лежит в своих хранилищах: его и не отнять, ради того и держат.
        var gold = new Money(total.Raw * reserves.GoldShare / 100);
        total -= gold;

        var shares = reserves.Composition
            .Where(pair => idByIso.ContainsKey(pair.Key))
            .OrderByDescending(pair => pair.Value)
            .ToArray();
        var sum = shares.Sum(pair => pair.Value);
        if (sum <= 0) return [new Reserve(ReserveKind.Metal, dto.Id, dto.Id, gold + total)];

        var left = total;
        var held = new Reserve[shares.Length + 1];
        held[^1] = new Reserve(ReserveKind.Metal, dto.Id, dto.Id, gold);
        for (var i = 1; i < shares.Length; i++)
        {
            var part = new Money(total.Raw * shares[i].Value / sum);
            // Валюту держат в банках эмитента: он же её и заморозит, если дойдёт до дела.
            held[i] = new Reserve(
                ReserveKind.ForeignCurrency, idByIso[shares[i].Key], idByIso[shares[i].Key], part);
            left -= part;
        }

        held[0] = new Reserve(
            ReserveKind.ForeignCurrency, idByIso[shares[0].Key], idByIso[shares[0].Key], left);

        return held;
    }

    private static Country ToCountry(
        CountryDto dto,
        IReadOnlyDictionary<GoodType, Money> startPrices,
        ReservesFile reserves,
        IReadOnlyDictionary<string, byte> idByIso,
        Population population) => new(
        dto.Id,
        dto.Name,
        dto.Iso,
        // Номер продавца пока совпадает с номером страны: государство одно на страну.
        // Цены у каждого свои, поэтому копия, а не общий объект.
        new Producer(
            dto.Id,
            new Stock(new Dictionary<GoodType, GoodAmount>()),
            new Treasury(Split(dto, ReservesOf(dto, reserves, population), reserves, idByIso)),
            new Prices(startPrices)),
        new Priorities()
    ); // баланс и склад - заглушки
}
