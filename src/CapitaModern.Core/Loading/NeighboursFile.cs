namespace CapitaModern.Core.Loading;

/// <summary>Соседство из data/map/neighbours.json. Числа при соседях — сколько ячеек
/// общей границы; маршрутам они пока не нужны, а фронту понадобятся.</summary>
public record NeighboursFile
(
    Dictionary<string, NeighbourDto> ByIso
);

public record NeighbourDto(bool Coastal, Dictionary<string, int> Neighbours);
