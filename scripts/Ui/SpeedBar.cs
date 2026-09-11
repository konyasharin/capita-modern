using Godot;

/// <summary>Скорость времени: пауза и множители. Правый край, под верхней панелью.</summary>
public partial class SpeedBar : HBoxContainer
{
    private static readonly int[] Steps = [4, 2, 1];

    private GameLoop _loop = null!;
    private readonly Dictionary<int, Button> _buttons = [];
    private Button _pause = null!;

    public override void _Ready()
    {
        _loop = GetNode<GameLoop>("/root/Game/GameLoop");

        AddThemeConstantOverride("separation", 4);

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
