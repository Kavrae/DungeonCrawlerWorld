namespace Game.Modules.Auras;

/// <summary>The Guids of the auras in the master manifest's Auras table -- the ones built-in actions and items grant.</summary>
/// <remarks>Auras that terrain and blueprints radiate stay with them in C# (BuiltInTerrain.LavaAura, HealingShrine.Aura) until those are content too.</remarks>
public static class BuiltInAuraIds
{
    /// <summary>The Toxic Idol's Poison aura.</summary>
    public static readonly Guid ToxicIdolPoison = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000302");

    /// <summary>The Scroll of Torch's Light: a glow with no effects.</summary>
    public static readonly Guid TorchLight = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000303");

    /// <summary>The Toxic Aura action's own Poison aura -- its own, so it doesn't add to the Idol's.</summary>
    public static readonly Guid ToxicAura = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000306");

    /// <summary>The Lantern action's light.</summary>
    public static readonly Guid Lantern = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000309");
}
