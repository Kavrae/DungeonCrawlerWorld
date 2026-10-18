using Engine.Tags;
using Engine.Tiles;
using Game.Modules.Actions;
using Game.Modules.Auras;
using Microsoft.Xna.Framework;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Presentation.UI.Details;

/// <summary>An aura's details from its definition alone: its glow, how its strength spreads, the effects each tick applies, and its tags.</summary>
/// <remarks>
/// Built with ActivatableDetailsSections, so it reads like Item Details and Action Details: the name row (a swatch of
/// the glow colour in place of a sprite -- an aura has none), "Strength" (AuraMagnitude and AuraFalloff), "Each tick"
/// (the effects, or "None" for a glow-only aura), then tags. The host is WrapContent and tiles its children vertically;
/// every row is built against the width Configure is given.
/// </remarks>
public sealed class AuraDetailsWindow(
    FontService fontService,
    ElementPoolService elementPoolService,
    LabelRenderer labelRenderer,
    ActionCatalog actionCatalog,
    GameplayTagRegistry gameplayTags)
    : Window(fontService, elementPoolService, labelRenderer)
{
    private const string GlowSwatchGlyph = "●";

    private readonly ActivatableDetailsSections _sections = new(elementPoolService, fontService, actionCatalog, gameplayTags);

    private AuraDefinition? _definition;
    private float _contentWidth;
    private bool _childrenReady;

    /// <summary>Shows definition with every row built to contentWidth -- safe before the first Initialize (the build waits for it) and after (rebuilds at once).</summary>
    public void Configure(AuraDefinition definition, float contentWidth)
    {
        _definition = definition;
        _contentWidth = contentWidth;

        if (_childrenReady)
        {
            Rebuild();
        }
    }

    /// <summary>How an aura's effects scale with its strength where an entity stands.</summary>
    public static string MagnitudeText(AuraMagnitude magnitude) => magnitude switch
    {
        AuraMagnitude.Power => "Effects scale with its strength",
        AuraMagnitude.Flat => "Effects at full amount wherever it reaches",
        _ => magnitude.ToString(),
    };

    /// <summary>How an aura's strength changes from its source to the edge of its reach.</summary>
    public static string FalloffText(AuraFalloff falloff) => falloff switch
    {
        AuraFalloff.Linear => "Fades with distance from its source",
        AuraFalloff.None => "Full strength to the edge of its reach",
        _ => falloff.ToString(),
    };

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
        var bodyColor = ActivatableDetailsSections.BodyTextColor;

        _sections.BuildNameRow(this, null, GlowSwatchGlyph, _definition.GlowColor, _definition.Name, width);

        _sections.BuildDivider(this, width, "Strength", 0.125f);
        _sections.BuildFixedTextLine(this, width, MagnitudeText(_definition.Magnitude), bodyColor);
        _sections.BuildFixedTextLine(this, width, FalloffText(_definition.Falloff), bodyColor);

        _sections.BuildEffectsSection(this, _definition.Effects, "Each tick", width, PlainLineColor);
        _sections.BuildTagsSection(this, _definition.Tags, width);
    }

    private static Color PlainLineColor(ItemComparisonStat stat) => ActivatableDetailsSections.BodyTextColor;
}
