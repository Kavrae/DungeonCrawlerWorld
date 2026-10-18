namespace Game.Modules.Actions;

/// <summary>The Guids of the built-in actions in the master manifest -- how code names one, looking it up in the session's ActionCatalog.</summary>
/// <remarks>The definitions themselves are content (Content/Definitions/Master/Actions.jsonl); a Guid here never changes, or every reference to it breaks.</remarks>
public static class BuiltInActionIds
{
    public static readonly Guid Heal = new("3f6e9c2a-8b4d-47a1-9c3e-5d2f7b1a6c9e");
    public static readonly Guid QuickAttack = new("7a1c3e5f-9b2d-4c6a-8e1f-3d5b7a9c2e4f");
    public static readonly Guid PowerAttack = new("2f4e6a8c-1d3b-4f5e-9a7c-6b8d0e2f4a6c");
    public static readonly Guid Dodge = new("9d1f3b5a-7c9e-4a1d-8f3b-5a7c9e1d3f5a");
    public static readonly Guid ToxicAura = new("5c2e8a47-1d9b-4f36-a8c1-7e3b9d5f2a64");
    public static readonly Guid Lantern = new("8e4b2c71-5d3a-4f96-b1e8-2a7c9d4f6b13");
    public static readonly Guid MagicMissile = new("2b6d4f8a-1c3e-4a5d-9f7b-6e8c1a3d5f9b");
    public static readonly Guid ToxicStrike = new("b8729e94-aee0-42d4-bda2-9b323afd3134");
    public static readonly Guid Fireball = new("6a1f3c8e-2b7d-4e95-a0c4-9d8e7f1b3a52");

    /// <summary>The spell the Scroll of Torch casts: a synthesised spell with no action of its own, so the catalog has nothing under it.</summary>
    public static readonly Guid TorchSpell = new("7c3e9a1d-4b6f-4e2a-8d1c-000000000022");
}
