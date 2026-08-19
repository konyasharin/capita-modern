using CapitaModern.Core.Loading;

namespace CapitaModern.Core.World;

/// <summary>
/// Изменяемое состояние региона: сколько ячеек за кем, население, постройки.
/// Владельца одним полем здесь нет: фронт может разрезать регион пополам,
/// поэтому принадлежность — это доли ячеек. См. docs/03-industry.md.
/// </summary>
public sealed class Region
{
    public Region()
    {

    }
}
