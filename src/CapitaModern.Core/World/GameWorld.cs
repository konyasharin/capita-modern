namespace CapitaModern.Core.World;

/// <summary>
/// Состояние партии: единственный владелец массивов регионов и стран.
/// Всё остальное получает его ссылкой, а не хранит свои копии.
/// </summary>
public class GameWorld
{
    private Region[] _regions;
    private Country[] _countries;

    public GameWorld(Region[] regions, Country[] countries)
    {
        _regions = regions;
        _countries = countries;
    }
}
