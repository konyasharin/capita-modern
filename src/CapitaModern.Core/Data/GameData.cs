using System.Text.Json;
using System.Text.Json.Serialization;
using CapitaModern.Core.Map;

namespace CapitaModern.Core.Data;

public sealed record GoodDto(string Id, string Name, string Category, string Unit);

public sealed record BuildingDto(
    string Type,
    string Name,
    string Sector,
    string? RequiresDeposit,
    Dictionary<string, double> Inputs,
    Dictionary<string, double> Outputs,
    double OptimalWorkers,
    Dictionary<string, double> BuildCost,
    double LifeYears);

public sealed record RegionDto(
    int Id,
    int Country,
    string Name,
    int Cells,
    double Lon,
    double Lat,
    long Population,
    Dictionary<string, double> Deposits);

public sealed record CompanyDto(string Name, string Sector);

public sealed record DivisionDto(
    string Type,
    string Name,
    string Role,
    int Manpower,
    Dictionary<string, double> Equipment,
    Dictionary<string, double> Upkeep);

/// <summary>Всё, из чего собирается мир на старте партии. Ничего не считает, только читает.</summary>
public sealed class GameData
{
    public required WorldMap Map { get; init; }
    public required RegionGrid Grid { get; init; }
    public required CountryTable Countries { get; init; }
    public required IReadOnlyList<RegionDto> Regions { get; init; }
    public required IReadOnlyList<GoodDto> Goods { get; init; }
    public required IReadOnlyList<BuildingDto> Buildings { get; init; }

    /// <summary>Базовая цена за единицу товара, тысячи долларов.</summary>
    public required IReadOnlyDictionary<string, double> Prices { get; init; }

    /// <summary>Сколько товара съедает за сутки миллион человек.</summary>
    public required IReadOnlyDictionary<string, double> Consumption { get; init; }

    /// <summary>Стартовые заводы: id региона → тип → число.</summary>
    public required IReadOnlyDictionary<int, Dictionary<string, int>> StartIndustry { get; init; }

    /// <summary>Именные компании: ISO → список.</summary>
    public required IReadOnlyDictionary<string, List<CompanyDto>> Companies { get; init; }

    /// <summary>Военные расходы к ВВП, в сотых процента: ISO → доля.</summary>
    public required IReadOnlyDictionary<string, int> Defence { get; init; }

    public required int DefaultDefence { get; init; }

    public required IReadOnlyList<DivisionDto> Divisions { get; init; }

    /// <summary>Насколько потребление товара идёт за доходом на душу, в сотых.</summary>
    public required IReadOnlyDictionary<string, double> IncomeElasticity { get; init; }

    /// <summary>Собирает данные; пути относительно корня репо, вида "data/map/world.bin".</summary>
    public static GameData Load(Func<string, string> readText, Func<string, byte[]> readBytes)
    {
        var map = WorldMap.FromBytes(readBytes("data/map/world.bin"));
        var countries = CountryTable.FromJson(readText("data/map/countries.json"));

        var regions = Read<RegionsFile>(readText("data/map/regions.json")).Regions;
        var grid = RegionGrid.FromBytes(readBytes("data/map/regions.bin"), regions.Max(r => r.Id));

        var prices = Read<PricesFile>(readText("data/economy/prices.json")).Prices;
        var consumptionFile = Read<ConsumptionFile>(readText("data/economy/consumption.json"));
        var consumption = consumptionFile.UnitPerMillionPeople;
        var start = Read<StartFile>(readText("data/economy/start-industry.json")).Regions
            .ToDictionary(pair => int.Parse(pair.Key), pair => pair.Value);
        var defence = Read<DefenceFile>(readText("data/politics/defence.json"));

        return new GameData
        {
            Map = map,
            Grid = grid,
            Countries = countries,
            Regions = regions,
            Goods = Read<List<GoodDto>>(readText("data/economy/goods.json")),
            Buildings = Read<List<BuildingDto>>(readText("data/economy/buildings.json")),
            Prices = prices,
            Consumption = consumption,
            StartIndustry = start,
            Companies = Read<CompaniesFile>(readText("data/economy/companies.json")).Companies,
            Defence = defence.Share,
            DefaultDefence = defence.DefaultShare,
            Divisions = Read<DivisionsFile>(readText("data/military/divisions.json")).Divisions,
            IncomeElasticity = consumptionFile.IncomeElasticity,
        };
    }

    private static T Read<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new InvalidDataException($"пустой файл для {typeof(T).Name}");

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private sealed record RegionsFile([property: JsonPropertyName("regions")] List<RegionDto> Regions);
    private sealed record PricesFile(Dictionary<string, double> Prices);
    private sealed record ConsumptionFile(Dictionary<string, double> UnitPerMillionPeople, Dictionary<string, double> IncomeElasticity);
    private sealed record StartFile(Dictionary<string, Dictionary<string, int>> Regions);
    private sealed record CompaniesFile(Dictionary<string, List<CompanyDto>> Companies);
    private sealed record DefenceFile(int DefaultShare, Dictionary<string, int> Share);
    private sealed record DivisionsFile(List<DivisionDto> Divisions);
}
