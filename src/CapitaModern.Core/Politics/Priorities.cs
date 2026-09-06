using CapitaModern.Core.Buildings;

namespace CapitaModern.Core.Politics;

public sealed class Priorities
{
    public const int NormalWeight = 100;
    private readonly Dictionary<Sector, int> _weights = new();

    public Priorities(IReadOnlyDictionary<Sector, int>? startWeights = null)
    {
        startWeights ??= new Dictionary<Sector, int>();
        foreach (var sector in Enum.GetValues<Sector>())
        {
            _weights.Add(sector, startWeights.GetValueOrDefault(sector, NormalWeight));
        }
    }

    public int WeightOf(Sector sector) => _weights[sector];

    public void SetWeight(Sector sector, int weight)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(weight);
        _weights[sector] = weight;
    }
}
