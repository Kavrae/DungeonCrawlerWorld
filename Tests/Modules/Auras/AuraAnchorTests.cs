using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Blueprints.Races;
using Game.Bootstrap;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Auras.Components;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Inventory.Definitions;
using Game.Modules.Movement.Components;
using Game.Spawning;
using Game.World;

namespace Tests.Modules.Auras;

/// <summary>An aura granted at a tile is an anchor entity there: Scroll of Torch read at the ground or an empty tile, the Lantern toggle switched on in Ground mode. Each ends cleanly, and is cancelled with its owner or when it goes itself.</summary>
[TestClass]
public sealed class AuraAnchorTests
{
    private const int FramesPerSecond = 60;
    private const int Ground = (int)MapLayer.Ground;

    private static readonly Vector3Int CasterPosition = new(10, 10, Ground);
    private static readonly Vector3Int AimedTile = new(13, 10, Ground);

    private sealed record Session(GameBuildPassResult Build, int CasterId)
    {
        public ComponentManager Components => Build.EcsContext.ComponentManager;

        /// <summary>The anchors on the map, by id.</summary>
        public int[] AnchorIds => Components.GetPackedPool<AuraAnchorComponent>().EntityIds.ToArray();

        public int Spawn(Guid blueprintId, Vector3Int position)
        {
            var entityId = Build.Factory.Spawn(SpawnRequest.At(Build.Context.Definitions.GetId(blueprintId), position) with { Seed = 1 });
            Components.GetPackedPool<MovementComponent>().Remove(entityId);
            return entityId;
        }

        public int PowerAt(Vector3Int tile, Game.Modules.Auras.AuraDefinition aura) => Build.Context.AuraField.GetTotalPowerAt(tile, Build.Context.Auras.GetId(aura.Id));

        public void ReadTorch(TargetingMode mode, Vector3Int tile)
        {
            var stackInstanceId = InventoryActions.AddItem(Components, CasterId, ScrollOfTorch.Id, quantity: 1);
            var selection = Build.Context.TargetResolution.Select(CasterId, ScrollOfTorch.Build().Activator!, mode, tile, Build.Context.SimulationClock.CurrentFrame);
            Components.Merge(CasterId, new PendingItemActivationComponent(stackInstanceId, selection));
            RunFrames(1);
        }

        public void PressLantern(TargetingMode mode)
        {
            var selection = Build.Context.TargetResolution.Select(CasterId, LanternAction.Build().Activator, mode, CasterPosition, Build.Context.SimulationClock.CurrentFrame);
            Components.Merge(CasterId, new PendingActionActivationComponent(LanternAction.Id, selection));
            RunFrames(1);
        }

        public bool LanternIsOn => Build.Context.Toggles.IsOn(CasterId, ActivatableReference.Action(LanternAction.Id));

