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

    /// <summary>С какого срока годовую инфляцию считают приведением к году. Раньше — просто
    /// рост с начала партии.</summary>
    public const int Enough = 90;

    private readonly Dictionary<Line, List<float>> _lines = [];

    /// <summary>Кольцо снимков цен: по одному на день, за последний месяц.</summary>
    private readonly List<float[]> _prices = [];

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

    /// <summary>Цена товара по дням за последний месяц.</summary>
    public IReadOnlyList<float> PricesOf(GoodType good)
    {
        var line = new float[_prices.Count];
        for (var day = 0; day < _prices.Count; day++) line[day] = _prices[day][(int)good];

        return line;
    }

    /// <summary>Во сколько раз цена товара ушла за месяц. Единица — не менялась.</summary>
    public double Jump(GoodType good)
    {
        if (_prices.Count < 2) return 1;

        var was = _prices[0][(int)good];
        var now = _prices[^1][(int)good];

        return was > 0 ? now / was : 1;
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

        var prices = new float[Enum.GetValues<GoodType>().Length];
        foreach (var good in Enum.GetValues<GoodType>()) prices[(int)good] = (float)me.State.Prices.Of(good).Exact;

        _prices.Add(prices);
        if (_prices.Count > Month) _prices.RemoveRange(0, _prices.Count - Month);
    }

    private void Put(Line line, float value)
    {
        var points = _lines[line];

        points.Add(value);
        if (points.Count > Days) points.RemoveRange(0, points.Count - Days);
    }
}
