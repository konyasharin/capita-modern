using CapitaModern.Core.Loading;

namespace CapitaModern.Core.Buildings;

public sealed class BuildingCatalog(IEnumerable<BuildingInfo> infos)
    : Catalog<BuildingInfo>(infos, info => (int)info.Type, Enum.GetValues<BuildingType>().Length)
{
    // Статические методы не наследуются, поэтому FromJson пишется в каждом каталоге.
    public static BuildingCatalog FromJson(string json) => new(JsonReader.Read<BuildingInfo>(json));

    public BuildingInfo this[BuildingType type] => Get((int)type);
}
