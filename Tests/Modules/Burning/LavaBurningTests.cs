using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Blueprints.Races;
using Game.Bootstrap;
using Game.Modules.Burning.Components;
using Game.Modules.Core.Components;
using Game.Modules.Health;
using Game.Modules.Movement.Components;
using Game.Modules.StatusEffects;
using Game.Spawning;
using Game.Terrain;
using Game.World;

namespace Tests.Modules.Burning;

/// <summary>Lava on a creature, in a build of every built-in module: spawned through the factory and run through the real systems.</summary>
[TestClass]
public sealed class LavaBurningTests
{
    private const int FramesPerSecond = 60;

    private static readonly Vector3Int LavaPosition = new(10, 10, (int)MapLayer.Ground);

    private sealed record Session(GameBuildPassResult Build, int EntityId)
    {
        public ComponentManager Components => Build.EcsContext.ComponentManager;

        public int EntityScopedStacks =>
            Components.GetPackedPool<BurningTimerComponent>().TryGetReadonly(EntityId, out var timer) ? timer.StackCount : 0;

        public int BurningPartCount => Components.GetMultiPool<BodyPartBurningTimerComponent>().CountForEntity(EntityId);

        public int StacksOnPart(int partId)
        {
            var partBurns = Components.GetMultiPool<BodyPartBurningTimerComponent>();
            for (var denseIndex = partBurns.GetFirstDenseIndex(EntityId); denseIndex != -1; denseIndex = partBurns.GetNextDenseIndex(denseIndex))
            {
                var burn = partBurns.GetReadonlyByDenseIndex(denseIndex);
                if (burn.PartId == partId)
                {
                    return burn.StackCount;
                }
            }

            return 0;
        }

        public int BottommostPartId => BodyPartSelection.PickBottommost(EntityBodyParts.For(Components, Build.Context.Definitions), EntityId, preferAlive: false);

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

    /// <summary>A goblin that can't walk away, standing tilesEastOfLava from the one lava cell.</summary>
    private static Session SpawnGoblin(int tilesEastOfLava)
    {
        var build = BuiltInTestModules.Build(new Map(new Vector3Int(40, 40, 3)), new MathUtility(new Random(1)), initialEntityCapacity: 200, initialComponentCapacity: 100);
        build.Context.ProcessingTierResolver.SetReferencePosition(LavaPosition);
        build.World.PopulateTerrain(LavaPosition.X, LavaPosition.Y, TerrainLayer.Ground, new TerrainCell(build.Context.Terrain.GetId(BuiltInTerrain.LavaKey), 0));

        var position = new Vector3Int(LavaPosition.X + tilesEastOfLava, LavaPosition.Y, LavaPosition.Z);
        var entityId = build.Factory.Spawn(SpawnRequest.At(build.Context.Definitions.GetId(Goblin.Id), position) with { Seed = 1 });
        build.EcsContext.ComponentManager.GetPackedPool<MovementComponent>().Remove(entityId);
        return new Session(build, entityId);
    }

    /// <summary>Standing in lava: its contact holds the burn on the part touching it, and its aura sets other parts alight on top of that.</summary>
    [TestMethod]
    public void StandingOnLava_BurnsThePartTouchingItAndTheAuraSpreadsToOthers()
    {
        var session = SpawnGoblin(tilesEastOfLava: 0);

        session.RunFrames(2 * FramesPerSecond);
        Assert.IsFalse(session.Components.GetPackedPool<Game.Modules.Death.Components.DeadComponent>().Has(session.EntityId), "Precondition: the goblin is still alive -- lava kills it within a few seconds.");

        Assert.IsGreaterThanOrEqualTo(7, session.StacksOnPart(session.BottommostPartId), "The contact tops the touching part back up to 8 every second; one may have burned off since.");
        Assert.IsGreaterThan(1, session.BurningPartCount, "The aura picks a part at random each second, so more than the touching part is burning.");
        Assert.AreEqual(0, session.EntityScopedStacks, "A creature with body parts burns on its parts.");
        Assert.IsGreaterThanOrEqualTo(7, StatusEffectQueries.CountStacks(session.Build.Context.StatusEffectDisplays, session.EntityId, StatusEffectType.Burning), "The entity-level display shows the highest burning part.");
    }

    /// <summary>Beside lava only its aura reaches: random parts burn at the aura's strength there, and the lava's contact -- which belongs to standing on it -- plays no part.</summary>
    [TestMethod]
    public void StandingBesideLava_TheAuraAloneBurnsRandomPartsAtItsStrengthThere()
    {
        var session = SpawnGoblin(tilesEastOfLava: 1);

        session.RunFrames(10 * FramesPerSecond);

        Assert.IsGreaterThan(1, session.BurningPartCount);
        Assert.AreEqual(0, session.EntityScopedStacks);
        var partBurns = session.Components.GetMultiPool<BodyPartBurningTimerComponent>();
        for (var denseIndex = partBurns.GetFirstDenseIndex(session.EntityId); denseIndex != -1; denseIndex = partBurns.GetNextDenseIndex(denseIndex))
        {
            Assert.IsLessThanOrEqualTo(4, partBurns.GetReadonlyByDenseIndex(denseIndex).StackCount, "No part is topped up past the aura's strength one tile away.");
        }
    }

    [TestMethod]
    public void OutOfTheAurasReach_NothingBurns()
    {
        var session = SpawnGoblin(tilesEastOfLava: 4);

        session.RunFrames(3 * FramesPerSecond);

        Assert.AreEqual(0, session.BurningPartCount);
        Assert.AreEqual(0, session.EntityScopedStacks);
    }
}
