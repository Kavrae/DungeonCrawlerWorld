using Presentation.Rendering;

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
        Assert.AreEqual(expected, ResourceBarValueText.DisplayedHealth(currentHealth));

    [TestMethod]
    [DataRow(2.7f, 2)]
    [DataRow(3f, 3)]
    [DataRow(0.99f, 0)]
    public void DisplayedMana_RoundsDown(float currentMana, int expected) =>
        Assert.AreEqual(expected, ResourceBarValueText.DisplayedMana(currentMana));

    [TestMethod]
    [DataRow(20.4f, 20)]
    [DataRow(20.5f, 21)]
    [DataRow(21.5f, 22)]
    public void DisplayedMaximum_RoundsToNearestWithMidpointsUp(float effectiveMaximum, int expected) =>
        Assert.AreEqual(expected, ResourceBarValueText.DisplayedMaximum(effectiveMaximum));

    [TestMethod]
    public void Update_FormatsCurrentOverMaximum() =>
        Assert.AreEqual("15 / 20", new ResourceBarValueText().Update(15, 20));

    [TestMethod]
    public void Update_CurrentAboveMaximum_ClampedToMaximum() =>
        Assert.AreEqual("20 / 20", new ResourceBarValueText().Update(21, 20));

    [TestMethod]
    public void Update_NegativeCurrent_ClampedToZero() =>
        Assert.AreEqual("0 / 20", new ResourceBarValueText().Update(-1, 20));

    [TestMethod]
    public void Update_SamePair_ReturnsSameStringInstance()
    {
        var valueText = new ResourceBarValueText();

        var first = valueText.Update(15, 20);
        var second = valueText.Update(15, 20);

        Assert.AreSame(first, second);
    }

    [TestMethod]
    public void Update_ChangedPair_ReturnsNewText()
    {
        var valueText = new ResourceBarValueText();
        valueText.Update(15, 20);

        Assert.AreEqual("14 / 20", valueText.Update(14, 20));
        Assert.AreEqual("14 / 21", valueText.Update(14, 21));
    }
}
