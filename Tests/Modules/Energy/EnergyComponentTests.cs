using Game.Modules.Energy.Components;

namespace Tests.Modules.Energy;

[TestClass]
public sealed class EnergyComponentTests
{
    [TestMethod]
    public void ToString_ValidMaximumEnergy_ReturnsPercentageBar()
    {
        var component = new EnergyComponent(currentEnergy: 50, energyRecharge: 5, maximumEnergy: 100);

        Assert.Contains("E", component.ToString());
    }

    /// <summary>
    /// Regression test: ToString used to call StringUtility.BuildPercentageBar unconditionally,
    /// which throws for maximumValue &lt;= 0 -- including default(EnergyComponent), which has
    /// every field zeroed. The debug inspector calls ToString on whatever a selected entity
    /// actually has, so this must degrade gracefully rather than crash that UI.
    /// </summary>
    [TestMethod]
    public void ToString_ZeroMaximumEnergy_DoesNotThrow()
    {
        var component = default(EnergyComponent);

        var text = component.ToString();

        Assert.Contains("invalid", text);
    }

    [TestMethod]
    public void ToString_NegativeMaximumEnergy_DoesNotThrow()
    {
        var component = new EnergyComponent(currentEnergy: 0, energyRecharge: 0, maximumEnergy: -5);

        var text = component.ToString();

        Assert.Contains("invalid", text);
    }
}