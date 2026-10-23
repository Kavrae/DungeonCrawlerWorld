using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Presentation.UI;

/// <summary>
/// Persistent right-side HUD panel showing whatever MapViewState.InspectionMode currently
/// targets (see InspectionWindowContent) -- Basic (map-tile click) or Detail (context-menu
/// Inspect, follows its target; also carries the always-on Admin component dump -- see
/// MapViewState.InspectionMode's own doc comment). Cannot be closed (CanUserClose = false);
/// minimizing clears the current inspection target and its title, since there's nothing
/// meaningful left to show once minimized. RecalculateMinimizedSize is overridden so minimizing
/// never shrinks the title bar's width the way Window's default does (MinimumHeaderWidth) --
/// this panel's width must always match PlayerHealthBarContent.Size.X (see
/// ShellBootstrapper), restored or not.
/// </summary>
public sealed class InspectionWindow(FontService fontService, ElementPoolService elementPoolService, UiRenderService renderService, MapViewState mapViewState)
    : Window(fontService, elementPoolService, renderService)
{
    /// <summary>Starts minimized -- a HUD-persistent panel with nothing to inspect yet defaults to its collapsed, title-only footprint, the same convention Folder.Initialize already follows.</summary>
    public override void Initialize()
    {
        base.Initialize();

        DisplayModeChanged += OnDisplayModeChanged;

        SetDisplayMode(ElementDisplayMode.Minimized);
    }

    /// <summary>Fires for every DisplayMode change, not just this window's own minimize button -- see MinimizeRestoreBehavior's own doc comment on why it syncs the same way. Only the transition into Minimized clears the inspection target/retitles; restoring (Basic/Detail selection un-minimizing this window) needs no action here.</summary>
    private void OnDisplayModeChanged(Element window)
    {
        if (DisplayMode != ElementDisplayMode.Minimized)
        {
            return;
        }

        mapViewState.InspectionMode = InspectionMode.None;
        mapViewState.InspectedEntityId = -1;
        TitleText = string.Empty;
    }

    /// <summary>Preserves the window's full configured width while minimized -- only Window.MinimumHeaderWidth's shrink-to-fit-text behavior is skipped, matching Folder's own per-subclass override of the same hook (RecalculateMinimizedSize is protected virtual specifically so subclasses can do this).</summary>
    protected override void RecalculateMinimizedSize() => ApplyMinimizedSize(OriginalSize.X, OriginalSize.X);
}
