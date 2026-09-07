using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Effects;
using Game.Modules.Health.Components;

namespace Tests.Modules.Actions.Effects;

/// <summary>
/// Regression coverage for a real, confirmed bug: DodgeActivation used to grant DodgingComponent to
/// context.TargetEntityId, the same convention every damage/heal effect uses. That happened to work
/// only because the old (now-replaced) direct World.MoveEntity relocation placed the caster at the
/// resolved target tile *before* this effect ran. Once relocation became a queued
/// MovementComponent.NextMapPosition move (see ActionTargetingController.TryRelocateForDodge's own
/// doc comment), the caster is usually still standing somewhere else -- for a directional dodge the
/// "target tile" is the *destination*, which is empty until MovementSystem later applies the queued
/// move, so ActionEffectResolver.Apply's own occupant lookup never even calls this effect at all
/// (confirmed live: no DodgingComponent ever granted for a directional dodge). Dodge's benefit must
/// always land on SourceEntityId (the caster), regardless of what TargetEntityId happens to resolve
/// to for this one activation.
/// </summary>
[TestClass]
public sealed class DodgeActivationTests
{
    private const int SourceEntityId = 1;
    private const int TargetEntityId = 2;

    private static (ComponentManager ComponentManager, ActionEffectContext Context) Build(MultiComponentPool<AbilityScoreComponent>? abilityScores = null)
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10);
        componentManager.RegisterPackedPool<DodgingComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<SimpleHealthComponent>(static (ref existing, incoming) => existing = incoming);

        var context = new ActionEffectContext(
            SourceEntityId: SourceEntityId,
            TargetEntityId: TargetEntityId,
            Health: componentManager.GetPackedPool<SimpleHealthComponent>(),
            EventBus: new EventBus(),
            MathUtility: new MathUtility(new Random()),
            ComponentManager: componentManager,
            ActivatorName: "Dodge",
            ActivatorTags: [],
            AbilityScores: abilityScores);

        return (componentManager, context);
    }

    [TestMethod]
    public void Apply_GrantsDodgingComponentToSourceEntityId_NotTargetEntityId()
    {
        var (componentManager, context) = Build();

        new DodgeActivation().Apply(context);

        var dodgingEntities = componentManager.GetPackedPool<DodgingComponent>();
        Assert.IsTrue(dodgingEntities.Has(SourceEntityId), "The caster must always receive the immunity window, regardless of what TargetEntityId happens to resolve to for this activation.");
        Assert.IsFalse(dodgingEntities.Has(TargetEntityId), "Only the caster -- never whoever the resolved target tile's occupant turns out to be -- should receive it.");
    }

    [TestMethod]
    public void Apply_ReadsDexterityFromSourceEntityId_NotTargetEntityId()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10);
        componentManager.RegisterPackedPool<DodgingComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<SimpleHealthComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<AbilityScoreComponent>();

        var abilityScores = componentManager.GetMultiPool<AbilityScoreComponent>();
        abilityScores.Add(SourceEntityId, new AbilityScoreComponent(AbilityScoreType.Dexterity, baseValue: 300, total: 300));
        // TargetEntityId deliberately has no Dexterity at all -- if Apply read the wrong entity it
        // would silently fall back to WindowFrames (Dexterity 1's own value) instead of the boosted one.

        var context = new ActionEffectContext(
            SourceEntityId: SourceEntityId,
            TargetEntityId: TargetEntityId,
            Health: componentManager.GetPackedPool<SimpleHealthComponent>(),
            EventBus: new EventBus(),
            MathUtility: new MathUtility(new Random()),
            ComponentManager: componentManager,
            ActivatorName: "Dodge",
            ActivatorTags: [],
            AbilityScores: abilityScores);

        new DodgeActivation().Apply(context);

        var dodgingEntities = componentManager.GetPackedPool<DodgingComponent>();
        Assert.AreEqual(DodgeEffects.MaxWindowFrames, dodgingEntities.GetReadonly(SourceEntityId).FramesRemaining,
            "Dexterity total 300 must yield the maxed-out window -- proves the lookup used SourceEntityId, not TargetEntityId (which has no AbilityScoreComponent at all).");
    }

    [TestMethod]
    public void Apply_NoAbilityScoresPool_FallsBackToWindowFrames()
    {
        var (componentManager, context) = Build();

        new DodgeActivation().Apply(context);

        Assert.AreEqual(DodgeEffects.WindowFrames, componentManager.GetPackedPool<DodgingComponent>().GetReadonly(SourceEntityId).FramesRemaining);
    }
}
