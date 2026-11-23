namespace Workshop.Tests;

[TestClass]
public static class AssemblySetup
{
    [AssemblyInitialize]
    public static void SetUp(TestContext context) => global::Game.GameFrameRate.Apply();
}
