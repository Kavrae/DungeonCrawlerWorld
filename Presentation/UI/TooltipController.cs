using Microsoft.Xna.Framework;
using Presentation.UI.Chrome;

namespace Presentation.UI;

/// <summary>
/// Owns the single shared Tooltip instance every hover-popup consumer in the codebase now shows/
/// hides through, instead of each owning a private Tooltip and self-polling Mouse.GetState()
/// independently -- see TODO.md's "Consolidate all tooltips into a single global tooltip" entry.
/// Consumers keep deciding *for themselves*, on their own schedule, when to call Show/Hide (this
/// class does no polling or delay-gating of its own) -- the only thing this adds is an ownership
/// check on Hide, which is what makes the previous stomping race structurally impossible: sharing
/// one Tooltip across two independently-polling consumers used to mean whichever one's Update ran
/// later in a frame won, since an unrelated consumer's routine "nothing hovered" Hide() call had no
/// way to know it wasn't the one currently showing something. Now it does.
/// </summary>
public sealed class TooltipController
{
    private Tooltip _tooltip = null!;
    private object? _currentOwner;

    public void Initialize(ElementPoolService elementPoolService, UiLayerStack layers)
    {
        _tooltip = elementPoolService.CreateElement<Tooltip>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, MaximumSize = PopupChrome.HoverPopupMaximumSize, DisplayMode = ElementDisplayMode.WrapContent, IsVisible = false },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, CanUserFocus = false, CanUserClose = false },
        });
        _tooltip.Initialize();
        layers.Add(UiLayer.Tooltip, _tooltip);
    }

    /// <summary>
    /// Shows (or updates) the shared tooltip on owner's behalf, taking ownership of it -- always
    /// accepted regardless of who (if anyone) currently owns it, since a genuine new Show always
    /// means the previous owner's own content is no longer the right thing to display. maximumSize
    /// is required, not defaulted, precisely because this Tooltip is now shared across consumers
    /// that each want a different cap (e.g. HotbarController's fixed HotbarContent.SummaryWidth vs.
    /// every grid/ability consumer's shared PopupChrome.HoverPopupMaximumSize) -- an implicit
    /// "whatever it was already set to" default would silently leak one consumer's sizing into
    /// another's call.
    /// </summary>
    public void Show(object owner, Rectangle target, PopupAnchor anchor, Vector2 gap, Vector2 maximumSize, string bodyText, string? titleText = null, IReadOnlyList<TooltipRow>? rows = null, bool useFixedWidth = false)
    {
        _currentOwner = owner;
        _tooltip.SetMaximumSize(maximumSize);
        _tooltip.UseFixedWidth = useFixedWidth;
        _tooltip.ShowNear(target, anchor, gap, bodyText, titleText, rows);
    }

    /// <summary>
    /// No-op unless owner is the one currently showing -- this single check is what makes the
    /// stomping race structurally impossible. An unrelated consumer's own routine "nothing hovered"
    /// Hide() call can never clear a different consumer's still-active tooltip.
    /// </summary>
    public void Hide(object owner)
    {
        if (!ReferenceEquals(owner, _currentOwner))
        {
            return;
        }

        _currentOwner = null;
        _tooltip.Hide();
    }
}
