using CapitaModern.Core.Buildings;

namespace CapitaModern.Core.World;

/// <summary>Состояние партии. Владеет областями и странами, остальные берут их ссылкой.</summary>
public sealed class GameWorld
{
    public BuildingCatalog Buildings { get; }

    private readonly Region[] _regions;
    private readonly Country[] _countries;
    private readonly Region[] _regionsById;
    private readonly Country[] _countriesById;

    public GameWorld(Region[] regions, Country[] countries, BuildingCatalog buildings)
    {
        Buildings = buildings;
        _regions = regions;
        _countries = countries;

        _regionsById = new Region[regions.Max(region => region.Id) + 1];
        foreach (var region in regions) _regionsById[region.Id] = region;

        _countriesById = new Country[countries.Max(country => country.Id) + 1];
        foreach (var country in countries) _countriesById[country.Id] = country;
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

}
