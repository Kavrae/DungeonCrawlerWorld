using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.ECS.Properties;
using Engine.Math;
using Engine.Utilities;
using Game.Blueprints;
using Game.Modules.Class.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Race.Components;
using Game.Modules.StatModifiers.Components;
using Game.Spawning;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.MapRendering;
using Presentation.UI.Characteristics;
using Presentation.UI.ColorPalettes;
using Presentation.UI.Looting;
using UIEngine;
using UIEngine.UI;
using UIEngine.UI.Chrome;
using UIEngine.UI.ColorPalettes;
using UIEngine.UI.Fields;

namespace Presentation.UI.Content;

/// <summary>
/// InspectionWindow's content, driven entirely by MapViewState.InspectionMode:
/// <list type="bullet">
/// <item>Basic -- one padded block per subject on the currently selected map tile
/// (SelectedMapNodePosition/CurrentMapLayer), then the structure, the terrain, every aura reaching the tile, strongest
/// first (AuraView.CopyAurasAt), and a Shadow block when something flies above it (FlyingShadows). Rebuilt wholesale whenever the
/// tile's own subject-id set, the auras reaching it or the shadow on it actually change (tile clicked elsewhere, or an entity walked
/// on/off the still-selected tile) -- cheap, since one tile's occupant count is always small,
/// unlike SelectionWindowContent's incremental add/remove diffing (built for a very different
/// perf profile -- see its own doc comment, now retired in favor of this and Detail below).</item>
/// <item>Detail -- the same one-subject block for InspectedEntityId, plus a full,
/// alphabetically-sorted component dump appended beneath it (Admin -- see
/// MapViewState.InspectionMode's own doc comment on why there's no separate gating yet).
/// Follows Admin Mode while open (MapViewState.EntityInspectionMode), and is rebuilt only when the followed entity id or
/// that mode changes, not every frame. The dump is one block per component instance, read through its property table
/// (ComponentInspectionView); every AdminDumpRefreshInterval updates a block is rewritten only if its component's version
/// changed, or if it shows a frame as seconds from now or a derived row, and a component added or removed rebuilds the
/// view. Ends the inspection and falls back to
/// Minimized (ReleaseDestroyedEntity) if the followed entity is ever genuinely
/// destroyed (EntityManager.EntityExists false) -- not on death alone, since DeathSystem never
/// destroys a corpse (see CLAUDE.md's Death notes), so a killed target just keeps showing, now
/// with DeadComponent visible in the dump.</item>
/// </list>
/// A single subject's block (icon beside its name/race/class rows and HP bar, then description) is shared by both
/// modes via BuildSubjectBlock. Every child tiles vertically via the host window's own
/// ChildElementTileMode.Vertical (see ShellBootstrapper) rather than manual Y math; a spacer
/// (with a centered 1px TextDivider line) after each subject's block supplies the padding between
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
    BlueprintRegistry creatures,
    SpawnRecordRebuilder spawnRecordRebuilder,
    CreatureSkeletons skeletons,
    AuraView auraView,
    ActionSourceNaming actionSourceNaming,
    NpcDecisionView npcDecisions,
    ComponentInspectionView componentInspection,
    ComponentPropertyEdits componentPropertyEdits) : IElementContent
{
    private const string UnsimulatedLabel = "Unsimulated -- spawn defaults";

    private const float IconSize = 80f;
    private const float RowHeight = 16f;
    private const float RowTextGap = 6f;
    private const float BarHeight = 8f;
    private const float CharacteristicIconSize = 20f;
    private const float CharacteristicIconGap = 4f;
    private const float CharacteristicRowGapAbove = 6f;
    private const float CharacteristicRowGapBelow = 2f;
    private const float BlockPadding = 12f;
    private const float SeparatorHeight = 1f;
    private const float SeparatorWidthFraction = 0.75f;

    /// <summary>Same cadence SelectionWindowContent's own per-component refresh used -- most components update every 10 frames, so more frequent text refreshes are wasted work.</summary>
    private const int AdminDumpRefreshInterval = 10;

    /// <summary>A wrapping text row's height cap: taller than any screen, so a long description or component dump grows with the column, which scrolls, rather than stopping at the screen's height (an element's default cap).</summary>
    private const float MaximumTextRowHeight = 10000f;

    private readonly DirectComponentPool<SpawnRecordComponent> _spawnRecords = componentManager.GetDirectPool<SpawnRecordComponent>();

    private readonly List<int> _lastSubjectIds = [];
    private int _lastSkeletonCount;
    private bool _lastDetailWasSkeleton;
    private InspectionMode _lastDetailMode;
    private int _liveDumpCount;
    private readonly List<int> _scratchSubjectIds = [];
    private TerrainView? _lastStructure;
    private TerrainView? _lastTerrain;
    private readonly List<AuraAtCell> _scratchAuras = [];
    private readonly List<AuraAtCell> _lastAuras = [];
    private EntitySizeCategory? _lastShadowCasterSize;
    private Point? _scratchAurasPosition;
    private int _scratchAurasMapLayer = -1;
    private int _scratchAurasVersion;
    private readonly List<TextWindow> _adminDumpWindows = [];
    private readonly List<InspectedComponentInstance> _instances = [];
    private readonly List<InspectedComponentInstance> _builtInstances = [];
    private readonly List<InspectedPropertyRow> _rows = [];
    private readonly List<InspectedComponentBlock> _unsimulatedDefaults = [];
    private readonly List<EditableBlock> _editableBlocks = [];
    private bool _isEditing;
    private EntityKey _inspectedKey;
    private readonly KeyedCache<NpcDecisionText.Key, string> _decisionText = new();
    private TextWindow? _decisionWindow;

    private Window _hostWindow = null!;
    private bool _hasContent;

    private Point? _lastBasicPosition;
    private int _lastBasicMapLayer = -1;

    private int _lastDetailEntityId = -1;
    private int _updatesSinceLastAdminRefresh;

    private const string EditLabel = "Edit";
    private const float EditButtonTextPadding = 10f;
    private const string EditTooltip = "Edit: pause the game and edit the inspected entity's components";
    private const string DoneEditingTooltip = "Done editing: the game resumes unless you paused it yourself";
    private const string EditUnavailableTooltip = "Edit: inspect an entity to edit its components";

    private const string SearchGhostText = "Filter components";
    private static readonly int SearchDebounceFrames = GameTiming.FramesForSeconds(0.3f);

    private Button? _editButton;

    /// <summary>The entity the view last showed, kept through a rebuild of the same entity (a component added or removed) so the view keeps its scroll position; -1 once anything else is shown.</summary>
    private int _lastShownEntityId = -1;

    /// <summary>Above the body, kept through every rebuild: filters an inspected entity's component blocks by name. Shown only while an entity is inspected.</summary>
    private TextBox _searchBox = null!;
    private readonly DebouncedTextFilter _searchFilter = new(SearchDebounceFrames);

    /// <summary>Everything the view shows below the search box, rebuilt whole; it scrolls, the search box doesn't.</summary>
    private Window _body = null!;

    private ComponentPropertyEdits Edits => componentPropertyEdits;

    public void Initialize(Window hostWindow)
    {
        _hostWindow = hostWindow;
        var width = hostWindow.ContentSize.X;
        _searchBox = elementPoolService.CreateElement<TextBox>(hostWindow, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { Size = new Vector2(width, FieldRow.RowHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = false },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelBackgroundColor },
            Text = new TextOptions { TextColor = WindowPalette.TitleTextColor },
        });
        _searchBox.GhostText = SearchGhostText;
        hostWindow.AddChild(_searchBox);
        _searchBox.IsVisible = false;

        _body = elementPoolService.CreateElement<Window>(hostWindow, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
            Layout = new ElementLayoutOptions { Size = hostWindow.ContentSize, DisplayMode = ElementDisplayMode.Fixed, FillsRemaining = true, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserScrollVertical = true, CanUserFocus = false },
        });
        _body.ContentPadding = Vector2.Zero;
        hostWindow.AddChild(_body);
    }

    /// <summary>Whether a component block is shown under the search box's applied text: every block while it is empty, else those whose name holds it, ignoring case.</summary>
    private bool MatchesSearch(string componentName) =>
        _searchFilter.AppliedText.Length == 0 || componentName.Contains(_searchFilter.AppliedText, StringComparison.OrdinalIgnoreCase);

    /// <summary>Copies the entity's component instances the search lets through into _instances.</summary>
    private void CopyMatchingInstances(int entityId)
    {
        componentInspection.CopyInstances(entityId, _instances);
        for (var index = _instances.Count - 1; index >= 0 && _searchFilter.AppliedText.Length > 0; index--)
        {
            if (!MatchesSearch(_instances[index].Name))
            {
                _instances.RemoveAt(index);
            }
        }
    }

    /// <summary>Shows the search box only while an entity is inspected, and rebuilds the view, from the top, when its applied text changes.</summary>
    private void UpdateSearch()
    {
        var searches = mapViewState.InspectionMode is InspectionMode.Detail or InspectionMode.Admin;
        if (_searchBox.IsVisible != searches)
        {
            _searchBox.IsVisible = searches;
        }

        if (_searchFilter.Update(_searchBox.OriginalText) && searches)
        {
            _lastDetailEntityId = -1;
            _lastShownEntityId = -1;
        }
    }

    /// <remarks>While the window is minimized its target is kept but nothing is rebuilt; restoring it rebuilds whatever changed meanwhile. A followed entity destroyed meanwhile is still let go at once.</remarks>
    public void Update(GameTime gameTime)
    {
        _editButton ??= BuildEditButton();
        if (_hostWindow.DisplayMode == ElementDisplayMode.Minimized)
        {
            if (mapViewState.InspectionMode is InspectionMode.Detail or InspectionMode.Admin && !InspectedEntityExists())
            {
                ReleaseDestroyedEntity();
            }

            SyncEditMode();
            return;
        }

        if (mapViewState.InspectionMode is InspectionMode.Detail or InspectionMode.Admin)
        {
            mapViewState.InspectionMode = MapViewState.EntityInspectionMode;
        }

        UpdateSearch();
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

        SyncEditMode();
    }

    /// <summary>The Edit title button, built on the first update and attached for good -- it closes with the window -- left of minimize, shown only while Admin Mode is on (SyncEditMode).</summary>
    private Button BuildEditButton()
    {
        var button = Window.BuildTitleButton(_hostWindow, EditLabel);
        button.SetFixedSize(new Vector2(button.ContentFont.MeasureString(EditLabel).X + EditButtonTextPadding, button.CurrentSize.Y));
        button.Clicked += _ => ToggleEditMode();
        button.IsVisible = false;
        _hostWindow.AddTitleButton(button);
        return button;
    }

    /// <summary>Whether Edit mode can be on: an entity inspected in Admin Mode, in a window that isn't minimized.</summary>
    private bool CanEdit =>
        _hostWindow.DisplayMode != ElementDisplayMode.Minimized && mapViewState.InspectionMode == InspectionMode.Admin && InspectedEntityExists();

    /// <summary>Turns Edit mode on or off; the next update rebuilds the view as fields or as text.</summary>
    private void ToggleEditMode()
    {
        if (!CanEdit)
        {
            return;
        }

        mapViewState.IsEditingComponents = !mapViewState.IsEditingComponents;
    }

    /// <summary>Ends Edit mode with the Admin inspection it belongs to; puts the button on the title bar while Admin Mode is on and takes it off while it is off, showing whether Edit mode is on and whether it can be.</summary>
    private void SyncEditMode()
    {
        var canEdit = CanEdit;
        if (!canEdit)
        {
            mapViewState.IsEditingComponents = false;
        }

        if (_editButton is not { } button)
        {
            return;
        }

        _hostWindow.SetTitleButtonShown(button, Input.AdminMode.IsOn);
        button.Enabled = canEdit;
        button.IsSelected = mapViewState.IsEditingComponents;
        button.TooltipText = !canEdit ? EditUnavailableTooltip : mapViewState.IsEditingComponents ? DoneEditingTooltip : EditTooltip;
    }

    /// <summary>Whether the dump is built as editable field rows: Admin inspection in Edit mode.</summary>
    private bool IsEditingNow => mapViewState.InspectionMode == InspectionMode.Admin && mapViewState.IsEditingComponents;

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

        CopyAurasAt(selected, currentMapLayer);
        EntitySizeCategory? shadowCasterSize = FlyingShadows.TryGetLargestCasterSize(mapView, selected.X, selected.Y, currentMapLayer, out var largestCaster) ? largestCaster : null;

        var skeletonCount = _scratchSubjectIds.Count(IsSkeleton);
        if (_hasContent && selected == _lastBasicPosition && currentMapLayer == _lastBasicMapLayer && _scratchSubjectIds.SequenceEqual(_lastSubjectIds) && skeletonCount == _lastSkeletonCount && structure == _lastStructure && terrain == _lastTerrain && _scratchAuras.SequenceEqual(_lastAuras) && shadowCasterSize == _lastShadowCasterSize)
        {
            return;
        }

        var keepsScroll = _hasContent && selected == _lastBasicPosition && currentMapLayer == _lastBasicMapLayer;
        var scrollOffset = _body.ScrollOffset;
        _lastShownEntityId = -1;
        _lastSkeletonCount = skeletonCount;
        _lastBasicPosition = selected;
        _lastBasicMapLayer = currentMapLayer;
        _lastSubjectIds.Clear();
        _lastSubjectIds.AddRange(_scratchSubjectIds);
        _lastStructure = structure;
        _lastTerrain = terrain;
        _lastAuras.Clear();
        _lastAuras.AddRange(_scratchAuras);
        _lastShadowCasterSize = shadowCasterSize;
        _lastDetailEntityId = -1; // Invalidates Detail's own cache so it rebuilds fresh if Detail mode resumes later.

        _hostWindow.TitleText = $"Tile ({selected.X}, {selected.Y})";

        elementPoolService.CloseAllChildren(_body);
        _decisionWindow = null;
        _adminDumpWindows.Clear();
        _editableBlocks.Clear();
        _hasContent = _scratchSubjectIds.Count > 0 || structure is not null || terrain is not null || _scratchAuras.Count > 0 || shadowCasterSize is not null;

        var blockWidth = _body.ContentSize.X;
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

        foreach (var aura in _scratchAuras)
        {
            BuildAuraBlock(aura, blockWidth);
        }

        if (shadowCasterSize is { } casterSize)
        {
            BuildShadowBlock(casterSize, blockWidth);
        }

        if (keepsScroll)
        {
            _body.ScrollTo(scrollOffset);
        }
    }

    /// <summary>Fills _scratchAuras with the auras reaching the tile, read again only when the tile or the auras reaching it have changed since it was last read.</summary>
    private void CopyAurasAt(Point selected, int mapLayer)
    {
        var version = auraView.VersionAt(selected.X, selected.Y, mapLayer);
        if (selected == _scratchAurasPosition && mapLayer == _scratchAurasMapLayer && version == _scratchAurasVersion)
        {
            return;
        }

        _scratchAurasPosition = selected;
        _scratchAurasMapLayer = mapLayer;
        _scratchAurasVersion = version;
        auraView.CopyAurasAt(selected.X, selected.Y, mapLayer, _scratchAuras);
    }

    private void UpdateDetail()
    {
        if (!InspectedEntityExists())
        {
            ReleaseDestroyedEntity();
            _hostWindow.SetDisplayMode(ElementDisplayMode.Minimized);
            return;
        }

        var entityId = mapViewState.InspectedEntityId;

        if (entityId != _lastDetailEntityId || IsSkeleton(entityId) != _lastDetailWasSkeleton || mapViewState.InspectionMode != _lastDetailMode || IsEditingNow != _isEditing)
        {
            var keepsScroll = entityId == _lastShownEntityId;
            var scrollOffset = _body.ScrollOffset;
            _lastShownEntityId = entityId;
            _lastDetailEntityId = entityId;
            _lastDetailWasSkeleton = IsSkeleton(entityId);
            _lastDetailMode = mapViewState.InspectionMode;
            _lastBasicPosition = null; // Invalidates Basic's own cache so it rebuilds fresh if Basic mode resumes later.
            _lastBasicMapLayer = -1;
            _lastSubjectIds.Clear();
            _lastStructure = null;
            _lastTerrain = null;
            _lastAuras.Clear();
            _updatesSinceLastAdminRefresh = 0;

            var subject = ReadSubject(entityId);
            _hostWindow.TitleText = subject.Name;

            elementPoolService.CloseAllChildren(_body);
            _decisionWindow = null;
            _adminDumpWindows.Clear();
            _editableBlocks.Clear();
            _hasContent = true;

            var blockWidth = _body.ContentSize.X;
            BuildSubjectBlock(entityId, subject, blockWidth);
            BuildDecisionBlock(entityId, blockWidth);
            BuildAdminDump(entityId, blockWidth);
            if (keepsScroll)
            {
                _body.ScrollTo(scrollOffset);
            }

            return;
        }

        RefreshDecisionBlock(entityId);
        _updatesSinceLastAdminRefresh++;
        if (_updatesSinceLastAdminRefresh >= AdminDumpRefreshInterval)
        {
            _updatesSinceLastAdminRefresh = 0;
            RefreshAdminDump(entityId);
        }
    }

    private bool InspectedEntityExists() => mapViewState.InspectedEntityId != -1 && entityManager.EntityExists(mapViewState.InspectedEntityId);

    /// <summary>Ends a Detail inspection whose entity is gone: nothing is inspected and the title is cleared.</summary>
    private void ReleaseDestroyedEntity()
    {
        ClearIfNeeded();
        mapViewState.InspectionMode = InspectionMode.None;
        mapViewState.InspectedEntityId = -1;
        _hostWindow.TitleText = string.Empty;
    }

    private void ClearIfNeeded()
    {
        if (!_hasContent)
        {
            return;
        }

        _hasContent = false;
        _lastShownEntityId = -1;
        _lastBasicPosition = null;
        _lastBasicMapLayer = -1;
        _lastSubjectIds.Clear();
        _lastStructure = null;
        _lastTerrain = null;
        _lastAuras.Clear();
        _lastDetailEntityId = -1;
        _adminDumpWindows.Clear();
        _editableBlocks.Clear();
        elementPoolService.CloseAllChildren(_body);
        _decisionWindow = null;
        npcDecisions.Inspect(-1);
    }

    /// <summary>One subject's block -- the icon, beside it the name/race/class rows and the HP bar (entities with a SimpleHealthComponent or BodyPartComponent only), then the description and a padded separator -- shared by Basic's per-occupant loop and Detail's single followed entity.</summary>
    private void BuildSubjectBlock(int entityId, float blockWidth) => BuildSubjectBlock(entityId, ReadSubject(entityId), blockWidth);

    private void BuildSubjectBlock(int entityId, SubjectView subject, float blockWidth)
    {
        BuildSubjectIconSummary(entityId, subject, blockWidth);
        AddTextBlock(subject.Description, blockWidth);
        BuildSpacer(blockWidth);
    }

    /// <summary>What a subject block shows for an entity: the entity itself, or for a skeleton, what its spawn record rebuilds to.</summary>
    private readonly record struct SubjectView(string Name, string? RaceName, string? ClassName, float? HealthFraction, string Description, bool IsUnsimulatedDefaults);

    private bool IsSkeleton(int entityId) => skeletons.IsSkeleton(entityId);

    private SubjectView ReadSubject(int entityId)
    {
        if (!IsSkeleton(entityId))
        {
            return ReadSubject(componentManager, entityId, isUnsimulatedDefaults: false);
        }

        var subject = default(SubjectView);
        spawnRecordRebuilder.Rebuild(_spawnRecords.GetReadonly(entityId), (stagingComponents, stagingEntityId) => subject = ReadSubject(stagingComponents, stagingEntityId, isUnsimulatedDefaults: true));
        return subject;
    }

    /// <summary>The name of the entity's first race, or null when it has none.</summary>
    private string? RaceNameOf(ComponentManager source, int entityId) =>
        source.GetPackedPool<RaceSlotsComponent>().TryGetReadonly(entityId, out var slots)
            && creatures.Races.TryGet(slots.Primary, out var race)
                ? race.Name
                : null;

    /// <inheritdoc cref="RaceNameOf"/>
    private string? ClassNameOf(ComponentManager source, int entityId) =>
        source.GetPackedPool<ClassSlotsComponent>().TryGetReadonly(entityId, out var slots)
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
        if (!HealthQueries.TryGetTotals(source.GetPackedPool<SimpleHealthComponent>(), BodyPartsOf(source), StatModifiersOf(source), entityId, out var currentHealth, out var maximumHealth) || maximumHealth <= 0)
        {
            return null;
        }

        return MathHelper.Clamp(currentHealth / maximumHealth, 0f, 1f);
    }

    /// <summary>The body parts of whatever world source belongs to -- the live one, or SpawnRecordRebuilder' staging world for an unsimulated creature's defaults.</summary>
    private EntityBodyParts BodyPartsOf(ComponentManager source) => EntityBodyParts.For(source, creatures);

    private static MultiComponentPool<StatModifierComponent> StatModifiersOf(ComponentManager source) =>
        source.GetMultiPool<StatModifierComponent>();

    /// <summary>The tile's structure or terrain, after its occupants: icon and name, then its description -- the same shape as an entity's block, minus what a cell doesn't have (race, class, health).</summary>
    private void BuildTerrainBlock(TerrainView terrain, float blockWidth)
    {
        var iconSummary = CreateIconSummary(blockWidth, RowHeight);
        var icon = CreateIcon(iconSummary);
        icon.Configure(terrain.Visual, new Vector2(IconSize, IconSize));

        AddSummaryLine(iconSummary, 0f, terrain.Name);
        AddTextBlock(terrain.Description, blockWidth);
        BuildSpacer(blockWidth);
    }

    /// <summary>An aura reaching the tile, after the floor: its name, its strength there and -- unless no one contributor can be named -- where it comes from, then its description.</summary>
    private void BuildAuraBlock(AuraAtCell aura, float blockWidth)
    {
        var nameAndStrength = $"Aura: {auraView.NameOf(aura.AuraId)}\nStrength: {aura.Power}";
        AddTextBlock(aura.Credit.Kind == ActionSourceKind.Aura ? nameAndStrength : $"{nameAndStrength}\nFrom: {actionSourceNaming.Describe(aura.Credit)}", blockWidth);
        AddTextBlock(auraView.DescriptionOf(aura.AuraId), blockWidth);
        BuildSpacer(blockWidth);
    }

    /// <summary>Something flying over the tile, after the auras: a "Shadow" line and how big the biggest thing above is, never what it is.</summary>
    private void BuildShadowBlock(EntitySizeCategory casterSize, float blockWidth)
    {
        AddTextBlock("Shadow", blockWidth);
        AddTextBlock(FlyingShadows.DescriptionFor(casterSize), blockWidth);
        BuildSpacer(blockWidth);
    }

    private void BuildSubjectIconSummary(int entityId, SubjectView subject, float blockWidth)
    {
        var lineCount = 1 + (subject.RaceName is null ? 0 : 1) + (subject.ClassName is null ? 0 : 1) + (subject.IsUnsimulatedDefaults ? 1 : 0) + (subject.HealthFraction is null ? 0 : 1);
        var characteristicIcons = mapView.TryGetOccupant(entityId, out var occupant) ? CharacteristicIcons.For(occupant) : [];
        var characteristicRowHeight = characteristicIcons.Count == 0 ? 0f : CharacteristicRowGapAbove + CharacteristicIconSize + CharacteristicRowGapBelow;
        var iconSummary = CreateIconSummary(blockWidth, lineCount * RowHeight + characteristicRowHeight);
        var icon = CreateIcon(iconSummary);
        icon.Configure(entityId, new Vector2(IconSize, IconSize));

        var y = 0f;

        AddSummaryLine(iconSummary, y, subject.Name);
        y += RowHeight;

        if (subject.RaceName is { } raceName)
        {
            AddSummaryLine(iconSummary, y, $"Race: {raceName}");
            y += RowHeight;
        }

        if (subject.ClassName is { } className)
        {
            AddSummaryLine(iconSummary, y, $"Class: {className}");
            y += RowHeight;
        }

        if (subject.IsUnsimulatedDefaults)
        {
            AddSummaryLine(iconSummary, y, UnsimulatedLabel);
            y += RowHeight;
        }

        if (characteristicIcons.Count > 0)
        {
            AddCharacteristicIconRow(iconSummary, y + CharacteristicRowGapAbove, characteristicIcons);
            y += characteristicRowHeight;
        }

        if (subject.HealthFraction is { } healthFraction)
        {
            AddHealthBar(iconSummary, y, healthFraction);
        }
    }

    /// <summary>The entity's characteristic icons, left to right, each its own tooltip.</summary>
    private void AddCharacteristicIconRow(Window iconSummary, float y, IReadOnlyList<CharacteristicIcon> icons)
    {
        var x = SummaryColumnX;
        var width = SummaryColumnWidth(iconSummary);
        var row = elementPoolService.CreateElement<Window>(iconSummary, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Horizontal, ChildGap = CharacteristicIconGap },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(x, y), Size = new Vector2(width, CharacteristicIconSize), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        row.ContentPadding = Vector2.Zero;
        iconSummary.AddChild(row);

        var iconOptions = new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { Size = new Vector2(CharacteristicIconSize, CharacteristicIconSize), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        };

        foreach (var characteristic in icons)
        {
            Element element;
            if (characteristic.Footprint is { } footprint)
            {
                var footprintIcon = elementPoolService.CreateElement<FootprintIconElement>(row, iconOptions);
                footprintIcon.Configure(footprint, WindowPalette.TitleTextColor);
                element = footprintIcon;
            }
            else
            {
                var symbolIcon = elementPoolService.CreateElement<IconElement>(row, iconOptions);
                symbolIcon.Configure(characteristic.SpriteName, characteristic.Glyph, WindowPalette.TitleTextColor, new Vector2(CharacteristicIconSize, CharacteristicIconSize));
                element = symbolIcon;
            }

            element.ContentPadding = Vector2.Zero;
            element.TooltipText = characteristic.TooltipText;
            row.AddChild(element);
        }
    }

    /// <summary>Where the lines beside the icon start, across the icon summary.</summary>
    private const float SummaryColumnX = IconSize + RowTextGap;

    /// <summary>How wide the lines beside the icon are: the icon summary's width after the icon and its gap.</summary>
    private static float SummaryColumnWidth(Window iconSummary) =>
        System.Math.Max(0f, iconSummary.OriginalSize.X - SummaryColumnX);

    /// <summary>A block's icon with lines beside it, as tall as the icon or the lines, whichever is taller, plus padding above and below; flush with the block across.</summary>
    private Window CreateIconSummary(float blockWidth, float linesHeight)
    {
        var iconSummary = elementPoolService.CreateElement<Window>(_body, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = new Vector2(blockWidth, System.Math.Max(IconSize, linesHeight) + WindowChrome.Padding * 2), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        iconSummary.ContentPadding = new Vector2(0f, WindowChrome.Padding);
        _body.AddChild(iconSummary);
        return iconSummary;
    }

    private EntityIconElement CreateIcon(Window iconSummary)
    {
        var icon = elementPoolService.CreateElement<EntityIconElement>(iconSummary, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(IconSize, IconSize), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        iconSummary.AddChild(icon);
        return icon;
    }

    /// <summary>One line beside the icon, y down the icon summary.</summary>
    private void AddSummaryLine(Window iconSummary, float y, string text)
    {
        var line = elementPoolService.CreateElement<Label>(iconSummary, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(SummaryColumnX, y), Size = new Vector2(SummaryColumnWidth(iconSummary), RowHeight), DisplayMode = ElementDisplayMode.Fixed },
            Text = new TextOptions { Text = text, TextColor = WindowPalette.TitleTextColor },
        });
        iconSummary.AddChild(line);
    }

    /// <summary>The HP bar as one of the lines beside the icon, as wide as them and centred in a line's height.</summary>
    private void AddHealthBar(Window iconSummary, float y, float healthFraction)
    {
        var bar = elementPoolService.CreateElement<FractionBarElement>(iconSummary, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(SummaryColumnX, y + (RowHeight - BarHeight) / 2f), Size = new Vector2(SummaryColumnWidth(iconSummary), BarHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        bar.Configure(healthFraction, hasResource: true, HealthBarPalette.OutlineColor, HealthBarPalette.FractionColor);
        iconSummary.AddChild(bar);
    }

    /// <summary>Text across the block, wrapped to its width -- a description, or an aura's or a shadow's lines; nothing for blank text.</summary>
    private void AddTextBlock(string text, float blockWidth)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var textWindow = elementPoolService.CreateElement<TextWindow>(_body, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { MaximumSize = new Vector2(blockWidth, MaximumTextRowHeight), DisplayMode = ElementDisplayMode.WrapContent },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
            Text = new TextOptions { Text = text, TextColor = WindowPalette.TitleTextColor },
        });
        _body.AddChild(textWindow);
    }

    /// <summary>
    /// Padding between one subject's section and the next, with a 1px divider (a TextDivider line at
    /// SeparatorWidthFraction of the width -- the same fraction the HP row above uses) vertically centered within
    /// it. spacer's outer height grows by WindowChrome.Padding on top and bottom (not externally
    /// constrained, unlike its width) so BlockPadding still means exactly what it always did --
    /// spacer's own padded content height -- and the vertical-centering math below is unaffected.
    /// spacer's outer width IS a hard constraint (must not exceed blockWidth), so the separator's
    /// own width shrinks to fit inside spacer's padded content area instead.
    /// </summary>
    private void BuildSpacer(float blockWidth)
    {
        var spacerHeight = BlockPadding + WindowChrome.Padding * 2;
        var spacer = elementPoolService.CreateElement<Window>(_body, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = new Vector2(blockWidth, spacerHeight), DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
        });
        _body.AddChild(spacer);

        var availableWidth = blockWidth - WindowChrome.Padding * 2;
        var separator = elementPoolService.CreateElement<TextDivider>(spacer, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, (BlockPadding - SeparatorHeight) / 2f), Size = new Vector2(availableWidth, SeparatorHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
        });
        separator.ConfigureAsLine(WindowPalette.TitleTextColor, SeparatorWidthFraction);
        spacer.AddChild(separator);
    }

    /// <summary>Admin Mode's readout of the inspected entity's decisions, between its description and its components: what holds it now, its last decision layer by layer, and the behaviors it decides with.</summary>
    /// <remarks>Only in Admin Mode, and only for an entity that decides -- or a skeleton whose blueprint would. Names it to the decision system as the one entity to trace (NpcDecisionView.Inspect), and lets go of any other.</remarks>
    private void BuildDecisionBlock(int entityId, float blockWidth)
    {
        if (mapViewState.InspectionMode != InspectionMode.Admin || (!npcDecisions.Decides(entityId) && npcDecisions.SetOf(entityId).IsEmpty))
        {
            npcDecisions.Inspect(-1);
            return;
        }

        npcDecisions.Inspect(entityId);
        _decisionWindow = elementPoolService.CreateElement<TextWindow>(_body, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { MaximumSize = new Vector2(blockWidth, MaximumTextRowHeight), DisplayMode = ElementDisplayMode.WrapContent },
            Chrome = new ElementChromeOptions { ShowTitle = true, TitleText = "Decisions", ShowBorder = true, BorderSize = new Vector2(1, 1) },
            Text = new TextOptions { Text = CurrentDecisionText(entityId), TextColor = WindowPalette.TitleTextColor },
        });
        _body.AddChild(_decisionWindow);
    }

    /// <remarks>Every update: the text is rebuilt only when what it shows changed (NpcDecisionText.Key), and UpdateText skips text that didn't.</remarks>
    private void RefreshDecisionBlock(int entityId) => _decisionWindow?.UpdateText(CurrentDecisionText(entityId));

    private string CurrentDecisionText(int entityId) =>
        _decisionText.Get(NpcDecisionText.KeyOf(npcDecisions, entityId), npcDecisions, static (key, view) => NpcDecisionText.Build(key, view));

    /// <summary>Detail/Admin's component dump: one bordered TextWindow per component instance the entity holds, sorted by component name, a row per property (ComponentInspectionView).</summary>
    /// <remarks>A skeleton's dump is what it holds, then -- under the unsimulated label -- what its spawn record rebuilds to; only the first part refreshes, the defaults never change.</remarks>
    private void BuildAdminDump(int entityId, float blockWidth)
    {
        CopyMatchingInstances(entityId);
        _builtInstances.Clear();
        _editableBlocks.Clear();
        _isEditing = IsEditingNow;
        _inspectedKey = componentInspection.KeyOf(entityId);
        foreach (var instance in _instances)
        {
            componentInspection.CopyRows(entityId, instance.ComponentType, instance.Instance, _rows);
            _builtInstances.Add(instance);
            if (_isEditing)
            {
                _editableBlocks.Add(AddEditableBlock(entityId, instance, blockWidth));
            }
            else
            {
                _adminDumpWindows.Add(AddDumpWindow(TitleOf(instance), FormatRows(_rows), blockWidth));
            }
        }

        _liveDumpCount = _builtInstances.Count;

        if (IsSkeleton(entityId))
        {
            AddTextBlock(UnsimulatedLabel, blockWidth);
            componentInspection.CopyUnsimulatedDefaults(entityId, spawnRecordRebuilder, _unsimulatedDefaults);
            foreach (var block in _unsimulatedDefaults)
            {
                if (MatchesSearch(block.Instance.Name))
                {
                    AddDumpWindow(TitleOf(block.Instance), FormatRows(block.Rows), blockWidth);
                }
            }
        }
    }

    private TextWindow AddDumpWindow(string title, string text, float blockWidth)
    {
        var componentWindow = elementPoolService.CreateElement<TextWindow>(_body, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { MaximumSize = new Vector2(blockWidth, MaximumTextRowHeight), DisplayMode = ElementDisplayMode.WrapContent },
            Chrome = new ElementChromeOptions { ShowTitle = true, TitleText = title, ShowBorder = true, BorderSize = new Vector2(1, 1) },
            Text = new TextOptions { Text = text, TextColor = WindowPalette.TitleTextColor },
        });
        _body.AddChild(componentWindow);
        return componentWindow;
    }

    /// <summary>Rewrites only the blocks whose component changed since they were built -- by version, or, for one showing a frame as seconds from now or a derived row, every refresh. A component added or removed rebuilds the whole view.</summary>
    private void RefreshAdminDump(int entityId)
    {
        CopyMatchingInstances(entityId);
        if (!HoldsTheSameInstances())
        {
            _lastDetailEntityId = -1;
            return;
        }

        for (var index = 0; index < _instances.Count; index++)
        {
            var instance = _instances[index];
            if (_isEditing && !_editableBlocks[index].NamesInstanceAt(instance.Instance))
            {
                _lastDetailEntityId = -1;
                return;
            }

            if (instance.Version == _builtInstances[index].Version && !instance.ShowsTime)
            {
                continue;
            }

            componentInspection.CopyRows(entityId, instance.ComponentType, instance.Instance, _rows);
            _builtInstances[index] = instance;
            if (!_isEditing)
            {
                _adminDumpWindows[index].UpdateText(FormatRows(_rows));
            }
            else if (!_editableBlocks[index].TryShow(entityId, _rows))
            {
                _lastDetailEntityId = -1;
                return;
            }
        }
    }

    /// <summary>A component instance as a heading and a field row per property: editable where the table writes it, greyed text where it doesn't.</summary>
    private EditableBlock AddEditableBlock(int entityId, InspectedComponentInstance instance, float blockWidth)
    {
        var header = elementPoolService.CreateElement<FieldSectionHeader>(_body, FieldRowOptions(FieldRow.RowHeight, WindowPalette.HeaderBackground, blockWidth));
        header.Configure(TitleOf(instance), depth: 0, isCollapsible: false, isExpanded: true, buttonLabels: []);
        _body.AddChild(header);

        var block = new EditableBlock(this, componentPropertyEdits.TargetOf(_inspectedKey, instance.ComponentType, instance.Instance));
        foreach (var row in _rows)
        {
            var choices = row.Property is { } property && !row.IsHeading && componentInspection.TryGetChoices(entityId, property.Meaning, out var offered) ? offered : null;
            var description = ComponentPropertyFields.Describe(row.Property, row.Label, row.Depth, row.Value, row.RawValue, IsEditable(row), row.IsHeading, choices);
            var field = elementPoolService.CreateElement<FieldRow>(_body, FieldRowOptions(FieldRow.HeightOf(description), WindowPalette.PanelBackgroundColor, blockWidth));
            field.Configure(description, ComponentPropertyFields.ValueOf(description, row.Property, row.Value, row.RawValue, choices));
            _body.AddChild(field);
            block.Add(new EditableRow(field, row, description, choices));
        }

        block.ShowChangeMarkers();
        return block;
    }

    private static bool IsEditable(in InspectedPropertyRow row) => !row.IsReadOnly && !row.IsHeading && row.Property is not null && row.Path is not null;

    private static ElementOptions FieldRowOptions(float height, Color background, float width)
    {
        var size = new Vector2(width, height);
        return new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = size, MinimumSize = size, MaximumSize = size, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = background },
        };
    }

    /// <summary>A committed field: written through Admin Mode's edits into the instance the block shows, or refused with the reason on the row and its value put back.</summary>
    private void Commit(EditableBlock block, EditableRow editable, FieldValue committed)
    {
        if (!ComponentPropertyFields.TryRead(editable.Description, editable.Row.Property!, committed, editable.Choices, out var written))
        {
            editable.Field.SetProblem("That isn't a value this property takes.", isWarning: false);
            editable.ShowCurrent();
            return;
        }

        var result = componentPropertyEdits.Apply(block.Target, editable.Row.Path!, written);
        editable.Field.SetProblem(result.Refusal, isWarning: false);
        if (result.Edit is not { } edit)
        {
            editable.ShowCurrent();
            return;
        }

        block.Target = componentPropertyEdits.CurrentTargetOf(edit);
        _updatesSinceLastAdminRefresh = AdminDumpRefreshInterval;
        block.ShowChangeMarkers();
    }

    /// <summary>The row's revert button: the latest edit of it written back, or refused with the reason on the row.</summary>
    private void Revert(EditableBlock block, EditableRow editable)
    {
        if (componentPropertyEdits.LatestFor(block.Target, editable.Row.Path!) is not { } edit)
        {
            return;
        }

        var result = componentPropertyEdits.Revert(edit);
        editable.Field.SetProblem(result.Refusal, isWarning: false);
        if (result.Applied)
        {
            block.Target = componentPropertyEdits.CurrentTargetOf(edit);
        }

        _updatesSinceLastAdminRefresh = AdminDumpRefreshInterval;
        block.ShowChangeMarkers();
    }

    /// <summary>One property's field row, the row it was built from, and what it offers to pick from.</summary>
    private sealed class EditableRow(FieldRow field, InspectedPropertyRow row, FieldEditorDescription description, IReadOnlyList<ComponentPropertyChoice>? choices)
    {
        public FieldRow Field { get; } = field;

        public InspectedPropertyRow Row { get; set; } = row;

        public FieldEditorDescription Description { get; } = description;

        public IReadOnlyList<ComponentPropertyChoice>? Choices { get; } = choices;

        public void ShowCurrent() => Field.ShowValue(ComponentPropertyFields.ValueOf(Description, Row.Property, Row.Value, Row.RawValue, Choices));
    }

    /// <summary>One component instance's rows in Admin Mode: shown again when the component changes, marked where an edit still holds.</summary>
    /// <param name="target">The instance it shows, named as the edits name it (ComponentPropertyEdits.TargetOf), so a commit reaches that instance however positions move.</param>
    private sealed class EditableBlock(InspectionWindowContent owner, ComponentPropertyTarget target)
    {
        private readonly List<EditableRow> _rows = [];

        /// <summary>The instance it shows, as its last edit or revert left it.</summary>
        public ComponentPropertyTarget Target { get; set; } = target;

        public void Add(EditableRow editable)
        {
            _rows.Add(editable);
            if (IsEditable(editable.Row))
            {
                editable.Field.Committed += committed => owner.Commit(this, editable, committed);
                editable.Field.RevertRequested += () => owner.Revert(this, editable);
            }
        }

        /// <summary>Whether the entity's instance at <paramref name="instance"/> is still the one this block was built for: the same keyed-timer key, or for one of several, the value it last showed. False once another instance has taken its place, or something other than this block changed it.</summary>
        public bool NamesInstanceAt(int instance)
        {
            if (Target.TimerKey is null && Target.Value is null)
            {
                return true;
            }

            var current = owner.Edits.TargetOf(Target.Entity, Target.ComponentType, instance);
            return current.TimerKey == Target.TimerKey && Equals(current.Value, Target.Value);
        }

        /// <summary>Shows freshly read rows in the fields already built; false when they no longer line up (a row came or went, or changed whether it can be edited), so the view is rebuilt.</summary>
        public bool TryShow(int entityId, List<InspectedPropertyRow> rows)
        {
            if (rows.Count != _rows.Count)
            {
                return false;
            }

            for (var index = 0; index < rows.Count; index++)
            {
                var built = _rows[index].Row;
                if (rows[index].Label != built.Label || rows[index].IsHeading != built.IsHeading || IsEditable(rows[index]) != IsEditable(built)
                    || rows[index].RawValue.IsAbsent != built.RawValue.IsAbsent)
                {
                    return false;
                }
            }

            for (var index = 0; index < rows.Count; index++)
            {
                var editable = _rows[index];
                var built = editable.Row;
                editable.Row = rows[index];
                if (editable.Row.IsHeading)
                {
                    continue;
                }

                if (editable.Row.Value != built.Value)
                {
                    editable.Field.SetHint(ComponentPropertyFields.Describe(editable.Row.Property, editable.Row.Label, editable.Row.Depth, editable.Row.Value, editable.Row.RawValue, IsEditable(editable.Row), isHeading: false, editable.Choices).Hint);
                }

                if (editable.Row.RawValue != built.RawValue || (editable.Description.Kind == FieldValueKind.ReadOnlyText && editable.Row.Value != built.Value))
                {
                    editable.ShowCurrent();
                }
            }

            ShowChangeMarkers();
            return true;
        }

        /// <summary>Marks each row an edit still holds, with what it replaced; its revert says "undo" while no frame has passed since, "revert" once the simulation has moved on.</summary>
        public void ShowChangeMarkers()
        {
            var target = Target;
            foreach (var editable in _rows)
            {
                if (!IsEditable(editable.Row))
                {
                    continue;
                }

                if (owner.Edits.LatestFor(target, editable.Row.Path!) is not { } edit)
                {
                    editable.Field.SetChangeMarker(false, null, null);
                    continue;
                }

                var previous = edit.Previous.ToString();
                var revertTooltip = owner.Edits.FramesHavePassedSince(edit)
                    ? $"Revert to {previous}: the simulation has moved on since the edit, so only the value comes back"
                    : $"Undo: back to {previous}";
                editable.Field.SetChangeMarker(true, $"Edited on frame {edit.Frame}; was {previous}", revertTooltip);
            }
        }
    }

    private bool HoldsTheSameInstances()
    {
        if (_instances.Count != _builtInstances.Count || _builtInstances.Count != _liveDumpCount)
        {
            return false;
        }

        for (var index = 0; index < _instances.Count; index++)
        {
            if (_instances[index].ComponentType != _builtInstances[index].ComponentType || _instances[index].Instance != _builtInstances[index].Instance)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The component's name, and which of the entity's instances of it this is when it holds several.</summary>
    internal static string TitleOf(InspectedComponentInstance instance) =>
        instance.InstanceCount > 1 ? $"{instance.Name} ({instance.Instance + 1} of {instance.InstanceCount})" : instance.Name;

    /// <summary>A row per line, "Label : value", indented two spaces a level; a heading row (a nested or indexed property) has no value.</summary>
    internal static string FormatRows(IReadOnlyList<InspectedPropertyRow> rows)
    {
        if (rows.Count == 0)
        {
            return "(no properties)";
        }

        var text = new System.Text.StringBuilder();
        foreach (var row in rows)
        {
            if (text.Length > 0)
            {
                text.Append('\n');
            }

            text.Append(' ', row.Depth * 2).Append(row.Label);
            if (row.Value.Length > 0)
            {
                text.Append(" : ").Append(row.Value);
            }
        }

        return text.ToString();
    }
}
