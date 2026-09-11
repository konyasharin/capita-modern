using Godot;

/// <summary>Цвета, шрифты и рамки интерфейса. Одно место, чтобы панели, таблицы и
/// подсказки не разъезжались.</summary>
public static class Skin
{
    public static readonly Color Ink = new("06090b");
    public static readonly Color Panel = new("0e1a20");
    public static readonly Color Panel2 = new("13232b");
    public static readonly Color Line = new("2b4652");
    public static readonly Color Soft = new("1b323b");
    public static readonly Color Text = new("b9c9d1");
    public static readonly Color Dim = new("748b95");
    public static readonly Color Bright = new("f2f7f9");
    public static readonly Color Link = new("7fc4d8");
    public static readonly Color Good = new("6fbf73");
    public static readonly Color Bad = new("d9634f");
    public static readonly Color Warn = new("e0913c");

    /// <summary>Цвет значка у каждого показателя свой: так их различают краем глаза, не
    /// вчитываясь в число.</summary>
    public static readonly Color People = new("5aa7e8");
    public static readonly Color Output = new("6fcf5a");
    public static readonly Color Plants = new("e0913c");
    public static readonly Color Prices = new("d9634f");
    public static readonly Color Money = new("e8c14a");
    public static readonly Color Labour = new("9d8ee0");
    public static readonly Color Owed = new("c96a9b");
    public static readonly Color Rate = new("5ad0c4");

    /// <summary>Ширина боковой панели и полосы вкладок.</summary>
    public const int PanelWidth = 440;
    public const int RailWidth = 44;

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
        var box = Box(new Color(Panel, 0.98f), Line, 5);

        box.ContentMarginLeft = 14;
        box.ContentMarginRight = 14;
        box.ContentMarginTop = 11;
        box.ContentMarginBottom = 12;
        box.ShadowColor = new Color(0f, 0f, 0f, 0.5f);
        box.ShadowSize = 12;

        return box;
    }

    /// <summary>Плашка кнопки скорости. Нажатая светлее и с рамкой по цвету связи.</summary>
    public static StyleBoxFlat SpeedBox(bool active)
    {
        var box = Box(active ? new Color(Line, 0.85f) : new Color(Ink, 0.82f),
            active ? Link : new Color(Line, 0.6f), 3);

        box.ContentMarginLeft = 9;
        box.ContentMarginRight = 9;
        box.ContentMarginTop = 2;
        box.ContentMarginBottom = 3;

        return box;
    }

    /// <summary>Тело боковой панели. Рамка только справа: слева к ней вплотную стоят
    /// вкладки, и вторая линия между ними лишняя.</summary>
    public static StyleBoxFlat PanelBox()
    {
        var box = Box(new Color(Panel, 0.97f), Line, 0);

        box.SetBorderWidthAll(0);
        box.BorderWidthRight = 1;
        box.ShadowColor = new Color(0f, 0f, 0f, 0.45f);
        box.ShadowSize = 14;
        box.ShadowOffset = new Vector2(4, 0);

        return box;
    }

    /// <summary>Шапка панели: чуть темнее тела, снизу отчёркнута.</summary>
    public static StyleBoxFlat HeaderBox()
    {
        var box = Box(new Color(Ink, 0.85f), Line, 0);

        box.SetBorderWidthAll(0);
        box.BorderWidthBottom = 1;
        box.ContentMarginLeft = 12;
        box.ContentMarginRight = 8;
        box.ContentMarginTop = 6;
        box.ContentMarginBottom = 6;

        return box;
    }

    /// <summary>Полоса вкладок слева.</summary>
    public static StyleBoxFlat RailBox()
    {
        var box = Box(new Color(Ink, 0.94f), Line, 0);

        box.SetBorderWidthAll(0);
        box.BorderWidthRight = 1;

        return box;
    }

    /// <summary>Вкладка. У открытой светлая засечка слева — так видно, где ты, даже когда
    /// значки одинаково блёклые.</summary>
    public static StyleBoxFlat TabBox(bool active)
    {
        var box = Box(active ? Panel2 : new Color(0f, 0f, 0f, 0f), Link, 0);

        box.SetBorderWidthAll(0);
        if (active) box.BorderWidthLeft = 2;

        return box;
    }

    /// <summary>Строка таблицы. Чётные подкрашены — глаз не съезжает на соседнюю.</summary>
    public static StyleBoxFlat RowBox(bool odd)
    {
        var box = Box(odd ? new Color(Panel2, 0.6f) : new Color(0f, 0f, 0f, 0f), Line, 0);

        box.SetBorderWidthAll(0);
        box.ContentMarginLeft = 6;
        box.ContentMarginRight = 6;
        box.ContentMarginTop = 1;
        box.ContentMarginBottom = 1;

        return box;
    }

    /// <summary>Метка-плашка: блок страны, вид ставки, состояние товара.</summary>
    public static StyleBoxFlat ChipBox(Color colour)
    {
        var box = Box(new Color(colour, 0.18f), new Color(colour, 0.55f), 3);

        box.ContentMarginLeft = 6;
        box.ContentMarginRight = 6;
        box.ContentMarginTop = 0;
        box.ContentMarginBottom = 1;

        return box;
    }

    /// <summary>Кнопка действия. Опасные — красные: печать денег и дефолт назад не
    /// отыграть.</summary>
    public static StyleBoxFlat ButtonBox(Color colour, bool hover)
    {
        var box = Box(new Color(colour, hover ? 0.34f : 0.15f), new Color(colour, 0.7f), 3);

        box.ContentMarginLeft = 10;
        box.ContentMarginRight = 10;
        box.ContentMarginTop = 4;
        box.ContentMarginBottom = 5;

        return box;
    }

    /// <summary>Полоса прокрутки под общий вид: у стандартной светло-серый ползунок,
    /// который на тёмной панели кричит громче содержимого.</summary>
    public static void Scrollbar(VScrollBar bar)
    {
        bar.CustomMinimumSize = new Vector2(6, 0);
        bar.AddThemeStyleboxOverride("scroll", BarBox(new Color(Soft, 0.35f)));
        bar.AddThemeStyleboxOverride("grabber", BarBox(new Color(Line, 0.9f)));
        bar.AddThemeStyleboxOverride("grabber_highlight", BarBox(Link));
        bar.AddThemeStyleboxOverride("grabber_pressed", BarBox(Link));
    }

    /// <summary>Полоска-указатель: жёлоб и заполнение.</summary>
    public static StyleBoxFlat BarBox(Color colour) => Box(colour, colour, 2);

    private static StyleBoxFlat Box(Color fill, Color border, int radius)
    {
        var box = new StyleBoxFlat
        {
            BgColor = fill,
            BorderColor = border,
            CornerRadiusTopLeft = radius,
            CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius,
            CornerRadiusBottomRight = radius,
        };

        box.SetBorderWidthAll(1);

        return box;
    }
}
