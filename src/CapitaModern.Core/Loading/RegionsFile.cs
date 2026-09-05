using System.Text.Json.Serialization;
using CapitaModern.Core.Economy;

namespace CapitaModern.Core.Loading;

/// <summary>Область как она лежит в regions.json. Геометрии нет — ядру хватает
/// количества ячеек.</summary>
/// <param name="Id">Номер области, до 2985 — в байт не влезает.</param>
/// <param name="Country">Владелец на старте. Область всегда внутри одной страны.</param>
/// <param name="Cells">Ячеек карты в области — знаменатель доли захвата.</param>
/// <param name="Deposits">Что можно добывать. Чего нет в словаре — того не добыть.</param>
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
