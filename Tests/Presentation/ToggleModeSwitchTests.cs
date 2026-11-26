using Engine.ECS.Systems;
using Engine.Math;
using Game.Blueprints.Composites;
using Game.Bootstrap;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Spawning;
using Game.World;
using Microsoft.Xna.Framework.Input;
using Presentation.Input;
using Presentation.MapRendering;
using Presentation.UI;
using Presentation.UI.Menus;
using UIEngine.UI;

namespace Tests.Presentation;

/// <summary>A toggle switched on and off from the hotbar across targeting-mode switches, through the real ActionTargetingController over a whole game session, with the map never focused: only the hotbar's key handler runs each frame, as when a window or the sidebar holds focus.</summary>
[TestClass]
public sealed class ToggleModeSwitchTests
{
    private const int FramesPerSecond = 60;
    private static readonly Vector3Int PlayerPosition = new(5, 5, 0);

    private sealed record Session(GameSession Game, int PlayerId, MapViewState MapViewState, ActionTargetingController Targeting, HotbarKeyInputHandler HotbarKeys)
    {
        public bool LanternIsOn => Game.Views.ActionStateView.IsActionToggledOn(PlayerId, BuiltInActionIds.Lantern);

        /// <summary>Frames of play: the input controller handing the hotbar handler its keys (none pressed), then the simulation.</summary>
        public void RunFrames(int count)
        {
            var frameDuration = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / FramesPerSecond);
            for (var index = 0; index < count; index++)
            {
                HotbarKeys.HandleKeys(new KeyboardState(), new KeyboardState(), isTextBoxFocused: false);
                var frame = Game.SimulationClock.CurrentFrame + 1;
                Game.EcsContext.SystemManager.Update(new EngineTime(frameDuration * frame, frameDuration, false, frame));
            }
        }

