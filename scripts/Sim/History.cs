using CapitaModern.Core.Economy;
using Godot;

/// <summary>Что с чем происходило по дням. Панели рисуют графики отсюда: модель хранит
/// только сегодняшнее состояние, а «стало хуже или лучше» по одному числу не увидеть.</summary>
public partial class History : Node
{
    /// <summary>Сколько дней помним. Десять лет: дальше партия обычно и не идёт, а память
    /// на десяток рядов по паре тысяч чисел ничего не стоит.</summary>
    private const int Days = 3650;

    public enum Line
    {
        Gdp,
        Inflation,
        Treasury,
        Debt,
        Employment,
        Load,
        Exports,
        Imports,
        Savings,
        Supply,
        Rate,
    }

    /// <summary>За сколько дней помним цены. По ним видно не уровень, а рывок: месяц —
    /// достаточный срок, чтобы отличить скачок от дневной ряби.</summary>
    public const int Month = 30;

    /// <summary>Дней в году. По нему считается годовая инфляция.</summary>
    public const int Year = 365;

    /// <summary>Дней в неделе.</summary>
    public const int Week = 7;

    /// <summary>С какого срока годовую инфляцию считают приведением к году. Раньше — просто
    /// рост с начала партии.</summary>
    public const int Enough = 90;

    private readonly Dictionary<Line, List<float>> _lines = [];

    /// <summary>Кольца снимков цен: наших и мировых, по одному на день, за год. Тридцать
    /// два числа в день — память на такое не жалко, а без истории не видно, что дорожает.</summary>
    private readonly List<float[]> _prices = [];
    private readonly List<float[]> _world = [];

    /// <summary>Кольца склада и заказа: по ним видно, отчего цена пошла — товар кончился
    /// или его вдруг стали больше просить.</summary>
    private readonly List<float[]> _stocks = [];
    private readonly List<float[]> _wants = [];

    private GameLoop _loop = null!;

    public override void _Ready()
    {
        _loop = GetNode<GameLoop>("/root/Game/GameLoop");
        _loop.Ticked += Sample;

        foreach (var line in Enum.GetValues<Line>()) _lines[line] = new List<float>(Days);

        // Первая точка до всякого хода: иначе график начинается с пустого места.
        Sample();
    }

    public IReadOnlyList<float> Of(Line line) => _lines[line];

    /// <summary>Значение ряда столько дней назад. Если истории меньше — самое раннее.</summary>
    public float Ago(Line line, int days)
    {
        var points = _lines[line];
        if (points.Count == 0) return 0;

        return points[Math.Max(0, points.Count - 1 - days)];
    }

    /// <summary>Годовая инфляция в процентах. Не «с начала партии»: за пять лет то число
    /// уходит в сотни процентов и перестаёт что-либо значить.</summary>
    /// <remarks>Когда года ещё не прошло, берётся весь срок и приводится к году сложным
    /// процентом — иначе первые месяцы показывали бы почти ноль.</remarks>
    public double Yearly()
    {
        var points = _lines[Line.Inflation];
        if (points.Count < 2) return 0;

        var days = Math.Min(points.Count - 1, Year);
        var now = 1 + points[^1] / 100.0;
        var was = 1 + points[points.Count - 1 - days] / 100.0;

        if (was <= 0 || now <= 0) return 0;

        var grew = now / was;

        // Приводить к году можно только с приличного срока. На второй день партии
        // показатель возводился в триста шестьдесят пятую степень, и процент дневного
        // движения превращался в тысячи процентов годовых.
        return (days >= Enough ? Math.Pow(grew, (double)Year / days) - 1 : grew - 1) * 100;
    }

    /// <summary>Недельная инфляция по дням: на сколько процентов подорожало всё за
    /// последние семь дней. По ней видно, когда именно начался разгон — на годовой это
    /// размазано по всему году.</summary>
    public IReadOnlyList<float> WeeklyLine()
    {
        var points = _lines[Line.Inflation];
        var line = new float[points.Count];

        for (var day = 1; day < points.Count; day++)
        {
            var back = Math.Min(day, Week);
            var now = 1 + points[day] / 100.0;
            var was = 1 + points[day - back] / 100.0;

            if (was > 0 && now > 0) line[day] = (float)((now / was - 1) * 100);
        }

        return line;
    }

