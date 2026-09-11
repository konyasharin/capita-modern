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

    private sealed record Alert(string Icon, string Key, Color Colour, int Tab, Func<bool> Lit);

    private GameLoop _loop = null!;
    private SideTabs _tabs = null!;

    private readonly List<(Alert Alert, TextureRect Icon, bool[] Lit)> _alerts = [];
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
                CustomMinimumSize = new Vector2(22, 22),
                FocusMode = FocusModeEnum.None,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
            };

            button.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
            button.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
            button.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
            button.AddChild(new HoverProbe { Stack = stack, Key = alert.Key });
            button.Pressed += () => _tabs.Show(alert.Tab);

            var icon = Ui.Icon(Names.Ui(alert.Icon), 18, alert.Colour);
            icon.SetAnchorsPreset(LayoutPreset.Center);
            icon.Position = new Vector2(-9, -9);
            button.AddChild(icon);

            _alerts.Add((alert, icon, new bool[1]));
            AddChild(button);
        }

        Check();
    }

    public override void _Process(double delta)
    {
        // Горящие значки медленно дышат: неподвижный красный значок глаз перестаёт
        // замечать через минуту.
        _phase += delta * 2.2;

        foreach (var (alert, icon, lit) in _alerts)
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
        foreach (var (alert, icon, lit) in _alerts)
        {
            lit[0] = alert.Lit();
            if (!lit[0]) icon.Modulate = new Color(Skin.Dim, 0.28f);
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
