namespace CapitaModern.Core.Economy;

/// <summary>Деньги продавца: свои и чужие отдельно.</summary>
/// <remarks>
/// Свою валюту государство может напечатать, чужую — только заработать или занять. Пока
/// это одно число, у страны без экспорта нет выхода вообще; разделив, мы получаем и
/// печатный станок, и заморозку резервов.
///
/// Долей меньше сотой доли тысячи долларов не бывает: за тысячи тиков плавающая точка
/// накопила бы ошибку.
/// </remarks>
public sealed class Treasury
{
    /// <summary>Местные деньги. Пока не растут ниоткуда: внутренний оборот придёт с
    /// зарплатами и налогами.</summary>
    public Money Balance { get; private set; }

    /// <summary>Чужая валюта. Только ей платят за импорт.</summary>
    public Reserves Reserves { get; }

    public Treasury(IEnumerable<Reserve>? reserves = null, Money balance = default)
    {
        Reserves = new Reserves(reserves);
        Balance = balance;
    }

    public void Receive(Money amount)
    {
        if (amount < default(Money)) throw new ArgumentOutOfRangeException(nameof(amount));

        Balance += amount;
    }

    /// <summary>Списывает, если хватает. Не хватило — возвращает false, казна не тронута.</summary>
    public bool TrySpend(Money amount)
    {
        if (amount < default(Money)) throw new ArgumentOutOfRangeException(nameof(amount));
        if (Balance - amount < default(Money)) return false;

        Balance -= amount;
        return true;
    }
}
