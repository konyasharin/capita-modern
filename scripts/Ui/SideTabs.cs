using Godot;

/// <summary>Полоса вкладок слева и панель, которую она открывает. Открыта не больше
/// одной: две панели рядом закрыли бы половину карты.</summary>
public partial class SideTabs : Control
{
    /// <summary>Как часто панель пересчитывает свои числа. Каждый кадр незачем: сутки
    /// идут за секунду, а таблица на сорок строк не бесплатна.</summary>
    private const double RefreshEvery = 0.25;

    private readonly List<SidePanel> _panels = [];
    private readonly List<Button> _tabs = [];

    private int _open = -1;
    private double _left;

    public override void _Ready()
    {
        var loop = GetNode<GameLoop>("/root/Game/GameLoop");
        var stack = GetNode<PopoverStack>("/root/Game/Overlay/PopoverStack");

        MouseFilter = MouseFilterEnum.Ignore;
        Stretch(this, 0, Skin.RailWidth + Skin.PanelWidth, TopBar.Height);

        var rail = new PanelContainer();
        rail.AddThemeStyleboxOverride("panel", Skin.RailBox());
        AddChild(rail);
        Stretch(rail, 0, Skin.RailWidth, 0);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 2);
        rail.AddChild(column);

        SidePanel[] panels =
        [
            new OverviewPanel(),
            new GoodsPanel(),
            new IndustryPanel(),
            new TradePanel(),
            new FinancePanel(),
            new PoliticsPanel(),
        ];

        foreach (var panel in panels)
        {
            var at = _panels.Count;

            panel.Setup(loop, stack);
            panel.Visible = false;
            panel.Closed = () => Show(-1);
            _panels.Add(panel);
            AddChild(panel);
            Stretch(panel, Skin.RailWidth, Skin.RailWidth + Skin.PanelWidth, 0);

            var tab = Tab(panel, stack);
            tab.Pressed += () => Show(_open == at ? -1 : at);

            _tabs.Add(tab);
            column.AddChild(tab);
        }

        // Окно производства открывается той же полосой, но панелью не является: оно на
        // весь экран, и место в ряду вкладок ему нужно только под кнопку.
        column.AddChild(Ui.Gap(10));
        column.AddChild(Big(stack));

        Paint();
    }

    private Button Big(PopoverStack stack)
    {
        var window = GetNode<ResourceWindow>("/root/Game/Overlay/ResourceWindow");

        var button = new Button
        {
            CustomMinimumSize = new Vector2(Skin.RailWidth, 40),
            FocusMode = FocusModeEnum.None,
            TooltipText = "Производство",
        };

        button.AddThemeStyleboxOverride("normal", Skin.TabBox(false));
        button.AddThemeStyleboxOverride("hover", Skin.TabBox(true));
        button.AddThemeStyleboxOverride("pressed", Skin.TabBox(true));
        button.AddChild(new HoverProbe { Stack = stack, Key = "tab-resources" });

        var icon = Ui.Icon(Names.Ui("tab-resources"), 22, Skin.Money);
        icon.SetAnchorsPreset(LayoutPreset.Center);
        icon.Position = new Vector2(-11, -11);
        button.AddChild(icon);

        button.Pressed += window.Toggle;

        return button;
    }

    /// <summary>Открывает вкладку с номером. Минус один — закрыть всё.</summary>
    public void Show(int index)
    {
        _open = index;

        for (var at = 0; at < _panels.Count; at++) _panels[at].Visible = at == index;

        Paint();
        if (index >= 0) _panels[index].Refresh();
    }

    public override void _Process(double delta)
    {
        if (_open < 0) return;

        _left -= delta;
        if (_left > 0) return;

        _left = RefreshEvery;
        _panels[_open].Refresh();
    }

    /// <summary>Растягивает по высоте между двумя краями. Одним пресетом якорей этого не
    /// добиться: он оставляет смещения такими, какими их посчитал по нынешнему размеру.</summary>
    private static void Stretch(Control control, int left, int right, int top)
    {
        control.AnchorLeft = 0;
        control.AnchorRight = 0;
        control.AnchorTop = 0;
        control.AnchorBottom = 1;
        control.OffsetLeft = left;
        control.OffsetRight = right;
        control.OffsetTop = top;
        control.OffsetBottom = 0;
    }

    private Button Tab(SidePanel panel, PopoverStack stack)
    {
        var tab = new Button
        {
            CustomMinimumSize = new Vector2(Skin.RailWidth, 40),
            FocusMode = FocusModeEnum.None,
            TooltipText = panel.Title,
        };

        tab.AddChild(new HoverProbe { Stack = stack, Key = $"tab-{panel.Icon}" });

        var icon = Ui.Icon(Names.Ui($"tab-{panel.Icon}"), 22, Skin.Dim);
        icon.SetAnchorsPreset(LayoutPreset.Center);
        icon.Position = new Vector2(-11, -11);
        tab.AddChild(icon);

        return tab;
    }

    private void Paint()
    {
        for (var at = 0; at < _tabs.Count; at++)
        {
            var active = at == _open;

            _tabs[at].AddThemeStyleboxOverride("normal", Skin.TabBox(active));
            _tabs[at].AddThemeStyleboxOverride("hover", Skin.TabBox(true));
            _tabs[at].AddThemeStyleboxOverride("pressed", Skin.TabBox(true));
            _tabs[at].GetChild<TextureRect>(1).Modulate = active ? Skin.Link : Skin.Dim;
        }
    }
}
