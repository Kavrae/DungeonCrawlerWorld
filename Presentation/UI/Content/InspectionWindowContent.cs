using Engine.Diagnostics;
using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.Math;
using Engine.Utilities;
using Game.Modules.Class.Components;
using Game.Modules.Core.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Race.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.UI.Chrome;
using Presentation.UI.ColorPalettes;
using Presentation.UI.Looting;

namespace Presentation.UI.Content;

/// <summary>
/// InspectionWindow's content, driven entirely by MapViewState.InspectionMode:
/// <list type="bullet">
/// <item>Basic -- one padded block per subject on the currently selected map tile
/// (SelectedMapNodePosition/CurrentMapLayer), terrain last. Rebuilt wholesale whenever the
/// tile's own subject-id set actually changes (tile clicked elsewhere, or an entity walked
/// on/off the still-selected tile) -- cheap, since one tile's occupant count is always small,
/// unlike SelectionWindowContent's incremental add/remove diffing (built for a very different
/// perf profile -- see its own doc comment, now retired in favor of this and Detail below).</item>
/// <item>Detail -- the same one-subject block for InspectedEntityId, plus a full,
/// alphabetically-sorted component dump appended beneath it (Admin -- see
/// MapViewState.InspectionMode's own doc comment on why there's no separate gating yet).
/// Rebuilt only when the followed entity id itself changes, not every frame -- but the dump's
/// text refreshes on the same ComponentRefreshInterval cadence SelectionWindowContent's own
/// per-component windows used to, since component values (health ticking, status effects, ...)
/// change continuously while an entity is being followed. Falls back to Minimized (clearing
/// itself, see InspectionWindow.OnDisplayModeChanged) if the followed entity is ever genuinely
/// destroyed (EntityManager.EntityExists false) -- not on death alone, since DeathSystem never
/// destroys a corpse (see CLAUDE.md's Death notes), so a killed target just keeps showing, now
/// with DeadComponent visible in the dump.</item>
/// </list>
/// A single subject's block (icon+name/race/class rows, HP bar, description) is shared by both
/// modes via BuildSubjectBlock. Every child tiles vertically via the host window's own
/// ChildElementTileMode.Vertical (see ShellBootstrapper) rather than manual Y math; a spacer
/// (with a centered 1px SeparatorBar) after each subject's block supplies the padding between
/// one subject's section and the next.
/// </summary>
public sealed class InspectionWindowContent(
    World world,
    MapViewState mapViewState,
    ComponentManager componentManager,
    EntityManager entityManager,
    ElementPoolService elementPoolService) : IElementContent
{
    private const float IconSize = 40f;
    private const float RowHeight = 16f;
    private const float RowTextGap = 6f;
    private const float BarHeight = 8f;
    private const float BarWidthFraction = 0.75f;
    private const float BlockPadding = 12f;
    private const float SeparatorHeight = 1f;

    /// <summary>Same cadence SelectionWindowContent's own per-component refresh used -- most components update every 10 frames, so more frequent text refreshes are wasted work.</summary>
    private const int AdminDumpRefreshInterval = 10;

    /// <summary>A generous, effectively-unlimited per-row height cap -- see SelectionWindowContent.UnboundedChildHeight's own doc comment for why this is needed: without it, a row tiled past the host window's own one-screen-tall content size gets silently clamped to nothing.</summary>
    private const float UnboundedChildHeight = 10000f;

    private readonly DirectComponentPool<DisplayTextComponent> _displayTextPool = componentManager.GetDirectPool<DisplayTextComponent>();
    private readonly MultiComponentPool<RaceComponent> _racePool = componentManager.GetMultiPool<RaceComponent>();
    private readonly MultiComponentPool<ClassComponent> _classPool = componentManager.GetMultiPool<ClassComponent>();
    private readonly PackedComponentPool<SimpleHealthComponent> _healthPool = componentManager.GetPackedPool<SimpleHealthComponent>();
    private readonly MultiComponentPool<BodyPartComponent> _bodyParts = componentManager.GetMultiPool<BodyPartComponent>();
    private readonly ComponentInspector _componentInspector = new(componentManager);

    // Optional -- see StatModifierMath.GetEffectiveValue's own doc comment for why a null pool
    // (StatModifiersModule not registered) is treated the same as "no active modifiers."
    private readonly MultiComponentPool<StatModifierComponent>? _statModifiers = componentManager.IsRegistered<StatModifierComponent>()
        ? componentManager.GetMultiPool<StatModifierComponent>()
        : null;

    private readonly List<int> _lastSubjectIds = [];
    private readonly List<int> _scratchSubjectIds = [];
    private readonly List<TextWindow> _adminDumpWindows = [];
    private readonly List<InspectedComponentEntry> _reusableInspectionList = [];

    private Window _hostWindow = null!;
    private bool _hasContent;

    private Point? _lastBasicPosition;
    private int _lastBasicMapLayer = -1;

    private int _lastDetailEntityId = -1;
    private int _updatesSinceLastAdminRefresh;

    public void Initialize(Window hostWindow) => _hostWindow = hostWindow;

    public void Update(GameTime gameTime)
    {
        switch (mapViewState.InspectionMode)
        {
            case InspectionMode.Basic:
                UpdateBasic();
                break;
            case InspectionMode.Detail:
            case InspectionMode.Admin:
                UpdateDetail();
                break;
            default:
                ClearIfNeeded();
                break;
        }
    }

    public void DrawContent(GameTime gameTime)
    {
        // Nothing to draw directly -- everything is child Elements, which Window already draws
        // as part of its own child-window loop.
    }

    private void UpdateBasic()
    {
        if (mapViewState.SelectedMapNodePosition is not { } selected || !world.IsOnMap(new Vector3Int(selected.X, selected.Y, 0)))
        {
            ClearIfNeeded();
            return;
        }

        var currentMapLayer = mapViewState.CurrentMapLayer;

        // Terrain always appears last -- every occupant is added before it, regardless of the
        // order World.GetOccupantEntityIdsAt happens to return them in.
        _scratchSubjectIds.Clear();
        foreach (var entityId in world.GetOccupantEntityIdsAt(new Vector3Int(selected.X, selected.Y, currentMapLayer)))
        {
            _scratchSubjectIds.Add(entityId);
        }

        if (Map.TerrainLayerFor(currentMapLayer) is { } terrainLayer)
        {
            var terrainEntityId = world.Map.GetTerrainEntityId(selected.X, selected.Y, terrainLayer);
            if (terrainEntityId != -1)
            {
                _scratchSubjectIds.Add(terrainEntityId);
            }
        }

        if (_hasContent && selected == _lastBasicPosition && currentMapLayer == _lastBasicMapLayer && _scratchSubjectIds.SequenceEqual(_lastSubjectIds))
        {
            return;
        }

        _lastBasicPosition = selected;
        _lastBasicMapLayer = currentMapLayer;
        _lastSubjectIds.Clear();
        _lastSubjectIds.AddRange(_scratchSubjectIds);
        _lastDetailEntityId = -1; // Invalidates Detail's own cache so it rebuilds fresh if Detail mode resumes later.

        _hostWindow.TitleText = $"Tile ({selected.X}, {selected.Y})";

        elementPoolService.CloseAllChildren(_hostWindow);
        _adminDumpWindows.Clear();
        _hasContent = _scratchSubjectIds.Count > 0;

        var blockWidth = _hostWindow.ContentSize.X;
        foreach (var entityId in _scratchSubjectIds)
        {
            BuildSubjectBlock(entityId, blockWidth);
        }
    }

    private void UpdateDetail()
    {
        var entityId = mapViewState.InspectedEntityId;
        if (entityId == -1 || !entityManager.EntityExists(entityId))
        {
            ClearIfNeeded();
            _hostWindow.SetDisplayMode(ElementDisplayMode.Minimized);
            return;
        }

        if (entityId != _lastDetailEntityId)
        {
            _lastDetailEntityId = entityId;
            _lastBasicPosition = null; // Invalidates Basic's own cache so it rebuilds fresh if Basic mode resumes later.
            _lastBasicMapLayer = -1;
            _lastSubjectIds.Clear();
            _updatesSinceLastAdminRefresh = 0;

            _hostWindow.TitleText = ResolveName(entityId);

            elementPoolService.CloseAllChildren(_hostWindow);
            _adminDumpWindows.Clear();
            _hasContent = true;

            var blockWidth = _hostWindow.ContentSize.X;
            BuildSubjectBlock(entityId, blockWidth);
            BuildAdminDump(entityId, blockWidth);
            return;
        }

        _updatesSinceLastAdminRefresh++;
        if (_updatesSinceLastAdminRefresh >= AdminDumpRefreshInterval)
        {
            _updatesSinceLastAdminRefresh = 0;
            RefreshAdminDump(entityId);
        }
    }

    private void ClearIfNeeded()
    {
        if (!_hasContent)
        {
            return;
        }

        _hasContent = false;
        _lastBasicPosition = null;
        _lastBasicMapLayer = -1;
        _lastSubjectIds.Clear();
        _lastDetailEntityId = -1;
        _adminDumpWindows.Clear();
        elementPoolService.CloseAllChildren(_hostWindow);
    }

    /// <summary>One subject's block -- icon+name/race/class rows, HP bar (entities with a SimpleHealthComponent or BodyPartComponent only), description, then a padded separator -- shared by Basic's per-occupant loop and Detail's single followed entity.</summary>
    private void BuildSubjectBlock(int entityId, float blockWidth)
    {
        BuildHeaderRow(entityId, blockWidth);
        BuildHealthRowIfPresent(entityId, blockWidth);
        BuildDescriptionRow(entityId, blockWidth);
        BuildSpacer(blockWidth);
    }

    private void BuildHeaderRow(int entityId, float blockWidth)
    {
        var hasRace = _racePool.CountForEntity(entityId) > 0;
        var hasClass = _classPool.CountForEntity(entityId) > 0;
        var rowCount = 1 + (hasRace ? 1 : 0) + (hasClass ? 1 : 0);
        // header's outer height isn't externally constrained (unlike its width, see textWidth
        // below), so it grows by WindowChrome.Padding on top and bottom -- the icon/text keep
        // their original sizes exactly, with real breathing room added around them, rather than
        // being squeezed into a smaller box.
        var headerHeight = System.Math.Max(IconSize, rowCount * RowHeight) + WindowChrome.Padding * 2;

        var header = elementPoolService.CreateElement<Window>(_hostWindow, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = new Vector2(blockWidth, headerHeight), MaximumSize = new Vector2(blockWidth, UnboundedChildHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        _hostWindow.AddChild(header);

        var icon = elementPoolService.CreateElement<EntityIconElement>(header, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(IconSize, IconSize), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        icon.Configure(entityId, new Vector2(IconSize, IconSize));
        header.AddChild(icon);

        var textX = IconSize + RowTextGap;
        // header's own content width is now blockWidth - 2*Padding (see ChildContentPadding),
        // not the full blockWidth -- header's width is a hard constraint (must not exceed the
        // block width), so text shrinks to fit inside the padded content area instead.
        var textWidth = System.Math.Max(0f, blockWidth - WindowChrome.Padding * 2 - textX);
        var rowIndex = 0;

        AddTextRow(header, textX, rowIndex++, textWidth, ResolveName(entityId));

        if (hasRace)
        {
            AddTextRow(header, textX, rowIndex++, textWidth, $"Race: {ResolveRaceName(entityId)}");
        }

        if (hasClass)
        {
            AddTextRow(header, textX, rowIndex++, textWidth, $"Class: {ResolveClassName(entityId)}");
        }
    }

    private void AddTextRow(Window parent, float x, int rowIndex, float width, string text)
    {
        var row = elementPoolService.CreateElement<TextWindow>(parent, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(x, rowIndex * RowHeight), Size = new Vector2(width, RowHeight), MaximumSize = new Vector2(width, UnboundedChildHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
            Text = new TextOptions { Text = text, TextColor = WindowPalette.TitleTextColor },
        });
        parent.AddChild(row);
    }

    private void BuildHealthRowIfPresent(int entityId, float blockWidth)
    {
        if (!HealthQueries.TryGetTotals(_healthPool, _bodyParts, entityId, out var currentHealth, out var maximumHealth) || maximumHealth <= 0)
        {
            return;
        }

        var effectiveMaximumHealth = StatModifierMath.GetEffectiveValue(_statModifiers, entityId, StatModifierTarget.MaximumHealth, maximumHealth);
        var healthFraction = effectiveMaximumHealth > 0 ? MathHelper.Clamp(currentHealth / effectiveMaximumHealth, 0f, 1f) : 1f;

        var rowHeight = BarHeight + WindowChrome.Padding * 2;
        var row = elementPoolService.CreateElement<Window>(_hostWindow, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = new Vector2(blockWidth, rowHeight), MaximumSize = new Vector2(blockWidth, UnboundedChildHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        _hostWindow.AddChild(row);

        // row's outer width is a hard constraint (must not exceed blockWidth); its outer height
        // above already grew by WindowChrome.Padding on top and bottom instead, so the bar keeps
        // its original BarHeight exactly, positioned at row's own (now padding-inset) content
        // origin -- see HealthWindow.AddBarRow's own doc comment for the same reasoning.
        var availableWidth = blockWidth - WindowChrome.Padding * 2;
        var barWidth = availableWidth * BarWidthFraction;
        var barX = (availableWidth - barWidth) / 2f;

        var bar = elementPoolService.CreateElement<FractionBarElement>(row, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(barX, 0), Size = new Vector2(barWidth, BarHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        bar.Configure(healthFraction, hasResource: true, HealthBarPalette.OutlineColor, HealthBarPalette.FractionColor);
        row.AddChild(bar);
    }

    private void BuildDescriptionRow(int entityId, float blockWidth)
    {
        var description = _displayTextPool.TryGetReadonly(entityId, out var displayText) ? displayText.Description : string.Empty;
        if (string.IsNullOrWhiteSpace(description))
        {
            return;
        }

        var descriptionWindow = elementPoolService.CreateElement<TextWindow>(_hostWindow, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { MaximumSize = new Vector2(blockWidth, UnboundedChildHeight), DisplayMode = ElementDisplayMode.WrapContent },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
            Text = new TextOptions { Text = description, TextColor = WindowPalette.TitleTextColor },
        });
        _hostWindow.AddChild(descriptionWindow);
    }

    /// <summary>
    /// Padding between one subject's section and the next, with a 1px divider (SeparatorBar's own
    /// 75%-width centering -- the same fraction the HP row above uses) vertically centered within
    /// it. spacer's outer height grows by WindowChrome.Padding on top and bottom (not externally
    /// constrained, unlike its width) so BlockPadding still means exactly what it always did --
    /// spacer's own padded content height -- and the vertical-centering math below is unaffected.
    /// spacer's outer width IS a hard constraint (must not exceed blockWidth), so the separator's
    /// own width shrinks to fit inside spacer's padded content area instead.
    /// </summary>
    private void BuildSpacer(float blockWidth)
    {
        var spacerHeight = BlockPadding + WindowChrome.Padding * 2;
        var spacer = elementPoolService.CreateElement<Window>(_hostWindow, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = new Vector2(blockWidth, spacerHeight), MaximumSize = new Vector2(blockWidth, UnboundedChildHeight), DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
        });
        _hostWindow.AddChild(spacer);

        var availableWidth = blockWidth - WindowChrome.Padding * 2;
        var separator = elementPoolService.CreateElement<SeparatorBar>(spacer, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, (BlockPadding - SeparatorHeight) / 2f), Size = new Vector2(availableWidth, SeparatorHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
        });
        separator.Configure(WindowPalette.TitleTextColor);
        spacer.AddChild(separator);
    }

    /// <summary>Detail/Admin's full component breakdown, alphabetically sorted by component type name -- one bordered TextWindow per component, mirroring the retired SelectionWindowContent's own per-component tiling (see this class's own doc comment). Unlike Basic's subject blocks, ComponentInspector's output isn't sorted on its own (neither it nor MultiComponentPool.CopyInspectionDataForEntity does), so the sort here is new, not reused.</summary>
    private void BuildAdminDump(int entityId, float blockWidth)
    {
        _reusableInspectionList.Clear();
        _componentInspector.CopyInspectionDataForEntity(entityId, _reusableInspectionList);
        ReplaceHealthEntriesWithEffectiveMaximum(_reusableInspectionList, entityId, _healthPool, _bodyParts, _statModifiers);
        _reusableInspectionList.Sort(static (a, b) => string.CompareOrdinal(a.ComponentType.Name, b.ComponentType.Name));

        foreach (var entry in _reusableInspectionList)
        {
            var componentWindow = elementPoolService.CreateElement<TextWindow>(_hostWindow, new ElementOptions
            {
                Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
                Layout = new ElementLayoutOptions { MaximumSize = new Vector2(blockWidth, UnboundedChildHeight), DisplayMode = ElementDisplayMode.WrapContent },
                Chrome = new ElementChromeOptions { ShowTitle = true, TitleText = entry.ComponentType.Name, ShowBorder = true, BorderSize = new Vector2(1, 1) },
                Text = new TextOptions { Text = entry.Value, TextColor = WindowPalette.TitleTextColor },
            });
            _hostWindow.AddChild(componentWindow);
            _adminDumpWindows.Add(componentWindow);
        }
    }

    /// <summary>Text-only refresh of the already-built admin dump windows, by sorted index position -- mirrors SelectionWindowContent.RefreshDebugWindowsForEntity's own "refresh in place, don't rebuild" approach and its same limitation: if a component is added/removed between refreshes (shifting alphabetical positions), this can briefly show a stale pairing until the next full rebuild (a mode/target change). Accepted rather than solved here, matching the precedent this replaces.</summary>
    private void RefreshAdminDump(int entityId)
    {
        _reusableInspectionList.Clear();
        _componentInspector.CopyInspectionDataForEntity(entityId, _reusableInspectionList);
        ReplaceHealthEntriesWithEffectiveMaximum(_reusableInspectionList, entityId, _healthPool, _bodyParts, _statModifiers);
        _reusableInspectionList.Sort(static (a, b) => string.CompareOrdinal(a.ComponentType.Name, b.ComponentType.Name));

        var count = System.Math.Min(_reusableInspectionList.Count, _adminDumpWindows.Count);
        for (var i = 0; i < count; i++)
        {
            _adminDumpWindows[i].UpdateText(_reusableInspectionList[i].Value);
        }
    }

    /// <summary>Replaces ComponentInspector's raw SimpleHealthComponent/BodyPartComponent entries with ones computed against the modifier-effective maximum instead of the raw stored field.</summary>
    /// <remarks>
    /// Each component's own parameterless ToString() can only ever show the pre-buff
    /// MaximumHealth field -- it has no access to entityId or the StatModifierComponent pool
    /// (Engine-layer generic code, no game-specific knowledge -- see CLAUDE.md). Removes the
    /// generic entries and re-adds hand-built replacements rather than editing them in place --
    /// CopyInspectionDataForEntity returns pre-formatted strings, not the underlying struct, so
    /// there's nothing to edit. Uses the same StatModifierMath.GetEffectiveValue chain
    /// HealthDamage/HealthHeal/ComplexHealthRegenSystem/BodyPartSelection already clamp against.
    /// Static and pool-parameterized (mirrors HealthQueries/BodyPartSelection's own shape) so it's
    /// directly unit-testable without constructing the rest of InspectionWindowContent.
    /// </remarks>
    internal static void ReplaceHealthEntriesWithEffectiveMaximum(
        List<InspectedComponentEntry> destination,
        int entityId,
        PackedComponentPool<SimpleHealthComponent> healthPool,
        MultiComponentPool<BodyPartComponent> bodyParts,
        MultiComponentPool<StatModifierComponent>? statModifiers)
    {
        destination.RemoveAll(static entry => entry.ComponentType == typeof(SimpleHealthComponent) || entry.ComponentType == typeof(BodyPartComponent));

        if (healthPool.TryGetReadonly(entityId, out var health))
        {
            var effectiveMaximumHealth = StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.MaximumHealth, health.MaximumHealth);
            destination.Add(new InspectedComponentEntry(typeof(SimpleHealthComponent), FormatHealthBar("HP", health.CurrentHealth, effectiveMaximumHealth), 0));
        }

        for (var denseIndex = bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex))
        {
            ref readonly var part = ref bodyParts.GetReadonlyByDenseIndex(denseIndex);
            var effectiveMaximumHealth = StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.MaximumHealth, part.MaximumHealth);
            destination.Add(new InspectedComponentEntry(typeof(BodyPartComponent), FormatHealthBar(part.Name, part.CurrentHealth, effectiveMaximumHealth), 0));
        }
    }

    /// <summary>Mirrors SimpleHealthComponent/BodyPartComponent's own ToString() bar format, fed the effective maximum instead of the raw stored field.</summary>
    private static string FormatHealthBar(string prefix, float currentHealth, float effectiveMaximumHealth) =>
        effectiveMaximumHealth > 0
            ? $"{StringUtility.BuildPercentageBar(prefix, (int)currentHealth, (int)effectiveMaximumHealth, 20)} {(int)currentHealth}/{(int)effectiveMaximumHealth}"
            : $"Invalid MaximumHealth: {effectiveMaximumHealth}";

    private string ResolveName(int entityId) =>
        _displayTextPool.TryGetReadonly(entityId, out var displayText) ? displayText.Name : "Unknown";

    private string ResolveRaceName(int entityId) =>
        _racePool.GetReadonlyByDenseIndex(_racePool.GetFirstDenseIndex(entityId)).Name;

    private string ResolveClassName(int entityId) =>
        _classPool.GetReadonlyByDenseIndex(_classPool.GetFirstDenseIndex(entityId)).Name;
}
