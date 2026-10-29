using CapitaModern.Core.Map;
using CapitaModern.Core.World;
using Godot;

/// <summary>
/// Рисует поле владения. Вся геометрия границ живёт в шейдере, здесь только
/// загрузка данных и заливка их в текстуры. Цвет региона — его нынешний держатель.
/// </summary>
public partial class WorldMapView : Sprite2D
{
    /// <summary>Насколько линия области бледнее границы страны.</summary>
    private const float RegionLineAlpha = 0.70f;

    private Session _session = null!;
    private ImageTexture _ownerTex = null!;
    private Image _ownerImage = null!;
    private byte[] _owner = null!;

    /// <summary>Ячейки каждого региона: при смене держателя перекрашиваются только они.</summary>
    private int[][] _cells = null!;
    private int[] _shown = null!;

    public WorldMap Map => _session.World.Map;
    public Vector2 MapSize => new(Map.Width, Map.Height);

    public event Action<Region?>? RegionClicked;
    public event Action<Region?>? RegionOrdered;

    public void Init(Session session)
    {
        _session = session;
        var world = session.World;
        var grid = world.Grid;
        if (grid.Width != Map.Width || grid.Height != Map.Height)
        {
            GD.PushError($"сетка регионов {grid.Width}x{grid.Height} не совпадает с картой {Map.Width}x{Map.Height}");
        }

        Centered = false;
        TextureFilter = TextureFilterEnum.Nearest;

        _owner = (byte[])Map.Owner.Clone();
        _cells = CellsByRegion(grid, world.RegionById.Length);
        _shown = new int[world.RegionById.Length];
        foreach (var region in world.Regions) _shown[region.Id] = region.Owner;

        _ownerImage = Image.CreateFromData(Map.Width, Map.Height, false, Image.Format.R8, _owner);
        _ownerTex = ImageTexture.CreateFromImage(_ownerImage);
        Texture = _ownerTex;

        var control = Image.CreateFromData(Map.Width, Map.Height, false, Image.Format.R8, ControlBytes());
        var regions = Image.CreateFromData(grid.Width, grid.Height, false, Image.Format.Rg8, grid.ToRg8());
        var palette = session.Palette;

        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://scenes/map/world.gdshader") };
        Material = material;

        material.SetShaderParameter("owner_tex", _ownerTex);
        material.SetShaderParameter("control_tex", ImageTexture.CreateFromImage(control));
        material.SetShaderParameter("region_tex", ImageTexture.CreateFromImage(regions));
        material.SetShaderParameter("palette_tex", BuildPaletteTexture());
        material.SetShaderParameter("map_size", MapSize);
        material.SetShaderParameter("ocean_color", palette.Ocean);
        material.SetShaderParameter("ocean_deep_color", palette.OceanDeep);
        material.SetShaderParameter("ocean_shelf_color", palette.OceanShelf);
        material.SetShaderParameter("coast_color", palette.Coast);
        material.SetShaderParameter("border_color", palette.Border);
        material.SetShaderParameter("region_line_color", new Color(palette.RegionLine, RegionLineAlpha));

        AddWrapCopies();
        SyncControllers();
    }

    private static int[][] CellsByRegion(RegionGrid grid, int regions)
    {
        var lists = new List<int>[regions];
        for (var i = 0; i < grid.Cell.Length; i++)
        {
            var id = grid.Cell[i];
            if (id == RegionGrid.None || id >= regions) continue;
            (lists[id] ??= []).Add(i);
        }

        return lists.Select(l => l?.ToArray() ?? []).ToArray();
    }

    /// <summary>Перекрасить регионы, у которых сменился держатель.</summary>
    public void SyncControllers()
    {
        var changed = false;
        foreach (var region in _session.World.Regions)
        {
            if (_shown[region.Id] == region.Controller) continue;

            _shown[region.Id] = region.Controller;
            foreach (var cell in _cells[region.Id]) _owner[cell] = (byte)region.Controller;
            changed = true;
        }

        if (!changed) return;
        _ownerImage.SetData(Map.Width, Map.Height, false, Image.Format.R8, _owner);
        _ownerTex.Update(_ownerImage);
    }

    /// <summary>Копии карты слева и справа: мир замкнут по долготе, и за краем должна
    /// быть карта, а не пустота. Материал и текстуры общие, так что стоит это почти ничего.</summary>
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
        var palette = _session.Palette;
        var image = Image.CreateEmpty(256, 1, false, Image.Format.Rgba8);
        image.Fill(palette.Ocean);

        foreach (var country in _session.Table.All) image.SetPixel(country.Id, 0, palette.Of(country));

        return ImageTexture.CreateFromImage(image);
    }

    private byte[] ControlBytes()
    {
        var data = new byte[Map.Control.Length];
        for (var i = 0; i < data.Length; i++) data[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(Map.Control[i] * 255f), 0, 255);

        return data;
    }

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

            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } when !_dragging:
                RegionClicked?.Invoke(RegionUnderMouse());
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
                RegionOrdered?.Invoke(RegionUnderMouse());
                break;
        }
    }

    private Region? RegionUnderMouse()
    {
        var local = GetLocalMousePosition();
        return _session.World.RegionAt(Mathf.PosMod((int)Mathf.Floor(local.X), Map.Width), (int)Mathf.Floor(local.Y));
    }
}
