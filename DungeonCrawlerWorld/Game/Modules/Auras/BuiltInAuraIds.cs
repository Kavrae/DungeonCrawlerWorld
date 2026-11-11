namespace Game.Modules.Auras;

/// <summary>The Guids of the auras in the master manifest's Auras table: the ones built-in actions, items, terrain and blueprints radiate.</summary>
public static class BuiltInAuraIds
{
    /// <summary>Lava's own aura: each tick tops one random body part up to the aura's power in Burning stacks.</summary>
    public static readonly Guid Lava = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000301");

    /// <summary>The Toxic Idol's Poison aura.</summary>
    public static readonly Guid ToxicIdolPoison = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000302");

    /// <summary>The Scroll of Torch's Light: a glow with no effects.</summary>
    public static readonly Guid TorchLight = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000303");

    /// <summary>The Healing Shrine's aura: each tick heals a point of health per point of power, to the part its healing priority picks.</summary>
    public static readonly Guid HealingShrine = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000304");

    /// <summary>Holy Ground's own aura: each tick heals a point of health per point of power, to the part its healing priority picks.</summary>
    public static readonly Guid HolyGround = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000305");

    /// <summary>The Toxic Aura action's own Poison aura -- its own, so it doesn't add to the Idol's.</summary>
    public static readonly Guid ToxicAura = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000306");

    /// <summary>The Radiant trait's glow-only aura at full power to its edge.</summary>
    public static readonly Guid RadiantSteady = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000307");

    /// <summary>The Radiant trait's glow-only aura fading to its edge.</summary>
    public static readonly Guid RadiantFading = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000308");

    /// <summary>The Lantern action's light.</summary>
    public static readonly Guid Lantern = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000309");

    public static readonly Guid AntiMagic = new("d9f6a1c4-8b2e-4f3a-9c1d-00000000030a");

    public static readonly Guid AntiLiteracy = new("d9f6a1c4-8b2e-4f3a-9c1d-00000000030b");
}
