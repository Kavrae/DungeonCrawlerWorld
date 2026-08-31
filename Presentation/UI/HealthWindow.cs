using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Utilities;
using FontStashSharp;
using Game.Modules;
using Game.Modules.AbilityScores;
using Game.Modules.Burning;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.ColorPalettes;

namespace Presentation.UI;

/// <summary>
/// The click-opened detail counterpart to the player health bar's own hover popup
/// (PlayerHealthHoverContent). Two independently-scrolling columns (built the same "fixed-size
/// sub-window, own ChildElementTileMode.Vertical" way AbilityScoreWindow.BuildColumns builds its
/// own 5): left column is Status Effects then one section per body part (a resource bar below each
/// part's own TextDivider header); right column is Buffs then Debuffs. Color and section-divider
/// style borrowed from ItemDetailsWindow (dark PanelBackgroundColor background, white body text,
/// TitleColor-labeled TextDivider headers), the same template every future detail window in this
/// codebase should reach for. The Status Effects section shows only entity-scoped effects
/// (StatusEffectStack -- Poison, Paralysis, entity-scoped Burning); a body-part-scoped Burning (see
/// PLAN-per-body-part-status-effects.md) instead shows its own line under that one part's own bar,
/// not repeated under every part. Buffs/Debuffs list every active StatModifierComponent on the
/// entity except one targeting an ability score -- those are AbilityScoreWindow's own territory
/// (see AbilityScoreModifierFormatter), shown there instead of duplicated here. The Buffs section
/// also lists every active StatusEffectImmunityComponent (immunity has no Debuffs-section
/// counterpart -- it's unconditionally beneficial). Every section collapses to nothing when
/// nothing is active.
/// </summary>
public sealed class HealthWindow(
    FontService fontService,
    ElementPoolService elementPoolService,
    LabelRenderer labelRenderer,
    ComponentManager componentManager,
    StatusEffectDisplayRegistry statusEffectDisplays)
    : Window(fontService, elementPoolService, labelRenderer)
{
    public static readonly Color BackgroundColor = WindowPalette.PanelBackgroundColor;

    private static readonly Color HeaderTextColor = WindowPalette.TitleColor;
    private static readonly Color BodyTextColor = Color.White;

    private const float RowHeight = 18f;
    private const float BarHeight = 14f;

    /// <summary>Matches InspectionWindowContent.BuildHealthRowIfPresent's own bar-narrower-than-its-row convention, just centered at a wider fraction since this bar has nothing to sit beside (the part's name lives in its own TextDivider header above, not next to the bar).</summary>
    private const float BarWidthFraction = 0.9f;

    /// <summary>Small breathing room between one body part's bar and the next part's own TextDivider header -- not added before the first part or after the last, only between consecutive ones.</summary>
    private const float BodyPartSpacing = 6f;

    /// <summary>Section-divider width fraction and label text position -- the exact values ItemDetailsWindow.BuildDivider's own labeled call sites (Effects/Activation) use, reused here so both windows' headers read as the same visual language.</summary>
    private const float DividerWidthFraction = 0.95f;
    private const float DividerLabelTextPosition = 0.125f;

    /// <summary>A generous, effectively-unlimited per-row height cap -- see InspectionWindowContent.UnboundedChildHeight's own doc comment for why this is needed: without it, a row tiled past this window's own one-screen-tall content size gets silently clamped to nothing.</summary>
    private const float UnboundedRowHeight = 10000f;

    /// <summary>Text-refresh cadence for live remaining-duration numbers and bar fractions -- same cadence InspectionWindowContent's own admin dump uses (AdminDumpRefreshInterval), since both refresh continuously-changing component values without needing to rebuild structure every frame.</summary>
    private const int TextRefreshInterval = 10;

    /// <summary>Gap between the left (Status Effects/Body Parts) and right (Buffs/Debuffs) columns -- same role as AbilityScoreWindow.ColumnGap, just between 2 columns instead of 5.</summary>
    private const float ColumnGap = 6f;

    /// <summary>TextWindow.ContentFont defaults to 12 only the first time a pooled instance is ever truly constructed -- a settable property, not reset by Build/Configure, so a recycled instance keeps whatever size its *previous* consumer last set (see ContextMenu/GridControl/TabbedContent's own ".ContentFont = _font" call sites, each with the same "must match" comment). Cached once and assigned explicitly in AddTextRow so a row's size never depends on what the pool handed back.</summary>
    private readonly SpriteFontBase _bodyFont = fontService.GetFont(12);

    private readonly PackedComponentPool<SimpleHealthComponent> _healthPool = componentManager.GetPackedPool<SimpleHealthComponent>();
    private readonly MultiComponentPool<BodyPartComponent> _bodyParts = componentManager.GetMultiPool<BodyPartComponent>();

    // Optional -- see StatModifierMath.GetEffectiveValue's own doc comment for why a null pool
    // (StatModifiersModule not registered) is treated the same as "no active modifiers."
    private readonly MultiComponentPool<StatModifierComponent>? _statModifiers = componentManager.IsRegistered<StatModifierComponent>()
        ? componentManager.GetMultiPool<StatModifierComponent>()
        : null;

    private readonly MultiComponentPool<StatusEffectStack> _statusEffectStacks = componentManager.GetMultiPool<StatusEffectStack>();

    // Optional -- BurningModule might not be loaded at all (e.g. a minimal test), in which case
    // no body part can ever carry a body-part-scoped burn and every per-part status line below
    // collapses to nothing, same as the entity-scoped section already does with no active effects.
    private readonly MultiComponentPool<BodyPartBurningTimerComponent>? _bodyPartBurningTimers = componentManager.IsRegistered<BodyPartBurningTimerComponent>()
        ? componentManager.GetMultiPool<BodyPartBurningTimerComponent>()
        : null;

    // Optional -- StatusEffectsModule might not register this pool at all (e.g. a minimal test),
    // in which case an entity can never be immune to anything and the immunity rows below always
    // collapse to nothing, same as every other optional pool in this class.
    private readonly MultiComponentPool<StatusEffectImmunityComponent>? _statusEffectImmunities = componentManager.IsRegistered<StatusEffectImmunityComponent>()
        ? componentManager.GetMultiPool<StatusEffectImmunityComponent>()
        : null;

    private readonly List<BodyPartRow> _bodyPartRows = [];
    private readonly List<StatusEffectRow> _statusEffectRows = [];
    private readonly List<ModifierRow> _buffRows = [];
    private readonly List<ModifierRow> _debuffRows = [];
    private readonly List<ImmunityRow> _immunityRows = [];
    private readonly List<StatusEffectType> _activeEffectTypesScratch = [];
    private readonly List<FractionBarElement> _bodyPartBars = [];
    private readonly List<TextWindow> _statusEffectRowWindows = [];
    private readonly List<TextWindow> _buffRowWindows = [];
    private readonly List<TextWindow> _debuffRowWindows = [];
    private readonly List<TextWindow> _immunityRowWindows = [];
    private readonly List<TextWindow?> _bodyPartStatusEffectRowWindows = [];

    // Last-seen structural signature for each section, compared (not version-watched -- see
    // Update's own comment for why) every frame to decide whether a real rebuild is warranted.
    private readonly List<StatusEffectType> _previousActiveEffectTypes = [];
    private readonly List<byte> _activeBurningPartIdsScratch = [];
    private readonly List<byte> _previousActiveBurningPartIds = [];
    private readonly List<ModifierSignature> _activeModifierSignatureScratch = [];
    private readonly List<ModifierSignature> _previousModifierSignature = [];
    private readonly List<StatusEffectType> _activeImmunityTypesScratch = [];
    private readonly List<StatusEffectType> _previousActiveImmunityTypes = [];

    private Window _leftColumn = null!;
    private Window _rightColumn = null!;

    private int _entityId;
    private int _framesSinceLastTextRefresh;

    /// <summary>Just records entityId -- must be called after CreateElement but before Initialize, same contract as AbilityScoreWindow.Configure.</summary>
    public void Configure(int entityId) => _entityId = entityId;

    /// <summary>
    /// Built here, not in Configure, for the same reason AbilityScoreWindow.BuildColumns is --
    /// ContentSize isn't real until Element.Initialize's own MeasureAndArrange has run. Primes
    /// the previous-signature snapshots against this same initial build so Update's own
    /// change-check doesn't immediately see the priming call itself as a change and rebuild a
    /// second time -- same priming AbilityScoreWindow.OnChildrenInitialized does for its own
    /// watchers.
    /// </summary>
    protected override void OnChildrenInitialized()
    {
        base.OnChildrenInitialized();

        BuildColumns();
        RebuildContent();
        StatusEffectQueries.GetActiveEffectTypes(_statusEffectStacks, _entityId, _previousActiveEffectTypes);
        BuildBurningPartIds(_previousActiveBurningPartIds, _bodyParts, _bodyPartBurningTimers, _entityId);
        BuildModifierSignature(_previousModifierSignature, _entityId, _statModifiers);
        BuildActiveImmunityTypes(_previousActiveImmunityTypes, _entityId, _statusEffectImmunities);
    }

    /// <summary>
    /// Two fixed-size, independently-scrolling sub-windows sitting side by side -- built once,
    /// here, not re-created by RebuildContent (only their own children churn as content changes).
    /// Mirrors AbilityScoreWindow.BuildColumns' own reasoning for why this can't happen any
    /// earlier than OnChildrenInitialized (ContentSize isn't real before MeasureAndArrange has
    /// run). Also subscribes to this window's own Resized here (not unsubscribed on close -- see
    /// ElementPoolService.CloseElement's own reflection-based event cleanup) so a user drag-resize
    /// (Element.SetBounds, changing this window's own OriginalSize directly) reflows both columns
    /// too -- see HandleResized's own doc comment for why that's required, not optional polish.
    /// </summary>
    private void BuildColumns()
    {
        var (leftPosition, rightPosition, columnSize) = ComputeColumnLayout();

        _leftColumn = elementPoolService.CreateElement<Window>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
            Layout = new ElementLayoutOptions { RelativePosition = leftPosition, Size = columnSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserScrollVertical = true, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        AddChild(_leftColumn);

        _rightColumn = elementPoolService.CreateElement<Window>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
            Layout = new ElementLayoutOptions { RelativePosition = rightPosition, Size = columnSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserScrollVertical = true, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        AddChild(_rightColumn);

        Resized += HandleResized;
    }

    /// <summary>
    /// Both columns are Fixed-size/Fixed-position -- they don't reflow on their own just because
    /// this window's own ContentSize changed. A user drag-resize sets this window's own
    /// OriginalSize directly (Element.SetBounds, via UiInputController's resize-drag handling),
    /// which only reaches a child as a Measure-time MaximumSize clamp against that child's own
    /// still-stale OriginalSize/RelativePosition -- it never actually moves or regrows the child.
    /// Left unfixed: shrinking this window left the right column's stale (too-large) RelativePosition.X
    /// in place, driving its own available width negative and corrupting its Measure/RetileChildren
    /// pass (the reported bug -- column scrolling breaking, rows overlapping); growing this window
    /// just left dead space with neither column any bigger. Re-running the same layout math on every
    /// Resized and pushing it into both columns via SetBounds mirrors the WrapContent quest
    /// composer's own resize-reflow fix (see TODO.md's own note on that).
    /// </summary>
    private void HandleResized(Element _)
    {
        var (leftPosition, rightPosition, columnSize) = ComputeColumnLayout();

        _leftColumn.SetBounds(leftPosition, columnSize);
        _rightColumn.SetBounds(rightPosition, columnSize);
    }

    private (Vector2 LeftPosition, Vector2 RightPosition, Vector2 ColumnSize) ComputeColumnLayout()
    {
        var columnWidth = (ContentSize.X - ColumnGap) / 2f;
        var columnSize = new Vector2(columnWidth, ContentSize.Y);
        return (Vector2.Zero, new Vector2(columnWidth + ColumnGap, 0), columnSize);
    }

    /// <summary>
    /// Rebuilds only when the *set* of visible rows actually changed (an effect type or a
    /// burning part appearing/disappearing), not on every raw pool mutation -- unlike
    /// MultiComponentPool.GetEntityVersion (what this used to watch via VersionWatcher), which
    /// bumps on every Add *and* Remove, including the routine "consume one stack, decrement the
    /// count" Remove every BurningSystem/PoisonSystem/BodyPartBurningSystem tick already does
    /// while an effect is simply ticking along unchanged. That made RebuildContent (which closes
    /// and recreates every divider/bar/row) fire on every ~1-second tick of any active effect
    /// instead of only on a real grant/expiry, starving the cheaper RefreshRowValues text-only
    /// path of ever actually running while this window was open. GetActiveEffectTypes/
    /// BuildBurningPartIds are both keyed off presence (a type is or isn't active; a part is or
    /// isn't currently burning), so they're stable frame to frame across a tick that only changes
    /// a stack count, and only actually differ on a genuine appearance/disappearance.
    /// </summary>
    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        StatusEffectQueries.GetActiveEffectTypes(_statusEffectStacks, _entityId, _activeEffectTypesScratch);
        BuildBurningPartIds(_activeBurningPartIdsScratch, _bodyParts, _bodyPartBurningTimers, _entityId);
        BuildModifierSignature(_activeModifierSignatureScratch, _entityId, _statModifiers);
        BuildActiveImmunityTypes(_activeImmunityTypesScratch, _entityId, _statusEffectImmunities);

        var statusEffectsChanged = !SequenceEqual(_activeEffectTypesScratch, _previousActiveEffectTypes);
        var bodyPartStatusEffectsChanged = !SequenceEqual(_activeBurningPartIdsScratch, _previousActiveBurningPartIds);
        var modifiersChanged = !SequenceEqual(_activeModifierSignatureScratch, _previousModifierSignature);
        var immunitiesChanged = !SequenceEqual(_activeImmunityTypesScratch, _previousActiveImmunityTypes);
        if (statusEffectsChanged || bodyPartStatusEffectsChanged || modifiersChanged || immunitiesChanged)
        {
            _previousActiveEffectTypes.Clear();
            _previousActiveEffectTypes.AddRange(_activeEffectTypesScratch);
            _previousActiveBurningPartIds.Clear();
            _previousActiveBurningPartIds.AddRange(_activeBurningPartIdsScratch);
            _previousModifierSignature.Clear();
            _previousModifierSignature.AddRange(_activeModifierSignatureScratch);
            _previousActiveImmunityTypes.Clear();
            _previousActiveImmunityTypes.AddRange(_activeImmunityTypesScratch);

            RebuildContent();
            _framesSinceLastTextRefresh = 0;
            return;
        }

        _framesSinceLastTextRefresh++;
        if (_framesSinceLastTextRefresh < TextRefreshInterval)
        {
            return;
        }

        _framesSinceLastTextRefresh = 0;
        RefreshRowValues();
    }

    /// <summary>Full rebuild -- status effect types actually appearing/disappearing is rare enough (a stack granted/expiring) that closing and re-adding every row is simpler and safer than an in-place structural diff. Only clears each column's own children, not the column sub-windows themselves (those are built once by BuildColumns and persist across opens).</summary>
    private void RebuildContent()
    {
        elementPoolService.CloseAllChildren(_leftColumn);
        elementPoolService.CloseAllChildren(_rightColumn);
        _statusEffectRowWindows.Clear();
        _buffRowWindows.Clear();
        _debuffRowWindows.Clear();
        _immunityRowWindows.Clear();
        _bodyPartBars.Clear();
        _bodyPartStatusEffectRowWindows.Clear();

        BuildStatusEffectSection(_leftColumn);
        BuildBodyPartSection(_leftColumn);
        BuildBuffSection(_rightColumn);
        BuildDebuffSection(_rightColumn);
    }

    /// <summary>Fills destination with the PartId of every body part currently showing an active body-part-scoped Burning line -- the per-part-section counterpart to StatusEffectQueries.GetActiveEffectTypes, in the same stable (dense body-part-chain) order every call, so Update's own frame-to-frame comparison only reports a change on a genuine appear/disappear, not on an ordinary tick's stack-count decrement.</summary>
    private static void BuildBurningPartIds(List<byte> destination, MultiComponentPool<BodyPartComponent> bodyParts, MultiComponentPool<BodyPartBurningTimerComponent>? bodyPartBurningTimers, int entityId)
    {
        destination.Clear();

        if (bodyPartBurningTimers is null)
        {
            return;
        }

        for (var denseIndex = bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex))
        {
            var partId = bodyParts.GetReadonlyByDenseIndex(denseIndex).PartId;
            if (TryGetBodyPartBurningLine(bodyPartBurningTimers, entityId, partId, out _, out _))
            {
                destination.Add(partId);
            }
        }
    }

    private static bool SequenceEqual<T>(List<T> first, List<T> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        var comparer = EqualityComparer<T>.Default;
        for (var index = 0; index < first.Count; index++)
        {
            if (!comparer.Equals(first[index], second[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Right column, first section -- every active non-ability-score StatModifierComponent with
    /// Polarity.Buff, one row each (unlike Status Effects' per-type grouping, each modifier
    /// instance is its own row since two modifiers can target the same stat with different
    /// magnitudes/sources), followed by every active StatusEffectImmunityComponent -- immunity is
    /// unambiguously beneficial (see StatusEffectImmunityComponent's own doc comment), so it never
    /// has a Debuffs-section counterpart the way a StatModifierComponent buff/debuff pair does.
    /// </summary>
    private void BuildBuffSection(Window parent)
    {
        BuildModifierRows(_buffRows, _entityId, _statModifiers, StatModifierPolarity.Buff);
        BuildImmunityRows(_immunityRows, _entityId, _statusEffectImmunities);
        if (_buffRows.Count == 0 && _immunityRows.Count == 0)
        {
            return;
        }

        BuildDivider(parent, parent.ContentSize.X, "Buffs");

        foreach (var row in _buffRows)
        {
            _buffRowWindows.Add(AddTextRow(parent, FormatModifierRow(row), GetModifierColor(row.Polarity)));
        }

        foreach (var row in _immunityRows)
        {
            _immunityRowWindows.Add(AddTextRow(parent, FormatImmunityRow(row), GetModifierColor(StatModifierPolarity.Buff)));
        }
    }

    /// <summary>Right column, second section -- same shape as BuildBuffSection, filtered to Polarity.Debuff instead.</summary>
    private void BuildDebuffSection(Window parent)
    {
        BuildModifierRows(_debuffRows, _entityId, _statModifiers, StatModifierPolarity.Debuff);
        if (_debuffRows.Count == 0)
        {
            return;
        }

        BuildDivider(parent, parent.ContentSize.X, "Debuffs");

        foreach (var row in _debuffRows)
        {
            _debuffRowWindows.Add(AddTextRow(parent, FormatModifierRow(row), GetModifierColor(row.Polarity)));
        }
    }

    private void BuildStatusEffectSection(Window parent)
    {
        BuildStatusEffectRows(_statusEffectRows, _activeEffectTypesScratch, _entityId, _statusEffectStacks, statusEffectDisplays, componentManager);
        if (_statusEffectRows.Count == 0)
        {
            return;
        }

        BuildDivider(parent, parent.ContentSize.X, "Status Effects");

        foreach (var row in _statusEffectRows)
        {
            _statusEffectRowWindows.Add(AddTextRow(parent, FormatStatusEffectRow(row), GetColor(row.Type)));
        }
    }

    private void BuildBodyPartSection(Window parent)
    {
        BuildBodyPartRows(_bodyPartRows, _entityId, _healthPool, _bodyParts, _statModifiers);

        var width = parent.ContentSize.X;
        for (var index = 0; index < _bodyPartRows.Count; index++)
        {
            if (index > 0)
            {
                AddSpacer(parent, width, BodyPartSpacing);
            }

            var row = _bodyPartRows[index];
            BuildDivider(parent, width, row.Name);
            _bodyPartBars.Add(AddBarRow(parent, width, ComputeFraction(row)));

            _bodyPartStatusEffectRowWindows.Add(TryGetBodyPartBurningLine(_bodyPartBurningTimers, _entityId, row.PartId, out var text, out var color)
                ? AddTextRow(parent, text, color)
                : null);
        }
    }

    /// <summary>Refreshes bar fractions/remaining-duration text in place, by index position -- mirrors InspectionWindowContent.RefreshAdminDump's own "refresh in place, don't rebuild" approach and its same accepted limitation if row count ever drifted between refreshes (it doesn't here -- structural changes go through RebuildContent instead, gated by Update's own signature comparison above).</summary>
    private void RefreshRowValues()
    {
        BuildBodyPartRows(_bodyPartRows, _entityId, _healthPool, _bodyParts, _statModifiers);
        var bodyPartCount = System.Math.Min(_bodyPartRows.Count, _bodyPartBars.Count);
        for (var index = 0; index < bodyPartCount; index++)
        {
            _bodyPartBars[index].Configure(ComputeFraction(_bodyPartRows[index]), hasResource: true, HealthBarPalette.OutlineColor, HealthBarPalette.FractionColor);

            if (index < _bodyPartStatusEffectRowWindows.Count
                && _bodyPartStatusEffectRowWindows[index] is { } bodyPartStatusEffectRow
                && TryGetBodyPartBurningLine(_bodyPartBurningTimers, _entityId, _bodyPartRows[index].PartId, out var text, out _))
            {
                bodyPartStatusEffectRow.UpdateText(text);
            }
        }

        BuildStatusEffectRows(_statusEffectRows, _activeEffectTypesScratch, _entityId, _statusEffectStacks, statusEffectDisplays, componentManager);
        var statusEffectCount = System.Math.Min(_statusEffectRows.Count, _statusEffectRowWindows.Count);
        for (var index = 0; index < statusEffectCount; index++)
        {
            _statusEffectRowWindows[index].UpdateText(FormatStatusEffectRow(_statusEffectRows[index]));
        }

        BuildModifierRows(_buffRows, _entityId, _statModifiers, StatModifierPolarity.Buff);
        var buffCount = System.Math.Min(_buffRows.Count, _buffRowWindows.Count);
        for (var index = 0; index < buffCount; index++)
        {
            _buffRowWindows[index].UpdateText(FormatModifierRow(_buffRows[index]));
        }

        BuildModifierRows(_debuffRows, _entityId, _statModifiers, StatModifierPolarity.Debuff);
        var debuffCount = System.Math.Min(_debuffRows.Count, _debuffRowWindows.Count);
        for (var index = 0; index < debuffCount; index++)
        {
            _debuffRowWindows[index].UpdateText(FormatModifierRow(_debuffRows[index]));
        }

        BuildImmunityRows(_immunityRows, _entityId, _statusEffectImmunities);
        var immunityCount = System.Math.Min(_immunityRows.Count, _immunityRowWindows.Count);
        for (var index = 0; index < immunityCount; index++)
        {
            _immunityRowWindows[index].UpdateText(FormatImmunityRow(_immunityRows[index]));
        }
    }

    private static float ComputeFraction(BodyPartRow row) => row.MaximumHealth > 0 ? MathHelper.Clamp(row.CurrentHealth / row.MaximumHealth, 0f, 1f) : 0f;

    /// <summary>Section-opening divider -- a single labeled TextDivider row, the same 95%-width/12.5%-label-position shape ItemDetailsWindow.BuildDivider's own Effects/Activation headers use, so this window reads as the same visual language. Added to parent (one of the two columns), not this window directly -- see BuildColumns' own doc comment.</summary>
    private void BuildDivider(Window parent, float width, string label)
    {
        var divider = elementPoolService.CreateElement<TextDivider>(parent, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { Size = new Vector2(width, RowHeight), MaximumSize = new Vector2(width, UnboundedRowHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        parent.AddChild(divider);
        divider.Configure(label, HeaderTextColor, DividerWidthFraction, DividerLabelTextPosition);
    }

    private TextWindow AddTextRow(Window parent, string text, Color textColor)
    {
        var row = elementPoolService.CreateElement<TextWindow>(parent, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { Size = new Vector2(parent.ContentSize.X, RowHeight), MaximumSize = new Vector2(parent.ContentSize.X, UnboundedRowHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
            Text = new TextOptions { Text = text, TextColor = textColor },
        });
        row.ContentFont = _bodyFont; // Must match the font Size/MaximumSize above were computed for -- see _bodyFont's own doc comment for why this can't be left to TextWindow's own default.
        parent.AddChild(row);
        return row;
    }

    /// <summary>Wraps the actual bar in a full-width, untiled row -- the same shape InspectionWindowContent.BuildHealthRowIfPresent uses -- so the bar itself can be narrower than the row (BarWidthFraction) and centered within it, without disturbing this window's own vertical child tiling (which only ever sees the outer row's full width).</summary>
    private FractionBarElement AddBarRow(Window parent, float width, float fraction)
    {
        var row = elementPoolService.CreateElement<Window>(parent, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = new Vector2(width, BarHeight), MaximumSize = new Vector2(width, UnboundedRowHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        parent.AddChild(row);

        var barWidth = width * BarWidthFraction;
        var barX = (width - barWidth) / 2f;

        var bar = elementPoolService.CreateElement<FractionBarElement>(row, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(barX, 0), Size = new Vector2(barWidth, BarHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        row.AddChild(bar);
        bar.Configure(fraction, hasResource: true, HealthBarPalette.OutlineColor, HealthBarPalette.FractionColor);
        return bar;
    }

    /// <summary>Blank vertical gap, no divider line -- BuildDivider already opens the next section, so a second line here would be redundant clutter, unlike InspectionWindowContent.BuildSpacer's own SeparatorBar (which has no adjacent divider to lean on).</summary>
    private void AddSpacer(Window parent, float width, float height)
    {
        var spacer = elementPoolService.CreateElement<Window>(parent, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { Size = new Vector2(width, height), MaximumSize = new Vector2(width, UnboundedRowHeight), DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
        });
        parent.AddChild(spacer);
    }

    private string FormatStatusEffectRow(StatusEffectRow row) =>
        row.RemainingSeconds is { } seconds
            ? $"{GetGlyph(row.Type)} {row.Type}: {seconds}s"
            : $"{GetGlyph(row.Type)} {row.Type}";

    /// <summary>Looks up each effect module's own registered glyph (see IStatusEffectDisplay). Same fallback philosophy as PlayerStatusEffectsContent.GetGlyph -- "?" is an intentionally-visible stand-in for any active effect type with no registered display, rather than throwing mid-build.</summary>
    private string GetGlyph(StatusEffectType effectType) => statusEffectDisplays.TryGet(effectType, out var display) ? display.Glyph : "?";

    /// <summary>Same reasoning/fallback as GetGlyph above -- kept as its own switch rather than folded into GetGlyph so each is a single, simple type -> value mapping. Poison brightened from PlayerStatusEffectsContent's own DarkGreen -- fine against that content's white icon tile, unreadable against this window's own dark PanelBackgroundColor.</summary>
    private static Color GetColor(StatusEffectType effectType) => effectType switch
    {
        StatusEffectType.Burning => Color.Red,
        StatusEffectType.Poison => Color.LightGreen,
        StatusEffectType.Paralysis => Color.Yellow,
        _ => BodyTextColor,
    };

    /// <summary>
    /// Several Target/Operation combinations have their own named, human-readable form instead of
    /// the generic sign+magnitude+enum-name fallback: Multiplicative IncomingDamage reads as a
    /// percentage-based Resistance/Vulnerability (e.g. ResistanceTestPotion's own
    /// ConditionTag: Tag.Poison grant -- "50% Poison Resistance" instead of "x-0.5 IncomingDamage");
    /// OutgoingDamage (either operation) as "Damage" -- Additive flat ("+2 Damage"/"-1 Damage",
    /// e.g. PlayerBlueprint's own buff), Multiplicative as a percentage ("-50% Damage", e.g.
    /// BodyPartEffectsSystem's own Arm/Hand-damage melee debuff); Multiplicative MaximumHealth the
    /// same percentage way as a "+50% Health"; MovementLockFrames (either operation) as a
    /// "Movement Penalty" --
    /// see FormatMovementPenalty's own doc comment for why that one keeps a literal "x" instead of
    /// converting to a percentage the way MaximumHealth does. Every other Target/Operation
    /// combination keeps the generic form, reusing StatModifierComponent.ToString()'s own +/-/x/÷
    /// sign convention (Additive Buff/Debuff = +/-, Multiplicative Buff/Debuff = x/÷) so it still
    /// reads the same way a debug dump of the same component does.
    /// </summary>
    internal static string FormatModifierRow(ModifierRow row)
    {
        if (row.Target == StatModifierTarget.IncomingDamage && row.Operation == StatModifierOperation.Multiplicative)
        {
            return FormatIncomingDamageResistance(row);
        }

        if (row.Target == StatModifierTarget.OutgoingDamage)
        {
            // Additive (e.g. PlayerBlueprint's own flat OutgoingDamage buff) reads as a flat
            // "+2 Damage"; Multiplicative (e.g. BodyPartEffectsSystem's own Melee-tagged Arm/Hand-
            // damage debuff, ConditionTag: Tag.Melee -- "-100% Melee Damage") reads as a percentage
            // instead, same "Damage" wording either way -- same dual-mode split MaximumHealth/Health
            // uses below.
            return row.Operation == StatModifierOperation.Additive
                ? FormatSignedFlatValue(row.Magnitude, "Damage", row.ConditionTag, row.RemainingSeconds)
                : FormatSignedPercentage(row.Magnitude, "Damage", row.ConditionTag, row.RemainingSeconds);
        }

        if (row.Target == StatModifierTarget.MaximumHealth && row.Operation == StatModifierOperation.Multiplicative)
        {
            return FormatSignedPercentage(row.Magnitude, "Health", row.ConditionTag, row.RemainingSeconds);
        }

        if (row.Target == StatModifierTarget.MovementLockFrames)
        {
            return FormatMovementPenalty(row);
        }

        var sign = row.Operation == StatModifierOperation.Additive
            ? row.Polarity == StatModifierPolarity.Buff ? '+' : '-'
            : row.Polarity == StatModifierPolarity.Buff ? 'x' : '÷';

        var text = $"{sign}{FormatMagnitude(row.Magnitude)} {WithTagPrefix(row.ConditionTag, row.Target.ToString())}";
        return row.RemainingSeconds is { } seconds ? $"{text}: {FormatRemainingDuration(seconds)}" : text;
    }

    /// <summary>Prepends "{Tag} " to subject when a modifier is scoped to a specific ConditionTag (e.g. "Melee Damage" for a Tag.Melee-scoped OutgoingDamage change) -- an untagged modifier applies broadly and needs no such qualifier.</summary>
    private static string WithTagPrefix(Tag? conditionTag, string subject) => conditionTag is { } tag ? $"{tag} {subject}" : subject;

    /// <summary>A negative magnitude is a reduction ("Resistance," reads as a Buff); positive is an increase ("Vulnerability," reads as a Debuff) -- keyed off the magnitude's own sign rather than Polarity, since Polarity is just the same fact restated for coloring. ConditionTag names which damage type it applies to (Poison, Fire, ...); an untagged modifier applies to all incoming damage, labeled generically as "Damage."</summary>
    private static string FormatIncomingDamageResistance(ModifierRow row)
    {
        var percentage = (int)System.MathF.Round(System.MathF.Abs(row.Magnitude) * 100f);
        var subject = row.ConditionTag is { } tag ? tag.ToString() : "Damage";
        var label = row.Magnitude < 0 ? "Resistance" : "Vulnerability";

        var text = $"{percentage}% {subject} {label}";
        return row.RemainingSeconds is { } seconds ? $"{text}: {FormatRemainingDuration(seconds)}" : text;
    }

    /// <summary>A flat "+N Subject"/"-N Subject" reading (or "+N Tag Subject" when conditionTag is set, e.g. "-1 Melee Damage") -- sign taken from the magnitude's own value (matching how content actually encodes a reduction as a negative number, e.g. a -1 Additive OutgoingDamage grant) rather than recomputed from Operation/Polarity, same reasoning as FormatIncomingDamageResistance's own sign.</summary>
    private static string FormatSignedFlatValue(float magnitude, string subject, Tag? conditionTag, int? remainingSeconds)
    {
        var sign = magnitude >= 0 ? "+" : "";
        var text = $"{sign}{FormatMagnitude(magnitude)} {WithTagPrefix(conditionTag, subject)}";
        return remainingSeconds is { } seconds ? $"{text}: {FormatRemainingDuration(seconds)}" : text;
    }

    /// <summary>Same shape as FormatSignedFlatValue, but for a Multiplicative modifier whose magnitude is a fraction added to 1 (see StatModifierMath.CalculateTotal) -- converted to the percentage a player actually reads it as, e.g. magnitude 0.5 -> "+50%", or magnitude -1 with conditionTag Tag.Melee -> "-100% Melee Damage".</summary>
    private static string FormatSignedPercentage(float magnitude, string subject, Tag? conditionTag, int? remainingSeconds)
    {
        var percentage = (int)System.MathF.Round(magnitude * 100f);
        var sign = percentage >= 0 ? "+" : "";
        var text = $"{sign}{percentage}% {WithTagPrefix(conditionTag, subject)}";
        return remainingSeconds is { } seconds ? $"{text}: {FormatRemainingDuration(seconds)}" : text;
    }

    /// <summary>
    /// Unlike MaximumHealth/OutgoingDamage, MovementLockFrames is never converted to a percentage
    /// -- "x10" (a literal 10x multiplier on how long an action locks) reads more naturally than
    /// "+900% Movement Penalty" would. Sign comes from Operation alone, not Polarity: an Additive
    /// grant's own magnitude sign already says whether it adds or subtracts lock frames, but a
    /// Multiplicative one is always shown as "x," never "÷" -- BodyPartEffectsSystem's own debuff
    /// here multiplies lock frames *up* (slower, worse), and "÷" would misleadingly suggest the
    /// opposite (dividing lock frames down, faster) even though that's exactly what the old generic
    /// Multiplicative-Debuff-means-÷ convention would have shown. "Penalty" in the label already
    /// says this is bad, so there's no separate "bonus" wording for a hypothetical future speed-up
    /// grant -- add one if/when non-penalty content actually exists (see TODO.md's Dexterity item).
    /// </summary>
    private static string FormatMovementPenalty(ModifierRow row)
    {
        var sign = row.Operation == StatModifierOperation.Multiplicative
            ? "x"
            : row.Magnitude >= 0 ? "+" : "";

        var text = $"{sign}{FormatMagnitude(row.Magnitude)} {WithTagPrefix(row.ConditionTag, "Movement Penalty")}";
        return row.RemainingSeconds is { } seconds ? $"{text}: {FormatRemainingDuration(seconds)}" : text;
    }

    /// <summary>Minutes, rounded, once a duration reaches a full minute -- seconds alone past that point (e.g. "600s") are harder to read at a glance than "10min."</summary>
    private static string FormatRemainingDuration(int seconds) =>
        seconds >= 60
            ? $"{(int)System.MathF.Round(seconds / 60f)}min"
            : $"{seconds}s";

    /// <summary>Rounds a raw (non-percentage) magnitude to at most one decimal place -- "0.#" rather than "0.0" so a whole number still reads as "2," not "2.0," while a genuine fraction reads as "2.3" instead of float's own full-precision ToString (e.g. "2.3000002" for a value that only looks clean in source).</summary>
    private static string FormatMagnitude(float magnitude) => magnitude.ToString("0.#");

    /// <summary>Buff/debuff color is keyed off Polarity alone, not Target -- unlike status effects there's no per-type identity to color by, just "helping" vs "hurting."</summary>
    private static Color GetModifierColor(StatModifierPolarity polarity) => polarity == StatModifierPolarity.Buff ? Color.LightGreen : Color.Red;

    internal static string FormatImmunityRow(ImmunityRow row)
    {
        var text = $"{GetImmunityDisplayName(row.EffectType)} Immunity";
        return row.RemainingSeconds is { } seconds ? $"{text}: {FormatRemainingDuration(seconds)}" : text;
    }

    /// <summary>Matches the elemental vocabulary FormatIncomingDamageResistance already reads a ConditionTag through (Tag.Fire/Tag.Poison) rather than StatusEffectType's own enum name -- Burning immunity and Fire resistance describe the same damage type, so both should say "Fire," not one saying "Burning" and the other "Fire." Every other type has no separate elemental-tag identity, so its own enum name already reads correctly (Poison, Paralysis).</summary>
    private static string GetImmunityDisplayName(StatusEffectType effectType) => effectType switch
    {
        StatusEffectType.Burning => "Fire",
        _ => effectType.ToString(),
    };

    internal readonly record struct BodyPartRow(string Name, float CurrentHealth, float MaximumHealth, byte PartId);

    internal readonly record struct StatusEffectRow(StatusEffectType Type, int? RemainingSeconds);

    internal readonly record struct ModifierRow(StatModifierTarget Target, StatModifierOperation Operation, StatModifierPolarity Polarity, float Magnitude, Tag? ConditionTag, int? RemainingSeconds);

    /// <summary>Identity used only to detect a modifier appearing/disappearing (see Update's own comment on why RemainingSeconds is excluded here -- ticking down every frame would otherwise look like a structural change every frame).</summary>
    private readonly record struct ModifierSignature(StatModifierTarget Target, StatModifierOperation Operation, StatModifierPolarity Polarity, float Magnitude, Tag? ConditionTag, StatusEffectSource Source);

    internal readonly record struct ImmunityRow(StatusEffectType EffectType, int? RemainingSeconds);

    /// <summary>
    /// Pure data assembly, no rendering -- split out so a test can assert row assembly headlessly,
    /// without a GraphicsDevice-backed SpriteBatch (mirrors InspectionWindowContent.
    /// ReplaceHealthEntriesWithEffectiveMaximum/PlayerHealthHoverContent.BuildRows's own shape).
    /// Simple-first-else-Complex dispatch mirrors HealthQueries.TryGetTotals, but (unlike that
    /// summed total) keeps each body part as its own row. Every current/maximum value is run
    /// through StatModifierMath.GetEffectiveValue -- never a raw MaximumHealth field -- so a
    /// MaximumHealth buff always reads correctly here.
    /// </summary>
    internal static void BuildBodyPartRows(
        List<BodyPartRow> destination,
        int entityId,
        PackedComponentPool<SimpleHealthComponent> healthPool,
        MultiComponentPool<BodyPartComponent> bodyParts,
        MultiComponentPool<StatModifierComponent>? statModifiers)
    {
        destination.Clear();

        if (healthPool.TryGetReadonly(entityId, out var health))
        {
            var effectiveMaximum = StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.MaximumHealth, health.MaximumHealth);
            destination.Add(new BodyPartRow("HP", health.CurrentHealth, effectiveMaximum, PartId: 0));
            return;
        }

        for (var denseIndex = bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex))
        {
            ref readonly var part = ref bodyParts.GetReadonlyByDenseIndex(denseIndex);
            var effectiveMaximum = StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.MaximumHealth, part.MaximumHealth);
            destination.Add(new BodyPartRow(part.Name, part.CurrentHealth, effectiveMaximum, part.PartId));
        }
    }

    /// <summary>True (with a formatted line, same glyph+name+duration format FormatStatusEffectRow already uses for the entity-scoped section) if entityId's partId currently has an active body-part-scoped Burning timer.</summary>
    /// <remarks>
    /// Pure data assembly, no rendering -- see BuildBodyPartRows' own doc comment for why (static,
    /// explicit parameters, directly testable). Reads BodyPartBurningTimerComponent's own
    /// FramesUntilNextTick/StackCount directly (the same formula BurningModule registers for the
    /// entity-scoped BurningTimerComponent case) rather than through IStatusEffectDisplay, since
    /// that interface's GetRemainingDurationFrames takes no partId. bodyPartBurningTimers is
    /// nullable -- BurningModule might not be loaded at all (see this class's own field doc comment).
    /// </remarks>
    internal static bool TryGetBodyPartBurningLine(MultiComponentPool<BodyPartBurningTimerComponent>? bodyPartBurningTimers, int entityId, byte partId, out string text, out Color color)
    {
        text = string.Empty;
        color = BodyTextColor;

        if (bodyPartBurningTimers is null)
        {
            return false;
        }

        for (var denseIndex = bodyPartBurningTimers.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyPartBurningTimers.GetNextDenseIndex(denseIndex))
        {
            ref readonly var timer = ref bodyPartBurningTimers.GetReadonlyByDenseIndex(denseIndex);
            if (timer.PartId != partId)
            {
                continue;
            }

            var remainingFrames = timer.FramesUntilNextTick + (timer.StackCount - 1) * BurningEffects.TickIntervalFrames;
            var remainingSeconds = (int)System.Math.Ceiling(remainingFrames / (float)GameTiming.FramesPerSecond);
            text = $"{BurningEffects.Glyph} {StatusEffectType.Burning}: {remainingSeconds}s";
            color = GetColor(StatusEffectType.Burning);
            return true;
        }

        return false;
    }

    /// <summary>Pure data assembly, no rendering -- see BuildBodyPartRows' own doc comment for why. One row per StatusEffectQueries.GetActiveEffectTypes entry, each with its own remaining duration in real seconds (null if the active type has no registered IStatusEffectDisplay -- a future effect type with no display registered yet).</summary>
    internal static void BuildStatusEffectRows(
        List<StatusEffectRow> destination,
        List<StatusEffectType> activeTypesScratch,
        int entityId,
        MultiComponentPool<StatusEffectStack> statusEffectStacks,
        StatusEffectDisplayRegistry statusEffectDisplays,
        ComponentManager componentManager)
    {
        StatusEffectQueries.GetActiveEffectTypes(statusEffectStacks, entityId, activeTypesScratch);

        destination.Clear();
        foreach (var effectType in activeTypesScratch)
        {
            var remainingFrames = statusEffectDisplays.TryGet(effectType, out var display)
                ? display.GetRemainingDurationFrames(componentManager, entityId)
                : null;
            var remainingSeconds = remainingFrames is { } frames ? (int)System.Math.Ceiling(frames / (float)GameTiming.FramesPerSecond) : (int?)null;
            destination.Add(new StatusEffectRow(effectType, remainingSeconds));
        }
    }

    /// <summary>Pure data assembly, no rendering -- see BuildBodyPartRows' own doc comment for why. One row per active StatModifierComponent instance matching polarity (not grouped by Target the way Status Effects groups by type) -- statModifiers is nullable the same way it is everywhere else in this class (StatModifiersModule might not be registered). Excludes any modifier targeting an ability score -- AbilityScoreMath.FromStatModifierTarget returning non-null is exactly AbilityScoreWindow's own test for "this is one of the 7 ability score targets" (see AbilityScoreModifierFormatter), which already shows these; duplicating them here would be redundant.</summary>
    internal static void BuildModifierRows(List<ModifierRow> destination, int entityId, MultiComponentPool<StatModifierComponent>? statModifiers, StatModifierPolarity polarity)
    {
        destination.Clear();

        if (statModifiers is null)
        {
            return;
        }

        for (var denseIndex = statModifiers.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = statModifiers.GetNextDenseIndex(denseIndex))
        {
            ref readonly var modifier = ref statModifiers.GetReadonlyByDenseIndex(denseIndex);
            if (modifier.Polarity != polarity || AbilityScoreMath.FromStatModifierTarget(modifier.Target) is not null)
            {
                continue;
            }

            var remainingSeconds = modifier.RemainingDurationFrames is { } frames ? (int)System.Math.Ceiling(frames / (float)GameTiming.FramesPerSecond) : (int?)null;
            destination.Add(new ModifierRow(modifier.Target, modifier.Operation, modifier.Polarity, modifier.Magnitude, modifier.ConditionTag, remainingSeconds));
        }
    }

    /// <summary>Signature-only counterpart to BuildModifierRows -- see BuildBurningPartIds' own doc comment for why a separate, RemainingSeconds-free pass is needed for Update's own frame-to-frame structural-change comparison. Covers both polarities at once (either one changing warrants the same full rebuild), same ability-score exclusion as BuildModifierRows.</summary>
    private static void BuildModifierSignature(List<ModifierSignature> destination, int entityId, MultiComponentPool<StatModifierComponent>? statModifiers)
    {
        destination.Clear();

        if (statModifiers is null)
        {
            return;
        }

        for (var denseIndex = statModifiers.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = statModifiers.GetNextDenseIndex(denseIndex))
        {
            ref readonly var modifier = ref statModifiers.GetReadonlyByDenseIndex(denseIndex);
            if (AbilityScoreMath.FromStatModifierTarget(modifier.Target) is not null)
            {
                continue;
            }

            destination.Add(new ModifierSignature(modifier.Target, modifier.Operation, modifier.Polarity, modifier.Magnitude, modifier.ConditionTag, modifier.Source));
        }
    }

    /// <summary>Pure data assembly, no rendering -- see BuildBodyPartRows' own doc comment for why. One row per active StatusEffectImmunityComponent (an entity is either immune to a type or it isn't -- no polarity/magnitude to filter or format beyond which type and how long). statusEffectImmunities is nullable the same way every other optional pool in this class is (StatusEffectsModule might not register it).</summary>
    internal static void BuildImmunityRows(List<ImmunityRow> destination, int entityId, MultiComponentPool<StatusEffectImmunityComponent>? statusEffectImmunities)
    {
        destination.Clear();

        if (statusEffectImmunities is null)
        {
            return;
        }

        for (var denseIndex = statusEffectImmunities.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = statusEffectImmunities.GetNextDenseIndex(denseIndex))
        {
            ref readonly var immunity = ref statusEffectImmunities.GetReadonlyByDenseIndex(denseIndex);
            var remainingSeconds = immunity.RemainingDurationFrames is { } frames ? (int)System.Math.Ceiling(frames / (float)GameTiming.FramesPerSecond) : (int?)null;
            destination.Add(new ImmunityRow(immunity.EffectType, remainingSeconds));
        }
    }

    /// <summary>Signature-only counterpart to BuildImmunityRows -- see BuildBurningPartIds' own doc comment for why a separate, RemainingSeconds-free pass is needed for Update's own frame-to-frame structural-change comparison.</summary>
    private static void BuildActiveImmunityTypes(List<StatusEffectType> destination, int entityId, MultiComponentPool<StatusEffectImmunityComponent>? statusEffectImmunities)
    {
        destination.Clear();

        if (statusEffectImmunities is null)
        {
            return;
        }

        for (var denseIndex = statusEffectImmunities.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = statusEffectImmunities.GetNextDenseIndex(denseIndex))
        {
            destination.Add(statusEffectImmunities.GetReadonlyByDenseIndex(denseIndex).EffectType);
        }
    }
}
