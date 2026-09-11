using System.Collections;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace CapitaModern.Core.Economy;

/// <summary>Счётчик «страна → ключ → сколько» на один тик. Нужен, чтобы итог не зависел
/// от порядка обхода: все считают от одних чисел, результат вливается в конце.</summary>
/// <remarks>Внутри плоский массив, а не словарь словарей: обращений за тик под миллион,
/// и на хешировании уходило больше времени, чем на самой экономике. Стран не больше
/// двухсот пятидесяти шести, ключей — сколько значений в перечислении.</remarks>
/// <typeparam name="TKey">Что считаем: товары или типы построек.</typeparam>
/// <typeparam name="TValue">Чем считаем: количеством товара или штуками.</typeparam>
public sealed class Tally<TKey, TValue> :
    IEnumerable<(byte Country, TKey Key, TValue Amount)>
    where TValue : struct, IAdditionOperators<TValue, TValue, TValue>, IComparisonOperators<TValue, TValue, bool>
    where TKey : struct, Enum
{
    private const int Countries = 256;

    private static readonly TKey[] Keys = Enum.GetValues<TKey>();

    private readonly TValue[] _amounts = new TValue[Countries * Keys.Length];

    /// <summary>Какие пары вообще трогали. Ноль ноль'ю, но «положили ноль» и «не клали
    /// ничего» — разные вещи: по первому цена всё равно двигается.</summary>
    private readonly bool[] _touched = new bool[Countries * Keys.Length];

    public IEnumerator<(byte Country, TKey Key, TValue Amount)> GetEnumerator()
    {
        for (var country = 0; country < Countries; country++)
        {
            var start = country * Keys.Length;
            for (var key = 0; key < Keys.Length; key++)
            {
                if (_touched[start + key]) yield return ((byte)country, Keys[key], _amounts[start + key]);
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Прибавляет к накопленному. Ноль допустим.</summary>
    public void Add(byte country, TKey key, TValue amount)
    {
        if (amount < default(TValue)) throw new ArgumentOutOfRangeException(nameof(amount));

        var at = At(country, key);
        _amounts[at] += amount;
        _touched[at] = true;
    }

    /// <summary>Для неизвестного ключа возвращает ноль, а не падает.</summary>
    public TValue Get(byte country, TKey key) => _amounts[At(country, key)];

    public void Set(byte country, TKey key, TValue amount)
    {
        if (amount < default(TValue)) throw new ArgumentOutOfRangeException(nameof(amount));

        var at = At(country, key);
        _amounts[at] = amount;
        _touched[at] = true;
    }

    public void Clear()
    {
        Array.Clear(_amounts);
        Array.Clear(_touched);
    }

    /// <summary>Перечисление у нас всегда на int, так что номер значения — оно само.</summary>
    private static int At(byte country, TKey key) => country * Keys.Length + Unsafe.As<TKey, int>(ref key);
}
