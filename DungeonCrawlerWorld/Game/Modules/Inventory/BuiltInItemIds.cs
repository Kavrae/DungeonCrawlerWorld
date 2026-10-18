namespace Game.Modules.Inventory;

/// <summary>The Guids of the built-in items in the master manifest -- how code names one, looking it up in the session's ItemCatalog.</summary>
/// <remarks>The definitions themselves are content (Content/Definitions/Master/Items.jsonl); a Guid here never changes, or every reference to it breaks.</remarks>
public static class BuiltInItemIds
{
    public static readonly Guid HealthPotion = new("7c3e9a1d-4b6f-4e2a-8d1c-000000000001");
    public static readonly Guid CurePoisonPotion = new("7c3e9a1d-4b6f-4e2a-8d1c-000000000032");
    public static readonly Guid ManaPotion = new("a4f2e8c1-6b3d-4e97-9a1f-2d8c5b7e4a63");
    public static readonly Guid HotkeyExpansionPotion = new("a4f2e8c1-6b3d-4e97-9a1f-2d8c5b7e4a64");
    public static readonly Guid DamagePotion = new("136d0466-3749-40c4-aea4-5f02254040b3");
    public static readonly Guid ToxicPotion = new("e72db9f1-9947-4b85-9cb4-0dcf38a8a53b");
    public static readonly Guid ToxicIdol = new("f3a8c1d6-2b4e-4a9f-8c6d-1e7b3a5f9c2d");
    public static readonly Guid ScrollOfHealing = new("7c3e9a1d-4b6f-4e2a-8d1c-000000000020");
    public static readonly Guid ScrollOfTorch = new("7c3e9a1d-4b6f-4e2a-8d1c-000000000021");
    public static readonly Guid WandOfFireball = new("7c3e9a1d-4b6f-4e2a-8d1c-000000000031");
    public static readonly Guid ImmunityTestPotion = new("f1a4c7e2-3b9d-4e6f-8a2c-000000000030");
    public static readonly Guid ResistanceTestPotion = new("f1a4c7e2-3b9d-4e6f-8a2c-000000000031");
    public static readonly Guid DraughtOfPlenty = new("f1a4c7e2-3b9d-4e6f-8a2c-000000000041");
    public static readonly Guid DraughtOfThrift = new("f1a4c7e2-3b9d-4e6f-8a2c-000000000042");
    public static readonly Guid DraughtOfVenom = new("f1a4c7e2-3b9d-4e6f-8a2c-000000000043");
    public static readonly Guid DraughtOfRadiance = new("f1a4c7e2-3b9d-4e6f-8a2c-000000000044");
}
