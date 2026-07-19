using Engine.Events;
using Engine.Math;
using Game.World;

namespace Game.Modules;

/// <summary>
/// A record, not positional Configure parameters, so a future fourth piece of context is a
/// new property rather than a signature break for every module already written against
/// IGameModule.Configure.
/// </summary>
public sealed record GameModuleContext(IMapQuery MapQuery, MathUtility MathUtility, EventBus EventBus);
