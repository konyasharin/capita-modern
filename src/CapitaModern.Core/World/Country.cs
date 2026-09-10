using CapitaModern.Core.Economy;
using CapitaModern.Core.Politics;

namespace CapitaModern.Core.World;

/// <summary>Государство. Территории здесь нет — её знает карта, склад и деньги лежат
/// у <see cref="State"/>.</summary>
public sealed class Country
{
    /// <summary>Тот же байт, что лежит в world.bin для каждой ячейки.</summary>
    public byte Id { get; }
    public string Name { get; }
    public string Iso { get; }

    /// <summary>Государство как хозяйствующий субъект — пока единственный в стране.
    /// Компании появятся такими же продавцами рядом с ним.</summary>
    public Producer State { get; }

    public Priorities Priorities { get; }

    /// <summary>Пускают ли на мировой рынок. По умолчанию везде да.</summary>
    public TradeAccess TradeAccess { get; } = new();

    public Country(byte id, string name, string iso, Producer state, Priorities priorities)
    {
        Id = id;
        Name = name;
        Iso = iso;
        State = state;
        Priorities = priorities;
    }
}
