using Godot;

/// <summary>Верхняя панель: показатели страны игрока посередине, дата справа.</summary>
/// <remarks>
/// Собирается кодом, а не сценой: состав панели будет меняться вместе с моделью, и
/// держать его в одном месте с форматированием проще, чем возить по узлам в редакторе.
/// </remarks>
public partial class TopBar : Control
{
    private const int Height = 30;

    /// <summary>Кружок с флагом крупнее панели и свисает из-под неё: это знак страны, а
    /// не ещё один значок в ряду.</summary>
    private const int FlagSize = 40;

    private GameLoop _loop = null!;
    private PopoverStack _stack = null!;

    private Metric _population = null!;
    private Metric _output = null!;
    private Metric _plants = null!;
    private Metric _inflation = null!;
    private Metric _treasury = null!;
    private Label _date = null!;
    private Control _flag = null!;

    public override void _Ready()
    {
        _loop = GetNode<GameLoop>("/root/Game/GameLoop");

        MouseFilter = MouseFilterEnum.Ignore;
        AddChild(Background());

        _stack = new PopoverStack { Glossary = Glossary.Load() };

        var middle = new HBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            Alignment = BoxContainer.AlignmentMode.Center,
        };

        middle.SetAnchorsPreset(LayoutPreset.FullRect);
        middle.AddThemeConstantOverride("separation", 15);
        AddChild(middle);

        _population = Add(middle, "population", Skin.People, 56);
        _output = Add(middle, "output", Skin.Output, 62);
        _plants = Add(middle, "plants", Skin.Plants, 50);

        middle.AddChild(Flag());

        _inflation = Add(middle, "inflation", Skin.Prices, 52);
        _treasury = Add(middle, "treasury", Skin.Money, 66);

        _date = new Label { MouseFilter = MouseFilterEnum.Ignore };
        _date.AddThemeFontOverride("font", Skin.Weight(600));
        _date.AddThemeColorOverride("font_color", Skin.Link);
        _date.AddThemeFontSizeOverride("font_size", 16);
        _date.SetAnchorsPreset(LayoutPreset.RightWide);
        _date.GrowHorizontal = GrowDirection.Begin;
        _date.HorizontalAlignment = HorizontalAlignment.Right;
        _date.VerticalAlignment = VerticalAlignment.Center;
        _date.OffsetLeft = -180;
        _date.OffsetRight = -16;
        AddChild(_date);

        var speed = new SpeedBar();
        speed.SetAnchorsPreset(LayoutPreset.TopRight);
        speed.GrowHorizontal = GrowDirection.Begin;
        speed.Position = new Vector2(-16, Height + 8);
        GetParent().CallDeferred(Node.MethodName.AddChild, speed);

        // Подсказки поверх всего: панель им не хозяин, иначе они обрежутся её высотой.
        GetParent().CallDeferred(Node.MethodName.AddChild, _stack);
    }

    public override void _Process(double delta)
    {
        _population.Set(Short(_loop.Population));
        _output.Set($"{Money(_loop.YearlyOutput)}$");
        _plants.Set(Short(_loop.Plants));

        var inflation = _loop.Inflation;
        _inflation.Set($"{inflation:+0.0;-0.0;0.0}%", inflation > 0.05 ? Skin.Bad : Skin.Bright);

        var treasury = _loop.Treasury;
        _treasury.Set($"{Money(treasury)}$", treasury >= 0 ? Skin.Bright : Skin.Bad);

        _date.Text = _loop.Today.ToString("dd.MM.yyyy");

        if (_flag.GetGlobalRect().HasPoint(_flag.GetGlobalMousePosition())) _stack.Open(_flag, "flag", 0);
    }

    private Metric Add(Node parent, string key, Color tint, int width)
    {
        var icon = GD.Load<Texture2D>($"res://assets/icons/ui/{key}.svg");
        var metric = Metric.Create(_stack, key, icon, tint, width);
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

    /// <summary>Людей и штуки — тысячами и миллионами, иначе в панель не влезет.</summary>
    private static string Short(long value) => value switch
    {
        >= 1_000_000_000 => $"{value / 1e9:0.##}B",
        >= 1_000_000 => $"{value / 1e6:0.##}M",
        >= 1_000 => $"{value / 1e3:0.#}K",
        _ => value.ToString(),
    };

    private static string Money(double value)
    {
        var sign = value < 0 ? "-" : string.Empty;
        var size = System.Math.Abs(value);

        return size switch
        {
            >= 1e12 => $"{sign}{size / 1e12:0.##}T",
            >= 1e9 => $"{sign}{size / 1e9:0.##}B",
            >= 1e6 => $"{sign}{size / 1e6:0.##}M",
            >= 1e3 => $"{sign}{size / 1e3:0.#}K",
            _ => $"{sign}{size:0}",
        };
    }
}
