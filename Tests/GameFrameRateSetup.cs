namespace Tests;

[TestClass]
public static class GameFrameRateSetup
{
    [AssemblyInitialize]
    public static void ApplyGameFrameRate(TestContext context) => global::Game.GameFrameRate.Apply();
}
