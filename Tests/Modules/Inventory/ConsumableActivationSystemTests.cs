using Engine.ECS.Components;
using Engine.Events;
using Engine.Math;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Effects;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Inventory.Systems;
using Game.Modules.Mana.Components;
using Game.Modules.Poison;
using Game.Modules.Poison.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.World;
using Microsoft.Xna.Framework;

namespace Tests.Modules.Inventory;

[TestClass]
public sealed class ConsumableActivationSystemTests
{
    private const int CasterEntityId = 1;
    private const int TargetEntityId = 2;
    private static readonly Guid PotionId = Guid.NewGuid();
    private static readonly Guid ManaPotionId = Guid.NewGuid();
    private static readonly Guid HotkeyExpansionPotionId = Guid.NewGuid();
    private static readonly Guid NonConsumableId = Guid.NewGuid();
    private static readonly Guid WandId = Guid.NewGuid();
    private static readonly Vector3Int TargetTile = new(5, 5, 0);

    /// <summary>Catalog placeholder only -- Charges/MaxCharges always come from the specific Override each test's own AddItemWithOverride call constructs, mirroring how WandGrantEffects.Grant never grants the bare catalog entry directly. DirectDamage only (no StatusEffectGrant) -- StatusEffectAppliers isn't wired in this fixture, and these tests are about charge/peel/repoint mechanics, not status effects, which DirectDamage/HealthOf already exercises well enough on its own.</summary>
    private static ItemDefinition CreateWandDefinition(ushort charges, ushort maxCharges) =>
        new(WandId, "Test Wand", null, "w", Color.OrangeRed, Tags: [],
            Effects: [new ActionEffect([new DirectDamage(10, 10)])],
            Activator: new WandActivator(new TargetingSpec(TargetShape.Burst, Range: 3, AreaSize: 1), new ActionTiming(ActionTimingCategory.Immediate, 60, null), charges, maxCharges));

    private sealed class FakeMapQuery : IMapQuery
    {
        private readonly Dictionary<(int, int, int), int> _occupantByPosition = [];

        public Vector3Int MapSize { get; } = new(100, 100, 1);
        public bool IsOnMap(Vector3Int position) => true;
        public bool IsBlocking(int entityId) => true;
        public int GetTerrainEntityIdAt(Vector3Int position) => -1;

        public void SetOccupant(Vector3Int position, int entityId) => _occupantByPosition[(position.X, position.Y, position.Z)] = entityId;

        public int GetEntityIdAt(Vector3Int position) =>
            _occupantByPosition.TryGetValue((position.X, position.Y, position.Z), out var id) ? id : -1;

        public IReadOnlyList<int> GetOccupantEntityIdsAt(Vector3Int position) =>
            GetEntityIdAt(position) is var entityId && entityId != -1 ? [entityId] : [];

        public void GetEntityIdsInBox(CubeInt box, Span<int> entityIds) { }
    }

