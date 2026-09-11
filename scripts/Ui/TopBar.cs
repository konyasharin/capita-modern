using Godot;

/// <summary>Верхняя панель: тревоги слева, показатели страны игрока посередине, дата
/// справа.</summary>
/// <remarks>
/// Собирается кодом, а не сценой: состав панели будет меняться вместе с моделью, и
/// держать его в одном месте с форматированием проще, чем возить по узлам в редакторе.
/// </remarks>
public partial class TopBar : Control
{
    /// <summary>Высота панели. Под ней начинается всё остальное.</summary>
    public const int Height = 30;

    /// <summary>Кружок с флагом крупнее панели и свисает из-под неё: это знак страны, а
    /// не ещё один значок в ряду.</summary>
    private const int FlagSize = 40;

    private GameLoop _loop = null!;
    private PopoverStack _stack = null!;
    private History _past = null!;

    private Metric _population = null!;
    private Metric _employment = null!;
    private Metric _output = null!;
    private Metric _plants = null!;
    private Metric _inflation = null!;
    private Metric _treasury = null!;
    private Metric _debt = null!;
    private Metric _rate = null!;

    private Label _date = null!;
    private Control _flag = null!;

    public override void _Ready()
    {
        _loop = GetNode<GameLoop>("/root/Game/GameLoop");
        _stack = GetNode<PopoverStack>("/root/Game/Overlay/PopoverStack");
        _past = GetNode<History>("/root/Game/History");

        MouseFilter = MouseFilterEnum.Ignore;
        AddChild(Background());

        var middle = new HBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            Alignment = BoxContainer.AlignmentMode.Center,
        };

        middle.SetAnchorsPreset(LayoutPreset.FullRect);
        middle.AddThemeConstantOverride("separation", 14);
        AddChild(middle);

        // Слева от флага страна, справа её деньги: так восемь чисел читаются двумя
        // взглядами, а не восемью.
        _population = Add(middle, "population", Skin.People, 54, Trends.People(_past));
        _employment = Add(middle, "employment", Skin.Labour, 46, Trends.Employment(_past));
        _output = Add(middle, "output", Skin.Output, 60, Trends.Output(_past));
        _plants = Add(middle, "plants", Skin.Plants, 48, Trends.Plants(_past));

        middle.AddChild(Flag());

        _inflation = Add(middle, "inflation", Skin.Prices, 52, Trends.Inflation(_past));
        _treasury = Add(middle, "treasury", Skin.Money, 64, Trends.Treasury(_past));
        _debt = Add(middle, "debt", Skin.Owed, 58, Trends.Debt(_past));
        _rate = Add(middle, "rate", Skin.Rate, 46, Trends.Rate(_past));

        _date = new Label { MouseFilter = MouseFilterEnum.Ignore };
        _date.AddThemeFontOverride("font", Skin.Digits());
        _date.AddThemeColorOverride("font_color", Skin.Link);
        _date.AddThemeFontSizeOverride("font_size", 16);
        _date.SetAnchorsPreset(LayoutPreset.RightWide);
        _date.GrowHorizontal = GrowDirection.Begin;
        _date.HorizontalAlignment = HorizontalAlignment.Right;
        _date.VerticalAlignment = VerticalAlignment.Center;
        _date.OffsetLeft = -180;
        _date.OffsetRight = -16;
        AddChild(_date);

        var alerts = new AlertBar();
        alerts.SetAnchorsPreset(LayoutPreset.TopLeft);
        alerts.Position = new Vector2(Skin.RailWidth + 10, 3);
        AddChild(alerts);

        var speed = new SpeedBar();
        speed.SetAnchorsPreset(LayoutPreset.TopRight);
        speed.GrowHorizontal = GrowDirection.Begin;
        speed.Position = new Vector2(-16, Height + 8);
        GetParent().CallDeferred(Node.MethodName.AddChild, speed);
    }

    public override void _Process(double delta)
    {
        _population.Set(Fmt.Count(_loop.Population));

        var employment = _loop.Employment;
        _employment.Set(Fmt.Percent(employment), employment > 99.5 ? Skin.Bad : Skin.Bright);

        _output.Set(Fmt.Cash(_loop.YearlyOutput));
        _plants.Set(Fmt.Count(_loop.Plants));

        var inflation = _past.Yearly();
        _inflation.Set(Fmt.Percent(inflation, signed: true), inflation > 4 ? Skin.Bad : Skin.Bright);

        var treasury = _loop.Treasury;
        _treasury.Set(Fmt.Cash(treasury), treasury >= 0 ? Skin.Bright : Skin.Bad);

        var debt = _loop.ExternalDebt;
        _debt.Set(Fmt.Cash(debt), debt > 0 ? Skin.Bright : Skin.Dim);

        _rate.Set($"×{_loop.Rate:0.00}");

        _date.Text = _loop.Today.ToString("dd.MM.yyyy");

        if (_flag.GetGlobalRect().HasPoint(_flag.GetGlobalMousePosition())) _stack.Open(_flag, "flag", 0);
    }

    private Metric Add(
        Node parent,
        string key,
        Color tint,
        int width,
        Func<(string Key, Control Body)?>? card = null)
    {
        var icon = GD.Load<Texture2D>($"res://assets/icons/ui/{key}.svg");
        var metric = Metric.Create(_stack, key, icon, tint, width, card);
        parent.AddChild(metric);

        return metric;
    }

    private Control Flag()
    {
        var texture = FlagTexture(_loop.PlayerCountry.Iso);
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://scenes/ui/flag.gdshader") };

        // Круг вырезается из середины полотнища по настоящим пропорциям, иначе флаг
        // сплющивается в квадрат.
        material.SetShaderParameter("aspect", (float)texture.GetWidth() / texture.GetHeight());

        var circle = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = TextureFilterEnum.LinearWithMipmaps,
            MouseFilter = MouseFilterEnum.Stop,
            Texture = texture,
            Material = material,
        };

        // В ряду кружок занимает место только по ширине: будь он высоким, ряд показателей
        // съехал бы вниз вместе с ним.
        var holder = new Control
        {
            CustomMinimumSize = new Vector2(FlagSize, Height),
            MouseFilter = MouseFilterEnum.Ignore,
        };

        holder.AddChild(circle);

        // Размер после добавления: до него TextureRect подгоняется под картинку.
        circle.Position = new Vector2(0, 1);
        circle.Size = new Vector2(FlagSize, FlagSize);

        _flag = circle;

        return holder;
    }

    /// <summary>Растеризует флаг сам, под нужный размер.</summary>
    /// <remarks>Импортёр делает из SVG картинку в размер полотнища — тысяча пикселей на
    /// сорок экранных, и в кружке остаётся каша. Здесь запас вчетверо и мипмапы.</remarks>
    private static ImageTexture FlagTexture(string iso)
    {
        var svg = Godot.FileAccess.GetFileAsBytes($"res://assets/flags/{iso}.svg");
        var image = new Image();

        image.LoadSvgFromBuffer(svg);
        image.LoadSvgFromBuffer(svg, FlagSize * 4f / Mathf.Max(image.GetWidth(), image.GetHeight()));
        image.GenerateMipmaps();

        return ImageTexture.CreateFromImage(image);
    }

    private ColorRect Background()
    {
        var back = new ColorRect
        {
            MouseFilter = MouseFilterEnum.Stop,
            Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://scenes/ui/topbar.gdshader") },
        };

        back.SetAnchorsPreset(LayoutPreset.FullRect);

        return back;
    }
}
