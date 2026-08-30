using System.Collections;

namespace CapitaModern.Core.Economy;

/// <summary>
/// Счётчик «страна → ключ → сколько» на один тик: спрос, выпуск, остатки складов и
/// число работающих предприятий копятся в нём, а не в самих странах.
/// </summary>
/// <remarks>
/// Отдельная таблица нужна, чтобы тик не зависел от порядка обхода: все считают от
/// склада на начало тика, а результат вливается в склады в конце. Иначе шахта,
/// обработанная раньше завода, успела бы отдать ему руду в том же тике, и итог
/// зависел бы от номеров областей.
/// </remarks>
/// <typeparam name="TKey">Что считаем: товары или типы построек.</typeparam>
public sealed class Tally<TKey> : IEnumerable<(byte Country, TKey Key, long Amount)>
    where TKey : struct, Enum
{
    private readonly Dictionary<byte, Dictionary<TKey, long>> _amounts = new();

    public IEnumerator<(byte Country, TKey Key, long Amount)> GetEnumerator()
    {
        foreach (var (country, amounts) in _amounts)
        {
            foreach (var (key, amount) in amounts)
            {
                yield return (country, key, amount);
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Прибавляет к уже накопленному. Ноль допустим: предприятие, которому
    /// не хватило сырья, отработает ноль раз, и это обычный исход, а не ошибка.</summary>
    public void Add(byte country, TKey key, long amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        var amounts = AmountsOf(country);
        amounts[key] = amounts.GetValueOrDefault(key) + amount;
    }

    /// <summary>Ноль для всего, чего не спрашивали: пустая ячейка и ноль здесь
    /// неразличимы по смыслу, а исключение ломало бы обход по всем ключам.</summary>
    public long Get(byte country, TKey key)
    {
        if (
            !_amounts.TryGetValue(country, out var countryAmounts) ||
            !countryAmounts.TryGetValue(key, out var result)
        )
            return 0;

        return result;
    }

    public void Set(byte country, TKey key, long amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        AmountsOf(country)[key] = amount;
    }

    public void Clear()
    {
        _amounts.Clear();
    }

    private Dictionary<TKey, long> AmountsOf(byte country)
    {
        if (!_amounts.TryGetValue(country, out var amounts))
        {
            _amounts[country] = amounts = new();
        }

        return amounts;
    }
}
