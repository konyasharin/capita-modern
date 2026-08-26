using System.Text.Json.Serialization;
using CapitaModern.Core.Buildings;

namespace CapitaModern.Core.Loading;

/// <summary>
/// Стартовые предприятия из data/economy/start-industry.json: номер области строкой,
/// затем счётчик по типам. Промышленность есть не везде — из 2606 областей заполнена
/// 831, у остальных ключа в файле просто нет.
/// </summary>
public record StartBuildingsFile(
    [property: JsonPropertyName("regions")] Dictionary<string, Dictionary<BuildingType, int>> StartBuildings
);
