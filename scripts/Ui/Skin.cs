using Godot;

/// <summary>Цвета и рамки интерфейса. Одно место, чтобы панель и подсказки не разъезжались.</summary>
public static class Skin
{
    public static readonly Color Ink = new("0d171c");
    public static readonly Color Panel = new("101f26");
    public static readonly Color Line = new("2b4652");
    public static readonly Color Text = new("c3d2d8");
    public static readonly Color Dim = new("7d949e");
    public static readonly Color Bright = new("eef4f6");
    public static readonly Color Link = new("7fc4d8");
    public static readonly Color Good = new("6fbf73");
    public static readonly Color Bad = new("d1635c");

    public static StyleBoxFlat PopoverBox()
    {
        var box = new StyleBoxFlat
        {
            BgColor = new Color(Panel, 0.98f),
            BorderColor = Line,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
            ContentMarginTop = 12,
            ContentMarginBottom = 12,
            ShadowColor = new Color(0f, 0f, 0f, 0.45f),
            ShadowSize = 10,
        };

        box.SetBorderWidthAll(1);

        return box;
    }
}
