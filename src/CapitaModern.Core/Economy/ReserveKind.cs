namespace CapitaModern.Core.Economy;

/// <summary>В чём страна держит валютные запасы.</summary>
/// <remarks>Пока наполняется только <see cref="ForeignCurrency"/>: под остальное нет
/// рынков. Форма заложена сразу, чтобы потом не переписывать всё, что резервов
/// касается.</remarks>
public enum ReserveKind
{
    /// <summary>Чужая валюта. Заморозить можно.</summary>
    ForeignCurrency,

    /// <summary>Чужие облигации: приносят купон, заморозить можно.</summary>
    Bond,

    /// <summary>Чужие акции: приносят дивиденд, цена ходит, заморозить можно.</summary>
    Equity,

    /// <summary>Золото и прочие металлы. Если лежит дома — не отнять.</summary>
    Metal,

    /// <summary>Криптовалюта. Не отнять, но цена ходит сильнее всего.</summary>
    Crypto,
}
