using CapitaModern.Core.Politics;
using CapitaModern.Core.World;
using Godot;

/// <summary>Отношения со всем миром и то, что с ними можно сделать: испортить, наладить
/// и заморозить чужие резервы, которые лежат у нас.</summary>
public partial class PoliticsPanel : SidePanel
{
    /// <summary>На сколько двигают отношение за одно нажатие.</summary>
    private const int Step = 10;

    public override string Title => "Политика";
    public override string Icon => "politics";

    private Country[] _others = [];
    private Country? _chosen;

    private Bars _blocs = null!;
    private StatRow _bloc = null!;
    private StatRow _friends = null!;
    private StatRow _foes = null!;

    private Label _who = null!;
    private Button _worse = null!;
    private Button _better = null!;
    private Button _freeze = null!;

    private Table _table = null!;

    protected override void Build()
    {
        Section("Мир по блокам", "tab-politics");
        _blocs = Columns();

        Section("Наше место", "rate");
        _bloc = Stat("Блок", "bloc");
        _friends = Stat("Дружественных стран", "attitude");
        _foes = Stat("Враждебных стран", "attitude");

        Section("Выбранная страна", "population");
        Note("Щелчок по строке в списке выбирает страну. Заморозка касается только тех " +
            "резервов, что она держит у нас: чужое хранилище нам не подчиняется.");

        _who = Ui.Text("никто не выбран", 15, 600, Skin.Dim);
        Rows.AddChild(_who);

        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 8);

        _worse = Ui.Act($"−{Step}", Skin.Bad, () => Move(-Step));
        _better = Ui.Act($"+{Step}", Skin.Good, () => Move(Step));
        _freeze = Ui.Act("Заморозить резервы", Skin.Warn, Freeze);

        line.AddChild(_worse);
        line.AddChild(_better);
        line.AddChild(_freeze);
        Rows.AddChild(line);

        Section("Страны", "tab-politics");
        _table = Table.Create(
            [
                new Column("Страна", 0, Right: false),
                new Column("Блок", 74),
                new Column("Отношение", 74),
                new Column("Путь", 48),
            ],
            row => Choose(_others[row]));

        Rows.AddChild(_table);
    }

    public override void Refresh()
    {
        var world = Loop.World;
        var relations = world.Relations;

        if (_others.Length == 0)
        {
            _others = world.Countries.Where(country => country.Id != Id).ToArray();
        }

        _bloc.Set(Names.Of(relations.BlocOf(Id)), Names.ColourOf(relations.BlocOf(Id)));

        var friends = 0;
        var foes = 0;
        var rows = new List<Cell[]>(_others.Length);

        foreach (var other in _others)
        {
            var attitude = relations.Between(Id, other.Id);
            var bloc = relations.BlocOf(other.Id);
            var reachable = world.Routes.CanReach(Id, other.Id);

            if (attitude >= Relations.Friendly) friends++;
            if (attitude <= -Relations.Friendly) foes++;

            rows.Add(
            [
                new Cell(Names.Of(other), 0, _chosen == other ? Skin.Link : Skin.Text),
                new Cell(Names.Of(bloc), (double)bloc, Names.ColourOf(bloc)),
                new Cell(attitude.ToString(), attitude, Fmt.Sign(attitude)),
                new Cell(reachable ? "есть" : "нет", reachable ? 1 : 0, reachable ? Skin.Dim : Skin.Bad),
            ]);
        }

        _blocs.Show(Enum.GetValues<Bloc>()
            .Select(bloc => (Bloc: bloc, Count: world.Countries.Count(c => relations.BlocOf(c.Id) == bloc)))
            .OrderByDescending(pair => pair.Count)
            .Select(pair => new Slice(
                Names.Of(pair.Bloc), pair.Count, pair.Count.ToString(), Names.ColourOf(pair.Bloc)))
            .ToList());

        _friends.Set(friends.ToString(), friends > 0 ? Skin.Good : Skin.Dim);
        _foes.Set(foes.ToString(), foes > 0 ? Skin.Bad : Skin.Dim);
        _table.Set(rows);

        var chosen = _chosen is not null;
        _who.Text = chosen ? $"{Names.Of(_chosen!)} · {relations.Between(Id, _chosen.Id)}" : "никто не выбран";
        _who.AddThemeColorOverride("font_color", chosen ? Skin.Bright : Skin.Dim);

        _worse.Disabled = !chosen;
        _better.Disabled = !chosen;
        _freeze.Disabled = !chosen || !HoldsOurs(_chosen);
    }

    private void Choose(Country country)
    {
        _chosen = country;
        Refresh();
    }

    private void Move(int by)
    {
        if (_chosen is null) return;

        var relations = Loop.World.Relations;

        relations.Set(Id, _chosen.Id, Mathf.Clamp(relations.Between(Id, _chosen.Id) + by, -100, 100));
        Loop.World.Routes.Recompute(relations.Between, null);
        Refresh();
    }

    /// <summary>Замораживаем не свои резервы, а чужие: отнимает тот, у кого лежит.</summary>
    private void Freeze()
    {
        if (_chosen is null) return;

        _chosen.State.Treasury.Reserves.Freeze(Id);
        Refresh();
    }

    private bool HoldsOurs(Country? country) =>
        country is not null
        && !country.State.Treasury.Reserves.FrozenBy.Contains(Id)
        && country.State.Treasury.Reserves.Held.Any(reserve => reserve.Custodian == Id);
}
