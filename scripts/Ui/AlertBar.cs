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

    /// <param name="Extra">Что дописать к подсказке по месту: какой товар подорожал и на
    /// сколько. У постоянных тревог этого нет — им хватает статьи.</param>
    private sealed record Alert(
        string Icon,
        string Key,
        Color Colour,
        int Tab,
        Func<bool> Lit,
        Func<string?>? Extra = null);

    /// <summary>Насколько цены должны уйти за месяц, чтобы это считалось рывком.</summary>
    private const double PriceJump = 1.5;

    /// <summary>На сколько процентных пунктов уровень цен должен вырасти за месяц.</summary>
    private const double InflationJump = 5;

    /// <summary>Насколько должен ослабнуть курс за месяц.</summary>
    private const double RateSlide = 1.15;

    private GameLoop _loop = null!;
    private SideTabs _tabs = null!;
    private History _past = null!;

    private readonly List<(Alert Alert, Button Plate, TextureRect Icon, bool[] Lit)> _alerts = [];
    private double _left;
    private double _phase;

    public override void _Ready()
    {
        _loop = GetNode<GameLoop>("/root/Game/GameLoop");
        _tabs = GetNode<SideTabs>("/root/Game/Ui/SideTabs");
        _past = GetNode<History>("/root/Game/History");

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

            button.AddChild(new HoverProbe { Stack = stack, Key = alert.Key, Extra = alert.Extra });
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

            // Дальше не состояния, а перемены: они и есть события, ради которых на панель
            // смотрят. Считаются сравнением с тем, что было месяц назад.
            new Alert("alert-spike", "spike", Skin.Warn, 1, () => Spiked().Count > 0, SpikeText),

            new Alert("alert-inflation", "runaway", Skin.Bad, 4,
                () => _loop.Inflation - _past.Ago(History.Line.Inflation, History.Month) > InflationJump,
                RunawayText),

            new Alert("alert-currency", "slide", Skin.Warn, 3, () => Slide() > RateSlide, SlideText),
        ];
    }

    /// <summary>Товары, подорожавшие за месяц сильнее порога, от худшего к меньшему.</summary>
    private List<(GoodType Good, double Times)> Spiked()
    {
        var found = new List<(GoodType, double)>();

        foreach (var good in Enum.GetValues<GoodType>())
        {
            var times = _past.Jump(good);
            if (times > PriceJump) found.Add((good, times));
        }

        found.Sort((a, b) => b.Item2.CompareTo(a.Item2));

        return found;
    }

    private string? SpikeText()
    {
        var spiked = Spiked();
        if (spiked.Count == 0) return null;

        var worst = spiked.Take(3).Select(pair => $"{Names.Of(pair.Good)} ×{pair.Times:0.0}");

        return $"[b]Подорожали за месяц:[/b] {string.Join(", ", worst)}.";
    }

    private string? RunawayText()
    {
        var grew = _loop.Inflation - _past.Ago(History.Line.Inflation, History.Month);

        return $"[b]За месяц цены выросли на {grew:0.0} пункта.[/b] " +
            $"Ключевая ставка сейчас {Fmt.Rate(_loop.PlayerCountry.KeyRate)}, " +
            $"доля труда {_loop.PlayerCountry.LabourShare}%.";
    }

    /// <summary>Во сколько раз ослаб курс за месяц. Больше единицы — своя валюта дешевеет.</summary>
    private double Slide()
    {
        var was = _past.Ago(History.Line.Rate, History.Month);

        return was > 0 ? _loop.Rate / was : 1;
    }

    private string? SlideText()
    {
        var times = Slide();

        return $"[b]За месяц валюта подешевела в {times:0.00} раза.[/b] " +
            $"Курс {_loop.Rate:0.00} против {_past.Ago(History.Line.Rate, History.Month):0.00} месяц назад.";
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
