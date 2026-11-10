using UIEngine.UI;

namespace Presentation.UI;

/// <summary>
/// Persistent right-side HUD panel showing whatever MapViewState.InspectionMode currently
/// targets (see InspectionWindowContent) -- Basic (map-tile click) or Detail (context-menu
/// Inspect, follows its target; also carries the always-on Admin component dump -- see
/// MapViewState.InspectionMode's own doc comment). Cannot be closed (CanUserClose = false);
/// minimizing keeps the inspection target and its title, so restoring shows the same tile or
/// entity again. RecalculateMinimizedSize is overridden so minimizing
/// never shrinks the title bar's width the way Window's default does (MinimumHeaderWidth) --
/// this panel's width must always match PlayerHealthBarContent.Size.X (see
/// ShellBootstrapper), restored or not.
/// </summary>
public sealed class InspectionWindow(ElementServices services)
    : Window(services)
{
    /// <summary>Starts minimized -- a HUD-persistent panel with nothing to inspect yet defaults to its collapsed, title-only footprint, the same convention Folder.Initialize already follows.</summary>
    public override void Initialize()
    {
        base.Initialize();

        SetDisplayMode(ElementDisplayMode.Minimized);
    }

    /// <summary>Preserves the window's full configured width while minimized -- only Window.MinimumHeaderWidth's shrink-to-fit-text behavior is skipped, matching Folder's own per-subclass override of the same hook (RecalculateMinimizedSize is protected virtual specifically so subclasses can do this).</summary>
    protected override void RecalculateMinimizedSize() => ApplyMinimizedSize(OriginalSize.X, OriginalSize.X);
}
