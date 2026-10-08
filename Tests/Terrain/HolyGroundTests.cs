using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Math;
using Engine.Utilities;
using Game.Blueprints.Objects;
using Game.Bootstrap;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.Resources;
using Game.Spawning;
using Game.Terrain;
using Game.World;

namespace Tests.Terrain;

/// <summary>Holy Ground in a build of every built-in module: its blessing through the real contact system and its aura through the real aura system.</summary>
[TestClass]
public sealed class HolyGroundTests
{
    private const int FramesPerSecond = 60;
    private const int GroundLayer = (int)MapLayer.Ground;
    private const int BlessingFrames = 5 * 60 * FramesPerSecond;

    private static readonly Vector3Int HolyGroundPosition = new(10, 10, GroundLayer);

    private sealed record Session(GameBuildPassResult Build, ushort HolyGroundTypeId)
    {
        public ComponentManager Components => Build.EcsContext.ComponentManager;

        public long Now => Build.Context.SimulationClock.CurrentFrame;

        public int SpawnChest(Vector3Int position) =>
            Build.Factory.Spawn(SpawnRequest.At(Build.Context.Definitions.GetId(TreasureChest.Id), position) with { Seed = 1 });

        public float HealthOf(int entityId) => Components.GetPackedPool<SimpleHealthComponent>().GetReadonly(entityId).CurrentHealth;

        public void SetHealth(int entityId, float currentHealth) =>
            Components.GetPackedPool<SimpleHealthComponent>().TryUpdate(entityId, currentHealth, static (ref SimpleHealthComponent health, float value) => health.CurrentHealth = value);

        public void Damage(int entityId, ushort amount) =>
            HealthDamage.Apply(
                Components.GetPackedPool<SimpleHealthComponent>(), Build.EcsContext.EventBus, entityId, amount, ActionSource.Admin, Build.World, "Test", Now,
                Components.GetMultiPool<StatModifierComponent>(), EntityBodyParts.For(Components, Build.Context.Definitions), Build.Context.MathUtility,
                Components.GetPackedPool<DeadComponent>(), Build.Context.FloatingTextFeed, ResourceLossCategory.Direct);

        /// <summary>Every modifier Holy Ground has given entityId.</summary>
        public List<StatModifierComponent> BlessingsOf(int entityId)
        {
            var modifiers = Components.GetMultiPool<StatModifierComponent>();
            var blessings = new List<StatModifierComponent>();
            for (var denseIndex = modifiers.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = modifiers.GetNextDenseIndex(denseIndex))
            {
                var modifier = modifiers.GetReadonlyByDenseIndex(denseIndex);
                if (modifier.Source == ActionSource.FromTerrain(HolyGroundTypeId))
                {
                    blessings.Add(modifier);
                }
            }

            return blessings;
        }

        public void RunFrames(int count)
        {
            var frameDuration = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / FramesPerSecond);
            var start = Now + 1;
            for (var frame = start; frame < start + count; frame++)
            {
                Build.EcsContext.SystemManager.Update(new EngineTime(frameDuration * frame, frameDuration, false, frame));
            }
        }
    }

    private static Session BuildSession()
    {
        var build = BuiltInTestModules.Build(new Map(new Vector3Int(40, 40, 3)), new MathUtility(new Random(1)), initialEntityCapacity: 200, initialComponentCapacity: 100);
        build.Context.ProcessingTierResolver.SetReferencePosition(HolyGroundPosition);
        var holyGroundTypeId = build.Context.Terrain.GetId(BuiltInTerrain.HolyGroundKey);
        build.World.PopulateTerrain(HolyGroundPosition.X, HolyGroundPosition.Y, TerrainLayer.Ground, new TerrainCell(holyGroundTypeId, 0));
        return new Session(build, holyGroundTypeId);
    }

    [TestMethod]
    public void SteppingOn_GrantsATenPercentDamageReductionForFiveMinutes()
    {
        var session = BuildSession();
        var chestId = session.SpawnChest(HolyGroundPosition);

        session.RunFrames(2);

        var blessing = session.BlessingsOf(chestId).Single();
        Assert.AreEqual(StatModifierTarget.IncomingDamage, blessing.Target);
        Assert.AreEqual(StatModifierOperation.Multiplicative, blessing.Operation);
        Assert.AreEqual(StatModifierPolarity.Buff, blessing.Polarity);
        Assert.AreEqual(-0.10f, blessing.Magnitude);
        Assert.IsGreaterThanOrEqualTo(session.Now + BlessingFrames - 2, (long)blessing.ExpiresAtFrame);

        session.Damage(chestId, 20);
        Assert.AreEqual(82, session.HealthOf(chestId));
    }

    /// <summary>While the entity stays, the blessing is renewed every second: there is only ever one, and its expiry keeps moving ahead.</summary>
    [TestMethod]
    public void StandingOnIt_KeepsOneBlessingAndRenewsItEachSecond()
    {
        var session = BuildSession();
        var chestId = session.SpawnChest(HolyGroundPosition);
        session.RunFrames(2);
        var firstExpiry = session.BlessingsOf(chestId).Single().ExpiresAtFrame;

        session.RunFrames(3 * FramesPerSecond);

        var blessing = session.BlessingsOf(chestId).Single();
        Assert.IsGreaterThanOrEqualTo(firstExpiry + 2 * FramesPerSecond, blessing.ExpiresAtFrame);
    }

    [TestMethod]
    public void NotOnIt_GrantsNothing()
    {
        var session = BuildSession();
        var chestId = session.SpawnChest(new Vector3Int(HolyGroundPosition.X + 1, HolyGroundPosition.Y, GroundLayer));

        session.RunFrames(2 * FramesPerSecond);

        Assert.IsEmpty(session.BlessingsOf(chestId));
    }

    [TestMethod]
    [DataRow(0, 8)]
    [DataRow(1, 6)]
    [DataRow(3, 2)]
    public void ItsHealingAura_HealsByItsPowerAtTheEntitysDistance(int tilesAway, int healthPerSecond)
    {
        var session = BuildSession();
        var chestId = session.SpawnChest(new Vector3Int(HolyGroundPosition.X + tilesAway, HolyGroundPosition.Y, GroundLayer));
        session.SetHealth(chestId, 50);

        for (var frame = 0; frame < 3 * FramesPerSecond && session.HealthOf(chestId) == 50; frame++)
        {
            session.RunFrames(1);
        }

        Assert.AreEqual(50 + healthPerSecond, session.HealthOf(chestId));
    }

    [TestMethod]
    public void GlowsWhite()
    {
        var session = BuildSession();

        Assert.IsTrue(session.Build.Context.AuraField.TryGetGlow(HolyGroundPosition, out var glowColor, out var totalPower));
        Assert.AreEqual(Microsoft.Xna.Framework.Color.White, glowColor);
        Assert.AreEqual(8, totalPower);
        Assert.AreEqual(8, session.Build.Context.AuraField.GetTotalPowerAt(HolyGroundPosition, session.Build.Context.Auras.GetId(BuiltInTerrain.HolyGroundAura.Id)));
    }

    [TestMethod]
    public void ContactRepeatsEverySecond()
    {
        Assert.IsNotNull(BuiltInTerrain.HolyGround.Contact);
        Assert.AreEqual(GameTiming.FramesPerSecond, (int)BuiltInTerrain.HolyGround.Contact.RepeatEveryFrames!);
    }
}
