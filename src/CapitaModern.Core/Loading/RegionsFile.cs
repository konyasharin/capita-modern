using System.Text.Json.Serialization;
using CapitaModern.Core.Economy;

namespace CapitaModern.Core.Loading;

/// <summary>
/// Регион как он лежит в data/map/regions.json. Геометрии здесь нет: какие ячейки
/// в регион входят, знает только карта, ядру достаточно их количества.
/// </summary>
/// <param name="Id">Номер региона, до 2985 — в байт не влезает, в отличие от id страны.</param>
/// <param name="Country">Владелец на старте: регион по построению целиком внутри одной страны.</param>
/// <param name="Cells">Сколько ячеек карты в регионе — знаменатель для доли захвата.</param>
/// <param name="Deposits">Потенциал добычи по товарам; чего нет в словаре, того не добыть.</param>
public record RegionDto(
    int Id,
    byte Country,
    int Cells,
    int Population,
    Dictionary<GoodType, int> Deposits
);

public record RegionsFile(
    int Width,
    int Height,
    [property: JsonPropertyName("regions")] RegionDto[] Regions
);
