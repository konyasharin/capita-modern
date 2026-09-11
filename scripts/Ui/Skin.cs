using Godot;

/// <summary>Цвета, шрифты и рамки интерфейса. Одно место, чтобы панель и подсказки не
/// разъезжались.</summary>
public static class Skin
{
    public static readonly Color Ink = new("06090b");
    public static readonly Color Panel = new("0e1a20");
    public static readonly Color Line = new("2b4652");
    public static readonly Color Text = new("b9c9d1");
    public static readonly Color Dim = new("748b95");
    public static readonly Color Bright = new("f2f7f9");
    public static readonly Color Link = new("7fc4d8");
    public static readonly Color Good = new("6fbf73");
    public static readonly Color Bad = new("d9634f");

    /// <summary>Цвет значка у каждого показателя свой: так их различают краем глаза, не
    /// вчитываясь в число.</summary>
    public static readonly Color People = new("5aa7e8");
    public static readonly Color Output = new("6fcf5a");
    public static readonly Color Plants = new("e0913c");
    public static readonly Color Prices = new("d9634f");
    public static readonly Color Money = new("e8c14a");

    private const string Face = "res://assets/fonts/RobotoCondensed.ttf";

    /// <summary>Узкий шрифт: в панель влезает вдвое больше, и цифры читаются столбиком.
    /// Начертание задаётся весом, файл один — он переменный.</summary>
    public static FontVariation Weight(int weight)
    {
        var font = new FontVariation { BaseFont = GD.Load<FontFile>(Face) };
        font.SetVariationOpentype(new Godot.Collections.Dictionary { { "wght", weight } });

        return font;
    }

    /// <summary>Шрифт для чисел: цифры одной ширины, иначе число шевелится само по себе,
    /// даже когда стоит на месте.</summary>
    public static FontVariation Digits()
    {
        var font = Weight(600);
        font.OpentypeFeatures = new Godot.Collections.Dictionary { { "tnum", 1 } };

        return font;
    }

    public static StyleBoxFlat PopoverBox()
    {
        var box = new StyleBoxFlat
        {
            BgColor = new Color(Panel, 0.98f),
            BorderColor = Line,
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            CornerRadiusBottomLeft = 5,
            CornerRadiusBottomRight = 5,
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
            ContentMarginTop = 11,
            ContentMarginBottom = 12,
            ShadowColor = new Color(0f, 0f, 0f, 0.5f),
            ShadowSize = 12,
        };

        box.SetBorderWidthAll(1);

        return box;
    }

    /// <summary>Плашка кнопки скорости. Нажатая светлее и с рамкой по цвету связи.</summary>
    public static StyleBoxFlat SpeedBox(bool active)
    {
        var box = new StyleBoxFlat
        {
            BgColor = active ? new Color(Line, 0.85f) : new Color(Ink, 0.82f),
            BorderColor = active ? Link : new Color(Line, 0.6f),
            CornerRadiusTopLeft = 3,
            CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3,
            CornerRadiusBottomRight = 3,
            ContentMarginLeft = 9,
            ContentMarginRight = 9,
            ContentMarginTop = 2,
            ContentMarginBottom = 3,
        };

        box.SetBorderWidthAll(1);

        return box;
    }
}
