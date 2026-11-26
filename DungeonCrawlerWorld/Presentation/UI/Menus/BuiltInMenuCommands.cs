using Game.Modules.Actions;
using Presentation.UI.AbilityScores;
using Presentation.UI.Actions;
using Presentation.UI.ColorPalettes;
using Presentation.UI.Inventory;

namespace Presentation.UI.Menus;

/// <summary>The game's own menu commands, each opening or closing a window or switching a mode, by its Guid in BuiltInMenuCommandIds.</summary>
/// <remarks>The menu sidebar's slots are bound to these by default (PlayerKit), and a slot runs one exactly as a click on it does.</remarks>
public static class BuiltInMenuCommands
{
    /// <summary>♥ (U+2665): drawn by the default font with no fallback needed.</summary>
    private const string HeartGlyph = "♥";

    /// <summary>◎ (U+25CE): the targeting-mode switch has no sprite of its own.</summary>
    private const string TargetingModeGlyph = "◎";

    public static void Register(
        MenuCommandCatalog catalog,
        HealthWindowController health,
        ActionsWindowController actions,
        InventoryWindowController inventory,
        AbilityScoreWindowController abilityScores,
        TargetingModeSwitch targetingModeSwitch)
    {
        catalog.Register(new MenuCommand(
            BuiltInMenuCommandIds.Health, "Health", "Opens your body parts' health, and the buffs and debuffs on you.",
            SpriteName: null, HeartGlyph, GameWindowPalette.HeartGlyphColor, "Open",
            health.ToggleHealthWindow, static () => true));

        catalog.Register(new MenuCommand(
            BuiltInMenuCommandIds.Actions, "Actions", "Opens everything you can do: your actions, your Core actions and these menu commands.",
            "Spell-Moderate", "A", GameWindowPalette.SlotGlyphColor, "Open",
            actions.ToggleActionsWindow, static () => true));

        catalog.Register(new MenuCommand(
            BuiltInMenuCommandIds.Inventory, "Inventory", "Opens the items you carry.",
            "Inventory", "I", GameWindowPalette.SlotGlyphColor, "Open",
            inventory.ToggleInventoryWindow, () => !inventory.IsInventoryDisabled()));

        catalog.Register(new MenuCommand(
            BuiltInMenuCommandIds.AbilityScores, "Ability Scores", "Opens your ability scores and what changes them.",
            "AbilityScore", "S", GameWindowPalette.SlotGlyphColor, "Open",
            abilityScores.ToggleAbilityScoreWindow, () => !abilityScores.IsInventoryDisabled()));

        catalog.Register(new MenuCommand(
            BuiltInMenuCommandIds.SwitchTargetingMode, "Switch targeting mode", "Switches between Target and Ground targeting (also Left Alt).",
            SpriteName: null, TargetingModeGlyph, GameWindowPalette.SlotGlyphColor, "Switch",
            targetingModeSwitch.Switch, static () => true));
    }
}
