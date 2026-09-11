using Godot;

/// <summary>
/// Рисует поле владения. Вся геометрия границ живёт в шейдере, здесь только
/// загрузка данных и заливка их в текстуры.
/// </summary>
public partial class WorldMapView : Sprite2D
{
    private const string MapPath = "res://data/map/world.bin";
    private const string RegionsPath = "res://data/map/regions.json";
    private const string RegionsBinPath = "res://data/map/regions.bin";
    private const string CountriesPath = "res://data/map/countries.json";
    private const string PalettePath = "res://data/map/palette.json";

    /// <summary>Насколько линия области бледнее границы страны.</summary>
    private const float RegionLineAlpha = 0.70f;

    private ShaderMaterial _material = null!;
    private ImageTexture _ownerTex = null!;
    private ImageTexture _controlTex = null!;
    private Image _ownerImage = null!;
    private Image _controlImage = null!;

    public WorldMap Map { get; private set; } = null!;
    public CountryTable Countries { get; private set; } = null!;
    public RegionMap Regions { get; private set; } = null!;
    public MapPalette Palette { get; private set; } = null!;

    public Vector2 MapSize => new(Map.Width, Map.Height);

    public override void _Ready()
    {
        Map = WorldMap.FromBytes(Godot.FileAccess.GetFileAsBytes(MapPath));
        Countries = CountryTable.FromJson(Godot.FileAccess.GetFileAsString(CountriesPath));
        Palette = MapPalette.Load(PalettePath);

        Centered = false;
        TextureFilter = TextureFilterEnum.Nearest;

        _ownerImage = Image.CreateFromData(Map.Width, Map.Height, false, Image.Format.R8, Map.Owner);
        _ownerTex = ImageTexture.CreateFromImage(_ownerImage);
        Texture = _ownerTex;

        _controlImage = Image.CreateFromData(Map.Width, Map.Height, false, Image.Format.R8, ControlBytes());
        _controlTex = ImageTexture.CreateFromImage(_controlImage);

        var started = Time.GetTicksMsec();
        Regions = RegionMap.FromData(
            Map,
            Godot.FileAccess.GetFileAsString(RegionsPath),
            Godot.FileAccess.GetFileAsBytes(RegionsBinPath)
        );

        var regionImage = Image.CreateFromData(
            Map.Width, Map.Height, false, Image.Format.Rg8, Regions.ToRg8()
        );

        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://scenes/map/world.gdshader") };
        Material = _material;

        _material.SetShaderParameter("owner_tex", _ownerTex);
        _material.SetShaderParameter("control_tex", _controlTex);
        _material.SetShaderParameter("region_tex", ImageTexture.CreateFromImage(regionImage));
        _material.SetShaderParameter("palette_tex", BuildPaletteTexture());
        _material.SetShaderParameter("map_size", MapSize);
        _material.SetShaderParameter("ocean_color", Palette.Ocean);
        _material.SetShaderParameter("ocean_deep_color", Palette.OceanDeep);
        _material.SetShaderParameter("ocean_shelf_color", Palette.OceanShelf);
        _material.SetShaderParameter("coast_color", Palette.Coast);
        _material.SetShaderParameter("border_color", Palette.Border);
        _material.SetShaderParameter("region_line_color",
            new Color(Palette.RegionLine, RegionLineAlpha));

        AddWrapCopies();

        GD.Print($"map {Map.Width}x{Map.Height}, стран: {Countries.All.Count}, "
            + $"регионов: {Regions.Regions.Count} за {Time.GetTicksMsec() - started} мс");
    }

    /// <summary>Копии карты слева и справа: мир замкнут по долготе, и за краем должна
    /// быть карта, а не пустота. Материал и текстуры общие, так что стоит это почти
    /// ничего — рисуется только то, что попало в кадр.</summary>
    private void AddWrapCopies()
    {
        foreach (var shift in new[] { -Map.Width, Map.Width })
        {
            AddChild(new Sprite2D
            {
                Texture = Texture,
                Material = Material,
                Centered = false,
                TextureFilter = TextureFilterEnum.Nearest,
                Position = new Vector2(shift, 0f),
            });
        }
    }

    /// <summary>Цвет каждой страны по её id: строка 256x1, индекс = id владельца.</summary>
    private ImageTexture BuildPaletteTexture()
    {
        var image = Image.CreateEmpty(256, 1, false, Image.Format.Rgba8);
        image.Fill(Palette.Ocean);

        foreach (var country in Countries.All)
        {
            var colour = Palette.ByIso.TryGetValue(country.Iso, out var own)
                ? own
                : Palette.Countries[country.Color % Palette.Countries.Length];

            image.SetPixel(country.Id, 0, colour);
        }

        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>Прочность владения в байтах: шейдеру нужна текстура, а не float.</summary>
    private byte[] ControlBytes()
    {
        var data = new byte[Map.Control.Length];

        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(Map.Control[i] * 255f), 0, 255);
        }

        return data;
    }

    /// <summary>Перезалить поле владения в текстуры после того, как его подвинули.</summary>
    public void RefreshField()
    {
        _ownerImage.SetData(Map.Width, Map.Height, false, Image.Format.R8, Map.Owner);
        _ownerTex.Update(_ownerImage);

        _controlImage.SetData(Map.Width, Map.Height, false, Image.Format.R8, ControlBytes());
        _controlTex.Update(_controlImage);
    }

    /// <summary>Щелчок по карте, в точке экрана. Перетаскивание сюда не попадает.</summary>
    public Action<Vector2>? Clicked;

    /// <summary>Насколько курсор может сдвинуться, чтобы щелчок ещё считался щелчком.</summary>
    private const float Slack = 4f;

    private Vector2 _pressed;
    private bool _dragging;

    public override void _UnhandledInput(InputEvent @event)
    {
        // Карту таскают той же левой кнопкой, поэтому щелчок отделяется по пройденному пути.
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } down:
                _pressed = down.Position;
                _dragging = false;
                break;

            case InputEventMouseMotion motion when motion.Position.DistanceTo(_pressed) > Slack:
                _dragging = true;
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } up when !_dragging:
                Clicked?.Invoke(up.Position);
                break;
        }
    }

    /// <summary>Владелец ячейки под точкой в координатах карты, 0 — океан или мимо.</summary>
    public int OwnerAt(Vector2 local)
    {
        var x = (int)local.X;
        var y = (int)local.Y;

        if (x < 0 || y < 0 || x >= Map.Width || y >= Map.Height)
        {
            return WorldMap.Ocean;
        }

        return Map.OwnerAt(x, y);
    }

    public MapCountry? CountryAt(Vector2 local) => Countries.ById(OwnerAt(local));
}
