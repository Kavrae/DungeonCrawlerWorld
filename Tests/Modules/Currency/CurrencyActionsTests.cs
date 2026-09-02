using Engine.ECS.Components;
using Game.Modules.Currency;
using Game.Modules.Currency.Components;

namespace Tests.Modules.Currency;

[TestClass]
public sealed class CurrencyActionsTests
{
    private static ComponentManager CreateRegisteredManager()
    {
        var manager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 4);
        new CurrencyModule().RegisterComponents(manager);
        return manager;
    }

    [TestMethod]
    public void TryTransfer_SameEntity_ReturnsFalseAndDoesNotChangeBalance()
    {
        var manager = CreateRegisteredManager();
        manager.Merge(0, new CurrencyComponent(gold: 5, credits: 0));

        var result = CurrencyActions.TryTransfer(manager, sourceEntityId: 0, destinationEntityId: 0, CurrencyType.Gold);

        Assert.IsFalse(result);
        Assert.AreEqual(5, manager.GetPackedPool<CurrencyComponent>().GetReadonly(0).Gold);
    }

    [TestMethod]
    public void TryTransfer_ZeroBalance_ReturnsFalse()
    {
        var manager = CreateRegisteredManager();
        manager.Merge(0, new CurrencyComponent(gold: 0, credits: 3));

        var result = CurrencyActions.TryTransfer(manager, sourceEntityId: 0, destinationEntityId: 1, CurrencyType.Gold);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void TryTransfer_Gold_NonzeroBalance_MovesEntireBalanceAndZeroesSource()
    {
        var manager = CreateRegisteredManager();
        manager.Merge(0, new CurrencyComponent(gold: 7, credits: 2));
        manager.Merge(1, new CurrencyComponent(gold: 3, credits: 1));

        var result = CurrencyActions.TryTransfer(manager, sourceEntityId: 0, destinationEntityId: 1, CurrencyType.Gold);

        Assert.IsTrue(result);
        var pool = manager.GetPackedPool<CurrencyComponent>();
        var source = pool.GetReadonly(0);
        var destination = pool.GetReadonly(1);
        Assert.AreEqual(0, source.Gold);
        Assert.AreEqual(2, source.Credits, "Credits must be untouched by a Gold-only transfer.");
        Assert.AreEqual(10, destination.Gold, "Destination's prior Gold balance must be added to, not overwritten.");
        Assert.AreEqual(1, destination.Credits, "Destination's prior Credits balance must survive a Gold-only transfer.");
    }

    [TestMethod]
    public void TryTransfer_Credits_NonzeroBalance_MovesEntireBalanceAndZeroesSource()
    {
        var manager = CreateRegisteredManager();
        manager.Merge(0, new CurrencyComponent(gold: 4, credits: 6));
        manager.Merge(1, new CurrencyComponent(gold: 1, credits: 2));

        var result = CurrencyActions.TryTransfer(manager, sourceEntityId: 0, destinationEntityId: 1, CurrencyType.Credits);

        Assert.IsTrue(result);
        var pool = manager.GetPackedPool<CurrencyComponent>();
        var source = pool.GetReadonly(0);
        var destination = pool.GetReadonly(1);
        Assert.AreEqual(4, source.Gold, "Gold must be untouched by a Credits-only transfer.");
        Assert.AreEqual(0, source.Credits);
        Assert.AreEqual(1, destination.Gold);
        Assert.AreEqual(8, destination.Credits);
    }

    [TestMethod]
    public void TryTransferAll_MovesBothCurrenciesInOneCall()
    {
        var manager = CreateRegisteredManager();
        manager.Merge(0, new CurrencyComponent(gold: 5, credits: 2));

        var result = CurrencyActions.TryTransferAll(manager, sourceEntityId: 0, destinationEntityId: 1);

        Assert.IsTrue(result);
        var pool = manager.GetPackedPool<CurrencyComponent>();
        var source = pool.GetReadonly(0);
        var destination = pool.GetReadonly(1);
        Assert.AreEqual(0, source.Gold);
        Assert.AreEqual(0, source.Credits);
        Assert.AreEqual(5, destination.Gold);
        Assert.AreEqual(2, destination.Credits);
    }

    [TestMethod]
    public void TryTransferAll_NothingToTransfer_ReturnsFalse()
    {
        var manager = CreateRegisteredManager();
        manager.Merge(0, new CurrencyComponent(gold: 0, credits: 0));

        var result = CurrencyActions.TryTransferAll(manager, sourceEntityId: 0, destinationEntityId: 1);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void TryTransfer_SourceHasNoComponentYet_ReturnsFalse()
    {
        var manager = CreateRegisteredManager();

        var result = CurrencyActions.TryTransfer(manager, sourceEntityId: 0, destinationEntityId: 1, CurrencyType.Gold);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void TryTransfer_ExactAmount_MovesOnlyThatAmountLeavingTheRemainderOnSource()
    {
        var manager = CreateRegisteredManager();
        manager.Merge(0, new CurrencyComponent(gold: 10, credits: 0));
        manager.Merge(1, new CurrencyComponent(gold: 2, credits: 0));

        var result = CurrencyActions.TryTransfer(manager, sourceEntityId: 0, destinationEntityId: 1, CurrencyType.Gold, amount: 7);

        Assert.IsTrue(result);
        var pool = manager.GetPackedPool<CurrencyComponent>();
        Assert.AreEqual(3, pool.GetReadonly(0).Gold, "Source should keep the balance it didn't hand over.");
        Assert.AreEqual(9, pool.GetReadonly(1).Gold);
    }

    [TestMethod]
    public void TryTransfer_ExactAmount_InsufficientBalance_ReturnsFalseAndChangesNothing()
    {
        var manager = CreateRegisteredManager();
        manager.Merge(0, new CurrencyComponent(gold: 5, credits: 0));
        manager.Merge(1, new CurrencyComponent(gold: 2, credits: 0));

        var result = CurrencyActions.TryTransfer(manager, sourceEntityId: 0, destinationEntityId: 1, CurrencyType.Gold, amount: 6);

        Assert.IsFalse(result);
        var pool = manager.GetPackedPool<CurrencyComponent>();
        Assert.AreEqual(5, pool.GetReadonly(0).Gold);
        Assert.AreEqual(2, pool.GetReadonly(1).Gold);
    }

    [TestMethod]
    public void TryTransfer_ExactAmount_ZeroOrNegative_IsANoOpThatReturnsTrue()
    {
        var manager = CreateRegisteredManager();
        manager.Merge(0, new CurrencyComponent(gold: 5, credits: 0));

        Assert.IsTrue(CurrencyActions.TryTransfer(manager, sourceEntityId: 0, destinationEntityId: 1, CurrencyType.Gold, amount: 0));
        Assert.IsTrue(CurrencyActions.TryTransfer(manager, sourceEntityId: 0, destinationEntityId: 1, CurrencyType.Gold, amount: -3));
        Assert.AreEqual(5, manager.GetPackedPool<CurrencyComponent>().GetReadonly(0).Gold, "A zero/negative-amount transfer must not touch either balance.");
    }

    [TestMethod]
    public void TryTransfer_ExactAmount_SameEntity_ReturnsFalse()
    {
        var manager = CreateRegisteredManager();
        manager.Merge(0, new CurrencyComponent(gold: 5, credits: 0));

        var result = CurrencyActions.TryTransfer(manager, sourceEntityId: 0, destinationEntityId: 0, CurrencyType.Gold, amount: 1);

        Assert.IsFalse(result);
    }
}
