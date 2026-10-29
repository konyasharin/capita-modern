using System.Text;
using CapitaModern.Core.Sim;
using CapitaModern.Core.World;
using Godot;

/// <summary>Интерфейс поверх карты: время сверху, справа — выбранный регион и его страна.</summary>
/// <remarks>Текст обновляется на ходу, а кнопки и списки пересобираются только при смене выбора:
/// иначе открытый список закрывался бы каждые четверть секунды.</remarks>
public partial class Hud : Control
{
    private const double RefreshSeconds = 0.25;

    private static readonly string[] Months =
        ["января", "февраля", "марта", "апреля", "мая", "июня", "июля", "августа", "сентября", "октября", "ноября", "декабря"];

    private Session _session = null!;
    private Label _date = null!;
    private Label _status = null!;
    private readonly List<Button> _speeds = [];
    private RichTextLabel _regionInfo = null!;
    private VBoxContainer _regionActions = null!;
    private RichTextLabel _countryInfo = null!;
    private VBoxContainer _countryActions = null!;
    private TabContainer _tabs = null!;

    private bool _dirty = true;
    private double _sinceRefresh;
    private string _actionsKey = "";

    /// <summary>Товар в торговой политике: панель пересобирается при смене налога, а выбор должен остаться.</summary>
    private int _tradeGood;

    public void Init(Session session)
    {
        _session = session;
        Theme = MakeTheme();
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        BuildTopBar();
        BuildSide();
        BuildHint();

        session.Changed += () => _dirty = true;
    }

    public override void _Process(double delta)
    {
        _sinceRefresh += delta;
        if (!_dirty || _sinceRefresh < RefreshSeconds) return;

        _dirty = false;
        _sinceRefresh = 0;
        Refresh();
    }

