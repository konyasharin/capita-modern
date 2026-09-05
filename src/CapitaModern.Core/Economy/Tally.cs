using System.Collections;
using System.Numerics;

namespace CapitaModern.Core.Economy;

/// <summary>Счётчик «страна → ключ → сколько» на один тик. Нужен, чтобы итог не зависел
/// от порядка обхода: все считают от одних чисел, результат вливается в конце.</summary>
/// <typeparam name="TKey">Что считаем: товары или типы построек.</typeparam>
/// <typeparam name="TValue">Чем считаем: количеством товара или штуками.</typeparam>
public sealed class Tally<TKey, TValue> :
    IEnumerable<(byte Country, TKey Key, TValue Amount)>
    where TValue : struct, IAdditionOperators<TValue, TValue, TValue>, IComparisonOperators<TValue, TValue, bool>
    where TKey : struct, Enum
{
    private readonly Dictionary<byte, Dictionary<TKey, TValue>> _amounts = new();

    public IEnumerator<(byte Country, TKey Key, TValue Amount)> GetEnumerator()
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

    /// <summary>Прибавляет к накопленному. Ноль допустим.</summary>
    public void Add(byte country, TKey key, TValue amount)
    {
        if (amount < default(TValue)) throw new ArgumentOutOfRangeException(nameof(amount));

        var amounts = AmountsOf(country);
        amounts[key] = amounts.GetValueOrDefault(key) + amount;
    }

    /// <summary>Для неизвестного ключа возвращает ноль, а не падает.</summary>
    public TValue Get(byte country, TKey key)
    {
        if (
            !_amounts.TryGetValue(country, out var countryAmounts) ||
            !countryAmounts.TryGetValue(key, out var result)
        )
            return default;

        return result;
    }

    public void Set(byte country, TKey key, TValue amount)
    {
        if (amount < default(TValue)) throw new ArgumentOutOfRangeException(nameof(amount));
        AmountsOf(country)[key] = amount;
    }

    public void Clear()
    {
        _amounts.Clear();
    }

    private Dictionary<TKey, TValue> AmountsOf(byte country)
    {
        if (!_amounts.TryGetValue(country, out var amounts))
        {
            _amounts[country] = amounts = new();
        }

        return amounts;
    }
}
