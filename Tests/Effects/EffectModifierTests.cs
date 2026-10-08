using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Math;
using Engine.Tags;
using Game.Blueprints.Races;
using Game.Bootstrap;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Auras;
using Game.Modules.Auras.Components;
using Game.Modules.Core.Components;
using Game.Modules.Mana.Components;
using Game.Modules.Movement.Components;
using Game.Modules.Poison.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffects;
using Game.Spawning;
using Game.Tags;
using Game.World;
using Microsoft.Xna.Framework;

namespace Tests.Effects;

/// <summary>Every entry with an amount scales it through Outgoing modifiers on its cause and Incoming ones on its target, conditional on the activator's tags; with no source entity, only Incoming applies.</summary>
[TestClass]
public sealed class EffectModifierTests
{
    private const int Ground = (int)MapLayer.Ground;

    private static readonly AuraDefinition Glow = new(new Guid("00000000-0000-0000-0000-0000000000e1"), "Glow", Color.White);

    private sealed record Session(GameBuildPassResult Build, int SourceId, int TargetId)
    {
        public ComponentManager Components => Build.EcsContext.ComponentManager;

        /// <summary>Doubles target on entityId (a multiplicative +100%), optionally only for activators carrying conditionTag.</summary>
        public void Double(int entityId, StatModifierTarget target, GameplayTag conditionTag = default) =>
            StatModifierEffects.Apply(Components, entityId, target, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, canModify: true, magnitude: 1f, FrameDeadline.Never, ActionSource.Admin, conditionTag);

        public EffectContext Context(GameplayTagSet tags = default) =>
            EffectContext.FromEntity(Build.Context.EffectServices, SourceId, TargetId, "Test", tags, Build.Context.SimulationClock.CurrentFrame);

        public EffectContext SourcelessContext() =>
            new(Build.Context.EffectServices, ActionSource.Admin, SourceEntityId: null, TargetId, "Test", default, Build.Context.SimulationClock.CurrentFrame);

        public void SetMana(int entityId, float current, float maximum)
        {
            var mana = Components.GetPackedPool<ManaComponent>();
            mana.Remove(entityId);
            mana.Add(entityId, new ManaComponent(current, maximum));
        }

        public float ManaOf(int entityId) => Components.GetPackedPool<ManaComponent>().GetReadonly(entityId).CurrentMana;

        public int PoisonStacksOf(int entityId) => Components.GetPackedPool<PoisonTimerComponent>().TryGetReadonly(entityId, out var poison) ? poison.StackCount : 0;
    }

    private static Session BuildSession()
    {
        var build = BuiltInTestModules.Build(new Map(new Vector3Int(20, 20, 3)), new MathUtility(new Random(1)), initialEntityCapacity: 100, initialComponentCapacity: 100);
        build.Context.ProcessingTierResolver.SetReferencePosition(new Vector3Int(5, 5, Ground));
        int Spawn(Vector3Int position)
        {
            var entityId = build.Factory.Spawn(SpawnRequest.At(build.Context.Definitions.GetId(Goblin.Id), position) with { Seed = 1 });
            build.EcsContext.ComponentManager.GetPackedPool<MovementComponent>().Remove(entityId);
            return entityId;
        }

        return new Session(build, Spawn(new Vector3Int(5, 5, Ground)), Spawn(new Vector3Int(7, 5, Ground)));
    }

    [TestMethod]
    public void ManaRestore_OutgoingAndIncoming_EachDouble()
    {
        var session = BuildSession();
        session.SetMana(session.TargetId, 0, 100);
        session.Double(session.SourceId, StatModifierTarget.OutgoingManaRestore);
        session.Double(session.TargetId, StatModifierTarget.IncomingManaRestore);

        new DirectManaRestore(0.1f).Apply(session.Context());

        Assert.AreEqual(40f, session.ManaOf(session.TargetId), 0.5f, "10% of 100, doubled twice.");
    }

    [TestMethod]
    public void ManaDrain_IncomingScalesWhatThePayerLoses()
    {
        var session = BuildSession();
        session.SetMana(session.TargetId, 50, 50);
        session.Double(session.TargetId, StatModifierTarget.IncomingManaDrain);

        new ManaDrain(10).Apply(session.Context());

        Assert.AreEqual(30f, session.ManaOf(session.TargetId), 0.5f);
    }

    [TestMethod]
    public void StatusStacks_OutgoingDoublesThem()
    {
        var session = BuildSession();
        session.Double(session.SourceId, StatModifierTarget.OutgoingStatusStacks);

        new StatusEffectGrant(StatusEffectType.Poison, StackCount: 3).Apply(session.Context());

        Assert.AreEqual(6, session.PoisonStacksOf(session.TargetId));
    }

