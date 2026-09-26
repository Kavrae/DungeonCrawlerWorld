using Engine.Diagnostics;
using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.Math;
using Engine.Utilities;
using Game.Blueprints;
using Game.Modules.Class.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Race.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.Spawning;
using Game.Views;
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
/// (SelectedMapNodePosition/CurrentMapLayer), structure then terrain last. Rebuilt wholesale whenever the
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
/// <remarks>
/// A creature skeleton (see CreatureSkeletons) holds none of what a subject block shows, and
/// inspecting it must never build it. Its block and dump show what its spawn record rebuilds to
/// instead (SpawnRecordRebuilder, in a staging world), labelled as unsimulated defaults, until it is
/// built -- which rebuilds the view.
/// </remarks>
public sealed class InspectionWindowContent(
    World world,
    IMapViewQuery mapView,
    MapViewState mapViewState,
    ComponentManager componentManager,
    EntityManager entityManager,
    ElementPoolService elementPoolService,
    SpawnRecordRebuilder? spawnRecordRebuilder = null,
    CreatureSkeletons? skeletons = null,
    BlueprintRegistry? creatures = null) : IElementContent
{
    private const string UnsimulatedLabel = "Unsimulated -- spawn defaults";

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

    private readonly DirectComponentPool<SpawnRecordComponent>? _spawnRecords = componentManager.IsRegistered<SpawnRecordComponent>()
        ? componentManager.GetDirectPool<SpawnRecordComponent>()
        : null;

    private readonly List<int> _lastSubjectIds = [];
    private int _lastSkeletonCount;
    private bool _lastDetailWasSkeleton;
    private int _liveDumpCount;
    private readonly List<int> _scratchSubjectIds = [];
    private TerrainView? _lastStructure;
    private TerrainView? _lastTerrain;
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

        // The structure, then terrain, always appear last -- every occupant is added before them, regardless of the
        // order World.GetOccupantEntityIdsAt happens to return them in.
        _scratchSubjectIds.Clear();
        foreach (var entityId in world.GetOccupantEntityIdsAt(new Vector3Int(selected.X, selected.Y, currentMapLayer)))
        {
            _scratchSubjectIds.Add(entityId);
        }

        TerrainView? structure = mapView.TryGetStructure(selected.X, selected.Y, currentMapLayer, out var foundStructure) ? foundStructure : null;
        TerrainView? terrain = mapView.TryGetTerrain(selected.X, selected.Y, currentMapLayer, out var foundTerrain) ? foundTerrain : null;

        var skeletonCount = _scratchSubjectIds.Count(IsSkeleton);
        if (_hasContent && selected == _lastBasicPosition && currentMapLayer == _lastBasicMapLayer && _scratchSubjectIds.SequenceEqual(_lastSubjectIds) && skeletonCount == _lastSkeletonCount && structure == _lastStructure && terrain == _lastTerrain)
        {
            return;
        }

        _lastSkeletonCount = skeletonCount;
        _lastBasicPosition = selected;
        _lastBasicMapLayer = currentMapLayer;
        _lastSubjectIds.Clear();
        _lastSubjectIds.AddRange(_scratchSubjectIds);
        _lastStructure = structure;
        _lastTerrain = terrain;
        _lastDetailEntityId = -1; // Invalidates Detail's own cache so it rebuilds fresh if Detail mode resumes later.

        _hostWindow.TitleText = $"Tile ({selected.X}, {selected.Y})";

        elementPoolService.CloseAllChildren(_hostWindow);
        _adminDumpWindows.Clear();
        _hasContent = _scratchSubjectIds.Count > 0 || structure is not null || terrain is not null;

        var blockWidth = _hostWindow.ContentSize.X;
        foreach (var entityId in _scratchSubjectIds)
        {
            BuildSubjectBlock(entityId, blockWidth);
        }

        if (structure is { } structureView)
        {
            BuildTerrainBlock(structureView, blockWidth);
        }

        if (terrain is { } terrainView)
        {
            BuildTerrainBlock(terrainView, blockWidth);
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

        if (entityId != _lastDetailEntityId || IsSkeleton(entityId) != _lastDetailWasSkeleton)
        {
            _lastDetailEntityId = entityId;
            _lastDetailWasSkeleton = IsSkeleton(entityId);
            _lastBasicPosition = null; // Invalidates Basic's own cache so it rebuilds fresh if Basic mode resumes later.
            _lastBasicMapLayer = -1;
            _lastSubjectIds.Clear();
            _lastStructure = null;
            _lastTerrain = null;
            _updatesSinceLastAdminRefresh = 0;

            var subject = ReadSubject(entityId);
            _hostWindow.TitleText = subject.Name;

            elementPoolService.CloseAllChildren(_hostWindow);
            _adminDumpWindows.Clear();
            _hasContent = true;

            var blockWidth = _hostWindow.ContentSize.X;
            BuildSubjectBlock(entityId, subject, blockWidth);
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
        _lastStructure = null;
        _lastTerrain = null;
        _lastDetailEntityId = -1;
        _adminDumpWindows.Clear();
        elementPoolService.CloseAllChildren(_hostWindow);
    }

    /// <summary>One subject's block -- icon+name/race/class rows, HP bar (entities with a SimpleHealthComponent or BodyPartComponent only), description, then a padded separator -- shared by Basic's per-occupant loop and Detail's single followed entity.</summary>
    private void BuildSubjectBlock(int entityId, float blockWidth) => BuildSubjectBlock(entityId, ReadSubject(entityId), blockWidth);

    private void BuildSubjectBlock(int entityId, SubjectView subject, float blockWidth)
    {
        BuildHeaderRow(entityId, subject, blockWidth);
        BuildHealthRowIfPresent(subject.HealthFraction, blockWidth);
        AddDescriptionRow(subject.Description, blockWidth);
        BuildSpacer(blockWidth);
    }

    /// <summary>What a subject block shows for an entity: the entity itself, or for a skeleton, what its spawn record rebuilds to.</summary>
    private readonly record struct SubjectView(string Name, string? RaceName, string? ClassName, float? HealthFraction, string Description, bool IsUnsimulatedDefaults);

    private bool IsSkeleton(int entityId) => skeletons?.IsSkeleton(entityId) == true && spawnRecordRebuilder is not null && _spawnRecords is not null;

    private SubjectView ReadSubject(int entityId)
    {
        if (!IsSkeleton(entityId))
        {
            return ReadSubject(componentManager, entityId, isUnsimulatedDefaults: false);
        }

        var subject = default(SubjectView);
        spawnRecordRebuilder!.Rebuild(_spawnRecords!.GetReadonly(entityId), (stagingComponents, stagingEntityId) => subject = ReadSubject(stagingComponents, stagingEntityId, isUnsimulatedDefaults: true));
        return subject;
    }

    /// <summary>The name of the entity's first race, or null when it has none or the definitions aren't available (a test shell).</summary>
    private string? RaceNameOf(ComponentManager source, int entityId) =>
        creatures is not null
            && source.GetPackedPool<RaceSlotsComponent>().TryGetReadonly(entityId, out var slots)
            && creatures.Races.TryGet(slots.Primary, out var race)
                ? race.Name
                : null;

    /// <inheritdoc cref="RaceNameOf"/>
    private string? ClassNameOf(ComponentManager source, int entityId) =>
        creatures is not null
            && source.GetPackedPool<ClassSlotsComponent>().TryGetReadonly(entityId, out var slots)
            && creatures.Classes.TryGet(slots.Primary, out var definition)
                ? definition.Name
                : null;

    private SubjectView ReadSubject(ComponentManager source, int entityId, bool isUnsimulatedDefaults)
    {
        var naming = EntityNaming.For(source, creatures);

        return new SubjectView(
            naming.NameOf(entityId),
            RaceNameOf(source, entityId),
            ClassNameOf(source, entityId),
            ReadHealthFraction(source, entityId),
            naming.DescriptionOf(entityId),
            isUnsimulatedDefaults);
    }

    private float? ReadHealthFraction(ComponentManager source, int entityId)
    {
        if (!HealthQueries.TryGetTotals(source.GetPackedPool<SimpleHealthComponent>(), BodyPartsOf(source), entityId, out var currentHealth, out var maximumHealth) || maximumHealth <= 0)
        {
            return null;
        }

        var effectiveMaximumHealth = StatModifierMath.GetEffectiveValue(StatModifiersOf(source), entityId, StatModifierTarget.MaximumHealth, maximumHealth);
        return effectiveMaximumHealth > 0 ? MathHelper.Clamp(currentHealth / effectiveMaximumHealth, 0f, 1f) : 1f;
    }

    /// <summary>The body parts of whatever world source belongs to -- the live one, or SpawnRecordRebuilder' staging world for an unsimulated creature's defaults.</summary>
    private EntityBodyParts BodyPartsOf(ComponentManager source) => EntityBodyParts.For(source, creatures ?? new BlueprintRegistry());

    /// <remarks>Optional -- see StatModifierMath.GetEffectiveValue's own doc comment for why a null pool (StatModifiersModule not registered) is treated the same as "no active modifiers."</remarks>
    private static MultiComponentPool<StatModifierComponent>? StatModifiersOf(ComponentManager source) =>
        source.IsRegistered<StatModifierComponent>() ? source.GetMultiPool<StatModifierComponent>() : null;

    /// <summary>The tile's structure or terrain, after its occupants: icon and name, then its description -- the same shape as an entity's block, minus what a cell doesn't have (race, class, health).</summary>
    private void BuildTerrainBlock(TerrainView terrain, float blockWidth)
    {
        var header = CreateHeaderWindow(blockWidth, rowCount: 1);
        var icon = CreateIcon(header);
        icon.Configure(terrain.Visual, new Vector2(IconSize, IconSize));

        AddTextRow(header, IconSize + RowTextGap, 0, HeaderTextWidth(blockWidth), terrain.Name);
        AddDescriptionRow(terrain.Description, blockWidth);
        BuildSpacer(blockWidth);
    }

    private void BuildHeaderRow(int entityId, SubjectView subject, float blockWidth)
    {
        var rowCount = 1 + (subject.RaceName is null ? 0 : 1) + (subject.ClassName is null ? 0 : 1) + (subject.IsUnsimulatedDefaults ? 1 : 0);
        var header = CreateHeaderWindow(blockWidth, rowCount);
        var icon = CreateIcon(header);
        icon.Configure(entityId, new Vector2(IconSize, IconSize));

        var textX = IconSize + RowTextGap;
        var textWidth = HeaderTextWidth(blockWidth);
        var rowIndex = 0;

        AddTextRow(header, textX, rowIndex++, textWidth, subject.Name);

        if (subject.RaceName is { } raceName)
        {
            AddTextRow(header, textX, rowIndex++, textWidth, $"Race: {raceName}");
        }

        if (subject.ClassName is { } className)
        {
            AddTextRow(header, textX, rowIndex++, textWidth, $"Class: {className}");
        }

        if (subject.IsUnsimulatedDefaults)
        {
            AddTextRow(header, textX, rowIndex, textWidth, UnsimulatedLabel);
        }
    }

    /// <summary>header's own content width is blockWidth - 2*Padding (see ChildContentPadding), not the full blockWidth -- its width is a hard constraint, so text shrinks to fit inside the padded content area instead.</summary>
    private static float HeaderTextWidth(float blockWidth) =>
        System.Math.Max(0f, blockWidth - WindowChrome.Padding * 2 - (IconSize + RowTextGap));

    private Window CreateHeaderWindow(float blockWidth, int rowCount)
    {
        // header's outer height isn't externally constrained (unlike its width, see
        // HeaderTextWidth), so it grows by WindowChrome.Padding on top and bottom -- the icon/text
        // keep their original sizes exactly, with real breathing room added around them, rather
        // than being squeezed into a smaller box.
        var headerHeight = System.Math.Max(IconSize, rowCount * RowHeight) + WindowChrome.Padding * 2;

        var header = elementPoolService.CreateElement<Window>(_hostWindow, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = new Vector2(blockWidth, headerHeight), MaximumSize = new Vector2(blockWidth, UnboundedChildHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        _hostWindow.AddChild(header);
        return header;
    }

    private EntityIconElement CreateIcon(Window header)
    {
        var icon = elementPoolService.CreateElement<EntityIconElement>(header, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(IconSize, IconSize), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        header.AddChild(icon);
        return icon;
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

    private void BuildHealthRowIfPresent(float? fraction, float blockWidth)
    {
        if (fraction is not { } healthFraction)
        {
            return;
        }

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

    private void AddDescriptionRow(string description, float blockWidth)
    {
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
    /// <remarks>A skeleton's dump is what it holds, then -- under the unsimulated label -- what its spawn record rebuilds to; only the first part refreshes, the defaults never change.</remarks>
    private void BuildAdminDump(int entityId, float blockWidth)
    {
        CopySortedEntries(componentManager, entityId, _reusableInspectionList);
        AddDumpWindows(blockWidth);
        _liveDumpCount = _adminDumpWindows.Count;

        if (IsSkeleton(entityId))
        {
            AddDescriptionRow(UnsimulatedLabel, blockWidth);
            spawnRecordRebuilder!.Rebuild(_spawnRecords!.GetReadonly(entityId), (stagingComponents, stagingEntityId) => CopySortedEntries(stagingComponents, stagingEntityId, _reusableInspectionList));
            AddDumpWindows(blockWidth);
        }
    }

    private void AddDumpWindows(float blockWidth)
    {
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

    private void CopySortedEntries(ComponentManager source, int entityId, List<InspectedComponentEntry> destination)
    {
        destination.Clear();
        new ComponentInspector(source).CopyInspectionDataForEntity(entityId, destination);
        ReplaceHealthEntriesWithEffectiveMaximum(destination, entityId, source.GetPackedPool<SimpleHealthComponent>(), BodyPartsOf(source), StatModifiersOf(source));
        destination.Sort(static (a, b) => string.CompareOrdinal(a.ComponentType.Name, b.ComponentType.Name));
    }

    /// <summary>Text-only refresh of the already-built admin dump windows, by sorted index position -- mirrors SelectionWindowContent.RefreshDebugWindowsForEntity's own "refresh in place, don't rebuild" approach and its same limitation: if a component is added/removed between refreshes (shifting alphabetical positions), this can briefly show a stale pairing until the next full rebuild (a mode/target change). Accepted rather than solved here, matching the precedent this replaces.</summary>
    private void RefreshAdminDump(int entityId)
    {
        CopySortedEntries(componentManager, entityId, _reusableInspectionList);

        var count = System.Math.Min(_reusableInspectionList.Count, _liveDumpCount);
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
        EntityBodyParts bodyParts,
        MultiComponentPool<StatModifierComponent>? statModifiers)
    {
        destination.RemoveAll(static entry => entry.ComponentType == typeof(SimpleHealthComponent) || entry.ComponentType == typeof(BodyPartStateComponent));

        if (healthPool.TryGetReadonly(entityId, out var health))
        {
            var effectiveMaximumHealth = StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.MaximumHealth, health.MaximumHealth);
            destination.Add(new InspectedComponentEntry(typeof(SimpleHealthComponent), FormatHealthBar("HP", health.CurrentHealth, effectiveMaximumHealth), 0));
        }

        foreach (var part in bodyParts.Parts(entityId))
        {
            var effectiveMaximumHealth = StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.MaximumHealth, part.MaximumHealth);
            destination.Add(new InspectedComponentEntry(typeof(BodyPartStateComponent), FormatHealthBar(part.Name, part.CurrentHealth, effectiveMaximumHealth), 0));
        }
    }

    /// <summary>Mirrors SimpleHealthComponent/BodyPartComponent's own ToString() bar format, fed the effective maximum instead of the raw stored field.</summary>
    private static string FormatHealthBar(string prefix, float currentHealth, float effectiveMaximumHealth) =>
        effectiveMaximumHealth > 0
            ? $"{StringUtility.BuildPercentageBar(prefix, (int)currentHealth, (int)effectiveMaximumHealth, 20)} {(int)currentHealth}/{(int)effectiveMaximumHealth}"
            : $"Invalid MaximumHealth: {effectiveMaximumHealth}";

}