    private static Theme MakeTheme()
    {
        var theme = new Theme
        {
            DefaultFont = GD.Load<Font>("res://assets/fonts/RobotoCondensed.ttf"),
            DefaultFontSize = 15,
        };

        var panel = new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.08f, 0.10f, 0.92f),
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 8, ContentMarginBottom = 8,
        };
        theme.SetStylebox("panel", "PanelContainer", panel);
        return theme;
    }

    private void BuildTopBar()
    {
        var bar = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        bar.SetAnchorsPreset(LayoutPreset.TopWide);
        AddChild(bar);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        bar.AddChild(row);

        _date = new Label { CustomMinimumSize = new Vector2(150, 0) };
        _date.AddThemeFontSizeOverride("font_size", 18);
        row.AddChild(_date);

        string[] labels = ["❚❚", "1", "2", "3", "4", "5"];
        for (var i = 0; i < labels.Length; i++)
        {
            var speed = i;
            var button = new Button { Text = labels[i], ToggleMode = true, CustomMinimumSize = new Vector2(34, 0), FocusMode = FocusModeEnum.None };
            button.Pressed += () => _session.SetSpeed(speed);
            _speeds.Add(button);
            row.AddChild(button);
        }

        row.AddChild(new VSeparator());
        _status = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true };
        row.AddChild(_status);
    }

    private void BuildSide()
    {
        var side = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        side.SetAnchorsPreset(LayoutPreset.RightWide);
        side.OffsetLeft = -420;
        side.OffsetTop = 52;
        side.OffsetRight = -8;
        side.OffsetBottom = -8;
        AddChild(side);

        _tabs = new TabContainer();
        side.AddChild(_tabs);
        _session.Changed += () =>
        {
            if (_session.Selected is null && _session.Game.Player is not null) _tabs.CurrentTab = 1;
        };

        (_regionInfo, _regionActions) = Page("Регион");
        (_countryInfo, _countryActions) = Page("Страна");
    }

    private (RichTextLabel, VBoxContainer) Page(string title)
    {
        var scroll = new ScrollContainer { Name = title, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _tabs.AddChild(scroll);

        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(column);

        var info = new RichTextLabel { BbcodeEnabled = true, FitContent = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, SelectionEnabled = false };
        column.AddChild(info);

        var actions = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddChild(actions);
        return (info, actions);
    }

    private void BuildHint()
    {
        var hint = new Label
        {
            Text = "ЛКМ — регион · ПКМ — отправить свои дивизии из выбранного региона · колесо — масштаб · пробел — пауза · 1–5 — скорость",
            Modulate = new Color(1, 1, 1, 0.6f),
        };
        hint.SetAnchorsPreset(LayoutPreset.BottomLeft);
        hint.OffsetLeft = 12;
        hint.OffsetTop = -30;
        AddChild(hint);
    }

    public void ShowTab(int tab) => _tabs.CurrentTab = tab;

    private Country? ShownCountry =>
        _session.Selected is { } region ? _session.World.Country(region.Owner)
        : _session.Game.Player is { } player ? _session.World.Country(player) : null;

    private void Refresh()
    {
        var world = _session.World;
        _date.Text = $"{world.Date.Day} {Months[world.Date.Month - 1]} {world.Date.Year}";
        for (var i = 0; i < _speeds.Count; i++) _speeds[i].SetPressedNoSignal(i == _session.Speed);

        _status.Text = _session.Game.Player is { } player
            ? $"Вы — {_session.Names.Of(world.Country(player))} · {Brief(world.Country(player))}"
            : "Щёлкните по стране и нажмите «Играть» во вкладке «Страна». Пока вы наблюдатель.";

        _regionInfo.Text = _session.Selected is { } region ? RegionText(region) : "Щёлкните по региону на карте.";
        _countryInfo.Text = ShownCountry is { } country ? CountryText(country) : "";

        var key = $"{_session.Selected?.Id}|{_session.Selected?.Controller}|{_session.Selected?.Owner}|{_session.Game.Player}|"
            + $"{ShownCountry?.System}|{ShownCountry?.AutoBuild}|{ShownCountry?.AutoTax}|{Math.Round((ShownCountry?.TaxRate ?? 0) * 100)}|{world.Wars.Count}";
        if (key == _actionsKey) return;

        _actionsKey = key;
        RebuildRegionActions();
        RebuildCountryActions();
    }

    private string Brief(Country c) =>
        $"ВВП {Money(c.GdpReal * c.PriceLevel)} · инфляция {c.Inflation * 100:F1}% · казна {Money(c.Treasury)} · нужды {c.Satisfaction * 100:F0}%";

    private string RegionText(Region region)
    {
        var world = _session.World;
        var catalog = world.Catalog;
        var names = _session.Names;
        var text = new StringBuilder();

        text.AppendLine($"[font_size=20][b]{names.Of(region)}[/b][/font_size]");
        text.AppendLine($"Владелец: {names.Of(world.Country(region.Owner))}");
        if (region.Controller != region.Owner) text.AppendLine($"[color=#ff7a66]Занят: {names.Of(world.Country(region.Controller))}[/color]");
        if (!region.Supplied) text.AppendLine("[color=#ff7a66]Отрезан от столицы[/color]");
        text.AppendLine($"Население: {region.Population / 1e6:F2} млн");
        if (region.Deposits.Count > 0)
        {
            text.AppendLine("Месторождения: " + string.Join(", ", region.Deposits.Keys.Select(g => catalog.Goods[g].Name)));
        }

        text.AppendLine();
        text.AppendLine("[b]Предприятия[/b]");
        if (region.Plants.Count == 0) text.AppendLine("нет");
        foreach (var plant in region.Plants.Where(p => p.Count > 0).OrderByDescending(p => p.Count * EconomyStep.Added(catalog, p.Building)))
        {
            var owner = world.CompanyOf(plant.Company)?.Name ?? "государство";
            text.AppendLine($"• {catalog.Buildings[plant.Building].Name} ×{plant.Count} — {owner}, {plant.Load * 100:F0}%");
        }

        var projects = world.Projects.Where(p => p.Region == region.Id).ToList();
        if (projects.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("[b]Стройка[/b]");
            foreach (var group in projects.GroupBy(p => (p.Building, p.Company)))
            {
                var owner = world.CompanyOf(group.Key.Company)?.Name ?? "государство";
                var done = group.Sum(p => p.Count * p.DaysDone / p.DaysTotal) / group.Sum(p => p.Count);
                text.AppendLine($"• {catalog.Buildings[group.Key.Building].Name} ×{group.Sum(p => p.Count)} — готово {done * 100:F0}%, {owner}");
            }
        }

        var divisions = world.Divisions.Where(d => d.Alive && d.Region == region.Id).ToList();
        if (divisions.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("[b]Дивизии[/b]");
            foreach (var side in divisions.GroupBy(d => d.Country))
            {
                var kinds = string.Join(", ", side.GroupBy(d => d.Template).Select(g => $"{world.Templates[g.Key].Name} ×{g.Count()}"));
                var battle = side.Any(d => d.InBattle) ? " [color=#ff7a66]в бою[/color]" : "";
                text.AppendLine($"• {names.Of(world.Country(side.Key))}: {kinds}; готовность {side.Average(d => d.Organization) * 100:F0}%{battle}");
            }
        }

        return text.ToString();
    }

    private string CountryText(Country c)
    {
        var world = _session.World;
        var catalog = world.Catalog;
        var names = _session.Names;
        var text = new StringBuilder();

        text.AppendLine($"[font_size=20][b]{names.Of(c)}[/b][/font_size]");
        text.AppendLine($"Строй: {(c.System == EconomicSystem.Market ? "рынок — строят компании" : "план — строит государство")}");
        text.AppendLine($"ВВП: {Money(c.GdpReal * c.PriceLevel)} в год (в ценах старта {Money(c.GdpReal)})");
        text.AppendLine($"Инфляция: {c.Inflation * 100:F1}% · уровень цен ×{c.PriceLevel:F2}");
        text.AppendLine($"Казна: {Money(c.Treasury)} · налоги {Money(c.TaxesLast)} и расходы {Money(c.SpentLast)} в неделю");
        text.AppendLine($"Налог: {c.TaxRate * 100:F1}%{(c.AutoTax ? ", подбирает советник" : "")}");
        text.AppendLine($"Выработка: ×{c.Productivity:F2} к старту, растёт на {c.ProductivityGrowth * 100:F1}% в год · издержки ×{TradeStep.Cost(c, 0) / c.PriceMult[0]:F2}");
        text.AppendLine($"Нужды людей закрыты на {c.Satisfaction * 100:F0}%");

        var enemies = world.Enemies(c.Id).ToList();
        var divisions = world.Divisions.Count(d => d.Alive && d.Country == c.Id);
        text.AppendLine($"Дивизий: {divisions}" + (enemies.Count > 0
            ? $" · [color=#ff7a66]война с: {string.Join(", ", enemies.Select(e => names.Of(world.Country(e))))}[/color]" : ""));

        var plants = world.Regions.Where(r => r.Owner == c.Id).SelectMany(r => r.Plants).ToList();
        var byCompany = plants.GroupBy(p => p.Company).ToDictionary(g => g.Key, g => g.Sum(p => p.Count));
        text.AppendLine();
        text.AppendLine("[b]Кто владеет заводами[/b]");
        foreach (var (id, count) in byCompany.OrderByDescending(p => p.Value).Take(10))
        {
            var company = world.CompanyOf(id);
            text.AppendLine(company is null
                ? $"• Государство — {count} зд."
                : $"• {company.Name} — {count} зд., касса {Money(company.Cash)}, прибыль {Money(company.ProfitLast)}/нед");
        }

        var goods = catalog.Goods.Where(g => !g.Perishable && c.DailyUse[g.Index] > 0).ToList();
        text.AppendLine();
        text.AppendLine("[b]Меньше всего запаса[/b]");
        foreach (var good in goods.OrderBy(g => c.Stock[g.Index] / c.DailyUse[g.Index]).Take(6))
        {
            text.AppendLine($"• {good.Name}: {c.Stock[good.Index] / c.DailyUse[good.Index]:F0} сут., цена ×{c.PriceMult[good.Index]:F2}");
        }

        var exports = goods.Where(g => c.ExportedWeek[g.Index] > 0).OrderByDescending(g => c.ExportedWeek[g.Index] * g.BasePrice).Take(5).ToList();
        if (exports.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("[b]Главный вывоз[/b]");
            foreach (var good in exports) text.AppendLine($"• {good.Name}: {Money(c.ExportedWeek[good.Index] * good.BasePrice)}/нед");
        }

        return text.ToString();
    }

    private void RebuildRegionActions()
    {
        Clear(_regionActions);
        if (_session.Selected is not { } region || _session.Game.Player is not { } player) return;

        var world = _session.World;
        var catalog = world.Catalog;
        var me = world.Country(player);

        var mine = world.Divisions.Count(d => d.Alive && d.Country == player && d.Region == region.Id);
        if (mine > 0) _regionActions.AddChild(new Label { Text = $"Ваших дивизий здесь: {mine}. ПКМ по другому региону — отправить.", AutowrapMode = TextServer.AutowrapMode.WordSmart });

        if (region.Controller == player)
        {
            var allowed = catalog.Buildings.Where(b => b.Deposit < 0 || region.Deposits.ContainsKey(b.Deposit)).OrderBy(b => b.Name).ToList();
            var pick = new OptionButton { FitToLongestItem = false };
            foreach (var building in allowed) pick.AddItem(building.Name, building.Index);

            var cost = new Label();
            void ShowCost() => cost.Text = pick.Selected < 0 ? "" : $"Цена: {Money(BuildStep.Cost(catalog, me, pick.GetSelectedId()))}, казна {Money(me.Treasury)}";
            pick.ItemSelected += _ => ShowCost();
            ShowCost();

            var build = new Button { Text = "Построить за счёт казны" };
            build.Pressed += () =>
            {
                var ok = false;
                _session.Act(() => ok = _session.Game.Build(player, pick.GetSelectedId(), region.Id));
                cost.Text = ok ? "Стройка начата." : "Не хватает денег в казне.";
            };

            _regionActions.AddChild(new HSeparator());
            _regionActions.AddChild(new Label { Text = "Государственная стройка" });
            _regionActions.AddChild(pick);
            _regionActions.AddChild(cost);
            _regionActions.AddChild(build);
        }

        if (region.Owner == player)
        {
            var to = new OptionButton();
            foreach (var country in world.Countries.Where(c => c.Id != player).OrderBy(c => _session.Names.Of(c)))
            {
                to.AddItem(_session.Names.Of(country), country.Id);
            }

            var give = new Button { Text = "Передать регион" };
            give.Pressed += () => _session.Act(() => _session.Game.Transfer(region.Id, to.GetSelectedId()));

            _regionActions.AddChild(new HSeparator());
            _regionActions.AddChild(new Label { Text = "Передать другой стране" });
            _regionActions.AddChild(to);
            _regionActions.AddChild(give);
        }
    }

    private void RebuildCountryActions()
    {
        Clear(_countryActions);
        if (ShownCountry is not { } country) return;

        var world = _session.World;
        var names = _session.Names;
        var player = _session.Game.Player;

        if (player != country.Id)
        {
            var play = new Button { Text = $"Играть за: {names.Of(country)}" };
            play.Pressed += () => _session.PlayAs(country.Id);
            _countryActions.AddChild(play);
        }

        if (player is { } me && me != country.Id)
        {
            var atWar = world.AtWar(me, country.Id);
            var war = new Button { Text = atWar ? "Заключить мир (что держим — наше)" : "Объявить войну" };
            war.Pressed += () => _session.Act(() =>
            {
                if (atWar) _session.Game.MakePeace(me, country.Id);
                else _session.Game.DeclareWar(me, country.Id);
            });
            _countryActions.AddChild(war);
        }

        if (player != country.Id) return;

        _countryActions.AddChild(new HSeparator());
        _countryActions.AddChild(new Label { Text = "Законы" });

        var system = new OptionButton();
        system.AddItem("Рынок: строят компании, государство — при дефиците", (int)EconomicSystem.Market);
        system.AddItem("План: строит только государство", (int)EconomicSystem.Planned);
        system.Select(system.GetItemIndex((int)country.System));
        system.ItemSelected += index => _session.Act(() => _session.Game.SetSystem(country.Id, (EconomicSystem)system.GetItemId((int)index)));
        _countryActions.AddChild(system);

        var auto = new CheckBox { Text = "Госстройку ведёт советник", ButtonPressed = country.AutoBuild };
        auto.Toggled += on => _session.Act(() => country.AutoBuild = on);
        _countryActions.AddChild(auto);

        var autoTax = new CheckBox { Text = "Налог подбирает советник", ButtonPressed = country.AutoTax };
        autoTax.Toggled += on => _session.Act(() => country.AutoTax = on);
        _countryActions.AddChild(autoTax);

        var taxRow = new HBoxContainer();
        taxRow.AddChild(new Label { Text = "Налог с добавленной стоимости, %" });
        var tax = new SpinBox { MinValue = 0, MaxValue = 60, Step = 1, Value = Math.Round(country.TaxRate * 100) };
        tax.ValueChanged += value => _session.Act(() =>
        {
            country.TaxRate = value / 100;
            country.AutoTax = false;
        });
        taxRow.AddChild(tax);
        _countryActions.AddChild(taxRow);

        _countryActions.AddChild(new HSeparator());
        _countryActions.AddChild(new Label { Text = "Торговля товаром" });

        var good = new OptionButton();
        foreach (var item in world.Catalog.Goods.Where(g => !g.Perishable)) good.AddItem(item.Name, item.Index);
        if (good.GetItemIndex(_tradeGood) >= 0) good.Select(good.GetItemIndex(_tradeGood));
        _countryActions.AddChild(good);

        var subsidy = new SpinBox { MinValue = 0, MaxValue = 50, Step = 1, Prefix = "доплата экспортёрам", Suffix = "%" };
        var tariff = new SpinBox { MinValue = 0, MaxValue = 100, Step = 1, Prefix = "пошлина на ввоз", Suffix = "%" };
        void ShowTrade()
        {
            subsidy.SetValueNoSignal(Math.Round(country.ExportSubsidy[good.GetSelectedId()] * 100));
            tariff.SetValueNoSignal(Math.Round(country.Tariff[good.GetSelectedId()] * 100));
        }

        good.ItemSelected += _ =>
        {
            _tradeGood = good.GetSelectedId();
            ShowTrade();
        };
        subsidy.ValueChanged += value => _session.Act(() => country.ExportSubsidy[good.GetSelectedId()] = value / 100);
        tariff.ValueChanged += value => _session.Act(() => country.Tariff[good.GetSelectedId()] = value / 100);
        ShowTrade();

        _countryActions.AddChild(subsidy);
        _countryActions.AddChild(tariff);

        foreach (var enemy in world.Enemies(country.Id).ToList())
        {
            var peace = new Button { Text = $"Мир с: {names.Of(world.Country(enemy))}" };
            peace.Pressed += () => _session.Act(() => _session.Game.MakePeace(country.Id, enemy));
            _countryActions.AddChild(peace);
        }
    }

    private static void Clear(Node node)
    {
        foreach (var child in node.GetChildren()) child.QueueFree();
    }

    /// <summary>Деньги в ядре — тысячи долларов.</summary>
    private static string Money(double thousands)
    {
        var dollars = thousands * 1000;
        var size = Math.Abs(dollars);
        return size >= 1e12 ? $"{dollars / 1e12:F2} трлн $"
            : size >= 1e9 ? $"{dollars / 1e9:F1} млрд $"
            : size >= 1e6 ? $"{dollars / 1e6:F0} млн $"
            : $"{dollars / 1e3:F0} тыс. $";
    }
}
