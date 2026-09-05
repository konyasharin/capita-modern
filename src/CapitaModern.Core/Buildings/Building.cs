namespace CapitaModern.Core.Buildings;

/// <summary>Пока не используется: постройки считаются числом в <c>Region</c>.
/// Понадобится для единичных объектов со своим состоянием — АЭС, порт.</summary>
public sealed class Building
{
    public BuildingType Type { get; init; }

    public Building(BuildingType type)
    {
        Type = type;
    }
}
