using System.Data;
using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;
using CapitaModern.Core.World;

namespace CapitaModern.Core.Loading;

/// <summary>
/// Разбор файлов мира. Принимает содержимое, а не пути: в игре файлы достаёт Godot
/// из <c>res://</c>, в консоли — обычный File, и ядро не должно знать разницы.
/// </summary>
public static class WorldDataLoader
{
    public static GameWorld LoadWorld(string countriesJson, string regionsJson, string buildingsJson, string startBuildingsJson)
    {
        var startBuildings = LoadStartBuildingsFile(startBuildingsJson).StartBuildings;
        var countriesFile = LoadCountriesFile(countriesJson);
        var regionsFile = LoadRegionsFile(regionsJson);
        if (countriesFile.Height != regionsFile.Height || countriesFile.Width != regionsFile.Width)
            throw new InvalidDataException("Не совпадают размеры карты в countries.json и regions.json");

        Region[] regions = regionsFile.Regions.Select(dto =>
            ToRegion(dto, startBuildings.GetValueOrDefault(dto.Id.ToString(), new()))
        ).ToArray();
        Country[] countries = countriesFile.Countries.Select(ToCountry).ToArray();
        BuildingCatalog buildingCatalog = BuildingCatalog.FromJson(buildingsJson);

        return new GameWorld(regions, countries, buildingCatalog);
    }

    private static CountriesFile LoadCountriesFile(string json) => JsonReader.Read<CountriesFile>(json);
    private static RegionsFile LoadRegionsFile(string json) => JsonReader.Read<RegionsFile>(json);
    private static StartBuildingsFile LoadStartBuildingsFile(string json) => JsonReader.Read<StartBuildingsFile>(json);
    private static Region ToRegion(RegionDto dto, Dictionary<BuildingType, int> buildings) => new Region(
        dto.Id,
        dto.Population,
        new Dictionary<byte, int>{ [dto.Country] = dto.Cells },
        buildings,
        dto.Deposits
    );
    private static Country ToCountry(CountryDto dto) => new Country(
        dto.Id,
        dto.Name,
        dto.Iso,
        0,
        new Dictionary<GoodType, long>()
    ); // баланс и склад - заглушки
}
