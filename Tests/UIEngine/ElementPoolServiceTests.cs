using Microsoft.Xna.Framework;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Tests.UIEngine;

[TestClass]
[DoNotParallelize]
public sealed class ElementPoolServiceTests
{
    private sealed class DerivedTextWindow(global::UIEngine.Fonts.FontService fontService, ElementPoolService elementPoolService, LabelRenderer labelRenderer)
        : TextWindow(fontService, elementPoolService, labelRenderer);

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
    public void RegisterFactory_SecondFactoryForOneType_Throws()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared, new LabelRenderer());

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => pool.RegisterFactory(() => new Window(TestFonts.Shared, pool, new LabelRenderer())));

        Assert.Contains(nameof(Window), exception.Message);
    }

    [TestMethod]
    public void CreateElement_FactoryMakingASubclass_ThrowsNamingBoth()
    {
        var pool = new ElementPoolService();
        pool.RegisterFactory<TextWindow>(() => new DerivedTextWindow(TestFonts.Shared, pool, new LabelRenderer()));

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => pool.CreateElement<TextWindow>(null, FixedOptions()));

        Assert.Contains(nameof(DerivedTextWindow), exception.Message);
    }

    [TestMethod]
    public void CloseElement_TypeWithNoPool_IsTornDownAgainOnALaterClose()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared, new LabelRenderer());
        var unpooledWindow = new DerivedTextWindow(TestFonts.Shared, pool, new LabelRenderer());
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
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared, new LabelRenderer());
        var window = pool.CreateElement<Window>(null, FixedOptions());

        pool.CloseElement(window);
        pool.CloseElement(window);

        var first = pool.CreateElement<Window>(null, FixedOptions());
        var second = pool.CreateElement<Window>(null, FixedOptions());
        Assert.AreNotSame(first, second);
    }
}
