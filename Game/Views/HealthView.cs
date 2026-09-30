using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Burning.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.StatusEffects.Components;

namespace Game.Views;

/// <summary>An entity's health, the timers and immunities shown beside it, and how it died.</summary>
public sealed class HealthView(ComponentManager componentManager, EntityBodyParts entityBodyParts)
{
    private readonly PackedComponentPool<SimpleHealthComponent> _simpleHealth = componentManager.GetPackedPool<SimpleHealthComponent>();
    private readonly MultiComponentPool<BodyPartBurningTimerComponent> _bodyPartBurningTimers = componentManager.GetMultiPool<BodyPartBurningTimerComponent>();
    private readonly MultiComponentPool<StatusEffectImmunityComponent> _statusEffectImmunities = componentManager.GetMultiPool<StatusEffectImmunityComponent>();
    private readonly PackedComponentPool<DeadComponent> _deaths = componentManager.GetPackedPool<DeadComponent>();

    /// <summary>entityId's simple health, if it has simple rather than complex health.</summary>
    public bool TryGetSimpleHealth(int entityId, out SimpleHealthComponent health) => _simpleHealth.TryGetReadonly(entityId, out health);

    /// <inheritdoc cref="HealthQueries.TryGetTotals"/>
    public bool TryGetTotals(int entityId, out float current, out float maximum) =>
        HealthQueries.TryGetTotals(_simpleHealth, entityBodyParts, entityId, out current, out maximum);

    /// <summary>Replaces destination's contents with every burning timer on entityId's body parts.</summary>
    public void CopyBodyPartBurningTimers(int entityId, List<BodyPartBurningTimerComponent> destination) => CopyAll(_bodyPartBurningTimers, entityId, destination);

    /// <summary>Replaces destination's contents with every status effect entityId is immune to, and until when.</summary>
    public void CopyStatusEffectImmunities(int entityId, List<StatusEffectImmunityComponent> destination) => CopyAll(_statusEffectImmunities, entityId, destination);

    /// <summary>Who killed entityId and when, if it's dead.</summary>
    public bool TryGetDeath(int entityId, out DeadComponent death) => _deaths.TryGetReadonly(entityId, out death);

    private static void CopyAll<T>(MultiComponentPool<T> pool, int entityId, List<T> destination) where T : struct
    {
        destination.Clear();
        for (var denseIndex = pool.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = pool.GetNextDenseIndex(denseIndex))
        {
            destination.Add(pool.GetReadonlyByDenseIndex(denseIndex));
        }
    }
}
