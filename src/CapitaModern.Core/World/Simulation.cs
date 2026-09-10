using System.Runtime.InteropServices;
using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;
using CapitaModern.Core.Politics;

namespace CapitaModern.Core.World;

/// <summary>Тик экономики: предприятия съедают сырьё и выдают продукцию.</summary>
/// <remarks>Два прохода: сначала считаем спрос всей страны, потом производим.
/// Дефицит делится на всех потребителей товара сразу.</remarks>
public sealed class Simulation
{
    private readonly GameWorld _world;

    /// <summary>Сколько каждого товара заказали все предприятия страны за этот тик.</summary>
    private readonly Tally<GoodType, GoodAmount> _inputs = new();

    /// <summary>Что произведено за тик. В склады вливается только в конце.</summary>
    private readonly Tally<GoodType, GoodAmount> _outputs = new();

    /// <summary>Склады на начало тика. Во втором проходе живой склад убывает, а доли
    /// должны считаться от одних и тех же чисел.</summary>
    private readonly Tally<GoodType, GoodAmount> _available = new();

    /// <summary>Тот же заказ, но умноженный на вес отрасли. По нему делится нехватка.</summary>
    private readonly Tally<GoodType, GoodAmount> _claims = new();
    /// <summary>Чего не хватило населению. Пока только копится: смертность и настроения,
    /// на которые это должно влиять, ещё не сделаны.</summary>
    private readonly Tally<GoodType, GoodAmount> _deficit = new();

    /// <summary>Сколько товара хочет население страны за тик. Считается в первом проходе,
    /// чтобы во втором не повторять формулу и не разойтись с дележом.</summary>
    private readonly Tally<GoodType, GoodAmount> _peopleWants = new();

    /// <summary>Что предприятия израсходовали на самом деле. От заказа отличается тем,
    /// что заказ мог не сбыться, а ещё в нём сидит население. По нему считается ВВП.</summary>
    private readonly Tally<GoodType, GoodAmount> _consumed = new();

    /// <summary>Работоспособные предприятия по стране и типу. Считаются вместе, где бы
    /// ни стояли: склад у страны общий.</summary>
    private readonly Tally<BuildingType, int> _working = new();

    /// <summary>Заявки на рынок по одному товару. Переиспользуется: тик их создаёт по
    /// штуке на товар, а мусора за год набежало бы много.</summary>
    private readonly List<TradeOrder> _orders = new();

    /// <summary>Склады участников до сведения сделок: по разнице видно, кто сколько
    /// купил и продал.</summary>
    private readonly List<GoodAmount> _stockBefore = new();

    /// <summary>Что страна ввезла и вывезла за тик. Из них считается сальдо.</summary>
    private readonly Tally<GoodType, GoodAmount> _imported = new();
    private readonly Tally<GoodType, GoodAmount> _exported = new();

    /// <summary>Чья заявка стоит на этом месте в списке.</summary>
    private readonly List<byte> _byOrder = new();

    /// <summary>Список товаров нужен каждый тик, а Enum.GetValues каждый раз выделяет
    /// новый массив.</summary>
    private static readonly GoodType[] AllGoods = Enum.GetValues<GoodType>();

    public Simulation(GameWorld world)
    {
        _world = world;
    }

    public void Tick()
    {
        Prepare();
        CollectInputs();
        Trade();
        CollectAvailable();
        CollectOutputs();
        FeedPeople();
        Store();
        MovePrices();
        UpdateDemographics();
    }

    /// <summary>Счётчики живут один тик. Чистим в начале, чтобы прошлые числа можно было
    /// посмотреть.</summary>
    private void Prepare()
    {
        _inputs.Clear();
        _outputs.Clear();
        _available.Clear();
        _claims.Clear();
        _working.Clear();
        _consumed.Clear();
        _imported.Clear();
        _exported.Clear();
        _peopleWants.Clear();
        _deficit.Clear();
    }

    /// <summary>Может ли предприятие работать в этой области. Зовётся только из первого
    /// прохода — во второй попадает уже готовый список.</summary>
    private bool CanWork(Region region, BuildingType building)
    {
        return _world.Buildings[building].RequiresDeposit is not { } deposit || region.HasDeposit(deposit);
    }

