namespace Game.Modules.NpcBehavior.Components;

/// <summary>Pure marker -- identifies an entity built by TestDummyBlueprint, so TestDummyAttackSystem knows which entities to drive without a race/name check.</summary>
public struct TestDummyComponent
{
    public override readonly string ToString() => "TestDummy";
}
