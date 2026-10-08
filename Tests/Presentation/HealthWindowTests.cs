using Engine.ECS.Systems;
using Game.Tags;
using Game.Views;
using Engine.ECS.Components;
using Game.Modules;
using Game.Modules.Actions.Activators;
using Game.Modules.Burning;
using Game.Modules.Burning.Components;
using Game.Modules.Health.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Definitions;
using Game.Modules.Paralysis;
using Game.Modules.Paralysis.Components;
using Game.Modules.Poison;
using Game.Modules.Poison.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.UI;
using System.Linq;

namespace Tests.Presentation;

/// <summary>
/// Drives HealthWindow's pure data-assembly statics (BuildBodyPartRows/BuildStatusEffectRows)
/// directly against hand-built pools -- no GraphicsDevice-backed rendering pipeline needed, the
/// same shape InspectionWindowContentTests.ReplaceHealthEntriesWithEffectiveMaximum and
/// PlayerHealthHoverContentTests.BuildRows already use for their own body-part row assembly.
/// </summary>
[TestClass]
public sealed class HealthWindowTests
{
    private const int EntityId = 0;

    private static void BuildBodyPartRows(List<HealthWindow.BodyPartRow> rows, BodyPartTestWorld bodyPartWorld) =>
        HealthWindow.BuildBodyPartRows(rows, EntityId, new HealthView(bodyPartWorld.Components, bodyPartWorld.BodyParts), bodyPartWorld.BodyParts, new StatModifierView(bodyPartWorld.Components));

    [TestMethod]
    public void BuildBodyPartRows_ComplexFixture_OneRowPerBodyPart()
    {
        var bodyPartWorld = BodyPartTestWorld.WithParts(EntityId, ("Head", BodyPartType.Head, 10, 10, true), ("Torso", BodyPartType.Torso, 15, 20, true), ("Left Arm", BodyPartType.Arm, 8, 8, false), ("Right Arm", BodyPartType.Arm, 8, 8, false), ("Left Leg", BodyPartType.Leg, 4, 9, false), ("Right Leg", BodyPartType.Leg, 9, 9, false));

        List<HealthWindow.BodyPartRow> rows = [];
        BuildBodyPartRows(rows, bodyPartWorld);

        Assert.HasCount(6, rows);
        var torsoRow = rows.Single(row => row.Name == "Torso");
        Assert.AreEqual(15f, torsoRow.CurrentHealth);
        Assert.AreEqual(20f, torsoRow.MaximumHealth);
    }

    [TestMethod]
    public void BuildBodyPartRows_MaximumHealthBuffActive_ShowsEffectiveMaximumNotRaw()
    {
        // A part sitting at its raw maximum (10/10) must still read below that once a +50%
        // MaximumHealth buff makes its true cap 15 -- regression for the same bug
        // ComplexHealthHeal/BodyPartSelection/PlayerHealthHoverContent had.
        var bodyPartWorld = BodyPartTestWorld.WithParts(EntityId, ("Head", BodyPartType.Head, 10, 10, true));
        bodyPartWorld.Components.GetMultiPool<StatModifierComponent>().Add(EntityId, new StatModifierComponent(StatModifierTarget.MaximumHealth, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: true, magnitude: 0.5f, expiresAtFrame: FrameDeadline.Never, ActionSource.Admin));

        List<HealthWindow.BodyPartRow> rows = [];
        BuildBodyPartRows(rows, bodyPartWorld);

        Assert.HasCount(1, rows);
        Assert.AreEqual(10f, rows[0].CurrentHealth);
        Assert.AreEqual(15f, rows[0].MaximumHealth, "Raw maximum is 10, but a +50% buff makes the effective maximum 15.");
    }

    [TestMethod]
    public void BuildBodyPartRows_SimpleHealthFixture_OneRowNamedHP()
    {
        var bodyPartWorld = new BodyPartTestWorld();
        bodyPartWorld.Components.Merge(EntityId, new SimpleHealthComponent(currentHealth: 50, maximumHealth: 100));

        List<HealthWindow.BodyPartRow> rows = [];
        BuildBodyPartRows(rows, bodyPartWorld);

        Assert.HasCount(1, rows);
        Assert.AreEqual("HP", rows[0].Name);
        Assert.AreEqual(50f, rows[0].CurrentHealth);
        Assert.AreEqual(100f, rows[0].MaximumHealth);
    }

