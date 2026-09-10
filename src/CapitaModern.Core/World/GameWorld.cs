using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;

namespace CapitaModern.Core.World;

/// <summary>Состояние партии. Владеет областями и странами, остальные берут их ссылкой.</summary>
public sealed class GameWorld
{
    public BuildingCatalog Buildings { get; }

    /// <summary>Мировой рынок: один на партию, цены на нём свои.</summary>
    public WorldMarket Market { get; }
    /// <summary>Сколько товара население съедает за сутки на миллион человек.</summary>
    public readonly IReadOnlyDictionary<GoodType, GoodAmount> Consumption;

    private readonly Region[] _regions;
    private readonly Country[] _countries;
    private readonly Region[] _regionsById;
    private readonly Country[] _countriesById;
    private readonly Population[] _populationsByCountry;

    public GameWorld(
        Region[] regions,
        Country[] countries,
        BuildingCatalog buildings,
        IReadOnlyDictionary<GoodType, GoodAmount> consumption,
        WorldMarket market
    ) {
        Buildings = buildings;
        Market = market;
        Consumption = consumption;
        _regions = regions;
        _countries = countries;

        _regionsById = new Region[regions.Max(region => region.Id) + 1];
        foreach (var region in regions) _regionsById[region.Id] = region;

        _countriesById = new Country[countries.Max(country => country.Id) + 1];
        foreach (var country in countries) _countriesById[country.Id] = country;

        _populationsByCountry = new Population[_countries.Max(country => country.Id) + 1];
        UpdatePopulations();
    }

    public IReadOnlyList<Region> Regions => _regions;
    public Region RegionById(int id) =>
        id > 0 && id < _regionsById.Length && _regionsById[id] is {} region ?
            region :
            throw new ArgumentOutOfRangeException(nameof(id), id, "Региона не найдено");

    public IReadOnlyList<Country> Countries => _countries;
    public Country CountryById(byte id) =>
        id < _countriesById.Length && _countriesById[id] is {} country ?
            country :
            throw new ArgumentOutOfRangeException(nameof(id), id, "Страны не найдено");

    public Population PopulationOf(byte country) =>
        country < _populationsByCountry.Length ?
            _populationsByCountry[country] :
            throw new ArgumentOutOfRangeException(nameof(country), country, "Страны не найдено");

    public void UpdatePopulations()
    {
        Array.Clear(_populationsByCountry);
        foreach (var region in _regions)
        {
            _populationsByCountry[region.LargestOwner] += region.Demographics.Population;
        }
    }

    public void TransferCells(int region, byte from, byte to, int count)
    {
        _regionsById[region].TransferCells(from, to, count);
        UpdatePopulations();
    }
}
