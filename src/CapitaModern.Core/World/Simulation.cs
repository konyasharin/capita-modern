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

    /// <summary>Что страна ввезла и вывезла за тик. Из них считается сальдо.</summary>
    private readonly Tally<GoodType, GoodAmount> _imported = new();
    private readonly Tally<GoodType, GoodAmount> _exported = new();

    /// <summary>Заявки на кредитный рынок. Переиспользуется, как и товарные.</summary>
    private readonly List<CreditOrder> _credit = new();

    /// <summary>На сколько денег страна подала заявок на рынок за этот тик. Считать
    /// нужду по свершившемуся ввозу нельзя: у кого нет валюты, тот и не ввозит, и
    /// выходит, что занимать ему незачем.</summary>
    private readonly Tally<GoodType, GoodAmount> _bid = new();

    /// <summary>Кто в этот тик не смог заплатить проценты. Отказ платить — про это, а
    /// не про пустую кассу: в кассе почти всегда что-то есть.</summary>
    private readonly HashSet<byte> _missedPayment = new();

    /// <summary>Накопленный износ по областям. Целыми заводами он осыпается редко,
    /// поэтому дробная часть копится.</summary>
    private readonly Dictionary<int, int> _decay = new();

    /// <summary>Топливо, сожжённое перевозкой. Отдельно от прочего расхода: это не
    /// сырьё для завода, а плата за расстояние.</summary>
    private readonly Tally<GoodType, GoodAmount> _burnedFuel = new();

    /// <summary>Кто сколько получил за проход чужих грузов.</summary>
    private readonly Dictionary<byte, Money> _transit = new();

    /// <summary>Заявки на парную торговлю по одному товару. Переиспользуются.</summary>
    private readonly List<MarketOrder> _market = new();

    private readonly Exchange _exchange = new();

    /// <summary>Какой товар сводится прямо сейчас. Сделка о товаре не знает, а списывать
    /// со склада надо именно его.</summary>
    private GoodType _trading;

    /// <summary>Сколько денег и товара прошло по сделкам: из них выходит цена рынка.</summary>
    private Money _dealValue;
    private GoodAmount _dealVolume;

    /// <summary>Сколько тиков подряд страна не платит по долгу.</summary>
    private readonly Dictionary<byte, int> _missedInARow = new();

    /// <summary>Сколько страна не смогла оплатить за этот тик.</summary>
    private readonly Dictionary<byte, Money> _unpaid = new();

    /// <summary>Доля валюты страны в мировых резервах, в десятитысячных.</summary>
    private readonly Dictionary<byte, long> _reserveShare = new();

    private const int ReserveShareScale = 10_000;

    /// <summary>Чья отрасль у каждого товара. Считается раз при создании мира.</summary>
    private readonly Sector[] _sectorOf = new Sector[Enum.GetValues<GoodType>().Length];

    /// <summary>Выпуск страны при полной загрузке, в стартовых ценах. База для якоря.</summary>
    /// <remarks>Считается из данных, а не меряется на первом тике: на первом тике склады
    /// пусты, половина заводов стоит без сырья, и база выходит втрое ниже правды. Якорь
    /// потом принимал разгон за рост выпуска и давил цены в десятки раз.</remarks>
    private readonly Dictionary<byte, Money> _baseReal = new();

    /// <summary>Рабочие места в услугах и на стройке за прошедший тик.</summary>
    private long _serviceJobs;
    private long _buildJobs;

    /// <summary>Сколько рук ушло на стройку в каждой стране в прошлом тике.</summary>
    private readonly Dictionary<byte, long> _builders = new();

    /// <summary>Уровень цен страны и мира с прошлого тика. Курс считается до выпуска,
    /// поэтому берёт вчерашние: сутки задержки здесь ничего не решают.</summary>
    private readonly Dictionary<byte, int> _level = new();
    private int _worldLevel = PriceLevel.Scale;

    /// <summary>Спрос стройки отдельно от заводского. Заводы держат сорокадневный запас
    /// сырья, а стройка съедает своё в тот же тик: цемент впрок не закупают.</summary>
    private readonly Tally<GoodType, GoodAmount> _build = new();

    /// <summary>Что страна собралась строить в этот тик. Решается вместе со спросом,
    /// чтобы стройку видели и цена, и мировой рынок: без этого главный потребитель
    /// материалов не создавал спроса, и они лежали на полу у 196 стран.</summary>
    private readonly Dictionary<byte, (BuildingType Type, Region Where, int Count)> _plan = new();

    /// <summary>Отложенные на стройку деньги. Завод стоит три своих годовых выпуска,
    /// за тик такого не накопить — поэтому копится.</summary>
    private readonly Dictionary<byte, Money> _investment = new();

    /// <summary>Сколько тиков прошло с начала партии. Нужно, чтобы помнить, когда кто
    /// отказался платить.</summary>
    private int _day;

    /// <summary>Список товаров нужен каждый тик, а Enum.GetValues каждый раз выделяет
    /// новый массив.</summary>
    private static readonly GoodType[] AllGoods = Enum.GetValues<GoodType>();

    /// <summary>Список типов построек: перебирается каждый тик при выборе стройки.</summary>
    private static readonly BuildingType[] AllBuildings = Enum.GetValues<BuildingType>();

    /// <summary>Сколько единиц страна ставит за тик. Предел от бесконечного цикла, а не
    /// от смысла: упираются обычно в отложенное или в материалы куда раньше. Единица
    /// мощности мелкая, поэтому и предел высокий — миру нужно около двухсот тысяч в год.</summary>
    private const int MaxBuildsPerTick = 500;

    /// <summary>Во сколько долей считается износ. Целыми заводами он осыпается редко,
    /// а сроки службы у типов разные — без общей доли их не сложить.</summary>
    private const int WearScale = 1000;

    public Simulation(GameWorld world)
    {
        _world = world;
        MapSectors();
        MeasurePotential();
    }

    /// <summary>Сколько лет после отказа платить на рынок не пускают. В жизни
    /// примерно столько и не пускают.</summary>
    public const int DefaultLockYears = 5;

    /// <summary>Сколько суток подряд можно не платить, прежде чем это станет отказом.
    /// По суверенным облигациям льготный срок обычно тридцать дней, по кредитам МВФ и
    /// клубов кредиторов доходит до полугода; девяносто — середина.</summary>
    public const int GraceDays = 90;

    /// <summary>Нагрузка, после которой долг заведомо не вернуть: вчетверо больше того,
    /// чем страна может платить за год. В жизни зона риска начинается вдвое раньше.</summary>
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
        PlanBuilds();
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
        AnchorPrices();
        Wear();
        Build();
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
        _serviceJobs = 0;
        _buildJobs = 0;
        _unpaid.Clear();
        _consumed.Clear();
        _jobs.Clear();
        _hands.Clear();
        _wages.Clear();
        _sales.Clear();
        _build.Clear();
        _plan.Clear();
        _burnedFuel.Clear();
        _transit.Clear();
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
                var hands = _world.Efficiency.HandsFor(
                    owner, info.Sector, (long)info.OptimalWorkers * building.Value);

                _jobs[owner] = _jobs.GetValueOrDefault(owner) + hands;
                if (info.Sector == Sector.Services) _serviceJobs += hands;
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
            // Услуги через границу не возят: стрижку покупают там же, где живут.
            if (good == GoodType.Services) continue;

            _trading = good;
            _market.Clear();

            var wanted = default(GoodAmount);
            var offered = default(GoodAmount);

            foreach (var country in _world.Countries)
            {
                if (!country.TradeAccess.CanTrade(good)) continue;

                // Своя цена, а не мировая: из разницы между странами и берётся торговля.
                var local = country.State.Prices.Of(good);
                var usual = country.State.Prices.StartOf(good);

                // Норма запаса сама зависит от цены: дёшево — держат больше, дорого —
                // живут с колёс. Без этого страна с полными складами не купит ничего ни
                // при какой дешевизне, и курс уезжает до упора вместо равновесия.
                // Запас держат под заводы и людей; стройке нужно ровно на сегодня.
                var forBuilding = _build.Get(country.Id, good);
                var flow = _inputs.Get(country.Id, good) - forBuilding;

                var target = Elasticity.Adjust(
                    _world.Elasticity.Demand(good),
                    new GoodAmount(Prices.TargetCoverDays * flow.Raw + forBuilding.Raw),
                    local,
                    usual,
                    Elasticity.MinStockFactor,
                    Elasticity.MaxStockFactor);

                var stock = country.State.Stock.Of(good);

                // Торгуются в мировой мере: у каждого своя валюта и свой курс.
                var inWorld = new Money(local.Raw * Money.Scale / Math.Max(1, country.ExchangeRate.Raw));

                // Страна разом и покупатель, и продавец. Просит она не только нехватку,
                // но и дневной расход: часть его закроет своим товаром, часть чужим, и
                // ровно отсюда берётся встречная торговля одним и тем же товаром.
                var shortfall = target > stock ? target - stock : default;
                var bid = shortfall + flow;

                var offer = default(GoodAmount);
                if (stock > target)
                {
                    // Дорого — продают и часть своего запаса, но не больше, чем есть.
                    offer = Elasticity.Adjust(
                        _world.Elasticity.Supply(good), stock - target, local, usual,
                        Elasticity.MinFactor, Elasticity.MaxSupplyFactor);

                    if (offer > stock) offer = stock;
                }

                if (bid.Raw == 0 && offer.Raw == 0) continue;

                _bid.Add(country.Id, good, bid);
                _market.Add(new MarketOrder(
                    country.Id, country.State, offer, bid, inWorld, inWorld,
                    _world.Efficiency.Of(country.Id, SectorOf(good))));
                wanted += bid;
                offered += offer;
            }

            _dealValue = default;
            _dealVolume = default;

            _exchange.Settle(CollectionsMarshal.AsSpan(_market), Delivered, Close);

            // Цена рынка — средняя из настоящих сделок, а не выдуманная одна на всех.
            // Не сошлось ни одной — двигаем прежним правилом, по перекосу заявок.
            var average = _dealVolume.Raw > 0
                ? new Money((long)((Int128)_dealValue.Raw * GoodAmount.Scale / _dealVolume.Raw))
                : default;

            if (average >= Prices.Floor)
            {
                _world.Market.Prices.Set(good, average);
            }
            else
            {
                _world.Market.Prices.MoveFromBalance(good, wanted, offered);
            }
        }
    }

    /// <summary>Чем страна может платить по внешнему долгу за год.</summary>
    /// <remarks>
    /// Обычной стране — только вывозом: чтобы отдать чужие деньги, их надо сперва
    /// заработать. А тому, чью валюту мир держит в резервах, отдавать можно своими же:
    /// их примут. Оттого США занимают под процент при долге в четырнадцать годовых
    /// вывозов, а Пакистан не занимает вовсе.
    /// </remarks>
    private Money DebtCapacity(Country country)
    {
        var exports = country.ExportsPerDay * DaysInYear;

        var share = _reserveShare.GetValueOrDefault(country.Id);
        if (share <= 0 || country.LabourShare <= 0) return exports;

        // Выпуск за год в мировой мере: фонд оплаты — известная доля добавленной стоимости.
        var daily = new Money(country.Payroll.Raw * 100 / country.LabourShare);
        var yearly = new Money(daily.Raw * DaysInYear * Money.Scale /
            Math.Max(1, country.ExchangeRate.Raw));

        return exports + new Money((long)((Int128)yearly.Raw * share / ReserveShareScale));
    }

    /// <summary>Какая доля мировых резервов лежит в валюте каждой страны.</summary>
    /// <remarks>Считается из состояния мира, а не задана: резервной станет та валюта,
    /// которую и правда начнут копить.</remarks>
    private void CountReserves()
    {
        _reserveShare.Clear();

        var total = 0L;
        foreach (var country in _world.Countries)
        {
            foreach (var held in country.State.Treasury.Reserves.Held)
            {
                if (held.Kind != ReserveKind.ForeignCurrency || held.Issuer == country.Id) continue;

                _reserveShare[held.Issuer] = _reserveShare.GetValueOrDefault(held.Issuer) + held.Amount.Raw;
                total += held.Amount.Raw;
            }
        }

        if (total <= 0) return;

        foreach (var issuer in _reserveShare.Keys.ToArray())
        {
            _reserveShare[issuer] = (long)((Int128)_reserveShare[issuer] * ReserveShareScale / total);
        }
    }

    /// <summary>Чья это отрасль. Нужна, чтобы знать, насколько страна умеет делать
    /// именно этот товар: нефть качают везде одинаково, станки — нет.</summary>
    private Sector SectorOf(GoodType good) => _sectorOf[(int)good];

    /// <summary>Во что обойдётся покупателю единица товара у этого продавца.</summary>
    /// <remarks>Цена продавца плюс дорога от него до покупателя плюс пошлина покупателя.
    /// Отсюда и берётся то, чего в общем котле быть не могло: дальний дешёвый товар
    /// проигрывает ближнему дорогому.</remarks>
    private Money Delivered(MarketOrder seller, MarketOrder buyer)
    {
        var route = _world.Routes.CostBetween(seller.Country, buyer.Country);
        if (route >= Politics.Routes.Unreachable) return default;

        var markup = _world.TradeCosts.ImportMarkup(buyer.Country, _trading, route);

        return new Money(seller.Ask.Raw * (TradeCosts.Scale + markup) / TradeCosts.Scale);
    }

    /// <summary>Проводит сделку: деньги, товар, топливо и плата за проход.</summary>
    private void Close(Deal deal)
    {
        var buyer = _world.CountryById(deal.Buyer);
        var seller = _world.CountryById(deal.Seller);

        if (!buyer.State.Treasury.Reserves.TrySpend(deal.Paid))
        {
            // Не хватило валюты. Под это и занимают: иначе страна без резервов не может
            // начать ввозить, а не ввозя — не может показать, что ей нужна валюта.
            _unpaid[deal.Buyer] = _unpaid.GetValueOrDefault(deal.Buyer) + deal.Paid;

            return;
        }
        if (seller.State.Stock.TakeUpTo(_trading, deal.Amount).Raw != deal.Amount.Raw) return;

        buyer.State.Stock.Store(_trading, deal.Amount);
        seller.State.Treasury.Reserves.Add(
            Reserves.Incoming((byte)seller.State.Id, seller.State.Custody, deal.Paid));

        _imported.Add(deal.Buyer, _trading, deal.Amount);
        _exported.Add(deal.Seller, _trading, deal.Amount);
        _dealValue += deal.Paid;
        _dealVolume += deal.Amount;

        BurnFuel(deal.Buyer, deal.Seller, _trading, deal.Amount);
        PayTransit(deal.Buyer, deal.Seller, deal.Paid);
    }

    /// <summary>Сколько страна выпустила бы при полной загрузке, в стартовых ценах.</summary>
    /// <remarks>От этого числа якорь и считает, вырос выпуск или упал. Меняться оно
    /// должно вместе со стройкой и износом, но пока предприятия стоят на месте весь
    /// расчёт — одного раза хватает.</remarks>
    /// <summary>Отрасль каждого товара — по тому, кто его делает.</summary>
    private void MapSectors()
    {
        foreach (var type in Enum.GetValues<BuildingType>())
        {
            var info = _world.Buildings[type];
            foreach (var good in info.Outputs.Keys) _sectorOf[(int)good] = info.Sector;
        }
    }

    private void MeasurePotential()
    {
        foreach (var region in _world.Regions)
        {
            var owner = region.LargestOwner;
            var prices = _world.CountryById(owner).State.Prices;

            foreach (var (type, count) in region.BuildingsCount)
            {
                foreach (var (good, amount) in _world.Buildings[type].Outputs)
                {
                    var worth = new Money(
                        (long)((Int128)prices.StartOf(good).Raw * amount.Raw * count / GoodAmount.Scale));

                    _baseReal[owner] = _baseReal.GetValueOrDefault(owner) + worth;
                }
            }
        }
    }

    /// <summary>Держит общий уровень цен у количества денег на единицу выпуска.</summary>
    /// <remarks>Относительные цены не трогаются: их задало покрытие в <see cref="MovePrices"/>,
    /// здесь двигается только уровень — все цены страны разом и в одну сторону.</remarks>
    private void AnchorPrices()
    {
        var nominalWorld = default(Money);
        var realWorld = default(Money);

        foreach (var country in _world.Countries)
        {
            var (nominal, real) = OutputValue(country);
            if (real.Raw <= 0) continue;

            nominalWorld += nominal;
            realWorld += real;

            var level = PriceLevel.Of(nominal, real);
            _level[country.Id] = level;

            // Своих денег нет — нет и центробанка, тянуть уровень нечем.
            var supply = country.Bank.Supply;
            if (supply.Raw <= 0) continue;

            var realBefore = _baseReal.GetValueOrDefault(country.Id);
            if (realBefore.Raw <= 0) continue;

            // Уровень не подталкивается на долю перекоса, а приравнивается деньгам:
            // покрытие двигает свои цены полным шагом, и подталкивание ему проигрывало.
            // Ограничена только скорость — не больше шага за тик, чтобы не прыгало.
            var want = PriceLevel.Target(supply, supply - country.Bank.Printed, real, realBefore);
            var step = Math.Clamp(
                want,
                level * (100 - Prices.StepPercent) / 100,
                level * (100 + Prices.StepPercent) / 100);

            country.State.Prices.Rescale(step, level);
        }

        _worldLevel = PriceLevel.Of(nominalWorld, realWorld);
    }

    /// <summary>Выпуск страны в своих ценах и в стартовых. Из их отношения выходит
    /// уровень цен, из знаменателя — реальный рост.</summary>
    private (Money Nominal, Money Real) OutputValue(Country country)
    {
        var prices = country.State.Prices;
        var nominal = default(Money);
        var real = default(Money);

        foreach (var good in AllGoods)
        {
            var made = _outputs.Get(country.Id, good);
            if (made.Raw == 0) continue;

            nominal += prices.CostOf(good, made);
            real += new Money((long)((Int128)prices.StartOf(good).Raw * made.Raw / GoodAmount.Scale));
        }

        return (nominal, real);
    }

    /// <summary>Сколько рабочих мест в услугах. Считается отдельно: настоящая занятость
    /// в услугах вдвое больше промышленной, и складывать их в один замер нечестно.</summary>
    public long ServiceJobs => _serviceJobs;

    /// <summary>Сколько рук занято на стройке.</summary>
    public long BuildJobs => _buildJobs;

    /// <summary>Загрузка предприятий страны в сотых долях от полной. Меньше единицы —
    /// значит рук не хватило и всё производство идёт вполсилы.</summary>
    public long LoadIn(byte country) => _hands.GetValueOrDefault(country, Load.Full);

    /// <summary>Что страна выпустила за тик по одному товару.</summary>
    public GoodAmount OutputOf(byte country, GoodType good) => _outputs.Get(country, good);

    /// <summary>Сколько людей заняты на производстве в стране прямо сейчас.</summary>
    public long EmployedIn(byte country) =>
        Math.Min(_jobs.GetValueOrDefault(country), _world.WorkersOf(country));

    /// <summary>Сколько людей просят предприятия. Больше занятых — значит рук не хватает.</summary>
    public long JobsIn(byte country) => _jobs.GetValueOrDefault(country);

    /// <summary>Сколько мир не купил из-за дорогой доставки и сколько — из-за того, что
    /// товара ни у кого не осталось.</summary>
    public (GoodAmount Refused, GoodAmount Empty) UnfilledBids => (_exchange.Refused, _exchange.Empty);

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
        foreach (var country in _world.Countries)
        {
            country.NoteExports(ExportsOf(country.Id), DaysInYear);
            country.NoteImports(ImportsOf(country.Id), DaysInYear);
        }
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
        CountReserves();

        _credit.Clear();
        foreach (var country in _world.Countries)
        {
            // Занимают под запас валюты на ввоз, а не под весь оборот: своё покупают за
            // свои же деньги, и чужая валюта на это не нужна. Раньше здесь стояла
            // стоимость всех заявок, и страна каждый тик занимала под дневной расход
            // целиком — оттого внешний долг и рос без предела.
            var need = new Money(country.ImportsPerDay.Raw * Prices.TargetCoverDays);

            // Валюта нужна и на погашение: старый долг гасят новым, и в жизни это норма,
            // а не крайность. Без этого страна у черты потолка не могла перезанять и
            // отказывалась платить — за пять лет так делали больше половины стран.
            var owed = country.State.Treasury.Debt.Owed(LoanSource.Foreign);
            need += new Money(owed.Raw / CreditMarket.LoanYears / DaysInYear * Prices.TargetCoverDays);

            // У кого ввоза ещё не было, тому нормы не из чего вывести: считаем по тому,
            // что он сегодня не смог оплатить.
            var unpaid = _unpaid.GetValueOrDefault(country.Id);
            if (unpaid > need) need = unpaid;
            var have = country.State.Treasury.Reserves.Liquid;
            var capacity = DebtCapacity(country);
            var premium = CreditMarket.PremiumFor(
                country.State.Treasury.Debt.BurdenToExports(capacity),
                LockedOut(country));

            var want = need > have ? need - have : default;

            // Больше, чем сможет обслуживать, не дадут ни под какой процент: за чертой
            // дефолта заём — это не сделка, а подарок. У кого платить нечем вовсе, того
            // держит не потолок, а ставка: нагрузка без вывоза уходит в бесконечность.
            if (capacity.Raw > 0)
            {
                var ceiling = new Money(capacity.Raw / 100 * CreditMarket.SafeBurden);
                var room = ceiling > owed ? ceiling - owed : default;
                if (want > room) want = room;
            }

            _credit.Add(new CreditOrder(
                country.Id,
                country.State.Treasury,
                country.State.Custody,
                want,
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
            var owed = debt.Owed(LoanSource.Foreign);
            if (owed.Raw == 0) continue;

            // Заём гасится по графику, а не когда останутся деньги. Иначе долг только
            // растёт: страна платит проценты, а тело не трогает никогда.
            var due = new Money(owed.Raw / (CreditMarket.LoanYears * DaysInYear));
            if (due.Raw > 0 && Settle(country, due) < due) _missedPayment.Add(country.Id);

            // Сверх графика гасят из того, что осталось после закупок: дорогое вперёд.
            var spare = country.State.Treasury.Reserves.Liquid - Valued(_bid, country.Id);
            if (spare.Raw > 0) Settle(country, spare);

            debt.Forget();
        }
    }

    /// <summary>Гасит тело займов, начиная с самого дорогого, и говорит, сколько ушло.</summary>
    private Money Settle(Country country, Money amount)
    {
        var debt = country.State.Treasury.Debt;
        var paid = default(Money);

        while (amount.Raw > 0)
        {
            var loan = debt.Priciest(country.KeyRate);
            if (loan is null) break;

            // Сначала списать, потом гасить: наоборот долг прощался бы бесплатно.
            var paying = amount < loan.Principal ? amount : loan.Principal;
            if (paying.Raw == 0 || !country.State.Treasury.Reserves.TrySpend(paying)) break;

            var went = loan.Repay(paying);
            amount -= went;
            paid += went;

            if (loan.Lender is { } lender)
            {
                var payee = _world.CountryById(lender).State;
                payee.Treasury.Reserves.Add(Reserves.Incoming((byte)payee.Id, payee.Custody, went));
            }
        }

        return paid;
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

            // Один пропущенный платёж — ещё не отказ: должнику дают срок договориться,
            // и в жизни договариваются чаще, чем отказываются. Считаем пропуски подряд.
            if (!_missedPayment.Contains(country.Id))
            {
                _missedInARow.Remove(country.Id);
                continue;
            }

            var missed = _missedInARow.GetValueOrDefault(country.Id) + 1;
            _missedInARow[country.Id] = missed;
            if (missed < GraceDays) continue;
            // Той же меркой, что и ставка: у кого валюту держат в резервах, тот платит
            // своими деньгами, и по вывозу его судить нельзя.
            if (debt.BurdenToExports(DebtCapacity(country)) < DefaultBurden) continue;

            debt.Default(LoanSource.Foreign);
            country.DefaultedOnDay = _day;
            _missedInARow.Remove(country.Id);

            // Курс здесь не трогается. Раньше отказ его удваивал, но курс теперь держит
            // паритет, и через несколько тиков он этот сдвиг откручивал. Валюта обвалится
            // сама, когда обвал будет чему держать: подорожавшему в местных деньгах ввозу.
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

            // Две силы на одно число. Паритет — куда курс тянет разница уровней цен: у
            // кого цены выросли вдвое против мира, у того и валюта вдвое дешевле. Сальдо —
            // отклонение от паритета, а не весь курс: иначе курс уезжал бы куда угодно,
            // лишь бы баланс сходился.
            var rate = country.ExchangeRate.Raw;
            if (_level.TryGetValue(country.Id, out var level) && _worldLevel > 0)
            {
                var parity = Money.FromWhole(1).Raw * (long)level / _worldLevel;
                rate = Drift.Step(rate, parity - rate, parity + rate, Prices.StepPercent);
            }

            rate = Drift.Step(
                rate, outflow.Raw - inflow.Raw, outflow.Raw + inflow.Raw, Prices.StepPercent);

            // Третья сила: сколько осталось резервов. Правило достаточности — запас на
            // сорок суток ввоза; кто проедает его, у того валюта слабеет, и ввоз дорожает
            // сам. Без этого страна с пустой казной продолжала покупать в долг без конца.
            var norm = country.ImportsPerDay.Raw * Prices.TargetCoverDays;
            if (norm > 0)
            {
                var left = country.State.Treasury.Reserves.Liquid.Raw;
                rate = Drift.Step(rate, norm - left, norm + left, Prices.StepPercent);
            }

            country.MoveRate(new Money(rate));
        }
    }

    /// <summary>Перевозка сжигает топливо. Это не наценка, а настоящий расход, и по нему
    /// на транспорт уходит четверть мировой нефти — как и в жизни.</summary>
    private void BurnFuel(byte country, byte from, GoodType good, GoodAmount brought)
    {
        if (good == GoodType.Fuel) return; // топливо везёт само себя, второй раз не считаем

        var freight = _world.TradeCosts.FreightOf(good) *
            (100 + _world.Routes.CostBetween(from, country)) / 100;

        if (freight == 0) return;

        // Считается по стартовым ценам, а не по нынешним: топливо жжёт не стоимость
        // груза, а его вес и расстояние. По нынешним выходила петля — подешевевшее
        // топливо жглось щедрее, и перевозка съедала его больше, чем мир добывал.
        var state = _world.CountryById(country).State;
        var fuelPrice = state.Prices.StartOf(GoodType.Fuel).Raw;
        if (fuelPrice <= 0) return;

        var worth = (Int128)state.Prices.StartOf(good).Raw * brought.Raw / GoodAmount.Scale;
        var spent = worth * freight / TradeCosts.Scale * TradeCosts.FuelInFreight / 100;
        var burned = new GoodAmount((long)(spent * GoodAmount.Scale / fuelPrice));
        _burnedFuel.Add(country, GoodType.Fuel, state.Stock.TakeUpTo(GoodType.Fuel, burned));
    }

    /// <summary>Платит хозяевам звеньев на пути от продавца к покупателю.</summary>
    /// <remarks>Это не пошлина: деньги уходят не себе в казну, а третьей стране. Отсюда и
    /// берётся, зачем держать пролив или дорогу — и зачем их перекрывать.</remarks>
    private void PayTransit(byte country, byte from, Money value)
    {
        var takers = _world.Routes.TollsBetween(from, country);
        if (takers.Count == 0) return;

        var payer = _world.CountryById(country);
        foreach (var through in takers)
        {
            var fee = new Money(value.Raw * TradeCosts.TransitFee / TradeCosts.Scale);
            if (fee.Raw == 0 || !payer.State.Treasury.Reserves.TrySpend(fee)) break;

            var host = _world.CountryById(through).State;
            host.Treasury.Reserves.Add(Reserves.Incoming((byte)host.Id, host.Custody, fee));
            _transit[through] = _transit.GetValueOrDefault(through) + fee;
        }
    }

    /// <summary>Сколько страна заработала на чужом транзите за тик.</summary>
    public Money TransitEarnedBy(byte country) => _transit.GetValueOrDefault(country);

    /// <summary>Сколько топлива сожгла перевозка за тик. Показывать это стоит: в жизни на
    /// транспорт уходит около четверти нефти.</summary>
    public GoodAmount FuelBurnedIn(byte country) => _burnedFuel.Get(country, GoodType.Fuel);

    /// <summary>Сколько страна ввезла за прошедший тик, в деньгах по ценам рынка.</summary>
    /// <summary>Что страна ввезла за тик. С ценами — в них: сальдо считается вычитанием,
    /// и обе половины должны быть в одной мере.</summary>
    public Money ImportsOf(byte country, Prices? at = null) => Valued(_imported, country, at);

    /// <summary>Сколько страна вывезла за прошедший тик.</summary>
    /// <summary>Что страна вывезла за тик. Без цен на входе — в нынешних мировых, с
    /// ценами — в них: сравнивать вывоз с ВВП можно только в одной и той же мере.</summary>
    public Money ExportsOf(byte country, Prices? at = null) => Valued(_exported, country, at);

    private Money Valued(Tally<GoodType, GoodAmount> what, byte country, Prices? at = null)
    {
        var prices = at ?? _world.Market.Prices;
        var total = default(Money);
        foreach (var good in AllGoods)
        {
            total += prices.CostOf(good, what.Get(country, good));
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

    /// <summary>Решает, что строить, и заявляет это как спрос наравне с заводами.</summary>
    private void PlanBuilds()
    {
        foreach (var country in _world.Countries)
        {
            var purse = _investment.GetValueOrDefault(country.Id);
            var best = BestBuild(country, purse);
            if (best is null) continue;

            var info = _world.Buildings[best.Value.Type];
            var price = Construction.CostOf(info.BuildCost, country.State.Prices) + WagesFor(country, info);
            if (price.Raw <= 0) continue;

            // Копить больше, чем на пару единиц, незачем: не найдя материалов, страна
            // накапливала бы на сотни и заявляла спрос, которого мир не выдержит.
            var ceiling = new Money(price.Raw * MaxBuildsPerTick);
            if (purse > ceiling) _investment[country.Id] = purse = ceiling;

            var count = (int)(purse.Raw / price.Raw);

            // Стройке нужны руки, и берёт она их у заводов: больше, чем свободно, не
            // построишь ни за какие деньги.
            var perUnit = _world.Efficiency.HandsFor(country.Id, info.Sector, info.BuildWorkers);

            if (perUnit > 0)
            {
                var free = _world.WorkersOf(country.Id) - _jobs.GetValueOrDefault(country.Id);
                count = (int)Math.Min(count, Math.Max(0, free / perUnit));
            }

            if (count <= 0) continue;

            _plan[country.Id] = (best.Value.Type, best.Value.Where, count);

            var weight = country.Priorities.WeightOf(info.Sector);
            foreach (var (good, amount) in info.BuildCost)
            {
                var wanted = amount * count;
                _inputs.Add(country.Id, good, wanted);
                _build.Add(country.Id, good, wanted);
                _claims.Add(country.Id, good, wanted * weight / Priorities.NormalWeight);
            }

            // Строители — такие же рабочие руки и конкурируют с заводами за людей. Берём
            // вчерашних: нанимают под то, что и правда строится, а сколько построится,
            // выяснится только в конце тика, когда приедут материалы.
            var builders = _builders.GetValueOrDefault(country.Id);

            _jobs[country.Id] = _jobs.GetValueOrDefault(country.Id) + builders;
            _buildJobs += builders;
        }
    }

    /// <summary>Во что обойдётся работа строителей по местной зарплате.</summary>
    private Money WagesFor(Country country, Buildings.BuildingInfo info)
    {
        if (country.Payroll.Raw <= 0) return default;

        return new Money(country.Payroll.Raw / Math.Max(1, EmployedIn(country.Id)) * info.BuildWorkers);
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

            var times = _world.Efficiency.OutputTimes(country, recipe.Sector);
            foreach (var (good, amount) in recipe.Outputs)
            {
                var made = amount * runs / Load.Full;
                _outputs.Add(country, good, times == Efficiency.Scale
                    ? made
                    : new GoodAmount(made.Raw * times / Efficiency.Scale));
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
            var paid = owed;
            if (!country.State.Treasury.TrySpend(owed))
            {
                // Не хватило — печатают недостающее. Не смогли и этого — платят сколько есть.
                Print(country, owed);
                if (!country.State.Treasury.TrySpend(owed)) paid = PayWhatIsLeft(country);
            }

            country.Households.Earn(paid);
            _wages[country.Id] = paid;

            country.Payroll = paid;
        }
    }

    /// <summary>Занять не вышло, платить надо — печатают недостающее.</summary>
    /// <remarks>Цена немедленная: местные цены растут на столько же, на сколько выросла
    /// денежная масса, и ровно настолько же слабеет валюта. Так это и работает в жизни,
    /// только не сразу, а за месяцы.</remarks>
    private static void Print(Country country, Money owed)
    {
        // Печатать может тот, у кого есть своя денежная масса. У страны без неё нет и
        // центробанка, а на нуле любая эмиссия была бы бесконечным ростом цен.
        if (country.Bank.Supply.Raw <= 0) return;

        var missing = owed - country.State.Treasury.Balance;
        if (missing.Raw <= 0) return;

        var growth = country.Bank.Emit(missing, EmissionKind.Open);
        country.State.Treasury.Receive(missing);
        if (growth == 0) return;

        foreach (var good in Enum.GetValues<GoodType>())
        {
            country.State.Prices.Set(good, new Money(
                country.State.Prices.Of(good).Raw * (100 + growth) / 100));
        }

        country.MoveRate(new Money(country.ExchangeRate.Raw * (100 + growth) / 100));
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

    /// <summary>Изношенное разваливается. Считается остатком: за срок службы должен
    /// осыпаться весь капитал, а по одному заводу в тик этого не набрать.</summary>
    private void Wear()
    {
        foreach (var region in _world.Regions)
        {
            // Каждый тип осыпается со своей скоростью: здания стоят вдвое дольше заводов.
            var wear = 0;
            foreach (var (type, count) in region.BuildingsCount)
            {
                wear += count * WearScale / _world.Buildings[type].LifeYears;
            }

            if (wear == 0) continue;

            _decay[region.Id] = _decay.GetValueOrDefault(region.Id) + wear;
            var span = WearScale * DaysInYear;
            var due = _decay[region.Id] / span;
            if (due == 0) continue;

            _decay[region.Id] -= due * span;

            // Осыпается самое многочисленное: так износ не выбивает единственный завод.
            var worst = default(BuildingType);
            var most = 0;
            foreach (var (type, count) in region.BuildingsCount)
            {
                if (count <= most) continue;

                most = count;
                worst = type;
            }

            region.TryRemoveBuildings(worst, Math.Min(due, most));
        }
    }

    /// <summary>Страна откладывает часть выпуска и строит то, что ей выгоднее всего.</summary>
    /// <remarks>
    /// Здесь и рождается сравнительное преимущество. Выгода меряется прибылью на
    /// работника: умелой стране тот же завод обходится меньшим числом рук, значит отдача
    /// с человека выше. Она и строит сложное, а отстающая — добычу, где разрыв в умении
    /// меньше всего. Никакой отдельной логики для этого не требуется.
    /// </remarks>
    private void Build()
    {
        foreach (var country in _world.Countries)
        {
            var added = ValueAddedOf(country.Id);
            if (added.Raw > 0)
            {
                _investment[country.Id] = _investment.GetValueOrDefault(country.Id) +
                    new Money(added.Raw * Construction.InvestmentShare / 100);
            }

            if (!_plan.TryGetValue(country.Id, out var plan)) continue;

            var info = _world.Buildings[plan.Type];
            var wages = WagesFor(country, info);
            var price = Construction.CostOf(info.BuildCost, country.State.Prices) + wages;
            var done = 0;

            for (var built = 0; built < plan.Count; built++)
            {
                if (_investment.GetValueOrDefault(country.Id) < price) break;
                if (!country.State.Stock.TryConsume(info.BuildCost, Load.Full)) break;

                _investment[country.Id] -= price;
                plan.Where.AddBuildings(plan.Type, 1);

                // Съеденное стройкой — такой же расход, как заводское сырьё. Без этого
                // материалы попадали бы в добавленную стоимость дважды: и когда их
                // сделали, и когда из них построили.
                foreach (var (good, amount) in info.BuildCost) _consumed.Add(country.Id, good, amount);

                // Строителям платят, и деньги уходят в те же кошельки, что и зарплата.
                if (country.State.Treasury.TrySpend(wages)) country.Households.Earn(wages);
                done++;
            }

            _builders[country.Id] = _world.Efficiency.HandsFor(
                country.Id, info.Sector, (long)info.BuildWorkers * done);
        }
    }

    /// <summary>Что и где стране строить по средствам. Пусто, если ничего не подходит.</summary>
    private (BuildingType Type, Region Where)? BestBuild(Country country, Money purse)
    {
        (BuildingType Type, Region Where)? best = null;
        var bestValue = 0L;

        foreach (var type in AllBuildings)
        {
            var info = _world.Buildings[type];
            if (info.BuildCost.Count == 0) continue;

            var profit = Construction.ProfitOf(
                new BuildingRecipe(info.Inputs, info.Outputs),
                country.State.Prices,
                good => Faced(country, good));
            if (profit.Raw <= 0) continue;
            if (Construction.CostOf(info.BuildCost, country.State.Prices) > purse) continue;

            var value = Construction.ValuePerWorker(
                profit, info.OptimalWorkers, _world.Efficiency.Of(country.Id, info.Sector));

            if (value <= bestValue) continue;

            var where = PlaceFor(country, info);
            if (where is null) continue;

            bestValue = value;
            best = (type, where);
        }

        return best;
    }

    /// <summary>По какой цене страна имеет дело с товаром, в сотых процента к обычной.</summary>
    /// <remarks>Чего не хватает — то придётся везти: дороже на перевозку и пошлину. Чего
    /// в избытке — то повезут от неё, и за перевозку платит она же: дешевле на ту же
    /// величину. Отсюда и выходит «сделать или купить»: строить стоит то, что дорого
    /// везти, а не то, что дорого стоит.</remarks>
    private int Faced(Country country, GoodType good)
    {
        var need = new GoodAmount(Prices.TargetCoverDays * _inputs.Get(country.Id, good).Raw);
        var markup = _world.TradeCosts.ImportMarkup(country.Id, good, _world.Routes.CostTo(country.Id));

        return country.State.Stock.Of(good) < need ? markup : -markup;
    }

    /// <summary>Где ставить: своя область, с месторождением если добыча, и та, где
    /// больше людей. Список уже отсортирован, поэтому берём первую подходящую.</summary>
    private Region? PlaceFor(Country country, Buildings.BuildingInfo info)
    {
        foreach (var region in _world.RegionsOf(country.Id))
        {
            if (info.RequiresDeposit is { } deposit && !region.HasDeposit(deposit)) continue;

            return region;
        }

        return null;
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
