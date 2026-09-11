using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;
using Godot;

/// <summary>Большое окно производства: все товары плашками, доли стран в мировом выпуске
/// кругом, сведения по выбранному товару и вложение денег в его производство.</summary>
/// <remarks>
/// Отдельным окном, а не вкладкой: здесь нужны сразу три колонки, и в четыреста пикселей
/// боковой панели они не встают.
/// </remarks>
public partial class ResourceWindow : Control
{
    private const double RefreshEvery = 0.25;
    private const int Columns = 6;

    /// <summary>Что показывает число под плашкой.</summary>
    private enum Mode
    {
        Output,
        Input,
        Stock,
        Price,
        Short,
        Deposits,
    }

    private static readonly GoodType[] AllGoods = Enum.GetValues<GoodType>();

    private GameLoop _loop = null!;
    private PopoverStack _stack = null!;

    private readonly Dictionary<GoodType, Badge> _badges = [];
    private readonly List<Button> _modes = [];
    private readonly List<StatRow> _stats = [];

    private GoodType _chosen = GoodType.Oil;
    private Mode _mode = Mode.Output;
    private BuildingType? _plant;
    private double _money;

    private Label _title = null!;
    private Pie _pie = null!;
    private VBoxContainer _plants = null!;
    private VBoxContainer _needs = null!;
    private Label _deposits = null!;
    private Label _order = null!;
    private Label _sum = null!;
    private Bars _sellers = null!;
    private Bars _buyers = null!;
    private Bars _world = null!;
    private Label _whose = null!;
    private Label _enough = null!;
    private Label _price = null!;
    private VBoxContainer _breakdown = null!;
    private HSlider _slider = null!;
    private Button _accept = null!;

    private double _left;

    public override void _Ready()
    {
        _loop = GetNode<GameLoop>("/root/Game/GameLoop");
        _stack = GetNode<PopoverStack>("/root/Game/Overlay/PopoverStack");

        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        Fill(this);

        AddChild(Curtain());

        var margin = new MarginContainer();
        Fill(margin);
        margin.AddThemeConstantOverride("margin_left", 26);
        margin.AddThemeConstantOverride("margin_right", 26);
        margin.AddThemeConstantOverride("margin_top", 18);
        margin.AddThemeConstantOverride("margin_bottom", 18);
        AddChild(margin);

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 24);
        margin.AddChild(columns);

        columns.AddChild(Left());
        columns.AddChild(Middle());
        columns.AddChild(Right());

