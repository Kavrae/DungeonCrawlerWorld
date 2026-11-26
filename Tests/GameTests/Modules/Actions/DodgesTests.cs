using Engine.Math;
using Engine.Tiles;
using Game.Content;
using Game.Effects;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.World;

namespace Tests.GameTests.Modules.Actions;

/// <summary>A dodge is what holds a DodgeActivation, whatever its Guid, and it steps one tile.</summary>
[TestClass]
public sealed class DodgesTests
{
    private static ActionDefinition LongStep(int range) =>
        TestContent.Action(BuiltInActionIds.Dodge) with
        {
            Id = Guid.NewGuid(),
            Name = "Long Step",
            Activator = new DirectAction(new TargetingSpec(TargetShape.SingleTarget, MaximumRange: range, Modes: TargetingModes.GroundOnly), new ActionTiming(ActionTimingCategory.FreeCast)),
        };

    [TestMethod]
    public void Dodge_AndAnotherActionHoldingADodgeActivation_AreDodges_AndAnAttackIsNot()
    {
        Assert.IsTrue(Dodges.IsDodge(TestContent.Action(BuiltInActionIds.Dodge)));
        Assert.IsTrue(Dodges.IsDodge(LongStep(range: 1)));
        Assert.IsFalse(Dodges.IsDodge(TestContent.Action(BuiltInActionIds.QuickAttack)));
        Assert.IsFalse(Dodges.IsDodge(TestActivatables.Action() with { Effects = [Effect.None] }));
    }

    [TestMethod]
    public void ADodgeSteppingFartherThanOneTile_FailsItsChecks_AndOneSteppingOneTileDoesNot()
    {
        var context = BuiltInTestModules.Build(new Map(new Vector3Int(5, 5, 1))).Context;
        var checks = new ContentChecks(context.GameplayTags, context.Auras, context.Terrain, context.StatusEffectAppliers);

        Assert.IsTrue(checks.ProblemsOf(LongStep(range: 2)).Any(problem => problem.Contains("a dodge steps 1 tile", StringComparison.Ordinal)));
        Assert.IsFalse(checks.ProblemsOf(LongStep(range: 1)).Any(problem => problem.Contains("dodge", StringComparison.Ordinal)));
        Assert.IsEmpty(checks.ProblemsOf(TestContent.Action(BuiltInActionIds.Dodge)));
    }
}
