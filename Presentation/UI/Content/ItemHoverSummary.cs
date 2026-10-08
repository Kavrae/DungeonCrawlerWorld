using Engine.Tags;
using Game.Modules.Actions.Activators;
using Game.Modules.Inventory;

namespace Presentation.UI.Content;

/// <summary>The body text of an item's hover tooltip: whether it is on, its summary, its target shape if it's activated, a wand's charges, and what using it takes (CostText).</summary>
/// <cleanupVersion>1</cleanupVersion>
public static class ItemHoverSummary
{
    /// <param name="showCharges">False where the tooltip stands for several stacks that could hold different charges (a merged cell), so no single number would be true.</param>
    /// <param name="gameplayTags">Names a tag an effect line mentions; null falls back to the tag's own name.</param>
    public static string For(ItemDefinition definition, bool showCharges, GameplayTagRegistry? gameplayTags = null)
    {
        var summary = definition.Summary;
        if (ToggleText.IsLit(definition))
        {
            summary = $"{ToggleText.Active}\n{summary}";
        }

        if (definition.Activator is not { } activator)
        {
            return summary;
        }

        summary = $"{summary}\nTarget: {activator.Targeting.Shape}";

        if (showCharges && activator is WandActivator wandActivator)
        {
            summary += $"\nCharges: {wandActivator.Charges}/{wandActivator.MaxCharges}";
        }

        foreach (var line in CostText.Lines(definition, gameplayTags))
        {
            summary += $"\n{line}";
        }

        return summary;
    }
}
