using Engine.ECS.Systems;
using Game.Modules.Burning.Components;
using Game.Modules.Burning.Systems;
using Game.Modules.Death.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatModifiers;
using Game.Modules.Death;
using Game.Modules.Race;
using Engine.Modules;

namespace Game.Modules.Burning;

/// <summary>
/// Burning-specific: its own entity-scoped and body-part-scoped timer components and systems,
/// depending on StatusEffectsModule (shared immunity storage) and HealthModule (what it damages).
/// Registers a BurningApplier (dispatches entity-scoped vs
/// body-part-scoped per grant -- see its own doc comment) into the shared
/// StatusEffectApplierRegistry during Configure, so anything that grants Burning -- an action, a terrain
/// contact, an aura -- does it without depending on this module directly.
/// </summary>
public sealed class BurningModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000008");

    public Guid Id => ModuleId;

    public IReadOnlyList<Guid> Requires { get; } = [StatusEffectsModule.ModuleId, HealthModule.ModuleId, StatModifiersModule.ModuleId, DeathModule.ModuleId, RaceModule.ModuleId];

    public void Configure(GameModuleContext context)
    {
        context.StatusEffectAppliers.Register(new BurningApplier(context.ComponentManager, context.Definitions, context.EventBus, context.PlayerQuery));
        context.StatusEffectDisplays.Register(new BurningDisplay(
            context.ComponentManager.GetPackedPool<BurningTimerComponent>(),
            context.ComponentManager.GetMultiPool<BodyPartBurningTimerComponent>()));
    }

    /// <summary>Frames until a burn at stackCount runs out: the running tick, then one tick per remaining stack. Shared by BurningDisplay and HealthWindow's per-part line so the two can't disagree.</summary>
    public static int RemainingFrames(uint nextTickFrame, byte stackCount, long now) =>
        FrameDeadline.Remaining(nextTickFrame, now) + (stackCount - 1) * BurningEffects.TickIntervalFrames;

    public void RegisterComponents(ComponentRegistration registration)
    {
        var componentManager = registration.ComponentManager;

        componentManager.RegisterPackedPool<BurningTimerComponent>(static (ref existing, incoming) => { });
        componentManager.RegisterMultiPool<BodyPartBurningTimerComponent>();
    }

    public void RegisterBehavior(BehaviorRegistration<GameModuleContext> registration)
    {
        var context = registration.Context;
        var systemManager = registration.SystemManager;
        var componentManager = registration.ComponentManager;

        var statModifiers = componentManager.GetMultiPool<StatModifierComponent>();
        var bodyParts = EntityBodyParts.For(componentManager, context.Definitions);
        var deadEntities = componentManager.GetPackedPool<DeadComponent>();
        var damageLedger = DamageLedger.For(componentManager, context.EntityKeys);

        systemManager.Register(new BurningSystem(
            componentManager.GetPackedPool<BurningTimerComponent>(),
            componentManager.GetPackedPool<SimpleHealthComponent>(),
            context.EventBus,
            context.PlayerQuery,
            context.MathUtility,
            statModifiers,
            bodyParts,
            deadEntities,
            damageLedger,
            context.FloatingTextFeed));

        systemManager.Register(new BodyPartBurningSystem(
            componentManager.GetMultiPool<BodyPartBurningTimerComponent>(),
            EntityBodyParts.For(componentManager, context.Definitions),
            componentManager.GetPackedPool<SimpleHealthComponent>(),
            context.EventBus,
            context.PlayerQuery,
            statModifiers,
            deadEntities,
            damageLedger,
            context.FloatingTextFeed));
    }
}
