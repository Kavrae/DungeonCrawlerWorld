using Presentation.UI.Content;

namespace Tests.Presentation;

/// <summary>
/// HotbarContent's Draw/Update logic needs a real GraphicsDevice to exercise (SpriteBatch.Draw,
/// RadialFillRenderer) -- the same reason ActionLockContent/PlayerHealthBarContent/
/// PlayerStatusEffectsContent have no test coverage either; verified by running the game
/// instead (see CLAUDE.md's UI-change rule). Size is the one piece of this class that's pure
/// arithmetic with no rendering dependency, so it's worth covering directly.
/// </summary>
[TestClass]
public sealed class HotbarContentTests
{
    [TestMethod]
    public void Size_AccountsForTenSlotsAcrossThreeVisualGroups()
    {
        // 10 slots total across groups of 2, 3, and 5 (QE / RFV / 12345): (2-1)+(3-1)+(5-1) = 7
        // intra-group gaps, plus 2 gaps between the 3 groups themselves. SlotGap (1) and
        // GroupGap (10) are HotbarContent's own private constants, duplicated here rather than
        // exposed publicly just for this test -- keep in sync if those ever change.
        const float slotGap = 1f;
        const float groupGap = 10f;

        var slotSize = HotbarContent.SlotSize;
        var expectedWidth = 10 * slotSize.X + 7 * slotGap + 2 * groupGap;

        Assert.AreEqual(expectedWidth, HotbarContent.Size.X, 0.01f);
        Assert.AreEqual(slotSize.Y, HotbarContent.Size.Y);
    }
}
