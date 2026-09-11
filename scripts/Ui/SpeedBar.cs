using Godot;

/// <summary>Скорость времени: пауза и множители. Правый край, под верхней панелью.</summary>
public partial class SpeedBar : HBoxContainer
{
    private static readonly int[] Steps = [4, 2, 1];

    private GameLoop _loop = null!;
    private Music _music = null!;
    private readonly Dictionary<int, Button> _buttons = [];
    private Button _pause = null!;
    private TextureRect _note = null!;

    public override void _Ready()
    {
        _loop = GetNode<GameLoop>("/root/Game/GameLoop");
        _music = GetNode<Music>("/root/Game/Music");

        AddThemeConstantOverride("separation", 4);
        AddChild(Sound());

        foreach (var step in Steps)
        {
            var speed = step;
            var button = Make($"x{step}");
            button.Pressed += () => _loop.SetSpeed(speed);

            _buttons[step] = button;
            AddChild(button);
        }

        // Пауза стоит последней и шире прочих: её жмут чаще всего.
        _pause = Make("II");
        _pause.Pressed += () => _loop.TogglePause();
        AddChild(_pause);
    }

    public override void _Process(double delta)
    {
        foreach (var (step, button) in _buttons)
        {
            Paint(button, !_loop.Paused && _loop.Speed == step);
        }

        Paint(_pause, _loop.Paused);
    }

    /// <summary>Кнопка громкости. Три ступени, а не «вкл-выкл»: тихая музыка под
    /// таблицами нужна чаще, чем полная.</summary>
    private Button Sound()
    {
        var button = new Button
        {
            CustomMinimumSize = new Vector2(26, 24),
            FocusMode = FocusModeEnum.None,
        };

        button.AddThemeStyleboxOverride("normal", Skin.SpeedBox(false));
        button.AddThemeStyleboxOverride("hover", Skin.SpeedBox(true));
        button.AddThemeStyleboxOverride("pressed", Skin.SpeedBox(true));

        _note = Ui.Icon(Names.Ui("sound"), 14, Skin.Link);
        _note.SetAnchorsPreset(LayoutPreset.Center);
        _note.Position = new Vector2(-7, -7);
        button.AddChild(_note);

        button.Pressed += Sounded;

        return button;
    }

    private void Sounded()
    {
        _music.Cycle();
        _note.Texture = GD.Load<Texture2D>(Names.Ui(_music.Loud ? "sound" : "mute"));
        _note.Modulate = _music.Loud ? Skin.Link : Skin.Dim;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.M }) Sounded();
    }

    private static Button Make(string text)
    {
        var button = new Button
        {
            Text = text,
            FocusMode = FocusModeEnum.None,
            MouseFilter = MouseFilterEnum.Stop,
        };

        button.AddThemeFontOverride("font", Skin.Weight(600));
        button.AddThemeFontSizeOverride("font_size", 15);

        return button;
    }

    private static void Paint(Button button, bool active)
    {
        button.AddThemeStyleboxOverride("normal", Skin.SpeedBox(active));
        button.AddThemeStyleboxOverride("hover", Skin.SpeedBox(true));
        button.AddThemeStyleboxOverride("pressed", Skin.SpeedBox(true));
        button.AddThemeColorOverride("font_color", active ? Skin.Bright : Skin.Dim);
        button.AddThemeColorOverride("font_hover_color", Skin.Bright);
        button.AddThemeColorOverride("font_pressed_color", Skin.Bright);
    }
}
