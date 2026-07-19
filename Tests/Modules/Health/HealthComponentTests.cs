using Game.Modules.Health.Components;

namespace Tests.Modules.Health;

[TestClass]
public sealed class HealthComponentTests
{
    [TestMethod]
    public void ToString_ValidMaximumHealth_ReturnsPercentageBar()
    {
        var component = new HealthComponent(currentHealth: 50, healthRegen: 5, maximumHealth: 100);

        StringAssert.Contains(component.ToString(), "HP");
    }

    /// <summary>
    /// Regression test: ToString used to call StringUtility.BuildPercentageBar unconditionally,
    /// which throws for maximumValue &lt;= 0 -- including default(HealthComponent), which has
    /// every field zeroed. The debug inspector calls ToString on whatever a selected entity
    /// actually has, so this must degrade gracefully rather than crash that UI.
    /// </summary>
    [TestMethod]
    public void ToString_ZeroMaximumHealth_DoesNotThrow()
    {
        var component = default(HealthComponent);

        var text = component.ToString();

        StringAssert.Contains(text, "invalid");
    }

    [TestMethod]
    public void ToString_NegativeMaximumHealth_DoesNotThrow()
    {
        var component = new HealthComponent(currentHealth: 0, healthRegen: 0, maximumHealth: -5);

        var text = component.ToString();

        StringAssert.Contains(text, "invalid");
    }
}
