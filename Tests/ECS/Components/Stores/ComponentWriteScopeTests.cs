using Engine.ECS.Components;
using Engine.ECS.Components.Stores;

namespace Tests.ECS.Components.Stores;

[TestClass]
public sealed class ComponentWriteScopeTests
{
    private struct Value
    {
        public int Number { get; set; }
        public string? Label { get; set; }
    }

    private readonly struct NumberMatcher(int number) : IComponentMatcher<Value>
    {
        public bool Matches(in Value value) => value.Number == number;
    }

    private static PackedComponentPool<Value> Packed() =>
        new(16, 4, static (ref existing, incoming) => existing = incoming, ComponentChangeObservation.Observed);

    private static MultiComponentPool<Value> Multi() => new(16, 4, ComponentChangeObservation.Observed);

    private static DirectComponentPool<Value> Direct() => new(16, static (ref existing, incoming) => existing = incoming);

    [TestMethod]
    public void Packed_Write_NotifiesOnceWhenItEnds_WithTheNewValueReadable()
    {
        var pool = Packed();
        pool.Add(3, new Value { Number = 1 });
        var seen = new List<int>();
        pool.ComponentChanged += (entityId, denseIndex) => seen.Add(pool.GetReadonlyByDenseIndex(denseIndex).Number);

        using (var write = pool.Write(3))
        {
            write.Value.Number = 2;
            write.Value.Number = 3;
            Assert.IsEmpty(seen, "Nothing is told until the scope ends.");
        }

        CollectionAssert.AreEqual(new[] { 3 }, seen);
        Assert.AreEqual(2u, pool.GetVersion(3));
    }

    [TestMethod]
    public void Packed_Write_AssignsAReferenceTypedField()
    {
        var pool = Packed();
        pool.Add(3, new Value());

        using (var write = pool.Write(3))
        {
            write.Value.Label = "lit";
        }

        Assert.AreEqual("lit", pool.GetReadonly(3).Label);
    }

    [TestMethod]
    public void Packed_Write_WithoutTheComponent_Throws()
    {
        var pool = Packed();

        Assert.ThrowsExactly<InvalidOperationException>(() =>
        {
            using var write = pool.Write(3);
        });
    }

    [TestMethod]
    public void TryWrite_WithoutTheComponent_IsNotFound_AndTellsNobody()
    {
        var pool = Packed();
        pool.Add(1, new Value());
        var changes = 0;
        pool.ComponentChanged += (_, _) => changes++;

        using (var write = pool.TryWrite(3))
        {
            Assert.IsFalse(write.Found);
        }

        Assert.AreEqual(0, changes);
        pool.Add(3, new Value());
    }

    [TestMethod]
    public void Multi_TryWriteFirst_WritesOnlyTheMatchedInstance_AndBumpsTheEntityVersion()
    {
        var pool = Multi();
        pool.Add(2, new Value { Number = 1 });
        pool.Add(2, new Value { Number = 2 });
        var entityVersion = pool.GetEntityVersion(2);

        using (var write = pool.TryWriteFirst(2, new NumberMatcher(1)))
        {
            Assert.IsTrue(write.Found);
            write.Value.Number = 10;
        }

        Assert.AreEqual(1, pool.CountMatching(2, new NumberMatcher(10)));
        Assert.AreEqual(1, pool.CountMatching(2, new NumberMatcher(2)));
        Assert.AreEqual(entityVersion + 1, pool.GetEntityVersion(2));
    }

    [TestMethod]
    public void Direct_Write_BumpsTheVersion()
    {
        var pool = Direct();
        pool.Add(4, new Value { Number = 1 });

        using (var write = pool.Write(4))
        {
            write.Value.Number = 5;
        }

        Assert.AreEqual(5, pool.GetReadonly(4).Number);
        Assert.AreEqual(2u, pool.GetVersion(4));
    }

    [TestMethod]
    public void Direct_TryWrite_OutOfBoundsOrAbsent_IsNotFound()
    {
        var pool = Direct();

        using (var absent = pool.TryWrite(4))
        {
            Assert.IsFalse(absent.Found);
        }

        using (var outOfBounds = pool.TryWrite(400))
        {
            Assert.IsFalse(outOfBounds.Found);
        }
    }

#if DEBUG
    [TestMethod]
    public void Packed_StructuralChangeWhileAScopeIsOpen_Throws()
    {
        var pool = Packed();
        pool.Add(1, new Value());
        pool.Add(2, new Value());

        using (pool.Write(1))
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => pool.Add(3, new Value()));
            Assert.ThrowsExactly<InvalidOperationException>(() => pool.Remove(2));
            Assert.ThrowsExactly<InvalidOperationException>(() => pool.ReserveDenseCapacity(64));
            Assert.ThrowsExactly<InvalidOperationException>(() => pool.Resize(64));
        }

        pool.Add(3, new Value());
        pool.Remove(2);
    }

    [TestMethod]
    public void Multi_StructuralChangeWhileAScopeIsOpen_Throws()
    {
        var pool = Multi();
        pool.Add(1, new Value { Number = 1 });

        using (pool.TryWriteFirst(1, new NumberMatcher(1)))
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => pool.Add(1, new Value()));
            Assert.ThrowsExactly<InvalidOperationException>(() => pool.RemoveFirst(1, new NumberMatcher(1)));
            Assert.ThrowsExactly<InvalidOperationException>(() => pool.Clear());
        }

        pool.Add(1, new Value());
    }

    [TestMethod]
    public void Direct_RemoveWhileAScopeIsOpen_Throws()
    {
        var pool = Direct();
        pool.Add(1, new Value());

        using (pool.Write(1))
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => pool.Remove(1));
        }

        Assert.IsTrue(pool.Remove(1));
    }

    [TestMethod]
    public void AScopeEndedByAnException_StillCloses()
    {
        var pool = Packed();
        pool.Add(1, new Value());

        Assert.ThrowsExactly<InvalidOperationException>(() =>
        {
            using var write = pool.Write(1);
            throw new InvalidOperationException("inside the block");
        });

        pool.Add(2, new Value());
    }

    [TestMethod]
    public void AnObserverMayChangeThePool_OnceTheScopeHasEnded()
    {
        var pool = Packed();
        pool.Add(1, new Value());
        pool.ComponentChanged += (entityId, _) =>
        {
            if (entityId == 1 && !pool.Has(9))
            {
                pool.Add(9, new Value());
            }
        };

        using (var write = pool.Write(1))
        {
            write.Value.Number = 1;
        }

        Assert.IsTrue(pool.Has(9));
    }
#endif
}
