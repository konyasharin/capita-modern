using CapitaModern.Core.Economy;

namespace CapitaModern.Core.World;

/// <summary>Государство: казна и общий склад. Территории здесь нет — её знает карта.</summary>
public sealed class Country
{
    /// <summary>Тот же байт, что лежит в world.bin для каждой ячейки.</summary>
    public byte Id { get; }
    public string Name { get; }
    public string Iso { get; }

    /// <summary>Казна в целых. Дробных денег нет намеренно: плавающая точка за тысячи
    /// тиков копит ошибку.</summary>
    public long Balance { get; private set; }

    /// <summary>Склад страны.</summary>
    private Dictionary<GoodType, GoodAmount> Stock { get; }

    public Country(byte id, string name, string iso, long balance, IReadOnlyDictionary<GoodType, GoodAmount> stock)
    {
        Id = id;
        Name = name;
        Iso = iso;
        Balance = balance;
        Stock = new(stock);
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

    public GoodAmount StockOf(GoodType goodType) => Stock.GetValueOrDefault(goodType);

    public void Store(GoodType good, GoodAmount amount)
    {
        if (amount < default(GoodAmount)) throw new ArgumentOutOfRangeException(nameof(amount));
        Stock[good] = StockOf(good) + amount;
    }

    /// <summary>Списывает рецепт целиком или ничего: если руда есть, а угля нет, руда
    /// должна остаться.</summary>
    /// <param name="load">Загрузка в сотых долях: 100 — один завод на полную.</param>
    public bool TryConsume(IReadOnlyDictionary<GoodType, GoodAmount> recipe, long load)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(load);

        foreach (var good in recipe.Keys)
        {
            if (StockOf(good) - recipe[good] * load / Load.Full < default(GoodAmount)) return false;
        }

        foreach (var good in recipe.Keys)
        {
            Stock[good] = StockOf(good) - recipe[good] * load / Load.Full;
        }

        return true;
    }
}
