using Engine.Events;
using Engine.Math;
using Game.World;

namespace Game.Modules;

/// <summary>
/// A record, not positional Configure parameters, so a future fourth piece of context is a
/// new property rather than a signature break for every module already written against
/// IGameModule.Configure -- PlayerQuery below is exactly that: an optional init property, not
/// a fourth positional parameter, so existing test call sites didn't need to change.
/// </summary>
public sealed record GameModuleContext(IMapQuery MapQuery, MathUtility MathUtility, EventBus EventBus)
{
    public IPlayerQuery? PlayerQuery { get; init; }
}