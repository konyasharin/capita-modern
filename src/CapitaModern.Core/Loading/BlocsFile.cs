using CapitaModern.Core.Politics;

namespace CapitaModern.Core.Loading;

/// <summary>Тяготение стран из data/politics/blocs.json. Кого нет в списке —
/// <see cref="Bloc.NonAligned"/>.</summary>
public record BlocsFile
(
    Dictionary<string, Bloc> ByIso
);
