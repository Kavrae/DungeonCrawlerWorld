using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Utilities;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.ColorPalettes;

namespace Presentation.UI;

/// <summary>
/// The click-opened detail counterpart to the player health bar's own hover popup
/// (PlayerHealthHoverContent): one section per body part, a resource bar (not text numbers) below
/// each part's own TextDivider header, plus a single Status Effects section above them. Color and
/// section-divider style borrowed from ItemDetailsWindow (dark PanelBackgroundColor background,
/// white body text, TitleColor-labeled TextDivider headers), the same template every future detail
/// window in this codebase should reach for. Status effects render once, not repeated per part --
/// they're entity-scoped only today, not per-body-part (see PLAN-health-window.md's own confirmed
/// decision), so repeating the same list under every part would misleadingly imply six
/// independently-afflicted parts. Collapses to nothing when no effect is active.
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

    private readonly PackedComponentPool<SimpleHealthComponent> _healthPool = componentManager.GetPackedPool<SimpleHealthComponent>();
    private readonly MultiComponentPool<BodyPartComponent> _bodyParts = componentManager.GetMultiPool<BodyPartComponent>();

    // Optional -- see StatModifierMath.GetEffectiveValue's own doc comment for why a null pool
    // (StatModifiersModule not registered) is treated the same as "no active modifiers."
    private readonly MultiComponentPool<StatModifierComponent>? _statModifiers = componentManager.IsRegistered<StatModifierComponent>()
        ? componentManager.GetMultiPool<StatModifierComponent>()
        : null;

    private readonly MultiComponentPool<StatusEffectStack> _statusEffectStacks = componentManager.GetMultiPool<StatusEffectStack>();

    private readonly List<BodyPartRow> _bodyPartRows = [];
    private readonly List<StatusEffectRow> _statusEffectRows = [];
    private readonly List<StatusEffectType> _activeEffectTypesScratch = [];
    private readonly List<FractionBarElement> _bodyPartBars = [];
    private readonly List<TextWindow> _statusEffectRowWindows = [];
    private readonly VersionWatcher _statusEffectVersionWatcher = new();

    private int _entityId;
    private int _framesSinceLastTextRefresh;

    /// <summary>Just records entityId -- must be called after CreateElement but before Initialize, same contract as AbilityScoreWindow.Configure.</summary>
    public void Configure(int entityId) => _entityId = entityId;

    /// <summary>
    /// Built here, not in Configure, for the same reason AbilityScoreWindow.BuildColumns is --
    /// ContentSize isn't real until Element.Initialize's own MeasureAndArrange has run. Primes
    /// the version watcher against this same initial build so Update's own change-check doesn't
    /// immediately see the priming call itself as a change and rebuild a second time -- same
    /// priming AbilityScoreWindow.OnChildrenInitialized does for its own two watchers.
    /// </summary>
    protected override void OnChildrenInitialized()
    {
        base.OnChildrenInitialized();

        RebuildContent();
        _statusEffectVersionWatcher.HasChanged(_statusEffectStacks.GetEntityVersion(_entityId));
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (_statusEffectVersionWatcher.HasChanged(_statusEffectStacks.GetEntityVersion(_entityId)))
        {
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

    /// <summary>Full rebuild -- status effect types actually appearing/disappearing is rare enough (a stack granted/expiring) that closing and re-adding every row is simpler and safer than an in-place structural diff, and keeps the Status Effects section reliably first (see this class's own doc comment).</summary>
    private void RebuildContent()
    {
        elementPoolService.CloseAllChildren(this);
        _statusEffectRowWindows.Clear();
        _bodyPartBars.Clear();

        BuildStatusEffectSection();
        BuildBodyPartSection();
    }

    private void BuildStatusEffectSection()
    {
        BuildStatusEffectRows(_statusEffectRows, _activeEffectTypesScratch, _entityId, _statusEffectStacks, statusEffectDisplays, componentManager);
        if (_statusEffectRows.Count == 0)
        {
            return;
        }

        BuildDivider(ContentSize.X, "Status Effects");

        foreach (var row in _statusEffectRows)
        {
            _statusEffectRowWindows.Add(AddTextRow(FormatStatusEffectRow(row), GetColor(row.Type)));
        }
    }

    private void BuildBodyPartSection()
    {
        BuildBodyPartRows(_bodyPartRows, _entityId, _healthPool, _bodyParts, _statModifiers);

        var width = ContentSize.X;
        for (var index = 0; index < _bodyPartRows.Count; index++)
        {
            if (index > 0)
            {
                AddSpacer(width, BodyPartSpacing);
            }

            var row = _bodyPartRows[index];
            BuildDivider(width, row.Name);
            _bodyPartBars.Add(AddBarRow(width, ComputeFraction(row)));
        }
    }

    /// <summary>Refreshes bar fractions/remaining-duration text in place, by index position -- mirrors InspectionWindowContent.RefreshAdminDump's own "refresh in place, don't rebuild" approach and its same accepted limitation if row count ever drifted between refreshes (it doesn't here -- structural changes go through RebuildContent instead, gated by _statusEffectVersionWatcher above).</summary>
    private void RefreshRowValues()
    {
        BuildBodyPartRows(_bodyPartRows, _entityId, _healthPool, _bodyParts, _statModifiers);
        var bodyPartCount = System.Math.Min(_bodyPartRows.Count, _bodyPartBars.Count);
        for (var index = 0; index < bodyPartCount; index++)
        {
            _bodyPartBars[index].Configure(ComputeFraction(_bodyPartRows[index]), hasResource: true, HealthBarPalette.OutlineColor, HealthBarPalette.FractionColor);
        }

        BuildStatusEffectRows(_statusEffectRows, _activeEffectTypesScratch, _entityId, _statusEffectStacks, statusEffectDisplays, componentManager);
        var statusEffectCount = System.Math.Min(_statusEffectRows.Count, _statusEffectRowWindows.Count);
        for (var index = 0; index < statusEffectCount; index++)
        {
            _statusEffectRowWindows[index].UpdateText(FormatStatusEffectRow(_statusEffectRows[index]));
        }
    }

    private static float ComputeFraction(BodyPartRow row) => row.MaximumHealth > 0 ? MathHelper.Clamp(row.CurrentHealth / row.MaximumHealth, 0f, 1f) : 0f;

    /// <summary>Section-opening divider -- a single labeled TextDivider row, the same 95%-width/12.5%-label-position shape ItemDetailsWindow.BuildDivider's own Effects/Activation headers use, so this window reads as the same visual language.</summary>
    private void BuildDivider(float width, string label)
    {
        var divider = elementPoolService.CreateElement<TextDivider>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { Size = new Vector2(width, RowHeight), MaximumSize = new Vector2(width, UnboundedRowHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        AddChild(divider);
        divider.Configure(label, HeaderTextColor, DividerWidthFraction, DividerLabelTextPosition);
    }

    private TextWindow AddTextRow(string text, Color textColor)
    {
        var row = elementPoolService.CreateElement<TextWindow>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { Size = new Vector2(ContentSize.X, RowHeight), MaximumSize = new Vector2(ContentSize.X, UnboundedRowHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
            Text = new TextOptions { Text = text, TextColor = textColor },
        });
        AddChild(row);
        return row;
    }

    /// <summary>Wraps the actual bar in a full-width, untiled row -- the same shape InspectionWindowContent.BuildHealthRowIfPresent uses -- so the bar itself can be narrower than the row (BarWidthFraction) and centered within it, without disturbing this window's own vertical child tiling (which only ever sees the outer row's full width).</summary>
    private FractionBarElement AddBarRow(float width, float fraction)
    {
        var row = elementPoolService.CreateElement<Window>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = new Vector2(width, BarHeight), MaximumSize = new Vector2(width, UnboundedRowHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        AddChild(row);

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
    private void AddSpacer(float width, float height)
    {
        var spacer = elementPoolService.CreateElement<Window>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { Size = new Vector2(width, height), MaximumSize = new Vector2(width, UnboundedRowHeight), DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
        });
        AddChild(spacer);
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

    internal readonly record struct BodyPartRow(string Name, float CurrentHealth, float MaximumHealth);

    internal readonly record struct StatusEffectRow(StatusEffectType Type, int? RemainingSeconds);

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
            destination.Add(new BodyPartRow("HP", health.CurrentHealth, effectiveMaximum));
            return;
        }

        for (var denseIndex = bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex))
        {
            ref readonly var part = ref bodyParts.GetReadonlyByDenseIndex(denseIndex);
            var effectiveMaximum = StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.MaximumHealth, part.MaximumHealth);
            destination.Add(new BodyPartRow(part.Name, part.CurrentHealth, effectiveMaximum));
        }
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
}
