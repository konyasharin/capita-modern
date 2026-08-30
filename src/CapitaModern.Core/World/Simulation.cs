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
    private readonly GoodsTally _inputs = new();
    private readonly GoodsTally _outputs = new();
    private readonly GoodsTally _available = new();

    public Simulation(GameWorld world)
    {
        _world = world;
    }

    public void Tick()
    {
        Prepare();

        foreach (var region in _world.Regions)
        {
            // Владелец берётся один раз на область: LargestOwner перебирает доли
            // ячеек, а внутри области он не меняется.
            var owner = region.LargestOwner;

            foreach (var building in region.BuildingsCount)
            {
                foreach (var input in _world.Buildings[building.Key].Inputs)
                {
                    _inputs.Add(owner, input.Key, building.Value * input.Value);
                }
            }
        }

        foreach (var (country, good, _) in _inputs.Entries())
        {
            _available.Set(country, good, _world.CountryById(country).StockOf(good));
        }

    }

    /// <summary>Счётчики живут ровно один тик. Чистка в начале, а не в конце: тогда
    /// числа прошлого тика доступны для разбора, и дописанный ниже код не окажется
    /// молча работающим с пустыми таблицами.</summary>
    private void Prepare()
    {
        _inputs.Clear();
        _outputs.Clear();
        _available.Clear();
    }
}
