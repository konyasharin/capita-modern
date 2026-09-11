using CapitaModern.Core.Economy;
using CapitaModern.Core.World;
using Godot;

/// <summary>Казна, резервы, долг — и рычаги, которыми всё это правится: ставка, потолок
/// заимствования, доля зарплат, печать денег и отказ платить.</summary>
public partial class FinancePanel : SidePanel
{
    public override string Title => "Финансы";
    public override string Icon => "finance";

    private Chart _purse = null!;
    private Bars _held = null!;

    private StatRow _balance = null!;
    private StatRow _budget = null!;
    private StatRow _wages = null!;
    private StatRow _supply = null!;
    private StatRow _printed = null!;

    private StatRow _reserves = null!;
    private StatRow _frozen = null!;
    private StatRow _frozenBy = null!;

    private StatRow _debt = null!;
    private StatRow _burden = null!;
    private StatRow _loans = null!;
    private StatRow _interest = null!;
    private StatRow _lockout = null!;

    private readonly List<Stepper> _knobs = [];
    private Table _table = null!;
    private Button _emit = null!;
    private Button _repudiate = null!;

    protected override void Build()
    {
        Section("Как идут деньги", "treasury");
        _purse = Graph("Казна и внешний долг", Fmt.Cash).Ranged();

        Section("Казна", "treasury");
        _balance = Stat("Остаток", "treasury", Trends.Treasury(Past));
        _budget = Stat("Сальдо за день", "budget");
        _wages = Stat("Зарплаты за день", "wages");
        _supply = Stat("Денежная масса", "supply", Trends.Supply(Past));
        _printed = Stat("Напечатано за партию", "printed");

        Section("Резервы", "rate");
        _held = Columns();
        _reserves = Stat("Всего", "reserves");
        _frozen = Stat("Заморожено", "frozen");
        _frozenBy = Stat("Кем заморожено", "frozen");

        Section("Долг", "debt");
        _debt = Stat("Внешний долг", "debt", Trends.Debt(Past));
        _burden = Stat("Нагрузка на вывоз", "burden");
        _loans = Stat("Займов", "debt");
        _interest = Stat("Проценты за день", "interest");
        _lockout = Stat("Кредит закрыт до", "lockout");

        _table = Table.Create(
        [
            new Column("Кредитор", 0, Right: false),
            new Column("Тело", 70),
            new Column("Ставка", 62),
        ]);

        Rows.AddChild(_table);

        Section("Рычаги", "tab-finance");
        Knob("Ключевая ставка", 0, 3000, 25,
            () => Me.KeyRate, value => Me.KeyRate = value, Fmt.Rate, "keyrate");
        Knob("Потолок заимствования", 0, CreditMarket.Ceiling, 100,
            () => Me.MaxBorrowRate, value => Me.MaxBorrowRate = value, Fmt.Rate, "maxrate");
        Knob("Доля труда в выручке", 0, 100, 1,
            () => Me.LabourShare, value => Me.LabourShare = value, value => $"{value}%", "labour");

        Section("Необратимое", "alert-default");
        Note("Печать поднимает цены по всей стране, отказ платить закрывает кредит " +
            "на пять лет. Оба действия обычно делает сама модель — здесь они вручную.");

        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 8);

        _emit = Ui.Act("Напечатать 1% массы", Skin.Warn, Emit, 168);
        _repudiate = Ui.Act("Отказаться платить", Skin.Bad, Repudiate);

