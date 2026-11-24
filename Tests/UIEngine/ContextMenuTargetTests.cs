using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.UI;

namespace Tests.UIEngine;

/// <summary>A context menu opened for an element is open for it -- what the element highlights from -- until the menu closes, however it closes, or the element is closed.</summary>
[TestClass]
[DoNotParallelize]
public sealed class ContextMenuTargetTests
{
    private static List<DropdownOption> Options(Action? onSelect = null) => [new DropdownOption("Use", null, Enabled: true, onSelect ?? (static () => { }))];

    private static (ControlTestHarness Harness, ContextMenuController Menus, ListView Target, ListView Other) Build()
    {
        var harness = new ControlTestHarness();
        var target = harness.AddRoot<ListView>(new Vector2(100, 100), new Vector2(80, 80));
        var other = harness.AddRoot<ListView>(new Vector2(400, 100), new Vector2(80, 80));
        return (harness, new ContextMenuController(harness.Dropdowns), target, other);
    }

    [TestMethod]
    public void OpenedForAnElement_IsOpenForItAlone()
    {
        var (_, menus, target, other) = Build();

        menus.Open(new Vector2(150, 150), Options(), target);

        Assert.IsTrue(menus.IsOpenFor(target));
        Assert.IsFalse(menus.IsOpenFor(other));
    }

    [TestMethod]
    public void OpenedForNothing_IsOpenForNoElement_AndRetargetDoesNothing()
    {
        var (_, menus, target, _) = Build();

        menus.Open(new Vector2(150, 150), Options());
        menus.Retarget(target);

        Assert.IsFalse(menus.IsOpenFor(target));
    }

    [TestMethod]
    public void ChoosingAnOption_EndsIt()
    {
        var (harness, menus, target, _) = Build();
        var chosen = false;
        menus.Open(new Vector2(150, 150), Options(() => chosen = true), target);

        harness.Click(harness.Dropdowns.Menu.OptionRows[0]!);

        Assert.IsTrue(chosen);
        Assert.IsFalse(menus.IsOpenFor(target));
    }

    [TestMethod]
    public void Escape_EndsIt()
    {
        var (harness, menus, target, _) = Build();
        menus.Open(new Vector2(150, 150), Options(), target);

        harness.Press(Keys.Escape);

        Assert.IsFalse(menus.IsOpenFor(target));
    }

    [TestMethod]
    public void APressOutside_EndsIt_EvenOnTheTarget()
    {
        var (harness, menus, target, _) = Build();
        menus.Open(new Vector2(170, 170), Options(), target);

        harness.Click(new Point(110, 110));

        Assert.IsFalse(menus.IsOpenFor(target));
    }

    [TestMethod]
    public void AListOpenedUnderAnOwner_ReplacesIt()
    {
        var (_, menus, target, other) = Build();
        menus.Open(new Vector2(150, 150), Options(), target);

        menus.Dropdowns.OpenBelow(other, Options());

        Assert.IsFalse(menus.IsOpenFor(target));
        Assert.IsFalse(menus.IsOpenFor(other));
    }

    [TestMethod]
    public void TheTargetClosed_IsNotItEvenWhenItsInstanceIsReused_UntilRetargeted()
    {
        var (harness, menus, target, _) = Build();
        menus.Open(new Vector2(150, 150), Options(), target);

        harness.Pool.CloseElement(target);
        var reused = harness.AddRoot<ListView>(new Vector2(100, 100), new Vector2(80, 80));

        Assert.AreSame(target, reused, "The pool hands the same instance back, as a grid rebuilding its cells gets it.");
        Assert.IsFalse(menus.IsOpenFor(reused));

        menus.Retarget(reused);

        Assert.IsTrue(menus.IsOpenFor(reused));
    }
}
