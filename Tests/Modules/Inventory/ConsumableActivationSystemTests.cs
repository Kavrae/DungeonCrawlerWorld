using Engine.ECS.Components;
using Engine.Events;
using Engine.Math;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Inventory.Systems;
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
    private static readonly Guid NonConsumableId = Guid.NewGuid();
    private static readonly Vector3Int TargetTile = new(5, 5, 0);

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

        public void GetEntityIdsInBox(CubeInt box, Span<int> entityIds) { }
    }

    private static (ConsumableActivationSystem System, ComponentManager ComponentManager, FakeMapQuery MapQuery, EventBus EventBus) Build()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 10);
        componentManager.RegisterPackedPool<PendingConsumableActivationComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<ActionLockComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<PotionCooldownComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<HealthComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<DeadComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<InventoryItemStackComponent>();
        componentManager.RegisterMultiPool<StatusEffectStack>();
        componentManager.RegisterPackedPool<PoisonTimerComponent>(static (ref existing, incoming) => { });

        var itemCatalog = new ItemCatalog();
        itemCatalog.Register(new ItemDefinition(
            PotionId, "Test Potion", null, "p", Color.Green, Tags: [],
            Consumable: new ConsumableEffect(ConsumableKind.Potion, HealFraction: 0.5f, Targeting: new TargetingSpec(TargetShape.Burst, Range: 3, AreaSize: 1), ActionLockFrames: 60)));
        itemCatalog.Register(new ItemDefinition(NonConsumableId, "Test Hammer", null, "h", Color.Gray, Tags: []));

        var mapQuery = new FakeMapQuery();
        var eventBus = new EventBus();

        var system = new ConsumableActivationSystem(
            componentManager.GetPackedPool<PendingConsumableActivationComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetPackedPool<PotionCooldownComponent>(),
            componentManager.GetPackedPool<HealthComponent>(),
            itemCatalog,
            mapQuery,
            eventBus,
            componentManager,
            statModifiers: null,
            componentManager.GetPackedPool<DeadComponent>());

        return (system, componentManager, mapQuery, eventBus);
    }

    private static short HealthOf(ComponentManager componentManager, int entityId) =>
        componentManager.GetPackedPool<HealthComponent>().TryGetReadonly(entityId, out var health) ? health.CurrentHealth : (short)-1;

    private static int StackQuantity(ComponentManager componentManager, int entityId, Guid itemDefinitionId) =>
        InventoryQueries.TryGetStack(componentManager.GetMultiPool<InventoryItemStackComponent>(), entityId, itemDefinitionId, out var stack) ? stack.Quantity : -1;

    [TestMethod]
    public void Potion_TargetOccupantAtTargetTile_HealsByHealFractionOfItsOwnMaxHealth()
    {
        var (system, componentManager, mapQuery, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new HealthComponent(currentHealth: 20, healthRegen: 0, maximumHealth: 100));
        InventoryActions.AddItem(componentManager, CasterEntityId, PotionId, quantity: 1);
        componentManager.Merge(CasterEntityId, new PendingConsumableActivationComponent(PotionId, [TargetTile]));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));

        system.Update(default, 0);

        Assert.AreEqual(70, HealthOf(componentManager, TargetEntityId));
    }

    [TestMethod]
    public void Potion_Activation_DecrementsInventoryStackAndSetsActionLock()
    {
        var (system, componentManager, mapQuery, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new HealthComponent(currentHealth: 20, healthRegen: 0, maximumHealth: 100));
        InventoryActions.AddItem(componentManager, CasterEntityId, PotionId, quantity: 3);
        componentManager.Merge(CasterEntityId, new PendingConsumableActivationComponent(PotionId, [TargetTile]));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));

        system.Update(default, 0);

        Assert.AreEqual(2, StackQuantity(componentManager, CasterEntityId, PotionId));
        Assert.AreEqual(60, componentManager.GetPackedPool<ActionLockComponent>().GetReadonly(CasterEntityId).LockFramesRemaining);
        Assert.IsFalse(componentManager.GetPackedPool<PendingConsumableActivationComponent>().Has(CasterEntityId));
    }

    /// <summary>The cooldown belongs to whoever the potion actually landed on -- TargetEntityId here, not the CasterEntityId who threw it. See ConsumableActivationSystem's own doc comment.</summary>
    [TestMethod]
    public void Potion_Activation_ResetsTargetsPotionCooldownToFull_NotCasters()
    {
        var (system, componentManager, mapQuery, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new HealthComponent(currentHealth: 20, healthRegen: 0, maximumHealth: 100));
        InventoryActions.AddItem(componentManager, CasterEntityId, PotionId, quantity: 1);
        componentManager.Merge(CasterEntityId, new PendingConsumableActivationComponent(PotionId, [TargetTile]));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));

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
        componentManager.Merge(CasterEntityId, new HealthComponent(currentHealth: 20, healthRegen: 0, maximumHealth: 100));
        InventoryActions.AddItem(componentManager, CasterEntityId, PotionId, quantity: 1);
        componentManager.Merge(CasterEntityId, new PendingConsumableActivationComponent(PotionId, [selfTile]));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));

        system.Update(default, 0);

        var cooldown = componentManager.GetPackedPool<PotionCooldownComponent>().GetReadonly(CasterEntityId);
        Assert.AreEqual(PotionCooldownEffects.DurationFrames, cooldown.FramesRemaining);
    }

    [TestMethod]
    public void Potion_CooldownStillActiveOnTarget_GrantsTargetAPoisonStackAndPublishesAbusedEventForTarget()
    {
        var (system, componentManager, mapQuery, eventBus) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new HealthComponent(currentHealth: 20, healthRegen: 0, maximumHealth: 100));
        InventoryActions.AddItem(componentManager, CasterEntityId, PotionId, quantity: 1);
        componentManager.Merge(CasterEntityId, new PendingConsumableActivationComponent(PotionId, [TargetTile]));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        componentManager.GetPackedPool<PotionCooldownComponent>().Add(TargetEntityId, new PotionCooldownComponent(totalFrames: 1200, framesRemaining: 500));

        PotionCooldownAbused? published = null;
        eventBus.Subscribe<PotionCooldownAbused>(e => published = e);

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
        componentManager.Merge(TargetEntityId, new HealthComponent(currentHealth: 20, healthRegen: 0, maximumHealth: 100));
        InventoryActions.AddItem(componentManager, CasterEntityId, PotionId, quantity: 1);
        componentManager.Merge(CasterEntityId, new PendingConsumableActivationComponent(PotionId, [TargetTile]));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));

        var published = false;
        eventBus.Subscribe<PotionCooldownAbused>(_ => published = true);

        system.Update(default, 0);

        Assert.AreEqual(0, StatusEffectQueries.CountStacks(componentManager.GetMultiPool<StatusEffectStack>(), TargetEntityId, StatusEffectType.Poison));
        Assert.IsFalse(published);
    }

    [TestMethod]
    public void ItemNotInCasterInventory_DoesNothing()
    {
        var (system, componentManager, mapQuery, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new HealthComponent(currentHealth: 20, healthRegen: 0, maximumHealth: 100));
        componentManager.Merge(CasterEntityId, new PendingConsumableActivationComponent(PotionId, [TargetTile]));

        system.Update(default, 0);

        Assert.AreEqual(20, HealthOf(componentManager, TargetEntityId));
        Assert.IsFalse(componentManager.GetPackedPool<PendingConsumableActivationComponent>().Has(CasterEntityId));
    }

    [TestMethod]
    public void ItemWithNoConsumableEffect_DoesNothingButStillConsumesTheRequest()
    {
        var (system, componentManager, _, _) = Build();
        InventoryActions.AddItem(componentManager, CasterEntityId, NonConsumableId, quantity: 1);
        componentManager.Merge(CasterEntityId, new PendingConsumableActivationComponent(NonConsumableId, [TargetTile]));

        system.Update(default, 0);

        Assert.AreEqual(1, StackQuantity(componentManager, CasterEntityId, NonConsumableId));
        Assert.IsFalse(componentManager.GetPackedPool<ActionLockComponent>().Has(CasterEntityId));
    }

    [TestMethod]
    public void ActionLockAlreadyBlocked_DoesNothing()
    {
        var (system, componentManager, mapQuery, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new HealthComponent(currentHealth: 20, healthRegen: 0, maximumHealth: 100));
        InventoryActions.AddItem(componentManager, CasterEntityId, PotionId, quantity: 1);
        componentManager.Merge(CasterEntityId, new PendingConsumableActivationComponent(PotionId, [TargetTile]));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(totalLockFrames: 30, lockFramesRemaining: 30));

        system.Update(default, 0);

        Assert.AreEqual(20, HealthOf(componentManager, TargetEntityId));
        Assert.AreEqual(1, StackQuantity(componentManager, CasterEntityId, PotionId));
    }

    [TestMethod]
    public void CasterIsDead_DoesNothing()
    {
        var (system, componentManager, mapQuery, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new HealthComponent(currentHealth: 20, healthRegen: 0, maximumHealth: 100));
        InventoryActions.AddItem(componentManager, CasterEntityId, PotionId, quantity: 1);
        componentManager.Merge(CasterEntityId, new PendingConsumableActivationComponent(PotionId, [TargetTile]));
        componentManager.GetPackedPool<DeadComponent>().Add(CasterEntityId, new DeadComponent(KilledByEntityId: null));

        system.Update(default, 0);

        Assert.AreEqual(20, HealthOf(componentManager, TargetEntityId));
        Assert.AreEqual(1, StackQuantity(componentManager, CasterEntityId, PotionId));
    }
}
