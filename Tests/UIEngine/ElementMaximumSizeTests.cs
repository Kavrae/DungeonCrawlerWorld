using Microsoft.Xna.Framework;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Tests.UIEngine;

[TestClass]
[DoNotParallelize]
public sealed class ElementMaximumSizeTests
{
    private static Window CreateRoot(ElementPoolService pool, Vector2 size, bool canUserScrollVertical = false)
    {
        var root = pool.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = size, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { CanUserScrollVertical = canUserScrollVertical },
        });
        root.Initialize();
        return root;
    }

    private static Window AddFixedChild(ElementPoolService pool, Window parent, Vector2 size, Vector2? maximumSize)
    {
        var child = pool.CreateElement<Window>(parent, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = size, MaximumSize = maximumSize, DisplayMode = ElementDisplayMode.Fixed },
        });
        parent.AddChild(child);
        return child;
    }

    [TestMethod]
    public void AnAuthoredMaximumSize_SurvivesEveryMeasure_AndStillCapsTheElement()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var parent = CreateRoot(pool, new Vector2(400, 400));
        var child = AddFixedChild(pool, parent, new Vector2(100, 100), new Vector2(150, 120));

        parent.SetSize(new Vector2(380, 380));
        child.SetSize(new Vector2(500, 500));

        Assert.AreEqual(new Vector2(150, 120), child.MaximumSize);
        Assert.AreEqual(new Vector2(150, 120), child.CurrentSize);
    }

    [TestMethod]
    public void AChild_IsStillBoundedByTheSpaceItsParentOffers()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var parent = CreateRoot(pool, new Vector2(200, 200));
        var child = AddFixedChild(pool, parent, new Vector2(500, 500), maximumSize: null);

        Assert.AreEqual(parent.ContentSize, child.CurrentSize);
    }

    [TestMethod]
    public void AChildOfAScrollingParent_KeepsItsAuthoredSizeOnTheScrollingAxis()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var parent = CreateRoot(pool, new Vector2(200, 100), canUserScrollVertical: true);
        var child = AddFixedChild(pool, parent, new Vector2(100, 400), maximumSize: null);

        Assert.AreEqual(400f, child.CurrentSize.Y);
    }

    [TestMethod]
    public void ARootWithNoAuthoredCap_IsCappedAtTheScreen_NotAtItsStartingSize()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var root = CreateRoot(pool, new Vector2(200, 100));

        root.SetSize(new Vector2(600, 500));

        Assert.AreEqual(UiRenderService.NoScreenSize, root.MaximumSize, "With no graphics device there is no screen to cap at.");
        Assert.AreEqual(new Vector2(600, 500), root.CurrentSize);
    }

    [TestMethod]
    public void AWrapContentTextWindowChild_IsOfferedThePlaceLeftAfterItsPosition_SubtractedOnce()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var parent = CreateRoot(pool, new Vector2(300, 200));
        var textWindow = pool.CreateElement<TextWindow>(parent, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(20, 30), DisplayMode = ElementDisplayMode.WrapContent },
        });
        textWindow.OriginalText = string.Concat(Enumerable.Repeat("line\n", 50));
        parent.AddChild(textWindow);

        Assert.AreEqual(parent.ContentSize - new Vector2(20, 30), textWindow.EffectiveMaximumSize);
        Assert.AreEqual(parent.ContentSize.Y - 30, textWindow.CurrentSize.Y);
    }
}
