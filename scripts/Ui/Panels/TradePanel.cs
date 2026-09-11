using CapitaModern.Core.Economy;
using Godot;

/// <summary>Внешняя торговля: сколько ввезли и вывезли, что закрыто запретом и до кого
/// вообще нет пути.</summary>
public partial class TradePanel : SidePanel
{
    private static readonly GoodType[] AllGoods = Enum.GetValues<GoodType>();

    public override string Title => "Торговля";
    public override string Icon => "trade";

    private Chart _flow = null!;
    private Bars _in = null!;
    private Bars _out = null!;

    private StatRow _exports = null!;
    private StatRow _imports = null!;
    private StatRow _balance = null!;
    private StatRow _yearly = null!;
    private StatRow _transit = null!;
    private StatRow _fuel = null!;

    private StatRow _unreachable = null!;
    private StatRow _tolls = null!;
    private StatRow _refused = null!;

    private Table _table = null!;

    protected override void Build()
    {
        Section("Как идёт торговля", "tab-trade");
        _flow = Graph("Вывоз и ввоз за день", Fmt.Cash);

        Section("За день", "output");
        _exports = Stat("Вывоз", "exports");
        _imports = Stat("Ввоз", "imports");
        _balance = Stat("Сальдо", "balance");
        _transit = Stat("Заработано на транзите", "transit");
        _fuel = Stat("Топливо на перевозку", "freight");

        Section("За год", "output");
        _yearly = Stat("Вывоз за год, скользящий", "exports");

        Section("Что ввозим", "alert-shortage");
        _in = Columns();

        Section("Что вывозим", "tab-trade");
        _out = Columns();

        Section("Доступ к рынкам", "tab-politics");
        _unreachable = Stat("Стран без пути", "routes");
        _tolls = Stat("Берут с нас пошлину", "tolls");
        _refused = Stat("Мир не купил из-за доставки", "shortage");

        Section("По товарам", "tab-goods");
        Note("Щелчок по строке ставит и снимает запрет на этот товар: он перестаёт " +
            "и ввозиться, и вывозиться.");

        _table = Table.Create(
            [
                new Column("Товар", 0, Right: false),
                new Column("Ввоз", 56),
                new Column("Вывоз", 56),
                new Column("Сальдо", 60),
                new Column("Запрет", 50),
            ],
            row => Toggle(AllGoods[row]),
            Stack,
            row => GoodCard.Of(Loop, AllGoods[row]));

        Rows.AddChild(_table);
    }

    public override void Refresh()
    {
        var sim = Loop.Simulation;
        var prices = Me.State.Prices;

        var exports = sim.ExportsOf(Id);
        var imports = sim.ImportsOf(Id);
        var balance = exports - imports;

        _exports.Set(Fmt.Cash(exports.Exact));
        _imports.Set(Fmt.Cash(imports.Exact));
        _balance.Set(Fmt.Cash(balance.Exact), Fmt.Sign(balance.Exact));
        _yearly.Set(Fmt.Cash(Me.ExportsPerDay.Exact * 365));
        _transit.Set(Fmt.Cash(sim.TransitEarnedBy(Id).Exact));
        _fuel.Set(Fmt.Amount(sim.FuelBurnedIn(Id)));

        _flow.Show(
            new Trace("вывоз", Skin.Output, Past.Of(History.Line.Exports)),
            new Trace("ввоз", Skin.Prices, Past.Of(History.Line.Imports)));

        _in.Show(Top(good => prices.CostOf(good, sim.ImportedOf(Id, good)), Skin.Prices));
        _out.Show(Top(good => prices.CostOf(good, sim.ExportedOf(Id, good)), Skin.Output));

        var routes = Loop.World.Routes;
        var unreachable = 0;

        foreach (var country in Loop.World.Countries)
        {
            if (country.Id != Id && !routes.CanReach(Id, country.Id)) unreachable++;
        }

        _unreachable.Set(unreachable.ToString(), unreachable > 0 ? Skin.Warn : Skin.Good);
        _tolls.Set(routes.TollTakers(Id).Count.ToString());

        var (refused, empty) = sim.UnfilledBids;
        _refused.Set($"{Fmt.Amount(refused)} / пусто {Fmt.Amount(empty)}",
            refused.Raw > 0 ? Skin.Warn : Skin.Good);

        var rows = new List<Cell[]>(AllGoods.Length);

        foreach (var good in AllGoods)
        {
            var brought = prices.CostOf(good, sim.ImportedOf(Id, good));
            var sent = prices.CostOf(good, sim.ExportedOf(Id, good));
            var net = sent - brought;
            var open = Me.TradeAccess.CanTrade(good);

            rows.Add(
            [
                new Cell(Names.Of(good), (double)good, Skin.Text, Names.IconOf(good)),
                new Cell(Fmt.Cash(brought.Exact), brought.Exact, brought.Raw > 0 ? Skin.Text : Skin.Dim),
                new Cell(Fmt.Cash(sent.Exact), sent.Exact, sent.Raw > 0 ? Skin.Text : Skin.Dim),
                new Cell(Fmt.Cash(net.Exact), net.Exact, Fmt.Sign(net.Exact)),
                new Cell(open ? "—" : "закрыт", open ? 0 : 1, open ? Skin.Dim : Skin.Bad),
            ]);
        }

        _table.Set(rows);
    }

    /// <summary>Шесть самых крупных товаров по заданной мерке.</summary>
    private List<Slice> Top(Func<GoodType, Money> worth, Color colour) => AllGoods
        .Select(good => (Good: good, Worth: worth(good)))
        .Where(pair => pair.Worth.Raw > 0)
        .OrderByDescending(pair => pair.Worth.Raw)
        .Take(6)
        .Select(pair => new Slice(
            Names.Of(pair.Good), pair.Worth.Exact, Fmt.Cash(pair.Worth.Exact), colour, Names.IconOf(pair.Good)))
        .ToList();

    private void Toggle(GoodType good)
    {
        if (Me.TradeAccess.CanTrade(good)) Me.TradeAccess.Block(good);
        else Me.TradeAccess.Allow(good);

        Refresh();
    }
}