    [TestMethod]
    public void StatusStacks_Sourceless_RunIncomingOnly()
    {
        var session = BuildSession();
        session.Double(session.SourceId, StatModifierTarget.OutgoingStatusStacks);
        session.Double(session.TargetId, StatModifierTarget.IncomingStatusStacks);

        new StatusEffectGrant(StatusEffectType.Poison, StackCount: 3).Apply(session.SourcelessContext());

        Assert.AreEqual(6, session.PoisonStacksOf(session.TargetId), "No source entity: its Outgoing modifiers can't apply; the target's Incoming does.");
    }

    [TestMethod]
    public void StatusStacks_TagConditionedModifier_AppliesOnlyToActivatorsCarryingTheTag()
    {
        var session = BuildSession();
        session.Double(session.SourceId, StatModifierTarget.OutgoingStatusStacks, GameTags.DamageFire);

        new StatusEffectGrant(StatusEffectType.Poison, StackCount: 2).Apply(session.Context());
        Assert.AreEqual(2, session.PoisonStacksOf(session.TargetId), "Not a fire activator.");

        new StatusEffectGrant(StatusEffectType.Poison, StackCount: 2).Apply(session.Context([GameTags.DamageFire]));
        Assert.AreEqual(6, session.PoisonStacksOf(session.TargetId), "2, then 4 more from a fire activator.");
    }

    [TestMethod]
    public void ProcChance_OutgoingDoublesAHalfChanceToCertain()
    {
        var session = BuildSession();
        session.SetMana(session.TargetId, 100, 100);
        session.Double(session.SourceId, StatModifierTarget.OutgoingProcChance);
        var chained = new ChainedEffect(0.5f, [new Effect([new ManaDrain(1)])]);

        for (var attempt = 0; attempt < 40; attempt++)
        {
            chained.Apply(session.Context());
        }

        Assert.AreEqual(60f, session.ManaOf(session.TargetId), 0.5f, "Every one of 40 attempts triggered.");
    }

    [TestMethod]
    public void AuraPowerAndSize_OutgoingScalesBoth()
    {
        var session = BuildSession();
        session.Double(session.SourceId, StatModifierTarget.OutgoingAuraPower);
        session.Double(session.SourceId, StatModifierTarget.OutgoingAuraSize);

        new AuraSourceGrant(Glow, Power: 8, Size: 3).Apply(session.Context());

        var source = session.Components.GetMultiPool<AuraSourceComponent>().GetReadonlyByDenseIndex(session.Components.GetMultiPool<AuraSourceComponent>().GetFirstDenseIndex(session.TargetId));
        Assert.AreEqual((16, 6), (source.Power, source.Size));
    }

    [TestMethod]
    public void AuraPowerAndSize_IncomingScalesWhatTheRadiatingEntityGets()
    {
        var session = BuildSession();
        session.Double(session.TargetId, StatModifierTarget.IncomingAuraPower);

        new AuraSourceGrant(Glow, Power: 8, Size: 3).Apply(session.Context());

        var sources = session.Components.GetMultiPool<AuraSourceComponent>();
        Assert.AreEqual(16, sources.GetReadonlyByDenseIndex(sources.GetFirstDenseIndex(session.TargetId)).Power);
    }

    [TestMethod]
    public void AuraPower_PlacedOnATile_RunsOutgoingOnly()
    {
        var session = BuildSession();
        session.Double(session.SourceId, StatModifierTarget.OutgoingAuraPower);
        var tile = new Vector3Int(9, 9, Ground);

        var outcome = new AuraSourceGrant(Glow, Power: 8, Size: 3).Apply(session.Context() with { TargetEntityId = EffectContext.NoTargetEntity, TargetLocation = tile });

        Assert.AreEqual(EffectOutcome.Applied, outcome);
        var anchorId = session.Components.GetPackedPool<AuraAnchorComponent>().EntityIds.ToArray().Single();
        var sources = session.Components.GetMultiPool<AuraSourceComponent>();
        Assert.AreEqual(16, sources.GetReadonlyByDenseIndex(sources.GetFirstDenseIndex(anchorId)).Power, "The placer's Outgoing doubles it; an anchor has no Incoming.");
    }

    [TestMethod]
    public void AuraDuration_IsScaledLikeABuffs()
    {
        var session = BuildSession();
        session.Double(session.SourceId, StatModifierTarget.OutgoingBuffDuration);
        session.Double(session.TargetId, StatModifierTarget.IncomingBuffDuration);
        var context = session.Context();

        new AuraSourceGrant(Glow, Power: 8, Size: 3, DurationFrames: 60).Apply(context);

        Assert.AreEqual(FrameDeadline.After(context.Now, 240), session.Components.GetPackedPool<AuraSourceExpiryComponent>().GetReadonly(session.TargetId).ExpiresAtFrame);
    }

