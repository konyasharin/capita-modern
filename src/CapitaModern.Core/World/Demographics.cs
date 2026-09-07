namespace CapitaModern.Core.World;

public sealed class Demographics
{
    public Population Population { get; private set; }

    public Demographics(Population population)
    {
        Population = population;
    }

    public void Grow(Population count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count.Raw);
        Population += count;
    }

    public void Lose(Population count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count.Raw);
        if ((Population - count).Raw < 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Невозможно уменьшить население, " +
                                                  "население будет меньше 0 после уменьшения");
        Population -= count;
    }
}
