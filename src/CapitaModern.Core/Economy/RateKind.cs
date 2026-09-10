namespace CapitaModern.Core.Economy;

public enum RateKind
{
    /// <summary>Ставка записана в займе и не меняется.</summary>
    Fixed,

    /// <summary>Ставка идёт за ключевой у кредитора плюс надбавка. От неё расход
    /// меняется на ходу, и потому поднять ключевую против инфляции — значит поднять
    /// себе же процентные платежи.</summary>
    Floating,
}
