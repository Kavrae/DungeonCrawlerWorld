using Engine.ECS.Properties;
using Engine.Math;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Actions.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.Inventory.Components;
using Game.Modules.Movement.Components;
using Game.Spawning;

namespace Tests.ComponentProperties;

/// <summary>The generated and hand-written property access over real components: what is read, what a write takes, what it refuses.</summary>
[TestClass]
public sealed class ComponentPropertyAccessTests
{
    [TestMethod]
    public void WritingEveryLeafBackUnchanged_LeavesEveryComponentAsItWas()
    {
        for (var i = 0; i < 8; i++)
        {
            AssertRoundTrips(ComponentPropertySamples.Movement(i));
            AssertRoundTrips(ComponentPropertySamples.Stack(i));
            AssertRoundTrips(ComponentPropertySamples.Dead(i));
            AssertRoundTrips(ComponentPropertySamples.Spawn(i), expectsWrites: false);
            AssertRoundTrips(ComponentPropertySamples.Windup(i));
            AssertRoundTrips(ComponentPropertySamples.BodyParts(i));
        }
    }

    [TestMethod]
    public void ANestedLeaf_IsWrittenThroughItsParent()
    {
        var windup = ComponentPropertySamples.Windup(1);

        Assert.IsTrue(Write(ref windup, [2, 1, 0], ComponentPropertyValue.FromInteger(42)));

        Assert.AreEqual(42, windup.Selection.AimedTile.X);
        Assert.AreEqual(1, windup.Selection.AimedTile.Y);
    }

    [TestMethod]
    public void AnOptional_IsClearedByAbsentAndRefusesALeafWhileEmpty()
    {
        var movement = ComponentPropertySamples.Movement(1);

        Assert.IsTrue(Write(ref movement, [0], ComponentPropertyValue.Absent));
        Assert.IsNull(movement.TargetMapPosition);
        Assert.IsFalse(Write(ref movement, [0, 0], ComponentPropertyValue.FromInteger(3)));
        Assert.IsTrue(Write(ref movement, [1, 0], ComponentPropertyValue.FromInteger(3)));
        Assert.AreEqual(new Vector3Int(3, 1, 0), movement.NextMapPosition);
    }

    [TestMethod]
    public void OutOfRangeUndefinedAndReadOnlyWrites_AreRefusedAndChangeNothing()
    {
        var stack = ComponentPropertySamples.Stack(2);
        var windup = ComponentPropertySamples.Windup(2);
        var transform = new TransformComponent(new Vector3Int(4, 5, 0), new Vector2Byte(1, 1));

        Assert.IsFalse(Write(ref stack, [4], ComponentPropertyValue.FromInteger(70000)), "out of a ushort's range");
        Assert.IsFalse(Write(ref windup, [2, 0], ComponentPropertyValue.FromEnum(9)), "not a TargetingMode");
        Assert.IsFalse(Write(ref stack, [0], ComponentPropertyValue.FromInteger(1)), "ItemId names the stack");
        Assert.IsFalse(Write(ref windup, [1], ComponentPropertyValue.FromText("anything")), "a definition");
        Assert.IsFalse(Write(ref transform, [0, 0], ComponentPropertyValue.FromInteger(9)), "a move is World.MoveEntity's");
        Assert.AreEqual(ComponentPropertySamples.Stack(2).Quantity, stack.Quantity);
        Assert.AreEqual(new Vector3Int(4, 5, 0), transform.Position);
    }

    [TestMethod]
    public void AFlagsValue_TakesDeclaredBitsAndRefusesAnyOther()
    {
        var nonBlocking = new NonBlockingComponent(NonBlockingKind.Tiny);

        Assert.IsTrue(Write(ref nonBlocking, [0], ComponentPropertyValue.FromEnum((long)(NonBlockingKind.Tiny | NonBlockingKind.Phasing))));
        Assert.IsFalse(Write(ref nonBlocking, [0], ComponentPropertyValue.FromEnum(4)));

        Assert.AreEqual(NonBlockingKind.Tiny | NonBlockingKind.Phasing, nonBlocking.Kind);
    }

    [TestMethod]
    public void AReadonlyRecordStruct_IsWrittenWhole()
    {
        var dead = ComponentPropertySamples.Dead(3);

        Assert.IsTrue(Write(ref dead, [1], ComponentPropertyValue.FromInteger(77)));

        Assert.AreEqual(ComponentPropertySamples.Dead(3) with { DiedAtFrame = 77 }, dead);
    }

