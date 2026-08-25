using Game.Modules.Health.Components;

namespace Tests.Modules.Health;

[TestClass]
public sealed class SimpleHealthComponentTests
{
    [TestMethod]
    public void ToString_ValidMaximumHealth_ReturnsPercentageBar()
    {
        var component = new SimpleHealthComponent(currentHealth: 50, maximumHealth: 100);

        Assert.Contains("HP", component.ToString());
    }

    /// <summary>
    /// Regression test: ToString used to call StringUtility.BuildPercentageBar unconditionally,
    /// which throws for maximumValue &lt;= 0 -- including default(SimpleHealthComponent), which has
    /// every field zeroed. The debug inspector calls ToString on whatever a selected entity
    /// actually has, so this must degrade gracefully rather than crash that UI.
    /// </summary>
    [TestMethod]
    public void ToString_ZeroMaximumHealth_DoesNotThrow()
    {
        var component = default(SimpleHealthComponent);

        var text = component.ToString();

        Assert.Contains("invalid", text);
    }

    [TestMethod]
    public void ToString_NegativeMaximumHealth_DoesNotThrow()
    {
        var component = new SimpleHealthComponent(currentHealth: 0, maximumHealth: -5);

        var text = component.ToString();

        Assert.Contains("invalid", text);
    }
}
