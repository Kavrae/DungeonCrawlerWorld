using Game.Modules.Actions;
using Presentation.UI;

namespace Tests.Presentation;

[TestClass]
public sealed class ActivationBlockerTextTests
{
    [TestMethod]
    public void EveryBlocker_HasTextOfItsOwn_AndNoneHasNone()
    {
        var blockers = Enum.GetValues<ActivationBlocker>().Where(static blocker => blocker != ActivationBlocker.None).ToArray();
        var texts = blockers.Select(ActivationBlockerText.Describe).ToArray();

        Assert.AreEqual(string.Empty, ActivationBlockerText.Describe(ActivationBlocker.None));
        Assert.DoesNotContain(string.Empty, texts);
        Assert.HasCount(blockers.Length, texts.Distinct());
    }
}
