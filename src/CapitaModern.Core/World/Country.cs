using CapitaModern.Core.Economy;

namespace CapitaModern.Core.World;

public sealed class Country
{
    public byte Id { get; init; }
    public string Name { get; init; } = "";
    public long Balance { get; set; }
    private Dictionary<GoodType, long> Stock { get; init; } = new();
}
