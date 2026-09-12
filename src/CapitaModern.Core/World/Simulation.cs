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

    /// <summary>Куда якорь двинул уровень цен на прошлом тике, в сотых долях процента.</summary>
    private readonly Dictionary<byte, int> _levelPush = new();
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
    private readonly Dictionary<byte, Money> _profits = new();
    private readonly Dictionary<byte, Money> _saved = new();
    private readonly Dictionary<byte, Company?> _builder = new();
    private readonly Dictionary<byte, Money> _soldCurrency = new();
    private readonly Dictionary<byte, Money> _boughtCurrency = new();

    /// <summary>Добавленная стоимость за тик. Считается в PayWages и переиспользуется:
    /// перебор всех товаров на каждую страну стоит дороже самого тика.</summary>
    private readonly Dictionary<byte, Money> _added = new();

    /// <summary>Страна, за которую модель ничего не решает сама: ни строит, ни занимает.
    /// За неё это делает игрок.</summary>
    public byte? HandsOff { get; set; }

    /// <summary>Что страна велела строить в обход выбора по прибыли и сколько вложенных
    /// денег на это ещё не потрачено.</summary>
    private readonly Dictionary<byte, (BuildingType Type, Money Left)> _ordered = new();

    /// <summary>Сколько тиков прошло с начала партии. Нужно, чтобы помнить, когда кто
    /// отказался платить.</summary>
    private int _day;

    /// <summary>Список товаров нужен каждый тик, а Enum.GetValues каждый раз выделяет
    /// новый массив.</summary>
    private static readonly GoodType[] AllGoods = Enum.GetValues<GoodType>();

    /// <summary>Список типов построек: перебирается каждый тик при выборе стройки.</summary>
    private static readonly BuildingType[] AllBuildings = Enum.GetValues<BuildingType>();

    /// <summary>Сколько единиц одна компания ставит за тик. Раньше предел стоял впятеро
    /// выше и был защитой от бесконечного цикла, а не смыслом: упирались в отложенное или в
    /// материалы куда раньше. С компаниями упираться перестали — у них есть накопленное, — и
    /// тик подорожал втрое: двести стран по пятьсот проверок склада на каждую.</summary>
    private const int MaxBuildsPerTick = 50;

    /// <summary>Какую долю склада стройка может съесть за тик, в сотых.</summary>
    /// <remarks>Вычерпать полку за день нельзя. Заказ на тысячу зданий растягивается на
    /// несколько дней, и каждый следующий день платит уже подорожавшую цену: спрос стройки
    /// попадает в заявку и двигает цену на конце тика. Без этого вся тысяча покупалась бы
    /// по вчерашней цене разом.</remarks>
    private const int BiteOfStock = 34;

    /// <summary>По каким товарам за тик сошлась хоть одна сделка.</summary>
    private readonly bool[] _dealt = new bool[Enum.GetValues<GoodType>().Length];

    /// <summary>Во сколько долей считается износ. Целыми заводами он осыпается редко,
    /// а сроки службы у типов разные — без общей доли их не сложить.</summary>
    private const int WearScale = 1000;

    public Simulation(GameWorld world)
    {
        _world = world;
        MapSectors();
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
        Tax();
        BuildEstate();
        Dole();
        MovePrices();
        AnchorPrices();
        PullPrices();
        Wear();
        Banking();
        Build();
        PayProfits();
        NoteDemand();
        UpdateDemographics();
    }

    /// <summary>Счётчики живут один тик. Чистим в начале, чтобы прошлые числа можно было
    /// посмотреть.</summary>
    private void Prepare()
    {
        _inputs.Clear();
        _outputs.Clear();
        _available.Clear();
        Array.Clear(_dealt);
        _claims.Clear();
        _working.Clear();
        _serviceJobs = 0;
        _buildJobs = 0;
        _unpaid.Clear();
        _consumed.Clear();
        _jobs.Clear();
        _hands.Clear();
        _wages.Clear();
        foreach (var country in _world.Countries) country.Budget.NewTick();

        _sales.Clear();
        _armsWants.Clear();
        _armsBought.Clear();
        _houseWants.Clear();
        _roadWants.Clear();
        _housing.Clear();
        _roads.Clear();
        _profits.Clear();
        _saved.Clear();
        _builder.Clear();
        _soldCurrency.Clear();
        _boughtCurrency.Clear();
        // _added не чистим: госзаказ и мера долга считаются до PayWages, и им нужен
        // вчерашний выпуск. Каждому тику он всё равно переписывается заново.
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

            OrderArms(country);
            OrderEstate(country);
        }
    }

    /// <summary>Госзаказ: сколько оружия страна хочет купить за сутки.</summary>
    /// <remarks>
    /// Считается от выпуска: страна тратит на оборону свою долю добавленной стоимости, а
    /// доля эта — настоящая, из data/politics/defence.json. Делится заказ поровну между
    /// всеми военными товарами: чем страна вооружается, модель пока не решает.
    ///
    /// Заказ идёт в общий спрос наравне с населением и стройкой — значит его видят и цена,
    /// и торговля, и приоритеты снабжения. Купят его потом, в <see cref="Order"/>, и
    /// только если в бюджете есть деньги.
    /// </remarks>
    private void OrderArms(Country country)
    {
        if (country.DefenceShare <= 0) return;

        var budget = new Money(_added.GetValueOrDefault(country.Id).Raw * country.DefenceShare / 10_000);
        if (budget.Raw <= 0) return;

        var each = new Money(budget.Raw / Arms.Length);
        var weight = country.Priorities.WeightOf(Sector.Military);

        foreach (var good in Arms)
        {
            var price = country.State.Prices.Of(good);
            if (price.Raw <= 0) continue;

            var wanted = new GoodAmount((long)((Int128)each.Raw * GoodAmount.Scale / price.Raw));
            if (wanted.Raw <= 0) continue;

            _armsWants.Add(country.Id, good, wanted);
            _inputs.Add(country.Id, good, wanted);
            _claims.Add(country.Id, good, wanted * weight / Priorities.NormalWeight);
        }
    }

    /// <summary>Сколько жилья и дорог страна хочет построить за сутки.</summary>
    /// <remarks>
    /// Доли настоящие: жилищное строительство в мире около пяти процентов ВВП, казённые
    /// вложения в дороги и сети — около трёх. Считаются они от выпуска, а платят за
    /// построенное разные карманы: за жильё люди, за дороги бюджет.
    /// </remarks>
    private void OrderEstate(Country country)
    {
        var added = _added.GetValueOrDefault(country.Id);
        if (added.Raw <= 0) return;

        Want(country, GoodType.Materials, new Money(added.Raw * HousingShare / 10_000 * 2 / 3), _houseWants);
        Want(country, GoodType.Timber, new Money(added.Raw * HousingShare / 10_000 / 3), _houseWants);
        Want(country, GoodType.Materials, new Money(added.Raw * RoadShare / 10_000 * 3 / 4), _roadWants);
        Want(country, GoodType.Metals, new Money(added.Raw * RoadShare / 10_000 / 4), _roadWants);
    }

    /// <summary>Кладёт заявку на столько-то денег в общий спрос.</summary>
    private void Want(Country country, GoodType good, Money money, Tally<GoodType, GoodAmount> into)
    {
        if (money.Raw <= 0) return;

        var price = country.State.Prices.Of(good);
        if (price.Raw <= 0) return;

        var wanted = new GoodAmount((long)((Int128)money.Raw * GoodAmount.Scale / price.Raw));
        if (wanted.Raw <= 0) return;

        into.Add(country.Id, good, wanted);
        _inputs.Add(country.Id, good, wanted);
        _claims.Add(country.Id, good, wanted);
    }

    /// <summary>Доля выпуска на жильё, в сотых процента. В жизни жилищное строительство —
    /// около пяти процентов ВВП.</summary>
    private const int HousingShare = 500;

    /// <summary>Доля выпуска на дороги и сети. В жизни казённые вложения — около трёх с
    /// третью процентов ВВП.</summary>
    private const int RoadShare = 330;

    private readonly Tally<GoodType, GoodAmount> _houseWants = new();
    private readonly Tally<GoodType, GoodAmount> _roadWants = new();

    /// <summary>Сколько жильё и дороги забрали за тик, в деньгах.</summary>
    public Money HousingOf(byte country) => _housing.GetValueOrDefault(country);

    public Money RoadsOf(byte country) => _roads.GetValueOrDefault(country);

    private readonly Dictionary<byte, Money> _housing = new();
    private readonly Dictionary<byte, Money> _roads = new();

    /// <summary>Что покупает армия. Всё, что делают оборонные заводы.</summary>
    private static readonly GoodType[] Arms =
    [
        GoodType.SmallArms, GoodType.Ammunition, GoodType.Armour, GoodType.Artillery,
        GoodType.Missiles, GoodType.Aircraft, GoodType.AirDefence, GoodType.StrikeDrones,
        GoodType.TacticalDrones, GoodType.ElectronicWarfare,
    ];

    private readonly Tally<GoodType, GoodAmount> _armsWants = new();

    /// <summary>Сколько оружия страна заказала за тик.</summary>
    public GoodAmount ArmsWantOf(byte country, GoodType good) => _armsWants.Get(country, good);

    /// <summary>Сколько страна потратила на оружие за тик.</summary>
    public Money ArmsBoughtOf(byte country) => _armsBought.GetValueOrDefault(country);

    private readonly Dictionary<byte, Money> _armsBought = new();

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
                _world.Market.Prices.MoveToward(good, average);
                _dealt[(int)good] = true;
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
        if (share <= 0) return exports;

        // Выпуск за год в мировой мере. Раньше он выводился из фонда оплаты труда, но фонд
        // теперь считается от проданного, а не от всего выпуска, и выводить из него выпуск
        // стало нельзя: у Китая мера долга падала вчетверо на ровном месте.
        var daily = _added.GetValueOrDefault(country.Id);
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

    /// <summary>Местную сумму в мировую меру.</summary>
    public static Money InWorld(Country country, Money local) =>
        new((long)((Int128)local.Raw * Money.Scale / Math.Max(1, country.ExchangeRate.Raw)));

    /// <summary>Сумма в мировой мере, пересчитанная в деньги страны.</summary>
    public Money InLocal(Country country, Money world) =>
        new((long)((Int128)world.Raw * country.ExchangeRate.Raw / Money.Scale));

    /// <summary>Продаёт валюту из резервов за местные деньги. Возвращает, сколько своих
    /// денег получено.</summary>
    /// <remarks>
    /// Валютное окно центробанка: скупая валюту, он создаёт местные деньги, продавая —
    /// изымает. Занятое и выручка за вывоз приходят резервами, а зарплаты и стройка идут
    /// на свои, и без обмена одно с другим не связано вовсе.
    ///
    /// Сам по себе, каждый тик, обмен пока не идёт. Пробовал менять сальдо за тик: у
    /// страны с вывозом казна полнеет, деньги расходятся по людям, те покупают больше,
    /// цены растут — и паритет тянет её валюту вниз. Выходит «голландская болезнь» вместо
    /// простого «профицит укрепляет», и знак у главного правила меняется на обратный.
    /// Автоматическому окну нужна стерилизация — чтобы интервенция не разгоняла цены, — а
    /// это отдельный шаг.
    /// </remarks>
    public Money SellCurrency(byte country, Money world)
    {
        var state = _world.CountryById(country);
        var have = state.State.Treasury.Reserves.Liquid;
        var take = world < have ? world : have;

        if (take.Raw <= 0 || !state.State.Treasury.Reserves.TrySpend(take)) return default;

        var local = InLocal(state, take);
        state.Bank.Emit(local, EmissionKind.ForCurrency);
        state.State.Treasury.Receive(local);
        _soldCurrency[country] = _soldCurrency.GetValueOrDefault(country) + local;

        return local;
    }

    /// <summary>Покупает валюту за местные деньги. Возвращает, сколько валюты куплено.</summary>
    public Money BuyCurrency(byte country, Money world)
    {
        var state = _world.CountryById(country);
        var local = InLocal(state, world);

        if (local.Raw <= 0 || !state.State.Treasury.TrySpend(local)) return default;

        state.Bank.Withdraw(local);
        state.State.Treasury.Reserves.Add(
            Reserves.Incoming(country, state.State.Custody, world));

        _boughtCurrency[country] = _boughtCurrency.GetValueOrDefault(country) + local;

        return world;
    }

    /// <summary>Сколько валюты страна сдала за тик, в своих деньгах.</summary>
    public Money SoldCurrencyOf(byte country) => _soldCurrency.GetValueOrDefault(country);

    /// <summary>Сколько валюты страна купила за тик, в своих деньгах.</summary>
    public Money BoughtCurrencyOf(byte country) => _boughtCurrency.GetValueOrDefault(country);

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

            // В мировую меру: складывать рубли с иенами и боливарами нельзя, а мировой
            // уровень цен — это сумма по всем. Без деления на курс в нём перевешивала
            // страна с самой дешёвой валютой, и курс тянулся неведомо куда.
            nominalWorld += InWorld(country, nominal);
            realWorld += InWorld(country, real);

            var level = PriceLevel.Of(nominal, real);
            _level[country.Id] = level;

            // Своих денег нет — нет и центробанка, тянуть уровень нечем.
            if (country.Bank.Start.Raw <= 0) continue;

            // Точка отсчёта — выпуск первого тика, а не паспортная мощность зданий. С
            // мощностью выходило сравнение разного: в первые дни заводы работают вполсилы,
            // отношение «было к стало» взлетало, и якорь гнал цены вверх, пока выпуск не
            // догонит. Отсюда и брался всплеск инфляции в первые секунды партии.
            if (!_baseReal.TryGetValue(country.Id, out var realBefore))
            {
                _baseReal[country.Id] = real;
                continue;
            }

            if (realBefore.Raw <= 0) continue;

            // Уровень не подталкивается на долю перекоса, а приравнивается деньгам:
            // покрытие двигает свои цены полным шагом, и подталкивание ему проигрывало.
            // Ограничена только скорость — не больше шага за тик, чтобы не прыгало.
            // Масса идёт за выпуском, и только напечатанное сверх этого двигает уровень.
            country.Bank.Follow(new Money((long)((Int128)country.Bank.Start.Raw * real.Raw / realBefore.Raw)));

            var want = PriceLevel.Target(country.Bank.Supply, country.Bank.Start, real, realBefore);
            var step = Math.Clamp(
                want,
                level * (100 - Prices.StepPercent) / 100,
                level * (100 + Prices.StepPercent) / 100);

            _levelPush[country.Id] = level > 0 ? (int)((long)(step - level) * 10_000 / level) : 0;

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

    /// <summary>Какие сутки партии идут. По ним считается срок отлучения от кредита.</summary>
    public int Day => _day;

    /// <summary>Сколько рабочих мест в услугах. Считается отдельно: настоящая занятость
    /// в услугах вдвое больше промышленной, и складывать их в один замер нечестно.</summary>
    public long ServiceJobs => _serviceJobs;

    /// <summary>Сколько рук занято на стройке.</summary>
    public long BuildJobs => _buildJobs;

    /// <summary>Во сколько раз цены страны ушли от начала партии, в долях
    /// <see cref="PriceLevel.Scale"/>. Считается на прошлом тике вместе с якорем.</summary>
    public int PriceLevelOf(byte country) => _level.GetValueOrDefault(country, PriceLevel.Scale);

    /// <summary>Загрузка предприятий страны в сотых долях от полной. Меньше единицы —
    /// значит рук не хватило и всё производство идёт вполсилы.</summary>
    public long LoadIn(byte country) => _hands.GetValueOrDefault(country, Load.Full);

    /// <summary>Что страна выпустила за тик по одному товару.</summary>
    public GoodAmount OutputOf(byte country, GoodType good) => _outputs.Get(country, good);

    /// <summary>Сколько страна заказала по одному товару — и заводы, и население.</summary>
    public GoodAmount InputOf(byte country, GoodType good) => _inputs.Get(country, good);

    /// <summary>Что население страны купило за тик.</summary>
    public GoodAmount BoughtOf(byte country, GoodType good) => _bought.Get(country, good);

    /// <summary>Сколько страна ввезла за тик по одному товару.</summary>
    public GoodAmount ImportedOf(byte country, GoodType good) => _imported.Get(country, good);

    /// <summary>Сколько страна вывезла за тик по одному товару.</summary>
    public GoodAmount ExportedOf(byte country, GoodType good) => _exported.Get(country, good);

    /// <summary>Чего не хватило по стране: заказали, а не досталось.</summary>
    public GoodAmount ShortOf(byte country, GoodType good) => _deficit.Get(country, good);

    /// <summary>Сколько рук занято на стройке в стране.</summary>
    public long BuildersIn(byte country) => _builders.GetValueOrDefault(country);

    /// <summary>Сколько денег скопилось на стройку и ещё не потрачено.</summary>
    /// <remarks>Копится, когда денег больше, чем материалов: купить нечего. Деньги при
    /// этом всё равно давят на цены — их считает денежная масса.</remarks>
    public Money InvestmentIn(byte country) => _investment.GetValueOrDefault(country);

    /// <summary>Вкладывает деньги казны в стройку и назначает, что строить.</summary>
    /// <remarks>Обычно страна сама выбирает, что выгоднее; заказ эту выборку обходит, но
    /// дальше идёт общим путём — те же материалы, те же руки, та же цена. Вкладывают
    /// деньгами, а не штуками: половина цены завода тоже вложение, она полежит в кошельке
    /// стройки, пока не наберётся на целый.</remarks>
    /// <returns>Ложь, если в казне столько нет.</returns>
    public bool Invest(byte country, BuildingType type, Money amount)
    {
        if (amount.Raw <= 0) return false;
        if (!_world.CountryById(country).State.Treasury.TrySpend(amount)) return false;

        var same = _ordered.TryGetValue(country, out var order) && order.Type == type;

        _investment[country] = _investment.GetValueOrDefault(country) + amount;
        _ordered[country] = (type, same ? order.Left + amount : amount);

        return true;
    }

    /// <summary>С какой скоростью цена товара идёт сейчас, в сотых долях процента за день.
    /// Плюс — дорожает.</summary>
    /// <remarks>То же правило, что и в <see cref="Prices.MoveFromCover"/>, только наружу и
    /// заранее: по нему видно, что будет, а не что уже случилось.</remarks>
    public int PaceOf(byte country, GoodType good) =>
        Pace(_inputs.Get(country, good).Raw, _world.CountryById(country).State.Stock.Of(good).Raw);

    /// <summary>На сколько ускорится подорожание, если добавить к спросу столько товара.</summary>
    public int PriceLift(byte country, GoodType good, GoodAmount extra)
    {
        var stock = _world.CountryById(country).State.Stock.Of(good).Raw;
        var demand = _inputs.Get(country, good).Raw;

        return Pace(demand + extra.Raw, stock) - Pace(demand, stock);
    }

    /// <summary>Какой материал заказ ударит сильнее всего и насколько.</summary>
    public (GoodType? Good, int Lift) OrderLift(byte country, BuildingType type, int count)
    {
        GoodType? worst = null;
        var most = 0;

        foreach (var (good, amount) in _world.Buildings[type].BuildCost)
        {
            var lift = PriceLift(country, good, new GoodAmount(amount.Raw * count));
            if (lift <= most) continue;

            most = lift;
            worst = good;
        }

        return (worst, most);
    }

    /// <summary>Куда денежный якорь тянет все цены страны разом, в сотых долях процента
    /// за день. Это общий сдвиг уровня, а не движение отдельного товара.</summary>
    public int LevelPushOf(byte country) => _levelPush.GetValueOrDefault(country);

    private static int Pace(long demand, long stock)
    {
        var target = (long)Prices.TargetCoverDays * demand;
        var over = target + stock;

        return over == 0 ? 0 : (int)((Int128)Prices.StepPercent * 100 * (target - stock) / over);
    }

    /// <summary>Сколько таких зданий можно заказать, не сдвинув цены на материалы, и во
    /// что это упирается.</summary>
    /// <remarks>
    /// Цена товара ходит от покрытия: пока на складе лежит запас на <see
    /// cref="Prices.TargetCoverDays"/> дней спроса, она стоит на месте. Стройка добавляет
    /// спрос наравне с заводами, поэтому запас для неё — это то, что лежит сверх нормы.
    ///
    /// Считается по вчерашнему спросу: сегодняшний ещё не собран, а вчерашний уже включает
    /// и стройку, и заводы, и население.
    /// </remarks>
    public (int Count, GoodType? Tight) SafeToBuild(byte country, BuildingType type)
    {
        var stock = _world.CountryById(country).State.Stock;
        var most = int.MaxValue;
        GoodType? tight = null;

        foreach (var (good, amount) in _world.Buildings[type].BuildCost)
        {
            if (amount.Raw <= 0) continue;

            var norm = _inputs.Get(country, good).Raw * Prices.TargetCoverDays;
            var spare = stock.Of(good).Raw - norm;
            var fits = spare <= 0 ? 0 : (int)Math.Min(int.MaxValue, spare / amount.Raw);

            if (fits >= most) continue;

            most = fits;
            tight = good;
        }

        return (most == int.MaxValue ? 0 : most, tight);
    }

    /// <summary>Мировая цена товара в деньгах страны.</summary>
    /// <remarks>Мировая цена живёт в мировой мере, местная — в своих деньгах. Сравнивать их
    /// напрямую нельзя: это рубли против непонятно чего.</remarks>
    public Money WorldPriceIn(byte country, GoodType good) =>
        new((long)((Int128)_world.Market.Prices.Of(good).Raw
            * _world.CountryById(country).ExchangeRate.Raw / Money.Scale));

    /// <summary>Наценка на ввоз товара в сотых долях процента: дорога и пошлины по пути.</summary>
    public int MarkupOn(byte country, GoodType good) =>
        _world.TradeCosts.ImportMarkup(country, good, _world.Routes.CostTo(country));

    /// <summary>Сколько таких зданий страна поднимет за один тик по нынешнему складу.</summary>
    /// <remarks>Хотя бы одно можно всегда: иначе дорогое здание в маленькой стране не
    /// построилось бы никогда, сколько ни копи.</remarks>
    public int CanRaisePerTick(byte country, BuildingType type)
    {
        var stock = _world.CountryById(country).State.Stock;
        var most = MaxBuildsPerTick;

        foreach (var (good, amount) in _world.Buildings[type].BuildCost)
        {
            if (amount.Raw <= 0) continue;

            most = (int)Math.Min(most, stock.Of(good).Raw * BiteOfStock / 100 / amount.Raw);
        }

        return Math.Max(most, 1);
    }

    /// <summary>Во что обойдётся работа строителей на одной постройке.</summary>
    public Money BuildWageOf(byte country, BuildingType type) =>
        WagesFor(_world.CountryById(country), _world.Buildings[type]);

    /// <summary>Что заказано и сколько вложенного ещё не потрачено.</summary>
    public (BuildingType Type, Money Left)? OrderOf(byte country) =>
        _ordered.TryGetValue(country, out var order) ? order : null;

    public void Cancel(byte country) => _ordered.Remove(country);

    /// <summary>Что строится прямо сейчас: тип, регион и сколько штук.</summary>
    public (BuildingType Type, Region Where, int Count)? PlanOf(byte country) =>
        _plan.TryGetValue(country, out var plan) ? plan : null;

    /// <summary>Сколько зданий типа работало на прошлом тике. Меньше построенных —
    /// значит не хватило сырья или рук.</summary>
    public int WorkingOf(byte country, BuildingType type) => _working.Get(country, type);

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

            // Пошлина уже сидит в цене ввоза — платит её покупатель на границе. Досюда
            // она доходила только как удорожание товара, а деньги не получал никто.
            var duty = ImportsOf(country.Id);
            if (duty.Raw <= 0) continue;

            country.Budget.Collect(
                TaxKind.Tariff,
                InLocal(country, new Money(duty.Raw * _world.TradeCosts.TariffOf(country.Id) / 10_000)));
        }
    }

    /// <summary>Во что мир оценивает свои деньги, в сотых процента.</summary>
    /// <remarks>
    /// Средняя ключевая ставка эмитентов резервных валют, взвешенная по тому, сколько их
    /// валюты лежит в мировых резервах. Это и есть цена мировых денег: доллар стоит
    /// столько, сколько стоит доллар, кто бы его ни давал взаймы.
    ///
    /// Раньше плавающий заём шёл за ключевой ставкой кредитора. Дешёвые кредиторы —
    /// Америка с четвертью процента, Германия с минусовой — раздают свободные резервы
    /// первыми, и дальше заёмщику оставались те, у кого дома тридцать пять процентов.
    /// Оттого Россия занимала под тридцать шесть при нулевой долговой нагрузке, а за пять
    /// лет отказывались платить сто шестьдесят пять стран из двухсот.
    /// </remarks>
    public int WorldRate { get; private set; } = CreditMarket.BaseRate;

    private void CountWorldRate()
    {
        Int128 weighted = 0;
        Int128 total = 0;

        foreach (var country in _world.Countries)
        {
            foreach (var held in country.State.Treasury.Reserves.Held)
            {
                // Ничейный эмитент — это мировая единица, а не страна: ключевой ставки
                // у неё нет, и в среднюю она не идёт.
                if (held.Kind != ReserveKind.ForeignCurrency || held.Amount.Raw <= 0) continue;
                if (held.Issuer == WorldMarket.WorldIssuer) continue;

                weighted += (Int128)held.Amount.Raw * _world.CountryById(held.Issuer).KeyRate;
                total += held.Amount.Raw;
            }
        }

        WorldRate = total > 0 ? (int)(weighted / total) : CreditMarket.BaseRate;
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
        CountWorldRate();

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

            // За страну игрока заявку подаёт он сам: иначе долг рос бы сам собой, а
            // «попросить в долг» было бы нечего.
            if (country.Id == HandsOff) want = default;

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

        _world.Credit.Settle(CollectionsMarshal.AsSpan(_credit), _world.Relations.Between, WorldRate);

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

                var due = loan.InterestPerTick(WorldRate);
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
            // Строит та компания, у которой сейчас больше всех денег: копили — значит на
            // что-то копили. Очередь меняется сама собой, потому что потратившая уходит
            // вниз списка.
            var builder = Richest(country.Id);
            var purse = builder?.Cash ?? _investment.GetValueOrDefault(country.Id);

            // Заказ игрока идёт мимо ниш: государство строит что велено. Сама страна не
            // решает ничего — за неё решают компании, каждая в своём деле.
            var best = Ordered(country) ?? Chosen(country, builder, purse);

            if (best is null) continue;

            _builder[country.Id] = builder;

            var info = _world.Buildings[best.Value.Type];
            var price = Construction.CostOf(info.BuildCost, country.State.Prices) + WagesFor(country, info);
            if (price.Raw <= 0) continue;

            // За тик строится не больше потолка: не найдя материалов, страна заявляла бы
            // спрос, которого мир не выдержит. Режется именно число, а не кошелёк —
            // раньше лишние деньги пропадали, и вложенное игроком исчезало бы, не дойдя
            // до стройки.
            var count = (int)Math.Min(purse.Raw / price.Raw, MaxBuildsPerTick);

            // Заявлять больше, чем со склада откусишь, нельзя: страна просила материалы
            // на пятьсот зданий, а поднимала пять, и выдуманный спрос гнал цену вверх.
            count = Math.Min(count, CanRaisePerTick(country.Id, best.Value.Type));

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
            _added[country.Id] = made;
            if (made.Raw <= 0) continue;

            // Платят с проданного, а не со всего выпуска. То, что легло на склад, денег в
            // кассу не принесло, и зарплату из него взять неоткуда — оттого касса и уходила
            // в минус, а станок печатал каждый день. В первый тик продавать ещё нечего:
            // считаем по выпуску, дальше по вчерашней выручке.
            var basis = _demand.TryGetValue(country.Id, out var sold) ? sold : made;
            var owed = new Money(basis.Raw * country.LabourShare / 100);
            var paid = owed;
            if (!country.State.Treasury.TrySpend(owed))
            {
                // Не хватило — печатают недостающее. Не смогли и этого — платят сколько есть.
                Print(country, owed);
                if (!country.State.Treasury.TrySpend(owed)) paid = PayWhatIsLeft(country);
            }

            // Труд стоит работодателю всё, что он на него потратил: и зарплату, и взносы,
            // и удержанный подоходный. Делится эта сумма, а не прибавляется сверху — иначе
            // взносы упирались бы в пустую кассу и не собирались вовсе.
            var dues = TaxCode.Take(paid, country.Taxes.Payroll);
            var onHand = paid - dues;
            var income = TaxCode.Take(onHand, country.Taxes.Income);

            country.Budget.Collect(TaxKind.Payroll, dues);
            country.Budget.Collect(TaxKind.Income, income);

            country.Households.Earn(onHand - income);
            _wages[country.Id] = paid;

            country.Payroll = paid;
        }
    }

    /// <summary>Что страна и правда продала за тик за свои деньги: населению и на
    /// стройку. От этого числа и считается завтрашняя зарплата.</summary>
    /// <remarks>
    /// Выпуск, ушедший на склад, сюда не входит: он ещё никем не оплачен, и платить с него
    /// зарплату — то же самое, что печатать. Именно этим касса и уходила в ноль.
    ///
    /// Вывоза здесь тоже нет, хотя по счетам он часть ВВП. За него платят чужой валютой, и
    /// она ложится в резервы, а не в кассу: зарплату из неё не выдать, пока её не поменяли.
    /// Считать её здесь — значит обещать зарплату деньгами, которых в стране нет; ровно на
    /// этом вывозящие страны и печатали. Дыра остаётся, и закроет её обмен выручки.
    /// </remarks>
    private void NoteDemand()
    {
        foreach (var country in _world.Countries)
        {
            _demand[country.Id] = _sales.GetValueOrDefault(country.Id);
        }
    }

    /// <summary>Оплаченный спрос прошлого тика, по странам.</summary>
    private readonly Dictionary<byte, Money> _demand = new();

    /// <summary>Сколько страна продала за прошлый тик населению, стройке и за границу.</summary>
    public Money DemandOf(byte country) => _demand.GetValueOrDefault(country);

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
                    // Налоги сидят в цене: человек платит их, сам того не замечая, а
                    // продавцу достаётся меньше. Оттого высокий НДС и бьёт по спросу — на
                    // те же деньги покупают меньше.
                    var vat = TaxCode.Take(cost, country.Taxes.Vat);
                    var excise = Excisable(good) ? TaxCode.Take(cost, country.Taxes.Excise) : default;
                    var full = cost + vat + excise;

                    var paid = country.Households.SpendUpTo(full);
                    if (paid < full) bought = new GoodAmount((long)((Int128)offered.Raw * paid.Raw / full.Raw));

                    // Собранное делится в той же пропорции: заплатил половину — половину
                    // и налогов.
                    var share = full.Raw > 0 ? (Int128)paid.Raw * 10_000 / full.Raw : 0;
                    var gotVat = new Money((long)((Int128)vat.Raw * share / 10_000));
                    var gotExcise = new Money((long)((Int128)excise.Raw * share / 10_000));

                    country.Budget.Collect(TaxKind.Vat, gotVat);
                    country.Budget.Collect(TaxKind.Excise, gotExcise);

                    var toSeller = paid - gotVat - gotExcise;
                    state.Treasury.Receive(toSeller);
                    _sales[country.Id] = _sales.GetValueOrDefault(country.Id) + toSeller;
                }

                state.Stock.TakeUpTo(good, bought);
                _bought.Add(country.Id, good, bought);

                var missing = wanted - bought;
                if (missing.Raw > 0) _deficit.Add(country.Id, good, missing);
            }
        }
    }

    /// <summary>Сколько товара просит стройка за тик.</summary>
    public GoodAmount BuildWantOf(byte country, GoodType good) => _build.Get(country, good);

    /// <summary>Сколько товара просит население за тик.</summary>
    public GoodAmount PeopleWantOf(byte country, GoodType good) => _peopleWants.Get(country, good);

    /// <summary>Выручка с населения за прошедший тик.</summary>
    public Money SalesOf(byte country) => _sales.GetValueOrDefault(country);

    /// <summary>Налог на добычу и расходы бюджета.</summary>
    /// <remarks>
    /// Добыча облагается отдельно от прибыли: недра принадлежат стране, а не тому, кто
    /// поставил над ними вышку. У России это треть бюджета, у Саудовской Аравии почти
    /// весь, и без этого налога сырьевая страна в модели оставалась без дохода вовсе —
    /// руду и нефть население не покупает, а значит и НДС с них не берётся.
    ///
    /// Тратится собранное тем же тиком: государство содержит бюджетников и платит
    /// пособия, а это те же люди, что покупают еду. Копить бюджету незачем — деньги,
    /// лежащие в нём мёртвым грузом, выпадают из оборота ровно так же, как выпадали из
    /// казны до того, как круг замкнули.
    /// </remarks>
    private void Tax()
    {
        foreach (var country in _world.Countries)
        {
            var rate = country.Taxes.Extraction;
            if (rate > 0)
            {
                var dug = default(Money);
                foreach (var good in AllGoods)
                {
                    if (!Dug(good)) continue;

                    dug += country.State.Prices.CostOf(good, _outputs.Get(country.Id, good));
                }

                var tax = TaxCode.Take(dug, rate);
                if (tax.Raw > 0 && country.State.Treasury.TrySpend(tax))
                {
                    country.Budget.Collect(TaxKind.Extraction, tax);
                }
            }

            Spend(country);
        }
    }

    /// <summary>Что берут из земли. С этого и платят за недра.</summary>
    private static bool Dug(GoodType good) => good is
        GoodType.Coal or GoodType.Oil or GoodType.Gas or GoodType.IronOre or GoodType.CopperOre
        or GoodType.Bauxite or GoodType.Uranium or GoodType.RareEarth or GoodType.Timber
        or GoodType.Agriculture;

    /// <summary>Государство тратит собранное: бюджетникам и пособиями.</summary>
    private void Spend(Country country) => Arm(country);

    /// <summary>Что осталось в бюджете после закупок, уходит бюджетникам и на пособия.</summary>
    /// <remarks>Идёт последним: сперва государство покупает оружие и строит дороги, и
    /// только не потраченное раздаёт людям. Раньше раздача стояла первой, и на дороги не
    /// оставалось ни копейки ни у одной страны.</remarks>
    private void Dole()
    {
        foreach (var country in _world.Countries)
        {
            var paid = country.Budget.SpendUpTo(country.Budget.Balance);
            if (paid.Raw <= 0) continue;

            country.Households.Earn(paid);
        }
    }

    /// <summary>Покупает заказанное и говорит, на сколько купило.</summary>
    /// <remarks>Одна и та же работа у армии, жилья и дорог: взять со склада сколько есть,
    /// заплатить сколько можешь, и деньги отдать тому, кто товар сделал.</remarks>
    private GoodAmount Buy(Country country, GoodType good, GoodAmount wanted, Func<Money, Money> purse)
    {
        if (wanted.Raw <= 0) return default;

        var onShelf = country.State.Stock.Of(good);
        var take = wanted < onShelf ? wanted : onShelf;
        if (take.Raw <= 0) return default;

        var cost = country.State.Prices.CostOf(good, take);
        var paid = purse(cost);
        if (paid.Raw <= 0) return default;

        // Заплатили меньше — и взяли меньше: в долг у завода никто не берёт.
        if (paid < cost) take = new GoodAmount((long)((Int128)take.Raw * paid.Raw / cost.Raw));
        if (take.Raw <= 0) return default;

        country.State.Stock.TakeUpTo(good, take);
        country.State.Treasury.Receive(paid);

        _bought.Add(country.Id, good, take);
        _sales[country.Id] = _sales.GetValueOrDefault(country.Id) + paid;

        return take;
    }

    /// <summary>Люди строят себе жильё, государство — дороги. И то и другое ветшает.</summary>
    /// <remarks>
    /// Без этого у стройматериалов и леса был один покупатель — стройка заводов, и цена их
    /// лежала на полу коридора у сорока пяти стран из двухсот. В жизни главный потребитель
    /// цемента и доски это жильё, а второй — казённое строительство.
    /// </remarks>
    private void BuildEstate()
    {
        foreach (var country in _world.Countries)
        {
            country.Estate.Wear(DaysInYear);

            var built = default(GoodAmount);
            var spent = default(Money);

            foreach (var good in EstateGoods)
            {
                var before = _sales.GetValueOrDefault(country.Id);
                var take = Buy(country, good, _houseWants.Get(country.Id, good),
                    cost => country.Households.SpendUpTo(cost));

                built += take;
                spent += _sales.GetValueOrDefault(country.Id) - before;
            }

            country.Estate.Settle(built);
            _housing[country.Id] = spent;

            built = default;
            spent = default;

            foreach (var good in EstateGoods)
            {
                var before = _sales.GetValueOrDefault(country.Id);
                var take = Buy(country, good, _roadWants.Get(country.Id, good),
                    cost => country.Budget.SpendUpTo(cost));

                built += take;
                spent += _sales.GetValueOrDefault(country.Id) - before;
            }

            country.Estate.Pave(built);
            _roads[country.Id] = spent;
        }
    }

    /// <summary>Из чего строят жильё и дороги.</summary>
    private static readonly GoodType[] EstateGoods =
        [GoodType.Materials, GoodType.Timber, GoodType.Metals];

    /// <summary>Государство покупает оружие и списывает отслужившее.</summary>
    /// <remarks>
    /// Платит бюджет, а деньги достаются тому, кто оружие сделал, — как и за всякую
    /// покупку. Оттого госзаказ и держит оборонную промышленность: без него военные
    /// заводы работали в никуда.
    ///
    /// Хочет страна столько, сколько заказала в <see cref="OrderArms"/>, а купит меньше:
    /// сколько лежит на складе и сколько есть в бюджете. Бюджет здесь и становится
    /// рычагом — поднял налоги, смог вооружаться.
    /// </remarks>
    private void Arm(Country country)
    {
        var spent = default(Money);

        foreach (var good in Arms)
        {
            // Отслужившее списывается всегда, куплено новое или нет.
            country.Army.Wear(good, DaysInYear);

            var before = _sales.GetValueOrDefault(country.Id);
            var take = Buy(country, good, _armsWants.Get(country.Id, good),
                cost => country.Budget.SpendUpTo(cost));

            if (take.Raw <= 0) continue;

            country.Army.Add(good, take);
            spent += _sales.GetValueOrDefault(country.Id) - before;
        }

        if (spent.Raw <= 0) return;

        _armsBought[country.Id] = spent;
    }

    /// <summary>С чего берут акциз: с того, что вредно, дорого возить или незаменимо.
    /// В жизни это топливо, табак и алкоголь; у нас табака с алкоголем нет, и их место
    /// занимают обычные товары.</summary>
    private static bool Excisable(GoodType good) =>
        good is GoodType.Fuel or GoodType.ConsumerGoods;

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

    /// <summary>Подтягивает цены к мировым: свободно возимый товар не может стоить намного
    /// дороже привозного.</summary>
    /// <remarks>Идёт после денежного якоря, и это важно. Якорь двигает все цены страны
    /// разом, чтобы уровень сошёлся с деньгами, и он способен вытолкнуть отдельный товар
    /// сколь угодно высоко над мировой ценой. Последнее слово должно оставаться за
    /// перевозчиком: держать товар дороже привозного никакая страна не может.</remarks>
    private void PullPrices()
    {
        foreach (var country in _world.Countries)
        {
            foreach (var good in AllGoods)
            {
                // Равняться можно только на то, чем и правда торгуют: держит закон
                // одной цены перевозчик, а не число.
                if (good == GoodType.Services || !_dealt[(int)good]) continue;

                country.State.Prices.PullToWorld(
                    good,
                    WorldPriceIn(country.Id, good),
                    _world.TradeCosts.ImportMarkup(country.Id, good, _world.Routes.CostTo(country.Id)));
            }
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

    /// <summary>Прибыль сверх дневного запаса уходит владельцам — тем же людям.</summary>
    /// <remarks>
    /// Без этого шага деньги утекали из оборота навсегда: казна собирала с населения
    /// больше, чем платила зарплатами, и разница копилась мёртвым грузом. За пять лет у
    /// России в казне оседало почти два миллиарда, а у населения оставалось две тысячных
    /// от начального — покупать было уже не на что.
    ///
    /// Владелец предприятия — такой же человек, и его прибыль идёт в те же кошельки.
    /// Отдельного счёта у него не будет, пока не появятся компании.
    /// </remarks>
    private void PayProfits()
    {
        foreach (var country in _world.Countries)
        {
            // Делится заработанное за сутки, а не весь остаток казны: остаток — это
            // оборотные деньги страны, и раздать их в первый же тик значит пустить
            // стартовый капитал на потребление.
            var free = _sales.GetValueOrDefault(country.Id) - _wages.GetValueOrDefault(country.Id);
            var balance = country.State.Treasury.Balance;

            if (free > balance) free = balance;
            if (free.Raw <= 0) continue;

            if (!country.State.Treasury.TrySpend(free)) continue;

            _profits[country.Id] = free;
            Divide(country, free);
        }
    }

    /// <summary>Что за тик ушло владельцам.</summary>
    public Money ProfitOf(byte country) => _profits.GetValueOrDefault(country);

    /// <summary>Делит заработанное между компаниями и владельцами.</summary>
    /// <remarks>
    /// Доля компании — её доля в числе зданий страны: чем больше у неё заводов, тем больше
    /// она и заработала. Считать по выпуску точнее, но это перебор всех зданий всех
    /// компаний каждый тик, а разница невелика — здания одного сектора похожи.
    ///
    /// Из своей доли компания оставляет четверть на развитие, остальное отдаёт владельцам.
    /// Владелец — население: акций и биржи пока нет, и делить их не с кем.
    /// </remarks>
    private void Divide(Country country, Money earned)
    {
        var companies = _world.CompaniesOf(country.Id);
        if (companies.Count == 0)
        {
            country.Households.Earn(earned);

            return;
        }

        var total = 0L;
        foreach (var company in companies)
        {
            if (company.Alive) total += company.Size;
        }

        if (total <= 0)
        {
            country.Households.Earn(earned);

            return;
        }

        var kept = default(Money);
        foreach (var company in companies)
        {
            if (!company.Alive) continue;

            var share = new Money((long)((Int128)earned.Raw * company.Size / total));

            // Сперва налог на прибыль, и только с остатка компания копит на стройку.
            var tax = TaxCode.Take(share, country.Taxes.Profit);
            country.Budget.Collect(TaxKind.Profit, tax);
            kept += tax;

            var save = new Money((share - tax).Raw * Construction.InvestmentShare / 100);
            if (save.Raw <= 0) continue;

            company.Earn(save);
            kept += save;
        }

        _saved[country.Id] = kept;
        country.Households.Earn(earned - kept);
    }

    /// <summary>Банковский день: вклады, кредиты компаниям, проценты и разорения.</summary>
    /// <remarks>
    /// Компания берёт в долг, когда своих денег на стройку не хватает, а дело выгодно.
    /// Отдаёт по графику из выручки; нечем — проценты уходят в тело, долг растёт сам себя,
    /// и рано или поздно она разоряется. Её здания при этом не пропадают: их подбирает
    /// сосед по отрасли, как и бывает при банкротстве.
    ///
    /// Банк не печатает денег: он раздаёт вклады населения, оставляя норму резерва. Оттого
    /// в бедной стране и занять не у кого — вкладов нет.
    /// </remarks>
    private void Banking()
    {
        foreach (var country in _world.Countries)
        {
            var bank = country.Banks;

            // Во вкладах люди держат долю всего, что у них есть, — и доносят, и забирают.
            // Раньше только доносили: каждый день пятую часть остатка, и вклады росли сами
            // себя до величин, которых в хозяйстве нет.
            var wealth = country.Households.Savings + bank.Deposits;
            var want = new Money(wealth.Raw * DepositShare / 100);

            if (want > bank.Deposits)
            {
                bank.Take(country.Households.SpendUpTo(want - bank.Deposits));
            }
            else if (bank.Deposits > want)
            {
                // Отдать можно только то, что не роздано: остальное лежит в чужих заводах.
                var loose = bank.Deposits > bank.Lent ? bank.Deposits - bank.Lent : default;
                var back = bank.Deposits - want;

                country.Households.Earn(bank.Give(back < loose ? back : loose));
            }

            country.Households.Earn(bank.PayOut());

            var rate = Math.Max(CreditMarket.BaseRate, country.KeyRate + Bank.Margin);

            foreach (var company in _world.CompaniesOf(country.Id))
            {
                if (!company.Alive) continue;

                Service(country, company, bank, rate);
            }
        }
    }

    /// <summary>Какую долю сбережений люди держат в банке, в процентах.</summary>
    private const int DepositShare = 20;

    /// <summary>Во сколько раз долг должен превысить годовую выручку, чтобы компания
    /// считалась безнадёжной.</summary>
    private const int BrokeAt = 5;

    private void Service(Country country, Company company, Bank bank, int rate)
    {
        // Проценты за сутки по годовой ставке.
        var due = new Money(company.Debt.Raw * rate / (100 * 100 * DaysInYear));
        var paid = company.Repay(due);

        if (paid < due) company.Capitalise(due - paid);

        // Тело гасится по графику, как и внешний долг страны.
        var body = new Money(company.Debt.Raw / (Bank.LoanYears * DaysInYear));
        var back = company.Repay(body);

        bank.Returned(back, paid);

        // Выручку компании считают не каждый день: это перебор всех её зданий, а банк
        // смотрит на дело раз в декаду, как и сама она решает, что строить.
        if (_day % ChooseEvery != company.Id % ChooseEvery) return;

        var yearly = Worth(country, company);

        // Сперва распродажа, и только если она не спасла — разорение. Так и делают: завод
        // продают соседу, пока он ещё чего-то стоит, а не после суда.
        if (yearly.Raw > 0 && company.Debt.Raw > yearly.Raw * SellAt
            && SellOff(country, company, bank, yearly))
        {
            yearly = Worth(country, company);
        }

        if (company.Debt.Raw > yearly.Raw * BrokeAt && yearly.Raw > 0)
        {
            Ruin(country, company, bank);

            return;
        }

        // Скопила на год вперёд и долгов почти нет — берётся за соседний передел. Это и
        // есть рост конгломерата: вверх по цепочке, а не в самую прибыльную отрасль света.
        if (company.Focus.Count < WidestFocus && company.Cash > yearly
            && company.Debt.Raw * 2 < yearly.Raw)
        {
            company.Expand(Founders.Next(company.Focus[^1]));
        }

        // Занимает, если на стройку не хватает своего, а дело того стоит.
        if (!_choice.TryGetValue(company.Id, out var plan)) return;

        var price = Construction.CostOf(_world.Buildings[plan.Type].BuildCost, country.State.Prices);
        if (price.Raw <= 0 || company.Cash >= price) return;

        var want = price - company.Cash;
        if (want > bank.Free) want = bank.Free;

        // Больше пяти годовых выручек никто не даст: это и есть черта безнадёжности.
        var room = new Money(yearly.Raw * BrokeAt) - company.Debt;
        if (want > room) want = room;

        if (want.Raw > 0 && bank.Lend(want)) company.Borrow(want);
    }

    /// <summary>Больше скольких отраслей компания не берёт. Даже у настоящих
    /// конгломератов дел наперечёт, а не по всему хозяйству.</summary>
    private const int WidestFocus = 3;

    /// <summary>Во сколько раз долг должен превысить годовую выручку, чтобы компания
    /// начала распродавать дело.</summary>
    private const int SellAt = 3;

    /// <summary>Почём уходит чужое здание, в процентах от стоимости постройки. Бывшее в
    /// работе дешевле нового, а продают его в спешке и с долгом на шее.</summary>
    private const int UsedPrice = 70;

    /// <summary>Продаёт часть дела соседу по отрасли и гасит вырученным долг.</summary>
    private bool SellOff(Country country, Company seller, Bank bank, Money yearly)
    {
        var keep = new Money(yearly.Raw * SellAt);
        var any = false;

        foreach (var ((region, type), count) in seller.Buildings.ToArray())
        {
            if (seller.Debt <= keep) break;

            var full = Construction.CostOf(_world.Buildings[type].BuildCost, country.State.Prices);
            var price = new Money(full.Raw * UsedPrice / 100);
            if (price.Raw <= 0) continue;

            var buyer = BuyerFor(country, seller, _world.Buildings[type].Sector, price);
            if (buyer is null) continue;

            // Продаёт не больше, чем нужно закрыть долг, и не больше, чем у покупателя денег.
            var need = (int)((seller.Debt - keep).Raw / price.Raw) + 1;
            var sold = Math.Min(count, Math.Min(need, (int)(buyer.Cash.Raw / price.Raw)));
            if (sold <= 0) continue;

            var paid = new Money(price.Raw * sold);
            if (!buyer.TrySpend(paid)) continue;

            seller.Remove(region, type, sold);
            buyer.Add(region, type, sold);
            seller.Earn(paid);
            bank.Returned(seller.Repay(paid), default);

            _sold[country.Id] = _sold.GetValueOrDefault(country.Id) + sold;
            any = true;
        }

        return any;
    }

    /// <summary>Кто в стране возьмётся за такое здание и у кого хватит денег.</summary>
    private Company? BuyerFor(Country country, Company seller, Sector sector, Money price)
    {
        Company? best = null;

        foreach (var company in _world.CompaniesOf(country.Id))
        {
            if (!company.Alive || company.Id == seller.Id) continue;
            if (!company.Works(sector) || company.Cash < price) continue;
            if (best is null || company.Cash > best.Cash) best = company;
        }

        return best;
    }

    /// <summary>Сколько зданий сменило хозяина через продажу за партию.</summary>
    public int SoldIn(byte country) => _sold.GetValueOrDefault(country);

    private readonly Dictionary<byte, int> _sold = new();

    /// <summary>Во что обходится годовой выпуск компании в её же ценах.</summary>
    private Money Worth(Country country, Company company)
    {
        var daily = default(Money);

        foreach (var ((_, type), count) in company.Buildings)
        {
            foreach (var (good, amount) in _world.Buildings[type].Outputs)
            {
                daily += new Money(
                    (long)((Int128)country.State.Prices.Of(good).Raw * amount.Raw * count / GoodAmount.Scale));
            }
        }

        return new Money(daily.Raw * DaysInYear);
    }

    /// <summary>Компания разорилась: долг списан, здания достаются соседу по отрасли.</summary>
    private void Ruin(Country country, Company broke, Bank bank)
    {
        bank.WriteOff(broke.Debt);

        Company? heir = null;
        foreach (var company in _world.CompaniesOf(country.Id))
        {
            if (!company.Alive || company.Id == broke.Id) continue;
            if (!company.Works(broke.Focus[0])) continue;
            if (heir is null || company.Cash > heir.Cash) heir = company;
        }

        if (heir is not null)
        {
            foreach (var ((region, type), count) in broke.Buildings) heir.Add(region, type, count);
        }

        broke.Break(_day);
        _ruined[country.Id] = _ruined.GetValueOrDefault(country.Id) + 1;
    }

    /// <summary>Сколько компаний разорилось в стране за партию.</summary>
    public int RuinedIn(byte country) => _ruined.GetValueOrDefault(country);

    private readonly Dictionary<byte, int> _ruined = new();

    /// <summary>Что компания решила строить. Решение держится декаду.</summary>
    /// <remarks>
    /// Выбор перебирает все типы зданий и все области страны, и делать это каждые сутки —
    /// и дорого, и бессмысленно: стройку не затевают заново каждое утро. С ежедневным
    /// перебором тик стоил пятьдесят пять миллисекунд против восемнадцати.
    /// </remarks>
    private (BuildingType Type, Region Where)? Chosen(Country country, Company? builder, Money purse)
    {
        // Компаний нет вовсе — строит государство и берётся за что угодно. Так живут
        // тестовые миры, и так же будет, если все компании в стране разорятся.
        if (builder is null)
        {
            return country.Id == HandsOff ? null : BestBuild(country, purse);
        }

        if (_choice.TryGetValue(builder.Id, out var held) && held.Until > _day) return (held.Type, held.Where);

        var best = BestBuild(country, purse, builder.Focus);
        if (best is null) return null;

        _choice[builder.Id] = (best.Value.Type, best.Value.Where, _day + ChooseEvery);

        return best;
    }

    /// <summary>Сколько суток держится решение о стройке.</summary>
    private const int ChooseEvery = 10;

    private readonly Dictionary<int, (BuildingType Type, Region Where, int Until)> _choice = new();

    /// <summary>Самая богатая компания страны. Она и строит: копила — значит на что-то.</summary>
    private Company? Richest(byte country)
    {
        Company? best = null;
        foreach (var company in _world.CompaniesOf(country))
        {
            if (!company.Alive) continue;
            if (best is null || company.Cash > best.Cash) best = company;
        }

        return best;
    }

    /// <summary>Кто строит в стране на этом тике. Пусто — никто.</summary>
    public Company? BuilderIn(byte country) => _builder.GetValueOrDefault(country);

    /// <summary>Что за тик отложено из казны на стройку.</summary>
    public Money SavedOf(byte country) => _saved.GetValueOrDefault(country);

    /// <summary>Предложение занять: кто даёт, сколько и под какой процент.</summary>
    /// <param name="Rate">Годовая ставка в сотых долях процента.</param>
    public readonly record struct LoanOffer(byte Lender, Money Amount, int Rate);

    /// <summary>Кто и на каких условиях даст стране в долг прямо сейчас.</summary>
    /// <remarks>
    /// Считается ровно тем же правилом, что и сам аукцион в <see cref="CreditMarket"/>:
    /// показанная ставка обязана совпасть с той, по которой заём и возьмут. Свободные
    /// деньги кредитора — это его резервы сверх сорокадневного запаса на ввоз.
    /// </remarks>
    public List<LoanOffer> OffersFor(byte country, Money want)
    {
        var borrower = _world.CountryById(country);
        var premium = CreditMarket.PremiumFor(
            borrower.State.Treasury.Debt.BurdenToExports(DebtCapacity(borrower)), LockedOut(borrower));

        var offers = new List<LoanOffer>();

        foreach (var lender in _world.Countries)
        {
            if (lender.Id == country) continue;

            var free = FreeToLend(lender);
            if (free.Raw <= 0) continue;

            var politics = CreditMarket.PoliticsOn(_world.Relations.Between(lender.Id, country));
            if (politics is null) continue;

            var rate = Math.Max(
                CreditMarket.BaseRate,
                CreditMarket.BaseRate + lender.KeyRate + premium + politics.Value);

            rate = (rate + CreditMarket.RateStep - 1) / CreditMarket.RateStep * CreditMarket.RateStep;
            if (rate > CreditMarket.Ceiling) continue;

            offers.Add(new LoanOffer(lender.Id, free < want ? free : want, rate));
        }

        offers.Sort((a, b) => a.Rate != b.Rate ? a.Rate - b.Rate : b.Amount.Raw.CompareTo(a.Amount.Raw));

        return offers;
    }

    /// <summary>Берёт заём у названного кредитора. Возвращает, сколько удалось занять.</summary>
    public Money TakeLoan(byte country, byte lender, Money want)
    {
        var offer = OffersFor(country, want).FirstOrDefault(one => one.Lender == lender);
        if (offer.Amount.Raw <= 0) return default;

        var taken = offer.Amount < want ? offer.Amount : want;
        var from = _world.CountryById(lender).State;
        var to = _world.CountryById(country).State;

        if (!from.Treasury.Reserves.TrySpend(taken)) return default;

        to.Treasury.Reserves.Add(Reserves.Incoming(country, to.Custody, taken));
        to.Treasury.Debt.Take(
            LoanSource.Foreign, lender, taken, RateKind.Floating,
            offer.Rate - _world.CountryById(lender).KeyRate);

        return taken;
    }

    /// <summary>Сколько у страны резервов сверх собственной нужды на ввоз.</summary>
    private static Money FreeToLend(Country lender)
    {
        var need = new Money(lender.ImportsPerDay.Raw * Prices.TargetCoverDays);
        var have = lender.State.Treasury.Reserves.Liquid;

        return have > need ? have - need : default;
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
            // Компаний нет — копит государство, как было до них. Так живут тестовые миры.
            if (_world.CompaniesOf(country.Id).Count == 0)
            {
                var added = _added.GetValueOrDefault(country.Id);
                if (added.Raw > 0)
                {
                    var share = new Money(added.Raw * Construction.InvestmentShare / 100);

                    _investment[country.Id] = _investment.GetValueOrDefault(country.Id) + share;
                    _saved[country.Id] = share;
                }
            }

            if (!_plan.TryGetValue(country.Id, out var plan)) continue;

            var builder = _builder.GetValueOrDefault(country.Id);
            var info = _world.Buildings[plan.Type];
            var wages = WagesFor(country, info);
            var price = Construction.CostOf(info.BuildCost, country.State.Prices) + wages;
            var done = 0;
            var limit = Math.Min(plan.Count, CanRaisePerTick(country.Id, plan.Type));

            for (var built = 0; built < limit; built++)
            {
                // Платит тот, кто строит: компания из своих денег, государство из
                // вложенного игроком.
                var paid = builder is not null
                    ? builder.TrySpend(price)
                    : _investment.GetValueOrDefault(country.Id) >= price;

                if (!paid) break;
                if (!country.State.Stock.TryConsume(info.BuildCost, Load.Full))
                {
                    // Материалов не нашлось — деньги возвращаем: списали их вперёд.
                    builder?.Earn(price);

                    break;
                }

                if (builder is null) _investment[country.Id] -= price;

                plan.Where.AddBuildings(plan.Type, 1);
                builder?.Add(plan.Where.Id, plan.Type, 1);

                // Съеденное стройкой — такой же расход, как заводское сырьё. Без этого
                // материалы попадали бы в добавленную стоимость дважды: и когда их
                // сделали, и когда из них построили.
                foreach (var (good, amount) in info.BuildCost) _consumed.Add(country.Id, good, amount);

                // Строителям платят из того же кошелька, а не вторым разом из казны:
                // зарплата уже сидит в цене постройки.
                var toBuilders = wages < price ? wages : price;
                country.Households.Earn(toBuilders);

                // Остальное — плата за материалы, и её получает тот, кто их сделал. Раньше
                // эти деньги просто исчезали: компания их списывала, а в кассу не приходило
                // ничего, и стройка выносила деньги из оборота.
                var forStuff = price - toBuilders;
                if (forStuff.Raw > 0)
                {
                    country.State.Treasury.Receive(forStuff);
                    _sales[country.Id] = _sales.GetValueOrDefault(country.Id) + forStuff;
                }

                done++;
            }

            _builders[country.Id] = _world.Efficiency.HandsFor(
                country.Id, info.Sector, (long)info.BuildWorkers * done);

            if (!_ordered.TryGetValue(country.Id, out var order) || order.Type != plan.Type) continue;

            // Заказ держится, пока вложенное игроком не израсходовано. Построенное сверх
            // него — это уже обычные деньги страны.
            var spent = new Money(price.Raw * done);

            if (order.Left > spent) _ordered[country.Id] = (order.Type, order.Left - spent);
            else _ordered.Remove(country.Id);
        }
    }

    /// <summary>Что и где стране строить по средствам. Пусто, если ничего не подходит.</summary>
    /// <summary>Заказанное игроком, если его есть где поставить.</summary>
    private (BuildingType Type, Region Where)? Ordered(Country country)
    {
        if (!_ordered.TryGetValue(country.Id, out var order) || order.Left.Raw <= 0) return null;

        var where = PlaceFor(country, _world.Buildings[order.Type]);

        return where is null ? null : (order.Type, where);
    }

    /// <param name="focus">Отрасли, в которых компания работает. Пусто — берётся за что
    /// угодно; так строит государство по заказу игрока.</param>
    private (BuildingType Type, Region Where)? BestBuild(
        Country country, Money purse, IReadOnlyList<Sector>? focus = null)
    {
        (BuildingType Type, Region Where)? best = null;
        var bestValue = 0L;

        foreach (var type in AllBuildings)
        {
            var info = _world.Buildings[type];
            if (info.BuildCost.Count == 0) continue;

            // Лесопромышленная компания не станет строить ракетный завод, даже если он
            // прибыльнее: ни людей, ни связей, ни понимания дела у неё нет.
            if (focus is not null && !focus.Contains(info.Sector)) continue;

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
