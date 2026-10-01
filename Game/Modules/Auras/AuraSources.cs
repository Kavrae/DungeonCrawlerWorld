using Engine.ECS.Components.Stores;
using Engine.Events;
using Game.Modules.Auras.Components;

namespace Game.Modules.Auras;

/// <summary>Adds and removes the auras an entity radiates, naming each aura by its definition.</summary>
/// <remarks>
/// Wraps AuraSourceEffects with the pool, catalog and event bus it needs, so a caller passes only the
/// entity, the aura and a strength. A definition the session hasn't met is registered on the way in,
/// so one made at runtime can be radiated without anything registering it first.
/// </remarks>
public sealed class AuraSources(MultiComponentPool<AuraSourceComponent> sources, AuraCatalog auras, EventBus eventBus)
{
    /// <summary>The session-local id of aura, registering it if the session hasn't met it.</summary>
    public byte GetId(AuraDefinition aura) => auras.Register(aura);

    /// <inheritdoc cref="AuraSourceEffects.Toggle"/>
    public void Toggle(int entityId, AuraDefinition aura, byte strength) =>
        AuraSourceEffects.Toggle(sources, eventBus, entityId, auras.Register(aura), strength);

    /// <inheritdoc cref="AuraSourceEffects.Apply"/>
    public void Apply(int entityId, AuraDefinition aura, byte strength) =>
        AuraSourceEffects.Apply(sources, eventBus, entityId, auras.Register(aura), strength);

    /// <inheritdoc cref="AuraSourceEffects.Revoke"/>
    public void Revoke(int entityId, AuraDefinition aura) =>
        AuraSourceEffects.Revoke(sources, eventBus, entityId, auras.Register(aura));
}
