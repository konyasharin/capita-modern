namespace CapitaModern.Core.Buildings;

public sealed class Building
{
    public BuildingType Type { get; init; }

    public Building(BuildingType type)
    {
        Type = type;
    }
}
