using CapitaModern.Core.Economy;
using CapitaModern.Core.Loading;

namespace CapitaModern.Core.Buildings;

public sealed class BuildingCatalog
{
    // Массив, а не словарь: в тике сюда обращаются на каждую постройку.
    private readonly BuildingInfo[] _byType;

    public BuildingCatalog(IEnumerable<BuildingInfo> infos)
    {
        _byType = new BuildingInfo[Enum.GetValues<BuildingType>().Length];

        // Дубль и пропуск ловим при загрузке, а не в тике.
        foreach (var info in infos)
        {
            int index = (int)info.Type;

            if (_byType[index] is not null)
                throw new InvalidDataException($"{info.Type} был указан дважды");

            _byType[index] = info;
        }

        foreach (var type in Enum.GetValues<BuildingType>())
        {
            if (_byType[(int)type] is null)
                throw new InvalidDataException($"{type} не был указан");
        }
    }

    public static BuildingCatalog FromJson(string json) => new(JsonReader.Read<BuildingInfo[]>(json));

    public BuildingInfo this[BuildingType type] => _byType[(int)type];
}
