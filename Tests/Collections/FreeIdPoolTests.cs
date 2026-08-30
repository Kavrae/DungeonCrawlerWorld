using Engine.Collections;

namespace Tests.Collections;

[TestClass]
public sealed class FreeIdPoolTests
{
    [TestMethod]
    public void Rent_FirstCalls_ReturnSequentialIds()
    {
        var pool = new FreeIdPool();

        Assert.AreEqual(0, pool.Rent());
        Assert.AreEqual(1, pool.Rent());
        Assert.AreEqual(2, pool.Rent());
        Assert.AreEqual(3, pool.Count);
    }

    [TestMethod]
    public void Release_ThenRent_ReissuesTheReleasedId()
    {
        var pool = new FreeIdPool();
        pool.Rent();
        var toRelease = pool.Rent();
        pool.Rent();

        pool.Release(toRelease);
        var reissued = pool.Rent();

        Assert.AreEqual(toRelease, reissued);
    }

    [TestMethod]
    public void Release_NotIssued_IsANoOp()
    {
        var pool = new FreeIdPool();

        pool.Release(0);

        Assert.AreEqual(0, pool.Count);
        Assert.AreEqual(-1, pool.HighestIssuedId);
    }

    /// <summary>Regression guard for the no-op: a redundant second Release must not push the id onto the free stack twice, which would otherwise let two separate Rent() calls hand out the same id to two different live entities.</summary>
    [TestMethod]
    public void Release_Twice_IsANoOpOnSecondRelease()
    {
        var pool = new FreeIdPool();
        var id = pool.Rent();
        pool.Release(id);

        pool.Release(id);
        var reissued = pool.Rent();
        var next = pool.Rent();

        Assert.AreEqual(id, reissued);
        Assert.AreNotEqual(id, next);
    }

    [TestMethod]
    public void IsIssued_ReflectsRentAndRelease()
    {
        var pool = new FreeIdPool();
        var id = pool.Rent();

        Assert.IsTrue(pool.IsIssued(id));

        pool.Release(id);

        Assert.IsFalse(pool.IsIssued(id));
    }

    [TestMethod]
    public void Count_DoesNotGrowWhenChurningWithinPeak()
    {
        var pool = new FreeIdPool();
        var a = pool.Rent();
        var b = pool.Rent();
        pool.Release(a);
        pool.Release(b);

        for (var i = 0; i < 100; i++)
        {
            var id = pool.Rent();
            pool.Release(id);
        }

        // Repeated create/destroy of the same 2 concurrent ids should never mint a new
        // high-water id beyond what was already issued.
        Assert.AreEqual(1, pool.HighestIssuedId);
    }
}