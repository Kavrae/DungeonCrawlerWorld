using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
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

    private static PackedComponentPool<SimpleHealthComponent> CreateHealthPool() =>
        new(maximumEntityCount: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    private static MultiComponentPool<BodyPartComponent> CreateBodyPartsPool() =>
        new(maximumEntityCount: 10, initialCapacity: 8);

    private static MultiComponentPool<StatModifierComponent> CreateMaximumHealthBuffPool(float magnitude)
    {
        var statModifiers = new MultiComponentPool<StatModifierComponent>(maximumEntityCount: 10, initialCapacity: 4);
        statModifiers.Add(EntityId, new StatModifierComponent(StatModifierTarget.MaximumHealth, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: true, magnitude: magnitude, remainingDurationFrames: null, StatusEffectSource.Admin));
        return statModifiers;
    }

    [TestMethod]
    public void BuildBodyPartRows_ComplexFixture_OneRowPerBodyPart()
    {
        var healthPool = CreateHealthPool();
        var bodyParts = CreateBodyPartsPool();
        bodyParts.Add(EntityId, new BodyPartComponent("Head", BodyPartType.Head, 0, verticalPosition: 0, currentHealth: 10, maximumHealth: 10, isVital: true));
        bodyParts.Add(EntityId, new BodyPartComponent("Torso", BodyPartType.Torso, 0, verticalPosition: 0, currentHealth: 15, maximumHealth: 20, isVital: true));
        bodyParts.Add(EntityId, new BodyPartComponent("Left Arm", BodyPartType.Arm, 0, verticalPosition: 0, currentHealth: 8, maximumHealth: 8, isVital: false));
        bodyParts.Add(EntityId, new BodyPartComponent("Right Arm", BodyPartType.Arm, 0, verticalPosition: 0, currentHealth: 8, maximumHealth: 8, isVital: false));
        bodyParts.Add(EntityId, new BodyPartComponent("Left Leg", BodyPartType.Leg, 0, verticalPosition: 0, currentHealth: 4, maximumHealth: 9, isVital: false));
        bodyParts.Add(EntityId, new BodyPartComponent("Right Leg", BodyPartType.Leg, 0, verticalPosition: 0, currentHealth: 9, maximumHealth: 9, isVital: false));

        List<HealthWindow.BodyPartRow> rows = [];
        HealthWindow.BuildBodyPartRows(rows, EntityId, healthPool, bodyParts, statModifiers: null);

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
        var healthPool = CreateHealthPool();
        var bodyParts = CreateBodyPartsPool();
        bodyParts.Add(EntityId, new BodyPartComponent("Head", BodyPartType.Head, 0, verticalPosition: 0, currentHealth: 10, maximumHealth: 10, isVital: true));
        var statModifiers = CreateMaximumHealthBuffPool(0.5f);

        List<HealthWindow.BodyPartRow> rows = [];
        HealthWindow.BuildBodyPartRows(rows, EntityId, healthPool, bodyParts, statModifiers);

        Assert.HasCount(1, rows);
        Assert.AreEqual(10f, rows[0].CurrentHealth);
        Assert.AreEqual(15f, rows[0].MaximumHealth, "Raw maximum is 10, but a +50% buff makes the effective maximum 15.");
    }

    [TestMethod]
    public void BuildBodyPartRows_SimpleHealthFixture_OneRowNamedHP()
    {
        var healthPool = CreateHealthPool();
        healthPool.Add(EntityId, new SimpleHealthComponent(currentHealth: 50, maximumHealth: 100));
        var bodyParts = CreateBodyPartsPool();

        List<HealthWindow.BodyPartRow> rows = [];
        HealthWindow.BuildBodyPartRows(rows, EntityId, healthPool, bodyParts, statModifiers: null);

        Assert.HasCount(1, rows);
        Assert.AreEqual("HP", rows[0].Name);
        Assert.AreEqual(50f, rows[0].CurrentHealth);
        Assert.AreEqual(100f, rows[0].MaximumHealth);
    }

    /// <summary>Fresh ComponentManager with every timer component pool registered -- BuildStatusEffectRows reads both presence and duration through TimerBasedStatusEffectDisplay's own GetPackedPool lookup, so the pools have to live behind a real ComponentManager instead of standing alone.</summary>
    private static ComponentManager CreateComponentManagerWithStatusEffectPools()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 8);
        componentManager.RegisterPackedPool<PoisonTimerComponent>(static (ref existing, incoming) => { });
        componentManager.RegisterPackedPool<BurningTimerComponent>(static (ref existing, incoming) => { });
        componentManager.RegisterPackedPool<ParalysisTimerComponent>(static (ref existing, incoming) => { });
        return componentManager;
    }

    /// <summary>Mirrors PoisonModule/BurningModule/ParalysisModule's own real Configure registrations -- same formulas, just assembled directly instead of via GameBootstrapper.</summary>
    private static StatusEffectDisplayRegistry CreateStatusEffectDisplayRegistry()
    {
        var registry = new StatusEffectDisplayRegistry();
        registry.Register(new TimerBasedStatusEffectDisplay<PoisonTimerComponent>(StatusEffectType.Poison, PoisonEffects.Glyph,
            poison => poison.FramesUntilNextTick + (poison.RemainingDurationTicks - 1) * PoisonEffects.TickIntervalFrames));
        registry.Register(new TimerBasedStatusEffectDisplay<BurningTimerComponent>(StatusEffectType.Burning, BurningEffects.Glyph,
            burning => burning.FramesUntilNextTick + (burning.StackCount - 1) * BurningEffects.TickIntervalFrames));
        registry.Register(new TimerBasedStatusEffectDisplay<ParalysisTimerComponent>(StatusEffectType.Paralysis, ParalysisEffects.Glyph,
            paralysis => paralysis.FramesUntilNextTick));
        return registry;
    }

    [TestMethod]
    public void BuildStatusEffectRows_NoActiveEffects_Empty()
    {
        var componentManager = CreateComponentManagerWithStatusEffectPools();
        List<HealthWindow.StatusEffectRow> rows = [];
        List<StatusEffectType> scratch = [];

        HealthWindow.BuildStatusEffectRows(rows, scratch, EntityId, CreateStatusEffectDisplayRegistry(), componentManager);

        Assert.IsEmpty(rows);
    }

    [TestMethod]
    public void BuildStatusEffectRows_PoisonActive_RemainingSecondsMatchesTimerFormula()
    {
        var componentManager = CreateComponentManagerWithStatusEffectPools();
        // FramesUntilNextTick 30 + (RemainingDurationTicks 3 - 1) * TickIntervalFrames 60 = 150 frames = 2.5s -> ceil to 3.
        componentManager.GetPackedPool<PoisonTimerComponent>().Add(EntityId, new PoisonTimerComponent(framesUntilNextTick: 30, stackCount: 1, remainingDurationTicks: 3, StatusEffectSource.Admin));

        List<HealthWindow.StatusEffectRow> rows = [];
        List<StatusEffectType> scratch = [];
        HealthWindow.BuildStatusEffectRows(rows, scratch, EntityId, CreateStatusEffectDisplayRegistry(), componentManager);

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
        componentManager.GetPackedPool<BurningTimerComponent>().Add(EntityId, new BurningTimerComponent(framesUntilNextTick: 45, stackCount: 2, StatusEffectSource.Admin));

        List<HealthWindow.StatusEffectRow> rows = [];
        List<StatusEffectType> scratch = [];
        HealthWindow.BuildStatusEffectRows(rows, scratch, EntityId, CreateStatusEffectDisplayRegistry(), componentManager);

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
        componentManager.GetPackedPool<ParalysisTimerComponent>().Add(EntityId, new ParalysisTimerComponent(framesUntilNextTick: 61));

        List<HealthWindow.StatusEffectRow> rows = [];
        List<StatusEffectType> scratch = [];
        HealthWindow.BuildStatusEffectRows(rows, scratch, EntityId, CreateStatusEffectDisplayRegistry(), componentManager);

        Assert.HasCount(1, rows);
        Assert.AreEqual(StatusEffectType.Paralysis, rows[0].Type);
        Assert.AreEqual(2, rows[0].RemainingSeconds);
        Assert.AreEqual(1, rows[0].StackCount);
    }

    [TestMethod]
    public void FormatStatusEffectRow_BurningWithMultipleStacks_ShowsStackCountNoDuration()
    {
        var row = new HealthWindow.StatusEffectRow(StatusEffectType.Burning, RemainingSeconds: 5, StackCount: 5);

        Assert.AreEqual($"{BurningEffects.Glyph} Burning x5", HealthWindow.FormatStatusEffectRow(row, CreateStatusEffectDisplayRegistry()));
    }

    [TestMethod]
    public void FormatStatusEffectRow_BurningWithOneStack_OmitsStackCountAndDuration()
    {
        var row = new HealthWindow.StatusEffectRow(StatusEffectType.Burning, RemainingSeconds: 5, StackCount: 1);

        Assert.AreEqual($"{BurningEffects.Glyph} Burning", HealthWindow.FormatStatusEffectRow(row, CreateStatusEffectDisplayRegistry()));
    }

    [TestMethod]
    public void FormatStatusEffectRow_PoisonWithMultipleStacksAndDuration_ShowsParenthesizedStackCountThenDuration()
    {
        var row = new HealthWindow.StatusEffectRow(StatusEffectType.Poison, RemainingSeconds: 18, StackCount: 21);

        Assert.AreEqual($"{PoisonEffects.Glyph} Poison (x21): 18s", HealthWindow.FormatStatusEffectRow(row, CreateStatusEffectDisplayRegistry()));
    }

    [TestMethod]
    public void FormatStatusEffectRow_PoisonWithOneStack_OmitsStackCount()
    {
        var row = new HealthWindow.StatusEffectRow(StatusEffectType.Poison, RemainingSeconds: 18, StackCount: 1);

        Assert.AreEqual($"{PoisonEffects.Glyph} Poison: 18s", HealthWindow.FormatStatusEffectRow(row, CreateStatusEffectDisplayRegistry()));
    }

    /// <summary>Paralysis's own StackCount is always exactly 1 (never a stacking effect -- see ParalysisTimerComponent), so it never enters the stack-count-shown branch at all.</summary>
    [TestMethod]
    public void FormatStatusEffectRow_Paralysis_NeverShowsStackCount()
    {
        var row = new HealthWindow.StatusEffectRow(StatusEffectType.Paralysis, RemainingSeconds: 2, StackCount: 1);

        Assert.AreEqual($"{ParalysisEffects.Glyph} Paralysis: 2s", HealthWindow.FormatStatusEffectRow(row, CreateStatusEffectDisplayRegistry()));
    }

    [TestMethod]
    public void TryGetBodyPartBurningLine_PartHasActiveBodyPartScopedBurn_ReturnsFormattedLine()
    {
        var bodyPartBurningTimers = new MultiComponentPool<BodyPartBurningTimerComponent>(maximumEntityCount: 10, initialCapacity: 4);
        // FramesUntilNextTick 45 + (StackCount 2 - 1) * TickIntervalFrames 60 = 105 frames = 1.75s -> ceil to 2 -- same formula the entity-scoped BurningTimerComponent display uses.
        bodyPartBurningTimers.Add(EntityId, new BodyPartBurningTimerComponent(partId: 1, stackCount: 2, framesUntilNextTick: 45, StatusEffectSource.Admin));

        var found = HealthWindow.TryGetBodyPartBurningLine(bodyPartBurningTimers, EntityId, partId: 1, out var text, out _);

        Assert.IsTrue(found);
        Assert.Contains("2s", text);
        Assert.Contains(BurningEffects.Glyph, text);
    }

    [TestMethod]
    public void TryGetBodyPartBurningLine_DifferentPartOnFire_ThisPartReturnsFalse()
    {
        var bodyPartBurningTimers = new MultiComponentPool<BodyPartBurningTimerComponent>(maximumEntityCount: 10, initialCapacity: 4);
        bodyPartBurningTimers.Add(EntityId, new BodyPartBurningTimerComponent(partId: 1, stackCount: 2, framesUntilNextTick: 45, StatusEffectSource.Admin));

        var found = HealthWindow.TryGetBodyPartBurningLine(bodyPartBurningTimers, EntityId, partId: 0, out var text, out _);

        Assert.IsFalse(found);
        Assert.AreEqual(string.Empty, text);
    }

    [TestMethod]
    public void TryGetBodyPartBurningLine_NoPoolSupplied_ReturnsFalse()
    {
        var found = HealthWindow.TryGetBodyPartBurningLine(null, EntityId, partId: 0, out _, out _);

        Assert.IsFalse(found);
    }

    private static PackedComponentPool<PotionCooldownComponent> CreatePotionCooldownPool() =>
        new(maximumEntityCount: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    private static ItemCatalog CreateItemCatalogWithHealthPotion()
    {
        var itemCatalog = new ItemCatalog();
        itemCatalog.Register(HealthPotion.Build());
        return itemCatalog;
    }

    [TestMethod]
    public void TryGetPotionCooldownLine_NoPoolSupplied_ReturnsFalse()
    {
        var found = HealthWindow.TryGetPotionCooldownLine(null, CreateItemCatalogWithHealthPotion(), EntityId, out _, out _);

        Assert.IsFalse(found);
    }

    [TestMethod]
    public void TryGetPotionCooldownLine_NoActiveCooldown_ReturnsFalse()
    {
        var potionCooldowns = CreatePotionCooldownPool();

        var found = HealthWindow.TryGetPotionCooldownLine(potionCooldowns, CreateItemCatalogWithHealthPotion(), EntityId, out _, out _);

        Assert.IsFalse(found);
    }

    [TestMethod]
    public void TryGetPotionCooldownLine_FramesRemainingZero_ReturnsFalse()
    {
        var potionCooldowns = CreatePotionCooldownPool();
        potionCooldowns.Add(EntityId, new PotionCooldownComponent(totalFrames: 1200, framesRemaining: 0));

        var found = HealthWindow.TryGetPotionCooldownLine(potionCooldowns, CreateItemCatalogWithHealthPotion(), EntityId, out _, out _);

        Assert.IsFalse(found);
    }

    [TestMethod]
    public void TryGetPotionCooldownLine_ActiveCooldown_ReturnsFormattedLine()
    {
        var potionCooldowns = CreatePotionCooldownPool();
        // 121 frames = 2.017s -> ceil to 3, same PotionCooldownEffects.RemainingSeconds rounding PlayerStatusEffectsContent already relies on.
        potionCooldowns.Add(EntityId, new PotionCooldownComponent(totalFrames: 1200, framesRemaining: 121));

        var found = HealthWindow.TryGetPotionCooldownLine(potionCooldowns, CreateItemCatalogWithHealthPotion(), EntityId, out var text, out var color);

        Assert.IsTrue(found);
        Assert.AreEqual("h Potion Cooldown: 3s", text);
        Assert.AreEqual(Color.Green, color);
    }

    [TestMethod]
    public void TryGetPotionCooldownLine_HealthPotionNotInCatalog_UsesFallbackGlyphAndColor()
    {
        var potionCooldowns = CreatePotionCooldownPool();
        potionCooldowns.Add(EntityId, new PotionCooldownComponent(totalFrames: 1200, framesRemaining: 60));

        var found = HealthWindow.TryGetPotionCooldownLine(potionCooldowns, new ItemCatalog(), EntityId, out var text, out var color);

        Assert.IsTrue(found);
        Assert.AreEqual("? Potion Cooldown: 1s", text);
        Assert.AreEqual(Color.White, color);
    }

    private static MultiComponentPool<StatModifierComponent> CreateStatModifiersPool() =>
        new(maximumEntityCount: 10, initialCapacity: 4);

    [TestMethod]
    public void BuildModifierRows_NoPoolSupplied_Empty()
    {
        List<HealthWindow.ModifierRow> rows = [];

        HealthWindow.BuildModifierRows(rows, EntityId, statModifiers: null, StatModifierPolarity.Buff);

        Assert.IsEmpty(rows);
    }

    [TestMethod]
    public void BuildModifierRows_NoActiveModifiers_Empty()
    {
        var statModifiers = CreateStatModifiersPool();
        List<HealthWindow.ModifierRow> rows = [];

        HealthWindow.BuildModifierRows(rows, EntityId, statModifiers, StatModifierPolarity.Buff);

        Assert.IsEmpty(rows);
    }

    [TestMethod]
    public void BuildModifierRows_TimedBuffActive_RemainingSecondsMatchesFrames()
    {
        var statModifiers = CreateStatModifiersPool();
        // 121 frames = 2.017s -> ceil to 3.
        statModifiers.Add(EntityId, new StatModifierComponent(StatModifierTarget.MaximumHealth, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: true, magnitude: 0.5f, remainingDurationFrames: 121, StatusEffectSource.Admin));

        List<HealthWindow.ModifierRow> rows = [];
        HealthWindow.BuildModifierRows(rows, EntityId, statModifiers, StatModifierPolarity.Buff);

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
        var statModifiers = CreateStatModifiersPool();
        statModifiers.Add(EntityId, new StatModifierComponent(StatModifierTarget.MovementLockFrames, StatModifierOperation.Additive, StatModifierPolarity.Debuff,
            canModify: true, magnitude: 10f, remainingDurationFrames: null, StatusEffectSource.Admin));

        List<HealthWindow.ModifierRow> rows = [];
        HealthWindow.BuildModifierRows(rows, EntityId, statModifiers, StatModifierPolarity.Debuff);

        Assert.HasCount(1, rows);
        Assert.AreEqual(StatModifierPolarity.Debuff, rows[0].Polarity);
        Assert.IsNull(rows[0].RemainingSeconds);
    }

    [TestMethod]
    public void BuildModifierRows_TwoBuffsOnSameTarget_BothListedAsSeparateRows()
    {
        var statModifiers = CreateStatModifiersPool();
        statModifiers.Add(EntityId, new StatModifierComponent(StatModifierTarget.OutgoingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff,
            canModify: true, magnitude: 5f, remainingDurationFrames: null, StatusEffectSource.Admin));
        statModifiers.Add(EntityId, new StatModifierComponent(StatModifierTarget.OutgoingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff,
            canModify: true, magnitude: 2f, remainingDurationFrames: null, StatusEffectSource.Admin));

        List<HealthWindow.ModifierRow> rows = [];
        HealthWindow.BuildModifierRows(rows, EntityId, statModifiers, StatModifierPolarity.Buff);

        Assert.HasCount(2, rows);
    }

    [TestMethod]
    public void BuildModifierRows_WrongPolarity_Excluded()
    {
        var statModifiers = CreateStatModifiersPool();
        statModifiers.Add(EntityId, new StatModifierComponent(StatModifierTarget.OutgoingDamage, StatModifierOperation.Additive, StatModifierPolarity.Debuff,
            canModify: true, magnitude: 2f, remainingDurationFrames: null, StatusEffectSource.Admin));

        List<HealthWindow.ModifierRow> rows = [];
        HealthWindow.BuildModifierRows(rows, EntityId, statModifiers, StatModifierPolarity.Buff);

        Assert.IsEmpty(rows);
    }

    [TestMethod]
    public void BuildModifierRows_AbilityScoreTarget_Excluded()
    {
        // Strength/Intelligence/etc. modifiers are AbilityScoreWindow's own territory (see
        // AbilityScoreModifierFormatter) -- HealthWindow must not duplicate them.
        var statModifiers = CreateStatModifiersPool();
        statModifiers.Add(EntityId, new StatModifierComponent(StatModifierTarget.Strength, StatModifierOperation.Additive, StatModifierPolarity.Buff,
            canModify: true, magnitude: 3f, remainingDurationFrames: null, StatusEffectSource.Admin));

        List<HealthWindow.ModifierRow> rows = [];
        HealthWindow.BuildModifierRows(rows, EntityId, statModifiers, StatModifierPolarity.Buff);

        Assert.IsEmpty(rows);
    }

    [TestMethod]
    public void BuildModifierRows_ConditionTagPresent_CarriedOntoTheRow()
    {
        var statModifiers = CreateStatModifiersPool();
        statModifiers.Add(EntityId, new StatModifierComponent(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: false, magnitude: -0.5f, remainingDurationFrames: null, StatusEffectSource.Admin, conditionTag: Tag.Poison));

        List<HealthWindow.ModifierRow> rows = [];
        HealthWindow.BuildModifierRows(rows, EntityId, statModifiers, StatModifierPolarity.Buff);

        Assert.HasCount(1, rows);
        Assert.AreEqual(Tag.Poison, rows[0].ConditionTag);
    }

    /// <summary>Matches ResistanceTestPotion's own real grant (Game/Modules/Inventory/Definitions/ResistanceTestPotion.cs) -- Multiplicative IncomingDamage, ConditionTag: Tag.Poison, Magnitude -0.5, 10-minute duration.</summary>
    [TestMethod]
    public void FormatModifierRow_TaggedIncomingDamageReduction_ReadsAsNamedResistance()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, Magnitude: -0.5f, ConditionTag: Tag.Poison, RemainingSeconds: 600);

        Assert.AreEqual("50% Poison Resistance: 10min", HealthWindow.FormatModifierRow(row));
    }

    [TestMethod]
    public void FormatModifierRow_SeventyFivePercentReduction_ReadsAsSeventyFivePercent()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, Magnitude: -0.75f, ConditionTag: Tag.Poison, RemainingSeconds: null);

        Assert.AreEqual("75% Poison Resistance", HealthWindow.FormatModifierRow(row));
    }

    [TestMethod]
    public void FormatModifierRow_UntaggedIncomingDamageReduction_LabelsSubjectAsDamage()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, Magnitude: -0.3f, ConditionTag: null, RemainingSeconds: null);

        Assert.AreEqual("30% Damage Resistance", HealthWindow.FormatModifierRow(row));
    }

    [TestMethod]
    public void FormatModifierRow_PositiveIncomingDamageMultiplier_ReadsAsVulnerability()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff, Magnitude: 0.25f, ConditionTag: Tag.Fire, RemainingSeconds: null);

        Assert.AreEqual("25% Fire Vulnerability", HealthWindow.FormatModifierRow(row));
    }

    [TestMethod]
    public void FormatModifierRow_DurationUnderSixtySeconds_StaysInSeconds()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, Magnitude: -0.5f, ConditionTag: Tag.Poison, RemainingSeconds: 45);

        Assert.AreEqual("50% Poison Resistance: 45s", HealthWindow.FormatModifierRow(row));
    }

    [TestMethod]
    public void FormatModifierRow_DurationExactlySixtySeconds_SwitchesToMinutes()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, Magnitude: -0.5f, ConditionTag: Tag.Poison, RemainingSeconds: 60);

        Assert.AreEqual("50% Poison Resistance: 1min", HealthWindow.FormatModifierRow(row));
    }

    [TestMethod]
    public void FormatModifierRow_NoSpecialCaseForTarget_UsesGenericFallback()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.CritChance, StatModifierOperation.Additive, StatModifierPolarity.Buff, Magnitude: 5f, ConditionTag: null, RemainingSeconds: 120);

        Assert.AreEqual("+5 CritChance: 2min", HealthWindow.FormatModifierRow(row));
    }

    [TestMethod]
    public void FormatModifierRow_AdditiveOutgoingDamageBuff_ReadsAsPlusDamage()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.OutgoingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff, Magnitude: 2f, ConditionTag: null, RemainingSeconds: null);

        Assert.AreEqual("+2 Damage", HealthWindow.FormatModifierRow(row));
    }

    [TestMethod]
    public void FormatModifierRow_AdditiveOutgoingDamageDebuff_ReadsAsMinusDamage()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.OutgoingDamage, StatModifierOperation.Additive, StatModifierPolarity.Debuff, Magnitude: -1f, ConditionTag: null, RemainingSeconds: null);

        Assert.AreEqual("-1 Damage", HealthWindow.FormatModifierRow(row));
    }

    /// <summary>Matches BodyPartEffectsSystem's own real grant shape (Multiplicative, Debuff, ConditionTag: Tag.Melee) -- the actual in-game "OutgoingDamage debuff" that used to fall through to the generic "÷0.5 OutgoingDamage" form instead of reading as "Melee Damage" like the Additive buff's own "Damage" wording, with its ConditionTag included.</summary>
    [TestMethod]
    public void FormatModifierRow_MultiplicativeOutgoingDamageDebuff_ReadsAsMinusPercentMeleeDamage()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.OutgoingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff, Magnitude: -0.5f, ConditionTag: Tag.Melee, RemainingSeconds: null);

        Assert.AreEqual("-50% Melee Damage", HealthWindow.FormatModifierRow(row));
    }

    [TestMethod]
    public void FormatModifierRow_MultiplicativeOutgoingDamageBuff_ReadsAsPlusPercentDamage()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.OutgoingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, Magnitude: 0.25f, ConditionTag: null, RemainingSeconds: null);

        Assert.AreEqual("+25% Damage", HealthWindow.FormatModifierRow(row));
    }

    /// <summary>Exact example from the request: BodyPartEffectsSystem's own fully-disabled-Arms/Hands case (combinedMultiplier 0 -> magnitude -1, a full -100% melee damage debuff), ConditionTag: Tag.Melee.</summary>
    [TestMethod]
    public void FormatModifierRow_FullyDisabledMeleeDamage_ReadsAsMinusOneHundredPercentMeleeDamage()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.OutgoingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff, Magnitude: -1f, ConditionTag: Tag.Melee, RemainingSeconds: null);

        Assert.AreEqual("-100% Melee Damage", HealthWindow.FormatModifierRow(row));
    }

    [TestMethod]
    public void FormatModifierRow_AdditiveOutgoingDamageWithConditionTag_IncludesTag()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.OutgoingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff, Magnitude: 2f, ConditionTag: Tag.Melee, RemainingSeconds: null);

        Assert.AreEqual("+2 Melee Damage", HealthWindow.FormatModifierRow(row));
    }

    [TestMethod]
    public void FormatModifierRow_MaximumHealthWithConditionTag_IncludesTag()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.MaximumHealth, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, Magnitude: 0.5f, ConditionTag: Tag.Fire, RemainingSeconds: null);

        Assert.AreEqual("+50% Fire Health", HealthWindow.FormatModifierRow(row));
    }

    [TestMethod]
    public void FormatModifierRow_MovementPenaltyWithConditionTag_IncludesTag()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.MovementLockFrames, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff, Magnitude: 10f, ConditionTag: Tag.Melee, RemainingSeconds: null);

        Assert.AreEqual("x10 Melee Movement Penalty", HealthWindow.FormatModifierRow(row));
    }

    [TestMethod]
    public void FormatModifierRow_GenericFallbackWithConditionTag_IncludesTag()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.CritChance, StatModifierOperation.Additive, StatModifierPolarity.Buff, Magnitude: 5f, ConditionTag: Tag.Melee, RemainingSeconds: null);

        Assert.AreEqual("+5 Melee CritChance", HealthWindow.FormatModifierRow(row));
    }

    [TestMethod]
    public void FormatModifierRow_NoConditionTag_NoTagPrefix()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.CritChance, StatModifierOperation.Additive, StatModifierPolarity.Buff, Magnitude: 5f, ConditionTag: null, RemainingSeconds: null);

        Assert.AreEqual("+5 CritChance", HealthWindow.FormatModifierRow(row));
    }

    [TestMethod]
    public void FormatModifierRow_MultiplicativeMaximumHealthBuff_ReadsAsPlusPercentHealth()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.MaximumHealth, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, Magnitude: 0.5f, ConditionTag: null, RemainingSeconds: null);

        Assert.AreEqual("+50% Health", HealthWindow.FormatModifierRow(row));
    }

    [TestMethod]
    public void FormatModifierRow_MultiplicativeMaximumHealthDebuff_ReadsAsMinusPercentHealth()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.MaximumHealth, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff, Magnitude: -0.25f, ConditionTag: null, RemainingSeconds: null);

        Assert.AreEqual("-25% Health", HealthWindow.FormatModifierRow(row));
    }

    /// <summary>Matches BodyPartEffectsSystem's own real grant shape (Multiplicative, Debuff) -- see PLAN-body-part-gameplay-effects.md.</summary>
    [TestMethod]
    public void FormatModifierRow_MultiplicativeMovementLockFrames_ReadsAsMovementPenaltyWithLiteralMultiplier()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.MovementLockFrames, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff, Magnitude: 10f, ConditionTag: null, RemainingSeconds: null);

        Assert.AreEqual("x10 Movement Penalty", HealthWindow.FormatModifierRow(row));
    }

    [TestMethod]
    public void FormatModifierRow_AdditiveMovementLockFrames_UsesSignedMagnitudeNotMultiplier()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.MovementLockFrames, StatModifierOperation.Additive, StatModifierPolarity.Debuff, Magnitude: 5f, ConditionTag: null, RemainingSeconds: null);

        Assert.AreEqual("+5 Movement Penalty", HealthWindow.FormatModifierRow(row));
    }

    [TestMethod]
    public void FormatModifierRow_FlatMagnitudeWithManyDecimals_RoundsToOneDecimalPlace()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.OutgoingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff, Magnitude: 2.3333333f, ConditionTag: null, RemainingSeconds: null);

        Assert.AreEqual("+2.3 Damage", HealthWindow.FormatModifierRow(row));
    }

    [TestMethod]
    public void FormatModifierRow_FlatMagnitudeIsWholeNumber_NoTrailingDecimal()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.OutgoingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff, Magnitude: 2f, ConditionTag: null, RemainingSeconds: null);

        Assert.AreEqual("+2 Damage", HealthWindow.FormatModifierRow(row));
    }

    [TestMethod]
    public void FormatModifierRow_GenericFallbackMagnitudeWithDecimals_RoundsToOneDecimalPlace()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.CritChance, StatModifierOperation.Additive, StatModifierPolarity.Buff, Magnitude: 0.16666667f, ConditionTag: null, RemainingSeconds: null);

        Assert.AreEqual("+0.2 CritChance", HealthWindow.FormatModifierRow(row));
    }

    [TestMethod]
    public void FormatModifierRow_MovementPenaltyMagnitudeWithDecimals_RoundsToOneDecimalPlace()
    {
        var row = new HealthWindow.ModifierRow(StatModifierTarget.MovementLockFrames, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff, Magnitude: 2.449f, ConditionTag: null, RemainingSeconds: null);

        Assert.AreEqual("x2.4 Movement Penalty", HealthWindow.FormatModifierRow(row));
    }

    private static MultiComponentPool<StatusEffectImmunityComponent> CreateImmunitiesPool() =>
        new(maximumEntityCount: 10, initialCapacity: 4);

    [TestMethod]
    public void BuildImmunityRows_NoPoolSupplied_Empty()
    {
        List<HealthWindow.ImmunityRow> rows = [];

        HealthWindow.BuildImmunityRows(rows, EntityId, statusEffectImmunities: null);

        Assert.IsEmpty(rows);
    }

    [TestMethod]
    public void BuildImmunityRows_NoActiveImmunities_Empty()
    {
        var immunities = CreateImmunitiesPool();
        List<HealthWindow.ImmunityRow> rows = [];

        HealthWindow.BuildImmunityRows(rows, EntityId, immunities);

        Assert.IsEmpty(rows);
    }

    /// <summary>Matches ImmunityTestPotion's own real grant (Game/Modules/Inventory/Definitions/ImmunityTestPotion.cs) -- Burning + Poison, 10-minute duration each.</summary>
    [TestMethod]
    public void BuildImmunityRows_TwoActiveImmunities_OneRowEach()
    {
        var immunities = CreateImmunitiesPool();
        immunities.Add(EntityId, new StatusEffectImmunityComponent(StatusEffectType.Burning, remainingDurationFrames: 36_000));
        immunities.Add(EntityId, new StatusEffectImmunityComponent(StatusEffectType.Poison, remainingDurationFrames: 36_000));

        List<HealthWindow.ImmunityRow> rows = [];
        HealthWindow.BuildImmunityRows(rows, EntityId, immunities);

        Assert.HasCount(2, rows);
    }

    [TestMethod]
    public void BuildImmunityRows_PermanentImmunity_RemainingSecondsIsNull()
    {
        var immunities = CreateImmunitiesPool();
        immunities.Add(EntityId, new StatusEffectImmunityComponent(StatusEffectType.Paralysis, remainingDurationFrames: null));

        List<HealthWindow.ImmunityRow> rows = [];
        HealthWindow.BuildImmunityRows(rows, EntityId, immunities);

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