        Choose(_chosen);
    }

    /// <summary>Открыть или закрыть. Пока закрыто, окно ничего не считает.</summary>
    public void Toggle()
    {
        Visible = !Visible;
        if (Visible) Refresh();
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;

        _left -= delta;
        if (_left > 0) return;

        _left = RefreshEvery;
        Refresh();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible) return;
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) Toggle();
    }

    // --- сборка ---------------------------------------------------------------------

    private ColorRect Curtain()
    {
        var back = new ColorRect
        {
            MouseFilter = MouseFilterEnum.Stop,
            Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://scenes/ui/curtain.gdshader") },
        };

        Fill(back);

        return back;
    }

    /// <summary>Растянуть на весь экран. Пресетом якорей этого не добиться: он оставляет
    /// смещения такими, какими посчитал их по нынешнему размеру.</summary>
    private static void Fill(Control control)
    {
        control.AnchorLeft = 0;
        control.AnchorTop = 0;
        control.AnchorRight = 1;
        control.AnchorBottom = 1;
        control.OffsetLeft = 0;
        control.OffsetTop = 0;
        control.OffsetRight = 0;
        control.OffsetBottom = 0;
    }

    private Control Left()
    {
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(Columns * 62 + 40, 0) };
        column.AddThemeConstantOverride("separation", 10);
        column.AddChild(Heading("Товары"));

        var grid = new GridContainer { Columns = Columns };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        column.AddChild(grid);

        foreach (var good in AllGoods)
        {
            var which = good;
            var badge = Badge.Create(good, () => Choose(which));

            _badges[good] = badge;
            grid.AddChild(badge);
        }

        column.AddChild(Ui.Gap(4));
        column.AddChild(Ui.Text("Что показывать числом", 12, 700, Skin.Dim));

        var modes = new GridContainer { Columns = 3 };
        modes.AddThemeConstantOverride("h_separation", 6);
        modes.AddThemeConstantOverride("v_separation", 6);
        column.AddChild(modes);

        foreach (var (mode, label) in Labels)
        {
            var which = mode;
            var button = Ui.Act(label, Skin.Link, () => SetMode(which));

            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _modes.Add(button);
            modes.AddChild(button);
        }

        column.AddChild(Ui.Spring());

        return column;
    }

    private static (Mode Mode, string Label)[] Labels =>
    [
        (Mode.Output, "Выпуск"),
        (Mode.Input, "Заказ"),
        (Mode.Stock, "Склад"),
        (Mode.Price, "Цена"),
        (Mode.Short, "Нехватка"),
        (Mode.Deposits, "Залежи"),
    ];

    private Control Middle()
    {
        // Колонка держит свою ширину и стоит посередине свободного места, но прижата
        // к верху: содержимое короткое, и по центру оно висело в пустоте.
        var column = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(560, 0),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            SizeFlagsVertical = SizeFlags.Fill,
        };

        column.AddThemeConstantOverride("separation", 10);

        _title = Ui.Text("—", 30, 700, Skin.Bright);
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(_title);

        column.AddChild(Heading("Чьи предприятия в нашей стране"));

        _pie = Pie.Create();
        _pie.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        column.AddChild(_pie);

        _whose = Ui.Text(string.Empty, 12, 400, Skin.Dim);
        column.AddChild(_whose);

        column.AddChild(Heading("Кто производит в мире"));
        _world = Bars.Create();
        column.AddChild(_world);

        column.AddChild(Heading("Вложиться в производство"));

        _plants = new VBoxContainer();
        _plants.AddThemeConstantOverride("separation", 3);
        column.AddChild(_plants);

        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 12);
        column.AddChild(line);

        _slider = Ui.Rail(share =>
        {
            _money = share * Purse();
            ShowSum();
        });

        line.AddChild(_slider);

        _sum = Ui.Number("0$", 18, Skin.Money, 130);
        line.AddChild(_sum);

        _accept = Ui.Act("Вложить", Skin.Good, Invest, 96);
        line.AddChild(_accept);

        _enough = Ui.Text(string.Empty, 13, 600, Skin.Text);
        column.AddChild(_enough);

        _order = Ui.Text(string.Empty, 13, 400, Skin.Dim);
        column.AddChild(_order);

        var head = new HBoxContainer();
        head.AddChild(Heading("Цена одного предприятия"));
        head.AddChild(Ui.Spring());

        _price = Ui.Number("—", 14, Skin.Money, 110);
        head.AddChild(_price);
        column.AddChild(head);

        _breakdown = new VBoxContainer();
        _breakdown.AddThemeConstantOverride("separation", 2);
        column.AddChild(_breakdown);

        var traffic = new HBoxContainer();
        traffic.AddThemeConstantOverride("separation", 24);
        column.AddChild(Ui.Gap(6));
        column.AddChild(traffic);

        traffic.AddChild(Side("Кто больше всех вывозит", out _sellers));
        traffic.AddChild(Side("Кто больше всех ввозит", out _buyers));

        column.AddChild(Ui.Spring());

        return column;
    }

    /// <summary>Половина нижнего ряда: заголовок и полосы под ним.</summary>
    private static Control Side(string title, out Bars bars)
    {
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 4);
        column.AddChild(Heading(title));

        bars = Bars.Create();
        column.AddChild(bars);

        return column;
    }

    private Control Right()
    {
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(340, 0) };
        column.AddThemeConstantOverride("separation", 4);

        var head = new HBoxContainer();
        head.AddChild(Heading("Сведения о товаре"));
        head.AddChild(Ui.Spring());

        var close = Ui.Act("✕", Skin.Dim, Toggle);
        close.CustomMinimumSize = new Vector2(28, 0);
        head.AddChild(close);
        column.AddChild(head);

        foreach (var (label, key) in Rows)
        {
            var row = StatRow.Create(label, _stack, key);

            _stats.Add(row);
            column.AddChild(row);
        }

        column.AddChild(Heading("Что нужно для выпуска"));

        _needs = new VBoxContainer();
        _needs.AddThemeConstantOverride("separation", 3);
        column.AddChild(_needs);

        column.AddChild(Ui.Spring());

        _deposits = Ui.Text("—", 15, 600, Skin.Link);
        _deposits.HorizontalAlignment = HorizontalAlignment.Right;
        column.AddChild(_deposits);

        return column;
    }

    private static (string Label, string? Key)[] Rows =>
    [
        ("На складе", null),
        ("Выпуск за день", "output"),
        ("Заказ за день", null),
        ("Не хватает", "shortage"),
        ("Цена", null),
        ("К цене старта", "inflation"),
        ("Рабочих мест", "employment"),
        ("Доля в мировом выпуске", null),
        ("Ввоз за день", "imports"),
        ("Вывоз за день", "exports"),
    ];

    private static Control Heading(string text)
    {
        var head = Ui.Text(text.ToUpperInvariant(), 13, 700, Skin.Link);
        head.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        return head;
    }

    // --- поведение ------------------------------------------------------------------

    private void Choose(GoodType good)
    {
        _chosen = good;
        _plant = null;
        Refresh();
    }

    private void SetMode(Mode mode)
    {
        _mode = mode;
        Refresh();
    }

    /// <summary>Сколько игрок может вложить: всё, что лежит в казне.</summary>
    private double Purse() => Math.Max(0, _loop.PlayerCountry.State.Treasury.Balance.Exact);

    private void Invest()
    {
        if (_plant is not { } type) return;

        var amount = new Money((long)(Math.Min(_money, Purse()) * 100));
        if (amount.Raw <= 0) return;

        _loop.Simulation.Invest(_loop.Player, type, amount);
        _money = 0;
        _slider.Value = 0;
        Refresh();
    }

    /// <summary>Во что обходится одна постройка: материалы по нынешним ценам плюс работа
    /// строителей. Ровно эту сумму снимает стройка за каждое здание.</summary>
    private Money Cost(BuildingType type) =>
        Construction.CostOf(_loop.World.Buildings[type].BuildCost, _loop.PlayerCountry.State.Prices)
        + _loop.Simulation.BuildWageOf(_loop.Player, type);

    // --- обновление -----------------------------------------------------------------

    private void Refresh()
    {
        var sim = _loop.Simulation;
        var id = _loop.Player;
        var prices = _loop.PlayerCountry.State.Prices;
        var stock = _loop.PlayerCountry.State.Stock;

        foreach (var good in AllGoods) _badges[good].Show(good == _chosen, Under(good));

        for (var index = 0; index < _modes.Count; index++)
        {
            Ui.Paint(_modes[index], Labels[index].Mode == _mode ? Skin.Bright : Skin.Link);
        }

        _title.Text = Names.Of(_chosen);
        _title.AddThemeColorOverride("font_color", Names.ColourOf(_chosen).Lightened(0.35f));

        var owners = Owners();

        _pie.Show(owners);
        _whose.Text = owners.Count == 0
            ? "В стране нет предприятий, делающих этот товар."
            : "Раздел по долям области: где граница разрезала область, часть предприятий числится за соседом.";

        _world.Show(Shares());

        var output = sim.OutputOf(id, _chosen);
        var world = sim.WorldOutputOf(_chosen);
        var start = prices.StartOf(_chosen);
        var times = start.Raw > 0 ? prices.Of(_chosen).Exact / start.Exact : 1;

        _stats[0].Set(Fmt.Amount(stock.Of(_chosen)));
        _stats[1].Set(Fmt.Amount(output));
        _stats[2].Set(Fmt.Amount(sim.InputOf(id, _chosen)));

        var lack = sim.ShortOf(id, _chosen);
        _stats[3].Set(Fmt.Amount(lack), lack.Raw > 0 ? Skin.Bad : Skin.Good);
        _stats[4].Set(Fmt.Price(prices.Of(_chosen)), Skin.Money);
        _stats[5].Set($"×{times:0.00}", Fmt.Sign(times - 1, moreIsBetter: false));
        _stats[6].Set(Fmt.Count(Jobs()));
        _stats[7].Set(world.Raw > 0 ? Fmt.Percent(output.Raw * 100.0 / world.Raw) : "—");
        _stats[8].Set(Fmt.Cash(prices.CostOf(_chosen, sim.ImportedOf(id, _chosen)).Exact));
        _stats[9].Set(Fmt.Cash(prices.CostOf(_chosen, sim.ExportedOf(id, _chosen)).Exact));

        ShowPlants();
        ShowNeeds();

        _sellers.Show(Traffic(who => sim.ExportedOf(who, _chosen), Skin.Output));
        _buyers.Show(Traffic(who => sim.ImportedOf(who, _chosen), Skin.Prices));

        var found = _loop.World.RegionsOf(id).Count(region => region.HasDeposit(_chosen));
        _deposits.Text = $"Месторождений в стране: {found}";

        var order = sim.OrderOf(id);
        _order.Text = order is { } what
            ? $"Заказано: {Names.Of(what.Type)}, вложенного осталось {Fmt.Cash(what.Left.Exact)}"
            : "Заказа нет — страна строит то, что выгоднее.";

        ShowSum();
    }

    /// <summary>Число под плашкой в выбранном режиме.</summary>
    private string Under(GoodType good)
    {
        var sim = _loop.Simulation;
        var id = _loop.Player;

        return _mode switch
        {
            Mode.Output => Fmt.Amount(sim.OutputOf(id, good)),
            Mode.Input => Fmt.Amount(sim.InputOf(id, good)),
            Mode.Stock => Fmt.Amount(_loop.PlayerCountry.State.Stock.Of(good)),
            Mode.Price => Fmt.Price(_loop.PlayerCountry.State.Prices.Of(good)),
            Mode.Short => Fmt.Amount(sim.ShortOf(id, good)),
            _ => _loop.World.RegionsOf(id).Count(region => region.HasDeposit(good)).ToString(),
        };
    }

    /// <summary>Кому принадлежат предприятия, делающие этот товар в нашей стране.</summary>
    /// <remarks>Своих предприятий у частника и у чужих стран пока нет: всё производство
    /// государственное. Другой владелец появляется только там, где граница разрезала
    /// область — тогда часть построек числится за соседом по доле ячеек.</remarks>
    private List<Wedge> Owners()
    {
        var types = Producers().ToList();
        var mine = new Dictionary<byte, long>();

        foreach (var region in _loop.World.RegionsOf(_loop.Player))
        {
            foreach (var type in types)
            {
                if (region.BuildingsOf(type) == 0) continue;

                foreach (var owner in region.Owners)
                {
                    var count = region.BuildingsOf(type, owner);
                    if (count > 0) mine[owner] = mine.GetValueOrDefault(owner) + count;
                }
            }
        }

        var total = mine.Values.Sum();
        if (total <= 0) return [];

        return mine
            .OrderByDescending(pair => pair.Value)
            .Take(8)
            .Select((pair, index) => new Wedge(
                pair.Key == _loop.Player ? "Наше государство" : Names.Of(_loop.World.CountryById(pair.Key)),
                (double)pair.Value / total,
                $"{Fmt.Count(pair.Value)} шт.",
                pair.Key == _loop.Player
                    ? Names.ColourOf(_chosen).Lightened(0.25f)
                    : Slices[(index + 1) % Slices.Length]))
            .ToList();
    }

    /// <summary>Доли стран в мировом выпуске товара: пятёрка крупнейших.</summary>
    private List<Slice> Shares()
    {
        var sim = _loop.Simulation;
        var total = sim.WorldOutputOf(_chosen).Raw;
        if (total <= 0) return [];

        return _loop.World.Countries
            .Select(country => (Country: country, Raw: sim.OutputOf(country.Id, _chosen).Raw))
            .Where(pair => pair.Raw > 0)
            .OrderByDescending(pair => pair.Raw)
            .Take(5)
            .Select(pair => new Slice(
                Names.Of(pair.Country),
                pair.Raw,
                Fmt.Percent(pair.Raw * 100.0 / total),
                pair.Country.Id == _loop.Player ? Skin.Bright : Names.ColourOf(_chosen).Lightened(0.3f)))
            .ToList();
    }

    /// <summary>Цвета долек. Оттенки одного цвета сливались, поэтому набор разный, а
    /// страна игрока всегда белая — её долю ищут первой.</summary>
    private static readonly Color[] Slices =
    [
        Skin.Link, Skin.Output, Skin.Money, Skin.Prices,
        Skin.Labour, Skin.Rate, Skin.Owed, Skin.Warn,
    ];

    /// <summary>Пятёрка стран по ввозу или вывозу этого товара.</summary>
    private List<Slice> Traffic(Func<byte, GoodAmount> amount, Color colour) => _loop.World.Countries
        .Select(country => (Country: country, Raw: amount(country.Id).Raw))
        .Where(pair => pair.Raw > 0)
        .OrderByDescending(pair => pair.Raw)
        .Take(5)
        .Select(pair => new Slice(
            Names.Of(pair.Country),
            pair.Raw,
            Fmt.Amount(new GoodAmount(pair.Raw)),
            pair.Country.Id == _loop.Player ? Skin.Bright : colour))
        .ToList();

    private long Jobs()
    {
        var catalog = _loop.World.Buildings;
        var total = 0L;

        foreach (var type in Producers())
        {
            total += (long)_loop.Simulation.WorkingOf(_loop.Player, type) * catalog[type].OptimalWorkers;
        }

        return total;
    }

    private IEnumerable<BuildingType> Producers() => Enum.GetValues<BuildingType>()
        .Where(type => _loop.World.Buildings[type].Outputs.ContainsKey(_chosen));

    private void ShowPlants()
    {
        var types = Producers().ToList();
        _plant ??= types.FirstOrDefault();

        Ui.Trim(_plants, types.Count);

        for (var index = 0; index < types.Count; index++)
        {
            if (index >= _plants.GetChildCount()) _plants.AddChild(PlantRow());

            var type = types[index];
            var row = _plants.GetChild<Button>(index);
            var line = row.GetChild(0);

            line.GetChild<TextureRect>(0).Texture = GD.Load<Texture2D>(Names.IconOf(type));
            line.GetChild<Label>(1).Text = Names.Of(type);
            line.GetChild<Label>(3).Text = $"есть {Fmt.Count(_loop.World.BuildingsOf(_loop.Player, type))}";
            line.GetChild<Label>(4).Text = Fmt.Cash(Cost(type).Exact);

            Ui.Paint(row, _plant == type ? Skin.Good : Skin.Dim);
            row.SetMeta("type", (int)type);
        }
    }

    private Button PlantRow()
    {
        var row = Ui.Act(string.Empty, Skin.Dim, null);
        row.CustomMinimumSize = new Vector2(0, 28);

        var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", 8);
        line.AnchorRight = 1;
        line.AnchorBottom = 1;
        line.OffsetLeft = 10;
        line.OffsetRight = -10;

        line.AddChild(Ui.Icon("res://assets/icons/ui/close.svg", 17, Skin.Text));
        line.AddChild(Ui.Text(string.Empty, 14, 600, Skin.Text));
        line.AddChild(Ui.Spring());
        line.AddChild(Ui.Number(string.Empty, 13, Skin.Dim, 90));
        line.AddChild(Ui.Number(string.Empty, 13, Skin.Money, 90));

        row.AddChild(line);
        row.Pressed += () =>
        {
            _plant = (BuildingType)(int)row.GetMeta("type");
            Refresh();
        };

        return row;
    }

    /// <summary>Что съедает выбранный завод за тик работы.</summary>
    private void ShowNeeds()
    {
        var inputs = _plant is { } type
            ? _loop.World.Buildings[type].Inputs.OrderByDescending(pair => pair.Value.Raw).ToList()
            : [];

        Ui.Trim(_needs, inputs.Count);

        for (var index = 0; index < inputs.Count; index++)
        {
            if (index >= _needs.GetChildCount()) _needs.AddChild(NeedRow());

            var (good, amount) = inputs[index];
            var row = _needs.GetChild(index);

            row.GetChild<TextureRect>(0).Texture = GD.Load<Texture2D>(Names.IconOf(good));
            row.GetChild<TextureRect>(0).Modulate = Names.ColourOf(good);
            row.GetChild<Label>(1).Text = Names.Of(good);
            row.GetChild<Label>(3).Text = Fmt.Amount(amount);
        }

        if (inputs.Count == 0 && _needs.GetChildCount() == 0)
        {
            _needs.AddChild(Ui.Text("Ничего: берётся из земли.", 13, 400, Skin.Dim));
        }
    }

    private static Control NeedRow()
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 8);

        row.AddChild(Ui.Icon("res://assets/icons/ui/close.svg", 15, Skin.Text));
        row.AddChild(Ui.Text(string.Empty, 14, 400, Skin.Text));
        row.AddChild(Ui.Spring());
        row.AddChild(Ui.Number(string.Empty, 14, Skin.Bright, 80));

        return row;
    }

    private void ShowSum()
    {
        var amount = Math.Min(_money, Purse());
        var price = _plant is { } type ? Cost(type).Exact : 0;

        _sum.Text = Fmt.Cash(amount);
        _accept.Disabled = amount <= 0 || _plant is null;

        _enough.Text = price > 0
            ? $"Хватит на {amount / price:0.00} предприятия"
            : "Выберите, что строить";

        _enough.AddThemeColorOverride("font_color", price > 0 && amount >= price ? Skin.Good : Skin.Dim);
        _price.Text = price > 0 ? Fmt.Cash(price) : "—";

        ShowBreakdown();
    }

    /// <summary>Из чего складывается цена постройки прямо сейчас.</summary>
    private void ShowBreakdown()
    {
        if (_plant is not { } type)
        {
            Ui.Trim(_breakdown, 0);
            return;
        }

        var prices = _loop.PlayerCountry.State.Prices;
        var info = _loop.World.Buildings[type];

        var rows = info.BuildCost
            .Select(pair => (
                Label: Names.Of(pair.Key),
                Icon: Names.IconOf(pair.Key),
                Tint: Names.ColourOf(pair.Key),
                Amount: Fmt.Amount(pair.Value),
                Worth: prices.CostOf(pair.Key, pair.Value)))
            .OrderByDescending(row => row.Worth.Raw)
            .ToList();

        rows.Add((
            "Работа строителей",
            Names.Ui("employment"),
            Skin.Labour,
            $"{Fmt.Count(info.BuildWorkers)} чел.",
            _loop.Simulation.BuildWageOf(_loop.Player, type)));

        Ui.Trim(_breakdown, rows.Count);

        for (var index = 0; index < rows.Count; index++)
        {
            if (index >= _breakdown.GetChildCount()) _breakdown.AddChild(CostRow());

            var row = _breakdown.GetChild(index);

            row.GetChild<TextureRect>(0).Texture = GD.Load<Texture2D>(rows[index].Icon);
            row.GetChild<TextureRect>(0).Modulate = rows[index].Tint;
            row.GetChild<Label>(1).Text = rows[index].Label;
            row.GetChild<Label>(3).Text = rows[index].Amount;
            row.GetChild<Label>(4).Text = Fmt.Cash(rows[index].Worth.Exact);
        }
    }

    private static Control CostRow()
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 8);

        row.AddChild(Ui.Icon("res://assets/icons/ui/close.svg", 14, Skin.Text));
        row.AddChild(Ui.Text(string.Empty, 13, 400, Skin.Text));
        row.AddChild(Ui.Spring());
        row.AddChild(Ui.Number(string.Empty, 13, Skin.Dim, 84));
        row.AddChild(Ui.Number(string.Empty, 13, Skin.Money, 96));

        return row;
    }

    /// <summary>Круглая плашка товара.</summary>
    private partial class Badge : Button
    {
        private const int Circle = 54;

        private ShaderMaterial _paint = null!;
        private Label _under = null!;

        public static Badge Create(GoodType good, Action pressed)
        {
            var badge = new Badge
            {
                CustomMinimumSize = new Vector2(Circle + 2, Circle + 16),
                FocusMode = FocusModeEnum.None,
                TooltipText = Names.Of(good),
            };

            badge.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
            badge.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
            badge.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
            badge.Pressed += pressed;

            badge._paint = new ShaderMaterial { Shader = GD.Load<Shader>("res://scenes/ui/badge.gdshader") };
            badge._paint.SetShaderParameter("tint", Names.ColourOf(good));

            var disc = new ColorRect
            {
                Material = badge._paint,
                Position = new Vector2(1, 0),
                Size = new Vector2(Circle, Circle),
                MouseFilter = MouseFilterEnum.Ignore,
            };

            badge.AddChild(disc);

            var icon = Ui.Icon(Names.IconOf(good), 28, Contrast(Names.ColourOf(good)));
            icon.Position = new Vector2(1 + (Circle - 28) / 2f, (Circle - 28) / 2f);
            icon.Size = new Vector2(28, 28);
            badge.AddChild(icon);

            badge._under = Ui.Number(string.Empty, 11, Skin.Text);
            badge._under.Position = new Vector2(0, Circle - 1);
            badge._under.Size = new Vector2(Circle + 2, 14);
            badge._under.HorizontalAlignment = HorizontalAlignment.Center;
            badge.AddChild(badge._under);

            return badge;
        }

        /// <summary>Цвет значка под цвет кружка: на тёмном светлый, на светлом тёмный.
        /// Один чёрный на всех сливался с половиной палитры.</summary>
        private static Color Contrast(Color disc)
        {
            var light = disc.R * 0.30f + disc.G * 0.59f + disc.B * 0.11f;

            return light > 0.55f ? disc.Darkened(0.78f) : disc.Lightened(0.86f);
        }

        public void Show(bool chosen, string under)
        {
            _paint.SetShaderParameter("chosen", chosen ? 1f : 0f);
            _paint.SetShaderParameter("dim", chosen ? 0f : 0.25f);

            _under.Text = under;
            _under.AddThemeColorOverride("font_color", chosen ? Skin.Bright : Skin.Dim);
        }
    }
}
