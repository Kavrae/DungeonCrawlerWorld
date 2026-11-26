using Engine.Utilities;
using Game.Modules.Actions;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.UI.ColorPalettes;
using Presentation.UI.Menus;
using UIEngine.UI;

namespace Presentation.UI.Details;

/// <summary>Who Action Details shows an action for: the player, whose state (its blocker, its cooldown, a toggle being on) and values as used it reads.</summary>
public sealed record ActionDetailsUser(ActionStateView ActionStateView, IPlayerQuery PlayerQuery);

/// <summary>An action's details, or a menu command's: the same sections Item Details shows for an item, built by ActivatableDetailsSections, with no pricing.</summary>
/// <remarks>
/// <para>
/// With a user (the game), it shows the action as that player would use it, as Item Details shows an item: the Active line
/// for a toggle that is on, why it can't be used now, how long its cooldown has left, every value as used
/// (ActivationPreviews), and the Simple/Advanced mode button, sharing Item Details' setting. Without one (the Workshop's
/// Actions tab), it shows the definition alone, always Advanced, with catalog values.
/// </para>
/// <para>
/// A menu command shows its icon and name, what it does, and the slots it is bound to. Like Item Details, the host is
/// WrapContent and tiles its children vertically, and every row is built against the width Configure is given.
/// </para>
/// </remarks>
public sealed class ActionDetailsWindow(
    ElementServices services,
    EffectTextNames names,
    ActivationPreviews? activationPreviews = null,
    ActionDetailsUser? user = null)
    : Window(services), IDetailsPopupWindow
{
    private readonly ActivatableDetailsSections _sections = new(services.ElementPoolService, services.FontService, names);
    private readonly ComparisonLineStyles _comparisonStyles = new(names);

    private ActionDefinition? _definition;
    private IReadOnlyList<ActionDefinition> _comparedAgainst = [];
    private MenuCommand? _command;
    private string _boundSlotsText = string.Empty;
    private float _contentWidth;
    private bool _childrenReady;

    private ActivationBlocker _displayedBlocker;
    private int _displayedCooldownSeconds;
    private bool _displayedToggledOn;
    private int? _displayedPreviewStamp;
    private DetailsMode _displayedMode;
    private bool _displayedComparing;

    /// <summary>Settable late-bound: the mode the player chose; unset, the window is Advanced, as the Workshop's details are.</summary>
    public Func<DetailsMode>? ChosenMode { get; set; }

    /// <summary>Settable late-bound: the mode button's action -- no button when unset.</summary>
    public Action? OnModeToggleRequested { get; set; }

    /// <summary>Settable late-bound: the "Compare" button's action, given the shown action's id -- no button when unset.</summary>
    public Action<Guid>? OnCompareRequested { get; set; }

    /// <summary>Settable late-bound: the "Stop Comparison" button's action, shown while IsComparing says a comparison is in progress.</summary>
    public Action? OnStopComparisonRequested { get; set; }

    /// <summary>Settable late-bound: whether this window is the pinned anchor of a comparison.</summary>
    public Func<bool>? IsComparing { get; set; }

    /// <summary>Settable late-bound: true for the comparison popup, always simple, with no Compare or mode button.</summary>
    public bool AlwaysSimple { get; set; }

    /// <summary>Settable late-bound: called at the end of every Update -- its controller's placement and close-on-leave check.</summary>
    public Action? OnUpdated { get; set; }

    /// <summary>The action shown, or null for a menu command.</summary>
    public ActionDefinition? Definition => _definition;

    /// <summary>The menu command shown, or null for an action.</summary>
    public MenuCommand? Command => _command;

    /// <summary>The mode the window shows: Advanced with no user, Simple while comparing (as Item Details), otherwise the chosen one.</summary>
    public DetailsMode ShownMode =>
        user is null ? DetailsMode.Advanced
        : AlwaysSimple || IsComparingNow() ? DetailsMode.Simple
        : ChosenMode?.Invoke() ?? DetailsMode.Advanced;

    /// <summary>Shows definition with every row built to contentWidth, its lines coloured against comparedAgainst -- safe before the first Initialize (the build waits for it) and after (rebuilds at once).</summary>
    public void Configure(ActionDefinition definition, float contentWidth, IReadOnlyList<ActionDefinition>? comparedAgainst = null)
    {
        _definition = definition;
        _comparedAgainst = comparedAgainst ?? [];
        _command = null;
        _contentWidth = contentWidth;
        RebuildIfReady();
    }

    /// <summary>Shows command, with boundSlotsText naming the slots it is bound to, every row built to contentWidth.</summary>
    public void ConfigureCommand(MenuCommand command, string boundSlotsText, float contentWidth)
    {
        _command = command;
        _boundSlotsText = boundSlotsText;
        _definition = null;
        _contentWidth = contentWidth;
        RebuildIfReady();
    }

    /// <summary>Rebuilds when what the user can do with the action changes while the window stays open -- its blocker, its cooldown, a toggle switching, the player's modifiers changing its values -- or the mode is switched.</summary>
    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (_childrenReady && _definition is not null &&
            (ComputeBlocker() != _displayedBlocker ||
             ComputeCooldownSeconds() != _displayedCooldownSeconds ||
             ComputeToggledOn() != _displayedToggledOn ||
             ComputePreviewStamp() != _displayedPreviewStamp ||
             IsComparingNow() != _displayedComparing ||
             ShownMode != _displayedMode))
        {
            Rebuild();
        }

        OnUpdated?.Invoke();
    }

    protected override void OnChildrenInitialized()
    {
        base.OnChildrenInitialized();
        _childrenReady = true;

        Rebuild();
    }

    private void RebuildIfReady()
    {
        if (_childrenReady)
        {
            Rebuild();
        }
    }

    /// <summary>The player the window shows the action for, or null with no user or no player yet.</summary>
    private int? UserEntityId => user is { } shownFor && shownFor.PlayerQuery.PlayerEntityId >= 0 ? shownFor.PlayerQuery.PlayerEntityId : null;

    private ActivationBlocker ComputeBlocker() =>
        UserEntityId is { } entityId && _definition is { } definition ? user!.ActionStateView.GetActionBlocker(entityId, definition.Id) : ActivationBlocker.None;

    private int ComputeCooldownSeconds() =>
        UserEntityId is { } entityId && _definition is { } definition
            ? (int)System.Math.Ceiling(user!.ActionStateView.ActionCooldownFramesRemaining(entityId, definition.Id) / (float)GameTiming.FramesPerSecond)
            : 0;

    private bool ComputeToggledOn() =>
        UserEntityId is { } entityId && _definition is { Toggle: not null } definition && user!.ActionStateView.IsActionToggledOn(entityId, definition.Id);

    private int? ComputePreviewStamp() => UserEntityId is { } entityId && activationPreviews is not null ? activationPreviews.ChangeStampOf(entityId) : null;

    private void Rebuild()
    {
        ElementPoolService.CloseAllChildren(this);

        var width = _contentWidth - ContentPadding.X * 2;
        if (_command is { } command)
        {
            BuildCommand(command, width);
            return;
        }

        if (_definition is not { } definition)
        {
            return;
        }

        _displayedMode = ShownMode;
        _displayedPreviewStamp = ComputePreviewStamp();
        var preview = UserEntityId is { } entityId && activationPreviews is not null ? activationPreviews.Preview(entityId, definition) : null;

        _comparisonStyles.Clear();
        foreach (var other in _comparedAgainst)
        {
            _comparisonStyles.Add(other, UserEntityId is { } otherUserId && activationPreviews is not null ? activationPreviews.Preview(otherUserId, other) : null);
        }

        _sections.BuildNameRowWithButtons(this, definition.SpriteName, definition.Glyph, definition.GlyphColor, definition.Name, width, ComputeButtons(definition));

        _displayedToggledOn = ComputeToggledOn();
        if (_displayedToggledOn)
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

        _sections.BuildEffectsSection(this, definition, _displayedMode, width, _comparisonStyles.Resolve, preview, activationPreviews is null ? null : activationPreviews.PreviewAura);
        _sections.BuildCostSection(this, definition, width, _comparisonStyles.Resolve, preview);
        _sections.BuildActivationSection(this, definition.Activator, _displayedMode, width, _comparisonStyles.Resolve, _comparisonStyles.ShapeHighlight, preview, isToggle: definition.Toggle is not null);
        _sections.BuildResourcesSection(this, definition.Activator, width, _comparisonStyles.Resolve);
        _sections.BuildDescriptionSection(this, definition, width);
        _sections.BuildTagsSection(this, definition, width);
    }

    /// <summary>A menu command: its icon and name, what it does, and the slots it is bound to.</summary>
    private void BuildCommand(MenuCommand command, float width)
    {
        _sections.BuildNameRow(this, command.SpriteName, command.Glyph, command.GlyphColor, command.Name, width);
        _sections.BuildWrappingTextLine(this, width, command.Description);
        _sections.BuildFixedTextLine(this, width, _boundSlotsText, ActivatableDetailsSections.BodyTextColor);
    }

    private bool IsComparingNow() => IsComparing?.Invoke() == true;

    /// <summary>The name row's buttons, by Item Details' rule: "Stop Comparison" while it is the comparison's anchor; otherwise, for the player's own action, "Compare" and the mode button -- neither on the comparison popup.</summary>
    private List<(string Label, Action OnClicked)> ComputeButtons(ActionDefinition definition)
    {
        _displayedComparing = IsComparingNow();
        var buttons = new List<(string Label, Action OnClicked)>(2);
        if (_displayedComparing && OnStopComparisonRequested is { } stopComparison)
        {
            buttons.Add(("Stop Comparison", stopComparison));
        }

        if (user is null || _displayedComparing || AlwaysSimple)
        {
            return buttons;
        }

        if (OnCompareRequested is { } compare)
        {
            var actionId = definition.Id;
            buttons.Add(("Compare", () => compare(actionId)));
        }

        if (OnModeToggleRequested is { } toggleMode)
        {
            buttons.Add((Inventory.ItemDetailsWindow.ModeButtonLabel(_displayedMode), toggleMode));
        }

        return buttons;
    }
}
