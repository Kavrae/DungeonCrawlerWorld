using Engine.Tags;
using Game.Modules.Actions;
using Game.Modules.Inventory;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;

using Presentation.UI.ColorPalettes;
using Presentation.UI.Details;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Presentation.UI.Inventory;

/// <summary>
/// Persistent single-item details pane -- shows whatever item ItemDetailsWindowController last
/// selected, updating in place rather than opening a second window when a different item is
/// clicked (see that controller's own doc comment). Close-only (no minimize), the same shape as
/// InventoryManagementWindow/SecondaryInventoryWindow, not InspectionWindow (minimize-only). The
/// sections it shares with Action Details are ActivatableDetailsSections'; the stack's blocker, the
/// gold value and Item Details Comparison's colouring are its own.
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
/// icon-beside-text shape the name row uses); Tags, comma-separated (no header). Every top-level child
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
/// top of Description). WrapContent's own Measure path always threads its MaximumSize through to
/// children unchanged instead, sidestepping that shrink-feedback loop entirely, the same
/// mechanism Tooltip already relies on for its own auto-height content. Since ContentSize is
/// therefore an *output* of Measure here, not a stable input, every row is built against an
/// explicit contentWidth passed into Configure (the player's own Inventory window's ContentSize.X)
/// instead of reading this window's own ContentSize.X the way InspectionWindowContent's Fixed-mode
/// host safely can.
/// </summary>
public sealed class ItemDetailsWindow(
    FontService fontService,
    ElementPoolService elementPoolService,
    LabelRenderer labelRenderer,
    ActionCatalog actionCatalog,
    GameplayTagRegistry gameplayTags,
    ActionStateView actionStateView,
    IPlayerQuery playerQuery)
    : Window(fontService, elementPoolService, labelRenderer)
{
    /// <summary>The stack id Configure is given for an item shown from its definition alone (a loot box reward whose stack is gone) -- there's no stack to compare, so Compare does nothing.</summary>
    public const uint NoStackInstanceId = 0;

    private static readonly Color BodyTextColor = ActivatableDetailsSections.BodyTextColor;

    /// <summary>Item Details Comparison's own per-line coloring -- Better doubles as the "this item has a stat at least one other compared item doesn't" exclusive marker too (see ResolveLineColor's own doc comment), so the same color means "advantage" either way.</summary>
    private static readonly Color BetterColor = Color.LightGreen;

    /// <summary>LightCoral, not IndianRed -- confirmed live that IndianRed's (205,92,92) read too dark/muted against the near-black panel background, hard to distinguish from BodyTextColor at a glance. LightCoral pairs naturally with BetterColor's own "Light" prefix and is meaningfully brighter (perceived luminance ~162 vs. ~126).</summary>
    private static readonly Color WorseColor = Color.LightCoral;

    private readonly ActivatableDetailsSections _sections = new(elementPoolService, fontService, actionCatalog, gameplayTags);

    private ItemDefinition? _definition;
    private float _contentWidth;
    private IReadOnlyList<ItemDefinition> _comparedAgainst = [];
    private readonly List<IReadOnlyList<ItemComparisonStat>> _otherItemsStats = [];
    private bool _childrenReady;
    private int _currentEntityId;
    private uint _currentStackInstanceId;
    private ActivationBlocker _displayedBlocker;

    /// <summary>Settable late-bound callback for this window's own "Compare" title button, if it has one -- see this class's own Initialize override for why only some instances do. Wired by whichever controller creates this instance (ItemDetailsWindowController for the anchor pane; ItemComparisonController deliberately never sets this for its own comparison columns) *before* calling Initialize.</summary>
    public Action<int, uint>? OnCompareRequested { get; set; }

    /// <summary>
    /// Safe to call both before the first Initialize (a brand-new window -- the real build is
    /// deferred to OnChildrenInitialized) and afterward, on an already-open window now showing a
    /// different item (rebuilds immediately in that case). contentWidth is the target width every
    /// row is built against -- see this class's own doc comment for why it can't just be read
    /// from this window's own ContentSize the way a Fixed-mode host safely could. comparedAgainst
    /// is empty for plain single-item viewing; Item Details Comparison passes every *other*
    /// compared item's own definition here (see ResolveLineColor) so this item's own lines color
    /// symmetrically against them. entityId/stackInstanceId are this exact stack's own identity --
    /// not used for rendering at all, only so the Compare title button (if this instance has one)
    /// can invoke OnCompareRequested with the item currently shown, not a stale one captured at
    /// construction time.
    /// </summary>
    public void Configure(int entityId, uint stackInstanceId, ItemDefinition definition, float contentWidth, IReadOnlyList<ItemDefinition> comparedAgainst = null!)
    {
        _currentEntityId = entityId;
        _currentStackInstanceId = stackInstanceId;
        _definition = definition;
        _contentWidth = contentWidth;
        _comparedAgainst = comparedAgainst ?? [];

        if (_childrenReady)
        {
            Rebuild();
        }
    }

    /// <summary>
    /// The Compare title button is opt-in per instance, not universal -- attached only when
    /// OnCompareRequested is already set by the time this runs (the anchor pane's own controller
    /// sets it before calling Initialize; Item Details Comparison's own columns never do), rather
    /// than always attaching it and leaving it a dead click on columns. Deliberately added here,
    /// not in OnChildrenInitialized -- Window.Initialize's own CloseBehavior/MinimizeRestoreBehavior
    /// attachment (see its own doc comment) doesn't run until *after* base.Initialize() (which is
    /// what actually fires OnChildrenInitialized, from deep inside Element.Initialize) returns, so
    /// attaching Compare there ran before Close ever existed -- appending (the default insertIndex)
    /// only inserts to the left of whatever's *already* attached, so Compare ended up added first
    /// and read as the rightmost button, past Close's own right edge. Calling AddTitleButton here,
    /// after base.Initialize() has fully returned (Close/Minimize included), keeps every window's
    /// title buttons in the same left-to-right order: unique -> minimize/restore -> close.
    /// </summary>
    public override void Initialize()
    {
        base.Initialize();

        if (OnCompareRequested is not null)
        {
            var compareButton = BuildTitleButton(this, "↔");
            compareButton.Clicked += _ =>
            {
                if (_currentStackInstanceId != NoStackInstanceId)
                {
                    OnCompareRequested?.Invoke(_currentEntityId, _currentStackInstanceId);
                }
            };
            AddTitleButton(compareButton);
        }
    }

    /// <summary>Rebuilds when the reason the player can't use the shown item appears, changes or clears -- arms destroyed, mana spent -- while the window stays open.</summary>
    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (_childrenReady && _definition is not null && ComputeBlocker() != _displayedBlocker)
        {
            Rebuild();
        }
    }

    /// <summary>Why the player can't use the shown stack right now -- only for the player's own activatable stack; a shop's or a corpse's item isn't the player's to use yet.</summary>
    private ActivationBlocker ComputeBlocker() =>
        _definition?.Activator is not null && _currentStackInstanceId != NoStackInstanceId && _currentEntityId == playerQuery.PlayerEntityId
            ? actionStateView.GetItemBlocker(_currentEntityId, _currentStackInstanceId)
            : ActivationBlocker.None;

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

        _otherItemsStats.Clear();
        foreach (var other in _comparedAgainst)
        {
            _otherItemsStats.Add(ItemComparisonStatExtraction.Extract(other, actionCatalog, gameplayTags));
        }

        // _contentWidth is the target OUTER width this window should end up matching (see this
        // class's own doc comment) -- since this window is WrapContent (grows to fit its rows plus
        // ContentPadding on both edges, not the reverse), rows themselves must be sized to
        // _contentWidth minus that padding, or the window would end up ContentPadding*2 wider than
        // the caller actually asked for.
        var width = _contentWidth - ContentPadding.X * 2;

        _sections.BuildNameRow(this, _definition, width);

        if (ToggleText.IsLit(_definition))
        {
            _sections.BuildFixedTextLine(this, width, ToggleText.Active, GameWindowPalette.ToggleActiveText);
        }

        _displayedBlocker = ComputeBlocker();
        if (_displayedBlocker != ActivationBlocker.None)
        {
            _sections.BuildFixedTextLine(this, width, ActivationBlockerText.Describe(_displayedBlocker), ActivationBlockerText.ReasonColor);
        }

        _sections.BuildEffectsSection(this, _definition, width, ResolveLineColor);
        _sections.BuildCostSection(this, _definition, width);

        if (_definition.Activator is { } activator)
        {
            _sections.BuildActivationSection(this, activator, width, ResolveLineColor, ComputeShapeHighlight);
        }

        _sections.BuildDescriptionSection(this, _definition, width);

        _sections.BuildDivider(this, width);
        _sections.BuildIconTextRow(this, "Currency-Gold", "G", Color.Gold, $"{_definition.GoldValue}G", width);

        _sections.BuildTagsSection(this, _definition, width);
    }

    /// <summary>
    /// Null (plain grid, no highlight) unless every compared item -- anchor included -- shares
    /// this exact same Targeting.Shape ("do not compare different shapes for color-coding," even
    /// though eligibility only gates on matching Activator type, not Shape). When shapes do match,
    /// a tile highlights green if it's present in this item's own offsets but absent from at
    /// least one other compared item's own offsets -- the same "green if not every other compared
    /// item has it" rule ResolveLineColor already applies per stat line, just at tile granularity
    /// instead. No red case exists here -- a tile absent from this item's own grid was never drawn
    /// in the first place, so there is nothing to color.
    /// </summary>
    private HashSet<Point>? ComputeShapeHighlight(TargetingSpec targeting, IReadOnlyList<Point> offsets)
    {
        if (_comparedAgainst.Count == 0)
        {
            return null;
        }

        var otherOffsetSets = new List<HashSet<Point>>(_comparedAgainst.Count);
        foreach (var other in _comparedAgainst)
        {
            if (other.Activator is not { } otherActivator || otherActivator.Targeting.Shape != targeting.Shape)
            {
                return null;
            }

            otherOffsetSets.Add(new HashSet<Point>(TargetShapePreviewGeometry.ComputeOffsets(otherActivator.Targeting)));
        }

        var highlighted = new HashSet<Point>();
        foreach (var offset in offsets)
        {
            foreach (var otherOffsets in otherOffsetSets)
            {
                if (!otherOffsets.Contains(offset))
                {
                    highlighted.Add(offset);
                    break;
                }
            }
        }

        return highlighted;
    }

    /// <summary>
    /// Normal (no comparison active, or this exact Key isn't shared/rankable). Better/Green when
    /// at least one other compared item lacks a line with this same Key at all -- an advantage/
    /// exclusive marker, the whole line colored rather than just a "name" token (this codebase's
    /// TextWindow has one TextColor for its entire string, no per-substring styling -- see the
    /// new "Rich inline text formatting" TODO item logged alongside this feature). Otherwise, if
    /// every other compared item *does* have this Key and every one of them (plus this line
    /// itself) has a ComparableValue, magnitude-colored via ItemComparisonHighlighting -- ties
    /// (or a non-numeric Key shared by all, e.g. Shape, or two Scrolls casting different named
    /// spells) stay Normal.
    /// </summary>
    private Color ResolveLineColor(ItemComparisonStat stat)
    {
        if (_comparedAgainst.Count == 0)
        {
            return BodyTextColor;
        }

        var otherValues = new List<double>();
        foreach (var otherStats in _otherItemsStats)
        {
            var found = false;
            foreach (var otherStat in otherStats)
            {
                if (otherStat.Key != stat.Key)
                {
                    continue;
                }

                found = true;
                if (otherStat.ComparableValue is { } otherValue)
                {
                    otherValues.Add(otherValue);
                }

                break;
            }

            if (!found)
            {
                return BetterColor;
            }
        }

        if (stat.ComparableValue is not { } ownValue)
        {
            return BodyTextColor;
        }

        otherValues.Add(ownValue);
        var highlight = ItemComparisonHighlighting.ComputeHighlight(ownValue, otherValues, stat.HigherIsBetter);
        return highlight switch
        {
            ComparisonHighlight.Better => BetterColor,
            ComparisonHighlight.Worse => WorseColor,
            _ => BodyTextColor,
        };
    }
}
