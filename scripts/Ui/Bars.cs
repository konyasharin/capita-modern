using Godot;

/// <summary>Одна полоса: значок, подпись, длина и число.</summary>
/// <param name="About">Что рассказать при наведении. Пусто — строка молчит.</param>
public readonly record struct Slice(
    string Label,
    double Value,
    string Text,
    Color Colour,
    string? Icon = null,
    Func<(string Key, Control Body)?>? About = null);

/// <summary>Столбик горизонтальных полос. Там, где важно не точное число, а кто больше
/// кого, полосы читаются с одного взгляда, а колонка цифр — нет.</summary>
public partial class Bars : VBoxContainer
{
    private readonly List<BarRow> _rows = [];
    private PopoverStack? _stack;
    private Label? _empty;

    public static Bars Create(PopoverStack? stack = null)
    {
        var bars = new Bars { MouseFilter = MouseFilterEnum.Ignore, _stack = stack };
        bars.AddThemeConstantOverride("separation", 3);

        return bars;
    }

    /// <summary>Подпись, которая показывается вместо полос, когда их нет. Пустое место
    /// читается как поломка, а «никого» — как ответ.</summary>
    public Control Empty(string text)
    {
        _empty = Ui.Text(text, 13, 400, Skin.Dim);

        return _empty;
    }

    /// <summary>Длина полосы считается от наибольшего в наборе, а не от общей суммы:
    /// сравнивают здесь соседей, а не доли целого.</summary>
    public void Show(IReadOnlyList<Slice> slices)
    {
        var top = 0.0;
        foreach (var slice in slices) top = Math.Max(top, Math.Abs(slice.Value));

        if (_empty is not null) _empty.Visible = slices.Count == 0;

        while (_rows.Count < slices.Count)
        {
            var row = BarRow.Create(_stack);

            _rows.Add(row);
            AddChild(row);
        }

        for (var index = 0; index < _rows.Count; index++)
        {
            _rows[index].Visible = index < slices.Count;
            if (index < slices.Count) _rows[index].Show(slices[index], top);
        }
    }

    private partial class BarRow : Control
    {
        private const int Height = 20;
        private const int LabelWidth = 130;

        private TextureRect _icon = null!;
        private Label _label = null!;
        private Label _value = null!;
        private ColorRect _groove = null!;
        private ColorRect _fill = null!;

        private Func<(string Key, Control Body)?>? _about;

        public static BarRow Create(PopoverStack? stack)
        {
            var row = new BarRow
            {
                CustomMinimumSize = new Vector2(0, Height),
                MouseFilter = MouseFilterEnum.Ignore,
            };

            if (stack is not null) row.Hover(stack, "bar", card: () => row._about?.Invoke());

            row._icon = Ui.Icon("res://assets/icons/ui/close.svg", 14, Skin.Dim);
            row._icon.Position = new Vector2(0, 3);
            row.AddChild(row._icon);

            row._label = Ui.Text(string.Empty, 13, 400, Skin.Text);
            row._label.Position = new Vector2(18, 0);
            row._label.Size = new Vector2(LabelWidth, Height);
            row._label.VerticalAlignment = VerticalAlignment.Center;
            row._label.ClipText = true;
            row.AddChild(row._label);

            // Полоса и число живут в правой части и тянутся с шириной панели, поэтому
            // держатся на якорях, а не на числах в пикселях.
            row._groove = new ColorRect { Color = new Color(Skin.Soft, 0.5f), MouseFilter = MouseFilterEnum.Ignore };
            row._groove.SetAnchorsPreset(LayoutPreset.FullRect);
            row._groove.OffsetLeft = 18 + LabelWidth;
            row._groove.OffsetRight = -66;
            row._groove.OffsetTop = 7;
            row._groove.OffsetBottom = -6;
            row.AddChild(row._groove);

            row._fill = new ColorRect
            {
                MouseFilter = MouseFilterEnum.Ignore,
                Material = Skin.Fill(Skin.Link),
            };
            row._fill.SetAnchorsPreset(LayoutPreset.FullRect);
            row._fill.AnchorRight = 0;
            row._groove.AddChild(row._fill);

            row._value = Ui.Number(string.Empty, 13, Skin.Bright);
            row._value.SetAnchorsPreset(LayoutPreset.RightWide);
            row._value.OffsetLeft = -64;
            row._value.OffsetRight = 0;
            row._value.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(row._value);

            return row;
        }

        public void Show(Slice slice, double top)
        {
            _about = slice.About;
            _label.Text = slice.Label;
            _value.Text = slice.Text;
            _value.AddThemeColorOverride("font_color", slice.Colour);

            _icon.Visible = slice.Icon is not null;
            if (slice.Icon is not null)
            {
                _icon.Texture = GD.Load<Texture2D>(slice.Icon);
                _icon.Modulate = slice.Colour;
            }

            _fill.Color = slice.Colour;
            ((ShaderMaterial)_fill.Material).SetShaderParameter("tint", slice.Colour);
            _fill.AnchorRight = top > 0 ? (float)Mathf.Clamp(Math.Abs(slice.Value) / top, 0.0, 1.0) : 0f;
        }
    }
}
