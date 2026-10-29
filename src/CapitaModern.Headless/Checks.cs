using CapitaModern.Core.World;

/// <summary>Проверка всех чисел страны неделя к неделе: где скачет и где вышло за разумные рамки.</summary>
sealed class Checks(GameWorld world)
{
    private sealed record Metric(string Name, string Unit, Func<GameWorld, Country, double> Read, double MaxStep, double Min, double Max);

    // Шаг — сколько число может измениться за неделю без события; рамки — где оно должно жить.
    private static readonly Metric[] Metrics =
    [
        new("ВВП", "%", (_, c) => c.GdpReal, 0.02, double.MinValue, double.MaxValue),
        new("нужды", "п.", (_, c) => c.Satisfaction * 100, 2, 85, 100.01),
        new("инфляция", "п.", (_, c) => c.Inflation * 100, 0.5, 0, 6),
        new("уровень цен", "%", (_, c) => c.PriceLevel, 0.005, 0, double.MaxValue),
        new("казна/ВВП", "п.", (_, c) => c.GdpReal > 0 ? c.Treasury / (c.GdpReal * c.PriceLevel) * 100 : 0, 3, 0, 150),
        new("цена товара, у предела", "%", (_, c) => 0, 0.029, double.MinValue, double.MaxValue),
        new("касса компаний/ВВП", "п.", (w, c) => c.GdpReal > 0
            ? w.Companies.Where(x => x.Alive && x.Country == c.Id).Sum(x => x.Cash) / (c.GdpReal * c.PriceLevel) * 100 : 0, 5, -5, 100),
        new("загрузка заводов", "п.", (w, c) => Load(w, c) * 100, 5, 50, 100.01),
    ];

    private readonly Dictionary<(int, int), double> _last = [];
    private readonly Dictionary<int, double[]> _lastPrices = [];
    private readonly Dictionary<(int, int), (double Value, string Iso)> _worstStep = [];
    private readonly Dictionary<(int, int), int> _outside = [];
    private readonly Dictionary<(int, int), (double Value, string Iso)> _extreme = [];
    private int _weeks;

    public void Week()
    {
        _weeks++;
        foreach (var c in world.Countries.Where(c => c.GdpReal > 1e5))
        {
            for (var m = 0; m < Metrics.Length; m++)
            {
                var metric = Metrics[m];
                var value = metric.Name.StartsWith("цена товара") ? 0 : metric.Read(world, c);

                if (metric.Name.StartsWith("цена товара"))
                {
                    if (_lastPrices.TryGetValue(c.Id, out var before))
                    {
                        var step = Enumerable.Range(0, before.Length).Where(g => c.DailyUse[g] > 0 && before[g] > 0)
                            .Select(g => Math.Abs(c.PriceMult[g] / before[g] - 1)).DefaultIfEmpty(0).Max();
                        Note(m, c, step);
                    }

                    _lastPrices[c.Id] = (double[])c.PriceMult.Clone();
                    continue;
                }

                if (_weeks > 1 && _last.TryGetValue((c.Id, m), out var last))
                {
                    var step = metric.Unit == "%" ? (last > 0 ? Math.Abs(value / last - 1) : 0) : Math.Abs(value - last);
                    Note(m, c, step);
                }

                _last[(c.Id, m)] = value;
                if (_weeks > 1 && (value < metric.Min || value > metric.Max))
                {
                    _outside[(m, c.Id)] = _outside.GetValueOrDefault((m, c.Id)) + 1;
                    var key = (m, value < metric.Min ? -1 : 1);
                    var far = value < metric.Min ? metric.Min - value : value - metric.Max;
                    if (!_extreme.TryGetValue(key, out var e) || far > e.Value) _extreme[key] = (far, c.Iso);
                }
            }
        }
    }

    private void Note(int m, Country c, double step)
    {
        if (step > Metrics[m].MaxStep) _outside[(m, -c.Id - 1)] = _outside.GetValueOrDefault((m, -c.Id - 1)) + 1;
        if (!_worstStep.TryGetValue((m, 0), out var w) || step > w.Value) _worstStep[(m, 0)] = (step, c.Iso);
    }

    public void Print()
    {
        Console.WriteLine("все числа по странам: худший недельный шаг; стран со скачками сверх нормы; стран вне рамок");
        for (var m = 0; m < Metrics.Length; m++)
        {
            var metric = Metrics[m];
            var worst = _worstStep.GetValueOrDefault((m, 0));
            var jumpy = _outside.Where(p => p.Key.Item1 == m && p.Key.Item2 < 0).ToList();
            var outside = _outside.Where(p => p.Key.Item1 == m && p.Key.Item2 >= 0).ToList();
            var step = metric.Unit == "%" ? worst.Value * 100 : worst.Value;

            var line = $"  {metric.Name,-20} шаг до {step,6:F2} {metric.Unit} ({worst.Iso}); скачут {jumpy.Count,3} стран";
            if (metric.Min > double.MinValue || metric.Max < double.MaxValue)
            {
                line += $"; вне [{metric.Min:G4}..{metric.Max:G4}] {outside.Count,3} стран";
                foreach (var side in new[] { -1, 1 })
                {
                    if (_extreme.TryGetValue((m, side), out var e)) line += $", {(side < 0 ? "ниже" : "выше")} всех {e.Iso} на {e.Value:F1}";
                }
            }

            Console.WriteLine(line);
            if (jumpy.Count > 0)
            {
                var top = jumpy.OrderByDescending(p => p.Value).Take(6).Select(p => $"{world.Country(-p.Key.Item2 - 1).Iso}×{p.Value}");
                Console.WriteLine($"      чаще всех скачут: {string.Join(", ", top)}");
            }
        }
    }

    private static double Load(GameWorld world, Country c)
    {
        var plants = world.Regions.Where(r => r.Owner == c.Id).SelectMany(r => r.Plants)
            .Where(p => !world.Catalog.Buildings[p.Building].Outputs.Any(o => world.Catalog.Goods[o.Good].Perishable)).ToList();
        var count = plants.Sum(p => (double)p.Count);
        return count > 0 ? plants.Sum(p => p.Count * p.Load) / count : 1;
    }
}