    /// <summary>Fresh ComponentManager with every timer component pool registered -- BuildStatusEffectRows reads both presence and duration through TimerBasedStatusEffectDisplay's own GetPackedPool lookup, so the pools have to live behind a real ComponentManager instead of standing alone.</summary>
    private static ComponentManager CreateComponentManagerWithStatusEffectPools()
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 8));
        return componentManager;
    }

    /// <summary>Mirrors PoisonModule/BurningModule/ParalysisModule's own real Configure registrations -- same formulas, just assembled directly instead of via GameBootstrapper.</summary>
    private static StatusEffectDisplayRegistry CreateStatusEffectDisplayRegistry(ComponentManager componentManager)
    {
        var registry = new StatusEffectDisplayRegistry();
        registry.Register(new TimerBasedStatusEffectDisplay<PoisonTimerComponent>(StatusEffectType.Poison, PoisonEffects.Glyph, componentManager.GetPackedPool<PoisonTimerComponent>(),
            (poison, now) => FrameDeadline.Remaining(poison.NextTickFrame, now) + (poison.RemainingDurationTicks - 1) * PoisonEffects.TickIntervalFrames));
        registry.Register(new TimerBasedStatusEffectDisplay<BurningTimerComponent>(StatusEffectType.Burning, BurningEffects.Glyph, componentManager.GetPackedPool<BurningTimerComponent>(),
            (burning, now) => FrameDeadline.Remaining(burning.NextTickFrame, now) + (burning.StackCount - 1) * BurningEffects.TickIntervalFrames));
        registry.Register(new TimerBasedStatusEffectDisplay<ParalysisTimerComponent>(StatusEffectType.Paralysis, ParalysisEffects.Glyph, componentManager.GetPackedPool<ParalysisTimerComponent>(),
            (paralysis, now) => FrameDeadline.Remaining(paralysis.ExpiresAtFrame, now)));
        return registry;
    }

    [TestMethod]
    public void BuildStatusEffectRows_NoActiveEffects_Empty()
    {
        var componentManager = CreateComponentManagerWithStatusEffectPools();
        List<HealthWindow.StatusEffectRow> rows = [];
        List<StatusEffectType> scratch = [];

        HealthWindow.BuildStatusEffectRows(rows, scratch, EntityId, CreateStatusEffectDisplayRegistry(componentManager), now: 0);

        Assert.IsEmpty(rows);
    }

    [TestMethod]
    public void BuildStatusEffectRows_PoisonActive_RemainingSecondsMatchesTimerFormula()
    {
        var componentManager = CreateComponentManagerWithStatusEffectPools();
        // FramesUntilNextTick 30 + (RemainingDurationTicks 3 - 1) * TickIntervalFrames 60 = 150 frames = 2.5s -> ceil to 3.
        componentManager.GetPackedPool<PoisonTimerComponent>().Add(EntityId, new PoisonTimerComponent(nextTickFrame: 30, stackCount: 1, remainingDurationTicks: 3, ActionSource.Admin));

        List<HealthWindow.StatusEffectRow> rows = [];
        List<StatusEffectType> scratch = [];
        HealthWindow.BuildStatusEffectRows(rows, scratch, EntityId, CreateStatusEffectDisplayRegistry(componentManager), now: 0);

        Assert.HasCount(1, rows);
        Assert.AreEqual(StatusEffectType.Poison, rows[0].Type);
        Assert.AreEqual(3, rows[0].RemainingSeconds);
        Assert.AreEqual(1, rows[0].StackCount);
    }

    [TestMethod]
    public void BuildStatusEffectRows_BurningActive_RemainingSecondsMatchesTimerFormula()
    {
        var componentManager = CreateComponentManagerWithStatusEffectPools();
        // FramesUntilNextTick 45 + (StackCount 2 - 1) * TickIntervalFrames 60 = 105 frames = 1.75s -> ceil to 2.
        componentManager.GetPackedPool<BurningTimerComponent>().Add(EntityId, new BurningTimerComponent(nextTickFrame: 45, stackCount: 2, ActionSource.Admin));

        List<HealthWindow.StatusEffectRow> rows = [];
        List<StatusEffectType> scratch = [];
        HealthWindow.BuildStatusEffectRows(rows, scratch, EntityId, CreateStatusEffectDisplayRegistry(componentManager), now: 0);

        Assert.HasCount(1, rows);
        Assert.AreEqual(StatusEffectType.Burning, rows[0].Type);
        Assert.AreEqual(2, rows[0].RemainingSeconds);
        Assert.AreEqual(2, rows[0].StackCount);
    }

    [TestMethod]
    public void BuildStatusEffectRows_ParalysisActive_RemainingSecondsUsesFramesUntilNextTickDirectly()
    {
        var componentManager = CreateComponentManagerWithStatusEffectPools();
        // 61 frames = 1.017s -> ceil to 2, straight off FramesUntilNextTick (no repeating tick to add on top).
        componentManager.GetPackedPool<ParalysisTimerComponent>().Add(EntityId, new ParalysisTimerComponent(expiresAtFrame: 61));

        List<HealthWindow.StatusEffectRow> rows = [];
        List<StatusEffectType> scratch = [];
        HealthWindow.BuildStatusEffectRows(rows, scratch, EntityId, CreateStatusEffectDisplayRegistry(componentManager), now: 0);

        Assert.HasCount(1, rows);
        Assert.AreEqual(StatusEffectType.Paralysis, rows[0].Type);
        Assert.AreEqual(2, rows[0].RemainingSeconds);
        Assert.AreEqual(1, rows[0].StackCount);
    }

    [TestMethod]
    public void FormatStatusEffectRow_BurningWithMultipleStacks_ShowsStackCountNoDuration()
    {
        var row = new HealthWindow.StatusEffectRow(StatusEffectType.Burning, RemainingSeconds: 5, StackCount: 5);

        Assert.AreEqual($"{BurningEffects.Glyph} Burning x5", HealthWindow.FormatStatusEffectRow(row, CreateStatusEffectDisplayRegistry(CreateComponentManagerWithStatusEffectPools())));
    }

    [TestMethod]
    public void FormatStatusEffectRow_BurningWithOneStack_OmitsStackCountAndDuration()
    {
        var row = new HealthWindow.StatusEffectRow(StatusEffectType.Burning, RemainingSeconds: 5, StackCount: 1);

        Assert.AreEqual($"{BurningEffects.Glyph} Burning", HealthWindow.FormatStatusEffectRow(row, CreateStatusEffectDisplayRegistry(CreateComponentManagerWithStatusEffectPools())));
    }

    [TestMethod]
    public void FormatStatusEffectRow_PoisonWithMultipleStacksAndDuration_ShowsParenthesizedStackCountThenDuration()
    {
        var row = new HealthWindow.StatusEffectRow(StatusEffectType.Poison, RemainingSeconds: 18, StackCount: 21);

        Assert.AreEqual($"{PoisonEffects.Glyph} Poison (x21): 18s", HealthWindow.FormatStatusEffectRow(row, CreateStatusEffectDisplayRegistry(CreateComponentManagerWithStatusEffectPools())));
    }

    [TestMethod]
    public void FormatStatusEffectRow_PoisonWithOneStack_OmitsStackCount()
    {
        var row = new HealthWindow.StatusEffectRow(StatusEffectType.Poison, RemainingSeconds: 18, StackCount: 1);

        Assert.AreEqual($"{PoisonEffects.Glyph} Poison: 18s", HealthWindow.FormatStatusEffectRow(row, CreateStatusEffectDisplayRegistry(CreateComponentManagerWithStatusEffectPools())));
    }

    /// <summary>Paralysis's own StackCount is always exactly 1 (never a stacking effect -- see ParalysisTimerComponent), so it never enters the stack-count-shown branch at all.</summary>
    [TestMethod]
    public void FormatStatusEffectRow_Paralysis_NeverShowsStackCount()
    {
        var row = new HealthWindow.StatusEffectRow(StatusEffectType.Paralysis, RemainingSeconds: 2, StackCount: 1);

        Assert.AreEqual($"{ParalysisEffects.Glyph} Paralysis: 2s", HealthWindow.FormatStatusEffectRow(row, CreateStatusEffectDisplayRegistry(CreateComponentManagerWithStatusEffectPools())));
    }

    [TestMethod]
    public void TryGetBodyPartBurningLine_PartHasActiveBodyPartScopedBurn_ReturnsFormattedLine()
    {
        var bodyPartBurningTimers = new List<BodyPartBurningTimerComponent>();
        // FramesUntilNextTick 45 + (StackCount 2 - 1) * TickIntervalFrames 60 = 105 frames = 1.75s -> ceil to 2 -- same formula the entity-scoped BurningTimerComponent display uses.
        bodyPartBurningTimers.Add(new BodyPartBurningTimerComponent(partId: 1, stackCount: 2, nextTickFrame: 45, ActionSource.Admin));

        var found = HealthWindow.TryGetBodyPartBurningLine(bodyPartBurningTimers, partId: 1, now: 0, out var text, out _);

        Assert.IsTrue(found);
        Assert.Contains("2s", text);
        Assert.Contains(BurningEffects.Glyph, text);
    }

    [TestMethod]
    public void TryGetBodyPartBurningLine_DifferentPartOnFire_ThisPartReturnsFalse()
    {
        var bodyPartBurningTimers = new List<BodyPartBurningTimerComponent>();
        bodyPartBurningTimers.Add(new BodyPartBurningTimerComponent(partId: 1, stackCount: 2, nextTickFrame: 45, ActionSource.Admin));

        var found = HealthWindow.TryGetBodyPartBurningLine(bodyPartBurningTimers, partId: 0, now: 0, out var text, out _);

        Assert.IsFalse(found);
        Assert.AreEqual(string.Empty, text);
    }

    private static ItemCatalog CreateItemCatalogWithHealthPotion()
    {
        var itemCatalog = new ItemCatalog();
        itemCatalog.Register(HealthPotion.Build());
        return itemCatalog;
    }

    [TestMethod]
    public void TryGetPotionCooldownLine_NoActiveCooldown_ReturnsFalse()
    {
        PotionCooldownComponent? potionCooldown = null;

        var found = HealthWindow.TryGetPotionCooldownLine(potionCooldown, CreateItemCatalogWithHealthPotion(), now: 0, out _, out _);

        Assert.IsFalse(found);
    }

    [TestMethod]
    public void TryGetPotionCooldownLine_FramesRemainingZero_ReturnsFalse()
    {
        PotionCooldownComponent? potionCooldown = null;
        potionCooldown = new PotionCooldownComponent(totalFrames: 1200, expiresAtFrame: 0);

        var found = HealthWindow.TryGetPotionCooldownLine(potionCooldown, CreateItemCatalogWithHealthPotion(), now: 0, out _, out _);

        Assert.IsFalse(found);
    }

    [TestMethod]
    public void TryGetPotionCooldownLine_ActiveCooldown_ReturnsFormattedLine()
    {
        PotionCooldownComponent? potionCooldown = null;
        // 121 frames = 2.017s -> ceil to 3, same PotionCooldownEffects.RemainingSeconds rounding PlayerStatusEffectsContent already relies on.
        potionCooldown = new PotionCooldownComponent(totalFrames: 1200, expiresAtFrame: 121);

        var found = HealthWindow.TryGetPotionCooldownLine(potionCooldown, CreateItemCatalogWithHealthPotion(), now: 0, out var text, out var color);

        Assert.IsTrue(found);
        Assert.AreEqual("h Potion Cooldown: 3s", text);
        Assert.AreEqual(Color.Green, color);
    }

    [TestMethod]
    public void TryGetPotionCooldownLine_HealthPotionNotInCatalog_UsesFallbackGlyphAndColor()
    {
        PotionCooldownComponent? potionCooldown = null;
        potionCooldown = new PotionCooldownComponent(totalFrames: 1200, expiresAtFrame: 60);

        var found = HealthWindow.TryGetPotionCooldownLine(potionCooldown, new ItemCatalog(), now: 0, out var text, out var color);

        Assert.IsTrue(found);
        Assert.AreEqual("? Potion Cooldown: 1s", text);
        Assert.AreEqual(Color.White, color);
    }

    private static List<StatModifierComponent> CreateStatModifiers() => [];

    [TestMethod]
    public void BuildModifierRows_NoActiveModifiers_Empty()
    {
        var statModifiers = CreateStatModifiers();
        List<HealthWindow.ModifierRow> rows = [];

        HealthWindow.BuildModifierRows(rows, statModifiers, StatModifierPolarity.Buff, now: 0);

        Assert.IsEmpty(rows);
    }

    [TestMethod]
    public void BuildModifierRows_TimedBuffActive_RemainingSecondsMatchesFrames()
    {
        var statModifiers = CreateStatModifiers();
        // 121 frames = 2.017s -> ceil to 3.
        statModifiers.Add(new StatModifierComponent(StatModifierTarget.MaximumHealth, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: true, magnitude: 0.5f, expiresAtFrame: 121, ActionSource.Admin));

        List<HealthWindow.ModifierRow> rows = [];
        HealthWindow.BuildModifierRows(rows, statModifiers, StatModifierPolarity.Buff, now: 0);

        Assert.HasCount(1, rows);
        Assert.AreEqual(StatModifierTarget.MaximumHealth, rows[0].Target);
        Assert.AreEqual(StatModifierOperation.Multiplicative, rows[0].Operation);
        Assert.AreEqual(StatModifierPolarity.Buff, rows[0].Polarity);
        Assert.AreEqual(0.5f, rows[0].Magnitude);
        Assert.AreEqual(3, rows[0].RemainingSeconds);
    }

    [TestMethod]
    public void BuildModifierRows_PermanentDebuffActive_RemainingSecondsIsNull()
    {
        var statModifiers = CreateStatModifiers();
        statModifiers.Add(new StatModifierComponent(StatModifierTarget.MovementLockFrames, StatModifierOperation.Additive, StatModifierPolarity.Debuff,
            canModify: true, magnitude: 10f, expiresAtFrame: FrameDeadline.Never, ActionSource.Admin));

        List<HealthWindow.ModifierRow> rows = [];
        HealthWindow.BuildModifierRows(rows, statModifiers, StatModifierPolarity.Debuff, now: 0);

        Assert.HasCount(1, rows);
        Assert.AreEqual(StatModifierPolarity.Debuff, rows[0].Polarity);
        Assert.IsNull(rows[0].RemainingSeconds);
    }

    [TestMethod]
    public void BuildModifierRows_TwoBuffsOnSameTarget_BothListedAsSeparateRows()
    {
        var statModifiers = CreateStatModifiers();
        statModifiers.Add(new StatModifierComponent(StatModifierTarget.OutgoingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff,
            canModify: true, magnitude: 5f, expiresAtFrame: FrameDeadline.Never, ActionSource.Admin));
        statModifiers.Add(new StatModifierComponent(StatModifierTarget.OutgoingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff,
            canModify: true, magnitude: 2f, expiresAtFrame: FrameDeadline.Never, ActionSource.Admin));

        List<HealthWindow.ModifierRow> rows = [];
        HealthWindow.BuildModifierRows(rows, statModifiers, StatModifierPolarity.Buff, now: 0);

        Assert.HasCount(2, rows);
    }

    [TestMethod]
    public void BuildModifierRows_WrongPolarity_Excluded()
    {
        var statModifiers = CreateStatModifiers();
        statModifiers.Add(new StatModifierComponent(StatModifierTarget.OutgoingDamage, StatModifierOperation.Additive, StatModifierPolarity.Debuff,
            canModify: true, magnitude: 2f, expiresAtFrame: FrameDeadline.Never, ActionSource.Admin));

        List<HealthWindow.ModifierRow> rows = [];
        HealthWindow.BuildModifierRows(rows, statModifiers, StatModifierPolarity.Buff, now: 0);

        Assert.IsEmpty(rows);
    }

    [TestMethod]
    public void BuildModifierRows_AbilityScoreTarget_Excluded()
    {
        // Strength/Intelligence/etc. modifiers are AbilityScoreWindow's own territory (see
        // AbilityScoreModifierFormatter) -- HealthWindow must not duplicate them.
        var statModifiers = CreateStatModifiers();
        statModifiers.Add(new StatModifierComponent(StatModifierTarget.Strength, StatModifierOperation.Additive, StatModifierPolarity.Buff,
            canModify: true, magnitude: 3f, expiresAtFrame: FrameDeadline.Never, ActionSource.Admin));

        List<HealthWindow.ModifierRow> rows = [];
        HealthWindow.BuildModifierRows(rows, statModifiers, StatModifierPolarity.Buff, now: 0);

        Assert.IsEmpty(rows);
    }

    [TestMethod]
    public void BuildModifierRows_ConditionTagPresent_CarriedOntoTheRow()
    {
        var statModifiers = CreateStatModifiers();
        statModifiers.Add(new StatModifierComponent(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: false, magnitude: -0.5f, expiresAtFrame: FrameDeadline.Never, ActionSource.Admin, conditionTag: GameTags.DamagePoison));

        List<HealthWindow.ModifierRow> rows = [];
        HealthWindow.BuildModifierRows(rows, statModifiers, StatModifierPolarity.Buff, now: 0);

        Assert.HasCount(1, rows);
        Assert.AreEqual(GameTags.DamagePoison, rows[0].ConditionTag);
    }

    /// <summary>Matches ResistanceTestPotion's own real grant (Game/Modules/Inventory/Definitions/ResistanceTestPotion.cs) -- Multiplicative IncomingDamage, ConditionTag: GameTags.DamagePoison, Magnitude -0.5, 10-minute duration.</summary>
    [TestMethod]
    public void FormatModifierRow_TaggedIncomingDamageReduction_ReadsAsNamedResistance()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, Magnitude: -0.5f, ConditionTag: GameTags.DamagePoison, RemainingSeconds: 600);

        Assert.AreEqual("50% Poison Resistance: 10min", HealthWindow.FormatModifierRow(row, TestGameplayTags.BuiltIn));
    }

    [TestMethod]
    public void FormatModifierRow_EffectAmountModifiers_ReadAsNamedAmounts()
    {
        HealthWindow.ModifierRow Row(StatModifierTarget target, float magnitude) => new(target, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, magnitude, ConditionTag: default, RemainingSeconds: null);

        Assert.AreEqual("-50% Mana Costs", HealthWindow.FormatModifierRow(Row(StatModifierTarget.IncomingManaDrain, -0.5f), TestGameplayTags.BuiltIn));
        Assert.AreEqual("x2 Status Effect Stacks", HealthWindow.FormatModifierRow(Row(StatModifierTarget.OutgoingStatusStacks, 1f), TestGameplayTags.BuiltIn));
        Assert.AreEqual("x2 Aura Size", HealthWindow.FormatModifierRow(Row(StatModifierTarget.OutgoingAuraSize, 1f), TestGameplayTags.BuiltIn));
        Assert.AreEqual("+50% Aura Power", HealthWindow.FormatModifierRow(Row(StatModifierTarget.OutgoingAuraPower, 0.5f), TestGameplayTags.BuiltIn));
    }

    [TestMethod]
    public void FormatModifierRow_SeventyFivePercentReduction_ReadsAsSeventyFivePercent()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, Magnitude: -0.75f, ConditionTag: GameTags.DamagePoison, RemainingSeconds: null);

        Assert.AreEqual("75% Poison Resistance", HealthWindow.FormatModifierRow(row, TestGameplayTags.BuiltIn));
    }

    [TestMethod]
    public void FormatModifierRow_UntaggedIncomingDamageReduction_LabelsSubjectAsDamage()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, Magnitude: -0.3f, ConditionTag: Engine.Tags.GameplayTag.None, RemainingSeconds: null);

        Assert.AreEqual("30% Damage Resistance", HealthWindow.FormatModifierRow(row, TestGameplayTags.BuiltIn));
    }

    [TestMethod]
    public void FormatModifierRow_PositiveIncomingDamageMultiplier_ReadsAsVulnerability()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff, Magnitude: 0.25f, ConditionTag: GameTags.DamageFire, RemainingSeconds: null);

        Assert.AreEqual("25% Fire Vulnerability", HealthWindow.FormatModifierRow(row, TestGameplayTags.BuiltIn));
    }

    [TestMethod]
    public void FormatModifierRow_DurationUnderSixtySeconds_StaysInSeconds()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, Magnitude: -0.5f, ConditionTag: GameTags.DamagePoison, RemainingSeconds: 45);

        Assert.AreEqual("50% Poison Resistance: 45s", HealthWindow.FormatModifierRow(row, TestGameplayTags.BuiltIn));
    }

    [TestMethod]
    public void FormatModifierRow_DurationExactlySixtySeconds_SwitchesToMinutes()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, Magnitude: -0.5f, ConditionTag: GameTags.DamagePoison, RemainingSeconds: 60);

        Assert.AreEqual("50% Poison Resistance: 1min", HealthWindow.FormatModifierRow(row, TestGameplayTags.BuiltIn));
    }

    [TestMethod]
    public void FormatModifierRow_NoSpecialCaseForTarget_UsesGenericFallback()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.CritChance, StatModifierOperation.Additive, StatModifierPolarity.Buff, Magnitude: 5f, ConditionTag: Engine.Tags.GameplayTag.None, RemainingSeconds: 120);

        Assert.AreEqual("+5 CritChance: 2min", HealthWindow.FormatModifierRow(row, TestGameplayTags.BuiltIn));
    }

    [TestMethod]
    public void FormatModifierRow_AdditiveOutgoingDamageBuff_ReadsAsPlusDamage()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.OutgoingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff, Magnitude: 2f, ConditionTag: Engine.Tags.GameplayTag.None, RemainingSeconds: null);

        Assert.AreEqual("+2 Damage", HealthWindow.FormatModifierRow(row, TestGameplayTags.BuiltIn));
    }

    [TestMethod]
    public void FormatModifierRow_AdditiveOutgoingDamageDebuff_ReadsAsMinusDamage()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.OutgoingDamage, StatModifierOperation.Additive, StatModifierPolarity.Debuff, Magnitude: -1f, ConditionTag: Engine.Tags.GameplayTag.None, RemainingSeconds: null);

        Assert.AreEqual("-1 Damage", HealthWindow.FormatModifierRow(row, TestGameplayTags.BuiltIn));
    }

    /// <summary>Matches BodyPartEffectsSystem's own real grant shape (Multiplicative, Debuff, ConditionTag: GameTags.DeliveryMelee) -- the actual in-game "OutgoingDamage debuff" that used to fall through to the generic "÷0.5 OutgoingDamage" form instead of reading as "Melee Damage" like the Additive buff's own "Damage" wording, with its ConditionTag included.</summary>
    [TestMethod]
    public void FormatModifierRow_MultiplicativeOutgoingDamageDebuff_ReadsAsMinusPercentMeleeDamage()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.OutgoingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff, Magnitude: -0.5f, ConditionTag: GameTags.DeliveryMelee, RemainingSeconds: null);

        Assert.AreEqual("-50% Melee Damage", HealthWindow.FormatModifierRow(row, TestGameplayTags.BuiltIn));
    }

    [TestMethod]
    public void FormatModifierRow_MultiplicativeOutgoingDamageBuff_ReadsAsPlusPercentDamage()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.OutgoingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, Magnitude: 0.25f, ConditionTag: Engine.Tags.GameplayTag.None, RemainingSeconds: null);

        Assert.AreEqual("+25% Damage", HealthWindow.FormatModifierRow(row, TestGameplayTags.BuiltIn));
    }

    /// <summary>Exact example from the request: BodyPartEffectsSystem's own fully-disabled-Arms/Hands case (combinedMultiplier 0 -> magnitude -1, a full -100% melee damage debuff), ConditionTag: GameTags.DeliveryMelee.</summary>
    [TestMethod]
    public void FormatModifierRow_FullyDisabledMeleeDamage_ReadsAsMinusOneHundredPercentMeleeDamage()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.OutgoingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff, Magnitude: -1f, ConditionTag: GameTags.DeliveryMelee, RemainingSeconds: null);

        Assert.AreEqual("-100% Melee Damage", HealthWindow.FormatModifierRow(row, TestGameplayTags.BuiltIn));
    }

    [TestMethod]
    public void FormatModifierRow_AdditiveOutgoingDamageWithConditionTag_IncludesTag()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.OutgoingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff, Magnitude: 2f, ConditionTag: GameTags.DeliveryMelee, RemainingSeconds: null);

        Assert.AreEqual("+2 Melee Damage", HealthWindow.FormatModifierRow(row, TestGameplayTags.BuiltIn));
    }

    [TestMethod]
    public void FormatModifierRow_MaximumHealthWithConditionTag_IncludesTag()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.MaximumHealth, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, Magnitude: 0.5f, ConditionTag: GameTags.DamageFire, RemainingSeconds: null);

        Assert.AreEqual("+50% Fire Health", HealthWindow.FormatModifierRow(row, TestGameplayTags.BuiltIn));
    }

    [TestMethod]
    public void FormatModifierRow_MovementPenaltyWithConditionTag_IncludesTag()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.MovementLockFrames, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff, Magnitude: 10f, ConditionTag: GameTags.DeliveryMelee, RemainingSeconds: null);

        Assert.AreEqual("x10 Melee Movement Penalty", HealthWindow.FormatModifierRow(row, TestGameplayTags.BuiltIn));
    }

    [TestMethod]
    public void FormatModifierRow_GenericFallbackWithConditionTag_IncludesTag()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.CritChance, StatModifierOperation.Additive, StatModifierPolarity.Buff, Magnitude: 5f, ConditionTag: GameTags.DeliveryMelee, RemainingSeconds: null);

        Assert.AreEqual("+5 Melee CritChance", HealthWindow.FormatModifierRow(row, TestGameplayTags.BuiltIn));
    }

    [TestMethod]
    public void FormatModifierRow_NoConditionTag_NoTagPrefix()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.CritChance, StatModifierOperation.Additive, StatModifierPolarity.Buff, Magnitude: 5f, ConditionTag: Engine.Tags.GameplayTag.None, RemainingSeconds: null);

        Assert.AreEqual("+5 CritChance", HealthWindow.FormatModifierRow(row, TestGameplayTags.BuiltIn));
    }

    [TestMethod]
    public void FormatModifierRow_MultiplicativeMaximumHealthBuff_ReadsAsPlusPercentHealth()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.MaximumHealth, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, Magnitude: 0.5f, ConditionTag: Engine.Tags.GameplayTag.None, RemainingSeconds: null);

        Assert.AreEqual("+50% Health", HealthWindow.FormatModifierRow(row, TestGameplayTags.BuiltIn));
    }

    [TestMethod]
    public void FormatModifierRow_MultiplicativeMaximumHealthDebuff_ReadsAsMinusPercentHealth()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.MaximumHealth, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff, Magnitude: -0.25f, ConditionTag: Engine.Tags.GameplayTag.None, RemainingSeconds: null);

        Assert.AreEqual("-25% Health", HealthWindow.FormatModifierRow(row, TestGameplayTags.BuiltIn));
    }

    /// <summary>Matches BodyPartEffectsSystem's own real grant shape (Multiplicative, Debuff).</summary>
    [TestMethod]
    public void FormatModifierRow_MultiplicativeMovementLockFrames_ReadsAsMovementPenaltyWithLiteralMultiplier()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.MovementLockFrames, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff, Magnitude: 10f, ConditionTag: Engine.Tags.GameplayTag.None, RemainingSeconds: null);

        Assert.AreEqual("x10 Movement Penalty", HealthWindow.FormatModifierRow(row, TestGameplayTags.BuiltIn));
    }

    [TestMethod]
    public void FormatModifierRow_AdditiveMovementLockFrames_UsesSignedMagnitudeNotMultiplier()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.MovementLockFrames, StatModifierOperation.Additive, StatModifierPolarity.Debuff, Magnitude: 5f, ConditionTag: Engine.Tags.GameplayTag.None, RemainingSeconds: null);

        Assert.AreEqual("+5 Movement Penalty", HealthWindow.FormatModifierRow(row, TestGameplayTags.BuiltIn));
    }

    [TestMethod]
    public void FormatModifierRow_FlatMagnitudeWithManyDecimals_RoundsToOneDecimalPlace()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.OutgoingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff, Magnitude: 2.3333333f, ConditionTag: Engine.Tags.GameplayTag.None, RemainingSeconds: null);

        Assert.AreEqual("+2.3 Damage", HealthWindow.FormatModifierRow(row, TestGameplayTags.BuiltIn));
    }

    [TestMethod]
    public void FormatModifierRow_FlatMagnitudeIsWholeNumber_NoTrailingDecimal()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.OutgoingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff, Magnitude: 2f, ConditionTag: Engine.Tags.GameplayTag.None, RemainingSeconds: null);

        Assert.AreEqual("+2 Damage", HealthWindow.FormatModifierRow(row, TestGameplayTags.BuiltIn));
    }

    [TestMethod]
    public void FormatModifierRow_GenericFallbackMagnitudeWithDecimals_RoundsToOneDecimalPlace()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.CritChance, StatModifierOperation.Additive, StatModifierPolarity.Buff, Magnitude: 0.16666667f, ConditionTag: Engine.Tags.GameplayTag.None, RemainingSeconds: null);

        Assert.AreEqual("+0.2 CritChance", HealthWindow.FormatModifierRow(row, TestGameplayTags.BuiltIn));
    }

    [TestMethod]
    public void FormatModifierRow_MovementPenaltyMagnitudeWithDecimals_RoundsToOneDecimalPlace()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.MovementLockFrames, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff, Magnitude: 2.449f, ConditionTag: Engine.Tags.GameplayTag.None, RemainingSeconds: null);

        Assert.AreEqual("x2.4 Movement Penalty", HealthWindow.FormatModifierRow(row, TestGameplayTags.BuiltIn));
    }

    private static List<StatusEffectImmunityComponent> CreateImmunities() => [];

    [TestMethod]
    public void BuildImmunityRows_NoActiveImmunities_Empty()
    {
        var immunities = CreateImmunities();
        List<HealthWindow.ImmunityRow> rows = [];

        HealthWindow.BuildImmunityRows(rows, immunities, now: 0);

        Assert.IsEmpty(rows);
    }

    /// <summary>Matches ImmunityTestPotion's own real grant (Game/Modules/Inventory/Definitions/ImmunityTestPotion.cs) -- Burning + Poison, 10-minute duration each.</summary>
    [TestMethod]
    public void BuildImmunityRows_TwoActiveImmunities_OneRowEach()
    {
        var immunities = CreateImmunities();
        immunities.Add(new StatusEffectImmunityComponent(StatusEffectType.Burning, expiresAtFrame: 36_000));
        immunities.Add(new StatusEffectImmunityComponent(StatusEffectType.Poison, expiresAtFrame: 36_000));

        List<HealthWindow.ImmunityRow> rows = [];
        HealthWindow.BuildImmunityRows(rows, immunities, now: 0);

        Assert.HasCount(2, rows);
    }

    [TestMethod]
    public void BuildImmunityRows_PermanentImmunity_RemainingSecondsIsNull()
    {
        var immunities = CreateImmunities();
        immunities.Add(new StatusEffectImmunityComponent(StatusEffectType.Paralysis, expiresAtFrame: FrameDeadline.Never));

        List<HealthWindow.ImmunityRow> rows = [];
        HealthWindow.BuildImmunityRows(rows, immunities, now: 0);

        Assert.HasCount(1, rows);
        Assert.IsNull(rows[0].RemainingSeconds);
    }

    [TestMethod]
    public void FormatImmunityRow_BurningImmunity_ReadsAsFireImmunity()
    {
        // 600 frames * 60 (10 minutes worth of ticks at 60fps) -- 36000 frames = 600s = 10min.
        var row = new HealthWindow.ImmunityRow(StatusEffectType.Burning, RemainingSeconds: 600);

        Assert.AreEqual("Fire Immunity: 10min", HealthWindow.FormatImmunityRow(row));
    }

    [TestMethod]
    public void FormatImmunityRow_PoisonImmunity_ReadsAsPoisonImmunity()
    {
        var row = new HealthWindow.ImmunityRow(StatusEffectType.Poison, RemainingSeconds: 600);

        Assert.AreEqual("Poison Immunity: 10min", HealthWindow.FormatImmunityRow(row));
    }

    [TestMethod]
    public void FormatImmunityRow_Permanent_OmitsDurationSuffix()
    {
        var row = new HealthWindow.ImmunityRow(StatusEffectType.Paralysis, RemainingSeconds: null);

        Assert.AreEqual("Paralysis Immunity", HealthWindow.FormatImmunityRow(row));
    }
}
