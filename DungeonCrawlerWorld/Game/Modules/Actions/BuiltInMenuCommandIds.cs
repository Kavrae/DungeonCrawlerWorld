namespace Game.Modules.Actions;

/// <summary>The Guids of the built-in menu commands -- how a hotkey slot names one (MenuCommandHotkeyBindingComponent).</summary>
/// <remarks>What each does is Presentation's (MenuCommandCatalog); Game holds them only so the player's kit can bind them. A Guid here never changes, or every slot bound to it breaks.</remarks>
public static class BuiltInMenuCommandIds
{
    public static readonly Guid Health = new("0b5c3e71-4a2d-4f8e-9c16-7d3a5e9b2f40");
    public static readonly Guid Actions = new("6e2a9d14-8f3c-4b57-a1e9-3c7d5b2f8a61");
    public static readonly Guid Inventory = new("a4f17c38-2e9b-4d65-8b3a-1f6e9c4d7a25");
    public static readonly Guid AbilityScores = new("d39e5b82-7c14-4a3f-b6d8-9e2a1c5f4b77");
    public static readonly Guid SwitchTargetingMode = new("57c8a2e6-3b1f-4e9d-8a4c-2d6f9b3e1c58");
}
