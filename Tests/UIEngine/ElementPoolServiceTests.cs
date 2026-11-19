using Microsoft.Xna.Framework;
using UIEngine.UI;

namespace Tests.UIEngine;

[TestClass]
[DoNotParallelize]
public sealed class ElementPoolServiceTests
{
    private sealed class DerivedTextWindow(ElementServices services)
        : TextWindow(services);

    private static ElementOptions FixedOptions() => new()
    {
        Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(100, 40), DisplayMode = ElementDisplayMode.Fixed },
    };

    private static ElementOptions ContainerOptions() => new()
    {
        Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
        Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(100, 40), DisplayMode = ElementDisplayMode.Fixed },
    };

    [TestMethod]
    public void AddChild_ToAnElementThatCantContainChildren_ReturnsTheChildToItsPool()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var parent = pool.CreateElement<Window>(null, FixedOptions());
        var child = pool.CreateElement<Window>(parent, FixedOptions());

        parent.AddChild(child);

        Assert.IsEmpty(parent.ChildElements);
        Assert.AreEqual(1, child.PoolReturnCount);
        Assert.AreSame(child, pool.CreateElement<Window>(null, FixedOptions()), "The refused child is handed out again.");
    }

    [TestMethod]
    public void RegisterFactory_SecondFactoryForOneType_Throws()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => pool.RegisterFactory(() => new Window(TestElementServices.For(pool))));

        Assert.Contains(nameof(Window), exception.Message);
    }

    [TestMethod]
    public void ReplaceFactory_NoElementTheOldFactoryMade_IsHandedOutAgain()
    {
        var pool = new ElementPoolService();
        var made = new List<(TextWindow Window, int Factory)>();
        pool.RegisterFactory(() => Made(1));
        var pooledBefore = pool.CreateElement<TextWindow>(null, FixedOptions());
        var inUseBefore = pool.CreateElement<TextWindow>(null, FixedOptions());
        pool.CloseElement(pooledBefore);

        pool.ReplaceFactory(() => Made(2));
        pool.CloseElement(inUseBefore);
        var first = pool.CreateElement<TextWindow>(null, FixedOptions());
        var second = pool.CreateElement<TextWindow>(null, FixedOptions());

        Assert.AreNotSame(pooledBefore, first);
        Assert.AreNotSame(inUseBefore, first);
        Assert.AreNotSame(pooledBefore, second);
        Assert.AreNotSame(inUseBefore, second);
        Assert.AreEqual(2, made.Single(entry => ReferenceEquals(entry.Window, first)).Factory);

        TextWindow Made(int factory)
        {
            var window = new TextWindow(TestElementServices.For(pool));
            made.Add((window, factory));
            return window;
        }
    }

    [TestMethod]
    public void ReplaceFactory_ForATypeWithNone_Throws()
    {
        var pool = new ElementPoolService();

        Assert.ThrowsExactly<InvalidOperationException>(() => pool.ReplaceFactory(() => new TextWindow(TestElementServices.For(pool))));
    }

    [TestMethod]
    public void CreateElement_FactoryMakingASubclass_ThrowsNamingBoth()
    {
        var pool = new ElementPoolService();
        pool.RegisterFactory<TextWindow>(() => new DerivedTextWindow(TestElementServices.For(pool)));

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => pool.CreateElement<TextWindow>(null, FixedOptions()));

        Assert.Contains(nameof(DerivedTextWindow), exception.Message);
    }

    [TestMethod]
    public void CloseElement_TypeWithNoPool_IsTornDownAgainOnALaterClose()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var unpooledWindow = new DerivedTextWindow(TestElementServices.For(pool));
        unpooledWindow.Build(null, ContainerOptions());
        unpooledWindow.AddChild(pool.CreateElement<Window>(unpooledWindow, FixedOptions()));
        pool.CloseElement(unpooledWindow);

        unpooledWindow.Build(null, ContainerOptions());
        unpooledWindow.AddChild(pool.CreateElement<Window>(unpooledWindow, FixedOptions()));
        pool.CloseElement(unpooledWindow);

        Assert.IsEmpty(unpooledWindow.ChildElements, "Not pooled, so not remembered as closed: the second close closes its new child too.");
    }

    [TestMethod]
    public void CloseElement_Twice_ReturnsItToItsPoolOnce()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var window = pool.CreateElement<Window>(null, FixedOptions());

        pool.CloseElement(window);
        pool.CloseElement(window);

        var first = pool.CreateElement<Window>(null, FixedOptions());
        var second = pool.CreateElement<Window>(null, FixedOptions());
        Assert.AreNotSame(first, second);
    }

    [TestMethod]
    public void Close_ReachedAgainFromItsOwnClosedHandler_NeverPoolsAWindowRentedInThatHandler()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var closingWindow = pool.CreateElement<Window>(null, FixedOptions());
        Window? replacementWindow = null;
        closingWindow.Closed += closed =>
        {
            closed.Close();
            replacementWindow = pool.CreateElement<Window>(null, FixedOptions());
        };

        closingWindow.Close();

        Assert.IsNotNull(replacementWindow);
        Assert.IsTrue(replacementWindow.IsVisible, "The window rented while the first was closing must still be live after that close finishes.");
        Assert.AreNotSame(replacementWindow, pool.CreateElement<Window>(null, FixedOptions()), "The live replacement must not have been returned to the pool.");
    }
}
