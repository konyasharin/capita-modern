using CapitaModern.Core.Economy;
using Godot;

/// <summary>Сводка по стране: люди, достаток, производство, деньги. Всё, по чему видно
/// состояние страны, не открывая остальных вкладок.</summary>
public partial class OverviewPanel : SidePanel
{
    public override string Title => "Обзор страны";
    public override string Icon => "overview";

    private StatRow _population = null!;
    private StatRow _workers = null!;
    private Bar _employment = null!;
    private StatRow _missing = null!;

    private StatRow _wage = null!;
    private StatRow _savings = null!;
    private StatRow _engel = null!;
    private StatRow _capacity = null!;

    private Bar _load = null!;
    private StatRow _added = null!;
    private StatRow _yearly = null!;
    private StatRow _perWorker = null!;

    private readonly Dictionary<Sector, StatRow> _efficiency = [];

    private StatRow _supply = null!;
    private StatRow _printed = null!;
    private StatRow _level = null!;
    private StatRow _rate = null!;

    protected override void Build()
    {
        Section("Люди");
        _population = Stat("Население", "population");
        _workers = Stat("Рабочая сила", "workers");
        _employment = Gauge("Занятость", Skin.People, "employment");
        _missing = Stat("Не хватает рук", "hands");

        Section("Достаток");
        _wage = Stat("Зарплата за день", "wage");
        _savings = Stat("Сбережения населения", "savings");
        _engel = Stat("Доля еды в расходах", "engel");
        _capacity = Stat("Корзин на дневной доход", "capacity");

        Section("Производство");
        _load = Gauge("Загрузка предприятий", Skin.Plants, "load");
        _added = Stat("Добавленная стоимость за день", "output");
        _yearly = Stat("ВВП за год, постоянные цены", "output");
        _perWorker = Stat("Выработка на работника за год");

        Section("Эффективность отраслей");
        foreach (var sector in Enum.GetValues<Sector>())
        {
            if (sector == Sector.People) continue;

            _efficiency[sector] = Stat(Names.Of(sector), "efficiency");
        }

        Section("Деньги");
        _supply = Stat("Денежная масса", "supply");
        _printed = Stat("Напечатано за партию", "printed");
        _level = Stat("Уровень цен к старту", "inflation");
        _rate = Stat("Курс к доллару", "rate");
    }

    public override void Refresh()
    {
        var sim = Loop.Simulation;
        var world = Loop.World;

        var people = world.PopulationOf(Id).Whole;
        var workers = world.WorkersOf(Id);
        var employed = sim.EmployedIn(Id);
        var wanted = sim.JobsIn(Id);

        _population.Set(Fmt.Count(people));
        _workers.Set(Fmt.Count(workers));
        _employment.Set(
            workers > 0 ? (double)employed / workers : 0,
            workers > 0 ? Fmt.Percent(employed * 100.0 / workers) : "—");

        var missing = wanted - employed;
        _missing.Set(missing > 0 ? Fmt.Count(missing) : "нет", missing > 0 ? Skin.Bad : Skin.Good);

        _wage.Set(Fmt.Cash(sim.WagePerWorkerIn(Id).Exact));
        _savings.Set(Fmt.Cash(Me.Households.Savings.Exact));

        var engel = sim.EngelOf(Id);
        _engel.Set(Fmt.Percent(engel), engel switch { 0 => Skin.Dim, < 25 => Skin.Good, < 45 => Skin.Text, _ => Skin.Bad });

        var capacity = sim.CapacityIn(Id) / (double)Needs.Scale;
        _capacity.Set($"{capacity:0.00}");

        var load = sim.LoadIn(Id) / (double)Load.Full;
        _load.Set(load, Fmt.Percent(load * 100), load < 0.99 ? Skin.Bad : Skin.Good);

        _added.Set(Fmt.Cash(sim.ValueAddedOf(Id).Exact));
        _yearly.Set(Fmt.Cash(Loop.YearlyOutput));
        _perWorker.Set(employed > 0 ? Fmt.Cash(Loop.YearlyOutput / employed) : "—");

        foreach (var (sector, row) in _efficiency)
        {
            var value = world.Efficiency.Of(Id, sector) / (double)Efficiency.Scale;

            row.Set($"×{value:0.00}", value >= 1 ? Skin.Good : Skin.Warn);
        }

        _supply.Set(Fmt.Cash(Me.Bank.Supply.Exact));
        _printed.Set(Fmt.Cash(Me.Bank.Printed.Exact), Me.Bank.Printed.Raw > 0 ? Skin.Warn : Skin.Text);

        var level = Loop.Inflation;
        _level.Set(Fmt.Percent(level, signed: true), Fmt.Sign(level, moreIsBetter: false));
        _rate.Set($"{Me.ExchangeRate.Exact:0.00}");
    }
}
