using CapitaModern.Core.Buildings;

namespace CapitaModern.Core.World;

public sealed class Country
{
    public string Name { get; init; } = "";
    public Dictionary<BuildingType, int> Buildings { get; init; } = new();
}