    [TestMethod]
    public void ShownSpellCost_IsWhatCastingItTakes()
    {
        var session = BuildSession();
        session.SetMana(session.SourceId, 100, 100);
        StatModifierEffects.Apply(session.Components, session.SourceId, StatModifierTarget.OutgoingManaDrain, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff, canModify: true, magnitude: 0.5f, FrameDeadline.Never, ActionSource.Admin, GameTags.Magic);
        StatModifierEffects.Apply(session.Components, session.SourceId, StatModifierTarget.IncomingManaDrain, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, canModify: true, magnitude: -0.25f, FrameDeadline.Never, ActionSource.Admin);
        var spell = new ActionDefinition(Guid.NewGuid(), "Test Spell", null, "s", Color.White, [], [Effect.None],
            new SpellActivator(new TargetingSpec(TargetShape.Self, Range: 0), new ActionTiming(ActionTimingCategory.Immediate)))
        {
            ActivationEffects = [new Effect([new ManaDrain(10)])],
        };
        var services = session.Build.Context.EffectServices;
        var now = session.Build.Context.SimulationClock.CurrentFrame;

        var cost = ActivationCosts.Of(services, session.SourceId, spell, now).Mana;
        ActivationEffectsApplier.Apply(services, session.SourceId, spell, now);

        Assert.AreEqual(11.25f, cost, 0.001f, "10, x1.5 for a Magic activator, x0.75 -- never rounded.");
        Assert.AreEqual(100f - cost, session.ManaOf(session.SourceId), 0.001f);
    }

    [TestMethod]
    public void ManaDrain_HalvedFromOne_TakesHalf_NotNothing()
    {
        var session = BuildSession();
        session.SetMana(session.TargetId, 10, 10);
        StatModifierEffects.Apply(session.Components, session.TargetId, StatModifierTarget.IncomingManaDrain, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, canModify: true, magnitude: -0.5f, FrameDeadline.Never, ActionSource.Admin);

        new ManaDrain(1).Apply(session.Context());
        new ManaDrain(1).Apply(session.Context());

        Assert.AreEqual(9f, session.ManaOf(session.TargetId), 0.001f, "0.5 twice: a halved cost is never rounded down to free.");
    }

    [TestMethod]
    public void ManaRestore_IsNeverRounded()
    {
        var session = BuildSession();
        session.SetMana(session.TargetId, 0, 100);

        new DirectManaRestore(0.017f).Apply(session.Context());

        Assert.AreEqual(1.7f, session.ManaOf(session.TargetId), 0.001f);
    }

    [TestMethod]
    public void Damage_ReducedBelowOnePoint_TakesTheFraction_NotNothing()
    {
        var session = BuildSession();
        const int plainTargetId = 50;
        var health = session.Components.GetPackedPool<Game.Modules.Health.Components.SimpleHealthComponent>();
        health.Add(plainTargetId, new Game.Modules.Health.Components.SimpleHealthComponent(10, 10));
        StatModifierEffects.Apply(session.Components, plainTargetId, StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, canModify: true, magnitude: -0.6f, FrameDeadline.Never, ActionSource.Admin);

        new DirectDamage(1, 1).Apply(session.SourcelessContext() with { TargetEntityId = plainTargetId });

        Assert.AreEqual(9.6f, health.GetReadonly(plainTargetId).CurrentHealth, 0.001f, "1 cut by 60% is 0.4, which is what lands.");
    }

    [TestMethod]
    public void ManaRestore_ToAFullTarget_HasNoEffect()
    {
        var session = BuildSession();
        session.SetMana(session.TargetId, 100, 100);

        Assert.AreEqual(EffectOutcome.NoEffect, new DirectManaRestore(0.5f).Apply(session.Context()));
    }

    [TestMethod]
    public void EveryEntryWithAnAmount_DeclaresItsModifierPairs_AndOnesWithoutDeclareNone()
    {
        IEffectEntry[] withAmounts =
        [
            new DirectDamage(1, 1), new DirectHeal(0.1f), new DirectManaRestore(0.1f), new ManaDrain(1), new HealthDrain(1),
            new StatusEffectGrant(StatusEffectType.Poison), new ChainedEffect(0.5f, []), new AuraSourceGrant(Glow, 8, 3),
            new StatusEffectImmunityGrant(StatusEffectType.Poison, DurationFrames: 60),
            new StatModifierGrant(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, CanModify: true, -0.1f, DurationFrames: 60),
        ];

        foreach (var entry in withAmounts)
        {
            Assert.IsNotEmpty(entry.AmountModifiers, entry.GetType().Name);
        }

        Assert.IsEmpty(((IEffectEntry)new StatusEffectRemoval(StatusEffectType.Poison)).AmountModifiers);
        Assert.IsEmpty(new StatusEffectImmunityGrant(StatusEffectType.Poison).AmountModifiers, "A permanent immunity has no duration to scale.");
    }
}
