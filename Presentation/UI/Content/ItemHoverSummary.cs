using Game.Modules.Actions.Activators;
using Game.Modules.Inventory;

namespace Presentation.UI.Content;

/// <summary>The body text of an item's hover tooltip: its summary, its target shape if it's activated, and a wand's charges.</summary>
/// <cleanupVersion>1</cleanupVersion>
public static class ItemHoverSummary
{
    /// <param name="showCharges">False where the tooltip stands for several stacks that could hold different charges (a merged cell), so no single number would be true.</param>
    public static string For(ItemDefinition definition, bool showCharges)
    {
        var summary = definition.Summary;
        if (definition.Activator is not { } activator)
        {
            return summary;
        }

        summary = $"{summary}\nTarget: {activator.Targeting.Shape}";

        if (showCharges && activator is WandActivator wandActivator)
        {
            summary += $"\nCharges: {wandActivator.Charges}/{wandActivator.MaxCharges}";
        }

        return summary;
    }
}
