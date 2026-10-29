using CapitaModern.Core.World;

namespace CapitaModern.Core.Sim;

/// <summary>Суточный шаг войны: движение, бои, захват регионов, боеготовность.</summary>
public static class WarStep
{
    /// <summary>Сколько километров в сутки проходит дивизия.</summary>
    private const double KmPerDay = 40;

    /// <summary>Бонус обороны: держать регион проще, чем брать.</summary>
    private const double DefenceBonus = 1.3;

    /// <summary>Сколько боеготовности за сутки теряет сторона в бою при равных силах.</summary>
    private const double BattleLoss = 0.10;

    /// <summary>Ниже этого дивизия выходит из боя и отступает.</summary>
    private const double BreakPoint = 0.2;

    private const double Recovery = 0.03;

    /// <summary>Какая доля техники выбывает за год без войны.</summary>
    private const double PeaceWear = 0.10;
    private const double OutOfSupply = 0.02;

    public static void Day(GameWorld world, int? player)
    {
        if (world.Day % 3 == 0) Bots(world, player);

        foreach (var division in world.Divisions.Where(d => d.Alive && !d.InBattle))
        {
            if (division.Target < 0) NextStep(world, division);
            if (division.Target < 0) continue;

            division.Progress += 1;
            if (division.Progress >= division.TravelDays) Arrive(world, division);
        }

        Battles(world);
        Readiness(world);
        world.Divisions.RemoveAll(d => !d.Alive);
    }

    /// <summary>Приказ идти в регион: дивизия пойдёт туда шаг за шагом.</summary>
    public static void Order(GameWorld world, Division division, int goal)
    {
        division.Goal = goal == division.Region ? -1 : goal;
        if (division.InBattle) return;

        division.Target = -1;
        division.Progress = 0;
        NextStep(world, division);
    }

    private static void NextStep(GameWorld world, Division division)
    {
        if (division.Goal < 0) return;

        var step = FirstStep(world, division.Country, division.Region, division.Goal);
        if (step < 0)
        {
            division.Goal = -1;
            return;
        }

        division.Target = step;
        division.Progress = 0;
        division.TravelDays = Travel(world, division.Region, step);
    }

    /// <summary>Первый шаг кратчайшего пути по своим регионам и регионам врага.</summary>
    private static int FirstStep(GameWorld world, int country, int from, int goal)
    {
        var previous = new Dictionary<int, int> { [from] = from };
        var queue = new Queue<int>([from]);

        while (queue.Count > 0)
        {
            var at = queue.Dequeue();
            if (at == goal) break;

            foreach (var next in world.Region(at).Neighbours)
            {
                var region = world.RegionById[next];
                if (region is null || previous.ContainsKey(next)) continue;
                if (region.Controller != country && !world.AtWar(country, region.Controller)) continue;

                previous[next] = at;
                queue.Enqueue(next);
            }
        }

        if (!previous.ContainsKey(goal)) return -1;

        var step = goal;
        while (previous[step] != from) step = previous[step];
        return step;
    }

    public static double Travel(GameWorld world, int from, int to)
    {
        var a = world.Region(from);
        var b = world.Region(to);
        var dx = Math.Abs(a.X - b.X);
        dx = Math.Min(dx, world.Grid.Width - dx);
        var cells = Math.Sqrt(dx * dx + (a.Y - b.Y) * (a.Y - b.Y));
        var km = cells * 360.0 / world.Grid.Width * 111;

        return Math.Max(1, km / KmPerDay);
    }

    private static void Arrive(GameWorld world, Division division)
    {
        var target = world.Region(division.Target);
        var template = world.Templates[division.Template];

        if (target.Controller != division.Country && !world.AtWar(division.Country, target.Controller))
        {
            Stop(division);
            return;
        }

        if (Defenders(world, division.Country, target.Id).Any())
        {
            division.InBattle = true;
            return;
        }

        // Регион берут только наземные дивизии; ПВО и РЭБ входят лишь в свои.
        if (target.Controller != division.Country)
        {
            if (!template.TakesGround)
            {
                Stop(division);
                return;
            }

            target.Controller = division.Country;
        }

        division.Region = target.Id;
        division.Target = -1;
        division.Progress = 0;
        if (division.Goal == target.Id) division.Goal = -1;
    }

    private static void Stop(Division division)
    {
        division.Target = -1;
        division.Goal = -1;
        division.Progress = 0;
        division.InBattle = false;
    }

    private static IEnumerable<Division> Defenders(GameWorld world, int attacker, int region) =>
        world.Divisions.Where(d => d.Alive && d.Region == region && world.AtWar(attacker, d.Country));

