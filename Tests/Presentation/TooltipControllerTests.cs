using Presentation.UI;

namespace Tests.Presentation;

/// <summary>
/// Covers TooltipController's ownership guard -- the mechanism that makes the old "whichever
/// consumer's Update runs last in a frame wins" stomping race (see TODO.md's "Consolidate all
/// tooltips" entry and PLAN-trade-window.md's own "Fixes since first landed") structurally
/// impossible. The actual arbitration behavior (one owner's Show, then a different owner's Hide,
/// leaves the first owner's tooltip still showing) isn't exercised here the same way TooltipTests
/// doesn't exercise Tooltip.ShowNear directly -- Show ultimately calls Tooltip.ShowNear, whose own
/// SetRelativePosition call reads ElementPoolService.GraphicsDevice.Viewport.Bounds, never wired
/// up headlessly (see TooltipTests' own doc comment). What's covered here instead is the one thing
/// that's both safe to call headlessly and actually load-bearing: Hide checks ownership *before*
/// ever touching the underlying Tooltip, so an unrelated consumer's routine "nothing hovered"
/// Hide() call is a true no-op, not just one that happens to render harmlessly. The full
/// stomping-race fix itself was confirmed live in the trade window (hovering back and forth
/// between its two columns) per this feature's own manual-verification pass.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TooltipControllerTests
{
    [TestMethod]
    public void Hide_NoOwnerHasEverShown_IsANoOpAndNeverTouchesTheUnderlyingTooltip()
    {
        // Deliberately never Initialize()'d -- if Hide touched the underlying Tooltip before
        // checking ownership, this would NullReferenceException instead of returning quietly.
        var controller = new TooltipController();

        controller.Hide(new object());
    }
}
