using Godot;

/// <summary>Стрелка «из этого делается это». Бегущие уголки показывают, что завод
/// работает: неподвижная стрелка на такой схеме читается как просто разделитель.</summary>
public partial class Flowline : Control
{
    private const int Width = 66;
    private const int Chevrons = 3;

    /// <summary>Сколько уголок проходит за секунду, в долях длины стрелки.</summary>
    private const double Speed = 0.5;

    private double _phase;
    private Color _colour = Skin.Link;

    public static Flowline Create()
    {
        return new Flowline
        {
            CustomMinimumSize = new Vector2(Width, 46),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        };
    }

    /// <summary>Цвет берётся у товара, который завод делает: стрелка так связывает
    /// левую и правую сторону.</summary>
    public void Tint(Color colour)
    {
        _colour = colour;
    }

    public override void _Process(double delta)
    {
        _phase = (_phase + delta * Speed) % 1.0;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var middle = Size.Y / 2;
        var from = new Vector2(4, middle);
        var to = new Vector2(Size.X - 4, middle);

        DrawLine(from, to, new Color(_colour, 0.22f), 2, true);

        // Наконечник на месте, уголки бегут по нему.
        DrawPolyline(
            [new Vector2(to.X - 9, middle - 6), to, new Vector2(to.X - 9, middle + 6)],
            new Color(_colour, 0.85f), 2, true);

        for (var index = 0; index < Chevrons; index++)
        {
            var along = (_phase + (double)index / Chevrons) % 1.0;
            var x = (float)Mathf.Lerp(from.X, to.X - 12, along);

            // Гаснут у концов, чтобы не выскакивать из ниоткуда и не втыкаться в наконечник.
            var fade = (float)Math.Sin(along * Math.PI);

            DrawPolyline(
                [new Vector2(x - 4, middle - 5), new Vector2(x + 3, middle), new Vector2(x - 4, middle + 5)],
                new Color(_colour, fade * 0.9f), 2, true);
        }
    }
}
