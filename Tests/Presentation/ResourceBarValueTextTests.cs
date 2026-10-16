using Presentation.Rendering;
using UIEngine.Rendering;

namespace Tests.Presentation;

[TestClass]
public sealed class ResourceBarValueTextTests
{
    [TestMethod]
    [DataRow(0.3f, 1)]
    [DataRow(0f, 0)]
    [DataRow(14.01f, 15)]
    [DataRow(15f, 15)]
    public void DisplayedHealth_RoundsUp(float currentHealth, int expected) =>
        Assert.AreEqual(expected, ResourceBarRounding.DisplayedHealth(currentHealth));

    [TestMethod]
    [DataRow(2.7f, 2)]
    [DataRow(3f, 3)]
    [DataRow(0.99f, 0)]
    public void DisplayedMana_RoundsDown(float currentMana, int expected) =>
        Assert.AreEqual(expected, ResourceBarRounding.DisplayedMana(currentMana));

    [TestMethod]
    [DataRow(20.4f, 20)]
    [DataRow(20.5f, 21)]
    [DataRow(21.5f, 22)]
    public void DisplayedMaximum_RoundsToNearestWithMidpointsUp(float effectiveMaximum, int expected) =>
        Assert.AreEqual(expected, ResourceBarRounding.DisplayedMaximum(effectiveMaximum));

    [TestMethod]
    public void Format_CurrentOverMaximum() =>
        Assert.AreEqual("15 / 20", ResourceBarValueText.Format(15, 20));

    [TestMethod]
    public void Format_CurrentAboveMaximum_ClampedToMaximum() =>
        Assert.AreEqual("20 / 20", ResourceBarValueText.Format(21, 20));

    [TestMethod]
    public void Format_NegativeCurrent_ClampedToZero() =>
        Assert.AreEqual("0 / 20", ResourceBarValueText.Format(-1, 20));

    [TestMethod]
    public void Format_NegativeMaximum_ClampedToZero() =>
        Assert.AreEqual("0 / 0", ResourceBarValueText.Format(5, -3));
}
