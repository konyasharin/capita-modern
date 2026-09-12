namespace CapitaModern.Core.Economy;

/// <summary>Склад страны: что накоплено и как это тратится.</summary>
/// <remarks>Внутри плоский массив, а не словарь: к складу обращаются отовсюду — торговля,
/// выпуск, покупки, износ, — и за тик это выходит в миллионы раз. На хешировании товара
/// уходило больше времени, чем на самой работе со складом.</remarks>
public sealed class Stock
{
    private static readonly int Kinds = Enum.GetValues<GoodType>().Length;

    private readonly GoodAmount[] _amounts = new GoodAmount[Kinds];

    public Stock(IReadOnlyDictionary<GoodType, GoodAmount> amounts)
    {
        foreach (var (good, amount) in amounts) _amounts[(int)good] = amount;
    }

    /// <summary>Чего нет на складе, того ноль.</summary>
    public GoodAmount Of(GoodType good) => _amounts[(int)good];

    public void Store(GoodType good, GoodAmount amount)
    {
        if (amount < default(GoodAmount)) throw new ArgumentOutOfRangeException(nameof(amount));

        _amounts[(int)good] += amount;
    }

    /// <summary>Списывает рецепт целиком или ничего: если руда есть, а угля нет, руда
    /// должна остаться.</summary>
    /// <param name="load">Загрузка: <see cref="Load.Full"/> — один завод на полную.</param>
    public bool TryConsume(IReadOnlyDictionary<GoodType, GoodAmount> recipe, long load)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(load);

        foreach (var (good, amount) in recipe)
        {
            if (_amounts[(int)good] < amount * load / Load.Full) return false;
        }

        foreach (var (good, amount) in recipe)
        {
            _amounts[(int)good] -= amount * load / Load.Full;
        }

        return true;
    }

    /// <summary>Забирает сколько получится и говорит, сколько удалось. Так потребляет
    /// население: недостача — это не ошибка, а повод показать её игроку.</summary>
    public GoodAmount TakeUpTo(GoodType good, GoodAmount wanted)
    {
        if (wanted < default(GoodAmount)) throw new ArgumentOutOfRangeException(nameof(wanted));

        var have = _amounts[(int)good];
        var taken = wanted < have ? wanted : have;
        _amounts[(int)good] = have - taken;

        return taken;
    }
}
