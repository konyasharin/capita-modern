using CapitaModern.Core.Economy;
using CapitaModern.Core.World;
using Country = CapitaModern.Core.World.Country;
using Godot;

/// <summary>Карточка по щелчку на карте: страна под курсором и её область. Правый нижний
/// угол — там она не закрывает ни панель, ни верхнюю строку.</summary>
public partial class SelectionPanel : PanelContainer
{
    private const int Width = 300;

    private GameLoop _loop = null!;
    private WorldMapView _map = null!;
    private PopoverStack _stack = null!;

    private TextureRect _flag = null!;
    private Label _name = null!;
    private PanelContainer _bloc = null!;
    private Label _blocText = null!;
    private VBoxContainer _rows = null!;

    private readonly List<StatRow> _stats = [];
    private Label _region = null!;
    private Label _buildings = null!;

    public override void _Ready()
    {
        _loop = GetNode<GameLoop>("/root/Game/GameLoop");
        _map = GetNode<WorldMapView>("/root/Game/WorldMapView");
        _stack = GetNode<PopoverStack>("/root/Game/Overlay/PopoverStack");
        _map.Clicked += Pick;

        Visible = false;
        CustomMinimumSize = new Vector2(Width, 0);
        AddThemeStyleboxOverride("panel", Skin.PopoverBox());

        // Прижата к правому нижнему углу и растёт влево и вверх: высота зависит от того,
        // сколько строк влезло, и заранее её не знать.
        AnchorLeft = 1;
        AnchorTop = 1;
        AnchorRight = 1;
        AnchorBottom = 1;
        OffsetLeft = -16;
        OffsetRight = -16;
        OffsetTop = -16;
        OffsetBottom = -16;
        GrowHorizontal = GrowDirection.Begin;
        GrowVertical = GrowDirection.Begin;

        var frame = new VBoxContainer();
        frame.AddThemeConstantOverride("separation", 6);
        AddChild(frame);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 8);
        frame.AddChild(head);

        _flag = new TextureRect
        {
            CustomMinimumSize = new Vector2(26, 26),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            TextureFilter = TextureFilterEnum.LinearWithMipmaps,
            Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://scenes/ui/flag.gdshader") },
        };

        head.AddChild(_flag);

        _name = Ui.Text("—", 17, 700, Skin.Bright);
        head.AddChild(_name);
        head.AddChild(Ui.Spring());

        _bloc = Ui.Chip("—", Skin.Dim);
        _blocText = _bloc.GetChild<Label>(0);
        head.AddChild(_bloc);

