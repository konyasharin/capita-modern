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
    /// <summary>Что население на самом деле купило. По желаемому судить нельзя: бедные
    /// хотят не меньше богатых, у них просто не хватает денег, и вся разница в уровне
    /// жизни именно здесь.</summary>
    private readonly Tally<GoodType, GoodAmount> _bought = new();

    /// <summary>Чего не хватило населению. Пока только копится: смертность и настроения,
    /// на которые это должно влиять, ещё не сделаны.</summary>
    private readonly Tally<GoodType, GoodAmount> _deficit = new();

    /// <summary>Сколько товара хочет население страны за тик. Считается в первом проходе,
    /// чтобы во втором не повторять формулу и не разойтись с дележом.</summary>
    private readonly Tally<GoodType, GoodAmount> _peopleWants = new();

    /// <summary>Что предприятия израсходовали на самом деле. От заказа отличается тем,
    /// что заказ мог не сбыться, а ещё в нём сидит население. По нему считается ВВП.</summary>
    private readonly Tally<GoodType, GoodAmount> _consumed = new();

    /// <summary>Сколько людей просят предприятия страны. С поправкой на эффективность:
    /// один и тот же завод в отстающей стране обслуживает куда больше народу.</summary>
    private readonly Dictionary<byte, long> _jobs = new();

    /// <summary>Во сколько урезана загрузка нехваткой людей, в долях Load.Full.</summary>
    private readonly Dictionary<byte, long> _hands = new();

    /// <summary>Сколько валюты пришло и ушло не за товар, а по капитальному счёту:
    /// займы, погашения, проценты. Без него курс считает только половину баланса.</summary>
    private readonly Dictionary<byte, Money> _capitalIn = new();
    private readonly Dictionary<byte, Money> _capitalOut = new();

    /// <summary>Выплачено зарплат и выручено с населения за тик. Разница — бюджет.</summary>
    private readonly Dictionary<byte, Money> _wages = new();
    private readonly Dictionary<byte, Money> _sales = new();

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

    /// <summary>Заявки на кредитный рынок. Переиспользуется, как и товарные.</summary>
    private readonly List<CreditOrder> _credit = new();

    /// <summary>На сколько денег страна подала заявок на рынок за этот тик. Считать
    /// нужду по свершившемуся ввозу нельзя: у кого нет валюты, тот и не ввозит, и
    /// выходит, что занимать ему незачем.</summary>
    private readonly Tally<GoodType, GoodAmount> _bid = new();

    /// <summary>Кто в этот тик не смог заплатить проценты. Отказ платить — про это, а
    /// не про пустую кассу: в кассе почти всегда что-то есть.</summary>
    private readonly HashSet<byte> _missedPayment = new();

    /// <summary>Сколько тиков прошло с начала партии. Нужно, чтобы помнить, когда кто
    /// отказался платить.</summary>
    private int _day;

    /// <summary>Список товаров нужен каждый тик, а Enum.GetValues каждый раз выделяет
    /// новый массив.</summary>
    private static readonly GoodType[] AllGoods = Enum.GetValues<GoodType>();

    public Simulation(GameWorld world)
    {
        _world = world;
    }

    /// <summary>Сколько лет после отказа платить на рынок не пускают. В жизни
    /// примерно столько и не пускают.</summary>
    public const int DefaultLockYears = 5;

    /// <summary>Нагрузка, после которой долг заведомо не вернуть: вчетверо больше
    /// годового вывоза. В жизни зона риска начинается вдвое раньше.</summary>
    public const int DefaultBurden = 400;

    private const int DaysInYear = 365;

    /// <summary>В каком порядке население тратит кошелёк. Еда прежде всего — на этом и
    /// стоит закон Энгеля.</summary>
    private static readonly GoodType[] NeedsOrder =
        [GoodType.Food, GoodType.Medicine, GoodType.ConsumerGoods, GoodType.Electricity, GoodType.Fuel];

    public void Tick()
    {
        _day++;
        Prepare();
        CollectInputs();
        CountHands();
        Trade();
        NoteTrade();
        Borrow();
        PayInterest();
        Repay();
        CheckDefaults();
        MoveRates();
        CollectAvailable();
        CollectOutputs();
        PayWages();
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
        _jobs.Clear();
        _hands.Clear();
        _wages.Clear();
        _sales.Clear();
        _capitalIn.Clear();
        _capitalOut.Clear();
        _imported.Clear();
        _exported.Clear();
        _bid.Clear();
        _peopleWants.Clear();
        _deficit.Clear();
        _bought.Clear();
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

                // Отстающей стране тот же завод обходится в большее число рук: комбайн
                // против полусотни человек с мотыгами.
                _jobs[owner] = _jobs.GetValueOrDefault(owner) +
                    (long)info.OptimalWorkers * building.Value * Efficiency.Scale /
                    _world.Efficiency.Of(owner, info.Sector);
            }
        }

        // Население — такой же претендент на товар, как отрасли, и со своим весом:
        // карточная система станет обычным законом, который этот вес поднимает.
        foreach (var country in _world.Countries)
        {
            var weight = country.Priorities.WeightOf(Sector.People);
            var capacity = CapacityOf(country);

            foreach (var (good, _) in _world.Needs.BaseRates)
            {
                var wanted = _world.Needs.PerMillion(good, capacity) *
                    _world.PopulationOf(country.Id).Whole / 1_000_000;
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

                // Цену страна видит в своих деньгах: мировая на курс.
                var local = new Money(
                    _world.Market.Prices.Of(good).Raw * country.ExchangeRate.Raw / Money.Scale);
                var usual = _world.Market.Prices.StartOf(good);

                // Норма запаса сама зависит от цены: дёшево — держат больше, дорого —
                // живут с колёс. Без этого страна с полными складами не купит ничего
                // ни при какой дешевизне, и курс уезжает до упора вместо равновесия.
                var target = Elasticity.Adjust(
                    _world.Elasticity.Demand(good),
                    new GoodAmount(Prices.TargetCoverDays * _inputs.Get(country.Id, good).Raw),
                    local,
                    usual,
                    Elasticity.MinStockFactor,
                    Elasticity.MaxStockFactor);

                var stock = country.State.Stock.Of(good);

                if (target > stock)
                {
                    _bid.Add(country.Id, good, target - stock);
                    _orders.Add(new TradeOrder(country.State, target - stock, default));
                }
                else if (stock > target)
                {
                    // Дорого — продают и часть своего запаса, но не больше, чем есть.
                    var offer = Elasticity.Adjust(_world.Elasticity.Supply(good), stock - target, local, usual);
                    if (offer > stock) offer = stock;

                    _orders.Add(new TradeOrder(country.State, default, offer));
                }
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

    /// <summary>Сколько людей заняты на производстве в стране прямо сейчас.</summary>
    public long EmployedIn(byte country) =>
        Math.Min(_jobs.GetValueOrDefault(country), _world.WorkersOf(country));

    /// <summary>Сколько людей просят предприятия. Больше занятых — значит рук не хватает.</summary>
    public long JobsIn(byte country) => _jobs.GetValueOrDefault(country);

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

    /// <summary>Пришло больше, чем было, — приток капитала; меньше — отток.</summary>
    private void NoteCapital(byte country, Money before, Money after)
    {
        _capitalIn[country] = after > before ? after - before : default;
        _capitalOut[country] = before > after ? before - after : default;
    }

    /// <summary>Запоминает вывоз, сглаживая его за год.</summary>
    private void NoteTrade()
    {
        foreach (var country in _world.Countries) country.NoteExports(ExportsOf(country.Id), DaysInYear);
    }

    /// <summary>Кому не хватает валюты — тот занимает, у кого лишняя.</summary>
    /// <remarks>
    /// Без этого деньги — храповик в одну сторону: получить их можно только за экспорт,
    /// и страна с дефицитом доезжает до нуля и перестаёт ввозить. Заём и есть капитальный
    /// счёт, вторая половина платёжного баланса.
    ///
    /// Норма запаса валюты та же, что у товаров: <see cref="Prices.TargetCoverDays"/>
    /// суток ввоза. Меньше — занимают, больше — дают.
    /// </remarks>
    private void Borrow()
    {
        _credit.Clear();
        foreach (var country in _world.Countries)
        {
            // Занимают ровно на то, что заказали и не смогли оплатить.
            var need = Valued(_bid, country.Id);
            var have = country.State.Treasury.Reserves.Liquid;
            var premium = CreditMarket.PremiumFor(
                country.State.Treasury.Debt.BurdenToExports(country.ExportsPerDay * DaysInYear),
                LockedOut(country));

            _credit.Add(new CreditOrder(
                country.Id,
                country.State.Treasury,
                country.State.Custody,
                need > have ? need - have : default,
                have > need ? have - need : default,
                country.KeyRate,
                premium,
                country.MaxBorrowRate));
        }

        // Движение по кредиту — вторая половина платёжного баланса. Считаем его по
        // разнице резервов: страна, раздавшая излишек в долг, не должна выглядеть так,
        // будто у неё вечный профицит.
        foreach (var country in _world.Countries)
        {
            _capitalIn[country.Id] = country.State.Treasury.Reserves.Liquid;
        }

        _world.Credit.Settle(CollectionsMarshal.AsSpan(_credit), _world.Relations.Between);

        foreach (var country in _world.Countries)
        {
            NoteCapital(country.Id, _capitalIn[country.Id], country.State.Treasury.Reserves.Liquid);
        }
    }

    /// <summary>Проценты за сутки. Нечем платить — они уходят в тело долга, и нагрузка
    /// растёт сама собой.</summary>
    private void PayInterest()
    {
        _missedPayment.Clear();
        foreach (var country in _world.Countries)
        {
            foreach (var loan in country.State.Treasury.Debt.Loans)
            {
                if (loan.Principal.Raw == 0) continue;

                var due = loan.InterestPerTick(loan.Lender is { } id ? _world.CountryById(id).KeyRate : 0);
                if (due.Raw == 0) continue;

                if (loan.Lender is { } lender && country.State.Treasury.Reserves.TrySpend(due))
                {
                    var payee = _world.CountryById(lender).State;
                    payee.Treasury.Reserves.Add(Reserves.Incoming((byte)payee.Id, payee.Custody, due));
                }
                else
                {
                    loan.Capitalise(due);
                    _missedPayment.Add(country.Id);
                }
            }

            country.State.Treasury.Debt.Forget();
        }
    }

    /// <summary>Лишняя валюта уходит на погашение, начиная с самого дорогого займа.</summary>
    /// <remarks>Без этого долг только растёт: проценты платятся, тело не гасится никогда,
    /// и любая страна рано или поздно упирается в дефолт.</remarks>
    private void Repay()
    {
        foreach (var country in _world.Countries)
        {
            var debt = country.State.Treasury.Debt;
            if (debt.Owed(LoanSource.Foreign).Raw == 0) continue;

            var spare = country.State.Treasury.Reserves.Liquid - Valued(_bid, country.Id);
            if (spare.Raw <= 0) continue;

            var loan = debt.Priciest(country.KeyRate);
            if (loan is null) continue;

            // Сначала списать, потом гасить: наоборот долг прощался бы бесплатно.
            var paying = spare < loan.Principal ? spare : loan.Principal;
            if (!country.State.Treasury.Reserves.TrySpend(paying)) continue;

            var paid = loan.Repay(paying);

            if (loan.Lender is { } lender)
            {
                var payee = _world.CountryById(lender).State;
                payee.Treasury.Reserves.Add(Reserves.Incoming((byte)payee.Id, payee.Custody, paid));
            }

            debt.Forget();
        }
    }

    /// <summary>Кто не может ни платить, ни вернуть — отказывается платить.</summary>
    /// <remarks>
    /// Без этого мёртвый долг капитализируется вечно: проценты идут в тело, нагрузка
    /// растёт, и страна тащит за собой весь мир. Отказ — не изъян, а выход, и он
    /// недёшев: пять лет без рынка и обвал курса.
    ///
    /// Отказаться можно только по внешнему долгу. Внутренний — это уже не дефолт, а
    /// инфляция или заморозка вкладов, и последствия у них другие.
    /// </remarks>
    private void CheckDefaults()
    {
        foreach (var country in _world.Countries)
        {
            if (LockedOut(country)) continue;

            var debt = country.State.Treasury.Debt;
            if (debt.Owed(LoanSource.Foreign).Raw == 0) continue;
            if (!_missedPayment.Contains(country.Id)) continue;
            if (debt.BurdenToExports(country.ExportsPerDay * DaysInYear) < DefaultBurden) continue;

            debt.Default(LoanSource.Foreign);
            country.DefaultedOnDay = _day;
            country.MoveRate(country.ExchangeRate * 2);
        }
    }

    /// <summary>Не пускают ли страну на кредитный рынок после отказа платить.</summary>
    private bool LockedOut(Country country) =>
        country.DefaultedOnDay > 0 && _day - country.DefaultedOnDay < DaysInYear * DefaultLockYears;

    /// <summary>Курс идёт за сальдо: кто больше ввозит, у того валюта дешевеет.</summary>
    /// <remarks>Петля замыкается через эластичность: подешевевшая валюта поднимает
    /// местную цену импортного, и заявка сама срезается.</remarks>
    private void MoveRates()
    {
        foreach (var country in _world.Countries)
        {
            // Полный платёжный баланс: товары плюс движение капитала. Только по товарам
            // курс экспортёра падал бы вечно — а он раздаёт излишек в долг, и это его
            // уравновешивает ровно так же, как в жизни.
            var outflow = ImportsOf(country.Id) + _capitalOut.GetValueOrDefault(country.Id);
            var inflow = ExportsOf(country.Id) + _capitalIn.GetValueOrDefault(country.Id);

            country.MoveRate(new Money(Drift.Step(
                country.ExchangeRate.Raw, outflow.Raw - inflow.Raw, outflow.Raw + inflow.Raw, Prices.StepPercent)));
        }
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

    /// <summary>Людей на всех не хватает — загрузка режется всем поровну.</summary>
    /// <remarks>Здесь эффективность и начинает работать: она не добавляет выпуска, она
    /// освобождает руки. Страна, которой тот же завод стоит вдесятеро больше людей,
    /// просто не может запустить их все.</remarks>
    private void CountHands()
    {
        foreach (var country in _world.Countries)
        {
            var wanted = _jobs.GetValueOrDefault(country.Id);
            var have = _world.WorkersOf(country.Id);

            _hands[country.Id] = wanted <= have ? Load.Full : have * Load.Full / wanted;
        }
    }

    /// <summary>Сколько базовых корзин покупает дневной доход, в сотых.</summary>
    /// <remarks>Считается в корзинах, а не в деньгах: так не нужен курс. Сравнивать
    /// доходы разных стран через него сейчас нельзя — он сам сломан, и вышел бы круг.
    ///
    /// В первый тик зарплат ещё не платили, доход нулевой — выходит обычная нужда, и это
    /// верно: голодают не оттого, что перехотели, а оттого, что не на что купить.</remarks>
    private int CapacityOf(Country country)
    {
        var basket = default(Money);
        foreach (var (good, rate) in _world.Needs.BaseRates)
        {
            basket += country.State.Prices.CostOf(good, rate);
        }

        var people = _world.PopulationOf(country.Id).Whole;
        if (basket.Raw <= 0 || people <= 0) return Needs.Scale;

        // Корзина посчитана на миллион человек, фонд оплаты — на всю страну.
        var capacity = (Int128)country.Payroll.Raw * 1_000_000 * Needs.Scale / ((Int128)basket.Raw * people);

        return (int)Int128.Clamp(capacity, Needs.MinCapacity, Needs.MaxCapacity);
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
            long runs = count * _hands.GetValueOrDefault(country, Load.Full);
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
    /// <summary>Государство платит зарплату из своей казны.</summary>
    /// <remarks>Сколько — доля труда в том, что произведено. Это стандартное тождество,
    /// и оно не выдумано: в жизни на оплату труда уходит около 55% добавленной стоимости.
    /// Не хватило в казне — платит сколько может, и это уже кризис бюджета.</remarks>
    private void PayWages()
    {
        foreach (var country in _world.Countries)
        {
            var made = ValueAddedOf(country.Id);
            if (made.Raw <= 0) continue;

            var owed = new Money(made.Raw * country.LabourShare / 100);
            var paid = country.State.Treasury.TrySpend(owed) ? owed : PayWhatIsLeft(country);

            country.Households.Earn(paid);
            _wages[country.Id] = paid;

            country.Payroll = paid;
        }
    }

    private static Money PayWhatIsLeft(Country country)
    {
        var left = country.State.Treasury.Balance;
        country.State.Treasury.TrySpend(left);

        return left;
    }

    /// <summary>Население покупает своё, а не берёт со склада даром.</summary>
    /// <remarks>
    /// Порядок задаёт Энгель: сперва еда, потом лекарства, потом всё остальное. Чем беднее
    /// страна, тем большая доля кошелька уходит на первое, и тем раньше кончается на
    /// последнее — это и есть уровень жизни, а не отдельный показатель.
    ///
    /// Нехватка теперь означает две разные беды сразу: не было на складе или не на что
    /// было купить. Различать их станет важно, когда появятся настроения.
    /// </remarks>
    private void FeedPeople()
    {
        foreach (var good in NeedsOrder)
        {
            foreach (var country in _world.Countries)
            {
                var wanted = _peopleWants.Get(country.Id, good);
                if (wanted.Raw == 0) continue;

                var state = country.State;
                var onShelf = state.Stock.Of(good);
                var offered = wanted < onShelf ? wanted : onShelf;

                var bought = offered;
                var cost = state.Prices.CostOf(good, offered);
                if (cost.Raw > 0)
                {
                    var paid = country.Households.SpendUpTo(cost);
                    if (paid < cost) bought = new GoodAmount((long)((Int128)offered.Raw * paid.Raw / cost.Raw));

                    state.Treasury.Receive(paid);
                    _sales[country.Id] = _sales.GetValueOrDefault(country.Id) + paid;
                }

                state.Stock.TakeUpTo(good, bought);
                _bought.Add(country.Id, good, bought);

                var missing = wanted - bought;
                if (missing.Raw > 0) _deficit.Add(country.Id, good, missing);
            }
        }
    }

    /// <summary>Бюджет за прошедший тик: выручка с населения минус зарплаты.</summary>
    public Money BudgetOf(byte country) =>
        _sales.GetValueOrDefault(country) - _wages.GetValueOrDefault(country);

    /// <summary>Сколько базовых корзин покупает дневной доход, в сотых. Для показа и сверки.</summary>
    public int CapacityIn(byte country) => CapacityOf(_world.CountryById(country));

    /// <summary>Доля еды в купленном населением, в сотых.</summary>
    /// <remarks>
    /// Коэффициент Энгеля — самая надёжная проверка уровня жизни: величина безразмерная и
    /// измерена по всему миру. Считается по купленному, а не по желаемому: бедные хотят не
    /// меньше богатых, разница вся в том, на что хватило денег.
    ///
    /// Оценивается в стартовых ценах, а не в местных. Местные у половины стран упёрлись в
    /// коридор, и доля еды мерила бы не достаток, а перекос цен.
    /// </remarks>
    public int EngelOf(byte country)
    {
        var prices = _world.CountryById(country).State.Prices;

        var all = default(Money);
        var food = default(Money);
        foreach (var (good, _) in _world.Needs.BaseRates)
        {
            var cost = new Money((long)((Int128)prices.StartOf(good).Raw *
                _bought.Get(country, good).Raw / GoodAmount.Scale));

            all += cost;
            if (good == GoodType.Food) food = cost;
        }

        return all.Raw > 0 ? (int)(food.Raw * 100 / all.Raw) : 0;
    }

    /// <summary>Сколько всего заплачено зарплат за тик.</summary>
    public Money WagesIn(byte country) => _wages.GetValueOrDefault(country);

    /// <summary>Сколько заплатили одному работнику за тик.</summary>
    public Money WagePerWorkerIn(byte country)
    {
        var employed = EmployedIn(country);

        return employed > 0 ? new Money(_wages.GetValueOrDefault(country).Raw / employed) : default;
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
