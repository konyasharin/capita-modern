using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;

namespace CapitaModern.Core.World;

/// <summary>
/// Тик экономики: предприятия съедают сырьё и выдают продукцию.
/// </summary>
/// <remarks>
/// Тик идёт в два прохода. Первый собирает спрос всех предприятий страны, второй
/// производит. Между ними становится известно, какая часть заказанного будет выдана,
/// и дефицит делится на всех потребителей товара сразу — иначе первый по счёту тип
/// заводов выел бы весь уголь, а остальным не досталось бы ничего, и результат
/// зависел бы от порядка обхода.
/// </remarks>
public sealed class Simulation
{
    private readonly GameWorld _world;
    private readonly Tally<GoodType> _inputs = new();
    private readonly Tally<GoodType> _outputs = new();
    private readonly Tally<GoodType> _available = new();
    private readonly Tally<BuildingType> _working = new();

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

    /// <summary>Счётчики живут ровно один тик. Чистка в начале, а не в конце: тогда
    /// числа прошлого тика доступны для разбора, и дописанный ниже код не окажется
    /// молча работающим с пустыми таблицами.</summary>
    private void Prepare()
    {
        _inputs.Clear();
        _outputs.Clear();
        _available.Clear();
        _working.Clear();
    }

    private bool CanWork(Region region, BuildingType building)
    {
        return _world.Buildings[building].RequiresDeposit is not { } deposit || region.HasDeposit(deposit);
    }

    private void CollectInputs()
    {
        foreach (var region in _world.Regions)
        {
            // Владелец берётся один раз на область: LargestOwner перебирает доли
            // ячеек, а внутри области он не меняется.
            var owner = region.LargestOwner;

            foreach (var building in region.BuildingsCount)
            {
                if (!CanWork(region, building.Key)) continue;
                foreach (var input in _world.Buildings[building.Key].Inputs)
                {
                    _inputs.Add(owner, input.Key, building.Value * input.Value);
                }
                _working.Add(owner, building.Key, building.Value);
            }
        }
    }

    private void CollectAvailable()
    {
        foreach (var (country, good, _) in _inputs)
        {
            _available.Set(country, good, _world.CountryById(country).StockOf(good));
        }
    }

    private void CollectOutputs()
    {
        foreach (var (country, building, count) in _working)
        {
            var recipe = _world.Buildings[building];

            long runs = count;
            foreach (var (good, _) in recipe.Inputs)
            {
                long available = _available.Get(country, good);
                long input = _inputs.Get(country, good);
                runs = Math.Min(runs, count * available / input);
            }

            if (runs == 0) continue;

            var consumed = _world.CountryById(country).TryConsume(recipe.Inputs, (int)runs);
            if (!consumed) throw new InvalidOperationException("Не получилось потратить предметы " +
                                                               "со склада, ошибка в расчетах в коде");

            foreach (var (good, amount) in recipe.Outputs)
            {
                _outputs.Add(country, good, amount * runs);
            }
        }
    }

    private void Store()
    {
        foreach (var (country, good, amount) in _outputs)
        {
            _world.CountryById(country).Store(good, amount);
        }
    }
}
