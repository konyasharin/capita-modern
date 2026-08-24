namespace CapitaModern.Core.Buildings;

/// <summary>
/// Пока не используется: постройки живут счётчиком по типам в <c>Region</c>, а не
/// объектами. Понадобится, когда появятся единичные объекты со своим состоянием —
/// АЭС, порт, — которые нельзя свести к числу в счётчике.
/// </summary>
public sealed class Building
{
    public BuildingType Type { get; init; }

    public Building(BuildingType type)
    {
        Type = type;
    }
}
