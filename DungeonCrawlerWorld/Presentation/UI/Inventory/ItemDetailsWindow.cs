using Engine.Utilities;
using Game.Modules.Actions;
using Game.Modules.Inventory;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;

using Presentation.UI.ColorPalettes;
using Presentation.UI.Details;
using UIEngine.Fonts;
using UIEngine.UI;
using UIEngine.UI.ColorPalettes;

namespace Presentation.UI.Inventory;

/// <summary>
/// Single-item details popup -- shows whatever item ItemDetailsWindowController last opened,
/// updating in place rather than opening a second window (see that controller's own doc comment).
/// The sections it shares with Action Details are ActivatableDetailsSections'; the stack's blocker,
/// the gold value, the shop pricing and Item Details Comparison's colouring are its own.
///
/// Sections, top to bottom, a divider (BuildDivider, a single TextDivider row -- plain when its
/// label is omitted, mirroring InspectionWindowContent.BuildSpacer; labeled for the two sections
/// that have one) before every one after the first: sprite/glyph + name (no header); Effects
/// (labeled divider, then one line per Effect entry, or "None"); Activation (labeled
/// divider, omitted entirely when Activator is null -- a shape-preview grid plus
/// Targeting/Timing/per-activator-type text lines, all sourced
/// from ItemComparisonStatExtraction so single-item rendering and Item Details Comparison's own
/// per-line coloring can never drift out of sync about what lines exist); full
/// Description (no header, omitted when blank -- same convention InspectionWindowContent.
/// BuildDescriptionRow already uses); GoldValue (no header, currency-gold icon + "{n}G", same
/// icon-beside-text shape the name row uses); Shop (labeled divider, then the band table and
/// receipt, only while a shop is open and the item was hovered in a grid that prices it); Tags,
/// comma-separated (no header). Every top-level child
/// tiles vertically via the host window's own ChildElementTileMode.Vertical (see
/// ItemDetailsWindowController.Open), the same convention InspectionWindowContent uses, rather
/// than manual Y math -- only the name row's own icon-beside-text layout needs an explicit
/// RelativePosition, within its own single non-tiled child Window.
///
/// DisplayMode.WrapContent (set by ItemDetailsWindowController.Open, not this class), not Fixed
/// -- this window's own height needs to track content, which varies a lot per item, and a
/// Fixed-mode window whose height shrinks between two Configure calls re-measures its children
/// against its own already-small content size instead of a stable outer budget on every
/// subsequent rebuild, silently clamping a later child's height to 0 once an earlier one's real
/// content pushed it far enough down the column -- confirmed by reproduction (Tags rendering on
/// top of Description). WrapContent's own Measure offers children the most its content area can be
/// within its MaximumSize instead, a stable budget, sidestepping that shrink-feedback loop entirely,
/// the same mechanism Tooltip already relies on for its own auto-height content. Since ContentSize is
/// therefore an *output* of Measure here, not a stable input, every row is built against an
/// explicit contentWidth passed into Configure (the player's own Inventory window's ContentSize.X)
/// instead of reading this window's own ContentSize.X the way InspectionWindowContent's Fixed-mode
/// host safely can.
/// </summary>
public sealed class ItemDetailsWindow(
    ElementServices services,
    EffectTextNames names,
    ActionStateView actionStateView,
    ActivationPreviews activationPreviews,
    IPlayerQuery playerQuery)
    : Window(services), IDetailsPopupWindow
{
    /// <summary>The stack id Configure is given for an item shown from its definition alone (a loot box reward whose stack is gone) -- there's no stack to compare, so Compare does nothing.</summary>
    public const uint NoStackInstanceId = 0;

    private static readonly Color BodyTextColor = ActivatableDetailsSections.BodyTextColor;

    private readonly ActivatableDetailsSections _sections = new(services.ElementPoolService, services.FontService, names);

    private ItemDefinition? _definition;
    private float _contentWidth;
    private IReadOnlyList<ItemDefinition> _comparedAgainst = [];
    private readonly ComparisonLineStyles _comparisonStyles = new(names);
    private bool _childrenReady;
    private int _currentEntityId;
    private uint _currentStackInstanceId;
    private ActivationBlocker _displayedBlocker;
    private int _displayedCooldownSeconds;
    private int? _displayedPreviewStamp;
    private IReadOnlyList<TooltipRow>? _displayedShopPricingRows;
    private bool _displayedComparing;
    private DetailsMode _displayedMode;

    /// <summary>Settable late-bound: the "Compare" button's action, with the item shown -- set only on the anchor pane (ItemDetailsWindowController); a comparison column has no Compare button.</summary>
    public Action<int, uint>? OnCompareRequested { get; set; }

    /// <summary>Settable late-bound: the "Stop Comparison" button's action, shown while IsComparing says a comparison is in progress.</summary>
    public Action? OnStopComparisonRequested { get; set; }

    /// <summary>Settable late-bound: whether a comparison is in progress -- the Stop Comparison button shows while it is.</summary>
    public Func<bool>? IsComparing { get; set; }

    /// <summary>Settable late-bound: the mode the player chose (ItemDetailsWindowController's setting); unset, the window is advanced, as the Workshop's details are.</summary>
    public Func<DetailsMode>? ChosenMode { get; set; }

    /// <summary>Settable late-bound: the mode button's action -- no button when unset.</summary>
    public Action? OnModeToggleRequested { get; set; }

    /// <summary>True for a pane that is only ever shown while comparing (the comparison popup): always simple, as comparison is, and with no Compare or mode button.</summary>
    public bool AlwaysSimple { get; set; }

    /// <summary>The mode the window shows: simple while comparing, otherwise the chosen one.</summary>
    public DetailsMode ShownMode => AlwaysSimple || IsComparingNow() ? DetailsMode.Simple : ChosenMode?.Invoke() ?? DetailsMode.Advanced;

    /// <summary>The mode button's label: the mode it switches to.</summary>
    public static string ModeButtonLabel(DetailsMode shownMode) => shownMode == DetailsMode.Simple ? "Advanced" : "Simple";

    /// <summary>Settable late-bound: called at the end of every Update -- ItemDetailsWindowController's placement and close-on-leave check.</summary>
    public Action? OnUpdated { get; set; }

    /// <summary>Where the shown item's shop pricing comes from -- the grid it was hovered in -- or null; its rows show while a shop is open.</summary>
    public IItemShopPricingSource? ShopPricing { get; private set; }

    /// <summary>
    /// Safe to call both before the first Initialize (a brand-new window -- the real build is
    /// deferred to OnChildrenInitialized) and afterward, on an already-open window now showing a
    /// different item (rebuilds immediately in that case). contentWidth is the target width every
    /// row is built against -- see this class's own doc comment for why it can't just be read
    /// from this window's own ContentSize the way a Fixed-mode host safely could. comparedAgainst
    /// is empty for plain single-item viewing; Item Details Comparison passes every *other*
    /// compared item's own definition here (see ComparisonLineStyles.Resolve) so this item's own lines color
    /// symmetrically against them. entityId/stackInstanceId are this exact stack's own identity --
    /// so the Compare button (if this instance has one) invokes OnCompareRequested with the
    /// item currently shown, and the blocker and shop pricing are read for it. shopPricing is the
    /// grid it was hovered in, or null.
    /// </summary>
    public void Configure(int entityId, uint stackInstanceId, ItemDefinition definition, float contentWidth, IReadOnlyList<ItemDefinition> comparedAgainst = null!, IItemShopPricingSource? shopPricing = null)
    {
        _currentEntityId = entityId;
        _currentStackInstanceId = stackInstanceId;
        _definition = definition;
        _contentWidth = contentWidth;
        _comparedAgainst = comparedAgainst ?? [];
        ShopPricing = shopPricing;

        if (_childrenReady)
        {
            Rebuild();
        }
    }

    /// <summary>Rebuilds when the reason the player can't use the shown item appears, changes or clears -- arms destroyed, mana spent -- when its shop pricing changes, when a comparison starts or stops, or when the player's modifiers or ability scores change what its values are when used, while the window stays open.</summary>
    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (_childrenReady && _definition is not null &&
            (ComputeBlocker() != _displayedBlocker ||
             ComputeCooldownSeconds() != _displayedCooldownSeconds ||
             !ReferenceEquals(ComputeShopPricingRows(), _displayedShopPricingRows) ||
             IsComparingNow() != _displayedComparing ||
             ShownMode != _displayedMode ||
             ComputePreviewStamp() != _displayedPreviewStamp))
        {
            Rebuild();
        }

        OnUpdated?.Invoke();
    }

    private bool IsComparingNow() => IsComparing?.Invoke() == true;

    /// <summary>The player whose values the window shows, or null when there is none (the Workshop's details), which shows each item's own values.</summary>
    private int? PreviewEntityId => playerQuery.PlayerEntityId >= 0 ? playerQuery.PlayerEntityId : null;

    private int? ComputePreviewStamp() => PreviewEntityId is { } entityId ? activationPreviews.ChangeStampOf(entityId) : null;

    /// <summary>definition as the player would use it now, or null with no player.</summary>
    private ActivatablePreview? PreviewOf(ItemDefinition definition) =>
        PreviewEntityId is { } entityId ? activationPreviews.Preview(entityId, definition) : null;

    private IReadOnlyList<TooltipRow>? ComputeShopPricingRows()
    {
        if (ShopPricing is null || _definition is null)
        {
            return null;
        }

        var stackInstanceId = _currentStackInstanceId == NoStackInstanceId ? (uint?)null : _currentStackInstanceId;
        return ShopPricing.ShopPricingRowsFor(new ItemHoverTarget(_currentEntityId, stackInstanceId, _definition));
    }

    /// <summary>Why the player can't use the shown stack right now -- only for the player's own activatable stack; a shop's or a corpse's item isn't the player's to use yet.</summary>
    private ActivationBlocker ComputeBlocker() =>
        _definition?.Activator is not null && _currentStackInstanceId != NoStackInstanceId && _currentEntityId == playerQuery.PlayerEntityId
            ? actionStateView.GetItemBlocker(_currentEntityId, _currentStackInstanceId)
            : ActivationBlocker.None;

    /// <summary>Whole seconds, rounded up, until the shown unit's own cooldown ends; 0 when it isn't cooling down or the stack isn't one held.</summary>
    private int ComputeCooldownSeconds() =>
        _currentStackInstanceId == NoStackInstanceId
            ? 0
            : (int)System.Math.Ceiling(actionStateView.ItemCooldownFramesRemaining(_currentEntityId, _currentStackInstanceId) / (float)GameTiming.FramesPerSecond);

    protected override void OnChildrenInitialized()
    {
        base.OnChildrenInitialized();
        _childrenReady = true;

        Rebuild();
    }


    private void Rebuild()
    {
        ElementPoolService.CloseAllChildren(this);

        if (_definition is null)
        {
            return;
        }

        _displayedPreviewStamp = ComputePreviewStamp();
        _displayedMode = ShownMode;
        var mode = _displayedMode;
        var preview = PreviewOf(_definition);

        _comparisonStyles.Clear();
        foreach (var other in _comparedAgainst)
        {
            _comparisonStyles.Add(other, PreviewOf(other));
        }

        // _contentWidth is the target OUTER width this window should end up matching (see this
        // class's own doc comment) -- since this window is WrapContent (grows to fit its rows plus
        // ContentPadding on both edges, not the reverse), rows themselves must be sized to
        // _contentWidth minus that padding, or the window would end up ContentPadding*2 wider than
        // the caller actually asked for.
        var width = _contentWidth - ContentPadding.X * 2;

        _sections.BuildNameRowWithButtons(this, _definition.SpriteName, _definition.Glyph, _definition.GlyphColor, _definition.Name, width, ComputeButtons());

        if (ToggleText.IsLit(_definition))
        {
            _sections.BuildFixedTextLine(this, width, ToggleText.Active, GameWindowPalette.ToggleActiveText);
        }

        _displayedBlocker = ComputeBlocker();
        if (_displayedBlocker != ActivationBlocker.None)
        {
            _sections.BuildFixedTextLine(this, width, ActivationBlockerText.Describe(_displayedBlocker), ActivationBlockerText.ReasonColor);
        }

        _displayedCooldownSeconds = ComputeCooldownSeconds();
        if (_displayedCooldownSeconds > 0)
        {
            _sections.BuildFixedTextLine(this, width, $"Cooling down: {_displayedCooldownSeconds}s", ActivationBlockerText.ReasonColor);
        }

        _sections.BuildEffectsSection(this, _definition, mode, width, _comparisonStyles.Resolve, preview, activationPreviews.PreviewAura);

        if (ToggleText.IsToggleItem(_definition))
        {
            _sections.BuildFixedTextLine(this, width, ToggleText.ItemPlacement, ActivatableDetailsSections.BodyTextColor);
        }

        _sections.BuildCostSection(this, _definition, width, _comparisonStyles.Resolve, preview);

        if (_definition.Activator is { } activator)
        {
            _sections.BuildActivationSection(this, activator, mode, width, _comparisonStyles.Resolve, _comparisonStyles.ShapeHighlight, preview, isToggle: _definition.Toggle is not null);
            _sections.BuildResourcesSection(this, activator, width, _comparisonStyles.Resolve);
        }

        if (_definition.Contents is { } contents)
        {
            _sections.BuildDivider(this, width, "Opens into", 0.125f);
            foreach (var line in ItemFactsText.Contents(contents, names))
            {
                _sections.BuildFixedTextLine(this, width, line, BodyTextColor);
            }
        }

        _sections.BuildDescriptionSection(this, _definition, width);

        _sections.BuildDivider(this, width);
        _sections.BuildIconTextRow(this, "Currency-Gold", "G", Color.Gold, CurrencyText(mode), width);

        _displayedShopPricingRows = ComputeShopPricingRows();
        if (_displayedShopPricingRows is { Count: > 0 } shopPricingRows)
        {
            BuildShopPricingSection(shopPricingRows, width);
        }

        _sections.BuildDivider(this, width);
        foreach (var restriction in ItemRestrictions.Of(_definition))
        {
            var restrictionLine = _sections.BuildFixedTextLine(this, width, ItemFactsText.NameOf(restriction), BodyTextColor);
            restrictionLine.TooltipText = ItemFactsText.DescriptionOf(restriction);
            restrictionLine.ShowsTooltipIndicator = true;
        }

        _sections.BuildTagsSection(this, _definition.Tags, width, withDivider: false);
    }

    /// <summary>
    /// The value beside the gold icon: the sell price in simple mode, the buy and sell prices in advanced. With a shop
    /// open they are its flat prices for this item (IItemShopPricingSource.ReferencePricesFor), otherwise the item's
    /// GoldValue both ways.
    /// </summary>
    private string CurrencyText(DetailsMode mode)
    {
        var stackInstanceId = _currentStackInstanceId == NoStackInstanceId ? (uint?)null : _currentStackInstanceId;
        var (buy, sell) = ShopPricing?.ReferencePricesFor(new ItemHoverTarget(_currentEntityId, stackInstanceId, _definition!)) ?? (_definition!.GoldValue, _definition.GoldValue);
        return mode == DetailsMode.Simple ? $"Sell price {sell}G" : $"Buy price {buy}G, sell price {sell}G";
    }

    /// <summary>The buttons the name row carries, left to right: Stop Comparison while comparing, otherwise Compare (anchor pane, for a stack that can be compared) and the mode button.</summary>
    private List<(string Label, Action OnClicked)> ComputeButtons()
    {
        _displayedComparing = IsComparingNow();
        var buttons = new List<(string Label, Action OnClicked)>(2);
        if (_displayedComparing && OnStopComparisonRequested is { } stopComparison)
        {
            buttons.Add(("Stop Comparison", stopComparison));
        }

        if (!_displayedComparing && !AlwaysSimple && OnCompareRequested is { } compare && _currentStackInstanceId != NoStackInstanceId && _definition!.Activator is not null)
        {
            var entityId = _currentEntityId;
            var stackInstanceId = _currentStackInstanceId;
            buttons.Add(("Compare", () => compare(entityId, stackInstanceId)));
        }

        if (!_displayedComparing && !AlwaysSimple && OnModeToggleRequested is { } toggleMode)
        {
            buttons.Add((ModeButtonLabel(_displayedMode), toggleMode));
        }

        return buttons;
    }

    /// <summary>"Shop", then the open shop's stock-band table and the receipt for this stack (see InventoryGridContent.ComputeShopRows).</summary>
    private void BuildShopPricingSection(IReadOnlyList<TooltipRow> rows, float width)
    {
        _sections.BuildDivider(this, width, "Shop", 0.125f);

        var table = ElementPoolService.CreateElement<TooltipRowsElement>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { Size = new Vector2(width, TooltipRowsElement.HeightFor(FontService, rows)), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        table.Configure(rows);
        AddChild(table);
    }
}
