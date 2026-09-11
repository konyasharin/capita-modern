using CapitaModern.Core.Economy;
using CapitaModern.Core.World;
using Godot;

/// <summary>Казна, резервы, долг — и рычаги, которыми всё это правится: ставка, потолок
/// заимствования, доля зарплат, печать денег и отказ платить.</summary>
public partial class FinancePanel : SidePanel
{
    public override string Title => "Финансы";
    public override string Icon => "finance";

    private StatRow _balance = null!;
    private StatRow _budget = null!;
    private StatRow _wages = null!;

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
        Section("Казна");
        _balance = Stat("Остаток", "treasury");
        _budget = Stat("Сальдо за день", "budget");
        _wages = Stat("Зарплаты за день", "wages");

        Section("Резервы");
        _reserves = Stat("Всего", "reserves");
        _frozen = Stat("Заморожено", "frozen");
        _frozenBy = Stat("Кем заморожено", "frozen");

        Section("Долг");
        _debt = Stat("Внешний долг", "debt");
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

        Section("Рычаги");
        Knob("Ключевая ставка", 0, 3000, 25,
            () => Me.KeyRate, value => Me.KeyRate = value, Fmt.Rate, "keyrate");
        Knob("Потолок заимствования", 0, CreditMarket.Ceiling, 100,
            () => Me.MaxBorrowRate, value => Me.MaxBorrowRate = value, Fmt.Rate, "maxrate");
        Knob("Доля труда в выручке", 0, 100, 1,
            () => Me.LabourShare, value => Me.LabourShare = value, value => $"{value}%", "labour");

        Section("Необратимое");
        Note("Печать поднимает цены по всей стране, отказ платить закрывает кредит " +
            "на пять лет. Оба действия обычно делает сама модель — здесь они вручную.");

        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 8);

        _emit = Ui.Act("Напечатать 1% массы", Skin.Warn, Emit);
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

        var reserves = treasury.Reserves;
        _reserves.Set(Fmt.Cash(reserves.Value.Exact));
        _frozen.Set(Fmt.Cash(reserves.Frozen.Exact), reserves.Frozen.Raw > 0 ? Skin.Bad : Skin.Good);
        _frozenBy.Set(reserves.FrozenBy.Count == 0
            ? "никем"
            : string.Join(", ", reserves.FrozenBy.Select(who => Loop.World.CountryById(who).Iso)));

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
                new Cell(loan.Lender is { } id ? Loop.World.CountryById(id).Name : Names.Of(loan.Source),
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

    /// <summary>Печатает процент денежной массы и кладёт в казну.</summary>
    private void Emit()
    {
        var amount = new Money(Me.Bank.Supply.Raw / 100);
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
