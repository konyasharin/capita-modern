using Godot;

/// <summary>
/// Временная демонстрация войны: включается ключом --war.
/// Показывает согласованную механику: наступление идёт по графу соседства областей,
/// а микроячейки рисуют, как область захватывают. См. docs/02-war.md.
/// </summary>
public partial class FrontDemo : Node
{
    /// <summary>Шаг фиксирован, а не берётся из delta: тогда кадр N — всегда одно и
    /// то же состояние, и серия снимков воспроизводима.</summary>
    private const float Step = 1f / 60f;

    /// <summary>Сколько прочности снимает штурм за секунду.</summary>
    private const float Push = 0.55f;

    /// <summary>Насколько поле подтягивается к среднему по соседям за тик.</summary>
    private const float Blur = 0.42f;

    /// <summary>Период смены наступающей стороны, в секундах.</summary>
    private const float Swing = 34f;

    /// <summary>Сколько областей штурмуют одновременно — это и есть «концентрация на
    /// направлении»: дивизий конечное число, широким фронтом давить нечем.</summary>
    private const int MaxAssaults = 4;

    /// <summary>Как быстро закрепляется тыл — ячейки без соседей противника.</summary>
    private const float Grip = 1.5f;

    /// <summary>Как часто пересчитывать владение областями и выбор целей.</summary>
    private const float Retarget = 0.5f;

    private WorldMapView _map = null!;
    private byte _rus;
    private byte _ukr;
    private byte _attacker;
    private byte _defender;
    private float _time;
    private float _clock;

    private int[] _cells = [];
    private float[] _field = [];
    private float[] _next = [];
    private bool[] _zone = [];
    private ushort[] _regionOf = [];

    private readonly Dictionary<int, List<int>> _regionCells = [];
    private readonly Dictionary<int, List<int>> _neighbours = [];
    private readonly Dictionary<int, byte> _regionOwner = [];
    private readonly Dictionary<int, Vector2> _regionAt = [];
    private readonly HashSet<int> _targets = [];
    private readonly List<int> _activeCells = [];
    private bool[] _active = [];

    public override void _Ready()
    {
        if (!OS.GetCmdlineUserArgs().Contains("--war"))
        {
            QueueFree();
            return;
        }

        _map = GetParent().GetNode<WorldMapView>("WorldMapView");
        _rus = (byte)(_map.Countries.ByIso("RUS")?.Id ?? 0);
        _ukr = (byte)(_map.Countries.ByIso("UKR")?.Id ?? 0);

        if (_rus == 0 || _ukr == 0)
        {
            GD.PushError("не нашёл страны для демонстрации");
            QueueFree();
            return;
        }

        _regionOf = _map.Regions.Cell;
        BuildZone();
        BuildGraph();
        RefreshOwners();

        GD.Print($"война: ячеек {_cells.Length}, областей {_regionCells.Count}");
    }

    /// <summary>Ячейки двух воюющих стран и знаковое поле по ним: плюс — Россия,
    /// минус — Украина, модуль — прочность владения.</summary>
    private void BuildZone()
    {
        var owner = _map.Map.Owner;
        var list = new List<int>();

        _field = new float[owner.Length];
        _next = new float[owner.Length];
        _zone = new bool[owner.Length];
        _active = new bool[owner.Length];

        for (var at = 0; at < owner.Length; at++)
        {
            if (owner[at] != _rus && owner[at] != _ukr)
            {
                continue;
            }

            list.Add(at);
            _zone[at] = true;
            _field[at] = owner[at] == _rus ? 1f : -1f;

            var region = _regionOf[at];

            if (region == RegionMap.None)
            {
                continue;
            }

            if (!_regionCells.TryGetValue(region, out var cells))
            {
                _regionCells[region] = cells = [];
            }

            cells.Add(at);
        }

        _cells = [.. list];

        foreach (var r in _map.Regions.Regions)
        {
            if (_regionCells.ContainsKey(r.Id))
            {
                _regionAt[r.Id] = new Vector2(r.CenterX, r.CenterY);
            }
        }
    }

