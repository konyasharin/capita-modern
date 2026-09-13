namespace CapitaModern.Core.Economy;

/// <summary>Цена, при которой спрос равен предложению.</summary>
/// <remarks>
/// Прежде цена ползла шагами вслед за покрытием склада, а склад — это накопленный выпуск,
/// который сам зависит от цены. Круг с задержкой в тик: в учебниках он зовётся
/// паутинообразной моделью и расходится сам собой, когда предложение отзывается на цену
/// сильнее спроса. Оттого хозяйство и раскачивалось, а подбор шага и коридора этого не
/// лечил — лечить там нечего, расходимость заложена в устройстве.
///
/// Здесь цена не ползёт, а сразу считается та, при которой рынок сходится. Спрос падает
/// степенью от цены, предложение растёт — и точка пересечения берётся одной формулой:
///
///     D₀·(p/p₀)^(−a) = S₀·(p/p₀)^b   ⟹   p = p₀·(D₀/S₀)^(1/(a+b))
///
/// Ни шага, ни коридора, ни нормы покрытия: цена ограничена сама по себе, потому что
/// зависит от отношения спроса к предложению, а не копится от тика к тику.
/// </remarks>
public static class Clearing
{
    /// <summary>Насколько цена может уйти от обычной за один счёт, в десятитысячных.</summary>
    /// <remarks>Не коридор в прежнем смысле — цена и не стремится уехать, — а защита от
    /// вырожденных случаев: спрос есть, предложения нет вовсе, и отношение уходит в
    /// бесконечность. Сотня раз покрывает любой настоящий эпизод.</remarks>
    private const long MostTimes = 100 * Powers.Scale;

    private const long LeastTimes = Powers.Scale / 100;

    /// <summary>Цена, при которой столько же купят, сколько привезут.</summary>
    /// <param name="usual">Обычная цена товара — та, при которой спрос и предложение
    /// равны по данным.</param>
    /// <param name="wanted">Сколько просят при обычной цене.</param>
    /// <param name="offered">Сколько дают при обычной цене.</param>
    /// <param name="ofDemand">Упругость спроса по цене, в сотых. Положительная.</param>
    /// <param name="ofSupply">Упругость предложения по цене, в сотых.</param>
    public static Money Price(Money usual, GoodAmount wanted, GoodAmount offered, int ofDemand, int ofSupply)
    {
        if (usual.Raw <= 0) return usual;

        // Ни спроса, ни предложения — про товар ничего не известно, цена прежняя.
        if (wanted.Raw <= 0 && offered.Raw <= 0) return usual;

        var stretch = Math.Abs(ofDemand) + Math.Abs(ofSupply);
        if (stretch <= 0) return usual;

        // Предложения нет вовсе — цена идёт к потолку; нет спроса — ко дну.
        if (offered.Raw <= 0) return new Money((long)((Int128)usual.Raw * MostTimes / Powers.Scale));
        if (wanted.Raw <= 0) return new Money(usual.Raw * LeastTimes / Powers.Scale);

        var ratio = (long)((Int128)wanted.Raw * Powers.Scale / offered.Raw);
        var times = Math.Clamp(Powers.PowCached(ratio, Elasticity.Scale * Elasticity.Scale / stretch),
            LeastTimes, MostTimes);

        return new Money((long)((Int128)usual.Raw * times / Powers.Scale));
    }
}