    private void CollectInputs()
    {
        foreach (var region in _world.Regions)
        {
            // Один раз на область: LargestOwner перебирает доли ячеек.
            var owner = region.LargestOwner;

            foreach (var building in region.BuildingsCount)
            {
                if (!CanWork(region, building.Key)) continue;
                var info = _world.Buildings[building.Key];
                var weight = _world.CountryById(owner).Priorities.WeightOf(info.Sector);

                foreach (var input in info.Inputs)
                {
                    var demand = new GoodAmount(building.Value * input.Value.Raw);
                    _inputs.Add(owner, input.Key, demand);
                    _claims.Add(owner, input.Key, demand * weight / Priorities.NormalWeight);
                }
                _working.Add(owner, building.Key, building.Value);
            }
        }

        // Население — такой же претендент на товар, как отрасли, и со своим весом:
        // карточная система станет обычным законом, который этот вес поднимает.
        foreach (var country in _world.Countries)
        {
            var weight = country.Priorities.WeightOf(Sector.People);
            foreach (var (good, ratePerMillion) in _world.Consumption)
            {
                var wanted = ratePerMillion * _world.PopulationOf(country.Id).Whole / 1_000_000;
                _peopleWants.Add(country.Id, good, wanted);
                _inputs.Add(country.Id, good, wanted);
                _claims.Add(country.Id, good, wanted * weight / Priorities.NormalWeight);
            }
        }
    }

    /// <summary>Склады на начало тика. Берём все товары, а не только заказанные: цена
    /// того, что никому не нужно, тоже должна двигаться — вниз.</summary>
    /// <summary>Докупить недостающее и продать лишнее. Стоит после <see cref="CollectInputs"/>
    /// (нужен спрос) и до <see cref="CollectAvailable"/> (склады уже другие).</summary>
    /// <remarks>Норма запаса та же, что двигает цену: страна докупает до неё и продаёт
    /// всё сверх. Одна константа на два механизма, и торговля сама чинит цены, которые
    /// иначе упирались бы в коридор.</remarks>
    private void Trade()
    {
        foreach (var good in AllGoods)
        {
            _orders.Clear();
            _stockBefore.Clear();
            _byOrder.Clear();
            foreach (var country in _world.Countries)
            {
                if (!country.TradeAccess.CanTrade(good)) continue;

                var target = new GoodAmount(Prices.TargetCoverDays * _inputs.Get(country.Id, good).Raw);
                var stock = country.State.Stock.Of(good);

                if (target > stock) _orders.Add(new TradeOrder(country.State, target - stock, default));
                else if (stock > target) _orders.Add(new TradeOrder(country.State, default, stock - target));
                else continue;

                _stockBefore.Add(stock);
                _byOrder.Add(country.Id);
            }

            _world.Market.Settle(good, CollectionsMarshal.AsSpan(_orders));

            for (var i = 0; i < _orders.Count; i++)
            {
                var now = _orders[i].Trader.Stock.Of(good);
                if (now > _stockBefore[i]) _imported.Add(_byOrder[i], good, now - _stockBefore[i]);
                else if (_stockBefore[i] > now) _exported.Add(_byOrder[i], good, _stockBefore[i] - now);
            }
        }
    }

    /// <summary>Что мир выпустил за прошедший тик.</summary>
    public GoodAmount WorldOutputOf(GoodType good) => WorldSum(_outputs, good);

    /// <summary>Сколько мир заказал за прошедший тик — и заводы, и население.</summary>
    public GoodAmount WorldDemandOf(GoodType good) => WorldSum(_inputs, good);

    private GoodAmount WorldSum(Tally<GoodType, GoodAmount> what, GoodType good)
    {
        var total = default(GoodAmount);
        foreach (var country in _world.Countries) total += what.Get(country.Id, good);

        return total;
    }

    /// <summary>Сколько страна ввезла за прошедший тик, в деньгах по ценам рынка.</summary>
    public Money ImportsOf(byte country) => Valued(_imported, country);

    /// <summary>Сколько страна вывезла за прошедший тик.</summary>
    public Money ExportsOf(byte country) => Valued(_exported, country);

    private Money Valued(Tally<GoodType, GoodAmount> what, byte country)
    {
        var total = default(Money);
        foreach (var good in AllGoods)
        {
            total += _world.Market.Prices.CostOf(good, what.Get(country, good));
        }

        return total;
    }

