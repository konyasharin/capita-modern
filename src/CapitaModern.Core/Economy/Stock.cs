namespace CapitaModern.Core.Economy;

/// <summary>Склад страны: что накоплено и как это тратится.</summary>
public sealed class Stock
{
    private readonly Dictionary<GoodType, GoodAmount> _amounts;

    public Stock(IReadOnlyDictionary<GoodType, GoodAmount> amounts)
    {
        _amounts = new(amounts);
    }

    /// <summary>Чего нет на складе, того ноль.</summary>
    public GoodAmount Of(GoodType good) => _amounts.GetValueOrDefault(good);

    public void Store(GoodType good, GoodAmount amount)
    {
        if (amount < default(GoodAmount)) throw new ArgumentOutOfRangeException(nameof(amount));

        _amounts[good] = Of(good) + amount;
    }

    /// <summary>Списывает рецепт целиком или ничего: если руда есть, а угля нет, руда
    /// должна остаться.</summary>
    /// <param name="load">Загрузка: <see cref="Load.Full"/> — один завод на полную.</param>
    public bool TryConsume(IReadOnlyDictionary<GoodType, GoodAmount> recipe, long load)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(load);

        foreach (var (good, amount) in recipe)
        {
            if (Of(good) < amount * load / Load.Full) return false;
        }

        foreach (var (good, amount) in recipe)
        {
            _amounts[good] = Of(good) - amount * load / Load.Full;
        }

        return true;
    }

    /// <summary>Забирает сколько получится и говорит, сколько удалось. Так потребляет
    /// население: недостача — это не ошибка, а повод показать её игроку.</summary>
    public GoodAmount TakeUpTo(GoodType good, GoodAmount wanted)
    {
        if (wanted < default(GoodAmount)) throw new ArgumentOutOfRangeException(nameof(wanted));

        var taken = wanted < Of(good) ? wanted : Of(good);
        _amounts[good] = Of(good) - taken;

        return taken;
    }
}