        line.AddChild(_emit);
        line.AddChild(_repudiate);
        Rows.AddChild(line);
    }

    public override void Refresh()
    {
        var sim = Loop.Simulation;
        var treasury = Me.State.Treasury;

        _balance.Set(Fmt.Cash(treasury.Balance.Exact), Fmt.Sign(treasury.Balance.Exact));

        var budget = sim.BudgetOf(Id);
        _budget.Set(Fmt.Cash(budget.Exact), Fmt.Sign(budget.Exact));
        _wages.Set(Fmt.Cash(sim.WagesIn(Id).Exact));
        _supply.Set(Fmt.Cash(Me.Bank.Supply.Exact));
        _printed.Set(Fmt.Cash(Me.Bank.Printed.Exact), Me.Bank.Printed.Raw > 0 ? Skin.Warn : Skin.Text);

        _purse.Show(
            new Trace("казна", Skin.Money, Past.Of(History.Line.Treasury)),
            new Trace("долг", Skin.Owed, Past.Of(History.Line.Debt)));

        var reserves = treasury.Reserves;
        _held.Show(reserves.Held
            .GroupBy(item => item.Issuer)
            .Select(group => (Iso: Loop.World.CountryById(group.Key).Iso, Sum: group.Sum(item => item.Amount.Exact)))
            .Where(pair => pair.Sum > 0)
            .OrderByDescending(pair => pair.Sum)
            .Take(6)
            .Select(pair => new Slice(pair.Iso, pair.Sum, Fmt.Cash(pair.Sum), Skin.Rate))
            .ToList());
        _reserves.Set(Fmt.Cash(reserves.Value.Exact));
        _frozen.Set(Fmt.Cash(reserves.Frozen.Exact), reserves.Frozen.Raw > 0 ? Skin.Bad : Skin.Good);
        // Перечисляем троих: длинный список вылезал за строку и наезжал на подпись.
        var froze = reserves.FrozenBy.Select(who => Loop.World.CountryById(who).Iso).ToList();
        _frozenBy.Set(froze.Count switch
        {
            0 => "никем",
            <= 3 => string.Join(", ", froze),
            _ => $"{string.Join(", ", froze.Take(3))} и ещё {froze.Count - 3}",
        });

        var debt = treasury.Debt;
        var owed = debt.Owed(LoanSource.Foreign);

        _debt.Set(Fmt.Cash(owed.Exact), owed.Raw > 0 ? Skin.Owed : Skin.Good);

        var burden = debt.BurdenToExports(Me.ExportsPerDay * 365);
        _burden.Set($"{burden / 100.0:0.0}×", burden >= Simulation.DefaultBurden ? Skin.Bad : Skin.Text);
        _loans.Set(debt.Loans.Count.ToString());

        var interest = default(Money);
        var rows = new List<Cell[]>(debt.Loans.Count);

        foreach (var loan in debt.Loans)
        {
            var lenderRate = loan.Lender is { } who ? Loop.World.CountryById(who).KeyRate : 0;
            var rate = loan.RateAt(lenderRate);

            interest += loan.InterestPerTick(lenderRate);

            rows.Add(
            [
                new Cell(loan.Lender is { } id ? Names.Of(Loop.World.CountryById(id)) : Names.Of(loan.Source),
                    loan.Principal.Exact, Skin.Text),
                new Cell(Fmt.Cash(loan.Principal.Exact), loan.Principal.Exact, Skin.Owed),
                new Cell(Fmt.Rate(rate), rate, rate > 1500 ? Skin.Bad : Skin.Text),
            ]);
        }

        _interest.Set(Fmt.Cash(interest.Exact));

        var locked = Me.DefaultedOnDay > 0
            ? Me.DefaultedOnDay + Simulation.DefaultLockYears * 365 - sim.Day
            : 0;

        _lockout.Set(locked > 0 ? $"{locked} дней" : "открыт", locked > 0 ? Skin.Bad : Skin.Good);
        _table.Set(rows);

        foreach (var knob in _knobs) knob.Refresh();

        _repudiate.Disabled = owed.Raw == 0;
    }

    private void Knob(string label, int min, int max, int step,
        Func<int> read, Action<int> write, Func<int, string> show, string key)
    {
        var knob = Stepper.Create(label, min, max, step, read, write, show, Stack, key);

        _knobs.Add(knob);
        Rows.AddChild(knob);
    }

    /// <summary>Печатает процент денежной массы и кладёт в казну. Ctrl и Shift множат.</summary>
    private void Emit()
    {
        var amount = new Money(Me.Bank.Supply.Raw * Ui.Louder() / 100);
        if (amount.Raw <= 0) return;

        Me.Bank.Emit(amount, EmissionKind.Open);
        Me.State.Treasury.Receive(amount);
        Refresh();
    }

    /// <summary>Списывает внешний долг. Кредит после этого закрыт на пять лет — так же,
    /// как при отказе, до которого модель доводит сама.</summary>
    private void Repudiate()
    {
        Me.State.Treasury.Debt.Default(LoanSource.Foreign);
        Me.DefaultedOnDay = Loop.Simulation.Day;
        Refresh();
    }
}
