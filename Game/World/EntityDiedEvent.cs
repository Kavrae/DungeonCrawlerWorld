using Engine.Events;

namespace Game.World;

/// <summary>
/// Published by HealthDamage.Apply on the wasAlive -> 0 CurrentHealth transition, for any
/// entity except the player (see that method's own doc comment). IBufferedEvent: HealthDamage.
/// Apply is called from deep inside other systems' own per-entity scans (BurningSystem.Tick,
/// ContactDamageSystem.Tick, ActionEffectResolver), so an immediate publish that synchronously
/// destroyed/mutated component pools mid-scan would corrupt whichever scan is currently
/// in-flight -- the same hazard CountdownTicker.Tick's own deferred-removal contract exists to
/// avoid. Buffering defers actual handling (see DeathSystem) to a dedicated system's own
/// Update(), never from inside another system's per-entity loop.
/// </summary>
public readonly record struct EntityDiedEvent(int EntityId, StatusEffectSource Source) : IBufferedEvent;
