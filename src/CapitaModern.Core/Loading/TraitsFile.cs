using CapitaModern.Core.Politics;

namespace CapitaModern.Core.Loading;

/// <summary>Черты стран и раздача: кому какая досталась.</summary>
/// <param name="Who">Имя черты — список стран по трёхбуквенному коду.</param>
/// <param name="Why">По какому признаку из жизни черта дана. Коду не нужно, но без этого
/// через год не вспомнить, откуда взялся список.</param>
public record TraitsFile(
    TraitDto[] Traits,
    Dictionary<string, string[]> Who,
    Dictionary<string, string>? Why = null);

public record TraitDto(
    string Name,
    string Tells,
    int Prints = 0,
    int Borrows = 0,
    int Arms = 0,
    int Invests = 0,
    int Hoards = 0,
    int Feeds = 0,
    int Holds = 0,
    int Builds = 0)
{
    public Trait ToTrait() => new(Name, Tells, Prints, Borrows, Arms, Invests, Hoards, Feeds, Holds, Builds);
}
