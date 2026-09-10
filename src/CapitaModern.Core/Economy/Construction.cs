namespace CapitaModern.Core.Economy;

/// <summary>Что стране выгоднее всего построить.</summary>
/// <remarks>
/// Отсюда берётся сравнительное преимущество, и никакой отдельной логики для него не
/// нужно. Выгода меряется прибылью на работника: у страны, которая умеет электронику,
/// тот же завод обслуживает меньше людей, значит и отдача с человека выше. Она и строит
/// электронику, а отстающая — рудники, потому что там разрыв в умении меньше всего.
///
/// Пока заводы стоят там, где их поставили данными, специализироваться нечем: экспорт
/// упирается в остатки, а не в разделение труда.
/// </remarks>
public static class Construction
{
    /// <summary>Какая доля добавленной стоимости уходит на стройку, в сотых.</summary>
    /// <remarks>В жизни валовое накопление основного капитала — около четверти ВВП.</remarks>
    public const int InvestmentShare = 25;

    /// <summary>Сколько лет служит завод.</summary>
    /// <remarks>Выводится из двух известных величин: капитал стоит три годовых выпуска,
    /// вкладывают в него четверть выпуска — значит за двенадцать лет он и обновляется.
    /// Без износа стройка ничем не уравновешена, и мир застраивается без предела.</remarks>
    public const int LifeYears = 12;

    /// <summary>Насколько прибыльнее должен быть новый завод, чтобы ради него закрыли
    /// старый, в сотых. Без запаса страна бы металась туда-сюда каждый тик.</summary>
    public const int ClosingMargin = 150;

    /// <summary>Прибыль завода за тик в местных ценах: что выпустил минус что съел.</summary>
    public static Money ProfitOf(BuildingRecipe recipe, Prices prices)
    {
        var made = default(Money);
        foreach (var (good, amount) in recipe.Outputs) made += prices.CostOf(good, amount);

        var spent = default(Money);
        foreach (var (good, amount) in recipe.Inputs) spent += prices.CostOf(good, amount);

        return made - spent;
    }

    /// <summary>Отдача с одного работника. По ней и выбирают, что строить.</summary>
    /// <param name="efficiency">Множитель страны для этой отрасли, в сотых.</param>
    public static long ValuePerWorker(Money profit, int workers, int efficiency)
    {
        if (workers <= 0) return profit.Raw > 0 ? long.MaxValue : 0;

        // Умелой стране тот же завод обходится в меньшее число рук, значит отдача выше.
        var hands = Math.Max(1, (long)workers * Efficiency.Scale / Math.Max(1, efficiency));

        return profit.Raw / hands;
    }

    /// <summary>Во что обойдётся стройка в местных ценах.</summary>
    public static Money CostOf(IReadOnlyDictionary<GoodType, GoodAmount> buildCost, Prices prices)
    {
        var total = default(Money);
        foreach (var (good, amount) in buildCost) total += prices.CostOf(good, amount);

        return total;
    }
}

/// <summary>То немногое от постройки, что нужно расчёту выгоды. Заведено, чтобы
/// <see cref="Construction"/> не тянул за собой весь каталог зданий.</summary>
public readonly record struct BuildingRecipe(
    IReadOnlyDictionary<GoodType, GoodAmount> Inputs,
    IReadOnlyDictionary<GoodType, GoodAmount> Outputs);
