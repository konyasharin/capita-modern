using CapitaModern.Core.Economy;
using CapitaModern.Core.Politics;

namespace CapitaModern.Core.World;

/// <summary>Государство: казна и склад. Территории здесь нет — её знает карта.</summary>
public sealed class Country
{
    /// <summary>Тот же байт, что лежит в world.bin для каждой ячейки.</summary>
    public byte Id { get; }
    public string Name { get; }
    public string Iso { get; }
    public int Population { get; } = 0;

    public Treasury Treasury { get; }
    public Stock Stock { get; }
    public Priorities Priorities { get; }

    public Country(byte id, string name, string iso, Treasury treasury, Stock stock, Priorities priorities)
    {
        Id = id;
        Name = name;
        Iso = iso;
        Treasury = treasury;
        Stock = stock;
        Priorities = priorities;

    }
}