        /// <summary>A press of the Lantern's slot, then a second of play.</summary>
        public void PressLantern()
        {
            Targeting.HandleHotkeySlotPress(HotkeySlot.Slot1);
            RunFrames(FramesPerSecond);
        }
    }

    /// <summary>The player as FloorBuilder.CreatePlayer makes it -- pinned Local before it is built, at its reserved id -- with the Lantern on Slot1.</summary>
    private static Session Build()
    {
        var game = GameBootstrapper.Build(ValidatedMods.None, new Map(new Vector3Int(20, 20, 3)), TestContent.Layers, sessionSeed: 1, initialEntityCapacity: 200, initialComponentCapacity: 100);
        var playerId = game.EcsContext.EntityManager.CreateEntity();
        game.World.PlayerEntityId = playerId;
        game.Internals.ProcessingTierResolver.PinLocalAndNotify(playerId);
        var playerBlueprintId = game.Catalogs.Definitions.GetId(Player.Id);
        game.Internals.Factory.LoadContentFor(playerBlueprintId);
        game.Internals.Factory.Spawn(SpawnRequest.At(playerBlueprintId, PlayerPosition) with { ReservedEntityId = playerId });
        game.Commands.HotkeyBindingCommands.BindAction(playerId, HotkeySlot.Slot1, BuiltInActionIds.Lantern);

        var mapViewState = new MapViewState { HoveredTile = PlayerPosition };
        var views = game.Views;
        var targeting = new ActionTargetingController(
            game.World, mapViewState, new MapCamera(views.MapView), new UiLayerStack(), game.Catalogs.ItemCatalog, views.TransformView, views.HotkeyBindingView, views.InventoryView, views.ActionStateView,
            views.TargetingView, game.Commands.PlayerCommands, game.SimulationClock, new TestPlayerItemDrops(), new MenuCommandCatalog());
        var session = new Session(game, playerId, mapViewState, targeting, new HotbarKeyInputHandler(targeting));
        session.RunFrames(FramesPerSecond);
        return session;
    }

    [TestMethod]
    public void ALanternSetDownInGroundMode_TurnsOffAndOnAgainInTargetMode()
    {
        var session = Build();
        session.MapViewState.TargetingMode = TargetingMode.Ground;
        session.PressLantern();
        session.PressLantern();
        Assert.IsTrue(session.LanternIsOn, "Armed by the first press, set down by the second.");

        session.MapViewState.TargetingMode = TargetingMode.Target;
        session.PressLantern();
        Assert.IsFalse(session.LanternIsOn);

        session.PressLantern();
        Assert.IsTrue(session.LanternIsOn);
    }

    [TestMethod]
    public void ALanternCarriedInTargetMode_TurnsOffAndIsSetDownAgainInGroundMode()
    {
        var session = Build();
        session.MapViewState.TargetingMode = TargetingMode.Target;
        session.PressLantern();
        Assert.IsTrue(session.LanternIsOn);

        session.MapViewState.TargetingMode = TargetingMode.Ground;
        session.PressLantern();
        Assert.IsFalse(session.LanternIsOn);

        session.PressLantern();
        session.PressLantern();
        Assert.IsTrue(session.LanternIsOn);
    }

    [TestMethod]
    public void APressDuringTheCooldown_IsWrittenOnceReady_WithTheMapUnfocused()
    {
        var session = Build();
        session.MapViewState.TargetingMode = TargetingMode.Target;
        session.Targeting.HandleHotkeySlotPress(HotkeySlot.Slot1);
        session.RunFrames(2);
        Assert.IsTrue(session.LanternIsOn);

        session.RunFrames(FramesPerSecond - 10);
        session.Targeting.HandleHotkeySlotPress(HotkeySlot.Slot1);
        Assert.IsFalse(session.Game.EcsContext.ComponentManager.GetPackedPool<PendingActionActivationComponent>().Has(session.PlayerId), "Still cooling down: the press is buffered.");

        session.RunFrames(15);

        Assert.IsFalse(session.LanternIsOn, "Written by the hotbar's own per-frame flush once the cooldown ended.");
    }

    private static Vector2Byte PlayerSize(Session session) =>
        session.Game.EcsContext.ComponentManager.GetDirectPool<Game.Modules.Core.Components.TransformComponent>().GetReadonly(session.PlayerId).Size;

    private static Vector3Int[] AnchorPositions(Session session)
    {
        var components = session.Game.EcsContext.ComponentManager;
        var transforms = components.GetDirectPool<Game.Modules.Core.Components.TransformComponent>();
        return [.. components.GetPackedPool<Game.Modules.Auras.Components.AuraAnchorComponent>().EntityIds.ToArray().Select(anchorId => transforms.GetReadonly(anchorId).Position)];
    }

    [TestMethod]
    public void ALanternArmedInGroundMode_OffersTheTilesADropDoes_AndIsSetDownOnTheOneAimedAt()
    {
        var session = Build();
        session.MapViewState.TargetingMode = TargetingMode.Ground;
        session.PressLantern();

        Assert.AreEqual(BuiltInActionIds.Lantern, session.MapViewState.ArmedActionId);
        var size = PlayerSize(session);
        Assert.AreEqual((size.X + 2) * (size.Y + 2), session.MapViewState.TargetableTiles!.Count, "The player's footprint and the ring of tiles around it, as a drop.");

        var besideThePlayer = PlayerPosition + new Vector3Int(-1, 0, 0);
        session.MapViewState.HoveredTile = besideThePlayer;
        session.PressLantern();

        Assert.IsTrue(session.LanternIsOn);
        CollectionAssert.AreEqual(new[] { besideThePlayer }, AnchorPositions(session));
    }

    [TestMethod]
    public void ALanternArmedInGroundMode_AimedOutOfReach_StaysArmedAndUnlit()
    {
        var session = Build();
        session.MapViewState.TargetingMode = TargetingMode.Ground;
        session.PressLantern();

        session.MapViewState.HoveredTile = PlayerPosition + new Vector3Int(PlayerSize(session).X + 2, 0, 0);
        session.PressLantern();

        Assert.IsFalse(session.LanternIsOn);
        Assert.AreEqual(BuiltInActionIds.Lantern, session.MapViewState.ArmedActionId);
    }

    [TestMethod]
    public void ALanternArmedInGroundMode_ConfirmedByAMapClickInTargetMode_IsCarried()
    {
        var session = Build();
        session.MapViewState.TargetingMode = TargetingMode.Ground;
        session.PressLantern();

        session.MapViewState.TargetingMode = TargetingMode.Target;
        session.Targeting.TryConfirmActivationAtTile(PlayerPosition);
        session.RunFrames(FramesPerSecond);

        Assert.IsTrue(session.LanternIsOn);
        Assert.IsEmpty(AnchorPositions(session), "Confirmed by a map click in Target mode, it is carried, not set down.");
        Assert.IsTrue(session.Game.EcsContext.ComponentManager.GetMultiPool<Game.Modules.Auras.Components.AuraSourceComponent>().Has(session.PlayerId));
    }

    [TestMethod]
    public void AToggleItemArmedInGroundMode_ConfirmedByAMapClickInTargetMode_IsLitAndCarried()
    {
        var session = Build();
        var stacks = new List<Game.Modules.Inventory.Components.InventoryItemStackComponent>();
        session.Game.Views.InventoryView.CopyStacks(session.PlayerId, stacks);
        var idol = stacks.First(stack => stack.ItemDefinitionId == Game.Modules.Inventory.BuiltInItemIds.ToxicIdol);
        session.Game.Commands.HotkeyBindingCommands.TryBindItem(session.PlayerId, HotkeySlot.Slot2, idol.StackInstanceId);
        session.MapViewState.TargetingMode = TargetingMode.Ground;
        session.Targeting.HandleHotkeySlotPress(HotkeySlot.Slot2);
        session.RunFrames(FramesPerSecond);

        session.MapViewState.TargetingMode = TargetingMode.Target;
        session.Targeting.TryConfirmActivationAtTile(PlayerPosition);
        session.RunFrames(FramesPerSecond);

        var components = session.Game.EcsContext.ComponentManager;
        Assert.IsTrue(components.GetMultiPool<ActiveToggleComponent>().Has(session.PlayerId), "Lit.");
        Assert.AreEqual(0, components.GetPackedPool<Game.Modules.ItemPiles.Components.ItemPileComponent>().Count, "Confirmed in Target mode, it is carried, not put down in a pile.");
    }

    [TestMethod]
    public void ALanternCarriedByALargePlayer_RadiatesFromTheCenterOfItsFootprint()
    {
        var session = Build();
        var size = PlayerSize(session);
        Assert.IsGreaterThan(1, (int)size.X, "Precondition: the player is wider than a tile.");
        session.MapViewState.TargetingMode = TargetingMode.Target;
        session.PressLantern();

        var auraView = session.Game.Views.AuraView;
        var lantern = session.Game.Catalogs.Auras.GetId(Game.Modules.Auras.BuiltInAuraIds.Lantern);
        var center = PlayerPosition + new Vector3Int((size.X - 1) / 2, (size.Y - 1) / 2, 0);
        int PowerAt(Vector3Int tile) => auraView.PowerAt(tile.X, tile.Y, tile.Z, lantern);
        const int Offset = 3;

        Assert.IsGreaterThan(0, PowerAt(center));
        Assert.AreEqual(PowerAt(center + new Vector3Int(-Offset, 0, 0)), PowerAt(center + new Vector3Int(Offset, 0, 0)), "As far on the left as on the right.");
        Assert.AreEqual(PowerAt(center + new Vector3Int(0, -Offset, 0)), PowerAt(center + new Vector3Int(0, Offset, 0)), "As far above as below.");
    }
}
