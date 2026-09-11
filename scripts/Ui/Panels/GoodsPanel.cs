using CapitaModern.Core.Economy;
using Godot;

/// <summary>Все тридцать два товара: сколько выпущено, сколько заказано, что на складе и
/// куда ушла цена. Главная таблица игры — по ней видно, чего не хватает.</summary>
public partial class GoodsPanel : SidePanel
{
    private static readonly GoodType[] AllGoods = Enum.GetValues<GoodType>();

    public override string Title => "Товары";
    public override string Icon => "goods";

    private Bars _top = null!;
    private StatRow _made = null!;
    private StatRow _short = null!;
    private Table _table = null!;

    protected override void Build()
    {
        Section("Итоги за день", "output");
        _made = Stat("Выпущено, в своих ценах", "output");
        _short = Stat("Не хватило заказанного", "shortage");

        Section("Крупнейшие производства", "plants");
        _top = Bars.Create(Stack);
        Rows.AddChild(_top);

        Section("По товарам", "tab-goods");
        Note("Выпуск и заказ — за сутки, склад — остаток на начало дня. Столбец «к миру» — " +
            "наша цена к среднемировой: она складывается из настоящих сделок между странами.");

        _table = Table.Create(
        [
            new Column("Товар", 0, Right: false),
            new Column("Выпуск", 52),
            new Column("Заказ", 52),
            new Column("Склад", 52),
            new Column("Цена", 54),
            new Column("К миру", 52),
        ],
            stack: Stack,
            about: row => GoodCard.Of(Loop, Past, AllGoods[row]));

        Rows.AddChild(_table);
    }

    public override void Refresh()
    {
        var sim = Loop.Simulation;
        var prices = Me.State.Prices;
        var stock = Me.State.Stock;

        var made = default(Money);
        var missing = default(Money);
        var rows = new List<Cell[]>(AllGoods.Length);

        foreach (var good in AllGoods)
        {
            var output = sim.OutputOf(Id, good);
            var input = sim.InputOf(Id, good);
            var held = stock.Of(good);
            var price = prices.Of(good);
            var world = Loop.World.Market.Prices.Of(good);
            var lack = sim.ShortOf(Id, good);

            made += prices.CostOf(good, output);
            missing += prices.CostOf(good, lack);

            var times = world.Raw > 0 ? price.Exact / world.Exact : 0;

            rows.Add(
            [
                new Cell(Names.Of(good), (double)good, Skin.Text, Names.IconOf(good)),
                new Cell(Fmt.Amount(output), output.Exact, output.Raw > 0 ? Skin.Bright : Skin.Dim),
                new Cell(Fmt.Amount(input), input.Exact, lack.Raw > 0 ? Skin.Bad : Skin.Text),
                new Cell(Fmt.Amount(held), held.Exact, held.Raw > 0 ? Skin.Text : Skin.Dim),
                new Cell(Fmt.Price(price), price.Exact, Skin.Money),
                new Cell(times > 0 ? $"×{times:0.00}" : "—", times,
                    times > 0 ? Fmt.Sign(times - 1, moreIsBetter: false) : Skin.Dim),
            ]);
        }

        _top.Show(AllGoods
            .Select(good => (Good: good, Worth: prices.CostOf(good, sim.OutputOf(Id, good))))
            .Where(pair => pair.Worth.Raw > 0)
            .OrderByDescending(pair => pair.Worth.Raw)
            .Take(8)
            .Select(pair => new Slice(
                Names.Of(pair.Good), pair.Worth.Exact, Fmt.Cash(pair.Worth.Exact),
                Skin.Output, Names.IconOf(pair.Good), () => GoodCard.Of(Loop, Past, pair.Good)))
            .ToList());

        _made.Set(Fmt.Cash(made.Exact));
        _short.Set(Fmt.Cash(missing.Exact), missing.Raw > 0 ? Skin.Bad : Skin.Good);
        _table.Set(rows);
    }
}
