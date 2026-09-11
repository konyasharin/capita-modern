using Godot;

/// <summary>Один показатель в верхней панели: значок и число.</summary>
public partial class Metric : HBoxContainer
{
    private Label _value = null!;
    private string _key = string.Empty;
    private PopoverStack _stack = null!;

    public static Metric Create(PopoverStack stack, string key, Texture2D icon, Color tint)
    {
        var metric = new Metric
        {
            _key = key,
            _stack = stack,
            MouseFilter = MouseFilterEnum.Stop,
        };

        metric.AddThemeConstantOverride("separation", 4);

        metric.AddChild(new TextureRect
        {
            Texture = icon,
            // Иконки лежат квадратами по 512: без IgnoreSize они растягивают панель собой.
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(17, 17),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            Modulate = tint,
            MouseFilter = MouseFilterEnum.Ignore,
        });

        metric._value = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            VerticalAlignment = VerticalAlignment.Center,
        };

        metric._value.AddThemeFontOverride("font", Skin.Weight(600));
        metric._value.AddThemeColorOverride("font_color", Skin.Bright);
        metric._value.AddThemeFontSizeOverride("font_size", 16);
        metric.AddChild(metric._value);

        return metric;
    }

    /// <summary>Наведение ловится положением курсора, а не сигналом входа: сигнал не
    /// приходит, если мышь поставили программно, а проверять прямоугольник всё равно
    /// приходится — подсказка так же и закрывается.</summary>
    public override void _Process(double delta)
    {
        if (GetGlobalRect().HasPoint(GetGlobalMousePosition())) _stack.Open(this, _key, 0);
    }

    public void Set(string text) => _value.Text = text;

    public void Set(string text, Color colour)
    {
        _value.Text = text;
        _value.AddThemeColorOverride("font_color", colour);
    }
}
