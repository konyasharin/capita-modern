using CapitaModern.Core.Economy;

namespace CapitaModern.Core.Politics;

/// <summary>Как страна добирается до мирового рынка и кому по дороге платит.</summary>
/// <remarks>
/// Путь идёт по трём видам звеньев: сухопутная граница с соседом, свой порт в морском
/// бассейне и пролив между бассейнами. Мировой рынок — это открытый океан; кто в него не
/// выходит напрямую, тот идёт через чужое.
///
/// Отсюда сами собой берутся вещи, которых иначе пришлось бы прописывать руками. У Турции
/// есть море, но только Чёрное и Средиземное, и до океана ей нужен чужой Гибралтар. У
/// Казахстана берег на Каспии, который никуда не ведёт, — он торгует как страна без моря.
/// Персидский залив висит на Ормузе, Балтика на датских проливах.
///
/// Перекрыть можно любое звено: враждебный сосед не пропустит по земле, враждебный
/// владелец пролива — по воде. Тогда страна ищет обход, а не найдя — теряет рынок.
/// </remarks>
public sealed class Routes
{
    /// <summary>Чего стоит перейти чужую границу по земле, в сотых. Дорого: грузовик
    /// возит вдесятеро дороже парохода за тонно-километр.</summary>
    public const int LandHop = 80;

    /// <summary>Чего стоит пройти чужой пролив. Дешевле земли, но не даром.</summary>
    public const int StraitHop = 20;

    /// <summary>Дороже этого пути не бывает: считаем, что рынка нет.</summary>
    public const int Unreachable = 1000;

    /// <summary>Открытый океан. Кто в нём, тот на рынке.</summary>
    public const int OpenSea = 1;

    private readonly int _countries;
    private readonly List<byte>[] _neighbours;
    private readonly List<int>[] _ports;
    private readonly List<(int Other, byte Owner)>[] _straits;
    private readonly int[] _cost;
    private readonly int[] _from;
    private readonly List<byte>[] _tolls;

    /// <param name="basinsOf">Каких морских бассейнов касается берег страны.</param>
    /// <param name="straits">Что какой бассейн с каким соединяет и кто этим владеет.</param>
    public Routes(
        IReadOnlyDictionary<byte, byte[]>? neighbours = null,
        IReadOnlyDictionary<byte, int[]>? basinsOf = null,
        IReadOnlyList<(int From, int To, byte Owner)>? straits = null,
        int countries = 256,
        int basins = 512)
    {
        _countries = countries;
        var nodes = countries + basins;

        _neighbours = new List<byte>[countries];
        _ports = new List<int>[countries];
        _straits = new List<(int, byte)>[nodes];
        _cost = new int[nodes];
        _from = new int[nodes];
        _tolls = new List<byte>[countries];

        for (var i = 0; i < countries; i++)
        {
            _neighbours[i] = [];
            _ports[i] = [];
            _tolls[i] = [];
        }

        for (var i = 0; i < nodes; i++) _straits[i] = [];

        var known = neighbours is not null || basinsOf is not null;
        foreach (var (country, list) in neighbours ?? new Dictionary<byte, byte[]>()) _neighbours[country].AddRange(list);
        foreach (var (country, list) in basinsOf ?? new Dictionary<byte, int[]>()) _ports[country].AddRange(list);

        foreach (var (from, to, owner) in straits ?? [])
        {
            _straits[countries + from].Add((countries + to, owner));
            _straits[countries + to].Add((countries + from, owner));
        }

        // Мир без географии не должен оказаться отрезанным: всем свой выход в океан.
        if (!known)
        {
            for (var i = 0; i < countries; i++) _ports[i].Add(OpenSea);
        }

        Recompute(null, null);
    }

    /// <summary>Во что обходится путь до рынка, в сотых. Ноль у тех, кто прямо в океане.</summary>
    public int CostTo(byte country) => _cost[country];

    public bool CanReachMarket(byte country) => _cost[country] < Unreachable;

    /// <summary>Кому страна платит за проход: соседям по земле и хозяевам проливов.</summary>
    public IReadOnlyList<byte> TollTakers(byte country) => _tolls[country];

    /// <summary>Пересчитывает пути. Звать при смене отношений, запретов и границ.</summary>
    /// <param name="attitude">Как хозяин звена относится к идущему; ниже враждебности не пустит.</param>
    /// <param name="blocked">Кто вообще никого через себя не пускает.</param>
    public void Recompute(Func<byte, byte, int>? attitude, Func<byte, bool>? blocked)
    {
        Array.Fill(_cost, Unreachable);
        Array.Fill(_from, -1);

        // Дейкстра от открытого океана наружу: у звеньев разная цена, обхода в ширину мало.
        var queue = new PriorityQueue<int, int>();
        _cost[_countries + OpenSea] = 0;
        queue.Enqueue(_countries + OpenSea, 0);

        while (queue.TryDequeue(out var at, out var spent))
        {
            if (spent > _cost[at]) continue;

            foreach (var (next, cost, owner) in StepsFrom(at))
            {
                if (owner is { } who && (blocked?.Invoke(who) == true)) continue;

                var total = spent + cost;
                if (total >= _cost[next]) continue;

                // Хозяин звена смотрит, кого пускает. Отношение берём к тому, кто идёт.
                if (owner is { } keeper && next < _countries &&
                    attitude?.Invoke(keeper, (byte)next) <= CreditMarket.Hostile) continue;

                _cost[next] = total;
                _from[next] = at;
                queue.Enqueue(next, total);
            }
        }

        for (var country = 0; country < _countries; country++) CollectTolls((byte)country);
    }

    /// <summary>Куда можно шагнуть и почём. Владелец пусто у своего порта.</summary>
    private IEnumerable<(int Next, int Cost, byte? Owner)> StepsFrom(int at)
    {
        if (at >= _countries)
        {
            var basin = at - _countries;
            foreach (var (other, owner) in _straits[at]) yield return (other, StraitHop, owner);

            for (var country = 0; country < _countries; country++)
            {
                if (_ports[country].Contains(basin)) yield return (country, 0, null);
            }

            yield break;
        }

        foreach (var basin in _ports[at]) yield return (_countries + basin, 0, null);
        foreach (var neighbour in _neighbours[at]) yield return (neighbour, LandHop, (byte)at);
    }

    /// <summary>Идёт по пути назад и собирает всех, кому платить.</summary>
    private void CollectTolls(byte country)
    {
        _tolls[country].Clear();
        if (!CanReachMarket(country)) return;

        var at = (int)country;
        for (var step = 0; step < 16 && _from[at] >= 0; step++)
        {
            var previous = _from[at];

            // Сосед, через которого шли по земле, и хозяин пройденного пролива.
            if (previous < _countries && previous != country) _tolls[country].Add((byte)previous);
            else if (previous >= _countries && at >= _countries)
            {
                foreach (var (other, owner) in _straits[at])
                {
                    if (other == previous) _tolls[country].Add(owner);
                }
            }

            at = previous;
        }
    }
}
