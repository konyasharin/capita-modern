using Godot;

/// <summary>Держит цепочку подсказок: показатель открыл подсказку, термин в ней — вторую.</summary>
/// <remarks>
/// Закрываются они с хвоста и только тогда, когда мышь ушла и с подсказки, и с того, от
/// чего она открыта. Считается это перебором прямоугольников каждый кадр, а не событиями
/// входа-выхода: между элементами есть щели в пиксель, и на них события врут.
/// </remarks>
public partial class PopoverStack : Control
{
    private readonly List<Popover> _open = [];

    public Glossary Glossary { get; set; } = null!;

    public override void _Ready()
    {
        Glossary ??= Glossary.Load();

        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    /// <summary>Показать подсказку от элемента. Всё, что было открыто глубже, закрывается.</summary>
    /// <param name="extra">Строка, дописываемая к статье. Нужна там, где подробность
    /// зависит от состояния игры: какой именно товар подорожал и на сколько.</param>
    /// <param name="ready">Готовая статья вместо словарной. Нужна там, где текст зависит
    /// от состояния игры целиком: карточка товара, например.</param>
    public void Open(Control source, string key, int depth, string? extra = null, Article? ready = null)
    {
        if (_open.Count > depth && _open[depth].Key == key && _open[depth].Extra == extra)
        {
            return;
        }

        CloseFrom(depth);

        var article = ready ?? Glossary.Any(key);
        if (article is null)
        {
            return;
        }

        var popover = Popover.Create(source, key, article, extra);
        AddChild(popover);
        popover.PlaceNear(source, GetViewportRect());

        // Верхняя подсказка держится курсором над своим элементом, вложенная — курсором
        // над самим термином: иначе она висит, пока мышь где угодно в родительской.
        var parent = depth > 0 ? _open[depth - 1] : null;
        popover.Alive = parent is null ? () => Under(source) : () => parent.HotTerm == key;

        var level = _open.Count;
        popover.Body.MetaHoverStarted += meta =>
        {
            popover.HotTerm = meta.AsString();
            Open(popover.Body, popover.HotTerm, level + 1);
        };

        popover.Body.MetaHoverEnded += meta =>
        {
            if (popover.HotTerm == meta.AsString()) popover.HotTerm = string.Empty;
        };

        _open.Add(popover);
    }

    public override void _Process(double delta)
    {
        if (_open.Count == 0)
        {
            return;
        }

        var last = _open[^1];
        if (Under(last) || last.Alive())
        {
            return;
        }

        CloseFrom(_open.Count - 1);
    }

    private static bool Under(Control control) =>
        IsInstanceValid(control) && control.GetGlobalRect().HasPoint(control.GetGlobalMousePosition());

    private void CloseFrom(int depth)
    {
        for (var i = _open.Count - 1; i >= depth; i--)
        {
            _open[i].QueueFree();
            _open.RemoveAt(i);
        }
    }
}
