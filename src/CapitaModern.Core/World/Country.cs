using CapitaModern.Core.Economy;

namespace CapitaModern.Core.World;

public sealed class Country
{
    public byte Id { get; init; }
    public string Name { get; init; } = "";
    public long Balance { get; private set; }
    private Dictionary<GoodType, long> Stock { get; init; } = new();

    public void Receive(long amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        Balance += amount;
    }
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
