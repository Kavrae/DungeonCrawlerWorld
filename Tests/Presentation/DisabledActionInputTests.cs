using Engine.ECS.Components;
using Game.Effects;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.BodyPartEffects.Components;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Mana.Components;
using Game.Modules.Movement.Components;
using Game.Tags;
using Game.Views;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Presentation.Input;
using Presentation.Rendering;
using Presentation.UI;
using Presentation.UI.Content;

namespace Tests.Presentation;

/// <summary>An action the player can't use can't be armed, and a confirm that can't be carried out leaves the action armed -- driven through the real hotbar key and click paths.</summary>
[TestClass]
[DoNotParallelize]
public sealed class DisabledActionInputTests
{
    private const int PlayerEntityId = 1;
    private const long StartFrame = 100;
    private const ushort SpellManaCost = 5;
    private const ushort SpellCooldownFrames = 60;
    private static readonly Vector3Int PlayerPosition = new(5, 5, 0);
    private static readonly Guid SpellId = new("3f6e1a9c-8b24-4d7e-a150-c92e4b7d6f18");

    private sealed class Harness
    {
        public required ActionTargetingController ActionTargeting { get; init; }
        public required HotbarController HotbarController { get; init; }
        public required HotbarContent HotbarContent { get; init; }
        public required PlayerCommands PlayerCommands { get; init; }
        public required EntityActions Actions { get; init; }
        public required MapViewState MapViewState { get; init; }
        public required ComponentManager ComponentManager { get; init; }
        public required SimulationClock Clock { get; init; }

        public bool HasPendingAction => ComponentManager.GetPackedPool<PendingActionActivationComponent>().Has(PlayerEntityId);

        public void PressKey(HotkeySlot slot)
        {
            var entry = HotkeySlotLayout.Entries.First(candidate => candidate.Slot == slot);
            var keys = entry.RequiresShift ? new[] { entry.Key, Keys.LeftShift } : new[] { entry.Key };
            var previous = entry.RequiresShift ? new KeyboardState(Keys.LeftShift) : new KeyboardState();
            ActionTargeting.HandleHotbarHotkeys(new KeyboardState(keys), previous);
        }

        public void Click(HotkeySlot slot)
        {
            HotbarController.OnSlotPressed(slot);
            HotbarController.OnSlotTapped(slot);
        }

        public void WaitOutTheDoubleTapWindow()
        {
            for (var frame = 0; frame <= UiInputController.DoubleClickWindowFrames; frame++)
            {
                ActionTargeting.Tick();
            }
        }

        public void DisableMelee() => ComponentManager.Merge(PlayerEntityId, new MeleeDisabledComponent());

        public void SetMana(float currentMana) =>
            ComponentManager.GetPackedPool<ManaComponent>().TryUpdate(PlayerEntityId, currentMana, static (ref ManaComponent mana, float value) => mana.CurrentMana = value);
    }

    private static Harness Build()
    {
        var world = TestWorlds.Create(new Game.World.Map(new Vector3Int(20, 20, 1)), playerEntityId: PlayerEntityId);
        var mapViewState = new MapViewState();
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(20, 10));

