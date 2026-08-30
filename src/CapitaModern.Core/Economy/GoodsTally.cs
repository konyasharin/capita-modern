namespace CapitaModern.Core.Economy;

/// <summary>
/// Счётчик «страна → товар → сколько» на один тик: спрос предприятий и выпуск
/// накапливаются в нём, а не в складах стран.
/// </summary>
/// <remarks>
/// Отдельная таблица нужна, чтобы тик не зависел от порядка обхода: все считают от
/// склада на начало тика, а результат вливается в склады в конце. Иначе шахта,
/// обработанная раньше завода, успела бы отдать ему руду в том же тике, и итог
/// зависел бы от номеров областей.
/// </remarks>
public sealed class GoodsTally
{
    private readonly Dictionary<byte, Dictionary<GoodType, long>> _amounts = new();

    public IEnumerable<(byte, GoodType, long)> Entries()
    {
        foreach (var (country, goods) in _amounts)
        {
            foreach (var (good, amount) in goods)
            {
                yield return (country, good, amount);
            }
        }
    }

    /// <summary>Прибавляет к уже накопленному. Ноль допустим: предприятие, которому
    /// не хватило сырья, отработает ноль раз, и это обычный исход, а не ошибка.</summary>
    public void Add(byte country, GoodType good, long amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        var goods = GoodsOf(country);
        goods[good] = goods.GetValueOrDefault(good) + amount;
    }

    public void Set(byte country, GoodType good, long amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        GoodsOf(country)[good] = amount;
    }

    public void Clear()
    {
        _amounts.Clear();
    }

    private Dictionary<GoodType, long> GoodsOf(byte country)
    {
        if (!_amounts.TryGetValue(country, out var goods))
        {
            _amounts[country] = goods = new();
        }

        return goods;
    }
}
