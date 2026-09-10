namespace CapitaModern.Core.Economy;

/// <summary>Деньги продавца. Долей меньше сотой доли тысячи долларов не бывает: за
/// тысячи тиков плавающая точка накопила бы ошибку.</summary>
public sealed class Treasury
{
    public Money Balance { get; private set; }

    public Treasury(Money balance)
    {
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
