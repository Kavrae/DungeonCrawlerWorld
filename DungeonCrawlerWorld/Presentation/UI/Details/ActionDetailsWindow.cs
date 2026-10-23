using Engine.Tags;
using Game.Modules.Actions;
using Microsoft.Xna.Framework;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Presentation.UI.Details;

/// <summary>An action's details from its definition alone: the same sections Item Details shows for an item, built by ActivatableDetailsSections.</summary>
/// <remarks>
/// Name row, effects ("While on" for a toggle), cost, activation with the target shape preview, description and tags --
/// Item Details' order, without what only an item has (gold value) or what needs an entity (the blocker, comparison).
/// Like Item Details, the host is WrapContent and tiles its children vertically, and every row is built against the
/// width Configure is given.
/// </remarks>
public sealed class ActionDetailsWindow(
    FontService fontService,
    ElementPoolService elementPoolService,
    UiRenderService renderService,
    ActionCatalog actionCatalog,
    GameplayTagRegistry gameplayTags)
    : Window(fontService, elementPoolService, renderService)
{
    private readonly ActivatableDetailsSections _sections = new(elementPoolService, fontService, actionCatalog, gameplayTags);

    private ActionDefinition? _definition;
    private float _contentWidth;
    private bool _childrenReady;

    /// <summary>Shows definition with every row built to contentWidth -- safe before the first Initialize (the build waits for it) and after (rebuilds at once).</summary>
    public void Configure(ActionDefinition definition, float contentWidth)
    {
        _definition = definition;
        _contentWidth = contentWidth;

        if (_childrenReady)
        {
            Rebuild();
        }
    }

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

        var width = _contentWidth - ContentPadding.X * 2;

        _sections.BuildNameRow(this, _definition, width);
        _sections.BuildEffectsSection(this, _definition, width, PlainLineColor);
        _sections.BuildCostSection(this, _definition, width);
        _sections.BuildActivationSection(this, _definition.Activator, width, PlainLineColor);
        _sections.BuildDescriptionSection(this, _definition, width);
        _sections.BuildTagsSection(this, _definition, width);
    }

    private static Color PlainLineColor(ItemComparisonStat stat) => ActivatableDetailsSections.BodyTextColor;
}