    /// <summary>
    /// Граф соседства областей по общим сторонам ячеек. Через воду связи не возникает —
    /// ровно то, чего не хватало прежней механике: фронт больше не может перетечь
    /// через пролив сам по себе.
    /// </summary>
    private void BuildGraph()
    {
        var width = _map.Map.Width;

        foreach (var at in _cells)
        {
            var here = _regionOf[at];

            if (here == RegionMap.None)
            {
                continue;
            }

            Link(here, at + 1, at % width == width - 1);
            Link(here, at + width, false);
        }

        void Link(int here, int to, bool wraps)
        {
            if (wraps || to < 0 || to >= _zone.Length || !_zone[to])
            {
                return;
            }

            var there = _regionOf[to];

            if (there == RegionMap.None || there == here)
            {
                return;
            }

            Add(here, there);
            Add(there, here);
        }

        void Add(int from, int to)
        {
            if (!_neighbours.TryGetValue(from, out var list))
            {
                _neighbours[from] = list = [];
            }

            if (!list.Contains(to))
            {
                list.Add(to);
            }
        }
    }

    public override void _Process(double delta)
    {
        _time += Step;
        _clock += Step;

        // Полпериода наступает одна сторона, полпериода отыгрывает другая.
        var russianTurn = Mathf.Sin(Mathf.Tau * _time / Swing) >= 0f;

        _attacker = russianTurn ? _rus : _ukr;
        _defender = russianTurn ? _ukr : _rus;

        if (_clock >= Retarget)
        {
            _clock = 0f;
            RefreshOwners();
            ChooseTargets();
        }

        Assault();
        Diffuse();
        Consolidate();
        Apply();

        _map.RefreshField();

        if (Mathf.RoundToInt(_time / Step) % 60 == 0)
        {
            var russian = 0;

            foreach (var at in _cells)
            {
                if (_field[at] >= 0f) russian++;
            }

            var side = _attacker == _rus ? "наступает РФ" : "наступает УА";
            GD.Print($"t={_time:0} {side} у РФ {russian} ячеек, целей {_targets.Count}");
        }
    }

    /// <summary>
    /// Область принадлежит тому, кто держит больше половины её ячеек. Это и есть
    /// правило захвата: микроячейки показывают процесс, владение считается областями.
    /// </summary>
    private void RefreshOwners()
    {
        foreach (var (region, cells) in _regionCells)
        {
            var russian = 0;

            foreach (var at in cells)
            {
                if (_field[at] >= 0f)
                {
                    russian++;
                }
            }

            _regionOwner[region] = russian * 2 >= cells.Count ? _rus : _ukr;
        }
    }

    /// <summary>
    /// Штурмуют не всё подряд: берутся области противника, граничащие со своими, и из
    /// них — ближайшие к направлению главного удара. Плюс области, где остались чужие
    /// ячейки: их дочищают, иначе фронт оставляет за собой дыры.
    /// </summary>
    private void ChooseTargets()
    {
        _targets.Clear();

        var focus = _attacker == _rus ? new Vector2(38.0f, 48.3f) : new Vector2(35.0f, 47.5f);
        var edge = new List<(int Region, float Distance)>();

        foreach (var (region, owner) in _regionOwner)
        {
            if (owner != _defender)
            {
                foreach (var at in _regionCells[region])
                {
                    if (Holds(at, _defender))
                    {
                        _targets.Add(region);
                        break;
                    }
                }

                continue;
            }

            if (!_neighbours.TryGetValue(region, out var around))
            {
                continue;
            }

            foreach (var next in around)
            {
                if (_regionOwner.GetValueOrDefault(next) != _attacker)
                {
                    continue;
                }

                edge.Add((region, _regionAt[region].DistanceTo(focus)));
                break;
            }
        }

        edge.Sort((a, b) => a.Distance.CompareTo(b.Distance));

        for (var i = 0; i < edge.Count && i < MaxAssaults; i++)
        {
            _targets.Add(edge[i].Region);
        }

        MarkActive();
    }

