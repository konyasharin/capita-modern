using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;
using CapitaModern.Core.Politics;
using CapitaModern.Core.World;
using Godot;

/// <summary>Предприятия и стройка. Здесь же приоритеты снабжения — единственный рычаг,
/// которым игрок влияет на то, что строится и кому достаётся дефицит.</summary>
public partial class IndustryPanel : SidePanel
{
    private static readonly BuildingType[] AllTypes = Enum.GetValues<BuildingType>();

    public override string Title => "Промышленность";
    public override string Icon => "industry";

    private readonly List<Stepper> _weights = [];

    private Bars _bySector = null!;
    private StatRow _building = null!;
    private StatRow _firms = null!;
    private StatRow _ruined = null!;
    private Table _companies = null!;
    private StatRow _builders = null!;
    private StatRow _investment = null!;
    private StatRow _purse = null!;
    private StatRow _total = null!;
    private Table _table = null!;

    protected override void Build()
    {
        Section("Приоритеты снабжения", "tab-industry");
        Note("Вес — множитель к заказу отрасли. При избытке он ни на что не влияет, " +
            "а при нехватке решает, кому достанется сырьё и руки. Обычный вес ×1.00.");

        foreach (var sector in Enum.GetValues<Sector>())
        {
            var which = sector;

            var knob = Stepper.Create(
                Names.Of(sector),
                0,
                400,
                10,
                () => Me.Priorities.WeightOf(which),
                weight => Me.Priorities.SetWeight(which, weight),
                weight => $"×{weight / (double)Priorities.NormalWeight:0.00}",
                Stack,
                "priority");

            _weights.Add(knob);
            Rows.AddChild(knob);
        }

        Section("Стройка", "plants");
        _building = Stat("Строится сейчас", "building");
        _builders = Stat("Занято на стройке", "builders");
        _investment = Stat("Откладывается за день", "investment");
        _purse = Stat("Скопилось на стройку", "purse");

        Section("Предприятия по отраслям", "employment");
        _bySector = Columns();

        Section("Предприятия", "tab-industry");
        _total = Stat("Всего", "plants", Trends.Plants(Past));
        Note("«Работает» меньше «есть» — значит зданию не хватило сырья или рук.");

        _table = Table.Create(
        [
            new Column("Здание", 0, Right: false),
            new Column("Есть", 54),
            new Column("Работает", 62),
            new Column("Людей", 58),
        ]);

        Rows.AddChild(_table);

        Section("Компании", "plants");
        _firms = Stat("Живых", "plants");
        _ruined = Stat("Разорилось", "debt");
        Note("Компания строит только в своих отраслях и только на свои. Не хватает — " +
            "берёт в банке; долг перевалил за три годовых выручки — распродаёт дело, " +
            "за пять — разоряется, и здания достаются соседу по отрасли.");

        _companies = Table.Create(
        [
            new Column("Компания", 0, Right: false),
            new Column("Зданий", 58),
            new Column("Деньги", 62),
            new Column("Долг", 62),
        ]);

        Rows.AddChild(_companies);
    }

    public override void Refresh()
    {
        var sim = Loop.Simulation;
        var catalog = Loop.World.Buildings;

        foreach (var knob in _weights) knob.Refresh();

        var plan = sim.PlanOf(Id);
        _building.Set(plan is { } what ? $"{Names.Of(what.Type)} ×{what.Count}" : "ничего",
            plan is null ? Skin.Dim : Skin.Good);

        var builders = sim.BuildersIn(Id);
        _builders.Set(builders > 0 ? Fmt.Count(builders) : "никого", builders > 0 ? Skin.Bright : Skin.Dim);
        // InvestmentIn — это кошелёк целиком, а не дневная доля: за день откладывается
        // четверть добавленной стоимости.
        var daily = sim.ValueAddedOf(Id).Exact * Construction.InvestmentShare / 100;
        var purse = sim.InvestmentIn(Id).Exact;

        _investment.Set(Fmt.Cash(daily));

        // Кошелёк копится, когда денег больше, чем материалов. Игроку это надо видеть:
        // напечатать ещё не значит построить.
        _purse.Set(Fmt.Cash(purse), daily > 0 && purse > daily * 30 ? Skin.Warn : Skin.Text);

        var rows = new List<Cell[]>(AllTypes.Length);
        var bySector = new Dictionary<Sector, long>();
        var total = 0L;

        foreach (var type in AllTypes)
        {
            var count = Loop.World.BuildingsOf(Id, type);
            if (count == 0) continue;

            total += count;

            var working = sim.WorkingOf(Id, type);
            var staff = (long)working * catalog[type].OptimalWorkers;

            bySector[catalog[type].Sector] = bySector.GetValueOrDefault(catalog[type].Sector) + count;

            rows.Add(
            [
                new Cell(Names.Of(type), (double)type, Skin.Text, Names.IconOf(type)),
                new Cell(Fmt.Count(count), count, Skin.Bright),
                new Cell(Fmt.Count(working), working, working < count ? Skin.Bad : Skin.Good),
                new Cell(Fmt.Count(staff), staff, Skin.Text),
            ]);
        }

        _bySector.Show(bySector
            .OrderByDescending(pair => pair.Value)
            .Select(pair => new Slice(Names.Of(pair.Key), pair.Value, Fmt.Count(pair.Value), Skin.Plants))
            .ToList());

        _total.Set(Fmt.Count(total));
        _table.Set(rows);

        ShowCompanies(sim);
    }

    /// <summary>Кто в стране чем владеет. Крупнейшие сверху: мелких сотни, и все они
    /// одинаковые.</summary>
    private void ShowCompanies(Simulation sim)
    {
        var mine = Loop.World.CompaniesOf(Id);
        var alive = mine.Count(company => company.Alive);

        _firms.Set($"{alive} из {mine.Count}", Skin.Bright);

        var ruined = sim.RuinedIn(Id);
        _ruined.Set(ruined > 0 ? $"{ruined}, продано зданий {sim.SoldIn(Id)}" : "никто",
            ruined > 0 ? Skin.Bad : Skin.Good);

        _companies.Set(mine
            .Where(company => company.Alive)
            .OrderByDescending(company => company.Size)
            .Take(20)
            .Select(company => new Cell[]
            {
                new($"{company.Name} ({string.Join(", ", company.Focus.Select(Names.Of))})",
                    company.Size, company.Known ? Skin.Bright : Skin.Text),
                new(Fmt.Count(company.Size), company.Size, Skin.Text),
                new(Fmt.Cash(company.Cash.Exact), company.Cash.Exact, Skin.Good),
                new(Fmt.Cash(company.Debt.Exact), company.Debt.Exact,
                    company.Debt.Raw > 0 ? Skin.Warn : Skin.Dim),
            })
            .ToList());
    }
}
