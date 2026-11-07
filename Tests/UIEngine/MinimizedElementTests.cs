using Microsoft.Xna.Framework;
using UIEngine.UI;

namespace Tests.UIEngine;

[TestClass]
[DoNotParallelize]
public sealed class MinimizedElementTests
{
    private sealed class UpdateCountingElement(ElementServices services)
        : Element(services)
    {
        public int UpdateCount { get; private set; }

        public override void Update(GameTime gameTime)
        {
            UpdateCount++;
            base.Update(gameTime);
        }
    }

    private static ElementPoolService CreatePool()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        pool.RegisterFactory(() => new UpdateCountingElement(TestElementServices.For(pool)));
        return pool;
    }

    private static Window CreateTitledWindow(ElementPoolService pool, Window? parent, Vector2 size)
    {
        var window = pool.CreateElement<Window>(parent, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = size, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowTitle = true, ShowTitleWhenMinimized = true, TitleText = "Window" },
        });

        if (parent is null)
        {
            window.Initialize();
        }
        else
        {
            parent.AddChild(window);
        }

        return window;
    }

    [TestMethod]
    public void MinimizingATiledWindow_MovesTheSiblingsAfterIt_AndRestoringMovesThemBack()
    {
        var pool = CreatePool();
        var column = pool.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
            Layout = new ElementLayoutOptions { Size = new Vector2(400, 600), DisplayMode = ElementDisplayMode.Fixed },
        });
        column.Initialize();
        var first = CreateTitledWindow(pool, column, new Vector2(300, 200));
        var second = CreateTitledWindow(pool, column, new Vector2(300, 100));

        first.SetDisplayMode(ElementDisplayMode.Minimized);
        Assert.AreEqual(first.CurrentSize.Y, second.RelativePosition.Y);
        Assert.IsLessThan(200f, second.RelativePosition.Y);

        first.SetDisplayMode(ElementDisplayMode.Fixed);
        Assert.AreEqual(200f, second.RelativePosition.Y);
    }

    [TestMethod]
    public void AMinimizedElementsChildren_AreNotUpdated_UntilItIsRestored()
    {
        var pool = CreatePool();
        var window = CreateTitledWindow(pool, null, new Vector2(300, 200));
        var child = pool.CreateElement<UpdateCountingElement>(window, new ElementOptions { Layout = new ElementLayoutOptions { Size = new Vector2(50, 50) } });
        window.AddChild(child);

        window.SetDisplayMode(ElementDisplayMode.Minimized);
        window.Update(new GameTime());
        Assert.AreEqual(0, child.UpdateCount);

        window.SetDisplayMode(ElementDisplayMode.Fixed);
        window.Update(new GameTime());
        Assert.AreEqual(1, child.UpdateCount);
    }

    [TestMethod]
    public void AMinimizedElementsChildren_AreNeverHit_EvenWhereTheyOverlapWhatIsStillShown()
    {
        var pool = CreatePool();
        var window = CreateTitledWindow(pool, null, new Vector2(300, 200));
        var child = pool.CreateElement<Window>(window, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, -window.HeaderSize.Y), Size = new Vector2(100, 100), DisplayMode = ElementDisplayMode.Fixed },
        });
        window.AddChild(child);
        var pointInTitleBarAndChild = new Point(window.Rectangle.X + 10, window.Rectangle.Y + 4);
        Assert.AreSame(child, window.TryHitTestInteraction(pointInTitleBarAndChild).Element, "Restored, the child is on top.");

        window.SetDisplayMode(ElementDisplayMode.Minimized);

        Assert.AreSame(window, window.TryHitTestInteraction(pointInTitleBarAndChild).Element);
    }
}
