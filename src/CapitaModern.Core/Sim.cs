namespace CapitaModern.Core;

public sealed class Sim
{
    public int Day { get; private set; }

    public void Tick()
    {
        Day++;
    }
}