    /// <summary>Каждые сутки боя обе стороны теряют боеготовность по соотношению сил.</summary>
    private static void Battles(GameWorld world)
    {
        var fronts = world.Divisions.Where(d => d.Alive && d.InBattle).GroupBy(d => d.Target).ToList();

        foreach (var front in fronts)
        {
            var attackers = front.ToList();
            var defenders = Defenders(world, attackers[0].Country, front.Key).ToList();

            if (defenders.Count == 0)
            {
                foreach (var division in attackers)
                {
                    division.InBattle = false;
                    division.Progress = division.TravelDays;
                    Arrive(world, division);
                }

                continue;
            }

            var attack = attackers.Sum(d => d.Strength(world.Templates[d.Template]) * Ammo(world, d));
            var defence = defenders.Sum(d => d.Strength(world.Templates[d.Template]) * Ammo(world, d)) * DefenceBonus;
            var total = Math.Max(1e-6, attack + defence);

            foreach (var division in defenders)
            {
                division.Organization = Math.Max(0, division.Organization - BattleLoss * attack / total * 2);
                division.Equipment = Math.Max(0, division.Equipment - 0.004 * attack / total);
            }

            foreach (var division in attackers)
            {
                division.Organization = Math.Max(0, division.Organization - BattleLoss * defence / total * 2);
                division.Equipment = Math.Max(0, division.Equipment - 0.004 * defence / total);
            }

            foreach (var division in defenders.Where(d => d.Organization < BreakPoint)) Retreat(world, division);
            foreach (var division in attackers.Where(d => d.Organization < BreakPoint)) Stop(division);
        }
    }

    /// <summary>Расход боеприпасов за сутки боя со склада страны; не хватило — бьют слабее.</summary>
    private static double Ammo(GameWorld world, Division division)
    {
        var country = world.Country(division.Country);
        var fill = 1.0;

        foreach (var (good, amount) in world.Templates[division.Template].Upkeep)
        {
            country.DemandWeek[good] += amount;
            fill = Math.Min(fill, amount <= 0 ? 1 : country.Stock[good] / amount);
            country.Stock[good] = Math.Max(0, country.Stock[good] - amount);
        }

        return 0.5 + 0.5 * Math.Clamp(fill, 0, 1);
    }

    /// <summary>Проигравший уходит в соседний свой регион; некуда — дивизия разбита.</summary>
    private static void Retreat(GameWorld world, Division division)
    {
        var back = world.Region(division.Region).Neighbours
            .Select(n => world.RegionById[n])
            .FirstOrDefault(r => r is not null && r.Controller == division.Country);

        if (back is null)
        {
            division.Alive = false;
            return;
        }

        division.Region = back.Id;
        Stop(division);
    }

    private static void Readiness(GameWorld world)
    {
        foreach (var division in world.Divisions.Where(d => d.Alive && !d.InBattle))
        {
            var region = world.Region(division.Region);
            division.Organization = region.Supplied && region.Controller == division.Country
                ? Math.Min(1, division.Organization + Recovery)
                : Math.Max(0, division.Organization - OutOfSupply);
        }
    }

    /// <summary>Пополнение раз в неделю: недостающую технику берут со склада страны.</summary>
    public static void Reinforce(GameWorld world)
    {
        // Техника изнашивается и в мирное время — оттого оборонные заводы работают и без войны.
        foreach (var division in world.Divisions.Where(d => d.Alive)) division.Equipment -= PeaceWear * 7 / 365;

        foreach (var division in world.Divisions.Where(d => d.Alive && d.Equipment < 1))
        {
            var country = world.Country(division.Country);
            var template = world.Templates[division.Template];
            var missing = Math.Min(1 - division.Equipment, 0.25);
            var fill = 1.0;

            foreach (var (good, amount) in template.Equipment)
            {
                var need = amount * missing;
                country.DemandWeek[good] += need;
                fill = Math.Min(fill, need <= 0 ? 1 : country.Stock[good] / need);
            }

            fill = Math.Clamp(fill, 0, 1);
            foreach (var (good, amount) in template.Equipment) country.Stock[good] -= amount * missing * fill;

            division.Equipment += missing * fill;
        }
    }

    /// <summary>Боты на войне: свободные дивизии атакуют слабейший соседний вражеский регион или идут к фронту.</summary>
    private static void Bots(GameWorld world, int? player)
    {
        foreach (var division in world.Divisions.Where(d => d.Alive && !d.InBattle && d.Target < 0 && d.Goal < 0))
        {
            if (division.Country == player || !world.Enemies(division.Country).Any()) continue;

            var here = world.Region(division.Region);
            var mine = division.Strength(world.Templates[division.Template]);
            if (division.Organization < 0.6) continue;

            var weakest = here.Neighbours
                .Select(n => world.RegionById[n])
                .Where(r => r is not null && world.AtWar(division.Country, r.Controller))
                .Select(r => (Region: r!, Defence: Defenders(world, division.Country, r!.Id)
                    .Sum(d => d.Strength(world.Templates[d.Template])) * DefenceBonus))
                .OrderBy(x => x.Defence)
                .FirstOrDefault();

            if (weakest.Region is not null && weakest.Defence < mine * 1.2)
            {
                Order(world, division, weakest.Region.Id);
                continue;
            }

            var front = NearestFront(world, division);
            if (front >= 0 && front != division.Region) Order(world, division, front);
        }
    }

    /// <summary>Ближайший свой регион, граничащий с врагом.</summary>
    private static int NearestFront(GameWorld world, Division division)
    {
        var seen = new HashSet<int> { division.Region };
        var queue = new Queue<int>([division.Region]);

        while (queue.Count > 0)
        {
            var at = world.Region(queue.Dequeue());
            if (at.Neighbours.Any(n => world.RegionById[n] is { } r && world.AtWar(division.Country, r.Controller))) return at.Id;

            foreach (var next in at.Neighbours)
            {
                if (world.RegionById[next] is { } r && r.Controller == division.Country && seen.Add(next)) queue.Enqueue(next);
            }
        }

        return -1;
    }
}
