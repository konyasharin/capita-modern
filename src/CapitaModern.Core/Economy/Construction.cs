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

    /// <summary>Срок службы теперь у каждого типа свой, см. BuildingInfo.LifeYears.
    /// Здесь остаётся средний по миру — для прикидок и для инструментов.</summary>
    /// <remarks>В статистике отношение капитала к его ежегодному потреблению как раз
    /// около двадцати: здания служат сорок лет, оборудование двенадцать.</remarks>
    public const int AverageLifeYears = 20;

    /// <summary>Насколько прибыльнее должен быть новый завод, чтобы ради него закрыли
    /// старый, в сотых. Без запаса страна бы металась туда-сюда каждый тик.</summary>
    public const int ClosingMargin = 150;

    /// <summary>Прибыль завода за тик: что выпустил минус что съел, по цене с доставкой.</summary>
    /// <remarks>
    /// Считать надо не по биржевой цене, а по той, которую страна на самом деле платит за
    /// ввоз: с перевозкой и пошлиной. Это и есть решение «сделать или купить» — своё
    /// выгодно ровно тогда, когда привозное дороже.
    ///
    /// Без этого перевозка и пошлины ничего не решают: страна ввозит то, чего не хватает
    /// до нормы запаса, и альтернатива нигде не сравнивается. Оттого цемент и возят через
    /// океан, хотя везти его дороже, чем сделать на месте.
    /// </remarks>
    /// <param name="faced">По какой цене страна на самом деле имеет дело с товаром, в
    /// сотых процента к обычной: чего не хватает — дороже на перевозку и пошлину, чего в
    /// избытке — дешевле на ту же перевозку, потому что вывозя, за неё платишь сам.</param>
    public static Money ProfitOf(BuildingRecipe recipe, Prices prices, Func<GoodType, int> faced)
    {
        var made = default(Money);
        foreach (var (good, amount) in recipe.Outputs) made += AsFaced(prices, good, amount, faced);

        var spent = default(Money);
        foreach (var (good, amount) in recipe.Inputs) spent += AsFaced(prices, good, amount, faced);

        return made - spent;
    }

    private static Money AsFaced(Prices prices, GoodType good, GoodAmount amount, Func<GoodType, int> faced)
    {
        // Ниже десятой доли цена не опускается: даже самый громоздкий товар чего-то стоит.
        var factor = Math.Max(TradeCosts.Scale / 10, TradeCosts.Scale + faced(good));

        return new Money(prices.CostOf(good, amount).Raw * factor / TradeCosts.Scale);
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
