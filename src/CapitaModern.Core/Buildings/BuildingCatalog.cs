using CapitaModern.Core.Loading;

namespace CapitaModern.Core.Buildings;

public sealed class BuildingCatalog
{
    // Массив по значению enum, а не словарь: индексатор дёргается в тике
    // на каждую постройку, обращение по индексу дешевле хеширования.
    private readonly BuildingInfo[] _byType;

    public BuildingCatalog(IEnumerable<BuildingInfo> infos)
    {
        _byType = new BuildingInfo[Enum.GetValues<BuildingType>().Length];

        // Дубль и пропуск должны падать при загрузке с внятным текстом,
        // а не всплывать NullReferenceException посреди тика.
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

    public static BuildingCatalog FromJson(string json) => new(JsonReader.Read<BuildingInfo>(json));

    public BuildingInfo this[BuildingType type] => _byType[(int)type];
}
