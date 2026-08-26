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
    private readonly Dictionary<byte, Dictionary<GoodType, int>> _amounts = new();

    /// <summary>Прибавляет к уже накопленному. Ноль допустим: предприятие, которому
    /// не хватило сырья, отработает ноль раз, и это обычный исход, а не ошибка.</summary>
    public void Add(byte country, GoodType good, int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        if (!_amounts.TryGetValue(country, out var goods))
        {
            _amounts[country] = new Dictionary<GoodType, int>{{ good, amount }};
        }
        else
        {
            goods[good] = goods.GetValueOrDefault(good) + amount;
        }
    }

    /// <summary>Ноль для всего, чего не спрашивали: пустая ячейка и ноль здесь
    /// неразличимы по смыслу, а исключение ломало бы обход по всем товарам.</summary>
    public int Get(byte country, GoodType good)
    {
        if (
            !_amounts.TryGetValue(country, out var countryAmounts) ||
            !countryAmounts.TryGetValue(good, out var result)
        )
            return 0;

        return result;
    }

    public void Clear()
    {
        _amounts.Clear();
    }
}
