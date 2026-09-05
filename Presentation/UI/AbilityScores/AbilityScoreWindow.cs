using Engine.ECS.Components;
using Engine.Utilities;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.Chrome;
using Presentation.UI.ColorPalettes;

namespace Presentation.UI.AbilityScores;

/// <summary>
/// Shows the 5 Core ability scores in equal-width columns -- 7, adding the 2 Hidden scores
/// (Luck, Wisdom), while GlobalState.IsAdminModeOn is on (see ActiveTypes) -- each column a
/// centered name/total header (non-scrolling) above an independently-scrolling list of
/// "Base : N" plus each active modifier (see AbilityScoreModifierFormatter). Builds its own
/// children directly via AddChild -- no
/// IElementContent/TabbedContent needed, since there's nothing to tab between. Created fresh by
/// AbilityScoreWindowController each time it's opened and returned to ElementPoolService's pool
/// on close, same lifecycle as InventoryManagementWindow. Also self-polls Mouse.GetState() every
/// Update (see UpdateHover),
/// the same idiom MapWindow uses for its own tile hover, to drive a header/modifier-row hover
/// popup -- kept self-contained here rather than routed through UiInputController since nothing
/// else needs to know about it.
/// </summary>
public sealed class AbilityScoreWindow(FontService fontService, ElementPoolService elementPoolService, LabelRenderer labelRenderer, ComponentManager componentManager)
    : Window(fontService, elementPoolService, labelRenderer)
{
    private const float HeaderHeight = 50f;
    private const float RowHeight = 20f;

    /// <summary>Between adjacent columns.</summary>
    private const float ColumnGap = 3f;

    /// <summary>See SeparatorBar -- the divider itself is drawn at 75% width; this is just the element's own full-width, 1px-tall footprint in the vertical tile chain.</summary>
    private const float SeparatorHeight = 1f;

    public static readonly Color ColumnColor = WindowPalette.AbilityScoreColumnBackground;

    private static readonly AbilityScoreType[] CoreTypes = Enum.GetValues<AbilityScoreType>()
        .Where(static type => !AbilityScoreCategory.IsHidden(type))
        .ToArray();

    /// <summary>Every ability score, Core then Hidden (Enum.GetValues' declaration order -- see AbilityScoreType) -- shown instead of CoreTypes while GlobalState.IsAdminModeOn is on.</summary>
    private static readonly AbilityScoreType[] AllTypes = Enum.GetValues<AbilityScoreType>();

    /// <summary>The column set actually shown this frame -- BuildColumns/RefreshAllColumns/RefreshColumn all key off this instead of a fixed count, and Update rebuilds whenever it changes (see _lastAdminModeOn).</summary>
    private AbilityScoreType[] ActiveTypes => GlobalState.IsAdminModeOn ? AllTypes : CoreTypes;

    /// <summary>Reallocated to exactly ActiveTypes.Length by every BuildColumns call -- never padded with null trailing slots (a real, confirmed crash: UpdateHover/FindHoverCandidate below assume every entry is populated).</summary>
    private Window[] _columnListWindows = [];

    /// <summary>See _columnListWindows.</summary>
    private AbilityScoreColumnHeader[] _columnHeaders = [];

    private readonly VersionWatcher _abilityScoreVersionWatcher = new();
    private readonly VersionWatcher _statModifierVersionWatcher = new();

    private int _entityId;
    private Tooltip _hoverPopup = null!;

    private Element? _hoveredCandidate;
    private int _hoveredFrames;

    /// <summary>Mirrors GlobalState.IsAdminModeOn as of the last BuildColumns -- Update rebuilds (not just refreshes) whenever this goes stale, since toggling admin mode changes the column *count*, not just their contents.</summary>
    private bool _lastAdminModeOn;

    /// <summary>Just records entityId/the shared popup -- must be called after CreateElement but before Initialize, same contract as InventoryManagementWindow.Configure. Column-building itself waits for Initialize (see its own doc comment for why). hoverPopup is owned by AbilityScoreWindowController (created once, top-level, shared across opens) rather than a child of this window -- see Tooltip's own doc comment for why a nested child can't work here.</summary>
    public void Configure(int entityId, Tooltip hoverPopup)
    {
        _entityId = entityId;
        _hoverPopup = hoverPopup;
    }

    /// <summary>
    /// Columns are built here, not in Configure, because ContentSize/ContentAbsolutePosition
    /// aren't real yet at Configure time -- Element.Build only sets raw geometry fields (Layout's
    /// requested Size/RelativePosition), and MeasureAndArrange (which resolves the actual
    /// content-area size/position this window's Fixed DisplayMode settles into, net of border/
    /// header insets) doesn't run until Element.Initialize, before OnChildrenInitialized below.
    /// Building columns any earlier reads a stale/zeroed ContentSize -- exactly the bug this
    /// fixes (columns clustered at the window's stale/default position, sized from a leftover
    /// width instead of the real one). Same reasoning TabbedContent.Initialize follows for its
    /// own body window's ContentSize.
    ///
    /// OnChildrenInitialized, not an Initialize override calling base first -- the same hook
    /// Window itself uses to mount IElementContent (see Window.OnChildrenInitialized), and the
    /// only point in Element's Initialize sequence that is both after MeasureAndArrange and
    /// before Opened fires. An Initialize override runs base.Initialize() (which already raises
    /// this window's own Opened) before it can build anything, so the columns AddChild builds
    /// below would exist only after Opened had already signaled "fully set up" -- misleading for
    /// any future Opened subscriber, and inconsistent with GridControl using the same hook now.
    /// </summary>
    protected override void OnChildrenInitialized()
    {
        base.OnChildrenInitialized();

        _lastAdminModeOn = GlobalState.IsAdminModeOn;
        BuildColumns();
        RefreshAllColumns();
        _abilityScoreVersionWatcher.HasChanged(GetAbilityScoreVersion());
        _statModifierVersionWatcher.HasChanged(GetStatModifierVersion());
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        UpdateHover(Mouse.GetState());

        if (GlobalState.IsAdminModeOn != _lastAdminModeOn)
        {
            // Admin mode changes the column *count*, not just their contents -- a full rebuild,
            // not RefreshAllColumns alone, same as the initial build above.
            _lastAdminModeOn = GlobalState.IsAdminModeOn;
            BuildColumns();
            RefreshAllColumns();
            return;
        }

        // Both watchers must be checked every call (not short-circuited) so each stays in sync
        // with its own version source regardless of whether the other one changed this time.
        var abilityScoreChanged = _abilityScoreVersionWatcher.HasChanged(GetAbilityScoreVersion());
        var statModifierChanged = _statModifierVersionWatcher.HasChanged(GetStatModifierVersion());
        if (!abilityScoreChanged && !statModifierChanged)
        {
            return;
        }

        RefreshAllColumns();
    }

    /// <summary>
    /// Header highlight is immediate (instant visual feedback); the popup itself is delay-gated
    /// the same way HotbarController.UpdateHover gates its own Armed Hotkey Summary popup,
    /// against the same shared HudChrome.HoverTooltipDelayFrames -- but hides immediately on candidate change/
    /// loss (no delay on hiding, only on showing, same convention MapViewState.HoverSlot uses).
    /// Known, accepted gap: unlike HandleHotbarHover, this doesn't suppress itself during an
    /// active drag of this window's own title bar -- not worth the extra plumbing unless it
    /// turns out to actually be noticeable.
    /// </summary>
    private void UpdateHover(MouseState mouseState)
    {
        var mousePosition = new Point(mouseState.X, mouseState.Y);
        var candidate = FindHoverCandidate(mousePosition);

        foreach (var header in _columnHeaders)
        {
            header.IsHovered = ReferenceEquals(header, candidate);
        }

        foreach (var listWindow in _columnListWindows)
        {
            foreach (var child in listWindow.ChildElements)
            {
                if (child is AbilityScoreModifierRow row)
                {
                    row.IsHovered = ReferenceEquals(row, candidate);
                }
            }
        }

        if (candidate == _hoveredCandidate)
        {
            _hoveredFrames++;
        }
        else
        {
            _hoveredCandidate = candidate;
            _hoveredFrames = candidate is null ? 0 : 1;
        }

        if (candidate is null || _hoveredFrames < HudChrome.HoverTooltipDelayFrames)
        {
            _hoverPopup.Hide();
            return;
        }

        if (candidate is AbilityScoreColumnHeader header2)
        {
            _hoverPopup.ShowNear(header2.Rectangle, PopupAnchor.East, PopupChrome.AbilityScorePopupGap, AbilityScoreDescriptions.Get(header2.Type));
        }
        else if (candidate is AbilityScoreModifierRow row)
        {
            var title = ModifierDisplayFormatting.DescribeSource(componentManager, row.Source!.Value);
            var body = $"{row.ModifierText}\n{ModifierDisplayFormatting.FormatDuration(row.RemainingDurationFrames)}";
            _hoverPopup.ShowNear(row.Rectangle, PopupAnchor.East, PopupChrome.AbilityScorePopupGap, body, title);
        }
    }

    /// <summary>Headers first, then modifier rows (Base rows excluded -- Source is null, nothing to pop up) across every column's list-window. Whichever the mouse point falls inside wins; null if it's over neither.</summary>
    private Element? FindHoverCandidate(Point mousePosition)
    {
        foreach (var header in _columnHeaders)
        {
            if (header.Rectangle.Contains(mousePosition))
            {
                return header;
            }
        }

        foreach (var listWindow in _columnListWindows)
        {
            foreach (var child in listWindow.ChildElements)
            {
                if (child is AbilityScoreModifierRow { Source: not null } row && row.Rectangle.Contains(mousePosition))
                {
                    return row;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Keeps each column's own width constant across an admin-mode toggle by growing/shrinking
    /// the whole window instead -- previously BuildColumns divided whatever the window's current
    /// ContentSize.X happened to be by the new column count, so toggling admin mode on (5 columns
    /// -> 7) squeezed every column narrower instead of widening the window to fit the 2 new ones
    /// (confirmed live). Reads the outgoing column count/width from _columnHeaders (still the
    /// previous build's array at this point -- ClearColumns doesn't touch it, see its own doc
    /// comment) and _columnHeaders.Length == 0 on the very first build, when there's no prior
    /// width to preserve and the window's own initial size (set by whoever created it, sized for
    /// CoreTypes.Length columns) is already correct as-is. Respects a player's own manual resize
    /// (CanUserResize is true) the same way -- it preserves whatever the column width actually
    /// was, not a hardcoded constant.
    ///
    /// SetMaximumSize before SetSize -- Element.Build sets a parentless Fixed-mode window's own
    /// MaximumSize once, falling back to its initial Layout.Size when (as here) no explicit
    /// MaximumSize is given, which otherwise permanently ceilings this window at whatever width it
    /// was first created with: SetSize alone silently clamped right back down to that stale
    /// original-5-column ceiling every time (confirmed live -- the window only ever "grew" back up
    /// to its own starting size, never past it, however many times admin mode was toggled back on).
    /// Only ever raises the ceiling (Math.Max against whatever it already is), never lowers it --
    /// shrinking back to 5 columns doesn't need a smaller ceiling, and lowering it here would also
    /// undo any headroom a player's own manual drag-resize had already established.
    /// </summary>
    private void ResizeWindowToPreserveColumnWidth()
    {
        var previousColumnCount = _columnHeaders.Length;
        var newColumnCount = ActiveTypes.Length;
        if (previousColumnCount == 0 || previousColumnCount == newColumnCount)
        {
            return;
        }

        var previousColumnWidth = (ContentSize.X - ColumnGap * (previousColumnCount - 1)) / previousColumnCount;
        var newContentWidth = previousColumnWidth * newColumnCount + ColumnGap * (newColumnCount - 1);
        var horizontalInset = CurrentSize.X - ContentSize.X;
        var targetOuterWidth = newContentWidth + horizontalInset;

        SetMaximumSize(new Vector2(System.Math.Max(MaximumSize.X, targetOuterWidth), MaximumSize.Y));
        SetSize(new Vector2(targetOuterWidth, CurrentSize.Y));

        // A window that grows to the right could otherwise end up partly off-screen -- the same
        // clamp AbilityScoreWindowController's own initial placement already applies.
        var screenBounds = elementPoolService.GraphicsDevice.Viewport.Bounds;
        SetRelativePosition(ScreenBoundsClamp.Clamp(RelativePosition, CurrentSize, new Vector2(screenBounds.Width, screenBounds.Height)));
    }

    private void BuildColumns()
    {
        ResizeWindowToPreserveColumnWidth();

        // A pooled instance being reused for a second open still has the previous open's
        // columns as live children (Element.Build resets its own _children list, but not these
        // subclass-owned arrays) -- close them first so they return to their own type pools
        // instead of leaking, mirroring RefreshColumn's row cleanup below.
        ClearColumns();

        var columnCount = ActiveTypes.Length;
        _columnHeaders = new AbilityScoreColumnHeader[columnCount];
        _columnListWindows = new Window[columnCount];

        // The outermost edges (all four sides) are the generic ContentPadding's job now (this
        // Window has children), not a manual constant here -- ContentSize already reflects it
        // (see ChildContentPadding's own doc comment: gated on CanContainChildren alone, so this
        // reads correctly even immediately after ClearColumns, while still transiently childless).
        // Only the internal header-to-list gap remains this method's own concern, via WindowChrome.Gap.
        var usableWidth = ContentSize.X - ColumnGap * (columnCount - 1);
        var columnWidth = usableWidth / columnCount;
        var listHeight = ContentSize.Y - HeaderHeight - WindowChrome.Gap;

        for (var index = 0; index < columnCount; index++)
        {
            var columnX = index * (columnWidth + ColumnGap);

            var header = elementPoolService.CreateElement<AbilityScoreColumnHeader>(this, new ElementOptions
            {
                Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
                Layout = new ElementLayoutOptions { RelativePosition = new Vector2(columnX, 0), Size = new Vector2(columnWidth, HeaderHeight), DisplayMode = ElementDisplayMode.Fixed },
                Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
                Content = new ElementContentOptions { ContentColor = ColumnColor },
            });
            header.SetOverlayGlow(true, WindowPalette.Hover, GlowMode.InteriorFade);
            AddChild(header);
            _columnHeaders[index] = header;

            var listWindow = elementPoolService.CreateElement<Window>(this, new ElementOptions
            {
                Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
                Layout = new ElementLayoutOptions { RelativePosition = new Vector2(columnX, HeaderHeight + WindowChrome.Gap), Size = new Vector2(columnWidth, listHeight), DisplayMode = ElementDisplayMode.Fixed },
                Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserScrollVertical = true, CanUserFocus = false },
                Content = new ElementContentOptions { ContentColor = ColumnColor },
            });
            listWindow.SetOverlayGlow(true, WindowPalette.Hover, GlowMode.InteriorFade);
            AddChild(listWindow);
            _columnListWindows[index] = listWindow;
        }
    }

    /// <summary>CloseAllChildren recursively closes an element's entire subtree (see ElementPoolService.CloseElement), so closing this window's own direct children (headers, list-windows) already reaches each list-window's rows/separators too. Doesn't touch _columnHeaders/_columnListWindows themselves -- BuildColumns (the only caller) immediately reallocates both right after this returns.</summary>
    private void ClearColumns() => elementPoolService.CloseAllChildren(this);

    private void RefreshAllColumns()
    {
        for (var index = 0; index < ActiveTypes.Length; index++)
        {
            RefreshColumn(index);
        }
    }

    private void RefreshColumn(int index)
    {
        var type = ActiveTypes[index];
        var listWindow = _columnListWindows[index];

        elementPoolService.CloseAllChildren(listWindow);

        _columnHeaders[index].Configure(type, GetTotal(type), new Vector2(listWindow.CurrentSize.X, HeaderHeight));

        var lines = AbilityScoreModifierFormatter.GetOrderedLines(componentManager, _entityId, type);
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            if (NeedsSeparatorBefore(lines, lineIndex))
            {
                var separator = elementPoolService.CreateElement<SeparatorBar>(listWindow, new ElementOptions
                {
                    Layout = new ElementLayoutOptions { Size = new Vector2(listWindow.ContentSize.X, SeparatorHeight), DisplayMode = ElementDisplayMode.Fixed },
                    Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
                    Content = new ElementContentOptions { ContentColor = ColumnColor },
                });
                separator.Configure(WindowPalette.TitleTextColor);
                listWindow.AddChild(separator);
            }

            var row = elementPoolService.CreateElement<AbilityScoreModifierRow>(listWindow, new ElementOptions
            {
                Layout = new ElementLayoutOptions { Size = new Vector2(listWindow.ContentSize.X, RowHeight), DisplayMode = ElementDisplayMode.Fixed },
                Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
                Content = new ElementContentOptions { ContentColor = ColumnColor },
            });
            row.Configure(lines[lineIndex], RowHeight);
            listWindow.AddChild(row);
        }
    }

    /// <summary>
    /// True right before the first Additive line (separating it from Base), and right before the
    /// first Multiplicative line (separating it from the Additive group) -- both only when the
    /// group being entered is actually non-empty on both sides of the boundary, per
    /// AbilityScoreModifierFormatter's own Base-then-Additive-then-Multiplicative ordering. A
    /// column with only Multiplicative modifiers (no Additive ones) gets no separator at all --
    /// there's no Additive group for either boundary to sit next to.
    /// </summary>
    private static bool NeedsSeparatorBefore(IReadOnlyList<ModifierDisplayLine> lines, int index)
    {
        if (index == 0)
        {
            return false;
        }

        var previousOperation = lines[index - 1].Operation;
        var currentOperation = lines[index].Operation;

        return (previousOperation is null && currentOperation == StatModifierOperation.Additive)
            || (previousOperation == StatModifierOperation.Additive && currentOperation == StatModifierOperation.Multiplicative);
    }

    private ushort GetTotal(AbilityScoreType type) =>
        AbilityScoreQueries.TryGetComponent(componentManager.GetMultiPool<AbilityScoreComponent>(), _entityId, type, out var component)
            ? component.Total
            : throw new InvalidOperationException($"No AbilityScoreComponent of type {type} for entity {_entityId}.");

    private uint GetAbilityScoreVersion() => componentManager.GetMultiPool<AbilityScoreComponent>().GetEntityVersion(_entityId);

    private uint GetStatModifierVersion() =>
        componentManager.IsRegistered<StatModifierComponent>()
            ? componentManager.GetMultiPool<StatModifierComponent>().GetEntityVersion(_entityId)
            : 0;
}
