using System.Text.Json.Serialization;
using CapitaModern.Core.Buildings;

namespace CapitaModern.Core.Loading;

/// <summary>Стартовые предприятия: номер области строкой, дальше счётчик по типам.
/// Заполнена 831 область из 2606, у остальных ключа нет.</summary>
public record StartBuildingsFile(
    [property: JsonPropertyName("regions")] Dictionary<string, Dictionary<BuildingType, int>> StartBuildings
);
