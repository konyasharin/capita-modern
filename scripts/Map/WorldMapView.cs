using Godot;

/// <summary>
/// Рисует поле владения. Вся геометрия границ живёт в шейдере, здесь только
/// загрузка данных и заливка их в текстуры.
/// </summary>
public partial class WorldMapView : Sprite2D
{
    private const int WorldSeed = 20260814;

    private const string MapPath = "res://data/map/world.bin";
    private const string CountriesPath = "res://data/map/countries.json";
    private const string PalettePath = "res://data/map/palette.json";

    private ShaderMaterial _material = null!;
    private ImageTexture _ownerTex = null!;
    private int _hoverId;

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

        var image = Image.CreateFromData(Map.Width, Map.Height, false, Image.Format.R8, Map.Owner);
        _ownerTex = ImageTexture.CreateFromImage(image);
        Texture = _ownerTex;

        var started = Time.GetTicksMsec();
        Regions = RegionMap.Build(Map, WorldSeed);

        var regionImage = Image.CreateFromData(
            Map.Width, Map.Height, false, Image.Format.Rg8, Regions.ToRg8()
        );

        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://scenes/map/world.gdshader") };
        Material = _material;

        _material.SetShaderParameter("owner_tex", _ownerTex);
        _material.SetShaderParameter("region_tex", ImageTexture.CreateFromImage(regionImage));
        _material.SetShaderParameter("palette_tex", BuildPaletteTexture());
        _material.SetShaderParameter("map_size", MapSize);
        _material.SetShaderParameter("ocean_color", Palette.Ocean);
        _material.SetShaderParameter("ocean_deep_color", Palette.OceanDeep);
        _material.SetShaderParameter("ocean_shelf_color", Palette.OceanShelf);
        _material.SetShaderParameter("coast_color", Palette.Coast);
        _material.SetShaderParameter("border_color", Palette.Border);
        _material.SetShaderParameter("select_color", Palette.Selected);

        GD.Print($"map {Map.Width}x{Map.Height}, стран: {Countries.All.Count}, "
            + $"регионов: {Regions.Regions.Count} за {Time.GetTicksMsec() - started} мс");
    }

    /// <summary>Цвет каждой страны по её id: строка 256x1, индекс = id владельца.</summary>
    private ImageTexture BuildPaletteTexture()
    {
        var image = Image.CreateEmpty(256, 1, false, Image.Format.Rgba8);
        image.Fill(Palette.Ocean);

        foreach (var country in Countries.All)
        {
            image.SetPixel(country.Id, 0, Palette.Countries[country.Color % Palette.Countries.Length]);
        }

        return ImageTexture.CreateFromImage(image);
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

    public Country? CountryAt(Vector2 local) => Countries.ById(OwnerAt(local));

    public void SetHover(int id)
    {
        if (id == _hoverId)
        {
            return;
        }

        _hoverId = id;
        _material.SetShaderParameter("hover_id", id);
    }
}
