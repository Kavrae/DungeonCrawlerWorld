using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace UIEngine.UI;

/// <summary>What makes a control pressable: hovered and pressed as one control, clicked, disabled as a whole, and activated from the keyboard while focused.</summary>
/// <remarks>
/// What it shows is its children, laid out by its tiling like any element's (Floating unless built otherwise), with no
/// padding of their own. UiInputController hovers and presses the nearest pressable above whatever was hit, so a label
/// or icon inside one lights and presses it, and a click on a child is handled by it unless the child handles the click
/// itself. A disabled pressable is never hovered or pressed, and none of its children take the pointer. Button (an
/// action) and Toggle (a choice) build on it.
/// </remarks>
public abstract class PressableElement(ElementServices services) : Element(services)
{
    /// <summary>Whether the pointer is over this control or anything inside it; set by UiInputController.</summary>
    public bool IsHovered { get; set; }

    /// <summary>Whether the left button went down on this control and is still held; set through SetPressed by UiInputController.</summary>
    public bool IsPressed { get; private set; }

    protected override bool IsHitTestable => base.IsHitTestable && Enabled;

    public override void Build(Element? parent, ElementOptions options)
    {
        base.Build(parent, options);

        _canContainChildren = true;
        ContentPadding = Vector2.Zero;
        IsHovered = false;
        IsPressed = false;
    }

    /// <summary>Shows this control pressed in or released.</summary>
    public void SetPressed(bool isPressed)
    {
        IsPressed = isPressed;
        OnPressedChanged();
    }

    /// <summary>No-op by default; Button sinks its bevel while pressed.</summary>
    protected virtual void OnPressedChanged() { }

    /// <summary>Space activates a focused, enabled control.</summary>
    protected override void OnKeyPressAction(Keys key)
    {
        if (key == Keys.Space && Enabled)
        {
            OnActivated();
        }
    }

    /// <summary>What the keyboard activation does; nothing by default, since a Button never takes focus.</summary>
    protected virtual void OnActivated() { }

    /// <summary>Whether the nearest pressable control holding element is disabled; false when none holds it.</summary>
    /// <remarks>How content drawn inside a control (a label, an icon) knows to draw itself disabled.</remarks>
    public static bool IsHeldByDisabledControl(Element element)
    {
        for (var ancestor = element.ParentElement; ancestor is not null; ancestor = ancestor.ParentElement)
        {
            if (ancestor is PressableElement pressable)
            {
                return !pressable.Enabled;
            }
        }

        return false;
    }
}
