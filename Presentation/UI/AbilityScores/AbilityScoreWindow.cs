using Engine.ECS.Components;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.StatModifiers.Components;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.ColorPalettes;

namespace Presentation.UI.AbilityScores;

/// <summary>
/// Shows the 5 Core ability scores in equal-width columns: a centered name/total header (non-
/// scrolling) above an independently-scrolling list of "Base : N" plus each active modifier
/// (see AbilityScoreModifierFormatter). Builds its own children directly via AddChild -- no
/// IElementContent/TabbedContent needed, since there's nothing to tab between (mirrors how
/// Folder.Initialize builds its own tiles directly). Created fresh by InventoryFolderController
/// each time it's opened and returned to ElementPoolService's pool on close, same lifecycle as
/// InventoryManagementWindow.
/// </summary>
public sealed class AbilityScoreWindow(FontService fontService, ElementPoolService elementPoolService, GlyphRenderer glyphRenderer, ComponentManager componentManager)
    : Window(fontService, elementPoolService, glyphRenderer)
{
    private const int ColumnCount = 5;
    private const float HeaderHeight = 50f;
    private const float RowHeight = 20f;

    /// <summary>Between adjacent columns.</summary>
    private const float ColumnGap = 3f;

    /// <summary>Between the outermost columns and this window's own content edges (all four sides).</summary>
    private const float Padding = 3f;

    /// <summary>Shared with InventoryManagementWindow's own background -- see WindowPalette.</summary>
    public static readonly Color BackgroundColor = WindowPalette.PanelBackgroundColor;

    /// <summary>Shared with InventoryFolderController's own tile background -- see WindowPalette.</summary>
    public static readonly Color ColumnColor = WindowPalette.PanelContentColor;

    private static readonly AbilityScoreType[] CoreTypes = Enum.GetValues<AbilityScoreType>()
        .Where(static type => !AbilityScoreCategory.IsHidden(type))
        .ToArray();

    private readonly Window[] _columnListWindows = new Window[ColumnCount];
    private readonly AbilityScoreColumnHeader[] _columnHeaders = new AbilityScoreColumnHeader[ColumnCount];

    private readonly VersionWatcher _abilityScoreVersionWatcher = new();
    private readonly VersionWatcher _statModifierVersionWatcher = new();

    private int _entityId;

    /// <summary>Just records entityId -- must be called after CreateElement but before Initialize, same contract as InventoryManagementWindow.Configure. Column-building itself waits for Initialize (see its own doc comment for why).</summary>
    public void Configure(int entityId) => _entityId = entityId;

    /// <summary>
    /// Columns are built here, not in Configure, because ContentSize/ContentAbsolutePosition
    /// aren't real yet at Configure time -- Element.Build only sets raw geometry fields (Layout's
    /// requested Size/RelativePosition), and MeasureAndArrange (which resolves the actual
    /// content-area size/position this window's Fixed DisplayMode settles into, net of border/
    /// header insets) doesn't run until base.Initialize() below. Building columns any earlier
    /// reads a stale/zeroed ContentSize -- exactly the bug this fixes (columns clustered at the
    /// window's stale/default position, sized from a leftover width instead of the real one).
    /// Same reasoning TabbedContent.Initialize follows for its own body window's ContentSize.
    /// </summary>
    public override void Initialize()
    {
        base.Initialize();

        BuildColumns();
        RefreshAllColumns();
        _abilityScoreVersionWatcher.HasChanged(GetAbilityScoreVersion());
        _statModifierVersionWatcher.HasChanged(GetStatModifierVersion());
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

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

    private void BuildColumns()
    {
        // A pooled instance being reused for a second open still has the previous open's
        // columns as live children (Element.Build resets its own _children list, but not these
        // subclass-owned arrays) -- close them first so they return to their own type pools
        // instead of leaking, mirroring RefreshColumn's row cleanup below.
        ClearColumns();

        var usableWidth = ContentSize.X - Padding * 2 - ColumnGap * (ColumnCount - 1);
        var columnWidth = usableWidth / ColumnCount;
        var listHeight = ContentSize.Y - Padding * 3 - HeaderHeight;

        for (var index = 0; index < ColumnCount; index++)
        {
            var columnX = Padding + index * (columnWidth + ColumnGap);

            var header = elementPoolService.CreateElement<AbilityScoreColumnHeader>(this, new ElementOptions
            {
                Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
                Layout = new ElementLayoutOptions { RelativePosition = new Vector2(columnX, Padding), Size = new Vector2(columnWidth, HeaderHeight), DisplayMode = ElementDisplayMode.Fixed },
                Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
                Content = new ElementContentOptions { ContentColor = ColumnColor },
            });
            AddChild(header);
            _columnHeaders[index] = header;

            var listWindow = elementPoolService.CreateElement<Window>(this, new ElementOptions
            {
                Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
                Layout = new ElementLayoutOptions { RelativePosition = new Vector2(columnX, HeaderHeight + Padding * 2), Size = new Vector2(columnWidth, listHeight), DisplayMode = ElementDisplayMode.Fixed },
                Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserScrollVertical = true, CanUserFocus = false },
                Content = new ElementContentOptions { ContentColor = ColumnColor },
            });
            AddChild(listWindow);
            _columnListWindows[index] = listWindow;
        }
    }

    /// <summary>Rows first (one level down, inside each list-window), then the headers/list-windows themselves (this window's own direct children) -- CloseAllChildren only closes one level, not recursively.</summary>
    private void ClearColumns()
    {
        foreach (var listWindow in _columnListWindows)
        {
            if (listWindow is not null)
            {
                elementPoolService.CloseAllChildren(listWindow);
            }
        }

        elementPoolService.CloseAllChildren(this);
        Array.Clear(_columnListWindows);
        Array.Clear(_columnHeaders);
    }

    private void RefreshAllColumns()
    {
        for (var index = 0; index < ColumnCount; index++)
        {
            RefreshColumn(index);
        }
    }

    private void RefreshColumn(int index)
    {
        var type = CoreTypes[index];
        var listWindow = _columnListWindows[index];

        elementPoolService.CloseAllChildren(listWindow);

        _columnHeaders[index].Configure(type.ToString(), GetTotal(type), new Vector2(listWindow.CurrentSize.X, HeaderHeight));

        foreach (var line in AbilityScoreModifierFormatter.GetOrderedLines(componentManager, _entityId, type))
        {
            var row = elementPoolService.CreateElement<AbilityScoreModifierRow>(listWindow, new ElementOptions
            {
                Layout = new ElementLayoutOptions { Size = new Vector2(listWindow.ContentSize.X, RowHeight), DisplayMode = ElementDisplayMode.Fixed },
                Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
                Content = new ElementContentOptions { ContentColor = ColumnColor },
            });
            row.Configure(line, RowHeight);
            listWindow.AddChild(row);
        }
    }

    private short GetTotal(AbilityScoreType type) =>
        AbilityScoreQueries.TryGetComponent(componentManager.GetMultiPool<AbilityScoreComponent>(), _entityId, type, out var component)
            ? component.Total
            : throw new InvalidOperationException($"No AbilityScoreComponent of type {type} for entity {_entityId}.");

    private uint GetAbilityScoreVersion() => componentManager.GetMultiPool<AbilityScoreComponent>().GetEntityVersion(_entityId);

    private uint GetStatModifierVersion() =>
        componentManager.IsRegistered<StatModifierComponent>()
            ? componentManager.GetMultiPool<StatModifierComponent>().GetEntityVersion(_entityId)
            : 0;
}
