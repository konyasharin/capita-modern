namespace CapitaModern.Core.World;

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