    [TestMethod]
    public void TheHandWrittenBodyPartTable_ReadsAndWritesRowsByPartId()
    {
        var state = ComponentPropertySamples.BodyParts(0);
        var access = ComponentPropertyAccessOf<BodyPartStateComponent>.Access!;

        Assert.IsTrue(access.Write(ref state, [0, 4], ComponentPropertyValue.FromNumber(1.5)));
        Assert.IsTrue(access.Write(ref state, [2, 5], ComponentPropertyValue.FromBoolean(true)));
        Assert.IsFalse(access.Write(ref state, [0, 16], ComponentPropertyValue.FromNumber(1)));

        Assert.AreEqual(1.5f, state.CurrentHealthOf(4));
        Assert.IsTrue(state.IsDisabled(5));
    }

    [TestMethod]
    public void TheHandWrittenAbilityScoreTable_ReadsAnUngrantedScoreAsAbsentAndWritesOnlyAGrantedBase()
    {
        var scores = new AbilityScoresComponent();
        scores.Set(AbilityScoreType.Dexterity, 12, 15);
        var access = ComponentPropertyAccessOf<AbilityScoresComponent>.Access!;
        var visitor = new RecordingVisitor();

        access.Read(scores, visitor);

        var dexterity = (int)AbilityScoreType.Dexterity;
        Assert.AreEqual(12, visitor.Leaves[dexterity].Value.Integer);
        Assert.IsTrue(visitor.Leaves[(int)AbilityScoreType.Strength].Value.IsAbsent);
        Assert.IsTrue(access.Write(ref scores, [0, dexterity], ComponentPropertyValue.FromInteger(20)));
        Assert.IsFalse(access.Write(ref scores, [0, (int)AbilityScoreType.Strength], ComponentPropertyValue.FromInteger(20)));
        Assert.IsFalse(access.Write(ref scores, [1, dexterity], ComponentPropertyValue.FromInteger(20)));
        Assert.IsTrue(scores.TryGet(AbilityScoreType.Dexterity, out var value));
        Assert.AreEqual(20, value.BaseValue);
        Assert.AreEqual(15, value.Total);
    }

    [TestMethod]
    public void DerivedAndInstanceNamingMembers_AreReadOnlyAndDerivedOnesAreNotSaved()
    {
        var stackTable = ComponentPropertyAccessOf<InventoryItemStackComponent>.Access!.Table;
        var derived = stackTable.Properties.Single(property => property.Name == nameof(InventoryItemStackComponent.ItemDefinitionId));
        var instanceId = stackTable.Properties.Single(property => property.Name == nameof(InventoryItemStackComponent.StackInstanceId));

        Assert.AreEqual(ComponentPropertyFlags.ReadOnly | ComponentPropertyFlags.NotSaved | ComponentPropertyFlags.Derived, derived.Flags);
        Assert.AreEqual(ComponentPropertyFlags.ReadOnly, instanceId.Flags);
        Assert.AreEqual(Game.GamePropertyMeanings.StackInstanceId, instanceId.Meaning.Name);
    }

    private static bool Write<T>(ref T component, int[] path, ComponentPropertyValue value)
        where T : struct =>
        ComponentPropertyAccessOf<T>.Access!.Write(ref component, path, value);

    private static void AssertRoundTrips<T>(T original, bool expectsWrites = true)
        where T : struct
    {
        var access = ComponentPropertyAccessOf<T>.Access!;
        var before = new RecordingVisitor();
        access.Read(original, before);
        var copy = original;
        var writes = 0;
        foreach (var (_, path, property, value, isWritable) in before.Leaves)
        {
            var written = access.Write(ref copy, path, value);
            if (written)
            {
                writes++;
            }

            if (isWritable && !value.IsAbsent)
            {
                Assert.IsTrue(written, $"{typeof(T).Name} refused {string.Join('.', path)} ({property}) = {value}");
            }
        }

        var after = new RecordingVisitor();
        access.Read(copy, after);
        Assert.AreEqual(expectsWrites, writes > 0, typeof(T).Name);
        Assert.HasCount(before.Leaves.Count, after.Leaves);
        for (var leaf = 0; leaf < before.Leaves.Count; leaf++)
        {
            Assert.AreEqual(before.Leaves[leaf].Value.ToString(), after.Leaves[leaf].Value.ToString(), $"{typeof(T).Name} {before.Leaves[leaf].Property}");
        }
    }
}