    private static (ConsumableActivationSystem System, ComponentManager ComponentManager, FakeMapQuery MapQuery, EventBus EventBus) Build()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 10);
        componentManager.RegisterPackedPool<PendingConsumableActivationComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<ActionLockComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<PotionCooldownComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<SimpleHealthComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<DeadComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<ManaComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<InventoryItemStackComponent>();
        componentManager.RegisterPackedPool<InventoryComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<StatusEffectStack>();
        componentManager.RegisterPackedPool<PoisonTimerComponent>(static (ref existing, incoming) => { });
        componentManager.RegisterPackedPool<HotkeyExpansionUnlockComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<ItemHotkeyBindingComponent>();
        componentManager.RegisterMultiPool<BodyPartComponent>();

        var itemCatalog = new ItemCatalog();
        var splashTargeting = new TargetingSpec(TargetShape.Burst, Range: 3, AreaSize: 1);
        itemCatalog.Register(new ItemDefinition(
            PotionId, "Test Potion", null, "p", Color.Green, Tags: [],
            Effects: [new ActionEffect([new DirectHeal(0.5f)])],
            Activator: new PotionActivator(splashTargeting, new ActionTiming(ActionTimingCategory.Immediate, 60, null))));
        itemCatalog.Register(new ItemDefinition(
            ManaPotionId, "Test Mana Potion", null, "m", Color.Blue, Tags: [],
            Effects: [new ActionEffect([new DirectManaRestore(1f)])],
            Activator: new PotionActivator(splashTargeting, new ActionTiming(ActionTimingCategory.Immediate, 60, null))));
        itemCatalog.Register(new ItemDefinition(
            HotkeyExpansionPotionId, "Test Hotkey Expansion Potion", null, "k", Color.Orange, Tags: [],
            Effects: [new ActionEffect([new HotkeyExpansionGrant(5)])],
            Activator: new PotionActivator(new TargetingSpec(TargetShape.Self, Range: 0, AreaSize: 0), new ActionTiming(ActionTimingCategory.Immediate, 60, null))));
        itemCatalog.Register(new ItemDefinition(NonConsumableId, "Test Hammer", null, "h", Color.Gray, Tags: [], Effects: []));
        itemCatalog.Register(CreateWandDefinition(charges: 0, maxCharges: 0)); // Placeholder -- never granted directly, see CreateWandDefinition's own doc comment.

        var mapQuery = new FakeMapQuery();
        var eventBus = new EventBus();
        var mathUtility = new MathUtility();

        var system = new ConsumableActivationSystem(
            componentManager.GetPackedPool<PendingConsumableActivationComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetPackedPool<PotionCooldownComponent>(),
            componentManager.GetPackedPool<SimpleHealthComponent>(),
            itemCatalog,
            new ActionCatalog(),
            mapQuery,
            eventBus,
            mathUtility,
            componentManager,
            statModifiers: null,
            deadEntities: componentManager.GetPackedPool<DeadComponent>(),
            mana: componentManager.GetPackedPool<ManaComponent>(),
            hotkeyExpansionUnlocks: componentManager.GetPackedPool<HotkeyExpansionUnlockComponent>(),
            abilityScores: null,
            statusEffectAppliers: null,
            playerQuery: null,
            auraSources: null,
            itemHotkeyBindings: componentManager.GetMultiPool<ItemHotkeyBindingComponent>(),
            bodyParts: componentManager.GetMultiPool<BodyPartComponent>());

        return (system, componentManager, mapQuery, eventBus);
    }

    /// <summary>Same wiring as Build, plus an AbilityScoreComponent pool -- for tests exercising Constitution's effect on the potion cooldown duration.</summary>
    private static (ConsumableActivationSystem System, ComponentManager ComponentManager, FakeMapQuery MapQuery, EventBus EventBus) BuildWithAbilityScores()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 10);
        componentManager.RegisterPackedPool<PendingConsumableActivationComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<ActionLockComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<PotionCooldownComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<SimpleHealthComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<DeadComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<ManaComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<InventoryItemStackComponent>();
        componentManager.RegisterPackedPool<InventoryComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<StatusEffectStack>();
        componentManager.RegisterPackedPool<PoisonTimerComponent>(static (ref existing, incoming) => { });
        componentManager.RegisterPackedPool<HotkeyExpansionUnlockComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<AbilityScoreComponent>();

        var itemCatalog = new ItemCatalog();
        var splashTargeting = new TargetingSpec(TargetShape.Burst, Range: 3, AreaSize: 1);
        itemCatalog.Register(new ItemDefinition(
            PotionId, "Test Potion", null, "p", Color.Green, Tags: [],
            Effects: [new ActionEffect([new DirectHeal(0.5f)])],
            Activator: new PotionActivator(splashTargeting, new ActionTiming(ActionTimingCategory.Immediate, 60, null))));

        var mapQuery = new FakeMapQuery();
        var eventBus = new EventBus();
        var mathUtility = new MathUtility();

        var system = new ConsumableActivationSystem(
            componentManager.GetPackedPool<PendingConsumableActivationComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetPackedPool<PotionCooldownComponent>(),
            componentManager.GetPackedPool<SimpleHealthComponent>(),
            itemCatalog,
            new ActionCatalog(),
            mapQuery,
            eventBus,
            mathUtility,
            componentManager,
            statModifiers: null,
            componentManager.GetPackedPool<DeadComponent>(),
            componentManager.GetPackedPool<ManaComponent>(),
            componentManager.GetPackedPool<HotkeyExpansionUnlockComponent>(),
            componentManager.GetMultiPool<AbilityScoreComponent>());

        return (system, componentManager, mapQuery, eventBus);
    }

    private static float HealthOf(ComponentManager componentManager, int entityId) =>
        componentManager.GetPackedPool<SimpleHealthComponent>().TryGetReadonly(entityId, out var health) ? health.CurrentHealth : -1f;

    private static float ManaOf(ComponentManager componentManager, int entityId) =>
        componentManager.GetPackedPool<ManaComponent>().TryGetReadonly(entityId, out var mana) ? mana.CurrentMana : -1f;

    private static int StackQuantity(ComponentManager componentManager, int entityId, Guid itemDefinitionId) =>
        InventoryQueries.TryGetStack(componentManager.GetMultiPool<InventoryItemStackComponent>(), entityId, itemDefinitionId, out var stack) ? stack.Quantity : -1;

    /// <summary>Grants one wand via AddItemWithOverride (void -- unlike AddItem/AddDivergentItem, it doesn't return the stack it landed in) and looks the resulting StackInstanceId back up by item id -- safe here since each of these tests grants exactly one wand to a fresh entity, so there's only ever one to find.</summary>
    private static Guid GrantWandAndGetStackInstanceId(ComponentManager componentManager, int entityId, ushort charges, ushort maxCharges)
    {
        InventoryActions.AddItemWithOverride(componentManager, entityId, CreateWandDefinition(charges, maxCharges), quantity: 1);
        Assert.IsTrue(InventoryQueries.TryGetStack(componentManager.GetMultiPool<InventoryItemStackComponent>(), entityId, WandId, out var stack));
        return stack.StackInstanceId;
    }

    [TestMethod]
    public void Potion_TargetOccupantAtTargetTile_HealsByHealFractionOfItsOwnMaxHealth()
    {
        var (system, componentManager, mapQuery, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(currentHealth: 20, maximumHealth: 100));
        var stackInstanceId = InventoryActions.AddItem(componentManager, CasterEntityId, PotionId, quantity: 1);
        componentManager.Merge(CasterEntityId, new PendingConsumableActivationComponent(stackInstanceId, [TargetTile]));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));

        system.Update(default, 0);

        Assert.AreEqual(70, HealthOf(componentManager, TargetEntityId));
    }

    /// <summary>A Complex target (BodyPartComponents, no SimpleHealthComponent) must not be rejected by ApplyPotionToTarget's presence gate -- proves the ConsumableActivationSystem fix in PLAN-human-race.md actually lands the effect instead of silently no-oping.</summary>
    [TestMethod]
    public void Potion_ComplexTargetWithBodyPartsAndNoSimpleHealth_HealsByHealFractionOfItsOwnMaxHealth()
    {
        var (system, componentManager, mapQuery, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.GetMultiPool<BodyPartComponent>().Add(TargetEntityId, new BodyPartComponent("Head", BodyPartType.Head, 0, 0, currentHealth: 40, maximumHealth: 40, isVital: true));
        componentManager.GetMultiPool<BodyPartComponent>().Add(TargetEntityId, new BodyPartComponent("Torso", BodyPartType.Torso, 0, 0, currentHealth: 40, maximumHealth: 160, isVital: true));
        var stackInstanceId = InventoryActions.AddItem(componentManager, CasterEntityId, PotionId, quantity: 1);
        componentManager.Merge(CasterEntityId, new PendingConsumableActivationComponent(stackInstanceId, [TargetTile]));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));

        system.Update(default, 0);

        Assert.IsFalse(componentManager.GetPackedPool<SimpleHealthComponent>().Has(TargetEntityId), "Sanity check: the target is Complex-only, no SimpleHealthComponent at all.");
        var bodyParts = componentManager.GetMultiPool<BodyPartComponent>();
        var totalCurrent = 0f;
        for (var denseIndex = bodyParts.GetFirstDenseIndex(TargetEntityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex))
        {
            totalCurrent += bodyParts.GetReadonlyByDenseIndex(denseIndex).CurrentHealth;
        }

        // DirectHeal(0.5f) computes one total against the entity's overall max (Head 40 + Torso
        // 160 = 200), 0.5*200 = 100, split evenly across the 2 parts = 50 each
        // (ComplexHealthHeal.ApplyToAllParts) -- Head: 40 + 50 = 90, clamped to its own max of 40
        // -> stays 40. Torso: 40 + 50 = 90, under its max of 160 -> 90. Total: 40 + 90 = 130.
        Assert.AreEqual(130f, totalCurrent);
        var cooldown = componentManager.GetPackedPool<PotionCooldownComponent>().GetReadonly(TargetEntityId);
        Assert.AreEqual(PotionCooldownEffects.DurationFrames, cooldown.FramesRemaining, "The potion must still land -- cooldown resets the same as a Simple target's would.");
    }

    [TestMethod]
    public void ManaPotion_TargetOccupantAtTargetTile_FullyRestoresMana()
    {
        var (system, componentManager, mapQuery, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(currentHealth: 20, maximumHealth: 100));
        componentManager.Merge(TargetEntityId, new ManaComponent(currentMana: 3, maximumMana: 10));
        var stackInstanceId = InventoryActions.AddItem(componentManager, CasterEntityId, ManaPotionId, quantity: 1);
        componentManager.Merge(CasterEntityId, new PendingConsumableActivationComponent(stackInstanceId, [TargetTile]));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));

        system.Update(default, 0);

        Assert.AreEqual(10, ManaOf(componentManager, TargetEntityId), "ManaFraction 1f -- a full restore regardless of starting mana.");
        Assert.AreEqual(20, HealthOf(componentManager, TargetEntityId), "No DirectHeal on the Mana Potion -- health must be untouched.");
    }

    /// <summary>An entity with Health but no ManaComponent (never gained a mana-costing action) is still a legitimate potion target -- the potion is consumed and the target's cooldown still resets, it just has nothing to restore.</summary>
    [TestMethod]
    public void ManaPotion_TargetHasNoManaComponent_StillConsumesPotionAndSetsCooldownButRestoresNothing()
    {
        var (system, componentManager, mapQuery, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(currentHealth: 20, maximumHealth: 100));
        var stackInstanceId = InventoryActions.AddItem(componentManager, CasterEntityId, ManaPotionId, quantity: 1);
        componentManager.Merge(CasterEntityId, new PendingConsumableActivationComponent(stackInstanceId, [TargetTile]));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));

        system.Update(default, 0);

        Assert.IsFalse(componentManager.GetPackedPool<ManaComponent>().Has(TargetEntityId));
        Assert.AreEqual(-1, StackQuantity(componentManager, CasterEntityId, ManaPotionId), "The single potion was consumed -- StackQuantity's -1 sentinel means no stack found at all, per InventoryActions.ConsumeItem's own doc comment.");
        var cooldown = componentManager.GetPackedPool<PotionCooldownComponent>().GetReadonly(TargetEntityId);
        Assert.AreEqual(PotionCooldownEffects.DurationFrames, cooldown.FramesRemaining);
    }

    [TestMethod]
    public void Potion_Activation_DecrementsInventoryStackAndSetsActionLock()
    {
        var (system, componentManager, mapQuery, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(currentHealth: 20, maximumHealth: 100));
        var stackInstanceId = InventoryActions.AddItem(componentManager, CasterEntityId, PotionId, quantity: 3);
        componentManager.Merge(CasterEntityId, new PendingConsumableActivationComponent(stackInstanceId, [TargetTile]));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));

        system.Update(default, 0);

        Assert.AreEqual(2, StackQuantity(componentManager, CasterEntityId, PotionId));
        Assert.AreEqual(60, componentManager.GetPackedPool<ActionLockComponent>().GetReadonly(CasterEntityId).CurrentLockFramesRemaining);
        Assert.IsFalse(componentManager.GetPackedPool<PendingConsumableActivationComponent>().Has(CasterEntityId));
    }

    /// <summary>The cooldown belongs to whoever the potion actually landed on -- TargetEntityId here, not the CasterEntityId who threw it. See ConsumableActivationSystem's own doc comment.</summary>
    [TestMethod]
    public void Potion_Activation_ResetsTargetsPotionCooldownToFull_NotCasters()
    {
        var (system, componentManager, mapQuery, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(currentHealth: 20, maximumHealth: 100));
        var stackInstanceId = InventoryActions.AddItem(componentManager, CasterEntityId, PotionId, quantity: 1);
        componentManager.Merge(CasterEntityId, new PendingConsumableActivationComponent(stackInstanceId, [TargetTile]));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));

        system.Update(default, 0);

        var cooldown = componentManager.GetPackedPool<PotionCooldownComponent>().GetReadonly(TargetEntityId);
        Assert.AreEqual(PotionCooldownEffects.DurationFrames, cooldown.FramesRemaining);
        Assert.AreEqual(PotionCooldownEffects.DurationFrames, cooldown.TotalFrames);
        Assert.IsFalse(componentManager.GetPackedPool<PotionCooldownComponent>().Has(CasterEntityId), "Throwing at another entity must not touch the thrower's own cooldown.");
    }

    [TestMethod]
    public void Potion_ThrownAtSelf_ResetsCastersOwnCooldown()
    {
        var (system, componentManager, mapQuery, _) = Build();
        var selfTile = new Vector3Int(9, 9, 0);
        mapQuery.SetOccupant(selfTile, CasterEntityId);
        componentManager.Merge(CasterEntityId, new SimpleHealthComponent(currentHealth: 20, maximumHealth: 100));
        var stackInstanceId = InventoryActions.AddItem(componentManager, CasterEntityId, PotionId, quantity: 1);
        componentManager.Merge(CasterEntityId, new PendingConsumableActivationComponent(stackInstanceId, [selfTile]));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));

        system.Update(default, 0);

        var cooldown = componentManager.GetPackedPool<PotionCooldownComponent>().GetReadonly(CasterEntityId);
        Assert.AreEqual(PotionCooldownEffects.DurationFrames, cooldown.FramesRemaining);
    }

    [TestMethod]
    public void Potion_TargetHasHighConstitution_ResetsCooldownToScaledDuration()
    {
        var (system, componentManager, mapQuery, _) = BuildWithAbilityScores();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(currentHealth: 20, maximumHealth: 100));
        componentManager.GetMultiPool<AbilityScoreComponent>().Add(TargetEntityId, new AbilityScoreComponent(AbilityScoreType.Constitution, baseValue: 300, total: 300));
        var stackInstanceId = InventoryActions.AddItem(componentManager, CasterEntityId, PotionId, quantity: 1);
        componentManager.Merge(CasterEntityId, new PendingConsumableActivationComponent(stackInstanceId, [TargetTile]));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));

        system.Update(default, 0);

        var cooldown = componentManager.GetPackedPool<PotionCooldownComponent>().GetReadonly(TargetEntityId);
        Assert.AreEqual(PotionCooldownEffects.MinDurationFrames, cooldown.FramesRemaining);
        Assert.AreEqual(PotionCooldownEffects.MinDurationFrames, cooldown.TotalFrames);
    }

    [TestMethod]
    public void Potion_CooldownStillActiveOnTarget_GrantsTargetAPoisonStackAndPublishesAbusedEventForTarget()
    {
        var (system, componentManager, mapQuery, eventBus) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(currentHealth: 20, maximumHealth: 100));
        var stackInstanceId = InventoryActions.AddItem(componentManager, CasterEntityId, PotionId, quantity: 1);
        componentManager.Merge(CasterEntityId, new PendingConsumableActivationComponent(stackInstanceId, [TargetTile]));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));
        componentManager.GetPackedPool<PotionCooldownComponent>().Add(TargetEntityId, new PotionCooldownComponent(totalFrames: 1200, framesRemaining: 500));

        PotionCooldownAbusedEvent? published = null;
        eventBus.Subscribe<PotionCooldownAbusedEvent>(e => published = e);

        system.Update(default, 0);

        Assert.AreEqual(1, StatusEffectQueries.CountStacks(componentManager.GetMultiPool<StatusEffectStack>(), TargetEntityId, StatusEffectType.Poison));
        Assert.AreEqual(0, StatusEffectQueries.CountStacks(componentManager.GetMultiPool<StatusEffectStack>(), CasterEntityId, StatusEffectType.Poison));
        Assert.IsNotNull(published);
        Assert.AreEqual(TargetEntityId, published!.EntityId);
    }

    [TestMethod]
    public void Potion_CooldownNotActive_DoesNotGrantPoisonOrPublishAbusedEvent()
    {
        var (system, componentManager, mapQuery, eventBus) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(currentHealth: 20, maximumHealth: 100));
        var stackInstanceId = InventoryActions.AddItem(componentManager, CasterEntityId, PotionId, quantity: 1);
        componentManager.Merge(CasterEntityId, new PendingConsumableActivationComponent(stackInstanceId, [TargetTile]));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));

        var published = false;
        eventBus.Subscribe<PotionCooldownAbusedEvent>(_ => published = true);

        system.Update(default, 0);

        Assert.AreEqual(0, StatusEffectQueries.CountStacks(componentManager.GetMultiPool<StatusEffectStack>(), TargetEntityId, StatusEffectType.Poison));
        Assert.IsFalse(published);
    }

    [TestMethod]
    public void ItemNotInCasterInventory_DoesNothing()
    {
        var (system, componentManager, mapQuery, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(currentHealth: 20, maximumHealth: 100));
        componentManager.Merge(CasterEntityId, new PendingConsumableActivationComponent(PotionId, [TargetTile]));

        system.Update(default, 0);

        Assert.AreEqual(20, HealthOf(componentManager, TargetEntityId));
        Assert.IsFalse(componentManager.GetPackedPool<PendingConsumableActivationComponent>().Has(CasterEntityId));
    }

    [TestMethod]
    public void ItemWithNoActivator_DoesNothingButStillConsumesTheRequest()
    {
        var (system, componentManager, _, _) = Build();
        var stackInstanceId = InventoryActions.AddItem(componentManager, CasterEntityId, NonConsumableId, quantity: 1);
        componentManager.Merge(CasterEntityId, new PendingConsumableActivationComponent(stackInstanceId, [TargetTile]));

        system.Update(default, 0);

        Assert.AreEqual(1, StackQuantity(componentManager, CasterEntityId, NonConsumableId));
        Assert.IsFalse(componentManager.GetPackedPool<ActionLockComponent>().Has(CasterEntityId));
    }

    [TestMethod]
    public void ActionLockAlreadyBlocked_DoesNothing()
    {
        var (system, componentManager, mapQuery, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(currentHealth: 20, maximumHealth: 100));
        var stackInstanceId = InventoryActions.AddItem(componentManager, CasterEntityId, PotionId, quantity: 1);
        componentManager.Merge(CasterEntityId, new PendingConsumableActivationComponent(stackInstanceId, [TargetTile]));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 30, currentLockFramesRemaining: 30));

        system.Update(default, 0);

        Assert.AreEqual(20, HealthOf(componentManager, TargetEntityId));
        Assert.AreEqual(1, StackQuantity(componentManager, CasterEntityId, PotionId));
    }

    [TestMethod]
    public void CasterIsDead_DoesNothing()
    {
        var (system, componentManager, mapQuery, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(currentHealth: 20, maximumHealth: 100));
        var stackInstanceId = InventoryActions.AddItem(componentManager, CasterEntityId, PotionId, quantity: 1);
        componentManager.Merge(CasterEntityId, new PendingConsumableActivationComponent(stackInstanceId, [TargetTile]));
        componentManager.GetPackedPool<DeadComponent>().Add(CasterEntityId, new DeadComponent(KilledByEntityId: null, DiedAtFrame: 0));

        system.Update(default, 0);

        Assert.AreEqual(20, HealthOf(componentManager, TargetEntityId));
        Assert.AreEqual(1, StackQuantity(componentManager, CasterEntityId, PotionId));
    }

    [TestMethod]
    public void HotkeyExpansionPotion_TargetOccupantAtTargetTile_GrantsFiveMoreUnlockedSlots()
    {
        var (system, componentManager, mapQuery, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(currentHealth: 20, maximumHealth: 100));
        componentManager.GetPackedPool<HotkeyExpansionUnlockComponent>().Add(TargetEntityId, new HotkeyExpansionUnlockComponent(unlockedSlotCount: 10));
        var stackInstanceId = InventoryActions.AddItem(componentManager, CasterEntityId, HotkeyExpansionPotionId, quantity: 1);
        componentManager.Merge(CasterEntityId, new PendingConsumableActivationComponent(stackInstanceId, [TargetTile]));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));

        system.Update(default, 0);

        Assert.AreEqual((short)15, componentManager.GetPackedPool<HotkeyExpansionUnlockComponent>().GetReadonly(TargetEntityId).UnlockedSlotCount);
    }

    [TestMethod]
    public void HotkeyExpansionPotion_WouldExceedCap_ClampsToMaxUnlockedSlots()
    {
        var (system, componentManager, mapQuery, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(currentHealth: 20, maximumHealth: 100));
        componentManager.GetPackedPool<HotkeyExpansionUnlockComponent>().Add(TargetEntityId, new HotkeyExpansionUnlockComponent(unlockedSlotCount: 18));
        var stackInstanceId = InventoryActions.AddItem(componentManager, CasterEntityId, HotkeyExpansionPotionId, quantity: 1);
        componentManager.Merge(CasterEntityId, new PendingConsumableActivationComponent(stackInstanceId, [TargetTile]));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));

        system.Update(default, 0);

        Assert.AreEqual(HotkeyExpansion.MaxUnlockedSlots, componentManager.GetPackedPool<HotkeyExpansionUnlockComponent>().GetReadonly(TargetEntityId).UnlockedSlotCount);
    }

    [TestMethod]
    public void Wand_ChargesRemaining_AppliesDamageAndPeelsIntoANewDivergentStackWithOneFewerCharge()
    {
        var (system, componentManager, mapQuery, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(currentHealth: 20, maximumHealth: 100));
        var originalStackInstanceId = GrantWandAndGetStackInstanceId(componentManager, CasterEntityId, charges: 3, maxCharges: 3);
        componentManager.Merge(CasterEntityId, new PendingConsumableActivationComponent(originalStackInstanceId, [TargetTile]));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));

        system.Update(default, 0);

        Assert.AreEqual(10, HealthOf(componentManager, TargetEntityId));
        Assert.AreEqual(60, componentManager.GetPackedPool<ActionLockComponent>().GetReadonly(CasterEntityId).CurrentLockFramesRemaining);
        Assert.IsFalse(componentManager.GetPackedPool<PendingConsumableActivationComponent>().Has(CasterEntityId));

        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
        Assert.IsFalse(InventoryQueries.TryFindByStackInstanceId(stacks, CasterEntityId, originalStackInstanceId, out _), "The original single-unit stack must be gone entirely once its one unit is peeled off, not left behind at Quantity: 0.");
        Assert.AreEqual(1, stacks.CountForEntity(CasterEntityId), "Exactly one stack -- the peeled-off divergent instance -- should exist for the caster afterward.");
        Assert.IsTrue(InventoryQueries.TryGetStack(stacks, CasterEntityId, WandId, out var peeledStack));
        Assert.IsTrue(peeledStack.IsDivergent);
        Assert.IsNotNull(peeledStack.Override);
        var wandActivator = (WandActivator)peeledStack.Override!.Activator!;
        Assert.AreEqual((ushort)2, wandActivator.Charges);
        Assert.AreEqual((ushort)3, wandActivator.MaxCharges);
    }

    [TestMethod]
    public void Wand_HotkeySlotBoundToTheOriginalStack_IsRepointedToWhereverTheChargeLanded()
    {
        var (system, componentManager, mapQuery, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(currentHealth: 20, maximumHealth: 100));
        var originalStackInstanceId = GrantWandAndGetStackInstanceId(componentManager, CasterEntityId, charges: 3, maxCharges: 3);
        var bindings = componentManager.GetMultiPool<ItemHotkeyBindingComponent>();
        bindings.Add(CasterEntityId, new ItemHotkeyBindingComponent(HotkeySlot.Slot1, originalStackInstanceId));
        componentManager.Merge(CasterEntityId, new PendingConsumableActivationComponent(originalStackInstanceId, [TargetTile]));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));

        system.Update(default, 0);

        Assert.IsTrue(ItemHotkeyBindingQueries.TryGet(bindings, CasterEntityId, HotkeySlot.Slot1, out var boundStackInstanceId));
        Assert.AreNotEqual(originalStackInstanceId, boundStackInstanceId, "The binding must follow the charge to wherever it actually landed, not stay pointed at the now-gone original stack.");
        Assert.IsTrue(InventoryQueries.TryFindByStackInstanceId(componentManager.GetMultiPool<InventoryItemStackComponent>(), CasterEntityId, boundStackInstanceId, out _));
    }

    [TestMethod]
    public void Wand_LastChargeConsumed_DestroysTheStackWithoutLeavingAZeroChargeHusk()
    {
        var (system, componentManager, mapQuery, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(currentHealth: 20, maximumHealth: 100));
        var stackInstanceId = GrantWandAndGetStackInstanceId(componentManager, CasterEntityId, charges: 1, maxCharges: 3);
        componentManager.Merge(CasterEntityId, new PendingConsumableActivationComponent(stackInstanceId, [TargetTile]));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));

        system.Update(default, 0);

        Assert.AreEqual(10, HealthOf(componentManager, TargetEntityId), "The last charge still fires its effect.");
        Assert.AreEqual(0, componentManager.GetMultiPool<InventoryItemStackComponent>().CountForEntity(CasterEntityId), "The wand must be destroyed outright, not left behind as a permanent Charges: 0 stack.");
    }

    [TestMethod]
    public void Wand_NoChargesRemaining_DoesNothing()
    {
        var (system, componentManager, mapQuery, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(currentHealth: 20, maximumHealth: 100));
        var stackInstanceId = GrantWandAndGetStackInstanceId(componentManager, CasterEntityId, charges: 0, maxCharges: 3);
        componentManager.Merge(CasterEntityId, new PendingConsumableActivationComponent(stackInstanceId, [TargetTile]));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));

        system.Update(default, 0);

        Assert.AreEqual(20, HealthOf(componentManager, TargetEntityId));
        Assert.IsFalse(componentManager.GetPackedPool<ActionLockComponent>().TryGetReadonly(CasterEntityId, out var actionLock) && actionLock.CurrentLockTotalFrames > 0);
        Assert.IsTrue(InventoryQueries.TryFindByStackInstanceId(componentManager.GetMultiPool<InventoryItemStackComponent>(), CasterEntityId, stackInstanceId, out _), "An empty wand is inert, not consumed -- it stays exactly as it was.");
    }

    [TestMethod]
    public void Wand_ActionLockAlreadyBlocked_DoesNothing()
    {
        var (system, componentManager, mapQuery, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(currentHealth: 20, maximumHealth: 100));
        var stackInstanceId = GrantWandAndGetStackInstanceId(componentManager, CasterEntityId, charges: 3, maxCharges: 3);
        componentManager.Merge(CasterEntityId, new PendingConsumableActivationComponent(stackInstanceId, [TargetTile]));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 30, currentLockFramesRemaining: 30));

        system.Update(default, 0);

        Assert.AreEqual(20, HealthOf(componentManager, TargetEntityId));
        Assert.IsTrue(InventoryQueries.TryFindByStackInstanceId(componentManager.GetMultiPool<InventoryItemStackComponent>(), CasterEntityId, stackInstanceId, out var stackAfter));
        Assert.IsFalse(stackAfter.IsDivergent, "Blocked entirely by the shared ActionLock -- must never even reach the peel step.");
    }
}