        public void RunFrames(int count)
        {
            var frameDuration = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / FramesPerSecond);
            var start = Build.EcsContext.SystemManager.Clock.CurrentFrame + 1;
            for (var frame = start; frame < start + count; frame++)
            {
                Build.EcsContext.SystemManager.Update(new EngineTime(frameDuration * frame, frameDuration, false, frame));
            }
        }
    }

    private static Session BuildSession()
    {
        var build = BuiltInTestModules.Build(new Map(new Vector3Int(40, 40, 3)), new MathUtility(new Random(1)), initialEntityCapacity: 200, initialComponentCapacity: 100);
        build.Context.ProcessingTierResolver.SetReferencePosition(CasterPosition);
        var session = new Session(build, CasterId: -1);
        var casterId = session.Spawn(Goblin.Id, CasterPosition);
        ActionGrantEffects.Grant(session.Components, session.Build.Context.Actions, casterId, LanternAction.Id, overrideDefinition: null);
        session.RunFrames(FramesPerSecond);
        return session with { CasterId = casterId };
    }

    [TestMethod]
    public void Torch_GroundMode_AnchorsTheLightOnTheTile_NamedAfterIt()
    {
        var session = BuildSession();

        session.ReadTorch(TargetingMode.Ground, AimedTile);

        var anchorId = session.AnchorIds.Single();
        var transform = session.Components.GetDirectPool<TransformComponent>().GetReadonly(anchorId);
        Assert.AreEqual(AimedTile, transform.Position);
        Assert.AreEqual("Light Aura", session.Components.GetPackedPool<DisplayTextComponent>().GetReadonly(anchorId).Name);
        Assert.AreEqual(8, session.PowerAt(AimedTile, ScrollOfTorch.Aura));
        Assert.IsFalse(session.Build.World.IsBlocking(anchorId), "An anchor never blocks the tile.");
    }

    [TestMethod]
    public void Torch_TargetModeAtAnEmptyTile_AnchorsThereToo()
    {
        var session = BuildSession();

        session.ReadTorch(TargetingMode.Target, AimedTile);

        Assert.HasCount(1, session.AnchorIds);
        Assert.AreEqual(8, session.PowerAt(AimedTile, ScrollOfTorch.Aura));
    }

    [TestMethod]
    public void Torch_TargetModeAtAGoblin_LightsTheGoblinAndFollowsIt_NoAnchor()
    {
        var session = BuildSession();
        var goblinId = session.Spawn(Goblin.Id, AimedTile);

        session.ReadTorch(TargetingMode.Target, AimedTile);

        Assert.IsEmpty(session.AnchorIds);
        Assert.IsTrue(session.Components.GetMultiPool<AuraSourceComponent>().Has(goblinId));
    }

    [TestMethod]
    public void Torch_Anchor_EndsAtItsDuration_LeavingNothingInTheField()
    {
        var session = BuildSession();
        session.ReadTorch(TargetingMode.Ground, AimedTile);
        var anchorId = session.AnchorIds.Single();
        var anchorKey = session.Build.EcsContext.EntityManager.Keys.GetKey(anchorId);

        session.RunFrames(20 * FramesPerSecond);

        Assert.IsEmpty(session.AnchorIds);
        Assert.IsFalse(session.Build.EcsContext.EntityManager.Keys.TryGetEntityId(anchorKey, out _), "The anchor entity is gone.");
        Assert.AreEqual(0, session.PowerAt(AimedTile, ScrollOfTorch.Aura));
    }

    [TestMethod]
    public void Torch_OwnerDestroyed_CancelsItsAnchor()
    {
        var session = BuildSession();
        session.ReadTorch(TargetingMode.Ground, AimedTile);

        session.Build.EcsContext.EntityManager.DestroyEntity(session.CasterId);
        session.RunFrames(1);

        Assert.IsEmpty(session.AnchorIds);
        Assert.AreEqual(0, session.PowerAt(AimedTile, ScrollOfTorch.Aura));
    }

    [TestMethod]
    public void Lantern_TargetMode_IsCarriedByItsHolder()
    {
        var session = BuildSession();

        session.PressLantern(TargetingMode.Target);

        Assert.IsTrue(session.LanternIsOn);
        Assert.IsEmpty(session.AnchorIds);
        Assert.AreEqual(8, session.PowerAt(CasterPosition, LanternAction.Aura));
    }

    [TestMethod]
    public void Lantern_GroundMode_IsSetDownWhereTheHolderStands_AndGoesWithTheToggle()
    {
        var session = BuildSession();

        session.PressLantern(TargetingMode.Ground);

        var anchorId = session.AnchorIds.Single();
        Assert.AreEqual(CasterPosition, session.Components.GetDirectPool<TransformComponent>().GetReadonly(anchorId).Position);
        Assert.IsFalse(session.Components.GetMultiPool<AuraSourceComponent>().Has(session.CasterId), "The holder carries nothing.");

        session.RunFrames(FramesPerSecond);
        session.PressLantern(TargetingMode.Ground);
        session.RunFrames(1);

        Assert.IsFalse(session.LanternIsOn);
        Assert.IsEmpty(session.AnchorIds);
        Assert.AreEqual(0, session.PowerAt(CasterPosition, LanternAction.Aura));
    }

    /// <summary>What its neighborhood unloading does: the streamer destroys the anchor, and the toggle holding it is switched off with it.</summary>
    [TestMethod]
    public void Lantern_AnchorDestroyed_SwitchesTheToggleOff()
    {
        var session = BuildSession();
        session.PressLantern(TargetingMode.Ground);

        session.Build.EcsContext.EntityManager.DestroyEntity(session.AnchorIds.Single());
        session.RunFrames(1);

        Assert.IsFalse(session.LanternIsOn, "A toggle is never on with nothing behind it.");
        Assert.AreEqual(0, session.PowerAt(CasterPosition, LanternAction.Aura));
    }

    [TestMethod]
    public void Lantern_HolderDestroyed_CancelsItsAnchor()
    {
        var session = BuildSession();
        session.PressLantern(TargetingMode.Ground);

        session.Build.EcsContext.EntityManager.DestroyEntity(session.CasterId);
        session.RunFrames(1);

        Assert.IsEmpty(session.AnchorIds);
        Assert.AreEqual(0, session.PowerAt(CasterPosition, LanternAction.Aura));
    }
}
