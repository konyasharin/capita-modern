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
    private readonly Dictionary<byte, List<(Company? Builder, BuildingType Type, Region Where, int Count)>>
        _plan = [];

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

    /// <summary>Сколько компания может занять сверх того, что у неё есть.</summary>
    /// <remarks>Меньшее из двух: что банк ещё не раздал и сколько ей дадут по мере долга —
    /// пять годовых выручек, дальше черта безнадёжности.</remarks>
    private Money CanBorrow(Country country, Company company)
    {
        var yearly = Worth(country, company);
        if (yearly.Raw <= 0) return default;

        var room = new Money(yearly.Raw * BrokeAt) - company.Debt;
        if (room.Raw <= 0) return default;

        var free = country.Banks.Free;

        return room < free ? room : free;
    }

    /// <summary>Какая доля рабочей силы приходится на стройку, в сотых.</summary>
    /// <remarks>В жизни строители — семь-восемь процентов занятых почти в любой стране.</remarks>
    public const int BuildersShare = 8;

    /// <summary>Почему стройка не идёт: нет ниши, нет денег, нет материалов, нет рук,
    /// и сколько зданий всё-таки заказано. Только для замера.</summary>
    public static readonly long[] Stall = new long[7];

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
        Run(nameof(Prepare), Prepare);
        Run(nameof(CollectInputs), CollectInputs);
        Run(nameof(PlanBuilds), PlanBuilds);
        Run(nameof(CountHands), CountHands);
        Run(nameof(Trade), Trade);
        Run(nameof(PayAbroad), PayAbroad);
        Run(nameof(NoteTrade), NoteTrade);
        Run(nameof(Borrow), Borrow);
        Run(nameof(PayInterest), PayInterest);
        Run(nameof(Repay), Repay);
        Run(nameof(CheckDefaults), CheckDefaults);
        Run(nameof(MoveRates), MoveRates);
        Run(nameof(CollectAvailable), CollectAvailable);
        Run(nameof(CollectOutputs), CollectOutputs);
        Run(nameof(PayWages), PayWages);
        Run(nameof(FeedPeople), FeedPeople);
        Run(nameof(Store), Store);
        Run(nameof(Tax), Tax);
        Run(nameof(BuildEstate), BuildEstate);
        Run(nameof(Dole), Dole);
        Run(nameof(MovePrices), MovePrices);
        Run(nameof(AnchorPrices), AnchorPrices);
        Run(nameof(PullPrices), PullPrices);
        Run(nameof(Wear), Wear);
        Run(nameof(Banking), Banking);
        Run(nameof(Build), Build);
        Run(nameof(Settle), Settle);
        Run(nameof(PayProfits), PayProfits);
        Run(nameof(NoteDemand), NoteDemand);
        Run(nameof(Balance), Balance);
        Run(nameof(Rank), Rank);
        Run(nameof(UpdateDemographics), UpdateDemographics);
    }

    /// <summary>Сколько тактов ушло на каждый шаг тика за всю партию.</summary>
    /// <remarks>Считается всегда: тридцать отметок времени на тик ничего не стоят, а без
    /// них узкое место ищется гаданием — и находится не с первого раза.</remarks>
    public IReadOnlyDictionary<string, long> Steps => _steps;

    private readonly Dictionary<string, long> _steps = new();

    private void Run(string name, Action step)
    {
        var from = System.Diagnostics.Stopwatch.GetTimestamp();
        step();
        _steps[name] = _steps.GetValueOrDefault(name) + System.Diagnostics.Stopwatch.GetTimestamp() - from;
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
        _stateWants.Clear();
        _stateBought.Clear();
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
        _replace.Clear();
        _raised.Clear();
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

                // Изношенное придётся заменить, и на это нужен материал — даже когда
                // построить сегодня не выходит. Без такого спроса выпуск сбавлялся до
                // нынешней стройки, стройка упиралась в пустой склад, и капитал таял по
                // кругу.
                //
                // Считается отдельно от _inputs нарочно: это нужда завтрашнего дня, а не
                // сегодняшний заказ. Попав в заявку внешнему рынку, она вдвое раздувала
                // ввоз и внешний долг мира — страны заказывали на замену то, чем платить
                // им нечем.
                var life = Math.Max(1, info.LifeYears * DaysInYear);
                foreach (var (good, amount) in info.BuildCost)
                {
                    var replace = new GoodAmount(building.Value * amount.Raw / life);
                    if (replace.Raw > 0) _replace.Add(owner, good, replace);
                }

                _working.Add(owner, building.Key, building.Value);

                // Отстающей стране тот же завод обходится в большее число рук: комбайн
                // против полусотни человек с мотыгами.
                //
                // По вчерашней загрузке, а не по полной мощности: цех, работающий вполсилы,
                // и людей держит вполовину. Пока считали по мощности, заводы занимали всех
                // до единого, и на стройку рук не оставалось вовсе — четыре миллиона
                // отказов за партию.
                var busy = _loadWas.GetValueOrDefault(owner, Load.Full);
                var hands = _world.Efficiency.HandsFor(
                    owner, info.Sector, (long)info.OptimalWorkers * building.Value * busy / Load.Full);

                _jobs[owner] = _jobs.GetValueOrDefault(owner) + hands;
                if (info.Sector == Sector.Services) _serviceJobs += hands;
            }
        }

        // Население — такой же претендент на товар, как отрасли, и со своим весом:
        // карточная система станет обычным законом, который этот вес поднимает.
        foreach (var country in _world.Countries)
        {
            var weight = country.Priorities.WeightOf(Sector.People);

            // Сперва прожиточный минимум по норме, затем то, что осталось от дохода, —
            // долями. Оттого спрос и идёт за достатком: прежде он был нормой и стоял на
            // месте, сколько бы денег людям ни доставалось.
            var people = _world.PopulationOf(country.Id).Whole;
            var prices = country.State.Prices;
            var floor = default(Money);
            var pull = 0L;

            foreach (var (good, rate) in _world.Needs.BaseRates)
            {
                var least = new GoodAmount(rate.Raw * people / 1_000_000);
                var cost = prices.CostOf(good, least);

                floor += cost;

                // Свободные деньги делятся не по величине минимума, а по тому, насколько
                // товар идёт за доходом: еда почти не идёт, услуги и потребтовары идут
                // быстрее его.
                pull += cost.Raw * _world.Needs.ByIncome(good) / Needs.Scale;
            }

            // Доход — это заработок плюс то, что берут из запаса: в первые дни партии
            // зарплат ещё не платили вовсе, и без запаса спрос выходил нулевым, а за ним
            // нулевым и выпуск.
            //
            // Из запаса берут годовую долю, а не месячную: накопленное копилось годами, и
            // тратить его двенадцать раз в год никто не станет. С месячной долей люди
            // просили услуг вдевятеро больше, чем мир способен дать.
            var income = country.Payroll + new Money(country.Households.Savings.Raw / SpendSavingsIn);

            foreach (var (good, rate) in _world.Needs.BaseRates)
            {
                var least = new GoodAmount(rate.Raw * people / 1_000_000);

                var share = pull <= 0
                    ? 0
                    : (int)(prices.CostOf(good, least).Raw * _world.Needs.ByIncome(good)
                        / Needs.Scale * 100 / pull);

                var wanted = Spending.Wanted(income, floor, prices.Of(good), least, share);
                _peopleWants.Add(country.Id, good, wanted);
                _inputs.Add(country.Id, good, wanted);
                _claims.Add(country.Id, good, wanted * weight / Priorities.NormalWeight);
            }

            OrderArms(country);
            OrderEstate(country);

            // Школы, больницы, управление — казённые услуги. В жизни на них уходит около
            // шестой части ВВП, и без них услуги в модели покупало одно население.
            Want(country, GoodType.Services,
                new Money(_added.GetValueOrDefault(country.Id).Raw * StateServiceShare / 10_000),
                _stateWants);
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

        // Склонность к армии — часть характера страны: при прочих равных она тратит на
        // оружие охотнее или скупее соседа с той же долей в данных.
        var budget = new Money(_added.GetValueOrDefault(country.Id).Raw * country.DefenceShare
            / 10_000 * country.Character.Arms / Character.Usual);
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

    /// <summary>Доля выпуска на казённые услуги, в сотых процента. В жизни конечное
    /// потребление государства — около семнадцати процентов ВВП, и почти всё это
    /// услуги: школы, больницы, управление, охрана порядка.</summary>
    private const int StateServiceShare = 1700;

    private readonly Tally<GoodType, GoodAmount> _stateWants = new();
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
        CountAfford();

        foreach (var good in AllGoods)
        {
            // Услуги через границу не возят: стрижку покупают там же, где живут.
            if (good == GoodType.Services) continue;

            var market = _markets[(int)good];
            market.Clear();

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

                // Замена изношенного входит в поток: это нужда завтрашнего дня, но
                // покупать под неё надо сегодня. Пока её не было в заявке, страна, которой
                // материалы нужны, за границей их не просила, а страна, у которой мощности
                // есть, видела свой полный склад и глушила заводы. Материалы шли на
                // тридцати восьми процентах мощности при избытке сырья для них.
                var flow = _inputs.Get(country.Id, good) + _replace.Get(country.Id, good) - forBuilding;

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

                // Просить можно сколько угодно, а заплатить — только тем, что есть.
                // Страна с пустыми резервами урезает заказ, а не занимает под него: без
                // этого она ввозила в долг без предела, и внешний долг рос вечно.
                var afford = _afford.GetValueOrDefault(country.Id, Money.Scale);
                if (afford < Money.Scale) bid = new GoodAmount(bid.Raw * afford / Money.Scale);

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
                market.Add(new MarketOrder(
                    country.Id, country.State, offer, bid, inWorld, inWorld,
                    _world.Efficiency.Of(country.Id, SectorOf(good))));
                wanted += bid;
                offered += offer;
            }

            _wanted[(int)good] = wanted;
            _offered[(int)good] = offered;
        }

        Match();

        foreach (var good in AllGoods)
        {
            if (good == GoodType.Services) continue;

            _trading = good;
            _dealValue = default;
            _dealVolume = default;

            var from = System.Diagnostics.Stopwatch.GetTimestamp();
            foreach (var deal in _deals[(int)good]) Close(deal);
            _steps["Trade.Close"] = _steps.GetValueOrDefault("Trade.Close")
                + System.Diagnostics.Stopwatch.GetTimestamp() - from;

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
                _world.Market.Prices.MoveFromBalance(good, _wanted[(int)good], _offered[(int)good]);
            }
        }
    }

    /// <summary>Сводит все товары разом, каждый своим потоком.</summary>
    /// <remarks>
    /// Сведение ничего не меняет в мире — оно только считает, кто у кого сколько возьмёт.
    /// Значит его можно вести по всем товарам сразу, а сделки проводить потом и по
    /// порядку, чтобы тик остался воспроизводимым. Семь десятых времени уходило сюда.
    ///
    /// Плата за это: заявки по всем товарам считаются от складов на начало тика, а не от
    /// того, что осталось после торговли предыдущим. Так даже честнее — торговый день
    /// один на всех, а не тридцать один по очереди.
    /// </remarks>
    private void Match()
    {
        var from = System.Diagnostics.Stopwatch.GetTimestamp();

        Parallel.ForEach(TradedGoods, good =>
        {
            var deals = _deals[(int)good];
            deals.Clear();

            _exchanges[(int)good].Settle(
                CollectionsMarshal.AsSpan(_markets[(int)good]),
                (seller, buyer) => Markup(seller, buyer, good),
                deals.Add,
                _world.Elasticity.Substitution(good));
        });

        _steps["Trade.Match"] = _steps.GetValueOrDefault("Trade.Match")
            + System.Diagnostics.Stopwatch.GetTimestamp() - from;
    }

    /// <summary>Что возят через границу. Услуги не возят.</summary>
    private static readonly GoodType[] TradedGoods =
        [.. Enum.GetValues<GoodType>().Where(good => good != GoodType.Services)];

    private readonly List<MarketOrder>[] _markets =
        [.. Enum.GetValues<GoodType>().Select(_ => new List<MarketOrder>())];

    private readonly List<Deal>[] _deals =
        [.. Enum.GetValues<GoodType>().Select(_ => new List<Deal>())];

    private readonly Exchange[] _exchanges =
        [.. Enum.GetValues<GoodType>().Select(_ => new Exchange())];

    private readonly GoodAmount[] _wanted = new GoodAmount[Enum.GetValues<GoodType>().Length];
    private readonly GoodAmount[] _offered = new GoodAmount[Enum.GetValues<GoodType>().Length];

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
    /// <summary>Отрасль товара: кто его делает.</summary>
    public Sector SectorOf(GoodType good) => _sectorOf[(int)good];

    /// <summary>Во что обойдётся покупателю единица товара у этого продавца.</summary>
    /// <remarks>Цена продавца плюс дорога от него до покупателя плюс пошлина покупателя.
    /// Отсюда и берётся то, чего в общем котле быть не могло: дальний дешёвый товар
    /// проигрывает ближнему дорогому.</remarks>
    private int Markup(byte seller, byte buyer, GoodType good)
    {
        var route = _world.Routes.CostBetween(seller, buyer);

        return route >= Politics.Routes.Unreachable
            ? -1
            : _world.TradeCosts.ImportMarkup(buyer, good, route);
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


        // Вывоз: товар ушёл со склада продавца, чужая валюта легла в резервы страны. Но
        // платить рабочим и покупать сырьё продавцу надо своими — центробанк меняет ему
        // выручку, как и делает в жизни. Ввоз забирает ровно столько же из обращения:
        // страна отдала валюту, значит местных денег стало меньше. Вместе это и есть
        // сальдо, и денежная масса ходит за ним, а не сама по себе.
        _earnedAbroad[Slot(deal.Seller, _trading)] += InLocal(seller, deal.Paid);
        buyer.Bank.Withdraw(InLocal(buyer, deal.Paid));

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
    /// <remarks>Сто двадцать восемь бит только когда без них не обойтись: на каждой сделке
    /// их деление стоило дороже самой сделки, а сделок за тик двадцать шесть тысяч.</remarks>
    public Money InLocal(Country country, Money world)
    {
        var rate = country.ExchangeRate.Raw;

        return new Money(world.Raw == 0 || Math.Abs(world.Raw) <= long.MaxValue / Math.Max(1, rate)
            ? world.Raw * rate / Money.Scale
            : (long)((Int128)world.Raw * rate / Money.Scale));
    }

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
            // За потенциалом, а не за нынешним выпуском. Выпуск падает и от нехватки сырья,
            // а деньги от этого не исчезают: дефицит в жизни цены поднимает, а не роняет.
            // Пока масса шла за фактом, выходила петля — у Германии встали цеха, масса
            // упала следом, цены рухнули вчетверо ниже стартовых, и на мировом рынке ей
            // перестали продавать вовсе: её заявка стоила копейки. Потенциал же меняется
            // только когда строят или изнашивают, то есть по делу.
            var could = PotentialValue(country);
            if (could.Raw <= 0) continue;

            if (!_basePotential.TryGetValue(country.Id, out var couldBefore))
            {
                _basePotential[country.Id] = could;
                continue;
            }

            if (couldBefore.Raw <= 0) continue;

            country.Bank.Follow(new Money((long)((Int128)country.Bank.Start.Raw * could.Raw / couldBefore.Raw)));

            var want = PriceLevel.Target(country.Bank.Supply, country.Bank.Start, could, couldBefore);
            var step = Math.Clamp(
                want,
                level * (100 - PriceLevel.StepPercent) / 100,
                level * (100 + PriceLevel.StepPercent) / 100);

            _levelPush[country.Id] = level > 0 ? (int)((long)(step - level) * 10_000 / level) : 0;

            country.State.Prices.Rescale(step, level);
        }

        _worldLevel = PriceLevel.Of(nominalWorld, realWorld);
    }

    /// <summary>Потенциальный выпуск страны в стартовых ценах: мера её мощности.</summary>
    /// <remarks>Меняется только когда строят или изнашивают. Оттого за ним и следует
    /// денежная масса: она про то, сколько экономика может, а не сколько вышло сегодня.</remarks>
    private Money PotentialValue(Country country)
    {
        var prices = country.State.Prices;
        var total = default(Money);

        foreach (var good in AllGoods)
        {
            var could = PotentialOutputOf(country.Id, good);
            if (could.Raw == 0) continue;

            total += new Money((long)((Int128)prices.StartOf(good).Raw * could.Raw / GoodAmount.Scale));
        }

        return total;
    }

    private readonly Dictionary<byte, Money> _basePotential = new();

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

    /// <summary>Уровень цен всего мира — то, вокруг чего ходят курсы.</summary>
    public int WorldPriceLevel => _worldLevel;

    /// <summary>Загрузка предприятий страны в сотых долях от полной. Меньше единицы —
    /// значит рук не хватило и всё производство идёт вполсилы.</summary>
    public long LoadIn(byte country) => _hands.GetValueOrDefault(country, Load.Full);

    /// <summary>Что страна выпустила за тик по одному товару.</summary>
    public GoodAmount OutputOf(byte country, GoodType good) => _outputs.Get(country, good);

    /// <summary>Сколько товара ушло в чужие рецепты за тик.</summary>
    public GoodAmount ConsumedOf(byte country, GoodType good) => _consumed.Get(country, good);

    /// <summary>Сколько страна заказала по одному товару — и заводы, и население.</summary>
    public GoodAmount InputOf(byte country, GoodType good) => _inputs.Get(country, good);

    /// <summary>Сколько материала нужно стране на замену изношенного за сутки.</summary>
    public GoodAmount ReplaceWantOf(byte country, GoodType good) => _replace.Get(country, good);

    /// <summary>Сколько страна просила у внешнего рынка.</summary>
    public GoodAmount BidOf(byte country, GoodType good) => _bid.Get(country, good);

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

    /// <summary>Что страна видит, выбирая, что строить: прибыль, плата работникам, цена
    /// постройки и окупаемость за век. Только для замера.</summary>
    public (Money Profit, Money Pay, Money Cost, long Payback) WhyBuild(byte country, BuildingType type)
    {
        var whose = _world.CountryById(country);
        var info = _world.Buildings[type];
        var profit = Construction.ProfitOf(
            new BuildingRecipe(info.Inputs, info.Outputs), whose.State.Prices, good => Faced(whose, good));

        var hands = _world.Efficiency.HandsFor(country, info.Sector, info.OptimalWorkers);
        var pay = new Money(whose.Payroll.Raw / Math.Max(1, EmployedIn(country)) * hands);
        var cost = Construction.CostOf(info.BuildCost, whose.State.Prices);

        return (profit, pay, cost, Construction.Payback(profit - pay, cost, info.LifeYears));
    }

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

    /// <summary>Что строится прямо сейчас: тип, регион и сколько штук. Строек за тик
    /// бывает несколько — здесь самая большая, для окна страны.</summary>
    public (BuildingType Type, Region Where, int Count)? PlanOf(byte country)
    {
        if (!_plan.TryGetValue(country, out var plans) || plans.Count == 0) return null;

        var biggest = plans[0];
        foreach (var plan in plans)
        {
            if (plan.Count > biggest.Count) biggest = plan;
        }

        return (biggest.Type, biggest.Where, biggest.Count);
    }

    /// <summary>Сбавляет загрузку, когда склад уже вдвое выше нормы запаса.</summary>
    /// <remarks>
    /// Завод не работает в никуда: чем больше на складе сверх нормы, тем ниже загрузка.
    /// Сбавляют не от первой лишней единицы, а когда запас вдвое выше нормы: склад и
    /// должен гулять вокруг неё.
    ///
    /// Пола у загрузки нет нарочно. С полом в половину мощности шахта в замкнутом мире
    /// копала в пятьдесят раз больше, чем тратилось, и склад пух без предела — это и
    /// поймал SteadyStateTests.StockDoesNotPileUp.
    ///
    /// Без этого выпуск оседал на полке: склады мира росли на двадцать триллионов в год
    /// при ВВП в сто, а в жизни изменение запасов — около процента. Заодно оттого и не
    /// было нигде запаса мощности: число заводов пришлось выводить из фактического
    /// выпуска, ведь работали они всегда на полную.
    ///
    /// Вывоз считается наравне со своими: он уже прошёл в этом тике, и без него страна,
    /// которая кормит полмира, считала бы свой склад лишним.
    /// </remarks>
    private long Ordered(Country owner, Buildings.BuildingInfo recipe, long runs)
    {
        var country = owner.Id;

        foreach (var (good, amount) in recipe.Outputs)
        {
            if (amount.Raw <= 0) continue;

            // Своя нужда, замена изношенного и доля мирового спроса. Последнее важно:
            // страна с полным складом глушила добычу, хотя мир просил товар, — а вывозить
            // она может ровно столько, сколько у неё просят. Доля берётся по её месту в
            // мировом предложении: кто может дать больше, на того больше и рассчитывают.
            var mine = PotentialOutputOf(country, good);
            var everyone = _worldPotential[(int)good];
            var abroad = everyone.Raw <= 0
                ? default
                : new GoodAmount((long)((Int128)_wanted[(int)good].Raw * mine.Raw / everyone.Raw));

            // Стройка входит настоящей заявкой (_build), а не нуждой на замену (_replace):
            // замену никто не выкупает, и норма от неё выходила вчетверо выше нужного. Полки
            // набивались под неё — от девяноста до трёхсот суток расхода у всех товаров, — и
            // правило загрузки глушило заводы: четырнадцать процентов мировой мощности.
            var wanted = _inputs.Get(country, good) + _build.Get(country, good) + abroad;
            var target = new GoodAmount(wanted.Raw * Prices.TargetCoverDays);
            var stock = _available.Get(country, good) + _outputs.Get(country, good);
            if (stock <= target) continue;

            // Товар, который вовсе никому не нужен, делают в самую малую силу: норма у
            // него нулевая, и делить на склад тут нечего.
            var load = target.Raw <= 0 ? MinLoad : target.Raw * Load.Full / stock.Raw;
            var fits = runs * load / Load.Full;
            if (fits < runs) runs = fits;
        }

        return runs;
    }

    /// <summary>Доля мощности для товара, которого не просит вовсе никто.</summary>
    /// <remarks>Не ноль: завод, который встал совсем, уже не заметит, что товар снова
    /// понадобился. Сотая доля мощности держит его тёплым и склад не пухнет.</remarks>
    public const int MinLoad = Load.Full / 100;

    /// <summary>Сколько зданий типа работало на прошлом тике. Меньше построенных —
    /// значит не хватило сырья или рук.</summary>
    public int WorkingOf(byte country, BuildingType type) => _working.Get(country, type);

    /// <summary>Сколько людей заняты на производстве в стране прямо сейчас.</summary>
    public long EmployedIn(byte country) =>
        Math.Min(_jobs.GetValueOrDefault(country), _world.WorkersOf(country));

    /// <summary>Сколько людей кормится своим делом, не попав на завод.</summary>
    /// <remarks>
    /// Предприятия и стройка берут не всех: в модели оставалось незанятыми шестьсот
    /// миллионов человек, и занятость выходила 2813 млн против 3240 в жизни. Но эти люди
    /// не сидят без дела — они пашут свой огород, торгуют на рынке, чинят и подвозят. В
    /// статистике это неформальный сектор, и в нём по оценкам МОТ два миллиарда человек,
    /// почти все в бедных странах.
    ///
    /// Без работы остаётся <see cref="Jobless"/> — примерно столько безработных в мире и
    /// есть.
    /// </remarks>
    public long SelfEmployedIn(byte country)
    {
        var workers = _world.WorkersOf(country);
        var idle = workers - EmployedIn(country);
        var jobless = workers * Jobless / 100;

        return idle > jobless ? idle - jobless : 0;
    }

    /// <summary>Какая доля рабочей силы не находит дела вовсе, в процентах. В жизни
    /// мировая безработица держится около пяти-шести процентов.</summary>
    public const int Jobless = 6;

    /// <summary>Выпуск своего дела отдельно не считается, и это не забывчивость.</summary>
    /// <remarks>
    /// Пробовали вменять его долей от заводской выработки, как считают в жизни. Мировой ВВП
    /// подскочил с 92.8 до 104.2 трлн при настоящих 84.9 — значит эти люди уже посчитаны
    /// внутри заводских чисел. Видно это и по выработке: у России она выходит 55.7 тыс. $
    /// против настоящих 20, у Бразилии 31.8 против 15. Завод в модели кормит больше людей,
    /// чем числится на нём занятыми, — вот они и есть.
    ///
    /// Поэтому своё дело считается в занятости, но не в выпуске: так обе меры сходятся с
    /// жизнью, а не одна за счёт другой.
    /// </remarks>
    public const int SelfEmployedTimes = 0;

    /// <summary>Сколько людей просят предприятия. Больше занятых — значит рук не хватает.</summary>
    public long JobsIn(byte country) => _jobs.GetValueOrDefault(country);

    /// <summary>Сколько мир не купил из-за дорогой доставки и сколько — из-за того, что
    /// товара ни у кого не осталось.</summary>
    public (GoodAmount Refused, GoodAmount Empty) UnfilledBids
    {
        get
        {
            var refused = default(GoodAmount);
            var empty = default(GoodAmount);

            foreach (var exchange in _exchanges)
            {
                refused += exchange.Refused;
                empty += exchange.Empty;
            }

            return (refused, empty);
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

    /// <summary>На какую долю заказа стране хватает валюты, в долях
    /// <see cref="Money.Scale"/>.</summary>
    /// <remarks>
    /// Платить за ввоз нечем, кроме резервов и сегодняшней выручки от вывоза. Раньше
    /// заявка от этого не зависела вовсе: страна просила сколько хотела, не могла
    /// заплатить, и недостачу закрывали займом — оттого внешний долг и рос без предела, а
    /// к пятому году больше половины стран отказывались платить.
    ///
    /// Считается до торговли и по вчерашним ценам: точнее не нужно, а перебирать товары
    /// дважды дорого.
    /// </remarks>
    private void CountAfford()
    {
        _afford.Clear();

        foreach (var country in _world.Countries)
        {
            // По настоящему ввозу, а не по всей заявке: большую её часть страна закрывает
            // своим же товаром, и валюта на это не нужна. Заявка — это заказ рынку, а
            // платят чужими деньгами только за то, что и правда приехало.
            var bill = new Money(country.ImportsPerDay.Raw * Prices.TargetCoverDays);
            if (bill.Raw <= 0) continue;

            // В кошелёк идёт и заём, но не весь разом: страна выбирает свой запас
            // заимствования за год, а не за день. Без этого ввоз держался бы на одних
            // резервах, и приток капитала в модели пропал бы вовсе.
            var owed = country.State.Treasury.Debt.Owed(LoanSource.Foreign);
            var ceiling = new Money(DebtCapacity(country).Raw / 100 * CreditMarket.SafeBurden);
            var room = ceiling > owed ? ceiling - owed : default;

            // Заказ — это запас на сорок суток, значит и выручку считаем за тот же срок:
            // ввоз в жизни оплачивают с отсрочкой в месяц-другой, а не в тот же день.
            var purse = country.State.Treasury.Reserves.Liquid
                + new Money(country.ExportsPerDay.Raw * Prices.TargetCoverDays)
                + new Money(room.Raw / DaysInYear);
            if (purse >= bill) continue;

            _afford[country.Id] = (int)((Int128)purse.Raw * Money.Scale / bill.Raw);
        }
    }

    /// <summary>Какая доля заказа стране по карману.</summary>
    public int AffordOf(byte country) => _afford.GetValueOrDefault(country, (int)Money.Scale);

    private readonly Dictionary<byte, int> _afford = new();

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

            // Кто живёт в долг — просит больше, кто копит — меньше или вовсе ничего.
            want = new Money(want.Raw * country.Character.Borrows / Character.Usual);

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

            // Сверх графика гасят из того, что осталось после закупок и сверх подушки:
            // дорогое вперёд. Норма достаточности — запас на сорок суток ввоза, та же, по
            // которой MoveRates судит о курсе. Раньше подушки не было, и резервы уходили
            // в долг подчистую: курс за это же слабел каждый тик, страна переставала
            // покупать сырьё, отрасли вставали, вывоза не было — и резервы не возвращались.
            // Копящая страна держит подушку толще нормы и гасит долг только сверх неё.
            var cushion = new Money(country.ImportsPerDay.Raw * Prices.TargetCoverDays
                * country.Character.Hoards / Character.Usual);

            var held = Valued(_bid, country.Id);
            if (cushion > held) held = cushion;

            var spare = country.State.Treasury.Reserves.Liquid - held;
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

            if (Restructure(country, debt, missed)) continue;
            if (missed < GraceDays) continue;
            // Той же меркой, что и ставка: у кого валюту держат в резервах, тот платит
            // своими деньгами, и по вывозу его судить нельзя.
            if (debt.BurdenToExports(DebtCapacity(country)) < DefaultBurden) continue;

            _refusals.Add(new Refusal(
                _day,
                country.Iso,
                debt.Owed(LoanSource.Foreign),
                DebtCapacity(country),
                country.State.Treasury.Reserves.Liquid,
                country.ImportsPerDay,
                country.ExportsPerDay,
                country.ExchangeRate));

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
        (country.DefaultedOnDay > 0 && _day - country.DefaultedOnDay < DaysInYear * DefaultLockYears)
        || (country.TalkedOnDay > 0 && _day - country.TalkedOnDay < DaysInYear * TalkLockYears);

    /// <summary>Через сколько суток непрерывных пропусков садятся за стол. Раньше отказа,
    /// но не сразу: два месяца просрочки — это уже не забывчивость.</summary>
    private const int TalksDays = 60;

    /// <summary>За какой чертой нагрузки долг переписывают, в процентах.</summary>
    /// <remarks>
    /// Выше той, за которой перестают давать в долг, и заметно ниже той, за которой
    /// отказываются платить. С порогом на уровне кредитного долг переписывали ста двадцати
    /// девяти странам за пять лет против примерно десятка случаев в год в жизни: за стол
    /// садились в тот же день, когда закрывался кредит, а в жизни сперва пробуют выкрутиться
    /// сами.
    /// </remarks>
    private const int TalksBurden = 300;

    /// <summary>На сколько лет закрывается кредит после переписанного долга. Короче, чем
    /// после отказа: договорившегося рынок прощает быстрее.</summary>
    private const int TalkLockYears = 2;

    /// <summary>Сколько раз одной стране перепишут долг за партию. Дальше кредиторы
    /// перестают верить обещаниям.</summary>
    private const int TalksAllowed = 2;

    /// <summary>Долг переписывают: часть списывают, чтобы страна снова могла платить.</summary>
    /// <remarks>
    /// В жизни до отказа доходит редко — раньше садятся за стол. Парижский клуб и МВФ этим
    /// и заняты: часть долга прощают, срок растягивают, и страна возвращается к платежам.
    /// Кредитор теряет меньше, чем потерял бы при отказе, потому и соглашается.
    ///
    /// Списывают ровно столько, чтобы нагрузка вернулась к той черте, за которой ещё дают
    /// в долг: меньше — и через год всё повторится, больше — и кредитор не сядет за стол.
    /// </remarks>
    private bool Restructure(Country country, Debt debt, int missed)
    {
        if (missed < TalksDays) return false;
        if (_talks.GetValueOrDefault(country.Id) >= TalksAllowed) return false;

        var capacity = DebtCapacity(country);
        if (capacity.Raw <= 0) return false;

        var burden = debt.BurdenToExports(capacity);
        if (burden <= TalksBurden) return false;

        // Пока есть чем платить, за стол не садятся: кредитор скажет сперва потратить своё.
        if (country.State.Treasury.Reserves.Liquid > country.ImportsPerDay) return false;

        var cut = 10_000 - CreditMarket.SafeBurden * 10_000 / burden;
        var forgiven = debt.Forgive(LoanSource.Foreign, cut);
        if (forgiven.Raw <= 0) return false;

        _talks[country.Id] = _talks.GetValueOrDefault(country.Id) + 1;
        _forgiven[country.Id] = _forgiven.GetValueOrDefault(country.Id) + forgiven;
        _missedInARow.Remove(country.Id);
        country.TalkedOnDay = _day;

        return true;
    }

    /// <summary>Сколько раз стране переписывали долг и сколько ей простили.</summary>
    public int TalksOf(byte country) => _talks.GetValueOrDefault(country);

    public Money ForgivenTo(byte country) => _forgiven.GetValueOrDefault(country);

    private readonly Dictionary<byte, int> _talks = new();
    private readonly Dictionary<byte, Money> _forgiven = new();

    /// <summary>Курс идёт за сальдо: кто больше ввозит, у того валюта дешевеет.</summary>
    /// <remarks>Петля замыкается через эластичность: подешевевшая валюта поднимает
    /// местную цену импортного, и заявка сама срезается.</remarks>
    /// <summary>Во сколько раз курс может отойти от паритета. Худшие настоящие обвалы —
    /// это разы за годы, а не за день.</summary>
    public const int RateSwing = 3;

    /// <summary>Насколько вывоз и ввоз отзываются на курс, в сотых.</summary>
    /// <remarks>Сумма упругостей больше единицы — условие Маршалла и Лернера, при котором
    /// ослабление валюты и правда улучшает сальдо. Полтора у каждой стороны с запасом его
    /// покрывают.</remarks>
    public const int TradeStretch = 150;

    /// <summary>Из чего сложился шаг курса за тик: паритет цен, сальдо, запас резервов.</summary>
    public readonly record struct RatePush(long Parity, long Balance, long Cushion);

    private readonly Dictionary<byte, RatePush> _ratePush = new();

    /// <summary>Что тянуло курс в последнем тике.</summary>
    public RatePush RatePushOf(byte country) => _ratePush.GetValueOrDefault(country);

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
            // Курс — такая же цена, и считается он так же: не ползёт шагами, а сразу
            // берётся тот, при котором сходится платёжный баланс. Прежде три силы двигали
            // его по проценту за тик, и выходил тот же интегратор, что и у цен: за пять
            // лет от старта вдвое уходили полторы сотни стран из двухсот.
            //
            // Паритет — куда курс тянет разница уровней цен: у кого цены выросли вдвое
            // против мира, у того и валюта вдвое дешевле. Сальдо отклоняет от паритета:
            // тратишь больше, чем получаешь, — валюта слабеет, и это делает твой вывоз
            // дешевле, а ввоз дороже, пока баланс не сойдётся.
            var parity = country.StartRate.Raw;
            if (_level.TryGetValue(country.Id, out var level) && _worldLevel > 0)
            {
                parity = country.StartRate.Raw * (long)level / _worldLevel;
            }

            var even = Clearing.Price(
                new Money(parity),
                new GoodAmount(outflow.Raw),
                new GoodAmount(inflow.Raw),
                TradeStretch,
                TradeStretch).Raw;

            // От паритета курс отходит втрое, не больше. Для товара стократный размах —
            // защита от вырожденного случая, а для валюты это уже нелепость: страна,
            // которой нечего вывезти, получала стократную девальвацию за один тик и
            // выпадала из мировой торговли вовсе, не успев даже занять.
            even = Math.Clamp(even, parity / RateSwing, parity * RateSwing);

            // Не прыжком, а половиной пути: цель считается заново каждый тик от паритета,
            // так что расходиться тут нечему, — но и мгновенным курс быть не должен. Без
            // задержки платёжный баланс сходился в тот же день, и занимать за границей
            // становилось незачем вовсе: внешний долг мира падал с шестидесяти семи
            // триллионов до четырёх, а кредит переставал работать как механизм.
            var rate = country.ExchangeRate.Raw + (even - country.ExchangeRate.Raw) / 2;

            var fromParity = parity - country.ExchangeRate.Raw;
            var fromBalance = even - parity;
            var was = rate;

            _ratePush[country.Id] = new RatePush(fromParity, fromBalance, rate - was);
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


    /// <summary>За сколько суток люди проедают накопленное, если перестанут зарабатывать.</summary>
    /// <remarks>Год. Полгода пробовал — хуже: спрос растёт, но вместе с ним цены, и
    /// загрузка падает с 77% до 66%.</remarks>
    public const int SpendSavingsIn = DaysInYear;

    /// <summary>Сколько прожиточных минимумов покрывает дневной доход людей, в сотых.</summary>
    /// <remarks>Сотня — доход ровно на минимум, двести — вдвое сверх него. Это и есть мера
    /// достатка: всё, что выше сотни, идёт на то, чего минимум не требует.</remarks>
    public int BasketsIn(byte country)
    {
        var whose = _world.CountryById(country);
        var people = _world.PopulationOf(country).Whole;
        var prices = whose.State.Prices;
        var floor = default(Money);

        foreach (var (good, rate) in _world.Needs.BaseRates)
        {
            floor += prices.CostOf(good, new GoodAmount(rate.Raw * people / 1_000_000));
        }

        if (floor.Raw <= 0) return Character.Usual;

        var income = whose.Payroll + new Money(whose.Households.Savings.Raw / SpendSavingsIn);

        return (int)Math.Min(int.MaxValue, income.Raw * Spending.Spends / 100 * 100 / floor.Raw);
    }

    /// <summary>Решает, что строить, и заявляет это как спрос наравне с заводами.</summary>
    /// <remarks>Строит не одна богатейшая компания, а каждая, у которой хватает на здание.
    /// Пока строила одна, мир поднимал девяносто тысяч зданий в год против трёхсот тысяч
    /// изношенных и терял по три процента капитала ежегодно.</remarks>
    private void PlanBuilds()
    {
        foreach (var country in _world.Countries)
        {
            if (!_plan.TryGetValue(country.Id, out var plans)) _plan[country.Id] = plans = [];
            plans.Clear();

            // Общие на страну потолки: материалы со склада и свободные руки одни на всех,
            // и вторая компания берёт то, что осталось после первой.
            // Потолок на тик — доля своего же капитала, а не одно число на всех. Полсотни
            // зданий в тик это восемнадцать тысяч в год: Китаю с четырьмя миллионами
            // зданий столько нужно только на замену двух недель износа. Оттого капитал и
            // таял у крупных стран, сколько бы денег и материалов у них ни было.
            //
            // Двадцатая доля капитала в год — предел того, что страна физически успевает
            // отстроить; в жизни быстрее не выходит даже на подъёме.
            var plants = 0;
            foreach (var region in _world.RegionsOf(country.Id))
            {
                foreach (var (type, _) in region.BuildingsCount) plants += region.BuildingsOf(type, country.Id);
            }

            var room = Math.Max(MaxBuildsPerTick, plants / (5 * DaysInYear));
            // Своя доля рабочей силы, а не остаток после заводов. В жизни строителей около
            // восьми процентов занятых, и берутся они не из тех, кого заводы не разобрали:
            // стройка нанимает наравне со всеми.
            //
            // Пока брали остаток, заводы занимали всех до единого — четыре миллиона отказов
            // «нет рук» за партию, — и капитал таял оттого, что строить его было некому.
            var free = _world.WorkersOf(country.Id) * BuildersShare / 100;
            var hired = 0L;

            // Заказ игрока идёт мимо ниш: государство строит что велено. Сама страна не
            // решает ничего — за неё решают компании, каждая в своём деле.
            var order = Ordered(country);

            foreach (var builder in Builders(country.Id))
            {
                if (room <= 0) break;

                // Кошелёк — это касса плюс то, что компания может занять. Заём приходит в
                // Banking, а план составляется здесь, за двадцать шагов до него: планируя
                // по одной кассе, компания заказывала меньше, чем могла поднять, и
                // капитал таял при полном банке. Двадцать восемь миллионов отказов
                // «нет денег» за партию.
                var purse = builder?.Cash ?? _investment.GetValueOrDefault(country.Id);
                if (builder is not null) purse += CanBorrow(country, builder);
                var best = order ?? Chosen(country, builder, purse);

                if (best is null)
                {
                    Stall[0]++;
                    continue;
                }

                var info = _world.Buildings[best.Value.Type];
                var price = Construction.CostOf(info.BuildCost, country.State.Prices) + WagesFor(country, info);
                if (price.Raw <= 0) continue;

                if (purse < price)
                {
                    Stall[1]++;
                    continue;
                }

                // За тик строится не больше потолка: не найдя материалов, страна заявляла бы
                // спрос, которого мир не выдержит. Режется именно число, а не кошелёк —
                // раньше лишние деньги пропадали, и вложенное игроком исчезало бы, не дойдя
                // до стройки.
                var count = (int)Math.Min(purse.Raw / price.Raw, room);

                // Заявлять больше, чем со склада откусишь, нельзя: страна просила материалы
                // на пятьсот зданий, а поднимала пять, и выдуманный спрос гнал цену вверх.
                var fits = RoomFor(country.Id, best.Value.Type);
                if (fits <= 0)
                {
                    Stall[2]++;
                    continue;
                }

                count = Math.Min(count, fits);

                // Стройке нужны руки, и берёт она их у заводов: больше, чем свободно, не
                // построишь ни за какие деньги.
                var perUnit = _world.Efficiency.HandsFor(
                    country.Id, info.Sector, info.BuildWorkers / Construction.BuildDays);

                if (perUnit > 0) count = (int)Math.Min(count, Math.Max(0, (free - hired) / perUnit));

                if (count <= 0)
                {
                    Stall[3]++;
                    continue;
                }

                Stall[4] += count;
                plans.Add((builder, best.Value.Type, best.Value.Where, count));
                room -= count;
                hired += perUnit * count;

                var weight = country.Priorities.WeightOf(info.Sector);
                foreach (var (good, amount) in info.BuildCost)
                {
                    var wanted = amount * count;
                    _inputs.Add(country.Id, good, wanted);
                    _build.Add(country.Id, good, wanted);
                    _claims.Add(country.Id, good, wanted * weight / Priorities.NormalWeight);
                }
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

    /// <summary>Выпуск считается по странам сразу: они друг другу не мешают.</summary>
    /// <remarks>Склады, кассы, компании и счётчики у каждой страны свои — а Tally внутри
    /// плоский массив, и ячейки разных стран не пересекаются. Единственное общее здесь —
    /// справочники зданий и умений, а их только читают.</remarks>
    /// <summary>Какой была загрузка страны на прошлом тике, в долях <see cref="Load.Full"/>.</summary>
    private readonly Dictionary<byte, int> _loadWas = new();

    /// <summary>За сколько суток занятость догоняет загрузку.</summary>
    /// <remarks>Квартал. Чем короче, тем сильнее качает: при единице занятость прыгала
    /// с 1976 до 2959 миллионов через год.</remarks>
    private const int HiringDays = 90;

    private void CollectOutputs()
    {
        // Мировая мощность по каждому товару: по ней делится мировой спрос между теми,
        // кто может его закрыть. Считается раз на тик — внутри MakeIn страны идут разом.
        foreach (var good in AllGoods)
        {
            var total = default(GoodAmount);
            foreach (var country in _world.Countries) total += PotentialOutputOf(country.Id, good);

            _worldPotential[(int)good] = total;
        }

        Parallel.ForEach(_world.Countries, MakeIn);

        // Запоминаем, на какой доле мощности страна и правда работала: по ней завтра
        // считаются руки.
        foreach (var country in _world.Countries)
        {
            var could = PotentialOf(country.Id, country.State.Prices);
            var did = ValueAddedOf(country.Id);

            var today = could.Raw <= 0
                ? Load.Full
                : (int)Math.Clamp(did.Raw * Load.Full / could.Raw, Load.Full / 10, Load.Full);

            // Не сегодняшняя загрузка, а сглаженная: завод не набирает и не увольняет людей
            // за сутки. Без этого занятость качалась вдвое через тик — руки считались по
            // вчерашней загрузке, а нехватка рук резала сегодняшнюю, и круг замыкался.
            var was = _loadWas.GetValueOrDefault(country.Id, today);
            _loadWas[country.Id] = (int)((was * (long)(HiringDays - 1) + today) / HiringDays);
        }
    }

    private readonly GoodAmount[] _worldPotential = new GoodAmount[Enum.GetValues<GoodType>().Length];

    /// <summary>Сколько загрузки потеряно на каждом ограничении: сырьё, склад, деньги, и
    /// сколько её было всего. Только для замера.</summary>
    public static readonly long[] Lost = new long[5];

    /// <summary>Куда за партию ушли деньги компаний: добавленная стоимость, зарплаты,
    /// владельцам, на стройку, износ в деньгах. Только для замера.</summary>
    public static readonly long[] Flows = new long[7];

    /// <summary>Сколько денег и долга у всех компаний мира прямо сейчас. Только для замера.</summary>
    public (Money Cash, Money Debt) CompanyPurses()
    {
        var cash = default(Money);
        var debt = default(Money);

        foreach (var country in _world.Countries)
        {
            foreach (var company in _world.CompaniesOf(country.Id))
            {
                if (!company.Alive) continue;

                cash += company.Cash;
                debt += company.Debt;
            }
        }

        return (cash, debt);
    }

    private void MakeIn(Country owner)
    {
        foreach (var building in AllBuildings)
        {
            var country = owner.Id;
            var count = _working.Get(country, building);
            if (count == 0) continue;

            var recipe = _world.Buildings[building];
            var weight = owner.Priorities.WeightOf(recipe.Sector);

            // Доля общая на всех, поэтому расход рецепта в ней сокращается.
            // Умножаем до деления, иначе целые числа дадут ноль.
            var load = _hands.GetValueOrDefault(country, Load.Full);
            long runs = count * load;

            if (load < Load.Full) Interlocked.Add(ref Lost[4], count * (long)(Load.Full - load));
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

            var afterInputs = runs;
            if (runs == 0)
            {
                Interlocked.Add(ref Lost[0], count * (long)Load.Full);
                continue;
            }

            runs = Ordered(owner, recipe, runs);
            if (runs <= 0) continue;

            Interlocked.Add(ref Lost[0], count * (long)Load.Full - afterInputs);
            Interlocked.Add(ref Lost[1], afterInputs - runs);
            Interlocked.Add(ref Lost[3], count * (long)Load.Full);

            var prices = owner.State.Prices;
            var owners = _world.Holdings.OwnersIn(country, building);

            // Доли хозяев одни на все товары рецепта — считаем их раз, а не на каждый.
            var total = 0L;
            foreach (var company in owners) total += company.CountOf(building);

            // Сырьё покупают, а не берут даром. Не хватает денег — завод работает вполсилы:
            // это и есть настоящее ограничение для компании, у которой пусто в кассе.
            if (owners.Count > 0 && total > 0)
            {
                var bill = default(Money);
                foreach (var (good, amount) in recipe.Inputs)
                {
                    bill += prices.CostOf(good, amount * runs / Load.Full);
                }

                if (bill.Raw > 0)
                {
                    var purse = default(Money);
                    foreach (var company in owners) purse += company.Cash;

                    if (purse < bill)
                    {
                        var poorer = (long)((Int128)runs * purse.Raw / bill.Raw);
                        Interlocked.Add(ref Lost[2], runs - poorer);
                        runs = poorer;
                    }

                    if (runs <= 0) continue;
                }
            }

            // Приведение безопасно: runs не может превысить count, с которого начали.
            var consumed = owner.State.Stock.TryConsume(recipe.Inputs, runs);
            if (!consumed) throw new InvalidOperationException("Не получилось потратить предметы " +
                                                               "со склада, ошибка в расчетах в коде");

            foreach (var (good, amount) in recipe.Inputs)
            {
                // То же выражение, что внутри TryConsume: расход должен совпасть до доли.
                var used = amount * runs / Load.Full;
                _consumed.Add(country, good, used);

                if (owners.Count > 0 && total > 0) Draw(owner, good, used, ask => Share(owners, building, total, ask));
            }

            var times = _world.Efficiency.OutputTimes(country, recipe.Sector);

            foreach (var (good, amount) in recipe.Outputs)
            {
                var made = amount * runs / Load.Full;
                if (times != Efficiency.Scale) made = new GoodAmount(made.Raw * times / Efficiency.Scale);

                _outputs.Add(country, good, made);
                Credit(owners, building, total, good, made, prices.Of(good));
            }
        }
    }

    /// <summary>Сколько выручки от вывоза ждёт раздачи, по стране и товару.</summary>
    /// <remarks>Копится за тик и раздаётся разом: сделок за тик двадцать шесть тысяч, и
    /// делить на каждой стоило пятнадцать миллисекунд.</remarks>
    private readonly Money[] _earnedAbroad =
        new Money[256 * Enum.GetValues<GoodType>().Length];

    /// <summary>Раздаёт накопленную выручку от вывоза и обнуляет счёт.</summary>
    private void PayAbroad()
    {
        foreach (var country in _world.Countries)
        {
            foreach (var good in AllGoods)
            {
                var slot = Slot(country.Id, good);
                if (_earnedAbroad[slot].Raw <= 0) continue;

                PayExporters(country, good, _earnedAbroad[slot]);
                _earnedAbroad[slot] = default;
            }
        }
    }

    /// <summary>Делит выручку от вывоза между теми, чей товар уехал.</summary>
    /// <remarks>По долям на складе: кто держал больше, тот больше и вывез. Деньги новые —
    /// центробанк выпускает их под пришедшую валюту, и это не дыра в бюджете, а обычная
    /// его работа.</remarks>
    private void PayExporters(Country country, GoodType good, Money local)
    {
        if (local.Raw <= 0) return;

        var sellers = _sellersOf[Slot(country.Id, good)];
        if (sellers.Count == 0) return;

        var total = 0L;
        foreach (var seller in sellers) total += seller.Holds(good).Raw;
        if (total <= 0) return;

        country.Bank.Emit(local, EmissionKind.ForCurrency);

        var left = local.Raw;
        for (var i = 0; i < sellers.Count && left > 0; i++)
        {
            var mine = i == sellers.Count - 1
                ? left
                : Math.Min(left, (long)((Int128)local.Raw * sellers[i].Holds(good).Raw / total));

            sellers[i].Earn(new Money(mine));
            sellers[i].NoteSold(new Money(mine));
            left -= mine;
        }
    }

    /// <summary>Хозяева заводов складываются на покупку сырья, каждый по своей доле.</summary>
    private static Money Share(IReadOnlyList<Company> owners, BuildingType building, long total, Money ask)
    {
        if (ask.Raw <= 0 || total <= 0) return default;

        var paid = default(Money);
        foreach (var company in owners)
        {
            var mine = new Money((long)((Int128)ask.Raw * company.CountOf(building) / total));
            var gave = company.Give(mine);

            company.NoteBought(gave);
            paid += gave;
        }

        return paid;
    }

    /// <summary>Записывает сделанное на счёт тех, чьи это здания.</summary>
    /// <remarks>Делится по долям: у кого больше таких заводов, тому больше и записано.
    /// Остаток от округления достаётся последнему — иначе он копился бы в никуда.</remarks>
    private static void Credit(
        IReadOnlyList<Company> owners,
        BuildingType building,
        long total,
        GoodType good,
        GoodAmount made,
        Money price)
    {
        if (owners.Count == 0 || made.Raw <= 0 || total <= 0) return;

        var left = made.Raw;
        for (var i = 0; i < owners.Count && left > 0; i++)
        {
            var mine = i == owners.Count - 1
                ? left
                : Math.Min(left, (long)((Int128)made.Raw * owners[i].CountOf(building) / total));

            owners[i].Store(good, new GoodAmount(mine));
            owners[i].NoteMade(new Money((long)((Int128)price.Raw * mine / GoodAmount.Scale)));
            left -= mine;
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
            // Компании есть — платят они сами, из своей выручки. Касса страны остаётся
            // только для внешней торговли.
            if (_world.CompaniesOf(country.Id).Count > 0) continue;

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

    /// <summary>Запись об отказе платить: когда, кто и с какими числами.</summary>
    /// <param name="Owed">Внешний долг на день отказа.</param>
    /// <param name="Capacity">Чем страна могла платить за год.</param>
    /// <param name="Reserves">Что оставалось в резервах.</param>
    public readonly record struct Refusal(
        int Day,
        string Iso,
        Money Owed,
        Money Capacity,
        Money Reserves,
        Money ImportsPerDay,
        Money ExportsPerDay,
        Money Rate);

    /// <summary>Все отказы платить за партию. Нужны, чтобы понять, откуда они берутся:
    /// само число их к пятому году гуляет от сорока до ста десяти при неизменных
    /// правилах, и чинить его вслепую нечего.</summary>
    public IReadOnlyList<Refusal> Refusals => _refusals;

    private readonly List<Refusal> _refusals = [];

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

                // Налоги сидят в цене: человек платит их, сам того не замечая, а продавцу
                // достаётся меньше. Оттого высокий НДС и бьёт по спросу — на те же деньги
                // покупают меньше. Акциз берём здесь же, поверх НДС.
                var toll = Excisable(good) ? country.Taxes.Excise : 0;
                var (bought, cost) = Draw(country, good, wanted, ask =>
                {
                    var excise = TaxCode.Take(ask, toll);
                    var paid = country.Households.SpendUpTo(ask + excise);
                    var got = ask.Raw + excise.Raw > 0
                        ? new Money((long)((Int128)excise.Raw * paid.Raw / (ask + excise).Raw))
                        : default;

                    country.Budget.Collect(TaxKind.Excise, got);

                    return paid - got;
                }, taxed: true);

                _sales[country.Id] = _sales.GetValueOrDefault(country.Id) + cost;
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
    private void Spend(Country country)
    {
        // Сперва школы и больницы, потом оружие: содержание идёт вперёд закупок.
        var before = _sales.GetValueOrDefault(country.Id);

        Buy(country, GoodType.Services, _stateWants.Get(country.Id, GoodType.Services),
            cost => country.Budget.SpendUpTo(cost));

        _stateBought[country.Id] = _sales.GetValueOrDefault(country.Id) - before;

        Arm(country);
    }

    /// <summary>Сколько государство купило услуг за тик.</summary>
    public Money StateServicesOf(byte country) => _stateBought.GetValueOrDefault(country);

    private readonly Dictionary<byte, Money> _stateBought = new();

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
    /// <param name="taxed">Берётся ли НДС. Государство с себя его не берёт — деньги ушли
    /// бы из бюджета в бюджет; а жильё и стройка платят, как и в жизни.</param>
    private GoodAmount Buy(
        Country country, GoodType good, GoodAmount wanted, Func<Money, Money> purse, bool taxed = false)
    {
        if (wanted.Raw <= 0) return default;

        // Берём у самых дешёвых и платим каждому его цену: продавцов в стране много.
        var (take, cost) = Draw(country, good, wanted, purse, taxed);
        if (take.Raw <= 0) return default;

        country.State.Stock.TakeUpTo(good, take);
        _bought.Add(country.Id, good, take);
        _sales[country.Id] = _sales.GetValueOrDefault(country.Id) + cost;

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
                    cost => country.Households.SpendUpTo(cost), taxed: true);

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

        Perish();
    }

    /// <summary>Услуги не лежат на складе: неоказанная стрижка назавтра пропадает.</summary>
    /// <remarks>
    /// Остаётся ровно сегодняшний выпуск — его и потребят завтра, — а что не съели раньше,
    /// гаснет. Пока услуги копились как товар, их набиралось сто восемь миллионов единиц
    /// при суточном выпуске в два миллиона: полсотни дней запаса того, что хранить нельзя.
    /// В приросте мировых запасов они и составляли большую часть.
    /// </remarks>
    private void Perish()
    {
        foreach (var country in _world.Countries)
        {
            var stock = country.State.Stock;
            var fresh = _outputs.Get(country.Id, GoodType.Services);
            var lying = stock.Of(GoodType.Services);

            if (lying > fresh) stock.TakeUpTo(GoodType.Services, lying - fresh);
        }
    }

    /// <summary>Цены двигаются в конце тика: спрос за тик против того запаса, что был
    /// на его начало.</summary>
    /// <summary>Ставит цену, при которой спрос сходится с предложением.</summary>
    /// <remarks>
    /// Не двигает вчерашнюю, а считает заново от обычной — оттого цене и не от чего
    /// раскачиваться. Прежде она ползла шагами вслед за покрытием склада, а склад копил
    /// выпуск, который сам зависел от цены: круг с задержкой в тик, который расходится сам
    /// собой. Это паутинообразная модель из учебника, и подбором шага она не лечится.
    ///
    /// Сравниваются потоки: сколько просят за сутки против того, сколько за сутки можно
    /// дать. Запас входит в предложение лишь той частью, что выше нормы, и растянутой на
    /// ту же норму, — иначе полный склад ронял бы цену во столько же раз, во сколько
    /// пустой её поднимал.
    /// </remarks>
    private void MovePrices()
    {
        foreach (var country in _world.Countries)
        {
            var prices = country.State.Prices;

            foreach (var good in AllGoods)
            {
                var wanted = _inputs.Get(country.Id, good);
                var kept = _available.Get(country.Id, good);
                var norm = new GoodAmount(wanted.Raw * Prices.TargetCoverDays);
                var spare = kept > norm
                    ? new GoodAmount((kept - norm).Raw / Prices.TargetCoverDays)
                    : default;

                prices.SetTo(good, Clearing.Price(
                    prices.StartOf(good),
                    wanted,
                    PotentialOutputOf(country.Id, good) + spare,
                    _world.Elasticity.Demand(good),
                    _world.Elasticity.Supply(good)));
            }
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

    /// <summary>Сколько страна дала бы, работай все её предприятия на полную.</summary>
    /// <remarks>
    /// Потенциальный выпуск: здания умножены на рецепт и на умение страны, без оглядки на
    /// то, хватило ли сырья, рук и денег. На старте он совпадает с настоящим ВВП страны —
    /// оттуда и выведено число заводов, — а дальше расходится с фактическим ровно на то,
    /// что модель теряет по дороге.
    ///
    /// Отношение факта к нему — загрузка мощностей, в жизни около трёх четвертей. Это и
    /// есть мера, по которой видно, здорова ли экономика: не «сколько вышло», а «сколько
    /// могло выйти и почему не вышло».
    /// </remarks>
    public Money PotentialOf(byte country, Prices prices)
    {
        var total = default(Money);

        foreach (var building in AllBuildings)
        {
            var count = _working.Get(country, building);
            if (count == 0) continue;

            var recipe = _world.Buildings[building];
            var times = _world.Efficiency.OutputTimes(country, recipe.Sector);

            foreach (var (good, amount) in recipe.Outputs)
            {
                var made = new GoodAmount(amount.Raw * count);
                if (times != Efficiency.Scale) made = new GoodAmount(made.Raw * times / Efficiency.Scale);

                total += prices.CostOf(good, made);
            }

            foreach (var (good, amount) in recipe.Inputs)
            {
                total -= prices.CostOf(good, new GoodAmount(amount.Raw * count));
            }
        }

        return total;
    }

    /// <summary>Сколько товара дали бы все предприятия страны на полной загрузке.</summary>
    public GoodAmount PotentialOutputOf(byte country, GoodType good)
    {
        var total = default(GoodAmount);

        foreach (var building in AllBuildings)
        {
            var count = _working.Get(country, building);
            if (count == 0) continue;

            var recipe = _world.Buildings[building];
            if (!recipe.Outputs.TryGetValue(good, out var amount)) continue;

            var times = _world.Efficiency.OutputTimes(country, recipe.Sector);
            var made = new GoodAmount(amount.Raw * count);

            total += times == Efficiency.Scale
                ? made
                : new GoodAmount(made.Raw * times / Efficiency.Scale);
        }

        return total;
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

            // Возведённое — тоже выпуск, и не считать его нельзя: материалы стройки уже
            // вычтены строкой выше как потраченные. Без этого чем больше страна строит,
            // тем ниже её измеренный ВВП, а в жизни стройка и есть валовое накопление.
            total += prices.CostOf(good, _raised.Get(country, good));
        }

        return total;
    }

    /// <summary>Во что обошлось возведённое за тик, по товарам рецепта стройки.</summary>
    private readonly Tally<GoodType, GoodAmount> _raised = new();

    /// <summary>Изношенное разваливается. Считается остатком: за срок службы должен
    /// осыпаться весь капитал, а по одному заводу в тик этого не набрать.</summary>
    /// <summary>Все здания страны по типам. Для стран без компаний: считать износ больше
    /// не по чему.</summary>
    private Dictionary<BuildingType, int> BuildingsIn(byte country)
    {
        var all = new Dictionary<BuildingType, int>();
        foreach (var region in _world.RegionsOf(country))
        {
            foreach (var (type, count) in region.BuildingsCount)
            {
                all[type] = all.GetValueOrDefault(type) + region.BuildingsOf(type, country);
            }
        }

        return all;
    }

    /// <summary>Сколько материала уйдёт на замену изношенного, по стране и товару.</summary>
    private readonly Tally<GoodType, GoodAmount> _replace = new();

    /// <summary>Во что обходится износ зданий за сутки — амортизация.</summary>
    /// <remarks>
    /// Здание живёт свой срок и рушится, а на замену никто не откладывал: в затратах
    /// износа не было вовсе. Оттого и выходил круг — выпуск сбавляется до нынешнего
    /// расхода, вместе с ним падает добавленная стоимость, из неё же берётся стройка, и
    /// капитал тает. В замкнутом мире это видно начисто: тысяча шахт превращалась в
    /// триста шестьдесят восемь за двадцать лет, хотя выпуска хватало с запасом.
    ///
    /// Считается по нынешним ценам постройки: заменять придётся по ним, а не по тем, что
    /// были при закладке.
    /// </remarks>
    private Money WearCost(Country country, IReadOnlyDictionary<BuildingType, int> what)
    {
        var total = default(Money);

        foreach (var (type, count) in what)
        {
            if (count <= 0) continue;

            var info = _world.Buildings[type];
            if (info.BuildCost.Count == 0) continue;

            var price = Construction.CostOf(info.BuildCost, country.State.Prices);
            total += new Money(price.Raw * count / (info.LifeYears * DaysInYear));
        }

        return total;
    }

    /// <summary>Сколько зданий поднято и сколько рухнуло за всю игру. Только для замера.</summary>
    public long BuiltSoFar { get; private set; }

    public long WornSoFar { get; private set; }

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

            var gone = Math.Min(due, most);
            if (!region.TryRemoveBuildings(worst, gone)) continue;

            WornSoFar += gone;
            Strip(region.Id, worst, gone);
        }
    }

    /// <summary>Списывает рухнувшее у тех, кому оно принадлежало.</summary>
    /// <remarks>
    /// Износ рушит здания на счету области, а счёт компании об этом не знал вовсе: за пять
    /// лет мир терял миллион предприятий, и ни одно из них не пропадало ни у одного
    /// хозяина. Пока прибыль делилась по числу зданий, это было незаметно; как только
    /// компания станет владельцем товара, расхождение станет ошибкой в деньгах.
    ///
    /// Делится потеря по долям: у кого больше стояло, у того больше и рухнуло.
    /// </remarks>
    private void Strip(int region, BuildingType type, int gone)
    {
        var owners = _world.Holdings.OwnersOf(region, type);
        if (owners.Count == 0) return;

        var total = 0L;
        foreach (var company in owners) total += company.CountAt(region, type);
        if (total <= 0) return;

        var left = gone;

        // Копия: Remove вычёркивает опустевших из того же списка.
        foreach (var company in owners.ToArray())
        {
            if (left <= 0) break;

            var mine = (int)((long)gone * company.CountAt(region, type) / total);
            left -= company.Remove(region, type, Math.Min(mine, left));
        }

        // Остаток от округления — первому, у кого ещё есть.
        foreach (var company in owners.ToArray())
        {
            if (left <= 0) break;

            left -= company.Remove(region, type, left);
        }
    }

    /// <summary>Компании рассчитываются за день: зарплата, налоги, дивиденды.</summary>
    /// <remarks>
    /// Здесь и замыкается круг. Компания знает, сколько продала и сколько купила, — разница
    /// и есть то, что она добавила. Доля труда из неё идёт рабочим, с остатка берётся налог
    /// на прибыль, четверть чистого она оставляет на развитие, остальное отдаёт владельцам.
    ///
    /// Не хватило денег на зарплату — платит сколько есть. Это не ошибка, а задержка выплат:
    /// в жизни так и бывает у фирмы, чей товар не покупают.
    ///
    /// Идёт после стройки: стройка тоже покупка, и её надо учесть в дневном расходе.
    /// </remarks>
    private void Settle()
    {
        foreach (var country in _world.Countries)
        {
            var companies = _world.CompaniesOf(country.Id);
            if (companies.Count == 0) continue;

            var wages = default(Money);

            foreach (var company in companies)
            {
                if (!company.Alive) continue;

                // За сделанное, а не за проданное. Товар, легший на склад, — это тоже
                // работа, и труд за неё оплачен: в жизни наниматель не задерживает зарплату
                // до дня продажи, он берёт из кассы или в долг.
                //
                // Пока платили за проданное, круг не замыкался: выпуск шёл на склад, денег
                // людям не доставалось, купить они не могли, склад рос ещё быстрее, и
                // правило загрузки глушило заводы. Первые три месяца мир работал на 85%
                // мощности, а дальше садился на 52%.
                var added = company.SoldToday - company.BoughtToday;
                if (added.Raw <= 0) continue;

                // Сперва откладывают на развитие, потом платят. Деньгами приходит только
                // за проданное, а начисляется за сделанное, и зарплата съедала кассу
                // подчистую: у всех крупных компаний стоял ноль, строить было не на что —
                // семь миллионов отказов «нет денег» за партию.
                //
                // Порядок тут и есть решение: возмещение капитала — не остаток после всех
                // выплат, а первая статья расхода. Проели его — завтра работать не на чем.
                Interlocked.Add(ref Flows[0], added.Raw);

                var toGrow = new Money(added.Raw * Construction.InvestmentShare / 100);
                var spare = company.Cash > toGrow ? company.Cash - toGrow : default;

                // Труд стоит работодателю всё, что он на него потратил: и зарплату, и
                // взносы, и удержанный подоходный. Делится эта сумма, а не прибавляется
                // сверху — иначе взносы упирались бы в пустую кассу.
                var owed = new Money(added.Raw * country.LabourShare / 100);
                var given = company.Give(owed < spare ? owed : spare);
                var dues = TaxCode.Take(given, country.Taxes.Payroll);
                var onHand = given - dues;
                var income = TaxCode.Take(onHand, country.Taxes.Income);

                Interlocked.Add(ref Flows[1], given.Raw);
                country.Budget.Collect(TaxKind.Payroll, dues);
                country.Budget.Collect(TaxKind.Income, income);
                country.Households.Earn(onHand - income);
                wages += given;

                var profit = added - given;
                if (profit.Raw <= 0) continue;

                country.Budget.Collect(
                    TaxKind.Profit, company.Give(TaxCode.Take(profit, country.Taxes.Profit)));

                // На развитие — четверть добавленной стоимости, остальное владельцам.
                // Владелец — население: акций и биржи пока нет.
                //
                // Доля считается от добавленной стоимости, а не от прибыли: в жизни
                // валовое накопление — четверть ВВП, а прибыль сама по себе меньше
                // половины его. Отсчёт от прибыли оставлял на стройку семь процентов ВВП —
                // вдвое меньше, чем нужно на одно возмещение износа, и мир терял по три
                // процента предприятий в год.
                var net = profit - TaxCode.Take(profit, country.Taxes.Profit);
                var want = new Money(added.Raw * Construction.InvestmentShare / 100
                    * country.Character.Invests / Character.Usual);

                // Но не меньше износа своих зданий: это не прибыль, а возврат вложенного,
                // и раздавать его владельцам значит проедать завод.
                var wear = WearCost(country, company.ByType);
                Interlocked.Add(ref Flows[4], wear.Raw);
                if (wear > want) want = wear;

                var keep = net < want ? net : want;
                var owners = net - keep;

                // Владельцам достаётся только то, что есть в кассе сверх отложенного на
                // стройку. Прежде этой защиты не было: запас вычитался из зарплат, а следом
                // дивиденды выгребали кассу до дна вместе с ним. Оттого у компаний и стоял
                // ноль — двадцать семь миллионов отказов «нет денег» за партию, — и мир
                // строил ровно столько, сколько изнашивал.
                var loose = company.Cash > toGrow ? company.Cash - toGrow : default;
                if (owners > loose) owners = loose;

                Interlocked.Add(ref Flows[2], owners.Raw);
                country.Households.Earn(company.Give(owners));
            }

            _wages[country.Id] = wages;
            country.Payroll = wages;
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
            // Где есть компании, там прибыль делят они сами — см. Settle.
            if (_world.CompaniesOf(country.Id).Count > 0) continue;

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

    /// <summary>Берёт товар у самых дешёвых продавцов страны и считает, во что он обошёлся.</summary>
    /// <remarks>
    /// Ради этого и затевался именной склад. Покупатель идёт по продавцам от дешёвого к
    /// дорогому и берёт, пока не наберёт своё или пока не кончатся деньги. Дешёвый продаёт
    /// больше штук и зарабатывает меньше на каждой — этот размен и есть конкуренция.
    ///
    /// Деньги по-прежнему идут в кассу страны, а не прямо продавцу: зарплату платят из неё
    /// же, и разводить эти потоки — отдельная работа. Но кто именно продал, теперь известно,
    /// и прибыль делится по этому.
    /// </remarks>
    /// <returns>Сколько взято и сколько это стоит по ценам продавцов.</returns>
    private (GoodAmount Took, Money Cost) Draw(
        Country country, GoodType good, GoodAmount want, Func<Money, Money> purse, bool taxed = false)
    {
        var usual = country.State.Prices.Of(good);
        if (want.Raw <= 0 || usual.Raw <= 0) return (default, default);

        var sellers = _sellersOf[Slot(country.Id, good)];
        if (sellers.Count == 0)
        {
            // Хозяев нет — берём со склада по общей цене, как было до компаний. Так живут
            // тестовые миры и страны, у которых компании все разорились.
            var plain = country.State.Stock.Of(good);
            var takePlain = want < plain ? want : plain;
            var askPlain = country.State.Prices.CostOf(good, takePlain);
            var gotPlain = purse(askPlain);

            if (gotPlain < askPlain && askPlain.Raw > 0)
            {
                takePlain = new GoodAmount((long)((Int128)takePlain.Raw * gotPlain.Raw / askPlain.Raw));
            }

            country.State.Treasury.Receive(gotPlain);

            return (takePlain, gotPlain);
        }

        var left = want.Raw;
        var took = 0L;
        var cost = default(Money);

        foreach (var seller in sellers)
        {
            if (left <= 0) break;

            var have = seller.Holds(good).Raw;
            if (have <= 0) continue;

            var mine = Math.Min(left, have);
            var price = new Money(usual.Raw * seller.Edge(good) / Company.Even);
            var ask = new Money((long)((Int128)price.Raw * mine / GoodAmount.Scale));
            var vat = taxed ? TaxCode.Take(ask, country.Taxes.Vat) : default;

            var paid = purse(ask + vat);
            if (paid.Raw <= 0) break;

            // Заплатили меньше — и взяли меньше: в долг продавец не отпускает.
            if (paid < ask + vat) mine = (long)((Int128)mine * paid.Raw / (ask + vat).Raw);
            if (mine <= 0) break;

            var got = new Money((long)((Int128)vat.Raw * paid.Raw / (ask + vat).Raw));

            country.Budget.Collect(TaxKind.Vat, got);
            seller.Take(good, new GoodAmount(mine));
            seller.NoteSold(paid - got);
            seller.Earn(paid - got);

            took += mine;
            left -= mine;
            cost += paid;

            if (paid < ask + vat) break;
        }

        return (new GoodAmount(took), cost);
    }

    /// <summary>Кто в стране продаёт этот товар, от дешёвого к дорогому. Плоским массивом,
    /// а не словарём: перебирается он весь и каждый тик.</summary>
    private readonly List<Company>[] _sellersOf =
        [.. Enumerable.Range(0, 256 * Enum.GetValues<GoodType>().Length).Select(_ => new List<Company>())];

    private static int Slot(byte country, GoodType good) =>
        country * Enum.GetValues<GoodType>().Length + (int)good;

    /// <summary>Сравнения по цене — по одному на товар, а не новое на каждый вызов.</summary>
    private static readonly Comparison<Company>[] Cheapest =
        [.. Enum.GetValues<GoodType>().Select<GoodType, Comparison<Company>>(
            good => (a, b) => a.Edge(good) != b.Edge(good) ? a.Edge(good) - b.Edge(good) : a.Id - b.Id)];

    /// <summary>Пересобирает очередь продавцов и двигает их цены.</summary>
    /// <remarks>Дешевеет тот, у кого товар залежался против общего по стране; дорожает тот,
    /// у кого его выметают. Сравнение с соседями, а не с собственным вчера: иначе цена
    /// уезжала бы у всех разом, а это работа общего механизма, не компаний.</remarks>
    private void Rank()
    {
        foreach (var list in _sellersOf) list.Clear();

        foreach (var country in _world.Countries)
        {
            var companies = _world.CompaniesOf(country.Id);
            if (companies.Count == 0) continue;

            var from = Slot(country.Id, default);
            foreach (var company in companies)
            {
                if (company.Alive) company.OfferTo(_sellersOf, from);
            }
        }

        foreach (var country in _world.Countries)
        {
            foreach (var good in AllGoods)
            {
                // Очередь пересобираем не каждый день: отклонения ходят по единице из сотни,
                // и порядок за сутки почти не меняется. Покупатель и в жизни не обзванивает
                // всех поставщиков каждое утро.
                Price(_sellersOf[Slot(country.Id, good)], good, _day % SortEvery == (int)good % SortEvery);
            }
        }

        // Счёт продаж обнуляем последним: по нему только что двигались цены, а до того —
        // делилась прибыль.
        foreach (var company in _world.Companies) company.ForgetSold();
    }

    /// <summary>Двигает цены продавцов одного товара и выстраивает их от дешёвого.</summary>
    /// <summary>Раз во сколько тиков продавцов перестраивают по цене.</summary>
    private const int SortEvery = 4;

    private static void Price(List<Company> sellers, GoodType good, bool sort)
    {
        if (sellers.Count == 0) return;

        // Общее покрытие страны: сколько у всех лежит против того, сколько все продали.
        var stock = 0L;
        var sold = 0L;
        foreach (var seller in sellers)
        {
            stock += seller.Holds(good).Raw;
            sold += seller.SoldToday.Raw;
        }

        foreach (var seller in sellers)
        {
            var mine = seller.Holds(good).Raw;
            var mySold = seller.SoldToday.Raw;

            // Залежался против общего — дешевеет, выметают — дорожает, а кто идёт вровень со
            // всеми, тот возвращается к общей цене. Без возврата отклонения разбредались по
            // краям все разом: у идущих вровень сравнение решало одинаково.
            var slow = (Int128)mine * sold * 100 > (Int128)stock * mySold * (100 + Gap);
            var fast = (Int128)mine * sold * (100 + Gap) < (Int128)stock * mySold * 100;

            seller.MoveEdge(good, slow ? -1 : fast ? 1 : seller.Edge(good) > Company.Even ? -1 : 1);
        }

        // Сдвигаем всех так, чтобы средняя по складу осталась общей ценой страны: её двигают
        // покрытие и якорь, и компаниям не полагается уводить её за собой.
        Level(sellers, good, stock);

        if (sort) sellers.Sort(Cheapest[(int)good]);
    }

    /// <summary>Насколько надо разойтись в покрытии, чтобы двигать цену, в процентах.</summary>
    private const int Gap = 5;

    /// <summary>Сдвигает отклонения так, чтобы средняя по товару осталась сотней.</summary>
    private static void Level(List<Company> sellers, GoodType good, long stock)
    {
        if (stock <= 0) return;

        var weighted = (Int128)0;
        foreach (var seller in sellers) weighted += (Int128)seller.Edge(good) * seller.Holds(good).Raw;

        var average = (int)(weighted / stock);
        if (average == Company.Even) return;

        foreach (var seller in sellers) seller.MoveEdge(good, Company.Even - average);
    }

    /// <summary>Сводит именные доли со складом страны и обнуляет дневной выпуск.</summary>
    /// <remarks>Один проход по компаниям вместо учёта в каждом месте, откуда берут со
    /// склада: заводы, население, стройка, армия, жильё, дороги и вывоз — семь мест, и
    /// протягивать через все именной учёт значило бы переписать пол-модели.</remarks>
    private void Balance() => Parallel.ForEach(_world.Countries, FitIn);

    private void FitIn(Country country)
    {
        var companies = _world.CompaniesOf(country.Id);
        if (companies.Count == 0) return;

        // На стеке, а не в поле: страны считаются разом, и общий буфер они бы затёрли.
        Span<long> mine = stackalloc long[AllGoods.Length];
        Span<long> have = stackalloc long[AllGoods.Length];

        foreach (var good in AllGoods) have[(int)good] = country.State.Stock.Of(good).Raw;

        foreach (var company in companies)
        {
            company.ForgetMade();
            company.ForgetBought();
            company.AddTo(mine);
        }

        foreach (var company in companies) company.FitAll(have, mine);
    }


    /// <summary>Что за тик ушло владельцам.</summary>
    public Money ProfitOf(byte country) => _profits.GetValueOrDefault(country);

    /// <summary>Делит заработанное между компаниями и владельцами.</summary>
    /// <remarks>
    /// Доля компании — то, что она сегодня и правда сделала, а не число её зданий и даже
    /// не их мощность. Разница не косметическая: завод, простоявший день без сырья, ничего
    /// не заработал, а рудник и завод микроэлектроники до этого считались одинаково.
    ///
    /// Считается по именному складу: выпуск записывается на счёт хозяев зданий там же, где
    /// и делается.
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

        var total = (Int128)0;
        foreach (var company in companies)
        {
            if (company.Alive) total += company.SoldToday.Raw;
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

            var share = new Money((long)((Int128)earned.Raw * company.SoldToday.Raw / total));

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

        // Занимают ровно под то, что сегодня не продалось: сырьё на него ушло, а выручка не
        // пришла. Прежде брали под весь дневной выпуск, и каждый день заново — долг рос сам
        // себя до шестисот сорока триллионов, и компании разорялись пачками на пятнадцатом
        // году.
        if (company.Cash < company.MadeToday && company.MadeToday.Raw > 0)
        {
            var gap = company.MadeToday - company.Cash;
            var limit = new Money(yearly.Raw * BrokeAt) - company.Debt;

            if (gap > limit) gap = limit;
            if (gap > bank.Free) gap = bank.Free;
            if (gap.Raw > 0 && bank.Lend(gap)) company.Borrow(gap);
        }

        // Занимает, если на стройку не хватает своего, а дело того стоит.
        if (!_choice.TryGetValue(company.Id, out var plan)) return;

        var price = Construction.CostOf(_world.Buildings[plan.Type].BuildCost, country.State.Prices);
        if (price.Raw <= 0) return;

        // Занимает не на одно здание, а на столько, сколько сегодня позволит склад: стройка
        // упиралась в кассу, касса — в непроданные запасы, запасы — в несостоявшуюся
        // стройку. Больше, чем можно поднять, брать незачем — материалов всё равно нет.
        var fits = Math.Max(1, RoomFor(country.Id, plan.Type));
        var whole = new Money(price.Raw * fits);
        if (company.Cash >= whole) return;

        var want = whole - company.Cash;
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

    /// <summary>Кто в стране может строить: живые компании, богатые вперёд. Компаний нет
    /// вовсе — строит государство, и это единственный «строитель» из пустоты.</summary>
    private IEnumerable<Company?> Builders(byte country)
    {
        var all = _world.CompaniesOf(country);
        if (all.Count == 0) return [null];

        _lineUp.Clear();
        foreach (var company in all)
        {
            if (company.Alive) _lineUp.Add(company);
        }

        if (_lineUp.Count == 0) return [null];

        _lineUp.Sort((a, b) => b.Cash.Raw.CompareTo(a.Cash.Raw));

        return _lineUp;
    }

    private readonly List<Company> _lineUp = [];

    /// <summary>Сколько зданий типа ещё поместится на склад после того, что уже заказано
    /// стройкой за этот тик.</summary>
    /// <remarks>Строителей за тик несколько, и полка у них одна. Считать каждому от
    /// полного склада значило заявить спрос, которого нет: заказывали вдвое больше, чем
    /// поднимали, и выдуманная заявка гнала цену материалов в потолок коридора.</remarks>
    private int RoomFor(byte country, BuildingType type)
    {
        var stock = _world.CountryById(country).State.Stock;
        var most = MaxBuildsPerTick;

        foreach (var (good, amount) in _world.Buildings[type].BuildCost)
        {
            if (amount.Raw <= 0) continue;

            var free = stock.Of(good).Raw * BiteOfStock / 100 - _build.Get(country, good).Raw;
            most = (int)Math.Min(most, Math.Max(0, free) / amount.Raw);
        }

        return most;
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
                    var wear = WearCost(country, BuildingsIn(country.Id));
                    if (wear > share) share = wear;

                    _investment[country.Id] = _investment.GetValueOrDefault(country.Id) + share;
                    _saved[country.Id] = share;
                }
            }

            if (!_plan.TryGetValue(country.Id, out var plans) || plans.Count == 0) continue;

            var raised = 0;
            var hands = 0L;

            foreach (var plan in plans)
            {
            var builder = plan.Builder;
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
                foreach (var (good, amount) in info.BuildCost)
                {
                    _consumed.Add(country.Id, good, amount);
                    _raised.Add(country.Id, good, amount);
                }

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
                    // Материалы стройка покупает с НДС, как и всякий покупатель.
                    var vat = TaxCode.Take(forStuff, country.Taxes.Vat);

                    country.Budget.Collect(TaxKind.Vat, vat);
                    country.State.Treasury.Receive(forStuff - vat);
                    _sales[country.Id] = _sales.GetValueOrDefault(country.Id) + forStuff - vat;
                }

                Interlocked.Add(ref Flows[3], price.Raw);
                done++;
                BuiltSoFar++;
            }

            raised += done;
            hands += _world.Efficiency.HandsFor(
                country.Id, info.Sector, (long)info.BuildWorkers * done / Construction.BuildDays);

            if (_ordered.TryGetValue(country.Id, out var order) && order.Type == plan.Type)
            {
                // Заказ держится, пока вложенное игроком не израсходовано. Построенное сверх
                // него — это уже обычные деньги страны.
                var spent = new Money(price.Raw * done);

                if (order.Left > spent) _ordered[country.Id] = (order.Type, order.Left - spent);
                else _ordered.Remove(country.Id);
            }
            }

            _builders[country.Id] = hands;
            _raisedToday[country.Id] = raised;
        }
    }

    /// <summary>Сколько зданий страна подняла за последний тик.</summary>
    public int RaisedIn(byte country) => _raisedToday.GetValueOrDefault(country);

    private readonly Dictionary<byte, int> _raisedToday = new();

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
            if (profit.Raw <= 0)
            {
                Stall[5]++;
                continue;
            }

            // По карману не смотрим: выбор дела — это выбор дела, а недостающее занимают.
            // Пока смотрели, выходил круг: денег нет — ниша не выбрана — плана нет — заём
            // не под что брать — денег так и нет. Сорок восемь миллионов отказов за партию
            // против одиннадцати по убыточности.
            //
            // Сколько построится на самом деле, решает PlanBuilds: там кошелёк и режет
            // число зданий.
            if (Construction.CostOf(info.BuildCost, country.State.Prices) > purse) Stall[6]++;

            // Чистая прибыль: из выручки за вычетом сырья вычитаем ещё и плату работникам.
            var hands = _world.Efficiency.HandsFor(country.Id, info.Sector, info.OptimalWorkers);
            var pay = new Money(country.Payroll.Raw / Math.Max(1, EmployedIn(country.Id)) * hands);
            if (pay >= profit)
            {
                Stall[5]++;
                continue;
            }

            var value = Construction.Payback(
                profit - pay, Construction.CostOf(info.BuildCost, country.State.Prices), info.LifeYears);

            // Пока не выбрано ничего, берём любое прибыльное: отдача на работника у
            // большого завода делится в ноль, и страна с полной казной решала, что строить
            // нечего вовсе. Это и поймал SteadyStateTests.CapitalHoldsItsGround.
            if (value <= bestValue && best is not null) continue;

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
