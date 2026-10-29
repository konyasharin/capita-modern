using CapitaModern.Core.Map;
using CapitaModern.Core.World;
using Godot;
using CoreGame = CapitaModern.Core.Sim.Game;

/// <summary>Партия в сцене: грузит ядро, крутит время, держит выбор игрока.</summary>
public partial class Session : Node2D
{
    /// <summary>Сколько суток проходит за секунду на каждой скорости; нулевая — пауза.</summary>
    private static readonly double[] DaysPerSecond = [0, 1, 4, 10, 25, 60];

    /// <summary>Больше этого за кадр не считаем: иначе на медленной машине кадр растягивается и время копится.</summary>
    private const int MaxDaysPerFrame = 8;

    private double _pending;

    public CoreGame Game { get; private set; } = null!;
    public GameWorld World => Game.World;
    public MapPalette Palette { get; private set; } = null!;
    public CountryTable Table { get; private set; } = null!;
    public Names Names { get; private set; } = null!;

    public int Speed { get; private set; }
    public Region? Selected { get; private set; }

    public Color ColorOf(int country) => Table.ById(country) is { } c ? Palette.Of(c) : Palette.Ocean;

    /// <summary>Что-то поменялось: прошли сутки или игрок что-то выбрал.</summary>
    public event Action? Changed;

    public override void _Ready()
    {
        var started = Time.GetTicksMsec();
        Game = CoreGame.Load(
            path => Godot.FileAccess.GetFileAsString("res://" + path),
            path => Godot.FileAccess.GetFileAsBytes("res://" + path));
        Palette = MapPalette.Load("res://data/map/palette.json");
        Table = CountryTable.FromJson(Godot.FileAccess.GetFileAsString("res://data/map/countries.json"));
        Names = Names.Load();
        GD.Print($"мир собран за {Time.GetTicksMsec() - started} мс: регионов {World.Regions.Count()}, дивизий {World.Divisions.Count}");

        var map = GetNode<WorldMapView>("WorldMapView");
        map.Init(this);
        map.RegionClicked += Select;
        map.RegionOrdered += Order;

        GetNode<DivisionLayer>("Divisions").Init(this);
        GetNode<MapCamera>("MapCamera").SetBounds(map.MapSize);
        GetNode<Hud>("Ui/Hud").Init(this);

        Changed += map.SyncControllers;
        ApplyLaunchArgs();
    }

    /// <summary>Для проверок без мыши: godot -- --play RUS --war UKR --speed 3.</summary>
    private void ApplyLaunchArgs()
    {
        var args = OS.GetCmdlineUserArgs();
        string? Arg(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;

        if (Arg("--play") is { } iso && World.Countries.FirstOrDefault(c => c.Iso == iso) is { } country)
        {
            PlayAs(country.Id);
            Select(World.Region(country.Capital));
            GetNode<MapCamera>("MapCamera").FocusOn(new Vector2(Selected!.X, Selected.Y), 4f);

            if (Arg("--war") is { } enemy && World.Countries.FirstOrDefault(c => c.Iso == enemy) is { } target)
            {
                Game.DeclareWar(country.Id, target.Id);
            }
        }

        if (int.TryParse(Arg("--speed"), out var speed)) SetSpeed(speed);
        if (int.TryParse(Arg("--tab"), out var tab)) GetNode<Hud>("Ui/Hud").ShowTab(tab);
    }

    public override void _Process(double delta)
    {
        if (Speed == 0) return;

        _pending += delta * DaysPerSecond[Speed];
        var days = 0;
        while (_pending >= 1 && days < MaxDaysPerFrame)
        {
            Game.Tick();
            _pending -= 1;
            days++;
        }

        if (_pending > MaxDaysPerFrame) _pending = 0;
        if (days > 0) Changed?.Invoke();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;

        if (key.Keycode == Key.Space) SetSpeed(Speed == 0 ? 1 : 0);
        else if (key.Keycode is >= Key.Key1 and <= Key.Key5) SetSpeed((int)(key.Keycode - Key.Key0));
    }

    public void SetSpeed(int speed)
    {
        Speed = Math.Clamp(speed, 0, DaysPerSecond.Length - 1);
        Changed?.Invoke();
    }

    public void Select(Region? region)
    {
        Selected = region;
        Changed?.Invoke();
    }

    public void PlayAs(int country)
    {
        Game.Player = country;
        Changed?.Invoke();
    }

    /// <summary>Свои дивизии в выбранном регионе идут в указанный.</summary>
    public void Order(Region? target)
    {
        if (target is null || Selected is null || Game.Player is not { } player) return;

        foreach (var division in World.Divisions.Where(d => d.Alive && d.Country == player && d.Region == Selected.Id))
        {
            Game.Move(division, target.Id);
        }

        Changed?.Invoke();
    }

    /// <summary>Действие игрока поверх ядра: после него интерфейс перерисовывается.</summary>
    public void Act(Action action)
    {
        action();
        Changed?.Invoke();
    }
}
