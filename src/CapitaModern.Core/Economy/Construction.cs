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
    /// <remarks>
    /// В жизни валовое накопление основного капитала — около четверти ВВП, но здесь доля
    /// считается не от ВВП, а от добавленной стоимости компаний, и через неё же проходит
    /// всё возмещение износа: у нас он съедает вчетверо больше, чем в жизни, потому что
    /// строят из двух самых дефицитных товаров.
    ///
    /// Проверено на двадцати годах: 30% дают рост 1.2% в год и полку с пятнадцатого года,
    /// 40% — 1.5%, 55% — 1.9% строго монотонно, 70% — 2.0%, но владельцам не остаётся уже
    /// ничего. Пятьдесят пять — там, где отдача ещё есть, а прибыль владельцам ещё жива.
    /// </remarks>
    public const int InvestmentShare = 55;

    /// <summary>Срок службы теперь у каждого типа свой, см. BuildingInfo.LifeYears.
    /// Здесь остаётся средний по миру — для прикидок и для инструментов.</summary>
    /// <remarks>В статистике отношение капитала к его ежегодному потреблению как раз
    /// около двадцати: здания служат сорок лет, оборудование двенадцать.</remarks>
    public const int AverageLifeYears = 20;

    /// <summary>За сколько суток строится здание. Рук за тик нужно во столько же раз
    /// меньше, чем записано в рецепте.</summary>
    /// <remarks><c>buildWorkers</c> в buildings.json — человеко-дни на всю постройку:
    /// tools/gen-buildcost.mjs сверяет их с настоящими 240 млн строителей, деля на срок
    /// жизни здания в днях. А стройка просила их все разом, за один тик — и упиралась в
    /// руки в двух случаях из трёх, отчего мир терял по три процента предприятий в год.
    /// Год — обычный срок для завода средней руки.</remarks>
    public const int BuildDays = 365;

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
    /// <param name="times">Во сколько раз этот завод в этой стране даст больше обычного, в
    /// сотых. Без множителя выгода считалась по сырому рецепту, а в отстающей стране добыча
    /// и услуги дают вчетверо меньше: мир строил там, где дешевле, и завод потом выдавал
    /// четверть обещанного — заводов материалов стало вдвое больше, а мощность не
    /// сдвинулась.</param>
    public static Money ProfitOf(
        BuildingRecipe recipe, Prices prices, Func<GoodType, int> faced, int times = Efficiency.Scale)
    {
        var made = default(Money);
        foreach (var (good, amount) in recipe.Outputs)
        {
            var mine = times == Efficiency.Scale
                ? amount
                : new GoodAmount(amount.Raw * times / Efficiency.Scale);

            made += AsFaced(prices, good, mine, faced);
        }

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

    /// <summary>Во сколько раз здание вернёт вложенное за свой век, в сотых.</summary>
    /// <remarks>
    /// Прежде выбирали по прибыли на работника. Это мерило годится, когда самое дефицитное
    /// в стране — руки, а у нас на них теряется два с половиной процента загрузки против
    /// четырнадцати на полном складе. Оттого мир и не строил заводы материалов, хотя
    /// материалов ему не хватало: они маленькие, дешёвые и на работника дают немного.
    ///
    /// Считается по чистой прибыли — за вычетом платы работникам, — иначе трудоёмкое
    /// здание выигрывало бы одним тем, что людей на нём больше.
    /// </remarks>
    public static long Payback(Money daily, Money cost, int lifeYears)
    {
        if (cost.Raw <= 0) return daily.Raw > 0 ? long.MaxValue : 0;
        if (daily.Raw <= 0) return 0;

        return (long)((Int128)daily.Raw * lifeYears * 365 * 100 / cost.Raw);
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
