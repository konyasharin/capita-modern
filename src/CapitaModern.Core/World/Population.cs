namespace CapitaModern.Core.World;

public sealed class Population
{
    private long _raw;
    private const int Scale = 100;

    public Population(long count)
    {
        _raw = count;
    }

    public long Count => _raw / Scale;

    public void Grow(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        _raw += count;
    }

    public void Lose(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        _raw -= count;
    }
}
