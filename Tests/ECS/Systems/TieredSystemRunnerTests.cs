using Engine.ECS.Systems;

namespace Tests.ECS.Systems;

[TestClass]
public sealed class TieredSystemRunnerTests
{
    /// <summary>Records every call so a test can assert order, tiers visited and framesPerVisit.</summary>
    private sealed class RecordingTieredSystem : ITieredSystem
    {
        public RecordingTieredSystem(byte baseStripeCount, byte[] tierDivisors, int[] entityIdsByTier)
        {
            // entityIdsByTier[t] is placed in tier t. Which of them is due on a given frame is the
            // ordinary stripe rule: entityId % (baseStripeCount * divisor) == frame % that.
            var tierOf = new Dictionary<int, byte>();
            for (byte tier = 0; tier < entityIdsByTier.Length; tier++)
            {
                tierOf[entityIdsByTier[tier]] = tier;
            }

            Tiers = new TieredEntityStripeSet(baseStripeCount, tierDivisors, entityIdsByTier, entityId => tierOf[entityId]);
        }

        public List<string> Calls { get; } = [];

        public byte StripeCount => 1;

        public TieredEntityStripeSet Tiers { get; }

        public void Update(EngineTime time, byte stripeIndex) => TieredSystemRunner.Run(this, time);

        public void BeginFrame(EngineTime time) => Calls.Add("BeginFrame");

        public void UpdateBucket(EngineTime time, ReadOnlySpan<int> entityIds, ushort framesPerVisit) =>
            Calls.Add($"Bucket[{string.Join(",", entityIds.ToArray())}] x{framesPerVisit}");
    }

    /// <summary>A plain system, to prove SystemManager still hands it its rotating stripe index.</summary>
    private sealed class RecordingPlainSystem : ISystem
    {
        public List<byte> StripeIndices { get; } = [];

        public byte StripeCount => 3;

        public void Update(EngineTime time, byte stripeIndex) => StripeIndices.Add(stripeIndex);
    }

    private static EngineTime Frame(long frameCount) => new(default, default, false, frameCount);

    /// <summary>All four tiers share base stripe count 1 and divisor 1, so every entity is due every frame and each tier's bucket holds exactly its one entity.</summary>
    private static RecordingTieredSystem FourTiersAllDue() => new(1, [1, 1, 1, 1], [10, 11, 12, 13]);

    [TestMethod]
    public void Run_CallsBeginFrameOnceThenEveryTierInAscendingOrder()
    {
        var system = FourTiersAllDue();

        TieredSystemRunner.Run(system, Frame(0));

        CollectionAssert.AreEqual(
            new[] { "BeginFrame", "Bucket[10] x1", "Bucket[11] x1", "Bucket[12] x1", "Bucket[13] x1" },
            system.Calls);
    }

    /// <summary>framesPerVisit is each tier's own bucket stripe count -- base * divisor -- which is the value every consumer must scale its countdowns by.</summary>
    [TestMethod]
    public void Run_HandsEachTierItsOwnFramesPerVisit()
    {
        var system = new RecordingTieredSystem(2, [1, 16, 32, 64], [0, 1, 2, 3]);

        // Frame 0 is due for bucket 0 of every tier; entity 0 is the only one in a bucket 0.
        TieredSystemRunner.Run(system, Frame(0));

        CollectionAssert.AreEqual(
            new[] { "BeginFrame", "Bucket[0] x2", "Bucket[] x32", "Bucket[] x64", "Bucket[] x128" },
            system.Calls);
    }

    /// <summary>The P2 policy hook: tiers at or past simulatedTierCount are skipped entirely -- not visited at all -- while BeginFrame still runs.</summary>
    [TestMethod]
    public void Run_SimulatedTierCount_SkipsTiersAtOrPastIt()
    {
        var system = FourTiersAllDue();

        TieredSystemRunner.Run(system, Frame(0), simulatedTierCount: 2);

        CollectionAssert.AreEqual(new[] { "BeginFrame", "Bucket[10] x1", "Bucket[11] x1" }, system.Calls);
    }

    [TestMethod]
    public void Run_SimulatedTierCountAboveTierCount_IsClampedToEveryTier()
    {
        var system = FourTiersAllDue();

        TieredSystemRunner.Run(system, Frame(0), simulatedTierCount: 99);

        Assert.HasCount(5, system.Calls);
    }

    /// <summary>
    /// SystemManager must run a tiered system through TieredSystemRunner with its own policy, not
    /// through the system's Update -- which runs every tier and would silently ignore the policy.
    /// </summary>
    [TestMethod]
    public void SystemManager_RunsTieredSystemsThroughTheRunnerWithItsPolicy()
    {
        var system = FourTiersAllDue();
        var manager = new SystemManager { SimulatedTierCount = 1 };
        manager.Register(system);

        manager.Update(Frame(0));

        CollectionAssert.AreEqual(new[] { "BeginFrame", "Bucket[10] x1" }, system.Calls);
    }

    [TestMethod]
    public void SystemManager_DefaultPolicy_SimulatesEveryTier()
    {
        var system = FourTiersAllDue();
        var manager = new SystemManager();
        manager.Register(system);

        manager.Update(Frame(0));

        Assert.HasCount(5, system.Calls);
    }

    /// <summary>A plain ISystem is untouched by any of this and still receives SystemManager's rotating stripe index.</summary>
    [TestMethod]
    public void SystemManager_PlainSystem_StillGetsItsRotatingStripeIndex()
    {
        var system = new RecordingPlainSystem();
        var manager = new SystemManager { SimulatedTierCount = 1 };
        manager.Register(system);

        manager.Update(Frame(0));
        manager.Update(Frame(1));
        manager.Update(Frame(2));
        manager.Update(Frame(3));

        CollectionAssert.AreEqual(new byte[] { 0, 1, 2, 0 }, system.StripeIndices);
    }

    /// <summary>Update on a tiered system -- the standalone path tests use -- runs every tier through the same runner.</summary>
    [TestMethod]
    public void TieredSystemUpdate_RunsEveryTierThroughTheSameRunner()
    {
        var system = FourTiersAllDue();

        system.Update(Frame(0), stripeIndex: 0);

        Assert.HasCount(5, system.Calls);
    }
}
