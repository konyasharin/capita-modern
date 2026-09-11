using CapitaModern.Core.Economy;
using CapitaModern.Core.World;
using Godot;

/// <summary>Тревоги в левом углу панели. Значок горит, пока беда не ушла; щелчок
/// открывает вкладку, где её видно подробно.</summary>
/// <remarks>Условия здесь не считаются заново — все они уже есть в модели. Панель только
/// показывает то, что иначе пришлось бы искать по вкладкам.</remarks>
public partial class AlertBar : HBoxContainer
{
    /// <summary>Пересчёт раз в четверть секунды: тревога не обязана загораться в тот же
    /// кадр, а обход товаров не бесплатный.</summary>
    private const double CheckEvery = 0.25;

    /// <summary>Сторона значка тревоги.</summary>
    private const int Size = 24;

    private sealed record Alert(string Icon, string Key, Color Colour, int Tab, Func<bool> Lit);

    private GameLoop _loop = null!;
    private SideTabs _tabs = null!;

    private readonly List<(Alert Alert, Button Plate, TextureRect Icon, bool[] Lit)> _alerts = [];
    private double _left;
    private double _phase;

    public override void _Ready()
    {
        _loop = GetNode<GameLoop>("/root/Game/GameLoop");
        _tabs = GetNode<SideTabs>("/root/Game/Ui/SideTabs");

        var stack = GetNode<PopoverStack>("/root/Game/Overlay/PopoverStack");

        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 6);

        foreach (var alert in Build())
        {
            var button = new Button
            {
                CustomMinimumSize = new Vector2(Size, Size),
                FocusMode = FocusModeEnum.None,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
            };

            button.AddChild(new HoverProbe { Stack = stack, Key = alert.Key });
            button.Pressed += () => _tabs.Show(alert.Tab);

            var icon = Ui.Icon(Names.Ui(alert.Icon), 17, alert.Colour);
            icon.SetAnchorsPreset(LayoutPreset.Center);
            icon.Position = new Vector2(-8.5f, -8.5f);
            button.AddChild(icon);

            _alerts.Add((alert, button, icon, new bool[1]));
            AddChild(button);
        }

        Check();
    }

    public override void _Process(double delta)
    {
        // Горящие значки медленно дышат: неподвижный красный значок глаз перестаёт
        // замечать через минуту.
        _phase += delta * 2.2;

        foreach (var (alert, _, icon, lit) in _alerts)
        {
            if (lit[0]) icon.Modulate = new Color(alert.Colour, 0.72f + 0.28f * (float)Mathf.Sin(_phase));
        }

        _left -= delta;
        if (_left > 0) return;

        _left = CheckEvery;
        Check();
    }

    private void Check()
    {
        foreach (var (alert, plate, icon, lit) in _alerts)
        {
            lit[0] = alert.Lit();

            // Горящая тревога получает плашку в свой цвет: одного оттенка значка мало,
            // на тёмной панели он теряется.
            var box = lit[0] ? Skin.ChipBox(alert.Colour) : Skin.ChipBox(new Color(Skin.Dim, 0.25f));
            box.ContentMarginLeft = 0;
            box.ContentMarginRight = 0;

            plate.AddThemeStyleboxOverride("normal", box);
            plate.AddThemeStyleboxOverride("hover", Skin.ChipBox(lit[0] ? alert.Colour : Skin.Dim));
            plate.AddThemeStyleboxOverride("pressed", Skin.ChipBox(Skin.Link));

            if (!lit[0]) icon.Modulate = new Color(Skin.Dim, 0.5f);
        }
    }

    private Alert[] Build()
    {
        var sim = _loop.Simulation;

        return
        [
            new Alert("alert-hands", "hands", Skin.Bad, 0,
                () => sim.JobsIn(_loop.Player) > sim.EmployedIn(_loop.Player)),

            new Alert("alert-idle", "load", Skin.Warn, 0,
                () => sim.LoadIn(_loop.Player) < Load.Full),

            new Alert("alert-shortage", "shortage", Skin.Warn, 1, Shortage),

            new Alert("alert-deficit", "treasury", Skin.Bad, 4,
                () => _loop.PlayerCountry.State.Treasury.Balance.Raw < 0),

            new Alert("alert-frozen", "frozen", Skin.Bad, 4,
                () => _loop.PlayerCountry.State.Treasury.Reserves.FrozenBy.Count > 0),

            new Alert("alert-default", "lockout", Skin.Bad, 4,
                () => _loop.PlayerCountry.DefaultedOnDay > 0
                    && sim.Day - _loop.PlayerCountry.DefaultedOnDay < Simulation.DefaultLockYears * 365),
        ];
    }

    private bool Shortage()
    {
        foreach (var good in Enum.GetValues<GoodType>())
        {
            if (_loop.Simulation.ShortOf(_loop.Player, good).Raw > 0) return true;
        }

        return false;
    }
}