    /// <summary>Годовая инфляция по дням — тем же правилом, что и сегодняшняя.</summary>
    public IReadOnlyList<float> YearlyLine()
    {
        var points = _lines[Line.Inflation];
        var line = new float[points.Count];

        for (var day = 0; day < points.Count; day++)
        {
            var back = Math.Min(day, Year);
            if (back == 0) continue;

            var now = 1 + points[day] / 100.0;
            var was = 1 + points[day - back] / 100.0;
            if (was <= 0 || now <= 0) continue;

            var grew = now / was;
            line[day] = (float)((back >= Enough ? Math.Pow(grew, (double)Year / back) - 1 : grew - 1) * 100);
        }

        return line;
    }

    /// <summary>Цена товара по дням. Ноль дней — вся история, что есть.</summary>
    public IReadOnlyList<float> PricesOf(GoodType good, int days = 0) => Slice(_prices, good, days);

    /// <summary>Наша цена к мировой по дням. Где мировой не было, стоит ноль.</summary>
    public IReadOnlyList<float> ToWorldOf(GoodType good)
    {
        var line = new float[_prices.Count];

        for (var day = 0; day < _prices.Count; day++)
        {
            var world = _world[day][(int)good];
            line[day] = world > 0 ? _prices[day][(int)good] / world : 0;
        }

        return line;
    }

    /// <summary>Во сколько раз цена товара ушла за месяц. Единица — не менялась.</summary>
    public double Jump(GoodType good)
    {
        if (_prices.Count < 2) return 1;

        var was = _prices[Math.Max(0, _prices.Count - 1 - Month)][(int)good];
        var now = _prices[^1][(int)good];

        return was > 0 ? now / was : 1;
    }

    /// <summary>На сколько процентов изменились склад и заказ за неделю.</summary>
    public (double Stock, double Want) WeekOf(GoodType good)
    {
        if (_stocks.Count < 2) return (0, 0);

        var back = Math.Max(0, _stocks.Count - 1 - Week);

        return (Change(_stocks[back][(int)good], _stocks[^1][(int)good]),
            Change(_wants[back][(int)good], _wants[^1][(int)good]));
    }

    private static double Change(float was, float now) => was > 0 ? (now / was - 1) * 100 : 0;

    private IReadOnlyList<float> Slice(List<float[]> rings, GoodType good, int days)
    {
        var from = days > 0 ? Math.Max(0, rings.Count - days) : 0;
        var line = new float[rings.Count - from];

        for (var day = 0; day < line.Length; day++) line[day] = rings[from + day][(int)good];

        return line;
    }

    private void Sample()
    {
        var sim = _loop.Simulation;
        var me = _loop.PlayerCountry;
        var id = _loop.Player;

        Put(Line.Gdp, (float)_loop.YearlyOutput);
        Put(Line.Inflation, (float)_loop.Inflation);
        Put(Line.Treasury, (float)_loop.Treasury);
        Put(Line.Debt, (float)_loop.ExternalDebt);
        Put(Line.Employment, (float)_loop.Employment);
        Put(Line.Load, (float)(sim.LoadIn(id) * 100.0 / Load.Full));
        Put(Line.Exports, (float)sim.ExportsOf(id).Exact);
        Put(Line.Imports, (float)sim.ImportsOf(id).Exact);
        Put(Line.Savings, (float)me.Households.Savings.Exact);
        Put(Line.Supply, (float)me.Bank.Supply.Exact);
        Put(Line.Rate, (float)me.ExchangeRate.Exact);

        var count = Enum.GetValues<GoodType>().Length;
        var prices = new float[count];
        var world = new float[count];
        var stocks = new float[count];
        var wants = new float[count];

        foreach (var good in Enum.GetValues<GoodType>())
        {
            prices[(int)good] = (float)me.State.Prices.Of(good).Exact;
            world[(int)good] = (float)_loop.World.Market.Prices.Of(good).Exact;
            stocks[(int)good] = (float)me.State.Stock.Of(good).Exact;
            wants[(int)good] = (float)sim.InputOf(id, good).Exact;
        }

        Ring(_prices, prices);
        Ring(_world, world);
        Ring(_stocks, stocks);
        Ring(_wants, wants);
    }

    private static void Ring(List<float[]> rings, float[] day)
    {
        rings.Add(day);
        if (rings.Count > Year) rings.RemoveRange(0, rings.Count - Year);
    }

    private void Put(Line line, float value)
    {
        var points = _lines[line];

        points.Add(value);
        if (points.Count > Days) points.RemoveRange(0, points.Count - Days);
    }
}
