namespace Game.Modules.Core.Components;

/// <summary>
/// How MapWindow renders an entity that isn't an ordinary Blocking one: IsTiny draws it in
/// the tile's 3x3 tiny-entity grid, IsPhasing draws it at 50% alpha. IsTiny and IsPhasing are
/// independent -- a tiny ghost is both. Purely a rendering concern now; collision/occupancy
/// is governed separately by NonBlockingComponent/ForceBlockingComponent (see
/// IMapQuery.IsBlocking) -- an entity with neither of those is Blocking regardless of what
/// this component says, so the two are set together wherever Tiny/Phasing behavior is granted.
/// </summary>
public struct OccupancyComponent(bool isTiny, bool isPhasing)
{
    public bool IsTiny { get; set; } = isTiny;

    public bool IsPhasing { get; set; } = isPhasing;
}