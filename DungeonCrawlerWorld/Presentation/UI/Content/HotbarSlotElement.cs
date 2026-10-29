using Game.Modules.Actions;
using Microsoft.Xna.Framework;
using Presentation.Input.DragDrop;
using UIEngine.Fonts;
using UIEngine.Input;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Presentation.UI.Content;

/// <summary>One hotbar slot, as an element of its own: hovered, pressed, clicked, dragged from and dropped on through UiInputController like any other element.</summary>
/// <remarks>
/// Created and laid out by HotbarContent, which keeps what a slot shows and does -- its binding, whether it is usable,
/// its drawing -- so every slot reads the same per-frame state. A click is a tap on the slot (the controller has already
/// told a tap from a drag); dragging picks up what the slot holds; a drop lands on HotbarSlotDropTarget.
/// </remarks>
public sealed class HotbarSlotElement(FontService fontService, ElementPoolService elementPoolService, UiRenderService renderService)
    : Element(fontService, elementPoolService, renderService), IContentDragSource, IContentDropTarget
{
    private HotbarContent _hotbar = null!;
    private HotbarSlotDropTarget _dropTarget = null!;

    public HotkeySlot Slot { get; private set; }

    /// <summary>Whether the slot holds an action or item stack that resolves, as of the hotbar's last Update.</summary>
    public bool IsBound => _hotbar.IsSlotBound(Slot);

    /// <summary>Whether the slot is usable, as of the hotbar's last Update -- false for a locked slot or a blocked binding.</summary>
    public bool IsActive => _hotbar.IsSlotActive(Slot);

    public void Configure(HotbarContent hotbar, HotkeySlot slot)
    {
        _hotbar = hotbar;
        Slot = slot;
        _dropTarget = new HotbarSlotDropTarget(hotbar, slot);
    }

    public override void DrawContent(GameTime gameTime) => _hotbar.DrawSlot(Render.Batch, Render.UnitRectangle, Slot, LocalContentRectangle);

    protected override bool OnContentClickAction(Point mousePosition)
    {
        _hotbar.ClickSlot(Slot);
        return true;
    }

    /// <summary>A right tap on a bound slot opens its menu; an empty one passes the tap up.</summary>
    protected override bool OnRightClickTapAction(Point mousePosition)
    {
        if (!IsBound)
        {
            return false;
        }

        _hotbar.RightClickSlot(Slot, mousePosition);
        return true;
    }

    public bool TryBeginContentDrag(Point pressPosition, out ContentDragStart start) => _hotbar.TryBeginSlotDrag(Slot, out start);

    public object? DropTargetFor(object payload, Point position) => _dropTarget;
}