    private void CollectAvailable()
    {
        foreach (var country in _world.Countries)
        {
            foreach (var good in AllGoods)
            {
                _available.Set(country.Id, good, country.State.Stock.Of(good));
            }
        }
    }

    private void CollectOutputs()
    {
        foreach (var (country, building, count) in _working)
        {
            var recipe = _world.Buildings[building];
            var weight = _world.CountryById(country).Priorities.WeightOf(recipe.Sector);

            // Доля общая на всех, поэтому расход рецепта в ней сокращается.
            // Умножаем до деления, иначе целые числа дадут ноль.
            long runs = count * Load.Full;
            foreach (var (good, _) in recipe.Inputs)
            {
                GoodAmount available = _available.Get(country, good);
                GoodAmount demand = _inputs.Get(country, good);
                // Хватает всем — загрузка остаётся полной. Заодно не считаем самое
                // большое произведение: переполниться оно могло бы только здесь.
                if (available >= demand) continue;

                // Доля отрасли — её вес против весов остальных претендентов.
                // Int128: пять множителей до деления не влезают в long.
                GoodAmount claims = _claims.Get(country, good);
                runs = Math.Min(
                    runs,
                    (long)(
                        (Int128)Load.Full * count * weight * available.Raw /
                        (claims.Raw * Priorities.NormalWeight)
                    )
                );
            }

            if (runs == 0) continue;

            // Приведение безопасно: runs не может превысить count, с которого начали.
            var consumed = _world.CountryById(country).State.Stock.TryConsume(recipe.Inputs, runs);
            if (!consumed) throw new InvalidOperationException("Не получилось потратить предметы " +
                                                               "со склада, ошибка в расчетах в коде");

            foreach (var (good, amount) in recipe.Inputs)
            {
                // То же выражение, что внутри TryConsume: расход должен совпасть до доли.
                _consumed.Add(country, good, amount * runs / Load.Full);
            }

            foreach (var (good, amount) in recipe.Outputs)
            {
                _outputs.Add(country, good, amount * runs / Load.Full);
            }
        }
    }

    /// <summary>Население забирает свою долю. Недобор одного товара не отменяет выдачу
    /// остальных — в отличие от рецепта, где либо всё, либо ничего.</summary>
    private void FeedPeople()
    {
        foreach (var (country, good, wanted) in _peopleWants)
        {
            var deficit = wanted - _world.CountryById(country).State.Stock.TakeUpTo(good, wanted);
            if (deficit.Raw == 0) continue;
            _deficit.Add(country, good, deficit);
        }
    }

    private void Store()
    {
        foreach (var (country, good, amount) in _outputs)
        {
            _world.CountryById(country).State.Stock.Store(good, amount);
        }
    }

    /// <summary>Цены двигаются в конце тика: спрос за тик против того запаса, что был
    /// на его начало.</summary>
    private void MovePrices()
    {
        foreach (var (country, good, available) in _available)
        {
            _world.CountryById(country).State.Prices.MoveFromCover(good, _inputs.Get(country, good), available);
        }
    }

    /// <summary>Добавленная стоимость страны за прошедший тик: что выпущено минус то,
    /// что на это ушло. Сумма по всем странам — мировой ВВП за сутки.</summary>
    /// <remarks>Может быть отрицательной: значит, сырьё стоит дороже продукции.</remarks>
    /// <param name="at">В каких ценах считать. По умолчанию в своих: тогда это
    /// номинальный ВВП. Постоянные цены дают реальный, который и надо сравнивать по годам.</param>
    public Money ValueAddedOf(byte country, Prices? at = null)
    {
        var prices = at ?? _world.CountryById(country).State.Prices;
        var total = default(Money);

        foreach (var good in AllGoods)
        {
            total += prices.CostOf(good, _outputs.Get(country, good));
            total -= prices.CostOf(good, _consumed.Get(country, good));
        }

        return total;
    }

    private void UpdateDemographics()
    {
        var random = new Random(123313); // пока делаем изменение население рандомным и всегда в плюс;
        foreach (var region in _world.Regions)
        {
            var diff = random.Next(1, 10);
            region.Demographics.Grow(Population.FromWhole(diff));
        }
        _world.UpdatePopulations();
    }

}
