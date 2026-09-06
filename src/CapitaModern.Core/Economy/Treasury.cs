namespace CapitaModern.Core.Economy;

public sealed class Treasury
{
    /// <summary>Казна в целых. Дробных денег нет намеренно: плавающая точка за тысячи
    /// тиков копит ошибку.</summary>
    public long Balance { get; private set; }

    public Treasury(long balance)
    {
        Balance = balance;
    }

    public void Receive(long amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        Balance += amount;
    }

    /// <summary>Списывает, если хватает. Не хватило — возвращает false, казна не тронута.</summary>
    public bool TrySpend(long amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        if (Balance - amount < 0) return false;

        Balance -= amount;
        return true;
    }
}
