using CapitaModern.Core.Economy;

namespace CapitaModern.Core.World;

/// <summary>
/// Государство: казна и общий склад. Территории у страны нет — какие ячейки ей
/// принадлежат, знает поле владения на карте, а не этот класс.
/// </summary>
public sealed class Country
{
    /// <summary>Тот же байт, что лежит в world.bin для каждой ячейки.</summary>
    public byte Id { get; }

    public string Name { get; }

    /// <summary>
    /// Казна в целых единицах. Дробей не бывает намеренно: плавающая точка копит
    /// ошибку за тысячи тиков и ломает совпадение сейва с оригиналом.
    /// </summary>
    public long Balance { get; private set; }

    /// <summary>Склад: накопленные за всю партию количества, отсюда long.</summary>
    private Dictionary<GoodType, long> Stock { get; }

    public Country(byte id, string name, long balance, IReadOnlyDictionary<GoodType, long> stock)
    {
        Id = id;
        Name = name;
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

    public long StockOf(GoodType goodType) => Stock.GetValueOrDefault(goodType);

    public void Store(GoodType good, long amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        Stock[good] = StockOf(good) + amount;
    }

    /// <summary>
    /// Списывает весь рецепт целиком или не списывает ничего.
    /// </summary>
    /// <remarks>
    /// Проверка идёт до списания намеренно: если у завода есть руда, но нет угля,
    /// руда должна остаться на складе. Поэтому рецепт передаётся целиком, а не
    /// списывается по одному товару.
    /// </remarks>
    /// <param name="times">Сколько раз применить рецепт — обычно число работающих предприятий.</param>
    public bool TryConsume(IReadOnlyDictionary<GoodType, int> recipe, int times = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(times);

        foreach (var good in recipe.Keys)
        {
            if (StockOf(good) - recipe[good] * times < 0) return false;
        }

        foreach (var good in recipe.Keys)
        {
            Stock[good] = StockOf(good) - recipe[good] * times;
        }

        return true;
    }
}
