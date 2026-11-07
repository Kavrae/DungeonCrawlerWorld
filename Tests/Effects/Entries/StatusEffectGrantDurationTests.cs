using Engine.ECS.Components;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
using Engine.Tags;
using Engine.Tiles;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Health.Components;
using Game.Modules.Poison;
using Game.Modules.Poison.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffects;
using Game.Tags;
using Game.World;

namespace Tests.Effects.Entries;

/// <summary>A grant's duration reaches the effect scaled once, the way every timed grant's is, and a build refuses one for an effect that takes none.</summary>
[TestClass]
public sealed class StatusEffectGrantDurationTests
{
    private const int SourceEntityId = 1;
    private const int TargetEntityId = 2;

    private static (ComponentManager ComponentManager, StatusEffectApplierRegistry Appliers) Build()
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10));
        var appliers = new StatusEffectApplierRegistry();
        appliers.Register(new TimerBasedStatusEffectApplier<PoisonTimerComponent>(StatusEffectType.Poison, componentManager.GetPackedPool<PoisonTimerComponent>(), PoisonEffects.MaxStacks, takesDuration: true,
            (entityId, count, source, now, announcesRefusal, immunityAlreadyChecked, durationTicks) => PoisonEffects.ApplyStacks(componentManager, entityId, count, source, durationTicks, now, new EventBus(), TestPlayerQuery.NoPlayer, announcesRefusal, immunityAlreadyChecked)));
        return (componentManager, appliers);
    }

    private static EffectContext Context(ComponentManager componentManager, StatusEffectApplierRegistry appliers, GameplayTagSet activatorTags = default, float durationScaleMultiplier = 1f) => TestActionEffects.Context(
        SourceEntityId: SourceEntityId,
        TargetEntityId: TargetEntityId,
        Health: componentManager.GetPackedPool<SimpleHealthComponent>(),
        EventBus: new EventBus(),
        MathUtility: new MathUtility(),
        ComponentManager: componentManager,
        EntityKeys: new EntityKeys(),
        ActivatorName: "Test",
        ActivatorTags: activatorTags,
        Now: 0,
        StatModifiers: componentManager.GetMultiPool<StatModifierComponent>(),
        StatusEffectAppliers: appliers,
        DurationScaleMultiplier: durationScaleMultiplier);

    private static void AddModifier(ComponentManager componentManager, int entityId, StatModifierTarget target, float magnitude, GameplayTag conditionTag = default) =>
        componentManager.GetMultiPool<StatModifierComponent>().Add(entityId, new StatModifierComponent(
            target, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff, canModify: false, magnitude, expiresAtFrame: FrameDeadline.Never, ActionSource.Admin, conditionTag));

    private static int RemainingDurationTicks(ComponentManager componentManager) =>
        componentManager.GetPackedPool<PoisonTimerComponent>().GetReadonly(TargetEntityId).RemainingDurationTicks;

    [TestMethod]
    public void Duration_ReachesThePoison()
    {
        var (componentManager, appliers) = Build();

        new StatusEffectGrant(StatusEffectType.Poison, StackCount: 10, DurationTicks: 6).Apply(Context(componentManager, appliers));

        Assert.AreEqual(6, RemainingDurationTicks(componentManager));
        Assert.AreEqual(10, componentManager.GetPackedPool<PoisonTimerComponent>().GetReadonly(TargetEntityId).StackCount);
    }

    [TestMethod]
    public void NoDuration_LastsThePoisonsDefault()
    {
        var (componentManager, appliers) = Build();

        new StatusEffectGrant(StatusEffectType.Poison).Apply(Context(componentManager, appliers));

        Assert.AreEqual(PoisonEffects.DefaultDurationTicks, RemainingDurationTicks(componentManager));
    }

    [TestMethod]
    public void OutgoingDebuffDurationOnTheSource_ScalesIt()
    {
        var (componentManager, appliers) = Build();
        AddModifier(componentManager, SourceEntityId, StatModifierTarget.OutgoingDebuffDuration, 1f);

        new StatusEffectGrant(StatusEffectType.Poison, DurationTicks: 5).Apply(Context(componentManager, appliers));

        Assert.AreEqual(10, RemainingDurationTicks(componentManager), "5 * (1 + 1.0) = 10.");
    }

    [TestMethod]
    public void IncomingDebuffDurationOnTheTarget_ScalesIt()
    {
        var (componentManager, appliers) = Build();
        AddModifier(componentManager, TargetEntityId, StatModifierTarget.IncomingDebuffDuration, -0.5f);

        new StatusEffectGrant(StatusEffectType.Poison, DurationTicks: 10).Apply(Context(componentManager, appliers));

        Assert.AreEqual(5, RemainingDurationTicks(componentManager), "10 * (1 - 0.5) = 5.");
    }

    [TestMethod]
    public void BuffDurationModifiers_LeaveADebuffAlone()
    {
        var (componentManager, appliers) = Build();
        AddModifier(componentManager, TargetEntityId, StatModifierTarget.IncomingBuffDuration, 1f);

        new StatusEffectGrant(StatusEffectType.Poison, DurationTicks: 6).Apply(Context(componentManager, appliers));

        Assert.AreEqual(6, RemainingDurationTicks(componentManager));
    }

    [TestMethod]
    public void ConditionalModifier_AppliesOnlyWithItsTag()
    {
        var (componentManager, appliers) = Build();
        AddModifier(componentManager, TargetEntityId, StatModifierTarget.IncomingDebuffDuration, -0.5f, GameTags.DeliveryMelee);

        new StatusEffectGrant(StatusEffectType.Poison, DurationTicks: 10).Apply(Context(componentManager, appliers));
        Assert.AreEqual(10, RemainingDurationTicks(componentManager), "No Delivery.Melee on the activation.");

        var (taggedComponentManager, taggedAppliers) = Build();
        AddModifier(taggedComponentManager, TargetEntityId, StatModifierTarget.IncomingDebuffDuration, -0.5f, GameTags.DeliveryMelee);

        new StatusEffectGrant(StatusEffectType.Poison, DurationTicks: 10).Apply(Context(taggedComponentManager, taggedAppliers, [GameTags.DeliveryMelee]));
        Assert.AreEqual(5, RemainingDurationTicks(taggedComponentManager));
    }

    [TestMethod]
    public void ScrollMultiplier_ScalesIt()
    {
        var (componentManager, appliers) = Build();

        new StatusEffectGrant(StatusEffectType.Poison, DurationTicks: 6).Apply(Context(componentManager, appliers, durationScaleMultiplier: 1.5f));

        Assert.AreEqual(9, RemainingDurationTicks(componentManager));
    }

    [TestMethod]
    public void Magnitude_NeverScalesIt()
    {
        var (componentManager, appliers) = Build();

        new StatusEffectGrant(StatusEffectType.Poison, StackCount: 2, DurationTicks: 6).Apply(Context(componentManager, appliers) with { Magnitude = 3f });

        Assert.AreEqual(6, RemainingDurationTicks(componentManager));
        Assert.AreEqual(6, componentManager.GetPackedPool<PoisonTimerComponent>().GetReadonly(TargetEntityId).StackCount);
    }

    [TestMethod]
    public void AtMaxStacks_StillRefreshesTheDuration()
    {
        var (componentManager, appliers) = Build();
        new StatusEffectGrant(StatusEffectType.Poison, StackCount: PoisonEffects.MaxStacks, DurationTicks: 2).Apply(Context(componentManager, appliers));

        new StatusEffectGrant(StatusEffectType.Poison, StackCount: 5, DurationTicks: 6).Apply(Context(componentManager, appliers));

        Assert.AreEqual(6, RemainingDurationTicks(componentManager));
        Assert.AreEqual(PoisonEffects.MaxStacks, componentManager.GetPackedPool<PoisonTimerComponent>().GetReadonly(TargetEntityId).StackCount);
    }

    [TestMethod]
    public void TopUpTo_TargetAlreadyHoldingTheStacks_RefreshesTheDuration()
    {
        var (componentManager, appliers) = Build();
        new StatusEffectGrant(StatusEffectType.Poison, StackCount: 5, Mode: StatusEffectGrantMode.TopUpTo, DurationTicks: 2).Apply(Context(componentManager, appliers));

        var outcome = new StatusEffectGrant(StatusEffectType.Poison, StackCount: 5, Mode: StatusEffectGrantMode.TopUpTo, DurationTicks: 6).Apply(Context(componentManager, appliers));

        Assert.AreEqual(EffectOutcome.NoEffect, outcome);
        Assert.AreEqual(6, RemainingDurationTicks(componentManager));
        Assert.AreEqual(5, componentManager.GetPackedPool<PoisonTimerComponent>().GetReadonly(TargetEntityId).StackCount);
    }

    [TestMethod]
    public void TopUpTo_NamingNoDuration_RefreshesThePoisonsDefault()
    {
        var (componentManager, appliers) = Build();
        var grant = new StatusEffectGrant(StatusEffectType.Poison, StackCount: 5, Mode: StatusEffectGrantMode.TopUpTo);
        grant.Apply(Context(componentManager, appliers));
        SetRemainingDurationTicks(componentManager, 1);

        grant.Apply(Context(componentManager, appliers));

        Assert.AreEqual(PoisonEffects.DefaultDurationTicks, RemainingDurationTicks(componentManager));
    }

    private static void SetRemainingDurationTicks(ComponentManager componentManager, ushort ticks)
    {
        using var timer = componentManager.GetPackedPool<PoisonTimerComponent>().Write(TargetEntityId);
        timer.Value.RemainingDurationTicks = ticks;
    }

    [TestMethod]
    public void ShorterDuration_LeavesALongerOneAlone()
    {
        var (componentManager, appliers) = Build();
        new StatusEffectGrant(StatusEffectType.Poison, DurationTicks: 6).Apply(Context(componentManager, appliers));

        new StatusEffectGrant(StatusEffectType.Poison, DurationTicks: 2).Apply(Context(componentManager, appliers));

        Assert.AreEqual(6, RemainingDurationTicks(componentManager));
        Assert.AreEqual(2, componentManager.GetPackedPool<PoisonTimerComponent>().GetReadonly(TargetEntityId).StackCount);
    }

    [TestMethod]
    public void DurationScaledToNothing_StartsNoPoison()
    {
        var (componentManager, appliers) = Build();
        AddModifier(componentManager, TargetEntityId, StatModifierTarget.IncomingDebuffDuration, -1f);

        var outcome = new StatusEffectGrant(StatusEffectType.Poison, StackCount: 3, DurationTicks: 5).Apply(Context(componentManager, appliers));

        Assert.AreEqual(EffectOutcome.NoEffect, outcome);
        Assert.IsFalse(componentManager.GetPackedPool<PoisonTimerComponent>().Has(TargetEntityId));
    }

    [TestMethod]
    public void AmountModifiers_NameTheDurationPairOnlyWhenTimed()
    {
        Assert.HasCount(1, new StatusEffectGrant(StatusEffectType.Poison).AmountModifiers);

        var timed = new StatusEffectGrant(StatusEffectType.Poison, DurationTicks: 6).AmountModifiers;
        Assert.HasCount(2, timed);
        Assert.AreEqual((StatModifierTarget.OutgoingDebuffDuration, StatModifierTarget.IncomingDebuffDuration), timed[1]);
    }

    private sealed class UntimedApplier(StatusEffectType effectType) : IStatusEffectApplier
    {
        public StatusEffectType EffectType { get; } = effectType;

        public int MaxStacks => 20;

        public bool TakesDuration => false;

        public int GetCurrentStackCount(int entityId, byte? bodyPartId = null) => 0;

        public StatusEffectStacksApplied ApplyStacks(int entityId, int count, ActionSource source, long now, bool announcesRefusal = true, byte? bodyPartId = null, bool immunityAlreadyChecked = false, ushort? durationTicks = null) =>
            StatusEffectStacksApplied.Landed(count);

        public bool RemoveAllStacks(int entityId) => false;
    }

    private sealed class RegisteringModule(Action<GameModuleContext> configure) : IGameModule
    {
        public void RegisterComponents(ComponentRegistration registration)
        {
        }

        public void Configure(GameModuleContext context) => configure(context);

        public void RegisterBehavior(BehaviorRegistration<GameModuleContext> registration)
        {
        }
    }

    private static void BuildWithGrant(StatusEffectGrant grant) =>
        BuiltInTestModules.BuildModules([new RegisteringModule(context =>
        {
            context.StatusEffectAppliers.Register(new UntimedApplier(StatusEffectType.Burning));
            context.Actions.Register(new ActionDefinition(Guid.NewGuid(), "Smoulder", null, "?", default, [], Effects: [new Effect([grant])],
                Activator: new DirectAction(new TargetingSpec(TargetShape.Self, MaximumRange: 0, Anchor: TargetAnchor.Caster), new ActionTiming(ActionTimingCategory.Immediate))));
        })]);

    [TestMethod]
    public void Build_DurationForAnEffectThatTakesNone_Throws_NamingBoth()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => BuildWithGrant(new StatusEffectGrant(StatusEffectType.Burning, DurationTicks: 6)));

        Assert.Contains("Action 'Smoulder'", exception.Message);
        Assert.Contains("Burning", exception.Message);
    }

    [TestMethod]
    public void Build_ZeroDuration_Throws_NamingTheDefinition()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => BuildWithGrant(new StatusEffectGrant(StatusEffectType.Poison, DurationTicks: 0)));

        Assert.Contains("Action 'Smoulder'", exception.Message);
        Assert.Contains("0 ticks", exception.Message);
    }

    [TestMethod]
    public void Build_NoDurationForAnEffectThatTakesNone_Builds()
    {
        BuildWithGrant(new StatusEffectGrant(StatusEffectType.Burning, StackCount: 3));
    }
}
