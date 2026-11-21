using Engine.Tiles;
using Game.Modules.Actions;
using Game.Modules.Auras;
using Microsoft.Xna.Framework;
using UIEngine.Fonts;
using UIEngine.UI;

namespace Presentation.UI.Details;

/// <summary>An aura's details from its definition alone: its glow, how its strength spreads, the effects each tick applies, and its tags.</summary>
/// <remarks>
/// Built with ActivatableDetailsSections, so it reads like Item Details and Action Details: the name row (a swatch of
/// the glow colour in place of a sprite -- an aura has none), "Strength" (StrengthLines; left out for a glow-only aura),
/// "Each tick" (the effects, or "None" for a glow-only aura), then tags. The host is WrapContent and tiles its children vertically;
/// every row is built against the width Configure is given.
/// </remarks>
public sealed class AuraDetailsWindow(
    ElementServices services,
    ActionCatalog actionCatalog,
    EffectTextNames names)
    : Window(services)
{
    private const string GlowSwatchGlyph = "●";

    private readonly ActivatableDetailsSections _sections = new(services.ElementPoolService, services.FontService, actionCatalog, names);

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

    /// <summary>The "Strength" section's lines: how the aura's effects scale with its strength (a Power aura's only -- a Flat one's are at full amount wherever it reaches) and how that strength changes toward the edge of its reach; none for an aura with no effects, whose strength changes only its glow.</summary>
    public static IReadOnlyList<string> StrengthLines(AuraDefinition definition)
    {
        if (!definition.Effects.Any(static effect => effect.Entries.Count > 0))
        {
            return [];
        }

        return definition.Magnitude == AuraMagnitude.Power
            ? ["Effects scale with its strength", FalloffText(definition.Falloff)]
            : [FalloffText(definition.Falloff)];
    }

    /// <summary>How an aura's strength changes from its source to the edge of its reach.</summary>
    public static string FalloffText(AuraFalloff falloff) => falloff switch
    {
        AuraFalloff.Linear => "Strength fading to the edge of its reach",
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

        if (StrengthLines(_definition) is { Count: > 0 } strengthLines)
        {
            _sections.BuildDivider(this, width, "Strength", 0.125f);
            foreach (var line in strengthLines)
            {
                _sections.BuildFixedTextLine(this, width, line, bodyColor);
            }
        }

        _sections.BuildEffectsSection(this, _definition.Effects, "Each tick", DetailsMode.Advanced, width, PlainLineStyle);
        _sections.BuildTagsSection(this, _definition.Tags, width);
    }

    private static LineStyle PlainLineStyle(ItemComparisonStat stat) => LineStyle.Of(stat);
}
