using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;
using CapitaModern.Core.Politics;

namespace CapitaModern.Core.World;

/// <summary>Тик экономики: предприятия съедают сырьё и выдают продукцию.</summary>
/// <remarks>Два прохода: сначала считаем спрос всей страны, потом производим.
/// Дефицит делится на всех потребителей товара сразу.</remarks>
public sealed class Simulation
{
    private readonly GameWorld _world;

    /// <summary>Сколько каждого товара заказали все предприятия страны за этот тик.</summary>
    private readonly Tally<GoodType, GoodAmount> _inputs = new();

    /// <summary>Что произведено за тик. В склады вливается только в конце.</summary>
    private readonly Tally<GoodType, GoodAmount> _outputs = new();

    /// <summary>Склады на начало тика. Во втором проходе живой склад убывает, а доли
    /// должны считаться от одних и тех же чисел.</summary>
    private readonly Tally<GoodType, GoodAmount> _available = new();
    private readonly Tally<GoodType, GoodAmount> _claims = new();

    /// <summary>Работоспособные предприятия по стране и типу. Считаются вместе, где бы
    /// ни стояли: склад у страны общий.</summary>
    private readonly Tally<BuildingType, int> _working = new();

    public Simulation(GameWorld world)
    {
        _world = world;
    }

    public void Tick()
    {
        Prepare();
        CollectInputs();
        CollectAvailable();
        CollectOutputs();
        Store();
    }

    /// <summary>Счётчики живут один тик. Чистим в начале, чтобы прошлые числа можно было
    /// посмотреть.</summary>
    private void Prepare()
    {
        _inputs.Clear();
        _outputs.Clear();
        _available.Clear();
        _claims.Clear();
        _working.Clear();
    }

    /// <summary>Может ли предприятие работать в этой области. Зовётся только из первого
    /// прохода — во второй попадает уже готовый список.</summary>
    private bool CanWork(Region region, BuildingType building)
    {
        return _world.Buildings[building].RequiresDeposit is not { } deposit || region.HasDeposit(deposit);
    }

    private void CollectInputs()
    {
        foreach (var region in _world.Regions)
        {
            // Один раз на область: LargestOwner перебирает доли ячеек.
            var owner = region.LargestOwner;

            foreach (var building in region.BuildingsCount)
            {
                if (!CanWork(region, building.Key)) continue;
                var info = _world.Buildings[building.Key];
                var weight = _world.CountryById(owner).Priorities.WeightOf(info.Sector);

                foreach (var input in info.Inputs)
                {
                    var demand = new GoodAmount(building.Value * input.Value.Raw);
                    _inputs.Add(owner, input.Key, demand);
                    _claims.Add(owner, input.Key, demand * weight / Priorities.NormalWeight);
                }
                _working.Add(owner, building.Key, building.Value);
            }
        }
    }

    private void CollectAvailable()
    {
        foreach (var (country, good, _) in _inputs)
        {
            _available.Set(country, good, _world.CountryById(country).Stock.Of(good));
        }
    }

    private void CollectOutputs()
    {
        foreach (var (country, building, count) in _working)
        {
            var recipe = _world.Buildings[building];
            var weight = _world.CountryById(country).Priorities.WeightOf(recipe.Sector);

            // Доля общая на всех, поэтому расход рецепта в ней сокращается.
            // Умножаем до деления, иначе целые числа дадут ноль.
            long runs = count * Load.Full;
            foreach (var (good, _) in recipe.Inputs)
            {
                GoodAmount available = _available.Get(country, good);
                GoodAmount demand = _inputs.Get(country, good);
                // Хватает всем — загрузка остаётся полной. Заодно не считаем самое
                // большое произведение: переполниться оно могло бы только здесь.
                if (available >= demand) continue;

                GoodAmount claims = _claims.Get(country, good);
                runs = Math.Min(
                    runs,
                    (long)(
                        (Int128)Load.Full * count * weight * available.Raw /
                        (claims.Raw * Priorities.NormalWeight)
                    )
                );
            }

            if (runs == 0) continue;

            // Приведение безопасно: runs не может превысить count, с которого начали.
            var consumed = _world.CountryById(country).Stock.TryConsume(recipe.Inputs, runs);
            if (!consumed) throw new InvalidOperationException("Не получилось потратить предметы " +
                                                               "со склада, ошибка в расчетах в коде");

            foreach (var (good, amount) in recipe.Outputs)
            {
                _outputs.Add(country, good, amount * runs / Load.Full);
            }
        }
    }

    private void Store()
    {
        foreach (var (country, good, amount) in _outputs)
        {
            _world.CountryById(country).Stock.Store(good, amount);
        }
    }
}