        var close = Ui.Act("✕", Skin.Dim, () => Visible = false);
        close.CustomMinimumSize = new Vector2(24, 0);
        head.AddChild(close);

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 3);
        frame.AddChild(_rows);

        foreach (var label in Labels)
        {
            var row = StatRow.Create(label.Text, _stack, label.Key);

            _stats.Add(row);
            _rows.AddChild(row);
        }

        frame.AddChild(Ui.Section("Область"));

        _region = Ui.Text("—", 14, 600, Skin.Bright);
        frame.AddChild(_region);

        _buildings = Ui.Text("—", 13, 400, Skin.Dim);
        _buildings.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _buildings.CustomMinimumSize = new Vector2(Width - 28, 0);
        frame.AddChild(_buildings);
    }

    private static (string Text, string Key)[] Labels =>
    [
        ("Население", "population"),
        ("ВВП за год", "output"),
        ("Инфляция", "inflation"),
        ("Курс к доллару", "rate"),
        ("Ключевая ставка", "keyrate"),
        ("Отношение к нам", "attitude"),
        ("Ввоз от нас", "imports"),
        ("Вывоз к нам", "exports"),
        ("Должна нам", "debt"),
        ("Путь до нас", "routes"),
    ];

    /// <summary>Что под точкой щелчка. Карта циклична, поэтому долгота сворачивается
    /// в ширину.</summary>
    /// <remarks>Берётся точка события, а не положение курсора: так выбор работает и с
    /// подставленным щелчком, которым снимают проверочные кадры.</remarks>
    private void Pick(Vector2 where)
    {
        var local = _map.MakeCanvasPositionLocal(where);
        var width = _map.Map.Width;
        var x = ((int)local.X % width + width) % width;
        var y = (int)local.Y;

        if (y < 0 || y >= _map.Map.Height)
        {
            Visible = false;
            return;
        }

        var owner = _map.Map.OwnerAt(x, y);
        if (owner == WorldMap.Ocean)
        {
            Visible = false;
            return;
        }

        Present(_loop.World.CountryById((byte)owner), _map.Regions.Cell[y * width + x]);
    }

    private void Present(Country country, ushort region)
    {
        Visible = true;

        var flag = $"res://assets/flags/{country.Iso}.svg";
        if (Godot.FileAccess.FileExists(flag))
        {
            var texture = FlagOf(flag);

            _flag.Texture = texture;
            ((ShaderMaterial)_flag.Material)
                .SetShaderParameter("aspect", (float)texture.GetWidth() / texture.GetHeight());
        }

        _name.Text = country.Name;

        var bloc = _loop.World.Relations.BlocOf(country.Id);
        _blocText.Text = Names.Of(bloc);
        _blocText.AddThemeColorOverride("font_color", Names.ColourOf(bloc));
        _bloc.AddThemeStyleboxOverride("panel", Skin.ChipBox(Names.ColourOf(bloc)));

        Fill(country);
        FillRegion(region);
    }

    private void Fill(Country country)
    {
        var sim = _loop.Simulation;
        var world = _loop.World;
        var they = country.Id;

        _stats[0].Set(Fmt.Count(world.PopulationOf(they).Whole));
        _stats[1].Set(Fmt.Cash(sim.ValueAddedOf(they).Exact * 365));

        var inflation = (sim.PriceLevelOf(they) - PriceLevel.Scale) * 100.0 / PriceLevel.Scale;
        _stats[2].Set(Fmt.Percent(inflation, signed: true), Fmt.Sign(inflation, moreIsBetter: false));

        _stats[3].Set($"{country.ExchangeRate.Exact:0.00}");
        _stats[4].Set(Fmt.Rate(country.KeyRate));

        var attitude = world.Relations.Between(_loop.Player, they);
        _stats[5].Set(attitude.ToString(), Fmt.Sign(attitude));

        _stats[6].Set(Fmt.Cash(country.ImportsPerDay.Exact));
        _stats[7].Set(Fmt.Cash(country.ExportsPerDay.Exact));

        var owed = default(Money);
        foreach (var loan in country.State.Treasury.Debt.Loans)
        {
            if (loan.Lender == _loop.Player) owed += loan.Principal;
        }

        _stats[8].Set(Fmt.Cash(owed.Exact), owed.Raw > 0 ? Skin.Money : Skin.Dim);

        var reachable = world.Routes.CanReach(_loop.Player, they);
        _stats[9].Set(reachable ? "есть" : "нет", reachable ? Skin.Good : Skin.Bad);
    }

    private void FillRegion(ushort id)
    {
        if (id == RegionMap.None)
        {
            _region.Text = "—";
            _buildings.Text = string.Empty;
            return;
        }

        var shown = _map.Regions.Regions.FirstOrDefault(region => region.Id == id);
        var core = _loop.World.Regions.FirstOrDefault(region => region.Id == id);

        _region.Text = $"{shown.Name} · {Fmt.Count(shown.Population)} чел.";

        if (core is null)
        {
            _buildings.Text = "нет данных о постройках";
            return;
        }

        var made = core.BuildingsCount
            .Where(pair => pair.Value > 0)
            .OrderByDescending(pair => pair.Value)
            .Take(6)
            .Select(pair => $"{Names.Of(pair.Key)} ×{pair.Value}");

        var deposits = Enum.GetValues<GoodType>()
            .Where(core.HasDeposit)
            .Select(Names.Of);

        var lines = string.Join(", ", made);
        var found = string.Join(", ", deposits);

        _buildings.Text = string.IsNullOrEmpty(found) ? lines : $"{lines}\nМесторождения: {found}";
    }

    /// <summary>Тот же приём, что и в верхней панели: SVG растеризуется под свой размер,
    /// иначе в кружке каша.</summary>
    private static ImageTexture FlagOf(string path)
    {
        var svg = Godot.FileAccess.GetFileAsBytes(path);
        var image = new Image();

        image.LoadSvgFromBuffer(svg);
        image.LoadSvgFromBuffer(svg, 26 * 4f / Mathf.Max(image.GetWidth(), image.GetHeight()));
        image.GenerateMipmaps();

        return ImageTexture.CreateFromImage(image);
    }
}
