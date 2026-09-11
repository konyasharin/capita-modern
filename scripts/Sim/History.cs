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
    }

    private readonly Dictionary<Line, List<float>> _lines = [];

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
    }

    private void Put(Line line, float value)
    {
        var points = _lines[line];

        points.Add(value);
        if (points.Count > Days) points.RemoveRange(0, points.Count - Days);
    }
}