        TestTransforms.Set(componentManager, PlayerEntityId, new TransformComponent(PlayerPosition, new Vector2Byte(1, 1)));
        componentManager.Merge(PlayerEntityId, new MovementComponent(MovementMode.PlayerControlled, null, null));
        componentManager.Merge(PlayerEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, unlockedAtFrame: 0));
        componentManager.Merge(PlayerEntityId, new ManaComponent(50f, 100f));
        componentManager.Merge(PlayerEntityId, new HotkeyExpansionUnlockComponent(unlockedSlotCount: 5));
        componentManager.Merge(PlayerEntityId, new ActionInstanceComponent(QuickAttackAction.Id, overrideDefinition: null));
        componentManager.Merge(PlayerEntityId, new ActionInstanceComponent(SpellId, overrideDefinition: null));
        var bindings = componentManager.GetMultiPool<ActionHotkeyBindingComponent>();
        bindings.Add(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot1, QuickAttackAction.Id));
        bindings.Add(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot2, SpellId));

        var actionCatalog = new ActionCatalog();
        actionCatalog.Register(QuickAttackAction.Build());
        actionCatalog.Register(new ActionDefinition(
            SpellId, "Test Self Spell", null, "*", Color.White, [GameTags.TargetingSelf],
            Effects: [Effect.None],
            Activator: new SpellActivator(
                new TargetingSpec(TargetShape.Self, Range: 0),
                new ActionTiming(ActionTimingCategory.Immediate, ActionLockFrames: 30, CooldownFrames: SpellCooldownFrames),
                SpellManaCost)));
        var itemCatalog = new ItemCatalog();

        var clock = new SimulationClock();
        clock.Advance(StartFrame);
        var entityActions = TestActionStateViews.EntityActions(componentManager, actionCatalog);
        var actionStateView = new ActionStateView(componentManager, entityActions, itemCatalog, localTierRoster: null);
        var playerCommands = new PlayerCommands(
            world,
            componentManager.GetDirectPool<TransformComponent>(),
            componentManager.GetPackedPool<MovementComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetPackedPool<PendingActionActivationComponent>(),
            componentManager.GetPackedPool<PendingConsumableActivationComponent>(),
            componentManager.GetPackedPool<PendingDelayedActionComponent>(),
            clock,
            entityActions,
            new EventBus());

        var actionTargeting = new ActionTargetingController(
            world,
            mapViewState,
            new MapCamera(world),
            new UiLayerStack(),
            actionCatalog,
            itemCatalog,
            new TransformView(componentManager),
            new HotkeyBindingView(componentManager),
            new InventoryView(componentManager, itemCatalog),
            actionStateView,
            new AbilityScoreView(componentManager),
            playerCommands,
            simulationClock: clock);

        var hotbarContent = new HotbarContent(world, mapViewState, new HotkeyBindingView(componentManager), new InventoryView(componentManager, itemCatalog), actionStateView, new HotkeyBindingCommands(componentManager, itemCatalog, new EventBus()), actionCatalog, itemCatalog, TestFonts.Shared, new SpriteSheetService(null, "Spritesheets"), new SpriteRenderer(), new Vector2(1920, 1080), simulationClock: clock);
        var hotbarController = new HotbarController(mapViewState, hotbarContent, actionTargeting, new TooltipController());

        return new Harness
        {
            ActionTargeting = actionTargeting,
            HotbarController = hotbarController,
            HotbarContent = hotbarContent,
            PlayerCommands = playerCommands,
            Actions = entityActions,
            MapViewState = mapViewState,
            ComponentManager = componentManager,
            Clock = clock,
        };
    }

    [TestMethod]
    public void MeleeDisabled_KeyPressClickAndDoubleTap_NeitherArmNorQueue()
    {
        var harness = Build();
        harness.DisableMelee();

        harness.PressKey(HotkeySlot.Slot1);
        Assert.IsNull(harness.MapViewState.ArmedSlot);

        harness.WaitOutTheDoubleTapWindow();
        harness.Click(HotkeySlot.Slot1);
        Assert.IsNull(harness.MapViewState.ArmedSlot);

        harness.WaitOutTheDoubleTapWindow();
        harness.PressKey(HotkeySlot.Slot1);
        harness.PressKey(HotkeySlot.Slot1);
        Assert.IsNull(harness.MapViewState.ArmedSlot);
        Assert.IsFalse(harness.HasPendingAction);
    }

    [TestMethod]
    public void MeleeDisabled_HotbarDimsTheMeleeSlotOnly()
    {
        var harness = Build();
        harness.HotbarContent.Update(new GameTime());
        Assert.IsTrue(harness.HotbarContent.IsSlotActive(HotkeySlot.Slot1));

        harness.DisableMelee();
        harness.HotbarContent.Update(new GameTime());

        Assert.IsFalse(harness.HotbarContent.IsSlotActive(HotkeySlot.Slot1));
        Assert.IsTrue(harness.HotbarContent.IsSlotActive(HotkeySlot.Slot2));
    }

    [TestMethod]
    public void ArmedMeleeAction_IsDisarmedOnTheNextTick_OnceMeleeIsDisabled()
    {
        var harness = Build();
        harness.PressKey(HotkeySlot.Slot1);
        Assert.AreEqual(HotkeySlot.Slot1, harness.MapViewState.ArmedSlot, "Sanity check: usable melee arms.");

        harness.DisableMelee();
        harness.ActionTargeting.Tick();

        Assert.IsNull(harness.MapViewState.ArmedSlot);
        Assert.IsNull(harness.MapViewState.ArmedActionId);
    }

    [TestMethod]
    public void NotEnoughMana_DimsAndWontArm_UntilManaReturns()
    {
        var harness = Build();
        harness.SetMana(SpellManaCost - 1);
        harness.HotbarContent.Update(new GameTime());

        harness.PressKey(HotkeySlot.Slot2);
        Assert.IsNull(harness.MapViewState.ArmedSlot);
        Assert.IsFalse(harness.HotbarContent.IsSlotActive(HotkeySlot.Slot2));

        harness.SetMana(SpellManaCost);
        harness.HotbarContent.Update(new GameTime());
        harness.WaitOutTheDoubleTapWindow();
        harness.PressKey(HotkeySlot.Slot2);

        Assert.AreEqual(HotkeySlot.Slot2, harness.MapViewState.ArmedSlot);
        Assert.IsTrue(harness.HotbarContent.IsSlotActive(HotkeySlot.Slot2));
    }

    [TestMethod]
    public void ConfirmDuringALongCooldown_QueuesNothingAndStaysArmed_ThenFiresOnceItCanLand()
    {
        var harness = Build();
        harness.Actions.SetCooldown(PlayerEntityId, SpellId, SpellCooldownFrames, StartFrame);

        harness.PressKey(HotkeySlot.Slot2);
        Assert.AreEqual(HotkeySlot.Slot2, harness.MapViewState.ArmedSlot, "A slot on cooldown still arms -- cooldown isn't a blocker.");

        harness.WaitOutTheDoubleTapWindow();
        harness.PressKey(HotkeySlot.Slot2);
        Assert.AreEqual(HotkeySlot.Slot2, harness.MapViewState.ArmedSlot, "A refused confirm leaves the action armed.");
        Assert.IsFalse(harness.HasPendingAction);

        var readyAtFrame = StartFrame + SpellCooldownFrames;
        harness.Clock.Advance(readyAtFrame - PlayerCommands.ExpiryFrames + 1);
        harness.WaitOutTheDoubleTapWindow();
        harness.PressKey(HotkeySlot.Slot2);
        Assert.IsNull(harness.MapViewState.ArmedSlot, "An accepted confirm disarms.");
        Assert.IsFalse(harness.HasPendingAction, "Buffered until the cooldown ends.");

        harness.Clock.Advance(readyAtFrame);
        harness.PlayerCommands.Flush(new Vector3Int());
        Assert.IsTrue(harness.HasPendingAction);
    }

    [TestMethod]
    public void DoubleTapDuringALongCooldown_FiresNothing_AndLeavesTheSlotArmed()
    {
        var harness = Build();
        harness.Actions.SetCooldown(PlayerEntityId, SpellId, SpellCooldownFrames, StartFrame);

        harness.PressKey(HotkeySlot.Slot2);
        harness.PressKey(HotkeySlot.Slot2);

        Assert.AreEqual(HotkeySlot.Slot2, harness.MapViewState.ArmedSlot);
        Assert.IsFalse(harness.HasPendingAction);
    }
}
