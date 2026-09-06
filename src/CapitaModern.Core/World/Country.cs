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

    /// <summary>Казна в целых. Дробных денег нет намеренно: плавающая точка за тысячи
    /// тиков копит ошибку.</summary>
    public long Balance { get; private set; }

    public Stock Stock { get; }
    public Priorities Priorities { get; }

    public Country(byte id, string name, string iso, long balance, Stock stock, Priorities priorities)
    {
        Id = id;
        Name = name;
        Iso = iso;
        Balance = balance;
        Stock = stock;
        Priorities = priorities;
    }

    public void Receive(long amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        Balance += amount;
    }

    /// <summary>Списывает, если хватает. Не хватило — возвращает false, казна не тронута.</summary>
    public bool TrySpend(long amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        if (Balance - amount < 0) return false;

        Balance -= amount;
        return true;
    }
}
