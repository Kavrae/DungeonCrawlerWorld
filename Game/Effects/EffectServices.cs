using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.Events;
using Engine.Math;
using Game.Blueprints;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Auras;
using Game.Modules.Auras.Components;
using Game.Modules.Death;
using Game.Modules.Death.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Mana;
using Game.Modules.Mana.Components;
using Game.Modules.Race;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffects;
using Game.World;

namespace Game.Effects;

/// <summary>The session's pools and services every effect entry works with, the same for every application.</summary>
/// <remarks>
/// One per build (GameModuleContext.EffectServices), handed to everything that applies effects, so
/// an applier passes this and what is its own -- who caused the effect, whom it lands on, when --
/// rather than each naming every pool an entry might read.
/// </remarks>
public sealed record EffectServices(
    ComponentManager ComponentManager,
    EntityKeys EntityKeys,
    EventBus EventBus,
    MathUtility MathUtility,
    IPlayerQuery PlayerQuery,
    BlueprintRegistry Definitions,
    PackedComponentPool<SimpleHealthComponent> Health,
    EntityBodyParts BodyParts,
    PackedComponentPool<DeadComponent> DeadEntities,
    MultiComponentPool<StatModifierComponent> StatModifiers,
    PackedComponentPool<AbilityScoresComponent> AbilityScores,
    PackedComponentPool<ManaComponent> Mana,
    PackedComponentPool<HotkeyExpansionUnlockComponent> HotkeyExpansionUnlocks,
    StatusEffectApplierRegistry StatusEffectAppliers,
    AuraSources AuraSources,
    FloatingTextFeed FloatingTextFeed)
{
    /// <summary>The modules whose pools the services are made from. A module that applies effects (reads GameModuleContext.EffectServices) lists these in its Requires.</summary>
    public static IReadOnlyList<Guid> RequiredModuleIds { get; } =
    [
        HealthModule.ModuleId, RaceModule.ModuleId, DeathModule.ModuleId, StatModifiersModule.ModuleId,
        AbilityScoresModule.ModuleId, ManaModule.ModuleId, ActionsModule.ModuleId, AurasModule.ModuleId,
    ];

    /// <summary>The services over a build's registered pools.</summary>
    public static EffectServices For(
        ComponentManager componentManager,
        EntityKeys entityKeys,
        EventBus eventBus,
        MathUtility mathUtility,
        IPlayerQuery playerQuery,
        BlueprintRegistry definitions,
        StatusEffectApplierRegistry statusEffectAppliers,
        AuraCatalog auras,
        FloatingTextFeed floatingTextFeed) =>
        new(
            componentManager,
            entityKeys,
            eventBus,
            mathUtility,
            playerQuery,
            definitions,
            componentManager.GetPackedPool<SimpleHealthComponent>(),
            EntityBodyParts.For(componentManager, definitions),
            componentManager.GetPackedPool<DeadComponent>(),
            componentManager.GetMultiPool<StatModifierComponent>(),
            componentManager.GetPackedPool<AbilityScoresComponent>(),
            componentManager.GetPackedPool<ManaComponent>(),
            componentManager.GetPackedPool<HotkeyExpansionUnlockComponent>(),
            statusEffectAppliers,
            new AuraSources(componentManager.GetMultiPool<AuraSourceComponent>(), auras, eventBus),
            floatingTextFeed);
}
