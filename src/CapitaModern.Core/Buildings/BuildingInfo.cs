using CapitaModern.Core.Economy;

namespace CapitaModern.Core.Buildings;

public sealed class BuildingInfo
{
    public BuildingType Type { get; init; }
    public Dictionary<GoodType, int> Inputs { get; init; } = new();
    public Dictionary<GoodType, int> Outputs { get; init; } = new();
    public int OptimalWorkers { get; init; }
}
