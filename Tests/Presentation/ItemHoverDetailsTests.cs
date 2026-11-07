using Game.Modules.Inventory;
using Microsoft.Xna.Framework;
using Presentation.UI.Inventory;
using Tests.UIEngine;
using UIEngine.Input;
using UIEngine.UI;
using UIEngine.UI.Content;

namespace Tests.Presentation;

/// <summary>Hovering an item cell names it above the cursor at once, then opens Item Details after DetailsDelayFrames -- unless something keeps the open popup where it is.</summary>
[TestClass]
[DoNotParallelize]
public sealed class ItemHoverDetailsTests
{
    private static readonly ItemHoverTarget Potion = new(1, 7, new ItemDefinition(Guid.NewGuid(), "Test Potion", null, "p", Color.Green, Tags: [], Effects: []));

    private sealed class RecordingPopup : IItemDetailsPopup
    {
        public bool Opens { get; set; } = true;

        public bool OpensFor(in ItemHoverTarget target) => Opens;

        public ItemHoverTarget? Shown { get; private set; }

        public bool IsShowing(in ItemHoverTarget target) => Shown is { } shown && shown.IsSameCellAs(target);

        public void OpenAtCursor(in ItemHoverTarget target, IItemShopPricingSource shopPricing) => Shown = target;

        public void Close() => Shown = null;

        public void Compare(int entityId, uint stackInstanceId, IItemShopPricingSource shopPricing)
        {
        }
    }

    private sealed class NoShopPricing : IItemShopPricingSource
    {
        public IReadOnlyList<TooltipRow>? ShopPricingRowsFor(in ItemHoverTarget target) => null;
    }

    private static (ItemHoverDetails Hover, CursorLabelContent Label, RecordingPopup Popup) Build()
    {
        var label = new CursorLabelContent(new PointerState(), TestFonts.Shared);
        var popup = new RecordingPopup();
        return (new ItemHoverDetails(label) { Popup = popup }, label, popup);
    }

    [TestMethod]
    public void BeforeTheDelay_NamesTheItemAndOpensNothing()
    {
        var (hover, label, popup) = Build();

        hover.Hover(this, Potion, ItemHoverDetails.DetailsDelayFrames - 1, new NoShopPricing());

        Assert.AreEqual("Test Potion", label.ShownText);
        Assert.IsNull(popup.Shown);
    }

    [TestMethod]
    public void AtTheDelay_OpensItemDetailsAndHidesTheName()
    {
        var (hover, label, popup) = Build();

        hover.Hover(this, Potion, ItemHoverDetails.DetailsDelayFrames, new NoShopPricing());

        Assert.AreEqual(Potion, popup.Shown);
        Assert.AreEqual(string.Empty, label.ShownText);
    }

    [TestMethod]
    public void WhenThePopupWontOpenForTheItem_OnlyNamesIt()
    {
        var (hover, label, popup) = Build();
        popup.Opens = false;

        hover.Hover(this, Potion, ItemHoverDetails.DetailsDelayFrames * 2, new NoShopPricing());

        Assert.IsNull(popup.Shown);
        Assert.AreEqual("Test Potion", label.ShownText);
    }

    [TestMethod]
    public void ClosedWhileTheCellIsStillHovered_StaysClosedUntilTheHoverEnds()
    {
        var (hover, _, popup) = Build();
        hover.Hover(this, Potion, ItemHoverDetails.DetailsDelayFrames, new NoShopPricing());
        popup.Close();

        hover.Hover(this, Potion, ItemHoverDetails.DetailsDelayFrames + 1, new NoShopPricing());
        Assert.IsNull(popup.Shown, "Escape closed it with the pointer still on the cell.");

        hover.EndHover(this);
        hover.Hover(this, Potion, ItemHoverDetails.DetailsDelayFrames, new NoShopPricing());
        Assert.AreEqual(Potion, popup.Shown);
    }

    [TestMethod]
    public void AnotherOwnersEndOfHover_LeavesTheNameShown()
    {
        var (hover, label, _) = Build();
        hover.Hover(this, Potion, 1, new NoShopPricing());

        hover.EndHover(new object());

        Assert.AreEqual("Test Potion", label.ShownText);
    }
}
