using Godot;

/// <summary>Одна ячейка таблицы.</summary>
/// <param name="Order">По чему сортировать. Отдельно от текста: «1.2M» и «900K» строками
/// сравниваются неверно.</param>
public readonly record struct Cell(string Text, double Order = 0, Color? Tint = null, string? Icon = null);

/// <summary>Столбец. Ширина ноль — тянется по остатку, такой в таблице один.</summary>
public sealed record Column(string Title, int Width, bool Right = true);

/// <summary>Таблица с сортировкой по столбцу. Узлы строк переиспользуются: панель
/// обновляется четырежды в секунду, и пересобирать двести подписей каждый раз незачем.</summary>
public partial class Table : VBoxContainer
{
    private const int RowHeight = 19;

    private Column[] _columns = [];
    private readonly List<TableRow> _rows = [];
    private readonly List<Button> _heads = [];

    private int _sort;
    private bool _ascending;
    private Action<int>? _clicked;

    public static Table Create(Column[] columns, Action<int>? clicked = null)
    {
        var table = new Table { _columns = columns, _clicked = clicked, _sort = columns.Length > 1 ? 1 : 0 };
        table.AddThemeConstantOverride("separation", 0);
        table.AddChild(table.Head());

        return table;
    }

    /// <summary>Заново раскладывает строки. Порядок задаёт выбранный столбец, а не
    /// порядок в списке.</summary>
    public void Set(IReadOnlyList<Cell[]> rows)
    {
        var order = Enumerable.Range(0, rows.Count).ToList();
        var by = Mathf.Min(_sort, _columns.Length - 1);

        order.Sort((a, b) => _ascending
            ? rows[a][by].Order.CompareTo(rows[b][by].Order)
            : rows[b][by].Order.CompareTo(rows[a][by].Order));

        while (_rows.Count < rows.Count)
        {
            var row = TableRow.Create(_columns, _rows.Count, _clicked);

            _rows.Add(row);
            AddChild(row);
        }

        for (var index = 0; index < _rows.Count; index++)
        {
            _rows[index].Visible = index < rows.Count;
            if (index < rows.Count) _rows[index].Show(rows[order[index]], order[index], index % 2 == 1);
        }
    }

    private Control Head()
    {
        var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        head.AddThemeConstantOverride("separation", 4);

        for (var index = 0; index < _columns.Length; index++)
        {
            var column = _columns[index];
            var at = index;

            var button = new Button
            {
                Text = column.Title,
                FocusMode = FocusModeEnum.None,
                Alignment = column.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                CustomMinimumSize = new Vector2(column.Width, 0),
                SizeFlagsHorizontal = column.Width == 0 ? SizeFlags.ExpandFill : SizeFlags.Fill,
            };

            button.AddThemeFontOverride("font", Skin.Weight(700));
            button.AddThemeFontSizeOverride("font_size", 11);
            button.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
            button.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
            button.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
            button.Pressed += () => Sort(at);

            _heads.Add(button);
            head.AddChild(button);
        }

        PaintHead();

        return head;
    }

    /// <summary>Тот же столбец второй раз — обратный порядок.</summary>
    private void Sort(int column)
    {
        _ascending = _sort == column && !_ascending;
        _sort = column;

        PaintHead();
    }

    private void PaintHead()
    {
        for (var index = 0; index < _heads.Count; index++)
        {
            var chosen = index == _sort;
            var mark = _ascending ? " ↑" : " ↓";

            _heads[index].Text = _columns[index].Title + (chosen ? mark : string.Empty);
            _heads[index].AddThemeColorOverride("font_color", chosen ? Skin.Link : Skin.Dim);
            _heads[index].AddThemeColorOverride("font_hover_color", Skin.Bright);
        }
    }

    /// <summary>Строка таблицы. Заводится один раз, дальше только меняет подписи.</summary>
    private partial class TableRow : PanelContainer
    {
        private readonly List<Label> _labels = [];
        private readonly List<TextureRect> _icons = [];

        private int _source;
        private Action<int>? _clicked;

        public static TableRow Create(Column[] columns, int index, Action<int>? clicked)
        {
            var row = new TableRow
            {
                _clicked = clicked,
                CustomMinimumSize = new Vector2(0, RowHeight),
                MouseFilter = clicked is null ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop,
            };

            row.AddThemeStyleboxOverride("panel", Skin.RowBox(index % 2 == 1));

            var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            line.AddThemeConstantOverride("separation", 4);
            row.AddChild(line);

            foreach (var column in columns)
            {
                var cell = new HBoxContainer
                {
                    MouseFilter = MouseFilterEnum.Ignore,
                    CustomMinimumSize = new Vector2(column.Width, 0),
                    SizeFlagsHorizontal = column.Width == 0 ? SizeFlags.ExpandFill : SizeFlags.Fill,
                };

                cell.AddThemeConstantOverride("separation", 4);

                var icon = Ui.Icon("res://assets/icons/ui/close.svg", 13, Skin.Dim);
                icon.Visible = false;
                cell.AddChild(icon);
                row._icons.Add(icon);

                var label = Ui.Text(string.Empty, 13);
                label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                label.HorizontalAlignment = column.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                label.VerticalAlignment = VerticalAlignment.Center;
                label.ClipText = true;
                if (column.Right) label.AddThemeFontOverride("font", Skin.Digits());

                cell.AddChild(label);
                row._labels.Add(label);

                line.AddChild(cell);
            }

            return row;
        }

        public void Show(Cell[] cells, int source, bool odd)
        {
            _source = source;
            AddThemeStyleboxOverride("panel", Skin.RowBox(odd));

            for (var index = 0; index < _labels.Count && index < cells.Length; index++)
            {
                _labels[index].Text = cells[index].Text;
                _labels[index].AddThemeColorOverride("font_color", cells[index].Tint ?? Skin.Text);

                var icon = cells[index].Icon;
                _icons[index].Visible = icon is not null;
                if (icon is not null) _icons[index].Texture = GD.Load<Texture2D>(icon);
            }
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                _clicked?.Invoke(_source);
            }
        }
    }
}
