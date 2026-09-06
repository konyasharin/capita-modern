using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;

namespace CapitaModern.Core.Politics;

/// <summary>Кому достанется дефицитный товар. Вес — множитель к заказу отрасли; при
/// избытке он ни на что не влияет.</summary>
/// <remarks>Позже веса будут ставить законы, а не код напрямую: приоритет должен чего-то
/// стоить — денег, поддержки бизнеса или политического кризиса.</remarks>
public sealed class Priorities
{
    /// <summary>Обычный вес, ×1. Втрое важнее — это 300.</summary>
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

    /// <summary>Ноль означает «не снабжать вовсе»: отрасль встанет при первой же нехватке.</summary>

    public void SetWeight(Sector sector, int weight)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(weight);
        _weights[sector] = weight;
    }
}