    /// <summary>
    /// Ячейки, где поле вообще шевелится: штурмуемые области и кайма вокруг них.
    /// Размывать всю полосу соприкосновения нельзя — тогда спокойные участки границы
    /// расплываются в дугу, и настоящая извилистая линия просто стирается.
    /// </summary>
    private void MarkActive()
    {
        var width = _map.Map.Width;

        Array.Clear(_active);
        _activeCells.Clear();

        foreach (var region in _targets)
        {
            foreach (var at in _regionCells[region])
            {
                Mark(at);

                foreach (var n in Around(at, width))
                {
                    if (n >= 0 && n < _zone.Length && _zone[n]) Mark(n);
                }
            }
        }

        void Mark(int at)
        {
            if (_active[at]) return;

            _active[at] = true;
            _activeCells.Add(at);
        }
    }

    /// <summary>Давление приходится только на штурмуемые области и только на полосу
    /// соприкосновения внутри них.</summary>
    private void Assault()
    {
        var width = _map.Map.Width;
        var force = (_attacker == _rus ? 1f : -1f) * Push * Step;

        foreach (var region in _targets)
        {
            foreach (var at in _regionCells[region])
            {
                if (!Holds(at, _defender))
                {
                    continue;
                }

                var touching = false;

                foreach (var n in Around(at, width))
                {
                    if (n >= 0 && n < _zone.Length && _zone[n] && Holds(n, _attacker))
                    {
                        touching = true;
                        break;
                    }
                }

                if (touching)
                {
                    _field[at] += force;
                }
            }
        }
    }

    /// <summary>
    /// Размытие поля — не косметика, а механика: оно срезает иглы, не даёт прорыву
    /// выродиться в нить шириной в ячейку и удерживает фронт гладким сам по себе.
    /// </summary>
    private void Diffuse()
    {
        var width = _map.Map.Width;

        foreach (var at in _activeCells)
        {
            var sum = 0f;
            var count = 0;

            foreach (var n in Around(at, width))
            {
                if (n < 0 || n >= _zone.Length || !_zone[n])
                {
                    continue;
                }

                sum += _field[n];
                count++;
            }

            _next[at] = count == 0
                ? _field[at]
                : Mathf.Clamp(Mathf.Lerp(_field[at], sum / count, Blur), -1f, 1f);
        }

        foreach (var at in _activeCells)
        {
            _field[at] = _next[at];
        }
    }

    /// <summary>
    /// Тыл закрепляется: ячейка, у которой нет соседей противника, уходит к полной
    /// прочности. Без этого за фронтом остаётся полоса ячеек, болтающихся около нуля,
    /// — они то и дело меняют знак поодиночке, и карта покрывается штрихами от
    /// границ вокруг каждой такой ячейки.
    /// </summary>
    private void Consolidate()
    {
        var width = _map.Map.Width;
        var step = Grip * Step;

        foreach (var at in _cells)
        {
            var mine = _field[at] >= 0f ? _rus : _ukr;
            var contact = false;

            foreach (var n in Around(at, width))
            {
                if (n < 0 || n >= _zone.Length || !_zone[n]) continue;

                if (!Holds(n, mine))
                {
                    contact = true;
                    break;
                }
            }

            if (contact) continue;

            _field[at] = Mathf.Clamp(_field[at] + (mine == _rus ? step : -step), -1f, 1f);
        }
    }

    /// <summary>Знак поля решает, чья ячейка, модуль — насколько прочно она держится.</summary>
    private void Apply()
    {
        var owner = _map.Map.Owner;
        var control = _map.Map.Control;

        foreach (var at in _cells)
        {
            owner[at] = _field[at] >= 0f ? _rus : _ukr;

            // Прочность растёт от фронта резко, а не линейно: иначе полоса, где обе
            // стороны голосуют почти поровну, растягивается на десяток ячеек, и
            // граница ловится в нескольких местах сразу — вместо линии выходит пунктир.
            control[at] = Mathf.Max(0.02f, Mathf.Pow(Mathf.Abs(_field[at]), 0.3f));
        }
    }

    private bool Holds(int at, byte country) => (_field[at] >= 0f ? _rus : _ukr) == country;

    private static int[] Around(int at, int width) => [at - 1, at + 1, at - width, at + width];
}
